namespace Iwesun.Runtime.Web;

public enum HtmlEventSpecification
{
	HtmlLivingStandard,
	UiEvents,
	PointerEvents,
	TouchEvents,
	CssAnimations,
	CssTransitions,
	Fullscreen,
	Selection,
	BrowserCompatibility
}

public sealed record HtmlEventHandlerDefinition(
	string AttributeName,
	string EventName,
	DomEventKind DomKind,
	HtmlEventSpecification Specification,
	bool BodyOnly,
	EventTranslationKind Translation,
	XamlEventKind XamlKind);

public static class HtmlEventHandlerCatalog
{
	private static readonly HtmlEventHandlerDefinition[] Global =
	[
		Html("auxclick", DomEventKind.AuxClick), Html("beforeinput", DomEventKind.BeforeInput),
		Html("beforematch", DomEventKind.BeforeMatch), Html("beforetoggle", DomEventKind.BeforeToggle),
		Html("blur", DomEventKind.Blur, XamlEventKind.LostFocus),
		Html("cancel", DomEventKind.Cancel), Html("canplay", DomEventKind.CanPlay),
		Html("canplaythrough", DomEventKind.CanPlayThrough), Html("change", DomEventKind.Change),
		Html("click", DomEventKind.Click, XamlEventKind.Click, EventTranslationKind.Direct),
		Html("close", DomEventKind.Close, XamlEventKind.Closed),
		Html("command", DomEventKind.Command), Html("contextlost", DomEventKind.ContextLost),
		Html("contextmenu", DomEventKind.ContextMenu, XamlEventKind.ContextRequested),
		Html("contextrestored", DomEventKind.ContextRestored), Html("copy", DomEventKind.Copy),
		Html("cuechange", DomEventKind.CueChange), Html("cut", DomEventKind.Cut),
		Html("dblclick", DomEventKind.DoubleClick, XamlEventKind.DoubleTapped),
		Html("drag", DomEventKind.Drag), Html("dragend", DomEventKind.DragEnd),
		Html("dragenter", DomEventKind.DragEnter, XamlEventKind.DragEnter),
		Html("dragleave", DomEventKind.DragLeave, XamlEventKind.DragLeave),
		Html("dragover", DomEventKind.DragOver, XamlEventKind.DragOver),
		Html("dragstart", DomEventKind.DragStart, XamlEventKind.DragStarting),
		Html("drop", DomEventKind.Drop, XamlEventKind.Drop, EventTranslationKind.Direct),
		Html("durationchange", DomEventKind.DurationChange), Html("emptied", DomEventKind.Emptied),
		Html("ended", DomEventKind.Ended, XamlEventKind.MediaEnded),
		Html("error", DomEventKind.Error, XamlEventKind.MediaFailed),
		Html("focus", DomEventKind.Focus, XamlEventKind.GotFocus),
		Html("formdata", DomEventKind.FormData), Html("input", DomEventKind.Input, XamlEventKind.TextChanging),
		Html("invalid", DomEventKind.Invalid), Html("keydown", DomEventKind.KeyDown, XamlEventKind.KeyDown),
		Html("keypress", DomEventKind.KeyPress, XamlEventKind.CharacterReceived),
		Html("keyup", DomEventKind.KeyUp, XamlEventKind.KeyUp),
		Html("load", DomEventKind.Load, XamlEventKind.Loaded),
		Html("loadeddata", DomEventKind.LoadedData), Html("loadedmetadata", DomEventKind.LoadedMetadata),
		Html("loadstart", DomEventKind.LoadStart), Html("mousedown", DomEventKind.MouseDown, XamlEventKind.PointerPressed),
		Html("mouseenter", DomEventKind.MouseEnter, XamlEventKind.PointerEntered),
		Html("mouseleave", DomEventKind.MouseLeave, XamlEventKind.PointerExited),
		Html("mousemove", DomEventKind.MouseMove, XamlEventKind.PointerMoved),
		Html("mouseout", DomEventKind.MouseOut, XamlEventKind.PointerExited),
		Html("mouseover", DomEventKind.MouseOver, XamlEventKind.PointerEntered),
		Html("mouseup", DomEventKind.MouseUp, XamlEventKind.PointerReleased),
		Html("paste", DomEventKind.Paste), Html("pause", DomEventKind.Pause),
		Html("play", DomEventKind.Play), Html("playing", DomEventKind.Playing),
		Html("progress", DomEventKind.Progress), Html("ratechange", DomEventKind.RateChange),
		Html("reset", DomEventKind.Reset), Html("resize", DomEventKind.Resize, XamlEventKind.SizeChanged),
		Html("scroll", DomEventKind.Scroll), Html("scrollend", DomEventKind.ScrollEnd),
		Html("securitypolicyviolation", DomEventKind.SecurityPolicyViolation),
		Html("seeked", DomEventKind.Seeked), Html("seeking", DomEventKind.Seeking),
		Html("select", DomEventKind.Select, XamlEventKind.SelectionChanged),
		Html("slotchange", DomEventKind.SlotChange), Html("stalled", DomEventKind.Stalled),
		Html("submit", DomEventKind.Submit), Html("suspend", DomEventKind.Suspend),
		Html("timeupdate", DomEventKind.TimeUpdate),
		Html("toggle", DomEventKind.Toggle, XamlEventKind.Toggled),
		Html("volumechange", DomEventKind.VolumeChange), Html("waiting", DomEventKind.Waiting),
		Html("wheel", DomEventKind.Wheel, XamlEventKind.PointerWheelChanged),
		External("animationcancel", DomEventKind.AnimationCancel, HtmlEventSpecification.CssAnimations),
		External("animationend", DomEventKind.AnimationEnd, HtmlEventSpecification.CssAnimations),
		External("animationiteration", DomEventKind.AnimationIteration, HtmlEventSpecification.CssAnimations),
		External("animationstart", DomEventKind.AnimationStart, HtmlEventSpecification.CssAnimations),
		External("compositionend", DomEventKind.CompositionEnd, HtmlEventSpecification.UiEvents),
		External("compositionstart", DomEventKind.CompositionStart, HtmlEventSpecification.UiEvents),
		External("compositionupdate", DomEventKind.CompositionUpdate, HtmlEventSpecification.UiEvents),
		External("focusin", DomEventKind.FocusIn, HtmlEventSpecification.UiEvents),
		External("focusout", DomEventKind.FocusOut, HtmlEventSpecification.UiEvents),
		External("fullscreenchange", DomEventKind.FullscreenChange, HtmlEventSpecification.Fullscreen),
		External("fullscreenerror", DomEventKind.FullscreenError, HtmlEventSpecification.Fullscreen),
		External("gotpointercapture", DomEventKind.GotPointerCapture, HtmlEventSpecification.PointerEvents),
		External("lostpointercapture", DomEventKind.LostPointerCapture, HtmlEventSpecification.PointerEvents),
		External("pointercancel", DomEventKind.PointerCancel, HtmlEventSpecification.PointerEvents),
		External("pointerdown", DomEventKind.PointerDown, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerPressed),
		External("pointerenter", DomEventKind.PointerEnter, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerEntered),
		External("pointerleave", DomEventKind.PointerLeave, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerExited),
		External("pointermove", DomEventKind.PointerMove, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerMoved),
		External("pointerout", DomEventKind.PointerOut, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerExited),
		External("pointerover", DomEventKind.PointerOver, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerEntered),
		External("pointerrawupdate", DomEventKind.PointerRawUpdate, HtmlEventSpecification.PointerEvents),
		External("pointerup", DomEventKind.PointerUp, HtmlEventSpecification.PointerEvents, XamlEventKind.PointerReleased),
		External("selectionchange", DomEventKind.SelectionChange, HtmlEventSpecification.Selection),
		External("selectstart", DomEventKind.SelectStart, HtmlEventSpecification.Selection),
		External("touchcancel", DomEventKind.TouchCancel, HtmlEventSpecification.TouchEvents),
		External("touchend", DomEventKind.TouchEnd, HtmlEventSpecification.TouchEvents),
		External("touchmove", DomEventKind.TouchMove, HtmlEventSpecification.TouchEvents),
		External("touchstart", DomEventKind.TouchStart, HtmlEventSpecification.TouchEvents),
		External("transitioncancel", DomEventKind.TransitionCancel, HtmlEventSpecification.CssTransitions),
		External("transitionend", DomEventKind.TransitionEnd, HtmlEventSpecification.CssTransitions),
		External("transitionrun", DomEventKind.TransitionRun, HtmlEventSpecification.CssTransitions),
		External("transitionstart", DomEventKind.TransitionStart, HtmlEventSpecification.CssTransitions),
		External("webkitanimationend", DomEventKind.WebKitAnimationEnd, HtmlEventSpecification.BrowserCompatibility),
		External("webkitanimationiteration", DomEventKind.WebKitAnimationIteration, HtmlEventSpecification.BrowserCompatibility),
		External("webkitanimationstart", DomEventKind.WebKitAnimationStart, HtmlEventSpecification.BrowserCompatibility),
		External("webkittransitionend", DomEventKind.WebKitTransitionEnd, HtmlEventSpecification.BrowserCompatibility)
	];

