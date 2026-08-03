using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

public static class WebRuntimeInputActions
{
	public const string MouseMove = "input.mouse.move";
	public const string MouseClick = "input.mouse.click";
	public const string MouseDoubleClick = "input.mouse.doubleClick";
	public const string MouseWheel = "input.mouse.wheel";
	public const string KeyboardType = "input.keyboard.type";
	public const string KeyboardPress = "input.keyboard.press";
	public const string KeyboardShortcut = "input.keyboard.shortcut";
	public const string AmbientStart = "input.ambient.start";
	public const string AmbientStop = "input.ambient.stop";
	public const string AmbientStatus = "input.ambient.status";
}

/// <summary>Standard script-supervision actions exposed by the public WebRuntime contract.</summary>
public static class WebRuntimeScriptActions
{
	public const string Evaluate = "script.evaluate";
	public const string EvalAlias = "eval";
	public const string DomSnapshotCapture = "dom.snapshot.capture";
	public const string DomSnapshotRestoreAndLink = "dom.snapshot.restoreAndLink";
}

/// <summary>Controlled actions for an attached HTTP evidence session.</summary>
public static class WebRuntimeNetworkEvidenceActions
{
	public const string Status = "network.evidence.status";
	public const string Policy = "network.evidence.policy";
	public const string Export = "network.evidence.export";
}

public sealed class WebRuntimeDomDataLink
{
	public string XPath { get; init; } = "";
	public string Operation { get; init; } = "link";
	public string? Name { get; init; }
	public string? DataKey { get; init; }
	public JsonElement? Value { get; init; }
}

public sealed class WebRuntimeDomEventLink
{
	public string XPath { get; init; } = "";
	public string EventType { get; init; } = "click";
	public string EventId { get; init; } = "";
	public bool PreventDefault { get; init; } = true;
	public bool StopPropagation { get; init; } = true;
}

public sealed class WebRuntimeDomLinkPlan
{
	public IReadOnlyList<WebRuntimeDomDataLink> DataLinks { get; init; } = Array.Empty<WebRuntimeDomDataLink>();
	public IReadOnlyList<WebRuntimeDomEventLink> EventLinks { get; init; } = Array.Empty<WebRuntimeDomEventLink>();
}

public sealed class WebRuntimeControlRequest
{
	public string ProgramId { get; init; } = "";
	public string BackendId { get; init; } = "";
	public string Action { get; init; } = "";
	public string? Url { get; init; }
	public string? XPath { get; init; }
	public string? Text { get; init; }
	public string? Script { get; init; }
	public int? X { get; init; }
	public int? Y { get; init; }
	public int? Width { get; init; }
	public int? Height { get; init; }
	public int? DurationMs { get; init; }
	public int? Count { get; init; }
	public int? DeltaX { get; init; }
	public int? DeltaY { get; init; }
	public string? Button { get; init; }
	public string? Key { get; init; }
	public string? Code { get; init; }
	public int? WindowsVirtualKeyCode { get; init; }
	public bool Ctrl { get; init; }
	public bool Shift { get; init; }
	public bool Alt { get; init; }
	public bool Meta { get; init; }
	public int? SlowPrefixChars { get; init; }
	public int? IntervalMinMs { get; init; }
	public int? IntervalMaxMs { get; init; }
	public bool Clear { get; init; }
	public bool IncludeOuterHtml { get; init; }
	public bool IncludeInnerHtml { get; init; }
	public int? MaxHtmlChars { get; init; }
	public Dictionary<string, JsonElement>? Args { get; init; }
}

public sealed class WebRuntimeEventEnvelope
{
	public string EventId { get; init; } = Guid.NewGuid().ToString("N");
	public string? CorrelationId { get; init; }
	public string ProgramId { get; init; } = "";
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
	public string BackendId { get; init; } = "";
	public string Source { get; init; } = "";
	public string Kind { get; init; } = "";
	public JsonElement? Data { get; init; }
}

public sealed class WebRuntimeDiscoveryRequest
{
	public int MaxControls { get; init; } = 160;
	public int MaxBodyTextChars { get; init; } = 1200;
	public bool IncludeHidden { get; init; }
	public bool IncludeForms { get; init; } = true;
	public bool IncludeLinks { get; init; } = true;
	public bool IncludeInputs { get; init; } = true;
}

public sealed class WebRuntimeDiscoveryTarget
{
	public string Kind { get; init; } = "";
	public string Label { get; init; } = "";
	public string Text { get; init; } = "";
	public string XPath { get; init; } = "";
	public int X { get; init; }
	public int Y { get; init; }
	public int W { get; init; }
	public int H { get; init; }
	public bool Visible { get; init; }
	public bool Disabled { get; init; }
	public int Priority { get; init; }
}

public sealed class WebRuntimeDiscoveryResult
{
	public string Title { get; init; } = "";
	public string Url { get; init; } = "";
	public string ReadyState { get; init; } = "";
	public string BodyText { get; init; } = "";
	public IReadOnlyList<WebRuntimeDiscoveryTarget> Targets { get; init; } = Array.Empty<WebRuntimeDiscoveryTarget>();
	public IReadOnlyList<WebRuntimeDiscoveryTarget> Buttons { get; init; } = Array.Empty<WebRuntimeDiscoveryTarget>();
	public IReadOnlyList<WebRuntimeDiscoveryTarget> Links { get; init; } = Array.Empty<WebRuntimeDiscoveryTarget>();
	public IReadOnlyList<WebRuntimeDiscoveryTarget> Inputs { get; init; } = Array.Empty<WebRuntimeDiscoveryTarget>();
	public IReadOnlyList<WebRuntimeDiscoveryTarget> Forms { get; init; } = Array.Empty<WebRuntimeDiscoveryTarget>();
}
