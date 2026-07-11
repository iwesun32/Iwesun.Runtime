using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeProxyCommandTarget : RuntimeDiagnosticTargetBase
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public RuntimeProxyCommandTarget() : base("diagnostics.proxy")
	{
	}

	public override object Snapshot() => new
	{
		targetId = TargetId,
		actions = new[] { "invoke", "forward" },
		requiredArgs = new[] { "module", "pipe", "proxyAction" }
	};

	public override async Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		var action = command.Action.ToLowerInvariant();
		if (action is not ("invoke" or "forward"))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}");

		var module = ReadString(command, "module");
		if (string.IsNullOrWhiteSpace(module))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "module is required.");

		var pipeToken = ReadString(command, "pipe") ?? ReadString(command, "name") ?? ReadString(command, "branchId") ?? ReadString(command, "id");
		if (string.IsNullOrWhiteSpace(pipeToken))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "pipe/name/branchId/id is required.");

		var lease = RuntimePipeRegistry.GetLease(pipeToken);
		if (lease == null)
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Pipe lease not found: {pipeToken}.");
		if (!lease.Active)
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Pipe lease is inactive: {pipeToken}.");
		if (!DiagnosticSwitchboard.IsProxyModuleAllowed(module))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Module '{module}' is blocked by proxy module whitelist.");
		if (!IsModuleAuthorized(module, lease))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Module '{module}' is not authorized for lease '{lease.BranchId}'.");

		var targetId = ReadString(command, "targetId") ?? "web.runtime";
		var proxyAction = ReadString(command, "proxyAction") ?? ReadString(command, "forwardAction") ?? ReadString(command, "webAction");
		if (string.IsNullOrWhiteSpace(proxyAction))
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "proxyAction/forwardAction/webAction is required.");

		var forwardedArgs = BuildForwardedArgs(command.Args);
		var backendId = ReadElement(command, "backendId");
		if (backendId.HasValue && !forwardedArgs.ContainsKey("backendId"))
			forwardedArgs["backendId"] = backendId.Value;

		var payload = ReadElement(command, "payload");
		if (payload.HasValue)
		{
			if (payload.Value.ValueKind == JsonValueKind.Object)
			{
				foreach (var property in payload.Value.EnumerateObject())
					forwardedArgs[property.Name] = property.Value.Clone();
			}
			else
			{
				forwardedArgs["payload"] = payload.Value.Clone();
			}
		}

		var request = new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				Category = ReadString(command, "category") ?? "instruction",
				Operation = ReadString(command, "operation") ?? "invoke",
				RequestId = Guid.NewGuid().ToString("N"),
				Timestamp = DateTimeOffset.UtcNow,
				Source = "runtime.proxy",
				Destination = lease.PipeName
			},
			Command = new RuntimeDiagnosticFrameCommand
			{
				Domain = ReadString(command, "domain") ?? "web.runtime",
				Target = targetId,
				Member = ReadString(command, "member"),
				Action = proxyAction,
				Args = forwardedArgs.Count == 0 ? null : forwardedArgs
			}
		};

		try
		{
			var connectTimeoutMs = ReadInt(command, "connectTimeoutMs") ?? 3000;
			var ioTimeoutMs = ReadInt(command, "ioTimeoutMs") ?? 15000;
			var response = await SendFrameAsync(lease.PipeName, request, connectTimeoutMs, ioTimeoutMs, ct).ConfigureAwait(false);

			if (response.Status?.Ok != true)
			{
				return RuntimeDiagnosticActionResult.Fail(
					TargetId,
					command.Action,
					response.Status?.Message ?? "Proxy target returned non-success status.");
			}

			return RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
			{
				module,
				lease,
				proxy = new
				{
					target = targetId,
					action = proxyAction
				},
				data = response.Data,
				status = response.Status,
				extStatus = response.ExtStatus,
				meta = response.Meta
			});
		}
		catch (IOException ex)
		{
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Proxy pipe IO failed: {ex.Message}");
		}
		catch (OperationCanceledException ex)
		{
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Proxy timeout/cancelled: {ex.Message}");
		}
		catch (JsonException ex)
		{
			return RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Proxy response JSON parse failed: {ex.Message}");
		}
	}

	private static bool IsModuleAuthorized(string module, RuntimePipeLeaseSnapshot lease)
	{
		var normalizedModule = module.Trim();
		return lease.RequestedName.Equals(normalizedModule, StringComparison.OrdinalIgnoreCase)
			|| lease.BranchId.Equals(normalizedModule, StringComparison.OrdinalIgnoreCase)
			|| lease.BranchId.StartsWith($"{normalizedModule}.", StringComparison.OrdinalIgnoreCase)
			|| lease.BranchId.StartsWith($"{normalizedModule}-", StringComparison.OrdinalIgnoreCase);
	}

	private static Dictionary<string, JsonElement> BuildForwardedArgs(Dictionary<string, JsonElement>? args)
	{
		var forwarded = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		if (args == null || args.Count == 0)
			return forwarded;

		var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"module",
			"pipe",
			"name",
			"branchId",
			"id",
			"targetId",
			"proxyAction",
			"forwardAction",
			"webAction",
			"member",
			"payload",
			"category",
			"operation",
			"domain",
			"connectTimeoutMs",
			"ioTimeoutMs"
		};

		foreach (var (key, value) in args)
		{
			if (reserved.Contains(key))
				continue;
			forwarded[key] = value.Clone();
		}

		return forwarded;
	}

	private static string? ReadString(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.String)
			return value.GetString();
		return null;
	}

	private static int? ReadInt(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var number))
			return number;
		return null;
	}

	private static JsonElement? ReadElement(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null && command.Args.TryGetValue(key, out var value))
			return value.Clone();
		return null;
	}

	private static async Task<RuntimeDiagnosticFrame> SendFrameAsync(
		string pipeName,
		RuntimeDiagnosticFrame request,
		int connectTimeoutMs,
		int ioTimeoutMs,
		CancellationToken cancellationToken)
	{
		await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		connectCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(connectTimeoutMs, 1)));
		await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

		using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ioCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(ioTimeoutMs, 1)));

		var requestJson = JsonSerializer.Serialize(request, JsonOptions);
		var requestBytes = Encoding.UTF8.GetBytes(requestJson);
		var requestLength = BitConverter.GetBytes(requestBytes.Length);
		await client.WriteAsync(requestLength, ioCts.Token).ConfigureAwait(false);
		await client.WriteAsync(requestBytes, ioCts.Token).ConfigureAwait(false);
		await client.FlushAsync(ioCts.Token).ConfigureAwait(false);

		var responseLengthBuffer = new byte[4];
		if (!await ReadExactAsync(client, responseLengthBuffer, ioCts.Token).ConfigureAwait(false))
			throw new IOException("Proxy pipe closed before response length.");
		var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
		if (responseLength <= 0 || responseLength > 16 * 1024 * 1024)
			throw new InvalidOperationException($"Invalid proxy response length: {responseLength}.");

		var responseBuffer = new byte[responseLength];
		if (!await ReadExactAsync(client, responseBuffer, ioCts.Token).ConfigureAwait(false))
			throw new IOException("Proxy pipe closed before full response.");
		var responseJson = Encoding.UTF8.GetString(responseBuffer);
		return JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(responseJson, JsonOptions)
			?? throw new InvalidOperationException("Proxy response frame was null.");
	}

	private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
			if (read == 0)
				return false;
			offset += read;
		}
		return true;
	}
}