	private static readonly HtmlEventHandlerDefinition[] Body =
	[
		BodyEvent("afterprint", DomEventKind.AfterPrint), BodyEvent("beforeprint", DomEventKind.BeforePrint),
		BodyEvent("beforeunload", DomEventKind.BeforeUnload), BodyEvent("hashchange", DomEventKind.HashChange),
		BodyEvent("languagechange", DomEventKind.LanguageChange), BodyEvent("message", DomEventKind.Message),
		BodyEvent("messageerror", DomEventKind.MessageError), BodyEvent("offline", DomEventKind.Offline),
		BodyEvent("online", DomEventKind.Online), BodyEvent("pagehide", DomEventKind.PageHide),
		BodyEvent("pagereveal", DomEventKind.PageReveal), BodyEvent("pageshow", DomEventKind.PageShow),
		BodyEvent("pageswap", DomEventKind.PageSwap), BodyEvent("popstate", DomEventKind.PopState),
		BodyEvent("rejectionhandled", DomEventKind.RejectionHandled), BodyEvent("storage", DomEventKind.Storage),
		BodyEvent("unhandledrejection", DomEventKind.UnhandledRejection), BodyEvent("unload", DomEventKind.Unload)
	];

	public static IReadOnlyList<HtmlEventHandlerDefinition> GetSupported(string tagName) =>
		tagName.Equals("body", StringComparison.OrdinalIgnoreCase)
			? [.. Global, .. Body]
			: Global;

	public static bool TryGet(
		string tagName,
		string attributeName,
		out HtmlEventHandlerDefinition? definition)
	{
		definition = GetSupported(tagName).FirstOrDefault(item =>
			item.AttributeName.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
		return definition is not null;
	}

	private static HtmlEventHandlerDefinition Html(
		string name,
		DomEventKind kind,
		XamlEventKind xaml = XamlEventKind.Custom,
		EventTranslationKind translation = EventTranslationKind.Semantic) =>
		new($"on{name}", name, kind, HtmlEventSpecification.HtmlLivingStandard, false, translation, xaml);

	private static HtmlEventHandlerDefinition BodyEvent(string name, DomEventKind kind) =>
		new($"on{name}", name, kind, HtmlEventSpecification.HtmlLivingStandard, true,
			EventTranslationKind.RuntimeAdapter, XamlEventKind.Custom);

	private static HtmlEventHandlerDefinition External(
		string name,
		DomEventKind kind,
		HtmlEventSpecification specification,
		XamlEventKind xaml = XamlEventKind.Custom) =>
		new($"on{name}", name, kind, specification, false,
			xaml == XamlEventKind.Custom ? EventTranslationKind.RuntimeAdapter : EventTranslationKind.Semantic,
			xaml);
}
