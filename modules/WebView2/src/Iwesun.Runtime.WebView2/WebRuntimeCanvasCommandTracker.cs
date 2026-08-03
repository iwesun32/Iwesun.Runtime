using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Installs a fixed, bounded CanvasRenderingContext2D command recorder before
/// navigation. It exposes no arbitrary script entry point and records design
/// operations rather than replacing HTTP image evidence with a screenshot.
/// </summary>
public static class WebRuntimeCanvasCommandTracker
{
	private const string InstallScript = """
		(() => {
		  if (globalThis.__iwesunCanvasCommandTrackerInstalled) return true;
		  const maximumCommands = 10000;
		  const maximumImageDataBytes = 4 * 1024 * 1024;
		  const resourceIds = new WeakMap();
		  const resourceOwners = new WeakMap();
		  let nextResourceId = 1;
		  const resourceIdentity = value => {
		    if (!resourceIds.has(value))
		      resourceIds.set(value, `canvas-resource-${nextResourceId++}`);
		    return resourceIds.get(value);
		  };
		  const normalize = value => {
		    if (value === null || value === undefined) return null;
		    if (['string', 'number', 'boolean'].includes(typeof value))
		      return value;
		    if (Array.isArray(value))
		      return value.map(normalize);
		    if (ArrayBuffer.isView(value) && !(value instanceof DataView))
		      return Array.from(value, normalize);
		    if (value instanceof DOMMatrix || value instanceof DOMMatrixReadOnly)
		      return [value.a, value.b, value.c, value.d, value.e, value.f];
		    if (value instanceof ImageData) {
		      const byteLength = value.data.byteLength;
		      const captured = value.data.subarray(
		        0, Math.min(byteLength, maximumImageDataBytes));
		      let binary = '';
		      const chunkSize = 0x8000;
		      for (let index = 0; index < captured.length; index += chunkSize)
		        binary += String.fromCharCode(
		          ...captured.subarray(index, index + chunkSize));
		      return {
		        imageData: true,
		        width: value.width,
		        height: value.height,
		        byteLength,
		        truncated: byteLength > captured.length,
		        rgbaBase64: btoa(binary)
		      };
		    }
		    if (value instanceof CanvasGradient || value instanceof CanvasPattern)
		      return { resourceId: resourceIdentity(value) };
		    const source = value.currentSrc || value.src;
		    if (source) return { source: String(source) };
		    return String(value);
		  };
		  const record = (context, name, args) => {
		    const canvas = context?.canvas;
		    if (!canvas) return;
		    if (!Object.hasOwn(canvas, '__iwesunCanvasCommands')) {
		      Object.defineProperty(canvas, '__iwesunCanvasCommands', {
		        value: [],
		        configurable: false,
		        enumerable: false,
		        writable: false
		      });
		    }
		    const commands = canvas.__iwesunCanvasCommands;
		    commands.push({
		      name,
		      args: Array.from(args || [], normalize),
		      sequence: commands.length
		    });
		    if (commands.length > maximumCommands)
		      commands.splice(0, commands.length - maximumCommands);
		  };
		  const methods = [
		    'save', 'restore', 'scale', 'rotate', 'translate', 'transform',
		    'setTransform', 'resetTransform', 'clearRect', 'fillRect',
		    'strokeRect', 'beginPath', 'closePath', 'moveTo', 'lineTo',
		    'bezierCurveTo', 'quadraticCurveTo', 'arc', 'arcTo', 'ellipse',
		    'rect', 'roundRect', 'fill', 'stroke', 'clip', 'fillText',
		    'strokeText', 'drawImage', 'putImageData', 'setLineDash'
		  ];
		  const prototype = globalThis.CanvasRenderingContext2D?.prototype;
		  if (!prototype) return false;
		  const resourceFactories = [
		    'createLinearGradient', 'createRadialGradient',
		    'createConicGradient', 'createPattern'
		  ];
		  for (const name of resourceFactories) {
		    const original = prototype[name];
		    if (typeof original !== 'function') continue;
		    Object.defineProperty(prototype, name, {
		      configurable: true,
		      writable: true,
		      value: function() {
		        const resource = original.apply(this, arguments);
		        if (resource) {
		          const identity = resourceIdentity(resource);
		          const owners = resourceOwners.get(resource) || new Set();
		          owners.add(this);
		          resourceOwners.set(resource, owners);
		          record(this, name, [...arguments, { resourceId: identity }]);
		        }
		        return resource;
		      }
		    });
		  }
		  const gradientPrototype = globalThis.CanvasGradient?.prototype;
		  const originalAddColorStop = gradientPrototype?.addColorStop;
		  if (typeof originalAddColorStop === 'function') {
		    Object.defineProperty(gradientPrototype, 'addColorStop', {
		      configurable: true,
		      writable: true,
		      value: function(offset, color) {
		        const result = originalAddColorStop.call(this, offset, color);
		        for (const owner of resourceOwners.get(this) || [])
		          record(owner, 'gradient:addColorStop',
		            [{ resourceId: resourceIdentity(this) }, offset, color]);
		        return result;
		      }
		    });
		  }
		  const patternPrototype = globalThis.CanvasPattern?.prototype;
		  const originalPatternTransform = patternPrototype?.setTransform;
		  if (typeof originalPatternTransform === 'function') {
		    Object.defineProperty(patternPrototype, 'setTransform', {
		      configurable: true,
		      writable: true,
		      value: function(transform) {
		        const result = originalPatternTransform.call(this, transform);
		        for (const owner of resourceOwners.get(this) || [])
		          record(owner, 'pattern:setTransform',
		            [{ resourceId: resourceIdentity(this) }, transform]);
		        return result;
		      }
		    });
		  }
		  for (const name of methods) {
		    const original = prototype[name];
		    if (typeof original !== 'function') continue;
		    Object.defineProperty(prototype, name, {
		      configurable: true,
		      writable: true,
		      value: function() {
		        record(this, name, arguments);
		        return original.apply(this, arguments);
		      }
		    });
		  }
		  const properties = [
		    'fillStyle', 'strokeStyle', 'lineWidth', 'lineCap', 'lineJoin',
		    'miterLimit', 'lineDashOffset', 'globalAlpha',
		    'globalCompositeOperation', 'font',
		    'textAlign', 'textBaseline', 'direction', 'shadowColor',
		    'shadowBlur', 'shadowOffsetX', 'shadowOffsetY', 'filter'
		  ];
		  for (const name of properties) {
		    const descriptor = Object.getOwnPropertyDescriptor(prototype, name);
		    if (!descriptor?.get || !descriptor?.set) continue;
		    Object.defineProperty(prototype, name, {
		      configurable: descriptor.configurable,
		      enumerable: descriptor.enumerable,
		      get: descriptor.get,
		      set: function(value) {
		        record(this, `set:${name}`, [value]);
		        descriptor.set.call(this, value);
		      }
		    });
		  }
		  globalThis.__iwesunCanvasCommandTrackerInstalled = true;
		  return true;
		})()
		""";

	public static async Task InstallAsync(CoreWebView2 browser)
	{
		ArgumentNullException.ThrowIfNull(browser);
		await InstallAsync(
			async script =>
			{
				await browser.AddScriptToExecuteOnDocumentCreatedAsync(script);
			},
			async script =>
			{
				await browser.ExecuteScriptAsync(script);
			});
	}

	public static async Task InstallAsync(
		Func<string, Task> installForNewDocument,
		Func<string, Task> installForCurrentDocument)
	{
		ArgumentNullException.ThrowIfNull(installForNewDocument);
		ArgumentNullException.ThrowIfNull(installForCurrentDocument);
		await installForNewDocument(InstallScript);
		await installForCurrentDocument(InstallScript);
	}
}
