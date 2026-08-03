using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.Web;

/// <summary>
/// Installs the event-listener evidence registry before page navigation.
/// </summary>
public static class WebView2EventInstrumentation
{
	public static string InstallationScript => Script;

	public static async ValueTask<string> InstallAsync(
		CoreWebView2 webView2,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(webView2);
		return await webView2
			.AddScriptToExecuteOnDocumentCreatedAsync(Script)
			.WaitAsync(cancellationToken);
	}

	internal static string Script { get; } =
		"""
		(() => {
			if (globalThis.__iwesunEventRegistry) return;
			const registrations = new WeakMap();
			const wrappers = new WeakMap();
			const allRecords = [];
			const originalAdd = EventTarget.prototype.addEventListener;
			const originalRemove = EventTarget.prototype.removeEventListener;

			function optionsOf(options) {
				if (typeof options === 'boolean') {
					return { capture: options, passive: false, once: false };
				}
				return {
					capture: Boolean(options && options.capture),
					passive: Boolean(options && options.passive),
					once: Boolean(options && options.once)
				};
			}

			function xpathOf(node) {
				if (node === globalThis) return '/@window';
				if (node instanceof Document) return '/@document';
				if (!(node instanceof Node)) {
					const kind = node?.constructor?.name || 'EventTarget';
					return `/@event-target/${kind}`;
				}
				const root = node.getRootNode?.();
				if (!(root instanceof Document)
					&& !(root instanceof ShadowRoot && root.host?.isConnected)) {
					const kind = node.localName || node.nodeName || 'node';
					return `/@detached/${String(kind).toLowerCase()}`;
				}
				const parts = [];
				let current = node.nodeType === Node.ELEMENT_NODE
					? node
					: node.parentElement;
				while (current) {
					const parent = current.parentElement;
					const peers = parent
						? [...parent.children].filter(item =>
							item.localName === current.localName)
						: [];
					const segment = peers.length > 1
						? `${current.localName}[${peers.indexOf(current) + 1}]`
						: current.localName;
					parts.unshift(segment);
					current = current.parentNode instanceof ShadowRoot
						? current.parentNode.host
						: current.parentElement;
				}
				return '/' + parts.join('/');
			}

			function recordsFor(target, type, create) {
				let byType = registrations.get(target);
				if (!byType && create) {
					byType = new Map();
					registrations.set(target, byType);
				}
				if (!byType) return null;
				let records = byType.get(type);
				if (!records && create) {
					records = [];
					byType.set(type, records);
				}
				return records || null;
			}

			function wrapperMapFor(listener, create) {
				let map = wrappers.get(listener);
				if (!map && create) {
					map = new Map();
					wrappers.set(listener, map);
				}
				return map || null;
			}

			EventTarget.prototype.addEventListener = function(type, listener, options) {
				if (!listener) return originalAdd.call(this, type, listener, options);
				const normalized = optionsOf(options);
				const key = `${type}:${normalized.capture}`;
				const map = wrapperMapFor(listener, true);
				let wrapper = map.get(key);
				if (!wrapper) {
					const target = this;
					const record = {
						target,
						eventName: String(type),
						useCapture: normalized.capture,
						passive: normalized.passive,
						once: normalized.once,
						invocationCount: 0,
						lastInvokedAt: null,
						targetXPath: '',
						currentTargetXPath: '',
						phase: 'None',
						defaultPrevented: false,
						propagationStopped: false,
						immediatePropagationStopped: false,
						active: true
					};
					wrapper = function(event) {
						record.invocationCount++;
						record.lastInvokedAt = new Date().toISOString();
						record.targetXPath = xpathOf(event.target);
						record.currentTargetXPath = xpathOf(event.currentTarget);
						record.phase = event.eventPhase === 1
							? 'Capture'
							: event.eventPhase === 2 ? 'Target' : 'Bubble';
						record.defaultPrevented = Boolean(event.defaultPrevented);
						try {
							if (typeof listener === 'function') {
								return listener.call(this, event);
							}
							return listener.handleEvent(event);
						} finally {
							record.defaultPrevented = Boolean(event.defaultPrevented);
							if (record.once) record.active = false;
						}
					};
					map.set(key, wrapper);
					recordsFor(target, String(type), true).push(record);
					allRecords.push(record);
					wrapper.__iwesunRecord = record;
				}
				return originalAdd.call(this, type, wrapper, options);
			};

			EventTarget.prototype.removeEventListener = function(type, listener, options) {
				const normalized = optionsOf(options);
				const map = listener && wrapperMapFor(listener, false);
				const wrapper = map && map.get(`${type}:${normalized.capture}`);
				if (wrapper && wrapper.__iwesunRecord) {
					wrapper.__iwesunRecord.active = false;
				}
				return originalRemove.call(this, type, wrapper || listener, options);
			};

			globalThis.__iwesunEventRegistry = Object.freeze({
				query(target, type) {
					const records = recordsFor(target, String(type), false);
					if (!records) return null;
					const active = records.filter(record => record.active);
					const evidence = active.length ? active : records;
					if (!evidence.length) return null;
					const last = evidence[evidence.length - 1];
					return {
						registration: {
							eventName: String(type),
							registrationKind: 'EventListener',
							useCapture: evidence.some(record => record.useCapture),
							passive: evidence.every(record => record.passive),
							once: evidence.every(record => record.once),
							listenerCount: active.length
						},
						runtime: {
							invocationCount: evidence.reduce(
								(total, record) => total + record.invocationCount, 0),
							lastInvokedAt: last.lastInvokedAt,
							targetXPath: last.targetXPath,
							currentTargetXPath: last.currentTargetXPath,
							phase: last.phase,
							defaultPrevented: last.defaultPrevented,
							propagationStopped: last.propagationStopped,
							immediatePropagationStopped:
								last.immediatePropagationStopped
						}
					};
				},
				readAll() {
					return allRecords
						.filter(record =>
							record.active
								|| record.once && record.invocationCount > 0)
						.map((record, index) => ({
							xpath: xpathOf(record.target),
							eventName: record.eventName,
							propertyName: `event.${record.eventName}.${index}`,
							handlerIdentity:
								`listener:${record.eventName}:${index}`,
							description: 'Instrumented addEventListener registration.'
						}));
				}
			});
		})();
		""";
}
