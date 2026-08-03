using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

public static class WebRuntimeResourceApplicationTracker
{
    public const string SnapshotSchema = "iwesun.webview2.request-dom-application/1.0";
    public const string AssignmentTasksSchema = "iwesun.webview2.request-dom-assignment-tasks/1.0";

    private const string InstallScript = """
        (() => {
          if (globalThis.__iwesunRequestDomApplicationTracker) return true;
          const maxRecords = 2048;
          const maxContainers = 128;
          const correlationWindowMs = 2500;
          const records = [];
          let sequence = 0;
          const trim = (value, limit = 4096) => String(value ?? '').slice(0, limit);
          const elementPath = element => {
            if (!element || element.nodeType !== 1) return '';
            const parts = [];
            let current = element;
            while (current && current.nodeType === 1) {
              const parent = current.parentElement;
              const peers = parent ? [...parent.children].filter(item => item.localName === current.localName) : [];
              parts.unshift(peers.length > 1 ? `${current.localName}[${peers.indexOf(current) + 1}]` : current.localName);
              if (parent) {
                current = parent;
                continue;
              }
              const root = current.getRootNode?.();
              if (root?.host) {
                parts.unshift('#shadow-root');
                current = root.host;
                continue;
              }
              break;
            }
            return '/' + parts.join('/');
          };
          const createRecord = (kind, method, url) => {
            const record = {
              id: `request-${++sequence}`,
              kind,
              method: trim(method || 'GET', 32).toUpperCase(),
              url: trim(url),
              startedAt: performance.now(),
              completedAt: null,
              status: null,
              succeeded: null,
              stack: trim(new Error().stack || ''),
              mutationCount: 0,
              droppedContainerCount: 0,
              containers: []
            };
            records.push(record);
            if (records.length > maxRecords) records.splice(0, records.length - maxRecords);
            return record;
          };
          const complete = (record, status, succeeded) => {
            if (!record || record.completedAt !== null) return;
            record.completedAt = performance.now();
            record.status = Number.isFinite(status) ? status : null;
            record.succeeded = !!succeeded;
          };
          const describeContainer = element => {
            const container = element?.closest?.('[role],main,aside,nav,header,footer,section,article,[class]') || element;
            return container ? {
              path: elementPath(container),
              tag: trim(container.localName, 64),
              role: trim(container.getAttribute?.('role'), 128),
              id: trim(container.id, 256),
              className: trim(container.className, 1024)
            } : null;
          };
          const registerMutation = target => {
            const now = performance.now();
            const element = target?.nodeType === 1 ? target : target?.parentElement;
            const description = describeContainer(element);
            if (!description?.path) return;
            for (const record of records) {
              const isPending = record.completedAt === null;
              const isRecent = !isPending && now >= record.completedAt && now - record.completedAt <= correlationWindowMs;
              if (!isPending && !isRecent) continue;
              record.mutationCount++;
              let container = record.containers.find(item => item.path === description.path);
              if (!container) {
                if (record.containers.length >= maxContainers) {
                  record.droppedContainerCount++;
                  continue;
                }
                container = { ...description, firstSeenAt: now, lastSeenAt: now, mutationCount: 0 };
                record.containers.push(container);
              }
              container.lastSeenAt = now;
              container.mutationCount++;
            }
          };
          const observers = new WeakSet();
          const observe = root => {
            if (!root || observers.has(root)) return;
            observers.add(root);
            new MutationObserver(mutations => {
              for (const mutation of mutations) {
                registerMutation(mutation.target);
                for (const node of mutation.addedNodes || []) {
                  if (node?.nodeType !== 1) continue;
                  if (node.shadowRoot) observe(node.shadowRoot);
                  for (const child of node.querySelectorAll?.('*') || []) if (child.shadowRoot) observe(child.shadowRoot);
                }
              }
            }).observe(root, { subtree: true, childList: true, attributes: true, characterData: true });
          };
          observe(document);
          const originalAttachShadow = Element.prototype.attachShadow;
          Element.prototype.attachShadow = function(init) {
            const root = originalAttachShadow.call(this, init);
            if (init?.mode === 'open') observe(root);
            return root;
          };
          const originalFetch = globalThis.fetch;
          if (typeof originalFetch === 'function') {
            globalThis.fetch = function(input, init) {
              let url = '';
              let method = init?.method || 'GET';
              try {
                url = input instanceof Request ? input.url : new URL(String(input), document.baseURI).href;
                if (input instanceof Request && !init?.method) method = input.method;
              } catch { url = trim(input); }
              const record = createRecord('fetch', method, url);
              return originalFetch.apply(this, arguments).then(
                response => { complete(record, response.status, response.ok); return response; },
                error => { complete(record, null, false); throw error; });
            };
          }
          const xhrState = new WeakMap();
          const originalOpen = XMLHttpRequest.prototype.open;
          const originalSend = XMLHttpRequest.prototype.send;
          XMLHttpRequest.prototype.open = function(method, url) {
            let absolute = trim(url);
            try { absolute = new URL(String(url), document.baseURI).href; } catch {}
            xhrState.set(this, { method: trim(method, 32), url: absolute, record: null });
            return originalOpen.apply(this, arguments);
          };
          XMLHttpRequest.prototype.send = function() {
            const state = xhrState.get(this) || { method: 'GET', url: '', record: null };
            state.record = createRecord('xhr', state.method, state.url);
            xhrState.set(this, state);
            const onReadyState = () => {
              if (this.readyState !== XMLHttpRequest.DONE) return;
              this.removeEventListener('readystatechange', onReadyState);
              complete(state.record, this.status, this.status >= 200 && this.status < 400);
            };
            this.addEventListener('readystatechange', onReadyState);
            try { return originalSend.apply(this, arguments); }
            catch (error) { complete(state.record, null, false); throw error; }
          };
          globalThis.__iwesunRequestDomApplicationTracker = {
            schema: 'iwesun.webview2.request-dom-application-tracker/1.0',
            snapshot: () => records.map(record => ({
              ...record,
              mappingStatus: record.containers.length ? 'candidate' : 'unknown',
              mappingNote: record.containers.length
                ? 'Temporal candidates only; verify with resource identity and UI state differences.'
                : 'No render container was found in the bounded observation window; assign the document or JavaScript control explicitly during interpretation.',
              containers: record.containers.map(item => ({ ...item }))
            }))
          };
          return true;
        })()
        """;

