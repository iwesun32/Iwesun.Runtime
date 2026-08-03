namespace Iwesun.Runtime.Web;

public enum DomEventKind
{
	Abort,
	AfterPrint,
	AnimationCancel,
	AnimationEnd,
	AnimationIteration,
	AnimationStart,
	AuxClick,
	BeforeInput,
	BeforeMatch,
	BeforePrint,
	BeforeToggle,
	BeforeUnload,
	Blur,
	Cancel,
	CanPlay,
	CanPlayThrough,
	Change,
	Click,
	Close,
	CompositionEnd,
	CompositionStart,
	CompositionUpdate,
	Command,
	ContextLost,
	ContextMenu,
	ContextRestored,
	Copy,
	CueChange,
	Cut,
	DoubleClick,
	Drag,
	DragEnd,
	DragEnter,
	DragLeave,
	DragOver,
	DragStart,
	Drop,
	DurationChange,
	Emptied,
	Ended,
	Error,
	Focus,
	FocusIn,
	FocusOut,
	FormData,
	FullscreenChange,
	FullscreenError,
	GamepadConnected,
	GamepadDisconnected,
	GotPointerCapture,
	HashChange,
	Input,
	Invalid,
	KeyDown,
	KeyPress,
	KeyUp,
	LanguageChange,
	Load,
	LoadedData,
	LoadedMetadata,
	LoadStart,
	LostPointerCapture,
	Message,
	MessageError,
	MouseDown,
	MouseEnter,
	MouseLeave,
	MouseMove,
	MouseOut,
	MouseOver,
	MouseUp,
	Offline,
	Online,
	PageHide,
	PageReveal,
	PageShow,
	PageSwap,
	Paste,
	Pause,
	Play,
	Playing,
	PointerCancel,
	PointerDown,
	PointerEnter,
	PointerLeave,
	PointerMove,
	PointerOut,
	PointerOver,
	PointerRawUpdate,
	PointerUp,
	PopState,
	Progress,
	RateChange,
	RejectionHandled,
	Reset,
	Resize,
	Scroll,
	ScrollEnd,
	SecurityPolicyViolation,
	Seeked,
	Seeking,
	Select,
	SelectionChange,
	SelectStart,
	SlotChange,
	Stalled,
	Storage,
	Submit,
	Suspend,
	TimeUpdate,
	Toggle,
	TouchCancel,
	TouchEnd,
	TouchMove,
	TouchStart,
	TransitionCancel,
	TransitionEnd,
	TransitionRun,
	TransitionStart,
	UnhandledRejection,
	Unload,
	VolumeChange,
	Waiting,
	WebKitAnimationEnd,
	WebKitAnimationIteration,
	WebKitAnimationStart,
	WebKitTransitionEnd,
	Wheel,
	Custom
}

public enum XamlEventKind
{
	AccessKeyDisplayDismissed,
	AccessKeyDisplayRequested,
	AccessKeyInvoked,
	ActualThemeChanged,
	BringIntoViewRequested,
	CharacterReceived,
	Checked,
	Click,
	Closed,
	Closing,
	ContextCanceled,
	ContextRequested,
	CurrentStateChanged,
	DataContextChanged,
	DoubleTapped,
	DragEnter,
	DragLeave,
	DragOver,
	DragStarting,
	Drop,
	DropCompleted,
	EffectiveViewportChanged,
	GettingFocus,
	GotFocus,
	Holding,
	Indeterminate,
	KeyDown,
	KeyUp,
	LayoutUpdated,
	Loaded,
	LosingFocus,
	LostFocus,
	ManipulationCompleted,
	ManipulationDelta,
	ManipulationInertiaStarting,
	ManipulationStarted,
	ManipulationStarting,
	MediaEnded,
	MediaFailed,
	MediaOpened,
	NoFocusCandidateFound,
	Opened,
	Opening,
	PointerCanceled,
	PointerCaptureLost,
	PointerEntered,
	PointerExited,
	PointerMoved,
	PointerPressed,
	PointerReleased,
	PointerWheelChanged,
	PreviewKeyDown,
	PreviewKeyUp,
	ProcessKeyboardAccelerators,
	RightTapped,
	SelectionChanged,
	SizeChanged,
	Tapped,
	TextChanged,
	TextChanging,
	Toggled,
	Unchecked,
	Unloaded,
	ValueChanged,
	Custom
}

public enum DomEventPhase
{
	None,
	Capture,
	Target,
	Bubble
}

public enum XamlEventRoutingStrategy
{
	None,
	Direct,
	Tunnel,
	Bubble
}

