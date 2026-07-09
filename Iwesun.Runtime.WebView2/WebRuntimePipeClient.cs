using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

public sealed class WebRuntimePipeClient
{
	public const string DefaultPipeName = "AIGateway.WebRuntime";
	public const int DefaultBufferSize = 2 * 1024 * 1024;

	private readonly string _pipeName;
	private readonly JsonSerializerOptions _jsonOptions;

	public WebRuntimePipeClient(
		string pipeName = DefaultPipeName,
		JsonSerializerOptions? jsonOptions = null)
	{
		_pipeName = pipeName;
		_jsonOptions = jsonOptions ?? DefaultJsonOptions.Create();
	}

	public async Task<string> ExecuteAsync(WebRuntimeControlRequest request, TimeSpan timeout, CancellationToken ct = default)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeoutCts.CancelAfter(timeout);

		await using var stream = new NamedPipeClientStream(
			".",
			_pipeName,
			PipeDirection.InOut,
			PipeOptions.Asynchronous);

		await stream.ConnectAsync(timeoutCts.Token);

		var payload = JsonSerializer.Serialize(request, _jsonOptions);
		var message = PipeMessage.CreateRequest(PipeCommand.WebRuntimeControl, payload);
		var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _jsonOptions));
		await stream.WriteAsync(bytes, timeoutCts.Token);
		await stream.FlushAsync(timeoutCts.Token);

		var buffer = new byte[DefaultBufferSize];
		var read = await stream.ReadAsync(buffer, timeoutCts.Token);
		if (read == 0)
			throw new IOException("WebRuntime pipe returned an empty response.");

		var responseJson = Encoding.UTF8.GetString(buffer, 0, read);
		var response = JsonSerializer.Deserialize<PipeMessage>(responseJson, _jsonOptions)
			?? throw new InvalidOperationException("WebRuntime pipe returned an invalid response.");

		if (response.Command == PipeCommand.Error)
			throw new InvalidOperationException(response.Error ?? "WebRuntime command failed.");

		return response.PayloadJson ?? "{}";
	}

	private static class DefaultJsonOptions
	{
		public static JsonSerializerOptions Create() =>
			new(JsonSerializerDefaults.Web)
			{
				DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
				WriteIndented = false
			};
	}

	private enum PipeCommand
	{
		Response = 15,
		Error = 16,
		WebRuntimeControl = 17
	}

	private sealed class PipeMessage
	{
		public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
		public PipeCommand Command { get; init; }
		public string? PayloadJson { get; init; }
		public string? Error { get; init; }

		public static PipeMessage CreateRequest(PipeCommand command, string? payloadJson = null) =>
			new() { Command = command, PayloadJson = payloadJson };
	}
}
