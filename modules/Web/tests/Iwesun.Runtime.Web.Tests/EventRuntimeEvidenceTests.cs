using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class EventRuntimeEvidenceTests
{
	[Fact]
	public void CapturedEvent_ParsesAndAuditsTypedRuntimeEvidence()
	{
		var captured = new CapturedDomElementEvent(
			"event.click.0",
			DomEventKind.Click,
			"click");
		captured.ApplyDomQueryResult(
			DomPropertyDataSlot.Initialization,
			DomPropertyQueryResult.DirectConstant(
				"""{"useCapture":false,"passive":false,"once":false}"""));
		captured.ApplyDomQueryResult(
			DomPropertyDataSlot.Runtime,
			DomPropertyQueryResult.DirectConstant(
				"""
				{
				  "invocationCount": 2,
				  "targetXPath": "/html/body/button",
				  "currentTargetXPath": "/html/body/button",
				  "phase": "Bubble"
				}
				"""));
		captured.ApplyXamlQueryResult(
			XamlPropertyDataSlot.Initialization,
			XamlPropertyQueryResult.DirectConstant(
				"""
				{
				  "kind": "Click",
				  "eventName": "Click",
				  "implementationKind": "RoutedEventHandler",
				  "routingStrategy": "Bubble",
				  "handledEventsToo": false
				}
				"""));
		captured.ApplyXamlQueryResult(
			XamlPropertyDataSlot.Runtime,
			XamlPropertyQueryResult.DirectConstant(
				"""
				{
				  "invocationCount": 2,
				  "originalSourceIdentity": "button",
				  "currentTargetIdentity": "button",
				  "routingStrategy": "Bubble",
				  "handled": false
				}
				"""));

		var audit = captured.AuditSlots();

		Assert.True(audit.Initialization.Passed);
		Assert.True(audit.Runtime.Passed);
		Assert.Equal(
			ElementSlotFeatureAuditStatus.Passed,
			audit.Runtime.Status);
	}

	[Fact]
	public void EventExecutionCatalog_UsesFormalEventReader()
	{
		var descriptor = DomXamlPropertyExecutionCatalog.Resolve(
			"event.tapped.0");

		Assert.Equal(XamlPropertyExecutionKind.Event, descriptor.Kind);
		Assert.True(descriptor.SupportsInitialization);
		Assert.True(descriptor.SupportsLink);
		Assert.True(descriptor.SupportsRuntime);
	}

	[Fact]
	public void HtmlHandler_ParsesInstrumentedListenerAndRuntimeEvidence()
	{
		Assert.True(HtmlEventHandlerCatalog.TryGet(
			"button",
			"onclick",
			out var definition));
		var handler = new HtmlDomEventHandler(
			Assert.IsType<HtmlEventHandlerDefinition>(definition));

		handler.ApplyDomQueryResult(
			DomPropertyDataSlot.Initialization,
			DomPropertyQueryResult.DirectConstant(
				"""
				{
				  "eventName": "click",
				  "registrationKind": "EventListener",
				  "useCapture": true,
				  "passive": true,
				  "once": false
				}
				"""));
		handler.ApplyDomQueryResult(
			DomPropertyDataSlot.Runtime,
			DomPropertyQueryResult.DirectConstant(
				"""
				{
				  "invocationCount": 3,
				  "targetXPath": "/html/body/button[1]",
				  "currentTargetXPath": "/html/body/button[1]",
				  "phase": "Target",
				  "defaultPrevented": false
				}
				"""));

		Assert.True(handler.DomInitialization.IsSet);
		Assert.Equal(
			DomEventRegistrationKind.EventListener,
			handler.DomInitialization.Value?.RegistrationKind);
		Assert.Equal(
			DomEventPhase.Capture,
			handler.DomInitialization.Value?.Phase);
		Assert.True(handler.DomInitialization.Value?.Passive);
		Assert.Equal(3, handler.DomRuntime.Value?.InvocationCount);
	}

	[Fact]
	public void InstrumentationScript_HooksRegistrationRemovalAndInvocation()
	{
		var script = WebView2EventInstrumentation.Script;

		Assert.Contains(
			"EventTarget.prototype.addEventListener",
			script,
			StringComparison.Ordinal);
		Assert.Contains(
			"EventTarget.prototype.removeEventListener",
			script,
			StringComparison.Ordinal);
		Assert.Contains(
			"record.invocationCount++",
			script,
			StringComparison.Ordinal);
		Assert.Contains(
			"__iwesunEventRegistry",
			script,
			StringComparison.Ordinal);
		Assert.Contains(
			"readAll()",
			script,
			StringComparison.Ordinal);
	}
}
