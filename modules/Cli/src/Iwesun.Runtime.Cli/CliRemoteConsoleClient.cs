using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.Cli;

internal sealed class CliRemoteConsoleClient(ResolvedRuntimeTarget target)
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public async Task<(RuntimeDiagnosticFrame Frame, string Json)> SendAsync(
		RuntimeDiagnosticFrame request,
		CancellationToken cancellationToken)
	{
		var json = await CliTransport.SendAsync(
			target,
			request,
			cancellationToken,
			enableImpersonation: true).ConfigureAwait(false);
		var frame = JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(json, JsonOptions)
			?? throw new CliException("CLI_PROTOCOL_RESPONSE_NULL", "RemoteConsole returned a null Frame.", 5);
		return (frame, json);
	}
}
