using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.Web;

/// <summary>
/// WebView2 实现的 DOM 属性查询引擎，直接调用 WebView2 API 填充属性槽位。
/// 作为缺省委托，无需转接其他函数，完全独立实现。
/// </summary>
public sealed class WebView2DomPropertyFiller : IDomPropertyQueryEngine
{
	private readonly CoreWebView2 _webView2;
	private readonly Dictionary<string, JsonElement> _elementCache = new(StringComparer.Ordinal);

	public WebView2DomPropertyFiller(CoreWebView2 webView2)
	{
		ArgumentNullException.ThrowIfNull(webView2);
		_webView2 = webView2;
	}

	public static async ValueTask<WebView2DomPropertyFiller>
		CreateInstrumentedAsync(
			CoreWebView2 webView2,
			CancellationToken cancellationToken = default)
	{
		await WebView2EventInstrumentation.InstallAsync(
			webView2,
			cancellationToken);
		return new(webView2);
	}

	/// <summary>
	/// 清除元素缓存，在页面导航后调用。
	/// </summary>
	public void ClearCache() => _elementCache.Clear();

	public async ValueTask<DomPropertyQueryResult> QueryAsync(
		DomPropertyQueryContext context,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		try
		{
			if (context.OwnerKind == ElementSlotOwnerKind.Event)
				return await QueryEventAsync(context, cancellationToken);

			// 根据槽位类型执行不同的查询
			return context.Slot switch
			{
				DomPropertyDataSlot.Initialization => await QueryInitializationAsync(context, cancellationToken),
				DomPropertyDataSlot.Link => await QueryLinkAsync(context, cancellationToken),
				DomPropertyDataSlot.Runtime => await QueryRuntimeAsync(context, cancellationToken),
				_ => DomPropertyQueryResult.ConfirmedAbsent($"Unknown slot {context.Slot}")
			};
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (ArgumentException)
		{
			// 查询结果违反六槽位/强类型链接契约属于程序错误，必须失败即停，
			// 不能伪装成源端不支持。
			throw;
		}
		catch (InvalidDataException)
		{
			// WebView2 返回了本查询器无法解释的协议结果，同样属于实现错误。
			throw;
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException(
				$"The legacy WebView2 DOM property query failed for "
					+ $"{context.DocumentScope}::{context.XPath}/"
					+ $"{context.PropertyName}/{context.Slot}. Query failures "
					+ "must not be downgraded to SourceUnsupported.",
				exception);
		}
	}

	/// <summary>
	/// 查询 Initialization 槽位：HTML attribute 声明值
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryInitializationAsync(
		DomPropertyQueryContext context,
		CancellationToken ct)
	{
		var xpath = context.XPath;
		var propName = context.PropertyName;

		// 全局服务属性不来自 DOM
		if (propName is "service.globalLayout" or "service.globalStyle")
			return DomPropertyQueryResult.ConfirmedAbsent("Service property");

		if (propName.StartsWith("content.", StringComparison.Ordinal))
			return await QueryContentAsync(
				context.DocumentScope,
				xpath,
				propName["content.".Length..],
				ct);

		// 构造 JS 查询 attribute 值
		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(context.DocumentScope, xpath)}}
				if (!node || node.nodeType !== 1) return null;
				const name = '{{EscapeJs(propName)}}';
				if (node.hasAttribute(name)) {
					return node.getAttribute(name);
				}
				// 布尔属性：disabled, hidden, checked, readonly, required, etc.
				if (typeof node[name] === 'boolean' && node[name] === true) {
					return 'true';
				}
				return null;
			})()
			""";

		var result = await ExecuteScriptAsync(js, ct);
		if (result is null)
			return DomPropertyQueryResult.ConfirmedAbsent($"Attribute {propName} not found");

		return DomPropertyQueryResult.DirectConstant(result);
	}

	/// <summary>
	/// 查询 Link 槽位：语义链接（布局绑定、CSS 表达式等）
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryLinkAsync(
		DomPropertyQueryContext context,
		CancellationToken ct)
	{
		var propName = context.PropertyName;

		// 布局属性需要返回 ContainerLayoutBinding
		if (propName is "style.width" or "style.height" or "style.minWidth" or "style.minHeight"
			or "style.maxWidth" or "style.maxHeight" or "style.left" or "style.top"
			or "style.right" or "style.bottom" or "style.marginLeft" or "style.marginTop"
			or "style.marginRight" or "style.marginBottom" or "style.paddingLeft"
			or "style.paddingTop" or "style.paddingRight" or "style.paddingBottom"
			or "style.flexBasis" or "style.gap" or "style.rowGap" or "style.columnGap")
		{
			return await QueryLayoutLinkAsync(context, ct);
		}

		// CSS 变量/表达式
		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(
					context.DocumentScope,
					context.XPath)}}
				if (!node) return null;
				const style = getComputedStyle(node);
				const name = '{{EscapeJs(propName.Replace("style.", ""))}}';
				const value = style[name];
				if (value && (value.includes('var(') || value.includes('calc(') || value.includes('min(') || value.includes('max('))) {
					return value;
				}
				return null;
			})()
			""";

		var result = await ExecuteScriptAsync(js, ct);
		if (result is not null)
		{
			return DomPropertyQueryResult.Captured(
				result,
				ElementPropertyValueSource.LinkedCalculation,
				new ElementPropertyLink(ElementPropertyLinkKind.CssExpression, result));
		}

		return DomPropertyQueryResult.ConfirmedAbsent("No link");
	}

	/// <summary>
	/// 查询布局链接，生成强类型 ContainerLayoutBinding
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryLayoutLinkAsync(
		DomPropertyQueryContext context,
		CancellationToken ct)
	{
		var cssProp = context.PropertyName.Replace("style.", "");
		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(
					context.DocumentScope,
					context.XPath)}}
				if (!node) return null;
				const style = getComputedStyle(node);
				const value = style.{{cssProp}};
				if (!value || value === 'auto' || value === 'none') return null;

				// 检测百分比
				if (value.endsWith('%')) {
					const num = parseFloat(value) / 100;
					// 确定参考基准
					let basis = 'parent';
					let refNode = node.parentElement;
					const position = style.position;
					if (position === 'absolute' || position === 'fixed') {
						let offsetParent = node.offsetParent;
						if (offsetParent) {
							basis = 'containingBlock';
							refNode = offsetParent;
						}
					}
					return { type: 'percentage', value: num, basis: basis };
				}
				// 检测 vw/vh
				if (value.endsWith('vw')) return { type: 'viewport', axis: 'x', value: parseFloat(value) };
				if (value.endsWith('vh')) return { type: 'viewport', axis: 'y', value: parseFloat(value) };
				// 元素尺寸可能由父 flex/grid 容器参与计算。元素自身的 display
				// 只决定其子项布局，不能作为自身 width/height 的容器链接。
				const parent = node.parentElement;
				if (parent) {
					const parentStyle = getComputedStyle(parent);
					const parentDisplay = parentStyle.display;
					if (parentDisplay === 'flex' || parentDisplay === 'inline-flex') {
						return {
							type: 'flex',
							direction: parentStyle.flexDirection
						};
					}
					if (parentDisplay === 'grid' || parentDisplay === 'inline-grid') {
						return { type: 'grid' };
					}
				}

				return null;
			})()
			""";

		var jsonResult = await ExecuteScriptRawAsync(js, ct);
		if (jsonResult is null)
			return DomPropertyQueryResult.ConfirmedAbsent("No layout link");

		try
		{
			using var doc = JsonDocument.Parse(jsonResult);
			var root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.Null)
				return DomPropertyQueryResult.ConfirmedAbsent("No layout link");

			var type = root.GetProperty("type").GetString();
			if (type == "percentage")
			{
				// 简化：返回 CssExpression 链接，完整 ContainerLayoutBinding 需要更多上下文
				var value = root.GetProperty("value").GetDouble();
				return DomPropertyQueryResult.Captured(
					$"{value * 100}%",
					ElementPropertyValueSource.LinkedCalculation,
					new ElementPropertyLink(ElementPropertyLinkKind.LayoutExpression,
						$"percentage:{value * 100}%"));
			}
			if (type == "viewport")
			{
				return DomPropertyQueryResult.Captured(
					root.GetProperty("value").GetRawText(),
					ElementPropertyValueSource.LinkedCalculation,
					new ElementPropertyLink(ElementPropertyLinkKind.LayoutExpression, "viewport"));
			}
			if (type == "flex" || type == "grid")
			{
				if (!TryGetSizeSlot(cssProp, out var targetSlot))
					return DomPropertyQueryResult.ConfirmedAbsent(
						$"{cssProp} is not a container-controlled size slot");

				var isFlex = type == "flex";
				var flexMainAxis = isFlex
					? ParseFlexMainAxis(root.GetProperty("direction").GetString())
					: (LayoutPhysicalAxis?)null;
				var binding = new ContainerLayoutBinding(
					isFlex
						? ContainerLayoutMechanism.Flex
						: ContainerLayoutMechanism.Grid,
					new LayoutContainerReference(
						isFlex
							? LayoutContainerReferenceKind.FlexContainer
							: LayoutContainerReferenceKind.GridContainer,
						LayoutReferenceBox.ContentBox),
					new ContainerSizeConstraint(
						targetSlot,
						new LayoutLength.Automatic()),
					$"parent {type} layout controls {context.PropertyName}",
					flexMainAxis);
				return DomPropertyQueryResult.Captured(
					type,
					ElementPropertyValueSource.ContainerAutomaticLayout,
					ElementPropertyLink.FromContainerLayout(binding));
			}
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException(
				"WebView2 layout-link query returned malformed JSON.",
				ex);
		}

		return DomPropertyQueryResult.ConfirmedAbsent("Could not parse layout link");
	}

	/// <summary>
	/// 查询 Runtime 槽位：浏览器运行时计算值
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryRuntimeAsync(
		DomPropertyQueryContext context,
		CancellationToken ct)
	{
		var xpath = context.XPath;
		var propName = context.PropertyName;

		if (propName.StartsWith("content.", StringComparison.Ordinal))
			return await QueryContentAsync(
				context.DocumentScope,
				xpath,
				propName["content.".Length..],
				ct);

		// 几何属性：getBoundingClientRect
		if (propName.StartsWith("rect.", StringComparison.Ordinal))
		{
			return await QueryRectAsync(
				context.DocumentScope,
				xpath,
				propName["rect.".Length..],
				ct);
		}

		// 滚动属性
		if (propName is "scrollLeft" or "scrollTop" or "scrollWidth" or "scrollHeight")
		{
			return await QueryElementPropertyAsync(
				context.DocumentScope,
				xpath,
				propName,
				context.Specialization == DomElementQuerySpecialization.Svg,
				ct);
		}

		// 客户端/偏移尺寸
		if (propName is "clientWidth" or "clientHeight" or "offsetWidth" or "offsetHeight")
		{
			return await QueryElementPropertyAsync(
				context.DocumentScope,
				xpath,
				propName,
				context.Specialization == DomElementQuerySpecialization.Svg,
				ct);
		}

		// 媒体属性
		if (propName is "naturalWidth" or "naturalHeight" or "currentSrc"
			or "currentTime" or "duration" or "paused")
		{
			return await QueryElementPropertyAsync(
				context.DocumentScope,
				xpath,
				propName,
				context.Specialization == DomElementQuerySpecialization.Svg,
				ct);
		}

		// 表单属性
		if (propName is "value" or "checked" or "selectedIndex" or "indeterminate")
		{
			return await QueryElementPropertyAsync(
				context.DocumentScope,
				xpath,
				propName,
				context.Specialization == DomElementQuerySpecialization.Svg,
				ct);
		}

		// 计算样式属性
		if (propName.StartsWith("style.", StringComparison.Ordinal))
		{
			return await QueryComputedStyleAsync(
				context.DocumentScope,
				xpath,
				propName["style.".Length..],
				ct);
		}

		// 其他属性：尝试 DOM property
		return await QueryElementPropertyAsync(
			context.DocumentScope,
			xpath,
			propName,
			context.Specialization == DomElementQuerySpecialization.Svg,
			ct);
	}

	private async ValueTask<DomPropertyQueryResult> QueryContentAsync(
		string documentScope,
		string xpath,
		string contentName,
		CancellationToken ct)
	{
		var js = BuildContentQueryScript(
			documentScope,
			xpath,
			contentName);
		var result = await ExecuteScriptAsync(js, ct);
		return result is not null
			? DomPropertyQueryResult.DirectConstant(result)
			: DomPropertyQueryResult.ConfirmedAbsent(
				$"Content property {contentName} not available");
	}

	internal static string BuildContentQueryScript(
		string documentScope,
		string xpath,
		string contentName) =>
		$$"""
			(() => {
				{{BuildScopedNodeLookupScript(documentScope, xpath)}}
				if (!node || node.nodeType !== 1) return null;
				switch ('{{EscapeJs(contentName)}}') {
					case 'name':
						return node.getAttribute('name') ?? node.name ?? null;
					case 'namespaceUri':
						return node.namespaceURI ?? null;
					case 'ariaLabel':
						return node.getAttribute('aria-label') ?? node.ariaLabel ?? null;
					case 'title':
						return node.getAttribute('title') ?? node.title ?? null;
					case 'href':
						return node.href ?? node.getAttribute('href') ?? null;
					case 'type':
						return node.type ?? node.getAttribute('type') ?? null;
					case 'value':
						return node.value ?? node.getAttribute('value') ?? null;
					case 'placeholder':
						return node.placeholder ?? node.getAttribute('placeholder') ?? null;
					case 'ownText':
						return Array.from(node.childNodes)
							.filter(child => child.nodeType === Node.TEXT_NODE)
							.map(child => child.nodeValue ?? '')
							.join('');
					case 'textContent':
						return node.textContent ?? null;
					case 'innerText':
						return node.innerText ?? null;
					default:
						return null;
				}
			})()
			""";

	/// <summary>
	/// 查询 getBoundingClientRect() 值
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryRectAsync(
		string documentScope,
		string xpath,
		string rectProp,
		CancellationToken ct)
	{
		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(documentScope, xpath)}}
				if (!node) return null;
				const rect = node.getBoundingClientRect();
				return String(rect.{{rectProp}});
			})()
			""";

		var result = await ExecuteScriptAsync(js, ct);
		return result is not null
			? DomPropertyQueryResult.DirectConstant(result)
			: DomPropertyQueryResult.ConfirmedAbsent($"rect.{rectProp} not available");
	}

	/// <summary>
	/// 查询 DOM 元素 property 值
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryElementPropertyAsync(
		string documentScope,
		string xpath,
		string propName,
		bool svg,
		CancellationToken ct)
	{
		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(documentScope, xpath)}}
				if (!node) return null;
				const value = node['{{EscapeJs(propName)}}'];
				if (value === null || value === undefined) return null;
				if ({{(svg ? "true" : "false")}}) {
					const animated = value.animVal ?? value.baseVal ?? value;
					if (animated !== null && animated !== undefined) {
						if (typeof animated === 'string'
							|| typeof animated === 'number'
							|| typeof animated === 'boolean') {
							return String(animated);
						}
						if (animated.valueAsString !== undefined) {
							return String(animated.valueAsString);
						}
						if (animated.value !== undefined) {
							return String(animated.value);
						}
					}
					const attribute = node.getAttribute('{{EscapeJs(propName)}}');
					return attribute === null ? null : attribute;
				}
				return String(value);
			})()
			""";

		var result = await ExecuteScriptAsync(js, ct);
		return result is not null
			? DomPropertyQueryResult.DirectConstant(result)
			: DomPropertyQueryResult.ConfirmedAbsent($"Property {propName} not available");
	}

	/// <summary>
	/// 查询 getComputedStyle() 值
	/// </summary>
	private async ValueTask<DomPropertyQueryResult> QueryComputedStyleAsync(
		string documentScope,
		string xpath,
		string cssProp,
		CancellationToken ct)
	{
		// 将 CSS 属性名转换为 camelCase
		var camelCase = string.Join("",
			cssProp.Split('-').Select((p, i) =>
				i == 0 ? p.ToLowerInvariant() : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(documentScope, xpath)}}
				if (!node) return null;
				const style = getComputedStyle(node);
				const value = style['{{camelCase}}'];
				return value != null && value !== '' ? String(value) : null;
			})()
			""";

		var result = await ExecuteScriptAsync(js, ct);
		return result is not null
			? DomPropertyQueryResult.DirectConstant(result)
			: DomPropertyQueryResult.ConfirmedAbsent($"Computed style {cssProp} not available");
	}

	private static bool TryGetSizeSlot(
		string cssPropertyName,
		out ElementSpaceSlotKind slot)
	{
		switch (cssPropertyName)
		{
			case "width":
				slot = ElementSpaceSlotKind.Width;
				return true;
			case "height":
				slot = ElementSpaceSlotKind.Height;
				return true;
			case "minWidth":
				slot = ElementSpaceSlotKind.MinWidth;
				return true;
			case "minHeight":
				slot = ElementSpaceSlotKind.MinHeight;
				return true;
			case "maxWidth":
				slot = ElementSpaceSlotKind.MaxWidth;
				return true;
			case "maxHeight":
				slot = ElementSpaceSlotKind.MaxHeight;
				return true;
			default:
				slot = default;
				return false;
		}
	}

	private async ValueTask<DomPropertyQueryResult> QueryEventAsync(
		DomPropertyQueryContext context,
		CancellationToken ct)
	{
		var eventName = GetEventName(context.PropertyName);
		if (string.IsNullOrWhiteSpace(eventName))
			return DomPropertyQueryResult.ConfirmedAbsent(
				$"Unknown event slot {context.PropertyName}");

		var slot = context.Slot.ToString();
		var js = $$"""
			(() => {
				{{BuildScopedNodeLookupScript(
					context.DocumentScope,
					context.XPath)}}
				if (!node) return null;
				const eventName = '{{EscapeJs(eventName)}}';
				const inlineSource = node.getAttribute('on' + eventName);
				const captured = node.ownerDocument.defaultView
					?.__iwesunEventRegistry?.query(
					node,
					eventName) ?? null;
				switch ('{{slot}}') {
					case 'Initialization':
						if (inlineSource !== null) {
							return {
								eventName,
								registrationKind: 'InlineAttribute',
								useCapture: false,
								passive: false,
								once: false,
								source: inlineSource
							};
						}
						return captured?.registration ?? null;
					case 'Link':
						if (inlineSource !== null) return 'inline-handler:' + inlineSource;
						if (captured?.registration) {
							return 'event-listener:' + eventName
								+ ':count=' + captured.registration.listenerCount;
						}
						return null;
					case 'Runtime':
						return captured?.runtime ?? null;
					default:
						return null;
				}
			})()
			""";
		var result = await ExecuteScriptAsync(js, ct);
		if (result is null)
			return DomPropertyQueryResult.ConfirmedAbsent(
				$"Event {eventName} has no {context.Slot} evidence");
		if (context.Slot == DomPropertyDataSlot.Link)
		{
			return DomPropertyQueryResult.Captured(
				result,
				ElementPropertyValueSource.LinkedCalculation,
				new ElementPropertyLink(
					ElementPropertyLinkKind.CustomString,
					result));
		}
		return DomPropertyQueryResult.DirectConstant(result);
	}

	private static string GetEventName(string queryName)
	{
		if (queryName.StartsWith("handler.on", StringComparison.Ordinal))
			return queryName["handler.on".Length..];
		if (queryName.StartsWith("listener.", StringComparison.Ordinal))
			return queryName["listener.".Length..];
		if (queryName.StartsWith("on", StringComparison.Ordinal))
			return queryName[2..];
		return queryName;
	}

	private static LayoutPhysicalAxis ParseFlexMainAxis(string? direction) =>
		direction is "column" or "column-reverse"
			? LayoutPhysicalAxis.Vertical
			: LayoutPhysicalAxis.Horizontal;

	internal static string BuildScopedNodeLookupScript(
		string documentScope,
		string xpath) =>
		$$"""
			const __scope = '{{EscapeJs(documentScope)}}';
			let __scopeDocument = document;
			if (__scope !== 'document') {
				for (const __frameIdentity of __scope.split('::')) {
					if (!__frameIdentity || __frameIdentity === 'document') continue;
					const __indexedFrame = /^iframe:(\d+)$/.exec(__frameIdentity);
					const __frameOwner = __indexedFrame
						? __scopeDocument.querySelectorAll('iframe,frame')[
							Number(__indexedFrame[1])] ?? null
						: __scopeDocument.evaluate(
							__frameIdentity,
							__scopeDocument,
							null,
							XPathResult.FIRST_ORDERED_NODE_TYPE,
							null).singleNodeValue;
					__scopeDocument = __frameOwner?.contentDocument ?? null;
					if (!__scopeDocument) break;
				}
			}
			const node = __scopeDocument
				? __scopeDocument.evaluate(
					'{{EscapeJs(xpath)}}',
					__scopeDocument,
					null,
					XPathResult.FIRST_ORDERED_NODE_TYPE,
					null).singleNodeValue
				: null;
			""";

	/// <summary>
	/// 执行 JS 并返回字符串结果（自动去引号）
	/// </summary>
	private async ValueTask<string?> ExecuteScriptAsync(string js, CancellationToken ct)
	{
		var raw = await ExecuteScriptRawAsync(js, ct);
		if (raw is null) return null;

		try
		{
			using var doc = JsonDocument.Parse(raw);
			if (doc.RootElement.ValueKind == JsonValueKind.Null)
				return null;
			if (doc.RootElement.ValueKind == JsonValueKind.String)
				return doc.RootElement.GetString();
			return doc.RootElement.GetRawText();
		}
		catch
		{
			return raw;
		}
	}

	/// <summary>
	/// 执行 JS 并返回原始 JSON 字符串
	/// </summary>
	private async ValueTask<string?> ExecuteScriptRawAsync(string js, CancellationToken ct)
	{
		var tcs = new TaskCompletionSource<string?>();
		using var registration = ct.Register(() => tcs.TrySetCanceled());

		_ = _webView2.ExecuteScriptAsync(js).ContinueWith(task =>
		{
			if (task.IsFaulted)
				tcs.TrySetResult(null);
			else if (task.IsCanceled)
				tcs.TrySetCanceled();
			else
				tcs.TrySetResult(task.Result);
		}, ct);

		return await tcs.Task;
	}

	private static string EscapeJs(string value) =>
		value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
}