    private const string CaptureScript = """
        (() => {
          const frames = [];
          const visited = new WeakSet();
          const collect = (target, scope) => {
            if (!target || visited.has(target)) return;
            visited.add(target);
            try {
              const tracker = target.__iwesunRequestDomApplicationTracker;
              frames.push({ scope, url: String(target.location.href), accessible: true, records: tracker?.snapshot?.() || [] });
              for (let index = 0; index < target.frames.length; index++) collect(target.frames[index], `${scope}/frame[${index + 1}]`);
            } catch (error) {
              frames.push({ scope, url: '', accessible: false, error: String(error).slice(0, 512), records: [] });
            }
          };
          collect(globalThis, 'top');
          return JSON.stringify({
            schema: 'iwesun.webview2.request-dom-application/1.0',
            capturedAt: new Date().toISOString(),
            correlation: { kind: 'temporal-window', windowMilliseconds: 2500 },
            frames
          });
        })()
        """;

    public static async Task InstallAsync(CoreWebView2 browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        await browser.AddScriptToExecuteOnDocumentCreatedAsync(InstallScript);
        await browser.ExecuteScriptAsync(InstallScript);
    }

    public static async Task<string> CaptureAsync(CoreWebView2 browser)
    {
        ArgumentNullException.ThrowIfNull(browser);
        var encoded = await browser.ExecuteScriptAsync(CaptureScript);
        var snapshot = JsonSerializer.Deserialize<string>(encoded)
            ?? throw new InvalidDataException("The request-to-DOM application tracker returned no snapshot.");
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    public static void ValidateSnapshot(string snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot);
        using var document = JsonDocument.Parse(snapshot);
        var root = document.RootElement;
        if (!root.TryGetProperty("schema", out var schema)
            || !string.Equals(schema.GetString(), SnapshotSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The request-to-DOM application snapshot schema is invalid.");
        }
        if (!root.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The request-to-DOM application snapshot has no frames array.");
        foreach (var frame in frames.EnumerateArray())
        {
            if (!frame.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("A request-to-DOM application frame has no records array.");
            foreach (var record in records.EnumerateArray())
            {
                if (!record.TryGetProperty("mappingStatus", out var mappingStatus)
                    || mappingStatus.ValueKind != JsonValueKind.String
                    || mappingStatus.GetString() is not ("candidate" or "unknown"))
                {
                    throw new InvalidDataException("A request-to-DOM application record has no valid mapping status.");
                }
            }
        }
    }

    public static string CreateManualAssignmentTasks(string snapshot)
    {
        ValidateSnapshot(snapshot);
        using var document = JsonDocument.Parse(snapshot);
        var tasks = new List<object>();
        foreach (var frame in document.RootElement.GetProperty("frames").EnumerateArray())
        {
            var scope = ReadString(frame, "scope");
            foreach (var record in frame.GetProperty("records").EnumerateArray())
            {
                if (!string.Equals(ReadString(record, "mappingStatus"), "unknown", StringComparison.Ordinal))
                    continue;
                tasks.Add(new
                {
                    scope,
                    requestId = ReadString(record, "id"),
                    kind = ReadString(record, "kind"),
                    method = ReadString(record, "method"),
                    url = ReadString(record, "url"),
                    status = "pending-user-assignment",
                    assignedContainerPath = (string?)null,
                    note = "Select the document or JavaScript control container during the interpretation step."
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            schema = AssignmentTasksSchema,
            generatedAt = DateTimeOffset.UtcNow,
            taskCount = tasks.Count,
            tasks
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
