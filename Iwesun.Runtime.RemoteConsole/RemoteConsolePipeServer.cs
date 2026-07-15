using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Iwesun.Runtime.Diagnostics;
using Iwesun.Runtime.RemoteConsole.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iwesun.Runtime.RemoteConsole;

internal sealed record RemoteConsoleClientIdentity(
	string UserSid,
	IReadOnlyList<string> PrincipalSids);

internal sealed class RemoteConsolePipeServer : BackgroundService
{
	private readonly RemoteConsoleOptions _options;
	private readonly RemoteConsoleResolvedPrincipals _principals;
	private readonly RemoteConsoleAuthorization _authorization;
	private readonly ILogger<RemoteConsolePipeServer> _logger;
	private readonly ConcurrentDictionary<int, NamedPipeServerStream> _activePipes = new();
	private int _nextPipeId;

	public RemoteConsolePipeServer(
		RemoteConsoleOptions options,
		RemoteConsoleResolvedPrincipals principals,
		RemoteConsoleAuthorization authorization,
		ILogger<RemoteConsolePipeServer> logger)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_principals = principals ?? throw new ArgumentNullException(nameof(principals));
		_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("RemoteConsole pipe server starting on {PipeName}", _options.PipeName);
		while (!stoppingToken.IsCancellationRequested)
		{
			var pipe = CreateServer();
			var pipeId = Interlocked.Increment(ref _nextPipeId);
			_activePipes[pipeId] = pipe;
			try
			{
				await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
				_ = HandleClientAsync(pipeId, pipe, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				_activePipes.TryRemove(pipeId, out _);
				await pipe.DisposeAsync().ConfigureAwait(false);
				break;
			}
			catch (IOException) when (stoppingToken.IsCancellationRequested)
			{
				_activePipes.TryRemove(pipeId, out _);
				await pipe.DisposeAsync().ConfigureAwait(false);
				break;
			}
			catch (Exception ex)
			{
				_activePipes.TryRemove(pipeId, out _);
				await pipe.DisposeAsync().ConfigureAwait(false);
				_logger.LogWarning(ex, "RemoteConsole pipe accept failed on {PipeName}", _options.PipeName);
				await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken).ConfigureAwait(false);
			}
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		foreach (var pipe in _activePipes.Values)
		{
			try
			{
				await pipe.DisposeAsync().ConfigureAwait(false);
			}
			catch (IOException)
			{
			}
		}
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task HandleClientAsync(int pipeId, NamedPipeServerStream pipe, CancellationToken cancellationToken)
	{
		try
		{
			var identity = CaptureClientIdentity(pipe);
			var request = await RuntimeFramePipeCodec.ReadAsync(
				pipe,
				_options.MaxRequestBytes,
				cancellationToken).ConfigureAwait(false);
			var response = Dispatch(identity, request);
			await RuntimeFramePipeCodec.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (IOException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "RemoteConsole client request failed on {PipeName}", _options.PipeName);
		}
		finally
		{
			_activePipes.TryRemove(pipeId, out _);
			try
			{
				pipe.Disconnect();
			}
			catch (InvalidOperationException)
			{
			}
			await pipe.DisposeAsync().ConfigureAwait(false);
		}
	}

	private RuntimeDiagnosticFrame Dispatch(RemoteConsoleClientIdentity identity, RuntimeDiagnosticFrame request)
	{
		var action = request.Command?.Action ?? "";
		var authorization = _authorization.Authorize(identity.UserSid, identity.PrincipalSids, action);
		if (!authorization.Ok)
			return CreateResponse(request, false, authorization.Code, authorization.Message, null);

		if (string.Equals(action, RemoteConsoleActions.ServerInfo, StringComparison.OrdinalIgnoreCase))
		{
			return CreateResponse(request, true, "OK", "", new
			{
				pipeName = _options.PipeName,
				approvalMode = _options.ApprovalMode.ToString(),
				serviceIdentity = WindowsIdentity.GetCurrent().User?.Value
			});
		}

		return CreateResponse(
			request,
			false,
			RemoteConsoleErrorCodes.InvalidRequest,
			$"RemoteConsole action '{action}' is not implemented by the current service stage.",
			null);
	}

	private NamedPipeServerStream CreateServer()
	{
		if (!OperatingSystem.IsWindows())
		{
			return new NamedPipeServerStream(
				_options.PipeName,
				PipeDirection.InOut,
				NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous);
		}
		return CreateWindowsServer();
	}

	[SupportedOSPlatform("windows")]
	private NamedPipeServerStream CreateWindowsServer()
	{
		var security = new PipeSecurity();
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl);
		AddAccessRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl);
		var serviceSid = WindowsIdentity.GetCurrent().User;
		if (serviceSid is not null)
			AddAccessRule(security, serviceSid, PipeAccessRights.FullControl);
		foreach (var sid in _principals.Submitters.Concat(_principals.Approvers).DistinctBy(static sid => sid.Value))
			AddAccessRule(security, sid, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance);

		return NamedPipeServerStreamAcl.Create(
			_options.PipeName,
			PipeDirection.InOut,
			NamedPipeServerStream.MaxAllowedServerInstances,
			PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous,
			0,
			0,
			security);
	}

	[SupportedOSPlatform("windows")]
	private static void AddAccessRule(PipeSecurity security, SecurityIdentifier sid, PipeAccessRights rights) =>
		security.AddAccessRule(new PipeAccessRule(sid, rights, AccessControlType.Allow));

	[SupportedOSPlatform("windows")]
	private static RemoteConsoleClientIdentity CaptureWindowsClientIdentity(NamedPipeServerStream pipe)
	{
		RemoteConsoleClientIdentity? result = null;
		pipe.RunAsClient(() =>
		{
			using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
			var userSid = identity.User?.Value
				?? throw new UnauthorizedAccessException("The named-pipe client has no Windows user SID.");
			var principals = identity.Groups?
				.Select(static group => group.Value)
				.Append(userSid)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray()
				?? [userSid];
			result = new RemoteConsoleClientIdentity(userSid, principals);
		});
		return result ?? throw new UnauthorizedAccessException("The named-pipe client identity could not be captured.");
	}

	private static RemoteConsoleClientIdentity CaptureClientIdentity(NamedPipeServerStream pipe)
	{
		if (!OperatingSystem.IsWindows())
			throw new PlatformNotSupportedException("RemoteConsole client identity capture requires Windows.");
		return CaptureWindowsClientIdentity(pipe);
	}

	private static RuntimeDiagnosticFrame CreateResponse(
		RuntimeDiagnosticFrame request,
		bool ok,
		string code,
		string message,
		object? data)
	{
		return new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "response",
				Category = request.Header.Category,
				Operation = request.Header.Operation,
				RequestId = request.Header.RequestId,
				CorrelationId = request.Header.RequestId ?? request.Header.CorrelationId,
				Source = RemoteConsoleProtocol.ServerTarget,
				Destination = request.Header.Source
			},
			Status = new RuntimeDiagnosticFrameStatus
			{
				Ok = ok,
				Code = code,
				Message = message
			},
			Data = data is null ? null : JsonSerializer.SerializeToElement(data)
		};
	}
}
