using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

internal interface IHtmlCursorTarget
{
	void ApplyCursor(string rule);

	bool TryReadCursor(out string value);
}

public static class HtmlCursorContract
{
	private sealed record ResourceDescriptor(string Url, string ModuleName, uint ResourceId);

	private static readonly ConcurrentDictionary<string, ResourceDescriptor>
		Resources = new(StringComparer.Ordinal);
	private static readonly ConditionalWeakTable<InputCursor, ResourceDescriptor>
		MaterializedResources = new();
	private static readonly ConditionalWeakTable<DependencyObject, CursorOwner>
		EffectiveOwners = new();
	private sealed class CursorOwner(IHtmlCursorTarget target)
	{
		internal IHtmlCursorTarget Target { get; } = target;
	}
	private static readonly Regex UrlCursorRegex = new(
		@"^url\(\s*(?:""(?<double>[^""]+)""|'(?<single>[^']+)'|(?<bare>[^)\s]+))\s*\)(?:\s+[+-]?(?:\d+(?:\.\d*)?|\.\d+)){0,2}(?:\s*,\s*(?<fallback>[a-z-]+))?$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	public static void RegisterResourceCursor(
		string url,
		string moduleName,
		uint resourceId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(url);
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
		if (resourceId == 0)
			throw new ArgumentOutOfRangeException(nameof(resourceId));
		Resources[url] = new(url, moduleName, resourceId);
	}

	internal static (string Rule, InputCursor? Cursor) Create(string value)
	{
		var originalRule = value.Trim();
		var urlMatch = UrlCursorRegex.Match(originalRule);
		if (urlMatch.Success)
		{
			var url = FirstValue(
				urlMatch.Groups["double"].Value,
				urlMatch.Groups["single"].Value,
				urlMatch.Groups["bare"].Value);
			if (!Resources.TryGetValue(url, out var descriptor))
			{
				throw new InvalidDataException(
					$"CSS cursor resource '{url}' was not compiled into a registered Win32 cursor module.");
			}
			var resourceCursor = InputDesktopResourceCursor.CreateFromModule(
				descriptor.ModuleName,
				descriptor.ResourceId);
			MaterializedResources.Add(resourceCursor, descriptor);
			return (originalRule, resourceCursor);
		}
		var rule = originalRule.ToLowerInvariant();
		if (rule is "auto" or "default")
			return (rule, rule == "auto" ? null : InputSystemCursor.Create(InputSystemCursorShape.Arrow));
		var shape = rule switch
		{
			"pointer" or "grab" or "grabbing" => InputSystemCursorShape.Hand,
			"text" or "vertical-text" => InputSystemCursorShape.IBeam,
			"crosshair" => InputSystemCursorShape.Cross,
			"move" or "all-scroll" => InputSystemCursorShape.SizeAll,
			"ew-resize" or "e-resize" or "w-resize" or "col-resize" =>
				InputSystemCursorShape.SizeWestEast,
			"ns-resize" or "n-resize" or "s-resize" or "row-resize" =>
				InputSystemCursorShape.SizeNorthSouth,
			"nesw-resize" or "ne-resize" or "sw-resize" =>
				InputSystemCursorShape.SizeNortheastSouthwest,
			"nwse-resize" or "nw-resize" or "se-resize" =>
				InputSystemCursorShape.SizeNorthwestSoutheast,
			"not-allowed" or "no-drop" => InputSystemCursorShape.UniversalNo,
			"wait" => InputSystemCursorShape.Wait,
			"progress" => InputSystemCursorShape.AppStarting,
			"help" => InputSystemCursorShape.Help,
			_ => throw new InvalidDataException(
				$"Unsupported CSS cursor '{value}'. URL and image cursors require a custom InputCursor resource.")
		};
		return (rule, InputSystemCursor.Create(shape));
	}

	internal static bool Matches(
		string rule,
		InputCursor? cursor)
	{
		var urlMatch = UrlCursorRegex.Match(rule);
		if (urlMatch.Success)
		{
			var url = FirstValue(
				urlMatch.Groups["double"].Value,
				urlMatch.Groups["single"].Value,
				urlMatch.Groups["bare"].Value);
			return cursor is InputDesktopResourceCursor
				&& MaterializedResources.TryGetValue(cursor, out var descriptor)
				&& string.Equals(descriptor.Url, url, StringComparison.Ordinal)
				&& Resources.TryGetValue(url, out var registered)
				&& descriptor == registered;
		}
		var expected = Create(rule).Cursor;
		return expected is null
			? cursor is null
			: cursor is InputSystemCursor actual
				&& expected is InputSystemCursor expectedSystem
				&& actual.CursorShape == expectedSystem.CursorShape;
	}

	internal static void RegisterEffectiveOwner(
		DependencyObject element,
		IHtmlCursorTarget owner)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentNullException.ThrowIfNull(owner);
		EffectiveOwners.Remove(element);
		EffectiveOwners.Add(element, new(owner));
	}

	internal static bool TryReadEffective(
		DependencyObject element,
		out string value)
	{
		if (element is IHtmlCursorTarget direct)
			return direct.TryReadCursor(out value);
		if (EffectiveOwners.TryGetValue(element, out var owner))
			return owner.Target.TryReadCursor(out value);
		value = string.Empty;
		return false;
	}

	private static string FirstValue(params string[] values) =>
		values.First(static value => value.Length > 0);
}
