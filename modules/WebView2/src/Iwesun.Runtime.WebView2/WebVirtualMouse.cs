using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

/// <summary>Locates DOM targets and dispatches a browser-like pointer sequence.</summary>
public static class WebVirtualMouse
{
	public static Task<string> ClickAsync(
		IWebRuntimeScriptSession session,
		string backendId,
		string stage,
		string findTargetScript,
		CancellationToken ct) =>
		session.EvaluateStringAsync(BuildClickScript(findTargetScript), ct);

	public static Task<string> ClickInFrameAsync(
		IWebRuntimeScriptSession session,
		string backendId,
		string stage,
		string sourceUrlContains,
		string findTargetScript,
		CancellationToken ct) =>
		session.EvaluateStringInFrameAsync(sourceUrlContains, BuildClickScript(findTargetScript), ct);

	public static bool IsSuccess(string? result)
	{
		if (string.IsNullOrWhiteSpace(result))
			return false;

		try
		{
			using var document = JsonDocument.Parse(result);
			return document.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static string EscapeJsString(string value) =>
		value.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("'", "\\'", StringComparison.Ordinal);

	private static string BuildClickScript(string findTargetScript)
	{
		findTargetScript = findTargetScript.Replace("return await virtualClick", "return virtualClick", StringComparison.Ordinal);
		return $$"""
(() => {
  const candidates = [];
  const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
  const clampPosition = (x, y) => ({
    x: clamp(Math.round(x), 1, Math.max(1, (window.innerWidth || 1280) - 2)),
    y: clamp(Math.round(y), 1, Math.max(1, (window.innerHeight || 768) - 2))
  });
  const describe = (element, strategy, matchedText) => {
    const bounds = element.getBoundingClientRect();
    return {
      strategy,
      matchedText: matchedText || '',
      tag: element.tagName,
      text: (element.innerText || element.textContent || '').replace(/\s/g, '').slice(0, 80),
      x: Math.round(bounds.x), y: Math.round(bounds.y),
      w: Math.round(bounds.width), h: Math.round(bounds.height)
    };
  };
  const dispatch = (element, type, x, y, buttons) => {
    const options = {
      bubbles: true, cancelable: true, composed: true,
      clientX: x, clientY: y, screenX: x, screenY: y,
      button: 0, buttons, pointerId: 1, pointerType: 'mouse', isPrimary: true
    };
    try {
      element.dispatchEvent(type.startsWith('pointer') ? new PointerEvent(type, options) : new MouseEvent(type, options));
    } catch {}
  };
  const dispatchAt = (x, y, type, buttons, preferred) => {
    const hit = document.elementFromPoint(x, y) || preferred || document.body || document.documentElement;
    dispatch(hit, type, x, y, buttons);
    if (preferred && preferred !== hit) dispatch(preferred, type, x, y, buttons);
    return hit;
  };
  const moveVirtualMouse = (targetX, targetY) => {
    const target = clampPosition(targetX, targetY);
    const state = window.__iwesun_runtime_virtual_mouse || { x: target.x, y: target.y + 80, vx: 0, vy: 0 };
    let x = state.x, y = state.y, vx = state.vx || 0, vy = state.vy || 0;
    const distance = Math.hypot(target.x - x, target.y - y);
    const steps = Math.max(10, Math.min(32, Math.round(distance / 8) + 10));
    for (let index = 1; index <= steps; index++) {
      const remaining = Math.max(1, steps - index + 1);
      vx = vx * 0.72 + ((target.x - x) / remaining) * 0.55 + (Math.random() - 0.5) * 4;
      vy = vy * 0.72 + ((target.y - y) / remaining) * 0.55 + (Math.random() - 0.5) * 4;
      const position = clampPosition(x + clamp(vx, -24, 24), y + clamp(vy, -24, 24));
      x = position.x; y = position.y;
      dispatchAt(x, y, 'pointermove', 0, null);
      dispatchAt(x, y, 'mousemove', 0, null);
    }
    window.__iwesun_runtime_virtual_mouse = { x: target.x, y: target.y, vx, vy, ts: Date.now() };
  };
  const virtualClick = (element, strategy, matchedText) => {
    try { element.scrollIntoView({ block: 'center', inline: 'center' }); } catch {}
    const target = describe(element, strategy, matchedText);
    candidates.push(target);
    const center = clampPosition(target.x + target.w / 2, target.y + target.h / 2);
    moveVirtualMouse(center.x, center.y);
    const hit = document.elementFromPoint(center.x, center.y) || element;
    try { hit.focus && hit.focus({ preventScroll: true }); } catch {}
    for (const type of ['pointerover', 'mouseover', 'pointermove', 'mousemove']) dispatchAt(center.x, center.y, type, 0, element);
    for (const type of ['pointerdown', 'mousedown']) dispatchAt(center.x, center.y, type, 1, element);
    for (const type of ['pointerup', 'mouseup', 'click']) dispatchAt(center.x, center.y, type, 0, element);
    try { hit.click && hit.click(); } catch {}
    return JSON.stringify({ success: true, strategy, target, hit: describe(hit, 'hit-test-target', ''), candidates });
  };
  const fail = reason => JSON.stringify({ success: false, strategy: reason, candidates });
  try {
{{findTargetScript}}
  } catch (error) {
    candidates.push({ strategy: 'script-error', error: String(error).slice(0, 160) });
    return fail('SCRIPT_ERROR');
  }
})();
""";
	}
}