public enum DomEventRegistrationKind
{
	None,
	InlineAttribute,
	EventHandlerProperty,
	EventListener,
	RuntimeSynthetic,
	Custom
}

public enum XamlEventImplementationKind
{
	None,
	RoutedEventHandler,
	DirectEventHandler,
	CommandBinding,
	Behavior,
	RuntimeAdapter,
	Unsupported
}

public enum EventTranslationKind
{
	Unspecified,
	Direct,
	Semantic,
	Composite,
	RuntimeAdapter,
	Unsupported
}

public readonly record struct ElementEventSlot<TValue>
{
	public bool IsSet { get; }
	public TValue? Value { get; }

	private ElementEventSlot(TValue value)
	{
		ArgumentNullException.ThrowIfNull(value);
		IsSet = true;
		Value = value;
	}

	public static ElementEventSlot<TValue> Unset => default;

	public static ElementEventSlot<TValue> FromValue(TValue value) => new(value);
}

public readonly record struct ElementEventLink
{
	public bool IsSet { get; }
	public string Description { get; }

	public ElementEventLink(string description)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(description);
		IsSet = true;
		Description = description;
	}

	public static ElementEventLink None => default;
}

public sealed record DomEventRegistration(
	DomEventKind Kind,
	string EventName,
	DomEventRegistrationKind RegistrationKind,
	DomEventPhase Phase,
	bool Bubbles,
	bool Cancelable,
	bool Composed,
	bool Passive,
	bool Once);

public sealed record DomEventRuntimeEvidence(
	long InvocationCount,
	DateTimeOffset? LastInvokedAt,
	string TargetXPath,
	string CurrentTargetXPath,
	DomEventPhase Phase,
	bool DefaultPrevented,
	bool PropagationStopped,
	bool ImmediatePropagationStopped);

public sealed record XamlEventRegistration(
	XamlEventKind Kind,
	string EventName,
	XamlEventImplementationKind ImplementationKind,
	XamlEventRoutingStrategy RoutingStrategy,
	bool HandledEventsToo);

public sealed record XamlEventRuntimeEvidence(
	long InvocationCount,
	DateTimeOffset? LastInvokedAt,
	string OriginalSourceIdentity,
	string CurrentTargetIdentity,
	XamlEventRoutingStrategy RoutingStrategy,
	bool Handled);

public abstract class DomElementEvent :
	IDomEventSlotOwner,
	IXamlEventSlotOwner,
	IDomElementSlotAuditOwner
{
	public DomElementEvent(
		string name,
		EventTranslationKind translation,
		ElementEventSlot<DomEventRegistration> domInitialization,
		ElementEventLink domLink,
		ElementEventSlot<DomEventRuntimeEvidence> domRuntime,
		ElementEventSlot<XamlEventRegistration> xamlInitialization,
		ElementEventLink xamlLink,
		ElementEventSlot<XamlEventRuntimeEvidence> xamlRuntime)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		Translation = translation;
		DomInitialization = domInitialization;
		DomLink = domLink;
		DomRuntime = domRuntime;
		XamlInitialization = xamlInitialization;
		XamlLink = xamlLink;
		XamlRuntime = xamlRuntime;
	}

	public string Name { get; }
	public ElementSlotCategory Category => ElementSlotCategory.Action;
	public EventTranslationKind Translation { get; }
	public ElementEventSlot<DomEventRegistration> DomInitialization { get; protected set; }
	public ElementEventLink DomLink { get; protected set; }
	public ElementEventSlot<DomEventRuntimeEvidence> DomRuntime { get; protected set; }
	public ElementEventSlot<XamlEventRegistration> XamlInitialization { get; protected set; }
	public ElementEventLink XamlLink { get; protected set; }
	public ElementEventSlot<XamlEventRuntimeEvidence> XamlRuntime { get; protected set; }

	public IReadOnlyList<DomPropertyDataSlot> DomSlots { get; } =
	[
		DomPropertyDataSlot.Initialization,
		DomPropertyDataSlot.Link,
		DomPropertyDataSlot.Runtime
	];

	public abstract DomQueryApplicationResult ApplyDomQueryResult(
		DomPropertyDataSlot slot,
		DomPropertyQueryResult result);

	public IReadOnlyList<XamlPropertyDataSlot> XamlSlots { get; } =
	[
		XamlPropertyDataSlot.Initialization,
		XamlPropertyDataSlot.Link,
		XamlPropertyDataSlot.Runtime
	];

	public abstract void ApplyXamlQueryResult(
		XamlPropertyDataSlot slot,
		XamlPropertyQueryResult result);

	public abstract DomElementSlottedPropertyAuditResult AuditSlots();
}
