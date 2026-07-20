using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimePageEvidenceResult(string OutputDirectory, DateTimeOffset CapturedAt);

public sealed record WebRuntimePageEvidenceProgress(
    string Stage,
    string Description,
    int? Completed = null,
    int? Total = null);

public sealed record WebRuntimePageEvidenceOptions
{
    public TimeSpan StabilizationDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public bool WaitForDocumentFonts { get; init; } = true;
    public WebRuntimeNetworkEvidenceExportOptions? NetworkExport { get; init; }
    public IProgress<WebRuntimePageEvidenceProgress>? Progress { get; init; }
}

public static class WebRuntimePageEvidenceCapture
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<WebRuntimePageEvidenceResult> CaptureAsync(
        CoreWebView2 browser,
        IWebRuntimeScriptSession scriptSession,
        string outputDirectory,
        WebRuntimeNetworkEvidenceSession? networkSession = null,
        WebRuntimeNetworkEvidenceCheckpoint networkCheckpoint = default,
        CancellationToken cancellationToken = default,
        WebRuntimePageEvidenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(scriptSession);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var capturedAt = DateTimeOffset.UtcNow;
        var captureOptions = options ?? new WebRuntimePageEvidenceOptions();

        Report(captureOptions, "stabilizing", "正在等待字体、布局和动画帧稳定…");
        await WaitForStablePresentationAsync(browser, captureOptions, cancellationToken);
        Report(captureOptions, "context", "正在记录视口和页面上下文…");
        await CaptureContextAsync(browser, directory, cancellationToken);

        Report(captureOptions, "dom", "正在抓取完整 DOM、属性、Shadow DOM 和 iframe…");
        var liveTree = await WebRuntimeDomSnapshot.CaptureAsync(scriptSession, cancellationToken)
            ;
        await File.WriteAllTextAsync(Path.Combine(directory, "live-dom-tree.json"), liveTree, Utf8NoBom, cancellationToken)
            ;
        await CaptureDocumentAndFramesAsync(browser, directory, cancellationToken);
        Report(captureOptions, "computed-styles", "正在分块抓取元素几何和完整计算样式…");
        await CaptureCompleteDomPropertiesAsync(browser, directory, captureOptions, cancellationToken);
        Report(captureOptions, "css", "正在抓取 CSSOM 和样式表原文…");
        await CaptureStyleSheetsAsync(browser, Path.Combine(directory, "maximum-template.css"), cancellationToken)
            ;
        Report(captureOptions, "scripts-events", "正在抓取脚本源码和事件监听接口…");
        await CaptureScriptsAndEventsAsync(browser, directory, cancellationToken);
        Report(captureOptions, "resource-map", "正在关联外部资源、申请模块和呈现容器…");
        var resourceApplicationMap = await CaptureResourceApplicationMapAsync(browser, directory, cancellationToken);
        var requestDomApplication = await WebRuntimeResourceApplicationTracker.CaptureAsync(browser);
        await File.WriteAllTextAsync(Path.Combine(directory, "request-dom-application-map.json"),
            requestDomApplication, Utf8NoBom, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "request-dom-assignment-tasks.json"),
            WebRuntimeResourceApplicationTracker.CreateManualAssignmentTasks(requestDomApplication),
            Utf8NoBom, cancellationToken);
        Report(captureOptions, "cdp-dom", "正在抓取 CDP DOM 和 DOMSnapshot…");
        await CaptureCdpDomAsync(browser, directory, cancellationToken);
        Report(captureOptions, "mhtml", "正在生成 MHTML 页面归档…");
        await CaptureMhtmlAsync(browser, Path.Combine(directory, "page.mhtml"), cancellationToken);
        if (networkSession is not null)
        {
            Report(captureOptions, "http", "正在保存 HTTP 文档、JSON 原文和共享资源引用…");
            if (captureOptions.NetworkExport is null)
                await networkSession.ExportAsync(directory, networkCheckpoint, cancellationToken);
            else
                await networkSession.ExportAsync(directory, networkCheckpoint, captureOptions.NetworkExport with
                {
                    LinkedResourceUrls = CollectLinkedResourceUrls(browser.Source, resourceApplicationMap, requestDomApplication),
                    ResourceApplicationSnapshot = resourceApplicationMap,
                    RequestDomApplicationSnapshot = requestDomApplication
                }, cancellationToken);
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "runtime-evidence-manifest.json"),
            JsonSerializer.Serialize(new
            {
                schema = "iwesun.webview2.page-evidence/1.0",
                capturedAt,
                files = new[]
                {
                    "capture-context.json", "live-dom-tree.json", "complete-dom-properties.json", "document.html", "frames.json",
                    "cdp-dom-tree.json", "dom-snapshot.json", "maximum-template.css",
                    "scripts/script-registry.json", "event-registry.json", "resource-container-map.json",
                    "request-dom-application-map.json", "request-dom-assignment-tasks.json", "page.mhtml"
                },
                httpEvidence = networkSession is not null
            }, JsonOptions), Utf8NoBom, cancellationToken);
        Report(captureOptions, "completed", "完整页面证据包已生成。");
        return new(directory, capturedAt);
    }

    private static void Report(
        WebRuntimePageEvidenceOptions options,
        string stage,
        string description,
        int? completed = null,
        int? total = null) =>
        options.Progress?.Report(new WebRuntimePageEvidenceProgress(stage, description, completed, total));

    private static async Task WaitForStablePresentationAsync(
        CoreWebView2 browser,
        WebRuntimePageEvidenceOptions options,
        CancellationToken cancellationToken)
    {
        if (options.StabilizationDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "StabilizationDelay cannot be negative.");
        if (options.WaitForDocumentFonts)
        {
            await browser.ExecuteScriptAsync("""
                (async()=>{if(document.fonts?.ready){await Promise.race([document.fonts.ready,new Promise(resolve=>setTimeout(resolve,3000))])}await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));return true})()
                """);
        }
        else
        {
            await browser.ExecuteScriptAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))")
                ;
        }
        if (options.StabilizationDelay > TimeSpan.Zero)
            await Task.Delay(options.StabilizationDelay, cancellationToken);
    }

    private static async Task CaptureContextAsync(
        CoreWebView2 browser,
        string directory,
        CancellationToken cancellationToken)
    {
        var encoded = await browser.ExecuteScriptAsync("""
            JSON.stringify({url:location.href,title:document.title,readyState:document.readyState,viewport:{innerWidth,innerHeight,outerWidth,outerHeight,devicePixelRatio,scrollX,scrollY,visualViewport:visualViewport?{width:visualViewport.width,height:visualViewport.height,scale:visualViewport.scale,offsetLeft:visualViewport.offsetLeft,offsetTop:visualViewport.offsetTop}:null},document:{scrollWidth:document.documentElement.scrollWidth,scrollHeight:document.documentElement.scrollHeight,clientWidth:document.documentElement.clientWidth,clientHeight:document.documentElement.clientHeight},capturedAt:new Date().toISOString()})
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "capture-context.json"),
            JsonSerializer.Deserialize<string>(encoded) ?? "{}", Utf8NoBom, cancellationToken);
    }

    public static async Task CaptureStyleSheetsAsync(
        CoreWebView2 browser, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var headers = new ConcurrentDictionary<string, JsonElement>(StringComparer.Ordinal);
        var receiver = browser.GetDevToolsProtocolEventReceiver("CSS.styleSheetAdded");
        void OnStyleSheetAdded(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
        {
            try
            {
                using var document = JsonDocument.Parse(args.ParameterObjectAsJson);
                if (!document.RootElement.TryGetProperty("header", out var header)) return;
                var id = ReadString(header, "styleSheetId");
                if (!string.IsNullOrEmpty(id)) headers[id] = header.Clone();
            }
            catch (JsonException) { }
        }
        receiver.DevToolsProtocolEventReceived += OnStyleSheetAdded;
        var cdpSections = new List<string>();
        try
        {
            await browser.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");
            await browser.CallDevToolsProtocolMethodAsync("CSS.enable", "{}");
            await Task.Delay(250, cancellationToken);
            foreach (var pair in headers.OrderBy(item => ReadString(item.Value, "sourceURL"), StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                var response = await browser.CallDevToolsProtocolMethodAsync("CSS.getStyleSheetText",
                    JsonSerializer.Serialize(new { styleSheetId = pair.Key }));
                using var document = JsonDocument.Parse(response);
                var text = document.RootElement.TryGetProperty("text", out var value) ? value.GetString() ?? string.Empty : string.Empty;
                cdpSections.Add($"/* CDP source: {ReadString(pair.Value, "sourceURL")}; id: {pair.Key} */\n{text}");
            }
        }
        finally
        {
            receiver.DevToolsProtocolEventReceived -= OnStyleSheetAdded;
            try { await browser.CallDevToolsProtocolMethodAsync("CSS.disable", "{}"); } catch (ArgumentException) { }
            try { await browser.CallDevToolsProtocolMethodAsync("DOM.disable", "{}"); } catch (ArgumentException) { }
        }
        var encoded = await browser.ExecuteScriptAsync("""
            (()=>{const sections=[],seen=new Set();const rules=(root,label)=>{const sheets=[...(root.styleSheets||[]),...(root.adoptedStyleSheets||[])];for(const sheet of sheets){if(seen.has(sheet))continue;seen.add(sheet);try{sections.push(`/* CSSOM source: ${String(sheet.href||label)} */\n${[...sheet.cssRules].map(rule=>rule.cssText).join('\n')}`)}catch(error){sections.push(`/* inaccessible stylesheet: ${String(sheet.href||label)} - ${String(error)} */`)}}};const visit=(root,label)=>{rules(root,label);for(const element of root.querySelectorAll?root.querySelectorAll('*'):[]){if(element.shadowRoot)visit(element.shadowRoot,`${label}#shadow-root`);if(element.localName==='iframe'){try{if(element.contentDocument)visit(element.contentDocument,`${label}/iframe`)}catch{}}}};visit(document,String(location.href||'top'));return `/* iwesun-template-kind: maximum */\n${sections.join('\n\n')}`})()
            """);
        var css = JsonSerializer.Deserialize<string>(encoded) ?? string.Empty;
        await File.WriteAllTextAsync(outputPath, css + "\n\n" + string.Join("\n\n", cdpSections), Utf8NoBom, cancellationToken);
    }

    public static async Task CaptureScriptsAndEventsAsync(
        CoreWebView2 browser, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var scriptsDirectory = Path.Combine(outputDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);
        var parsed = new ConcurrentDictionary<string, JsonElement>(StringComparer.Ordinal);
        var receiver = browser.GetDevToolsProtocolEventReceiver("Debugger.scriptParsed");
        void OnParsed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
        {
            try
            {
                using var document = JsonDocument.Parse(args.ParameterObjectAsJson);
                if (document.RootElement.TryGetProperty("scriptId", out var id) && !string.IsNullOrEmpty(id.GetString()))
                    parsed[id.GetString()!] = document.RootElement.Clone();
            }
            catch (JsonException) { }
        }
        receiver.DevToolsProtocolEventReceived += OnParsed;
        try
        {
            await browser.CallDevToolsProtocolMethodAsync("Debugger.enable", "{}");
            await Task.Delay(250, cancellationToken);
            var registry = new List<object>();
            var index = 0;
            foreach (var pair in parsed.OrderBy(item => ReadString(item.Value, "url"), StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                var response = await browser.CallDevToolsProtocolMethodAsync("Debugger.getScriptSource",
                    JsonSerializer.Serialize(new { scriptId = pair.Key }));
                using var sourceDocument = JsonDocument.Parse(response);
                var source = sourceDocument.RootElement.TryGetProperty("scriptSource", out var value) ? value.GetString() ?? string.Empty : string.Empty;
                var file = $"{index++:D4}-{SafeToken(pair.Key)}.js";
                await File.WriteAllTextAsync(Path.Combine(scriptsDirectory, file), source, Utf8NoBom, cancellationToken);
                registry.Add(new { scriptId = pair.Key, url = ReadString(pair.Value, "url"), length = source.Length, file });
            }
            await File.WriteAllTextAsync(Path.Combine(scriptsDirectory, "script-registry.json"),
                JsonSerializer.Serialize(new { schema = "iwesun.webview2.script-registry/1.0", count = registry.Count, scripts = registry }, JsonOptions),
                Utf8NoBom, cancellationToken);

            var listeners = await CaptureListenersAsync(browser);
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "event-registry.json"), listeners, Utf8NoBom, cancellationToken)
                ;
        }
        finally
        {
            receiver.DevToolsProtocolEventReceived -= OnParsed;
            try { await browser.CallDevToolsProtocolMethodAsync("Debugger.disable", "{}"); }
            catch (ArgumentException) { }
        }
    }

    public static async Task CaptureMhtmlAsync(
        CoreWebView2 browser, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var response = await browser.CallDevToolsProtocolMethodAsync("Page.captureSnapshot", "{\"format\":\"mhtml\"}")
            ;
        using var document = JsonDocument.Parse(response);
        var data = document.RootElement.GetProperty("data").GetString()
            ?? throw new InvalidDataException("Page.captureSnapshot returned no MHTML data.");
        await File.WriteAllTextAsync(outputPath, data, Utf8NoBom, cancellationToken);
    }

    private static async Task CaptureDocumentAndFramesAsync(CoreWebView2 browser, string directory, CancellationToken cancellationToken)
    {
        var htmlJson = await browser.ExecuteScriptAsync("document.documentElement.cloneNode(true).outerHTML");
        await File.WriteAllTextAsync(Path.Combine(directory, "document.html"), JsonSerializer.Deserialize<string>(htmlJson) ?? string.Empty,
            Utf8NoBom, cancellationToken);
        var framesJson = await browser.ExecuteScriptAsync("""
            JSON.stringify([...document.querySelectorAll('iframe')].map((frame,index)=>{const rect=frame.getBoundingClientRect();try{const doc=frame.contentDocument;return{index,src:frame.src,width:rect.width,height:rect.height,accessible:!!doc,html:doc?.documentElement?.cloneNode(true).outerHTML||''}}catch(error){return{index,src:frame.src,width:rect.width,height:rect.height,accessible:false,error:String(error)}}}))
            """);
        var frames = JsonSerializer.Deserialize<string>(framesJson) ?? "[]";
        await File.WriteAllTextAsync(Path.Combine(directory, "frames.json"), frames, Utf8NoBom, cancellationToken);
        using var document = JsonDocument.Parse(frames);
        foreach (var frame in document.RootElement.EnumerateArray())
        {
            if (!frame.GetProperty("accessible").GetBoolean()) continue;
            await File.WriteAllTextAsync(Path.Combine(directory, $"frame-{frame.GetProperty("index").GetInt32():D2}.html"),
                frame.GetProperty("html").GetString() ?? string.Empty, Utf8NoBom, cancellationToken);
        }
    }

    private static async Task CaptureCompleteDomPropertiesAsync(
        CoreWebView2 browser,
        string directory,
        WebRuntimePageEvidenceOptions options,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 100;
        var preparationJson = await browser.ExecuteScriptAsync("""
            (()=>{const entries=[];const names=Array.from(getComputedStyle(document.documentElement));const path=e=>{const parts=[];for(let current=e;current?.nodeType===1;current=current.parentElement){const peers=current.parentElement?[...current.parentElement.children].filter(x=>x.localName===current.localName):[];parts.unshift(peers.length>1?`${current.localName}[${peers.indexOf(current)+1}]`:current.localName)}return'/'+parts.join('/')};const walk=(root,scope)=>{for(const e of root.querySelectorAll?root.querySelectorAll('*'):[]){const elementPath=`${scope}${path(e)}`;entries.push({element:e,scope,path:elementPath});if(e.shadowRoot)walk(e.shadowRoot,`${elementPath}#shadow-root`);if(e.localName==='iframe'){try{if(e.contentDocument)walk(e.contentDocument,`${elementPath}#document`)}catch{}}}};walk(document,'');globalThis.__iwesunCompleteDomCapture={entries,names};return JSON.stringify({count:entries.length,names})})()
            """);
        var preparationEncoded = JsonSerializer.Deserialize<string>(preparationJson) ?? "{}";
        using var preparation = JsonDocument.Parse(preparationEncoded);
        var total = preparation.RootElement.GetProperty("count").GetInt32();
        var propertyNames = preparation.RootElement.GetProperty("names").Clone();
        var outputPath = Path.Combine(directory, "complete-dom-properties.json");
        await using var output = new StreamWriter(outputPath, append: false, Utf8NoBom);
        await output.WriteAsync("{\"schema\":\"iwesun.webview2.complete-dom-properties/2.0\",\"stylePropertyNames\":".AsMemory(), cancellationToken);
        await output.WriteAsync(propertyNames.GetRawText().AsMemory(), cancellationToken);
        await output.WriteAsync(",\"elements\":[".AsMemory(), cancellationToken);
        var wroteAny = false;
        try
        {
            for (var offset = 0; offset < total; offset += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkResult = await browser.ExecuteScriptAsync($$$$"""
                    (()=>{const capture=globalThis.__iwesunCompleteDomCapture||{entries:[],names:[]};const values=s=>capture.names.map(name=>s.getPropertyValue(name));const pseudo=(e,name)=>{const s=getComputedStyle(e,name),content=s.getPropertyValue('content');if(name!=='::marker'&&(content==='none'||content==='normal'))return null;if(name==='::marker'&&getComputedStyle(e).display!=='list-item')return null;return values(s)};const propertyNames=['value','checked','selected','disabled','readOnly','open','currentSrc','naturalWidth','naturalHeight','scrollLeft','scrollTop','scrollWidth','scrollHeight','clientWidth','clientHeight','offsetWidth','offsetHeight','currentTime','duration','paused'];return JSON.stringify(capture.entries.slice({{{{offset}}}},{{{{offset + chunkSize}}}}).map(entry=>{const e=entry.element,s=getComputedStyle(e),r=e.getBoundingClientRect(),properties={};for(const name of propertyNames){try{const value=e[name];if(value===null||['string','number','boolean'].includes(typeof value))properties[name]=value}catch{}}return{scope:entry.scope,path:entry.path,tag:e.localName,attributes:Object.fromEntries([...e.attributes].map(a=>[a.name,a.value])),properties,styleValues:values(s),pseudo:{before:pseudo(e,'::before'),after:pseudo(e,'::after'),marker:pseudo(e,'::marker')},rect:[r.x,r.y,r.width,r.height],visible:!!(r.width||r.height||e.getClientRects().length)}}))})()
                    """);
                var chunk = JsonSerializer.Deserialize<string>(chunkResult) ?? "[]";
                if (chunk.Length > 2)
                {
                    if (wroteAny) await output.WriteAsync(",".AsMemory(), cancellationToken);
                    await output.WriteAsync(chunk.AsMemory(1, chunk.Length - 2), cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    wroteAny = true;
                }
                var completed = Math.Min(offset + chunkSize, total);
                Report(options, "computed-styles", $"正在流式抓取完整计算样式：{completed}/{total}", completed, total);
            }
            await output.WriteAsync("]}".AsMemory(), cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            try { await browser.ExecuteScriptAsync("delete globalThis.__iwesunCompleteDomCapture;true"); }
            catch (InvalidOperationException) { }
        }
        Report(options, "computed-styles", "元素几何和完整计算样式已写入。", total, total);
    }

    private static async Task<string> CaptureResourceApplicationMapAsync(CoreWebView2 browser, string directory, CancellationToken cancellationToken)
    {
        var encoded = await browser.ExecuteScriptAsync("""
            (()=>{const applications=[];const path=e=>{const parts=[];for(let current=e;current?.nodeType===1;current=current.parentElement){const peers=current.parentElement?[...current.parentElement.children].filter(x=>x.localName===current.localName):[];parts.unshift(peers.length>1?`${current.localName}[${peers.indexOf(current)+1}]`:current.localName)}return'/'+parts.join('/')};const visit=(root,scope)=>{for(const e of root.querySelectorAll?root.querySelectorAll('*'):[]){for(const attribute of ['src','href','data','poster']){const raw=e.getAttribute?.(attribute);if(!raw)continue;let url=raw;try{url=new URL(raw,e.ownerDocument.baseURI).href}catch{}const container=e.closest?.('[role],main,aside,nav,header,footer,section,article,[class]')||e.parentElement;applications.push({scope,requestModule:`dom:${e.localName}:${attribute}`,elementPath:`${scope}${path(e)}`,tag:e.localName,attribute,url,renderContainer:{path:container?`${scope}${path(container)}`:'',tag:container?.localName||'',role:container?.getAttribute?.('role')||'',id:container?.id||'',className:String(container?.className||'')}})}if(e.shadowRoot)visit(e.shadowRoot,`${scope}${path(e)}#shadow-root`);if(e.localName==='iframe'){try{if(e.contentDocument)visit(e.contentDocument,`${scope}${path(e)}#document`)}catch{}}}};visit(document,'top');const entries=[...performance.getEntriesByType('resource')].map(entry=>({url:entry.name,initiatorType:entry.initiatorType,startTime:entry.startTime,duration:entry.duration,transferSize:entry.transferSize,encodedBodySize:entry.encodedBodySize,decodedBodySize:entry.decodedBodySize}));const urls=[...new Set([...entries.map(entry=>entry.url),...applications.map(item=>item.url)])];const resources=urls.map(url=>({url,requestModules:[...new Set(entries.filter(entry=>entry.url===url).map(entry=>`performance:${entry.initiatorType}`))],performance:entries.filter(entry=>entry.url===url),renderApplications:applications.filter(item=>item.url===url)}));return JSON.stringify({schema:'iwesun.webview2.resource-container-map/1.1',capturedAt:new Date().toISOString(),resources,applications})})()
            """);
        var snapshot = JsonSerializer.Deserialize<string>(encoded) ?? "{}";
        await File.WriteAllTextAsync(Path.Combine(directory, "resource-container-map.json"),
            snapshot, Utf8NoBom, cancellationToken);
        return snapshot;
    }

    private static IReadOnlyCollection<string> CollectLinkedResourceUrls(
        string documentUrl,
        string resourceApplicationMap,
        string requestDomApplication)
    {
        var urls = new HashSet<string>(StringComparer.Ordinal);
        AddAbsoluteUrl(urls, documentUrl);
        using (var document = JsonDocument.Parse(resourceApplicationMap))
        {
            if (document.RootElement.TryGetProperty("resources", out var resources))
                foreach (var resource in resources.EnumerateArray())
                {
                    var url = ReadString(resource, "url");
                    AddAbsoluteUrl(urls, url);
                }
        }
        using (var document = JsonDocument.Parse(requestDomApplication))
        {
            if (document.RootElement.TryGetProperty("frames", out var frames))
                foreach (var frame in frames.EnumerateArray())
                foreach (var record in frame.GetProperty("records").EnumerateArray())
                {
                    var url = ReadString(record, "url");
                    AddAbsoluteUrl(urls, url);
                }
        }
        return urls;
    }

    private static void AddAbsoluteUrl(HashSet<string> urls, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return;
        urls.Add(new UriBuilder(uri) { Fragment = string.Empty }.Uri.AbsoluteUri);
    }

    private static async Task CaptureCdpDomAsync(CoreWebView2 browser, string directory, CancellationToken cancellationToken)
    {
        var snapshot = await browser.CallDevToolsProtocolMethodAsync("DOMSnapshot.captureSnapshot",
            "{\"computedStyles\":[],\"includeDOMRects\":true,\"includePaintOrder\":true,\"includeBlendedBackgroundColors\":true,\"includeTextColorOpacities\":true}");
        var tree = await browser.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\":-1,\"pierce\":true}");
        await File.WriteAllTextAsync(Path.Combine(directory, "dom-snapshot.json"), snapshot, Utf8NoBom, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "cdp-dom-tree.json"), tree, Utf8NoBom, cancellationToken);
    }

    private static async Task<string> CaptureListenersAsync(CoreWebView2 browser)
    {
        var result = new List<object>();
        foreach (var target in new[] { (Expression: "window", Name: "/window"), (Expression: "document", Name: "/document") })
        {
            var evaluated = await browser.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = target.Expression, objectGroup = "iwesun-evidence", returnByValue = false }));
            using var evaluatedDocument = JsonDocument.Parse(evaluated);
            var objectId = evaluatedDocument.RootElement.GetProperty("result").GetProperty("objectId").GetString();
            if (string.IsNullOrEmpty(objectId)) continue;
            var response = await browser.CallDevToolsProtocolMethodAsync("DOMDebugger.getEventListeners",
                JsonSerializer.Serialize(new { objectId, depth = target.Expression == "document" ? -1 : 0, pierce = target.Expression == "document" }));
            using var document = JsonDocument.Parse(response);
            foreach (var listener in document.RootElement.GetProperty("listeners").EnumerateArray())
                result.Add(new { target = target.Name, type = ReadString(listener, "type"), raw = listener.Clone() });
        }
        await browser.CallDevToolsProtocolMethodAsync("Runtime.releaseObjectGroup", "{\"objectGroup\":\"iwesun-evidence\"}");
        return JsonSerializer.Serialize(new { schema = "iwesun.webview2.event-registry/1.0", count = result.Count, listeners = result }, JsonOptions);
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string SafeToken(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

}
