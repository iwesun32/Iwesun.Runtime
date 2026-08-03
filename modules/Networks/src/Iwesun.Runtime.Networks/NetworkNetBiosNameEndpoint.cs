using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Iwesun.Runtime.Networks;

/// <summary>Networks 3.0 unicast NBNS node-status endpoint.</summary>
public sealed class NetworkNetBiosNameEndpoint<TKey>
	: TrackedRequestReplyEndpointBase<NetworkNetBiosNameRequest<TKey>, NetworkNetBiosNameResponse<TKey>, TKey>
	where TKey : notnull
{
	private readonly NetworkAccessExecutionContext _context;
	protected override bool StartAttemptsConcurrently => true;

	public NetworkNetBiosNameEndpoint(
		NetworkAccessExecutionContext? context = null,
		int maxSendQueueLength = 256,
		int maxReceiveQueueLength = 256,
		int maxAttemptCount = 3,
		int defaultTimeoutMs = 3000)
		: base(maxSendQueueLength, maxReceiveQueueLength, maxAttemptCount, defaultTimeoutMs)
	{
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
	}

	protected override Guid GetRequestId(NetworkNetBiosNameRequest<TKey> request) => request.RequestId;
	protected override TKey GetRequestKey(NetworkNetBiosNameRequest<TKey> request) => request.Key;
	protected override Guid GetResponseRequestId(NetworkNetBiosNameResponse<TKey> response) => response.Identity.RequestId;
	protected override TKey GetResponseKey(NetworkNetBiosNameResponse<TKey> response) => response.Key;
	protected override NetworkExecutionIdentity GetResponseIdentity(NetworkNetBiosNameResponse<TKey> response) => response.Identity;
	protected override int? GetRequestTimeoutOverrideMs(NetworkNetBiosNameRequest<TKey> request) => request.TimeoutMs;
	protected override byte? GetResponseRetryCount(NetworkNetBiosNameResponse<TKey> response) => response.RetryCount;
	protected override bool CanRetry(NetworkNetBiosNameRequest<TKey> request, NetworkFailure failure) => request.AllowRetry;
	protected override TrackedResponseCollectionPolicy GetResponseCollectionPolicy(
		NetworkNetBiosNameRequest<TKey> request) => request.ResponsePolicy.IsValid
			? request.ResponsePolicy
			: TrackedResponseCollectionPolicy.FirstValidResponse();

	protected override TrackedAttemptStartResult OnStartAttempt(
		NetworkNetBiosNameRequest<TKey> request, byte retryCount,
		int timeoutMs, CancellationToken cancellationToken) => PrecisionSocketEndpointSupport.BranchIdentityRequired();

	protected override TrackedAttemptStartResult OnStartBranch(
		NetworkNetBiosNameRequest<TKey> request,
		byte retryCount,
		byte branchNumber,
		NetworkExecutionIdentity identity,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		if (branchNumber != 1 || request.Port == 0 || !request.AccessPlan.TryValidate(out _) ||
			request.AccessPlan.Destination.Kind != NetworkSelectorKind.Exact ||
			!request.AccessPlan.Destination.ExactValue.IsIPv4 ||
			request.AccessPlan.PathProvider is not NetworkPathProvider.Automatic and not NetworkPathProvider.Direct)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.NetBiosRequestInvalid);
		}
		var address = request.AccessPlan.Destination.ExactValue.ToIPAddress();
		var firstOctet = address.GetAddressBytes()[0];
		var multipleResponses = address.Equals(IPAddress.Broadcast) ||
			request.AccessPlan.IpPacketPolicy.AllowBroadcast;
		if (firstOctet is >= 224 and <= 239)
		{
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.NetBiosMultipleResponsesUnsupported);
		}
		if (multipleResponses && request.ResponsePolicy.Mode !=
			TrackedResponseCollectionMode.CollectUntilWindowEnds)
			return PrecisionSocketEndpointSupport.Reject(NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid);
		if (request.AccessPlan.PathProvider == NetworkPathProvider.Direct &&
			request.AccessPlan.NextHop.Kind == NetworkSelectorKind.Exact &&
			!request.AccessPlan.NextHop.IsOnLink)
		{
			var capability = _context.SocketExecutor.QueryWfpCapability();
			if (!capability.Supported) return PrecisionSocketEndpointSupport.Reject(capability.ReasonCode);
		}
		if (!_context.TryResolve(request.AccessPlan, request.SecurityBoundary, out var resolved, out var failure))
			return PrecisionSocketEndpointSupport.Reject(failure.ReasonCode);
		_ = multipleResponses
			? QueryMultipleAsync(request, retryCount, identity, resolved, timeoutMs, cancellationToken)
			: QueryAsync(request, retryCount, identity, resolved, timeoutMs, cancellationToken);
		return TrackedAttemptStartResult.Accepted();
	}

	private async Task QueryMultipleAsync(
		NetworkNetBiosNameRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		timeout.CancelAfter(Math.Min(timeoutMs, request.ResponsePolicy.WindowMs));
		var transactionId = (ushort)Random.Shared.Next(1, 65536);
		var payload = BuildNodeStatusQuery(transactionId);
		var executionRequest = new NetworkSocketExecutionRequest(
			identity, request.AccessPlan, resolved, request.SecurityBoundary,
			NetworkSocketOperation.UdpExchange, request.Port, payload, 4096);
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		try
		{
			NetworkSocketExecutor.ApplyPlan(socket, executionRequest);
			using var policy = _context.SocketExecutor.AcquireWfpPolicyIfRequired(
				socket, executionRequest, ProtocolType.Udp, out var policyFailure);
			if (!string.IsNullOrEmpty(policyFailure.ReasonCode)) return;
			var remote = new IPEndPoint(
				NetworkIpAddressInterop.ToSystemAddress(
					resolved.Destination,
					resolved.Interface.InterfaceIndex),
				request.Port);
			await socket.SendToAsync(payload, SocketFlags.None, remote, timeout.Token).ConfigureAwait(false);
			for (ushort count = 0; count < request.ResponsePolicy.MaxResponses;)
			{
				var buffer = new byte[4096];
				EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
				var received = await socket.ReceiveFromAsync(
					buffer, SocketFlags.None, sender, timeout.Token).ConfigureAwait(false);
				Array.Resize(ref buffer, received.ReceivedBytes);
				if (!TryParseNodeStatusResponse(buffer, transactionId, out var records, out var responseCode) ||
					records.Length == 0)
					continue;
				count++;
				var execution = NetworkSocketExecutor.CreateSuccessResult(
					executionRequest, socket, buffer, ProofKind.Observed, 0,
					(IPEndPoint)received.RemoteEndPoint,
					policyEnforced: policy is not null);
				var succeeded = execution.Compliance == AccessCompliance.Satisfied;
				PublishResponse(new NetworkNetBiosNameResponse<TKey>(
					identity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, execution,
					records, SelectPrimaryName(records), responseCode, succeeded,
					succeeded ? string.Empty : NetworkAccessFailureCodes.AccessEvidenceIncomplete,
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), identity.BranchId);
			}
		}
		catch (OperationCanceledException) when (timeout.IsCancellationRequested)
		{
		}
		catch
		{
			// The tracking window owns the terminal outcome; transport failures leave an empty window.
		}
	}

	protected override bool IsSuccessfulResponse(NetworkNetBiosNameResponse<TKey> response, out NetworkFailure failure)
	{
		if (response.Succeeded)
		{
			failure = NetworkFailure.None;
			return true;
		}
		if (!response.Execution.IsSuccess || response.Execution.Compliance != AccessCompliance.Satisfied)
			return PrecisionSocketEndpointSupport.IsSuccessful(response.Execution, out failure);
		failure = new NetworkFailure(
			NetworkFailureKind.Protocol, response.ResponseCode,
			NetworkAccessFailureCodes.NetBiosResponseInvalid, response.Execution.Error);
		return false;
	}

	protected override ProtocolOutcome GetResponseProtocolOutcome(
		NetworkNetBiosNameResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) =>
		response.Succeeded ? ProtocolOutcome.Succeeded
			: response.Execution.Outcome == ProtocolOutcome.Succeeded
				? ProtocolOutcome.ProtocolFailed : response.Execution.Outcome;

	protected override AccessCompliance GetResponseAccessCompliance(
		NetworkNetBiosNameResponse<TKey> response, bool contractSatisfied, NetworkFailure failure) =>
		response.Execution.Compliance;

	private async Task QueryAsync(
		NetworkNetBiosNameRequest<TKey> request,
		byte retryCount,
		NetworkExecutionIdentity identity,
		ResolvedAccessPlan resolved,
		int timeoutMs,
		CancellationToken lifetimeToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
		timeout.CancelAfter(timeoutMs);
		var transactionId = (ushort)Random.Shared.Next(1, 65536);
		var execution = await _context.SocketExecutor.ExecuteAsync(new NetworkSocketExecutionRequest(
			identity, request.AccessPlan, resolved, request.SecurityBoundary,
			NetworkSocketOperation.UdpExchange, request.Port, BuildNodeStatusQuery(transactionId), 4096),
			timeout.Token).ConfigureAwait(false);
		NetworkNetBiosNameRecord[] records = [];
		byte responseCode = 0;
		var parsed = execution.IsSuccess && TryParseNodeStatusResponse(
			execution.Payload, transactionId, out records, out responseCode);
		var succeeded = parsed && records.Length > 0 && execution.Compliance == AccessCompliance.Satisfied;
		PublishResponse(new NetworkNetBiosNameResponse<TKey>(
			identity.CreateResponse(Guid.NewGuid()), request.Key, retryCount, execution,
			records, SelectPrimaryName(records), responseCode, succeeded,
			succeeded ? string.Empty : execution.IsSuccess
				? NetworkAccessFailureCodes.NetBiosResponseInvalid
				: execution.ReasonCode,
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), identity.BranchId);
	}

	internal static byte[] BuildNodeStatusQuery(ushort transactionId)
	{
		var message = new byte[50];
		BinaryPrimitives.WriteUInt16BigEndian(message, transactionId);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), 1);
		message[12] = 32;
		Span<byte> rawName = stackalloc byte[16];
		rawName[0] = (byte)'*';
		for (var index = 0; index < rawName.Length; index++)
		{
			message[13 + index * 2] = (byte)('A' + (rawName[index] >> 4));
			message[14 + index * 2] = (byte)('A' + (rawName[index] & 0x0F));
		}
		message[45] = 0;
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(46), 0x0021);
		BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(48), 0x0001);
		return message;
	}

	internal static bool TryParseNodeStatusResponse(
		ReadOnlySpan<byte> data,
		ushort transactionId,
		out NetworkNetBiosNameRecord[] records,
		out byte responseCode)
	{
		records = [];
		responseCode = 0;
		if (data.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(data) != transactionId) return false;
		var flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
		responseCode = (byte)(flags & 0x0F);
		if ((flags & 0x8000) == 0 || responseCode != 0) return false;
		var offset = 12;
		var questions = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
		for (var index = 0; index < questions; index++)
		{
			offset = SkipName(data, offset);
			if (offset + 4 > data.Length) return false;
			offset += 4;
		}
		var answers = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
		for (var answer = 0; answer < answers; answer++)
		{
			offset = SkipName(data, offset);
			if (offset + 10 > data.Length) return false;
			var type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
			var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
			offset += 10;
			if (offset + length > data.Length) return false;
			if (type != 0x0021 || length < 1) { offset += length; continue; }
			var count = data[offset++];
			if (offset + count * 18 > data.Length) return false;
			var parsed = new List<NetworkNetBiosNameRecord>(count);
			for (var index = 0; index < count; index++)
			{
				var name = Encoding.ASCII.GetString(data.Slice(offset, 15)).TrimEnd(' ', '\0');
				var suffix = data[offset + 15];
				var nameFlags = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 16)..]);
				offset += 18;
				if (name.Length > 0) parsed.Add(new NetworkNetBiosNameRecord(name, suffix, nameFlags));
			}
			records = [.. parsed];
			return records.Length > 0;
		}
		return false;
	}

	private static int SkipName(ReadOnlySpan<byte> data, int offset)
	{
		while (offset < data.Length)
		{
			var length = data[offset];
			if (length == 0) return offset + 1;
			if ((length & 0xC0) == 0xC0) return offset + 2;
			offset += 1 + length;
		}
		return offset;
	}

	private static string SelectPrimaryName(IReadOnlyList<NetworkNetBiosNameRecord> records) =>
		records.FirstOrDefault(static item => item.Suffix == 0x20).Name ??
		records.FirstOrDefault(static item => item.Suffix == 0x00).Name ?? string.Empty;
}

public readonly record struct NetworkNetBiosNameRequest<TKey>(
	Guid RequestId,
	TKey Key,
	RequestedAccessPlan AccessPlan,
	NetworkAccessSecurityBoundary SecurityBoundary,
	ushort Port = 137,
	int? TimeoutMs = null,
	bool AllowRetry = true,
	TrackedResponseCollectionPolicy ResponsePolicy = default)
	where TKey : notnull;

public readonly record struct NetworkNetBiosNameRecord(string Name, byte Suffix, ushort Flags);

public readonly record struct NetworkNetBiosNameResponse<TKey>(
	NetworkExecutionIdentity Identity,
	TKey Key,
	byte RetryCount,
	NetworkSocketExecutionResult Execution,
	NetworkNetBiosNameRecord[] Records,
	string PrimaryName,
	byte ResponseCode,
	bool Succeeded,
	string ReasonCode,
	long CompletedAtUnixMs)
	where TKey : notnull;
