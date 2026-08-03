using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    private const int BrowserEvidenceJsonMaxDepth = 2048;
    private const int StyleProvenanceRequestBatchSize = 4;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonDocumentOptions BrowserEvidenceJsonOptions = new()
    {
        MaxDepth = BrowserEvidenceJsonMaxDepth
    };

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
        await StabilizePresentationAsync(browser, captureOptions, cancellationToken);
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
        Report(captureOptions, "layout-state", "正在抓取布局关系、活动状态、动画和页面样式环境…");
        await CaptureLayoutStateAsync(browser, directory, captureOptions, cancellationToken);
        Report(captureOptions, "style-provenance", "正在逐元素解析原始 CSS 定义、继承和伪元素来源…");
        await CaptureStyleProvenanceAsync(
            browser,
            Path.Combine(directory, "style-provenance.json"),
            captureOptions,
            cancellationToken);
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
                schema = "iwesun.webview2.page-evidence/1.2",
                capturedAt,
                files = new[]
                {
                    "capture-context.json", "live-dom-tree.json", "complete-dom-properties.json", "layout-state.json", "document.html", "frames.json",
                    "cdp-dom-tree.json", "dom-snapshot.json", "maximum-template.css", "style-provenance.json",
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

    public static async Task StabilizePresentationAsync(
        CoreWebView2 browser,
        WebRuntimePageEvidenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        options ??= new WebRuntimePageEvidenceOptions();
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
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("header", out var header))
                    return;
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
                var text = document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("text", out var value)
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
                cdpSections.Add($"/* CDP source: {ReadString(pair.Value, "sourceURL")}; id: {pair.Key} */\n{text}");
            }
        }
        finally
        {
            receiver.DevToolsProtocolEventReceived -= OnStyleSheetAdded;
            try
            {
                await browser.CallDevToolsProtocolMethodAsync(
                    "Runtime.releaseObjectGroup",
                    "{\"objectGroup\":\"iwesun-element-events\"}");
            }
            catch (ArgumentException) { }
            try { await browser.CallDevToolsProtocolMethodAsync("CSS.disable", "{}"); } catch (ArgumentException) { }
            try { await browser.CallDevToolsProtocolMethodAsync("DOM.disable", "{}"); } catch (ArgumentException) { }
        }
        var encoded = await browser.ExecuteScriptAsync("""
            (()=>{const sections=[],seen=new Set();const rules=(root,label)=>{const sheets=[...(root.styleSheets||[]),...(root.adoptedStyleSheets||[])];for(const owner of root.querySelectorAll?root.querySelectorAll('style,link[rel="stylesheet"]'):[])if(owner.sheet)sheets.push(owner.sheet);for(const sheet of sheets){if(seen.has(sheet))continue;seen.add(sheet);try{sections.push(`/* CSSOM source: ${String(sheet.href||label)} */\n${[...sheet.cssRules].map(rule=>rule.cssText).join('\n')}`)}catch(error){sections.push(`/* inaccessible stylesheet: ${String(sheet.href||label)} - ${String(error)} */`)}}};const visit=(root,label)=>{rules(root,label);for(const element of root.querySelectorAll?root.querySelectorAll('*'):[]){if(element.shadowRoot)visit(element.shadowRoot,`${label}#shadow-root`);if(element.localName==='iframe'){try{if(element.contentDocument)visit(element.contentDocument,`${label}/iframe`)}catch{}}}};visit(document,String(location.href||'top'));return `/* iwesun-template-kind: maximum */\n${sections.join('\n\n')}`})()
            """);
        var css = JsonSerializer.Deserialize<string>(encoded) ?? string.Empty;
        await File.WriteAllTextAsync(outputPath, css + "\n\n" + string.Join("\n\n", cdpSections), Utf8NoBom, cancellationToken);
    }

    public static Task CaptureStyleProvenanceAsync(
        CoreWebView2 browser,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        CaptureStyleProvenanceAsync(browser, outputPath, new WebRuntimePageEvidenceOptions(), cancellationToken);

    private static async Task CaptureStyleProvenanceAsync(
        CoreWebView2 browser,
        string outputPath,
        WebRuntimePageEvidenceOptions options,
        CancellationToken cancellationToken)
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
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("header", out var header))
                    return;
                var id = ReadString(header, "styleSheetId");
                if (!string.IsNullOrEmpty(id)) headers[id] = header.Clone();
            }
            catch (JsonException) { }
        }

        receiver.DevToolsProtocolEventReceived += OnStyleSheetAdded;
        try
        {
            await browser.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");
            await browser.CallDevToolsProtocolMethodAsync("CSS.enable", "{}");
            await Task.Delay(250, cancellationToken);

            var treeJson = await browser.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\":-1,\"pierce\":true}");
            using var treeDocument = JsonDocument.Parse(treeJson, BrowserEvidenceJsonOptions);
            var nodes = new List<CdpElementReference>();
            CollectCdpElementReferences(treeDocument.RootElement.GetProperty("root"), string.Empty, nodes);

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteString("schema", "iwesun.webview2.style-provenance/1.2");
            writer.WriteString("absoluteValuesFile", "complete-dom-properties.json");
            writer.WriteString("styleSheetTextFile", "maximum-template.css");
            writer.WriteNumber("elementCount", nodes.Count);
            writer.WritePropertyName("styleSheets");
            writer.WriteStartArray();
            foreach (var header in headers.OrderBy(item => ReadString(item.Value, "sourceURL"), StringComparer.Ordinal)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
                header.Value.WriteTo(writer);
            writer.WriteEndArray();
            var mediaQueriesJson = await browser.CallDevToolsProtocolMethodAsync("CSS.getMediaQueries", "{}");
            using (var mediaQueriesDocument = JsonDocument.Parse(mediaQueriesJson))
            {
                writer.WritePropertyName("mediaQueries");
                mediaQueriesDocument.RootElement.WriteTo(writer);
            }
            writer.WritePropertyName("elements");
            writer.WriteStartArray();

            var failureCount = 0;
            var definitionsByHash = new Dictionary<string, CompactStyleDefinition>(StringComparer.Ordinal);
            var definitions = new List<CompactStyleDefinition>();
            for (var batchStart = 0; batchStart < nodes.Count; batchStart += StyleProvenanceRequestBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchLength = Math.Min(StyleProvenanceRequestBatchSize, nodes.Count - batchStart);
                var requests = new Task<CdpMatchedStylesResult>[batchLength];
                for (var offset = 0; offset < batchLength; offset++)
                    requests[offset] = CaptureMatchedStylesAsync(browser, nodes[batchStart + offset]);
                var results = await Task.WhenAll(requests);

                foreach (var result in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.WriteStartObject();
                    writer.WriteString("path", result.Node.Path);
                    writer.WriteString("tag", result.Node.Tag);
                    writer.WriteNumber("nodeId", result.Node.NodeId);
                    writer.WriteNumber("backendNodeId", result.Node.BackendNodeId);
                    if (result.Error is not null)
                    {
                        failureCount++;
                        writer.WriteString("resolutionStatus", "unresolved");
                        writer.WriteString("error", result.Error);
                    }
                    else
                    {
                        using var matchedDocument = JsonDocument.Parse(result.Json!, BrowserEvidenceJsonOptions);
                        WriteCompactMatchedStyles(writer, matchedDocument.RootElement, definitionsByHash, definitions);
                    }
                    if (result.ListenerError is not null)
                    {
                        writer.WriteString("eventResolutionStatus", "unresolved");
                        writer.WriteString("eventError", result.ListenerError);
                    }
                    else
                    {
                        using var listenerDocument = JsonDocument.Parse(result.ListenerJson!, BrowserEvidenceJsonOptions);
                        writer.WritePropertyName("eventListeners");
                        listenerDocument.RootElement.GetProperty("listeners").WriteTo(writer);
                    }
                    writer.WriteEndObject();

                }

                var completed = batchStart + batchLength;
                await writer.FlushAsync(cancellationToken);
                Report(options, "style-provenance", $"正在解析并去重原始 CSS 定义：{completed}/{nodes.Count}", completed, nodes.Count);
            }
            writer.WriteEndArray();
            writer.WritePropertyName("definitionCatalog");
            writer.WriteStartArray();
            foreach (var definition in definitions)
            {
                writer.WriteStartObject();
                writer.WriteString("id", definition.Id);
                writer.WriteString("kind", definition.Kind);
                writer.WritePropertyName("conditionIds");
                writer.WriteStartArray();
                foreach (var condition in definition.Conditions)
                    writer.WriteStringValue(condition.Id);
                writer.WriteEndArray();
                writer.WritePropertyName("payload");
                definition.Payload.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            var conditions = definitions.SelectMany(definition => definition.Conditions)
                .GroupBy(condition => condition.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(condition => condition.Id, StringComparer.Ordinal)
                .ToArray();
            writer.WritePropertyName("conditionCatalog");
            writer.WriteStartArray();
            foreach (var condition in conditions)
            {
                writer.WriteStartObject();
                writer.WriteString("id", condition.Id);
                writer.WriteString("kind", condition.Kind);
                writer.WriteString("text", condition.Text);
                if (condition.Active.HasValue)
                    writer.WriteBoolean("active", condition.Active.Value);
                writer.WriteString("styleSheetId", condition.StyleSheetId);
                writer.WriteString("sourceRange", condition.SourceRange);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteNumber("definitionCount", definitions.Count);
            writer.WriteNumber("conditionCount", conditions.Length);
            writer.WriteNumber("unresolvedCount", failureCount);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
            Report(options, "style-provenance", $"原始 CSS 定义证据已写入；未解析节点 {failureCount} 个。", nodes.Count, nodes.Count);
        }
        finally
        {
            receiver.DevToolsProtocolEventReceived -= OnStyleSheetAdded;
            try { await browser.CallDevToolsProtocolMethodAsync("CSS.disable", "{}"); } catch (ArgumentException) { }
            try { await browser.CallDevToolsProtocolMethodAsync("DOM.disable", "{}"); } catch (ArgumentException) { }
        }
    }

    private static async Task<CdpMatchedStylesResult> CaptureMatchedStylesAsync(
        CoreWebView2 browser,
        CdpElementReference node)
    {
        try
        {
            var json = await browser.CallDevToolsProtocolMethodAsync(
                "CSS.getMatchedStylesForNode",
                JsonSerializer.Serialize(new { nodeId = node.NodeId }));
            try
            {
                var resolved = await browser.CallDevToolsProtocolMethodAsync(
                    "DOM.resolveNode",
                    JsonSerializer.Serialize(new
                    {
                        nodeId = node.NodeId,
                        objectGroup = "iwesun-element-events"
                    }));
                using var resolvedDocument = JsonDocument.Parse(resolved);
                var objectId = resolvedDocument.RootElement
                    .GetProperty("object")
                    .GetProperty("objectId")
                    .GetString();
                if (string.IsNullOrWhiteSpace(objectId))
                    return new(node, json, null, null, "DOM.resolveNode returned no objectId.");
                var listeners = await browser.CallDevToolsProtocolMethodAsync(
                    "DOMDebugger.getEventListeners",
                    JsonSerializer.Serialize(new { objectId, depth = 0, pierce = false }));
                return new(node, json, null, listeners, null);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return new(node, json, null, null, exception.Message);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return new(node, null, exception.Message, null, "Style resolution failed before event resolution.");
        }
    }

    private static void WriteCompactMatchedStyles(
        Utf8JsonWriter writer,
        JsonElement matchedStyles,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        WriteDefinitionId(writer, matchedStyles, "inlineStyle", "inlineStyleId", "style", definitionsByHash, definitions);
        WriteDefinitionId(writer, matchedStyles, "attributesStyle", "attributesStyleId", "style", definitionsByHash, definitions);
        WriteRuleIds(writer, matchedStyles, "matchedCSSRules", "matchedRuleIds", definitionsByHash, definitions);

        if (matchedStyles.TryGetProperty("inherited", out var inherited) && inherited.ValueKind == JsonValueKind.Array)
        {
            writer.WritePropertyName("inherited");
            writer.WriteStartArray();
            foreach (var entry in inherited.EnumerateArray())
            {
                writer.WriteStartObject();
                WriteDefinitionId(writer, entry, "inlineStyle", "inlineStyleId", "style", definitionsByHash, definitions);
                WriteRuleIds(writer, entry, "matchedCSSRules", "matchedRuleIds", definitionsByHash, definitions);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        WritePseudoElements(writer, matchedStyles, "pseudoElements", definitionsByHash, definitions);
        WriteInheritedPseudoElements(writer, matchedStyles, definitionsByHash, definitions);
        WriteDefinitionIds(writer, matchedStyles, "cssKeyframes", "keyframeIds", "keyframes", definitionsByHash, definitions);
        WriteDefinitionIds(writer, matchedStyles, "cssPropertyRules", "propertyRuleIds", "property-rule", definitionsByHash, definitions);
        WriteDefinitionIds(writer, matchedStyles, "cssPositionFallbackRules", "positionFallbackRuleIds", "position-fallback", definitionsByHash, definitions);

        if (matchedStyles.TryGetProperty("activePositionFallbackIndex", out var fallbackIndex)
            && fallbackIndex.ValueKind == JsonValueKind.Number)
        {
            writer.WritePropertyName("activePositionFallbackIndex");
            fallbackIndex.WriteTo(writer);
        }

        writer.WritePropertyName("additionalDefinitions");
        writer.WriteStartArray();
        foreach (var property in matchedStyles.EnumerateObject())
        {
            if (property.Name is "inlineStyle" or "attributesStyle" or "matchedCSSRules" or "inherited"
                or "pseudoElements" or "inheritedPseudoElements" or "cssKeyframes" or "cssPropertyRules"
                or "cssPositionFallbackRules" or "activePositionFallbackIndex")
                continue;
            writer.WriteStartObject();
            writer.WriteString("name", property.Name);
            writer.WriteString("definitionId", RegisterDefinition($"additional:{property.Name}", property.Value, definitionsByHash, definitions));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteInheritedPseudoElements(
        Utf8JsonWriter writer,
        JsonElement source,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        if (!source.TryGetProperty("inheritedPseudoElements", out var inherited)
            || inherited.ValueKind != JsonValueKind.Array)
            return;
        writer.WritePropertyName("inheritedPseudoElements");
        writer.WriteStartArray();
        foreach (var entry in inherited.EnumerateArray())
        {
            writer.WriteStartObject();
            WritePseudoElements(writer, entry, "pseudoElements", definitionsByHash, definitions);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePseudoElements(
        Utf8JsonWriter writer,
        JsonElement source,
        string propertyName,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        if (!source.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array) return;
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values.EnumerateArray())
        {
            writer.WriteStartObject();
            if (value.TryGetProperty("pseudoType", out var pseudoType) && pseudoType.ValueKind == JsonValueKind.String)
                writer.WriteString("pseudoType", pseudoType.GetString());
            if (value.TryGetProperty("pseudoIdentifier", out var pseudoIdentifier) && pseudoIdentifier.ValueKind == JsonValueKind.String)
                writer.WriteString("pseudoIdentifier", pseudoIdentifier.GetString());
            WriteRuleIds(writer, value, "matches", "ruleIds", definitionsByHash, definitions);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteRuleIds(
        Utf8JsonWriter writer,
        JsonElement source,
        string sourcePropertyName,
        string outputPropertyName,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        if (!source.TryGetProperty(sourcePropertyName, out var matches) || matches.ValueKind != JsonValueKind.Array) return;
        writer.WritePropertyName(outputPropertyName);
        writer.WriteStartArray();
        foreach (var match in matches.EnumerateArray())
        {
            if (!match.TryGetProperty("rule", out var rule) || rule.ValueKind != JsonValueKind.Object) continue;
            writer.WriteStringValue(RegisterDefinition("rule", rule, definitionsByHash, definitions));
        }
        writer.WriteEndArray();
    }

    private static void WriteDefinitionId(
        Utf8JsonWriter writer,
        JsonElement source,
        string sourcePropertyName,
        string outputPropertyName,
        string kind,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        if (!source.TryGetProperty(sourcePropertyName, out var value) || value.ValueKind != JsonValueKind.Object) return;
        writer.WriteString(outputPropertyName, RegisterDefinition(kind, value, definitionsByHash, definitions));
    }

    private static void WriteDefinitionIds(
        Utf8JsonWriter writer,
        JsonElement source,
        string sourcePropertyName,
        string outputPropertyName,
        string kind,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        if (!source.TryGetProperty(sourcePropertyName, out var values) || values.ValueKind != JsonValueKind.Array) return;
        writer.WritePropertyName(outputPropertyName);
        writer.WriteStartArray();
        foreach (var value in values.EnumerateArray())
            writer.WriteStringValue(RegisterDefinition(kind, value, definitionsByHash, definitions));
        writer.WriteEndArray();
    }

    private static string RegisterDefinition(
        string kind,
        JsonElement payload,
        Dictionary<string, CompactStyleDefinition> definitionsByHash,
        List<CompactStyleDefinition> definitions)
    {
        var raw = payload.GetRawText();
        var input = Encoding.UTF8.GetBytes(kind + "\0" + raw);
        var hash = Convert.ToHexString(SHA256.HashData(input));
        if (definitionsByHash.TryGetValue(hash, out var existing)) return existing.Id;
        var definition = new CompactStyleDefinition(
            $"d{definitions.Count + 1:D6}",
            kind,
            payload.Clone(),
            ReadDefinitionConditions(payload));
        definitionsByHash.Add(hash, definition);
        definitions.Add(definition);
        return definition.Id;
    }

    private static IReadOnlyList<CompactStyleCondition> ReadDefinitionConditions(JsonElement payload)
    {
        var result = new List<CompactStyleCondition>();
        if (payload.ValueKind != JsonValueKind.Object)
            return result;
        AddConditions(payload, "media", "media", result);
        AddConditions(payload, "containerQueries", "container", result);
        AddConditions(payload, "supports", "supports", result);
        AddConditions(payload, "scopes", "scope", result);
        return result;
    }

    private static void AddConditions(
        JsonElement payload,
        string propertyName,
        string kind,
        ICollection<CompactStyleCondition> result)
    {
        if (!payload.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
            return;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
                continue;
            var text = ReadString(value, "text");
            if (text.Length == 0) text = ReadString(value, "conditionText");
            if (text.Length == 0) continue;
            bool? active = null;
            if (value.TryGetProperty("active", out var activeValue)
                && activeValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                active = activeValue.GetBoolean();
            else if (value.TryGetProperty("mediaList", out var mediaList)
                && mediaList.ValueKind == JsonValueKind.Array)
            {
                var states = mediaList.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("active", out var itemActive)
                        && itemActive.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    .Select(item => item.GetProperty("active").GetBoolean())
                    .ToArray();
                if (states.Length > 0) active = states.All(state => state);
            }
            var styleSheetId = ReadString(value, "styleSheetId");
            var sourceRange = value.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object
                ? range.GetRawText()
                : string.Empty;
            var identity = $"{kind}\0{text}\0{styleSheetId}\0{sourceRange}";
            var id = "c" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
            result.Add(new CompactStyleCondition(id, kind, text, active, styleSheetId, sourceRange));
        }
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
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("scriptId", out var id)
                    && !string.IsNullOrEmpty(id.GetString()))
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
                var source = sourceDocument.RootElement.ValueKind == JsonValueKind.Object
                    && sourceDocument.RootElement.TryGetProperty("scriptSource", out var value)
                    ? value.GetString() ?? string.Empty
                    : string.Empty;
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
                    (()=>{const capture=globalThis.__iwesunCompleteDomCapture||{entries:[],names:[]};const values=s=>capture.names.map(name=>s.getPropertyValue(name));const pseudo=(e,name)=>{const s=getComputedStyle(e,name),content=s.getPropertyValue('content');if(name!=='::marker'&&(content==='none'||content==='normal'))return null;if(name==='::marker'&&getComputedStyle(e).display!=='list-item')return null;return values(s)};const propertyNames=['value','checked','selected','disabled','readOnly','open','currentSrc','naturalWidth','naturalHeight','complete','selectedIndex','index','width','height','scrollLeft','scrollTop','scrollWidth','scrollHeight','clientLeft','clientTop','clientWidth','clientHeight','offsetLeft','offsetTop','offsetWidth','offsetHeight','tabIndex','selectionStart','selectionEnd','currentTime','duration','paused','readyState','videoWidth','videoHeight'];return JSON.stringify(capture.entries.slice({{{{offset}}}},{{{{offset + chunkSize}}}}).map(entry=>{const e=entry.element,s=getComputedStyle(e),r=e.getBoundingClientRect(),properties={};for(const name of propertyNames){try{const value=e[name];if(value===null||['string','number','boolean'].includes(typeof value))properties[name]=value}catch{}}return{scope:entry.scope,path:entry.path,tag:e.localName,attributes:Object.fromEntries([...e.attributes].map(a=>[a.name,a.value])),properties,styleValues:values(s),pseudo:{before:pseudo(e,'::before'),after:pseudo(e,'::after'),marker:pseudo(e,'::marker')},rect:[r.x,r.y,r.width,r.height],visible:!!(r.width||r.height||e.getClientRects().length)}}))})()
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

    private static async Task CaptureLayoutStateAsync(
        CoreWebView2 browser,
        string directory,
        WebRuntimePageEvidenceOptions options,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 100;
        var preparationJson = await browser.ExecuteScriptAsync("""
            (()=>{const entries=[],paths=new WeakMap(),conditionalRules=[],seenSheets=new Set();const semanticNames=['display','position','inset','top','right','bottom','left','z-index','float','clear','box-sizing','width','height','min-width','min-height','max-width','max-height','aspect-ratio','margin','margin-top','margin-right','margin-bottom','margin-left','padding','padding-top','padding-right','padding-bottom','padding-left','overflow','overflow-x','overflow-y','visibility','opacity','order','flex','flex-flow','flex-direction','flex-wrap','flex-grow','flex-shrink','flex-basis','grid','grid-template','grid-template-columns','grid-template-rows','grid-template-areas','grid-auto-flow','grid-auto-columns','grid-auto-rows','grid-column','grid-row','gap','row-gap','column-gap','place-content','align-content','justify-content','place-items','align-items','justify-items','place-self','align-self','justify-self','writing-mode','direction','text-orientation','contain','contain-intrinsic-size','content-visibility','container','container-name','container-type','transform','transform-origin','translate','rotate','scale','perspective','perspective-origin','clip','clip-path','object-fit','object-position','table-layout','columns','column-width','column-count','break-before','break-after','break-inside'];const path=e=>{const parts=[];for(let current=e;current?.nodeType===1;current=current.parentElement){const peers=current.parentElement?[...current.parentElement.children].filter(x=>x.localName===current.localName):[];parts.unshift(peers.length>1?`${current.localName}[${peers.indexOf(current)+1}]`:current.localName)}return'/'+parts.join('/')};const scanRules=(rules,scope,source,prefix='')=>{for(let index=0;index<(rules?.length||0);index++){const rule=rules[index],indexPath=prefix?`${prefix}.${index}`:String(index),kind=rule.constructor?.name||'CSSRule',condition=String(rule.conditionText||'');let matches=null;if(kind==='CSSMediaRule'){try{matches=matchMedia(condition).matches}catch{}}else if(kind==='CSSSupportsRule'){try{matches=CSS.supports(condition)}catch{}}conditionalRules.push({scope,source,indexPath,kind,conditionText:condition||null,matches,name:String(rule.name||''),selectorText:String(rule.selectorText||''),layerName:String(rule.layerName||'')});if(rule.cssRules)scanRules(rule.cssRules,scope,source,indexPath)}};const scanSheets=(root,scope)=>{const linked=[...(root.styleSheets||[]),...(root.adoptedStyleSheets||[])];for(const owner of root.querySelectorAll?root.querySelectorAll(':scope > style,:scope > link[rel="stylesheet"]'):[])if(owner.sheet)linked.push(owner.sheet);for(const sheet of linked){if(seenSheets.has(sheet))continue;seenSheets.add(sheet);const source=String(sheet.href||`${scope}:inline`);try{scanRules(sheet.cssRules,scope,source)}catch(error){conditionalRules.push({scope,source,kind:'StyleSheetAccessError',error:String(error)})}}};const walk=(root,scope)=>{scanSheets(root,scope||'/document');for(const e of root.querySelectorAll?root.querySelectorAll('*'):[]){const elementPath=`${scope}${path(e)}`;entries.push({element:e,path:elementPath,scope});paths.set(e,elementPath);if(e.shadowRoot)walk(e.shadowRoot,`${elementPath}#shadow-root`);if(e.localName==='iframe'){try{if(e.contentDocument)walk(e.contentDocument,`${elementPath}#document`)}catch{}}}};walk(document,'');const mediaQueries=['(prefers-color-scheme: dark)','(prefers-color-scheme: light)','(prefers-reduced-motion: reduce)','(prefers-contrast: more)','(forced-colors: active)','(inverted-colors: inverted)','(pointer: fine)','(pointer: coarse)','(any-pointer: fine)','(any-pointer: coarse)','(hover: hover)','(any-hover: hover)','(orientation: portrait)','(orientation: landscape)'];const environment={url:location.href,viewport:{innerWidth,innerHeight,devicePixelRatio,visualViewport:visualViewport?{width:visualViewport.width,height:visualViewport.height,scale:visualViewport.scale}:null},document:{compatMode:document.compatMode,language:document.documentElement.lang||'',direction:getComputedStyle(document.documentElement).direction,activeElementPath:paths.get(document.activeElement)||null,targetId:location.hash||null},navigator:{language:navigator.language,languages:[...(navigator.languages||[])],platform:navigator.platform,maxTouchPoints:navigator.maxTouchPoints},media:mediaQueries.map(query=>({query,matches:matchMedia(query).matches})),conditionalRules};globalThis.__iwesunLayoutStateCapture={entries,paths,semanticNames};return JSON.stringify({count:entries.length,environment})})()
            """);
        var preparationEncoded = JsonSerializer.Deserialize<string>(preparationJson) ?? "{}";
        using var preparation = JsonDocument.Parse(preparationEncoded);
        var total = preparation.RootElement.GetProperty("count").GetInt32();
        var environment = preparation.RootElement.GetProperty("environment").Clone();
        var outputPath = Path.Combine(directory, "layout-state.json");
        await using var output = new StreamWriter(outputPath, append: false, Utf8NoBom);
        await output.WriteAsync("{\"schema\":\"iwesun.webview2.layout-state/1.0\",\"environment\":".AsMemory(), cancellationToken);
        await output.WriteAsync(environment.GetRawText().AsMemory(), cancellationToken);
        await output.WriteAsync(",\"elements\":[".AsMemory(), cancellationToken);
        var wroteAny = false;
        try
        {
            for (var offset = 0; offset < total; offset += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkResult = await browser.ExecuteScriptAsync($$$$"""
                    (()=>{const capture=globalThis.__iwesunLayoutStateCapture||{entries:[],paths:new WeakMap(),semanticNames:[]};const lookup=node=>node?(capture.paths.get(node)||null):null;const scalar=value=>value===null||['string','number','boolean'].includes(typeof value)?value:String(value);const stateNames=['hover','active','focus','focus-visible','focus-within','target','checked','indeterminate','disabled','enabled','required','optional','valid','invalid','user-valid','user-invalid','in-range','out-of-range','read-only','read-write','placeholder-shown','default','open','defined','fullscreen','modal','popover-open'];const states=e=>Object.fromEntries(stateNames.map(name=>{try{return[name,e.matches(`:${name}`)]}catch{return[name,null]}}));const quads=e=>{if(typeof e.getBoxQuads!=='function')return null;const result={};for(const box of ['margin','border','padding','content']){try{result[box]=e.getBoxQuads({box}).map(q=>[q.p1.x,q.p1.y,q.p2.x,q.p2.y,q.p3.x,q.p3.y,q.p4.x,q.p4.y])}catch{result[box]=null}}return result};const animations=e=>{let list=[];try{list=e.getAnimations?e.getAnimations():[]}catch{}return list.map(animation=>{const effect=animation.effect;let timing=null,computedTiming=null,keyframes=null;try{timing=effect?.getTiming?.()||null}catch{}try{computedTiming=effect?.getComputedTiming?.()||null}catch{}try{keyframes=effect?.getKeyframes?.()||null}catch{}return{animationName:String(animation.animationName||''),transitionProperty:String(animation.transitionProperty||''),playState:animation.playState,pending:animation.pending,replaceState:animation.replaceState,currentTime:scalar(animation.currentTime),startTime:scalar(animation.startTime),playbackRate:animation.playbackRate,timelineCurrentTime:scalar(animation.timeline?.currentTime),timing,computedTiming,keyframes}})};return JSON.stringify(capture.entries.slice({{{{offset}}}},{{{{offset + chunkSize}}}}).map(entry=>{const e=entry.element,s=getComputedStyle(e),semanticStyles=Object.fromEntries(capture.semanticNames.map(name=>[name,s.getPropertyValue(name)]));const root=e.getRootNode?.();const rects=[...e.getClientRects()].map(r=>[r.x,r.y,r.width,r.height]);return{path:entry.path,scope:entry.scope,tag:e.localName,relationships:{parent:lookup(e.parentElement),offsetParent:lookup(e.offsetParent),rootHost:lookup(root?.host),assignedSlot:lookup(e.assignedSlot)},authored:{className:String(e.className||''),inlineStyle:e.getAttribute?.('style'),part:e.getAttribute?.('part'),slot:e.getAttribute?.('slot')},semanticStyles,states:states(e),scroll:{left:e.scrollLeft,top:e.scrollTop,width:e.scrollWidth,height:e.scrollHeight,clientWidth:e.clientWidth,clientHeight:e.clientHeight},clientRects:rects,boxQuads:quads(e),animations:animations(e)}}))})()
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
                Report(options, "layout-state", $"正在抓取布局关系和活动状态：{completed}/{total}", completed, total);
            }
            await output.WriteAsync("]}".AsMemory(), cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            try { await browser.ExecuteScriptAsync("delete globalThis.__iwesunLayoutStateCapture;true"); }
            catch (InvalidOperationException) { }
        }
        Report(options, "layout-state", "布局关系、活动状态、动画和样式环境已写入。", total, total);
    }

    private static async Task<string> CaptureResourceApplicationMapAsync(CoreWebView2 browser, string directory, CancellationToken cancellationToken)
    {
        var encoded = await browser.ExecuteScriptAsync("""
            (()=>{
              const applications=[];
              const path=e=>{const parts=[];for(let current=e;current?.nodeType===1;current=current.parentElement){const peers=current.parentElement?[...current.parentElement.children].filter(x=>x.localName===current.localName):[];parts.unshift(peers.length>1?`${current.localName}[${peers.indexOf(current)+1}]`:current.localName)}return'/'+parts.join('/')};
              const resolve=(raw,doc)=>{try{return new URL(raw,doc.baseURI).href}catch{return raw}};
              const cssUrls=value=>{const result=[];if(!value||value==='none')return result;const expression=/url\(\s*(["']?)(.*?)\1\s*\)/g;for(let match;(match=expression.exec(value));)if(match[2])result.push(match[2]);return result};
              const visit=(root,scope)=>{for(const e of root.querySelectorAll?root.querySelectorAll('*'):[]){
                const elementPath=`${scope}${path(e)}`;
                const container=e.closest?.('[role],main,aside,nav,header,footer,section,article,[class]')||e.parentElement;
                const renderContainer={path:container?`${scope}${path(container)}`:'',tag:container?.localName||'',role:container?.getAttribute?.('role')||'',id:container?.id||'',className:String(container?.className||'')};
                const add=(sourceKind,property,rawValue,urlValue)=>{if(!rawValue&&!urlValue)return;const url=urlValue||resolve(rawValue,e.ownerDocument);applications.push({scope,requestModule:`${sourceKind}:${e.localName}:${property}`,sourceKind,elementPath,tag:e.localName,property,attribute:sourceKind==='dom-attribute'?property:'',rawValue:rawValue||'',url,urlKind:url.startsWith('data:')?'inline-data':url.startsWith('blob:')?'blob':/^https?:/i.test(url)?'http':'other',elementMetadata:{rel:e.getAttribute?.('rel')||'',as:e.getAttribute?.('as')||'',type:e.getAttribute?.('type')||'',media:e.getAttribute?.('media')||'',disabled:!!e.disabled},renderContainer})};
                const attributes=['src','xlink:href'];if(['link','use','image','feimage'].includes(e.localName))attributes.push('href');if(e.localName==='object')attributes.push('data');if(e.localName==='video')attributes.push('poster');for(const attribute of attributes){const raw=e.getAttribute?.(attribute);if(raw)add('dom-attribute',attribute,raw,resolve(raw,e.ownerDocument))}
                if(e.localName==='img'){const raw=e.getAttribute('src')||'';add('responsive-image','currentSrc',raw,e.currentSrc||e.src||'')}if(['img','source'].includes(e.localName)){const srcset=e.getAttribute('srcset')||'';for(const candidate of srcset.split(',').map(value=>value.trim()).filter(Boolean)){const candidateUrl=candidate.split(/\s+/)[0];add('responsive-image-candidate','srcset',candidate,resolve(candidateUrl,e.ownerDocument))}}
                const collectStyle=(style,kind,prefix='')=>{for(const property of ['backgroundImage','maskImage','webkitMaskImage','content','cursor','listStyleImage','borderImageSource'])for(const raw of cssUrls(style[property]))add(kind,`${prefix}${property}`,style[property],resolve(raw,e.ownerDocument))};
                collectStyle(e.ownerDocument.defaultView.getComputedStyle(e),'css-computed');
                for(const pseudo of ['::before','::after','::marker'])try{collectStyle(e.ownerDocument.defaultView.getComputedStyle(e,pseudo),'css-pseudo',`${pseudo}:`)}catch{}
                if(e.shadowRoot)visit(e.shadowRoot,`${scope}${path(e)}#shadow-root`);
                if(e.localName==='iframe')try{if(e.contentDocument)visit(e.contentDocument,`${scope}${path(e)}#document`)}catch{}
              }};
              visit(document,'top');
              const entries=[...performance.getEntriesByType('resource')].map(entry=>({url:entry.name,initiatorType:entry.initiatorType,startTime:entry.startTime,duration:entry.duration,transferSize:entry.transferSize,encodedBodySize:entry.encodedBodySize,decodedBodySize:entry.decodedBodySize}));
              const urls=[...new Set([...entries.map(entry=>entry.url),...applications.map(item=>item.url)])];
              const resources=urls.map(url=>({url,requestModules:[...new Set(entries.filter(entry=>entry.url===url).map(entry=>`performance:${entry.initiatorType}`))],performance:entries.filter(entry=>entry.url===url),renderApplications:applications.filter(item=>item.url===url)}));
              return JSON.stringify({schema:'iwesun.webview2.resource-container-map/1.2',capturedAt:new Date().toISOString(),resources,applications});
            })()
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
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return;
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

    private static void CollectCdpElementReferences(
        JsonElement node,
        string scope,
        List<CdpElementReference> result,
        string? elementPath = null)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        var nodeType = node.TryGetProperty("nodeType", out var nodeTypeValue) ? nodeTypeValue.GetInt32() : 0;
        var localName = ReadString(node, "localName");
        var currentPath = scope;
        var isPseudoElement = node.TryGetProperty("pseudoType", out _);
        if (nodeType == 1 && !string.IsNullOrEmpty(localName) && !isPseudoElement)
        {
            currentPath = elementPath ?? $"{scope}/{localName}";
            var nodeId = node.TryGetProperty("nodeId", out var nodeIdValue) ? nodeIdValue.GetInt32() : 0;
            var backendNodeId = node.TryGetProperty("backendNodeId", out var backendNodeIdValue)
                ? backendNodeIdValue.GetInt32()
                : 0;
            if (nodeId != 0)
                result.Add(new CdpElementReference(currentPath, localName, nodeId, backendNodeId));
        }

        CollectCdpChildren(node, currentPath, result);

        if (node.TryGetProperty("shadowRoots", out var shadowRoots) && shadowRoots.ValueKind == JsonValueKind.Array)
            foreach (var shadowRoot in shadowRoots.EnumerateArray())
                CollectCdpDocumentChildren(shadowRoot, $"{currentPath}#shadow-root", result);

        if (node.TryGetProperty("contentDocument", out var contentDocument) && contentDocument.ValueKind == JsonValueKind.Object)
            CollectCdpDocumentChildren(contentDocument, $"{currentPath}#document", result);
    }

    private static void CollectCdpDocumentChildren(
        JsonElement documentNode,
        string scope,
        List<CdpElementReference> result)
    {
        if (!documentNode.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return;
        foreach (var child in children.EnumerateArray())
        {
            var childPath = IsRegularCdpElement(child) ? $"{scope}/{ReadString(child, "localName")}" : null;
            CollectCdpElementReferences(child, scope, result, childPath);
        }
    }

    private static void CollectCdpChildren(
        JsonElement parentNode,
        string scope,
        List<CdpElementReference> result)
    {
        if (!parentNode.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return;

        var elementNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in children.EnumerateArray())
        {
            if (!IsRegularCdpElement(child)) continue;
            var childName = ReadString(child, "localName");
            elementNameCounts[childName] = elementNameCounts.GetValueOrDefault(childName) + 1;
        }

        var elementNameIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in children.EnumerateArray())
        {
            string? childPath = null;
            if (IsRegularCdpElement(child))
            {
                var childName = ReadString(child, "localName");
                var childIndex = elementNameIndexes.GetValueOrDefault(childName) + 1;
                elementNameIndexes[childName] = childIndex;
                var segment = elementNameCounts[childName] > 1 ? $"{childName}[{childIndex}]" : childName;
                childPath = $"{scope}/{segment}";
            }
            CollectCdpElementReferences(child, scope, result, childPath);
        }
    }

    private static bool IsRegularCdpElement(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty("nodeType", out var nodeType)
        && nodeType.GetInt32() == 1
        && !string.IsNullOrEmpty(ReadString(node, "localName"))
        && !node.TryGetProperty("pseudoType", out _);

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string SafeToken(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

    private sealed record CdpElementReference(string Path, string Tag, int NodeId, int BackendNodeId);
    private sealed record CdpMatchedStylesResult(
        CdpElementReference Node,
        string? Json,
        string? Error,
        string? ListenerJson,
        string? ListenerError);
    private sealed record CompactStyleDefinition(
        string Id,
        string Kind,
        JsonElement Payload,
        IReadOnlyList<CompactStyleCondition> Conditions);
    private sealed record CompactStyleCondition(
        string Id,
        string Kind,
        string Text,
        bool? Active,
        string StyleSheetId,
        string SourceRange);

}
