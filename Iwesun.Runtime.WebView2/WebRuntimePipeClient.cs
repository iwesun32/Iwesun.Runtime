using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

public sealed class WebRuntimePipeClient
{
	private readonly string _pipeName;
	private readonly RuntimeFramePipeClient _frameClient = new();

	public WebRuntimePipeClient(string pipeName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
		_pipeName = pipeName;
	}

	public async Task<RuntimeDiagnosticFrame> ExecuteAsync(
		WebRuntimeControlRequest request,
		TimeSpan timeout,
		CancellationToken ct = default,
		WebRuntimeFrameOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		options = options is null
			? new WebRuntimeFrameOptions { Destination = _pipeName }
			: new WebRuntimeFrameOptions
			{
				RequestId = options.RequestId,
				CorrelationId = options.CorrelationId,
				Source = options.Source,
				Destination = string.IsNullOrWhiteSpace(options.Destination) ? _pipeName : options.Destination
			};
		var frame = WebRuntimeProtocol.CreateRequestFrame(request, options);
		var response = await _frameClient.ExecuteAsync(_pipeName, frame, timeout, ct).ConfigureAwait(false);
		var validation = WebRuntimeProtocol.ValidateResponseFrame(frame, response);
		if (!validation.IsValid)
			throw new WebRuntimeCommandException(validation.Code, validation.Message, false, frame.Header.RequestId);
		return response;
	}

	public async Task<JsonElement?> ExecuteDataAsync(
		WebRuntimeControlRequest request,
		TimeSpan timeout,
		CancellationToken ct = default,
		WebRuntimeFrameOptions? options = null)
	{
		var response = await ExecuteAsync(request, timeout, ct, options).ConfigureAwait(false);
		if (response.Status?.Ok != true)
			throw new WebRuntimeCommandException(
				response.Status?.Code ?? "WEBRUNTIME_COMMAND_FAILED",
				response.Status?.Message ?? "WebRuntime command failed.",
				response.Status?.Retryable ?? false,
				response.Header.CorrelationId ?? response.Header.RequestId);
		return response.Data?.Clone();
	}
}
