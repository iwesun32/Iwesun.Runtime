using System.Runtime.CompilerServices;
using Iwesun.Runtime.Diagnostics;

[assembly: InternalsVisibleTo("Iwesun.Runtime.Web.WinUI.Tests")]

#if DEBUG
[assembly: DiagnosticBreakpoint(
	"runtime.web.winui.xaml.built",
	"runtime-web-winui",
	"Pause after the strong WinUI object tree is built.",
	"src/Iwesun.Runtime.Web.WinUI/WinUiHtmlRuntimeSession.cs",
	Enabled = false)]
[assembly: DiagnosticBreakpoint(
	"runtime.web.winui.xaml.displayed",
	"runtime-web-winui",
	"Pause after the WinUI object tree completes first layout.",
	"src/Iwesun.Runtime.Web.WinUI/WinUiHtmlRuntimeSession.cs",
	Enabled = false)]
[assembly: DiagnosticBreakpoint(
	"runtime.web.winui.xaml.filled",
	"runtime-web-winui",
	"Pause after live WinUI properties are read into XAML evidence slots.",
	"src/Iwesun.Runtime.Web.WinUI/WinUiHtmlRuntimeSession.cs",
	Enabled = false)]
[assembly: DiagnosticBreakpoint(
	"runtime.web.winui.audit.completed",
	"runtime-web-winui",
	"Pause after the element-owned bidirectional audit completes.",
	"src/Iwesun.Runtime.Web.WinUI/WinUiHtmlRuntimeSession.cs",
	Enabled = false)]
[assembly: DiagnosticWatchPoint(
	"runtime.web.winui.xaml.style-resolution",
	"runtime-web-winui",
	"style-resolution",
	"Captures unresolved CSS expressions at strong WinUI property assignment.",
	"src/Iwesun.Runtime.Web.WinUI/WinUiXamlElementObjectFactory.cs")]
[assembly: DiagnosticWatchPoint(
	"runtime.web.winui.layout.padding-containing-block",
	"runtime-web-winui",
	"padding-containing-block",
	"Captures strong WinUI padding containing-block measurement and placement.",
	"src/Iwesun.Runtime.Web.WinUI/HtmlInteractiveFlexControl.cs")]
#endif
