using System.Collections.Concurrent;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class TrackedRequestReplyEndpointBaseTests
{
	[Fact]
	public void MaximumAttemptCountIsHardLimitedToThree()
	{
		using var endpoint = new FakeEndpoint(maxAttemptCount: 99);
		Assert.Equal(3, endpoint.MaxAttemptCount);
	}

	[Fact]
	public void AttemptTimeoutPropertiesAreIndependentAndDefaultSetterUpdatesAllThree()
	{
		using var endpoint = new FakeEndpoint();
		endpoint.FirstAttemptTimeoutMs = 2000;
		endpoint.SecondAttemptTimeoutMs = 3000;
		endpoint.ThirdAttemptTimeoutMs = 4000;

		Assert.Equal(2000, endpoint.FirstAttemptTimeoutMs);
		Assert.Equal(3000, endpoint.SecondAttemptTimeoutMs);
		Assert.Equal(4000, endpoint.ThirdAttemptTimeoutMs);
		Assert.Equal(2000, endpoint.DefaultTimeoutMs);

		endpoint.DefaultTimeoutMs = 750;
		Assert.Equal(750, endpoint.FirstAttemptTimeoutMs);
		Assert.Equal(750, endpoint.SecondAttemptTimeoutMs);
		Assert.Equal(750, endpoint.ThirdAttemptTimeoutMs);
	}

	[Fact]
	public async Task ConcurrentAttemptEndpointStartsEntireSubmittedBatchBeforeAnyAttemptReturns()
	{
		using var endpoint = new ConcurrentStartEndpoint();
		var requests = Enumerable.Range(1, 8)
			.Select(static key => new FakeRequest(Guid.NewGuid(), key, 3000, false, default))
			.ToArray();

		Assert.Equal(requests.Length, endpoint.Send(requests));
		try
		{
			await WaitUntilAsync(() => endpoint.StartedCount == requests.Length);
			Assert.Equal(requests.Length, endpoint.StartedCount);
		}
		finally
		{
			endpoint.ReleaseStarts();
		}
	}

	[Fact]
	public async Task SuccessfulResponseCarriesOriginalRequestAndCorrelation()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 42, 200, true, default);
		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);

		endpoint.Reply(new(request.RequestId, true, 7));

		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(request, completion.Request);
		Assert.Equal(request.RequestId, completion.RequestId);
		Assert.Equal(42, completion.Key);
		Assert.Equal(42, completion.Request.Correlation);
		Assert.Equal(7, completion.Response.Value);
		Assert.Equal(1, completion.AttemptCount);
		Assert.Equal(0, completion.RetryCount);
		Assert.Equal(1, completion.AttemptHistory.Count);
		Assert.Equal(1, completion.AttemptHistory.First.AttemptNumber);
		Assert.Equal(NetworkFailureKind.None, completion.AttemptHistory.First.FailureKind);
		Assert.True(completion.TotalElapsedMs >= 0);
		Assert.Equal(0, endpoint.PendingCount);
	}

	[Fact]
	public async Task AttemptPlanningExceptionFailsRequestAndSendThreadContinues()
	{
		using var endpoint = new FakeEndpoint(planningFailureCount: 1);
		var failedRequest = new FakeRequest(Guid.NewGuid(), 1, 200, false, default);
		var failed = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);

		Assert.True(endpoint.TrySend(failedRequest));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
		Assert.Equal(NetworkAccessFailureCodes.AttemptPlanThrew, failure.Failure.Reason);
		Assert.Equal(1, failure.AttemptCount);

		var nextRequest = new FakeRequest(Guid.NewGuid(), 2, 200, true, default);
		Assert.True(endpoint.TrySend(nextRequest));
		await WaitUntilAsync(() => endpoint.AttemptCount(nextRequest.RequestId) == 1);
		endpoint.Reply(new(nextRequest.RequestId, true, 17, Correlation: 2));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);

		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(nextRequest.RequestId, completion.RequestId);
	}

	[Fact]
	public async Task TimeoutPerformsAtMostThreeTotalAttemptsThenFails()
	{
		using var endpoint = new FakeEndpoint(maxAttemptCount: 3);
		var request = new FakeRequest(Guid.NewGuid(), 1, 25, true, default);
		var failed = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);

		Assert.True(endpoint.TrySend(request));
		var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(3, result.AttemptCount);
		Assert.Equal(2, result.RetryCount);
		Assert.Equal(NetworkFailureKind.Timeout, result.Failure.Kind);
		Assert.Equal(3, endpoint.AttemptCount(request.RequestId));
		Assert.Equal(3, result.AttemptHistory.Count);
		Assert.Equal([1, 2, 3], new[]
		{
			result.AttemptHistory.First.AttemptNumber,
			result.AttemptHistory.Second.AttemptNumber,
			result.AttemptHistory.Third.AttemptNumber,
		});
		Assert.All(new[]
		{
			result.AttemptHistory.First,
			result.AttemptHistory.Second,
			result.AttemptHistory.Third,
		}, attempt =>
		{
			Assert.Equal(25, attempt.TimeoutMs);
			Assert.Equal(NetworkFailureKind.Timeout, attempt.FailureKind);
			Assert.True(attempt.ElapsedMs >= 20);
		});
		Assert.NotEqual(result.AttemptHistory.First.Identity.AttemptId, result.AttemptHistory.Second.Identity.AttemptId);
		Assert.NotEqual(result.AttemptHistory.Second.Identity.AttemptId, result.AttemptHistory.Third.Identity.AttemptId);
		Assert.All(new[]
		{
			result.AttemptHistory.First,
			result.AttemptHistory.Second,
			result.AttemptHistory.Third,
		}, attempt =>
		{
			Assert.NotEqual(Guid.Empty, attempt.Identity.RequestId);
			Assert.NotEqual(Guid.Empty, attempt.Identity.AttemptId);
			Assert.Equal(Guid.Empty, attempt.Identity.BranchId);
			Assert.Equal(1, attempt.BranchCount);
		});
		Assert.Equal((int)(result.FailedAtUnixMs - result.FirstQueuedAtUnixMs), result.TotalElapsedMs);
		Assert.Equal(0, endpoint.PendingCount);
	}

	[Fact]
	public async Task NullRequestTimeoutUsesT1T2T3AndRetryEventReportsFinishedAttempt()
	{
		using var endpoint = new FakeEndpoint(maxAttemptCount: 3);
		endpoint.FirstAttemptTimeoutMs = 20;
		endpoint.SecondAttemptTimeoutMs = 30;
		endpoint.ThirdAttemptTimeoutMs = 40;
		var retries = new ConcurrentQueue<(TrackedPendingSnapshot<FakeRequest, int> Snapshot, byte NextRetry, byte NextAttempt)>();
		var failed = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestRetrying += (_, args) => retries.Enqueue((args.Snapshot, args.NextRetryCount, args.NextAttemptNumber));
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);

		Assert.True(endpoint.TrySend(new(Guid.NewGuid(), 11, null, true, default)));
		var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(3, result.AttemptCount);
		Assert.Equal(2, result.RetryCount);
		Assert.Equal(3, result.AttemptHistory.Count);
		Assert.Equal(20, result.AttemptHistory.First.TimeoutMs);
		Assert.Equal(30, result.AttemptHistory.Second.TimeoutMs);
		Assert.Equal(40, result.AttemptHistory.Third.TimeoutMs);
		Assert.Equal(2, retries.Count);
		Assert.True(retries.TryDequeue(out var firstRetry));
		Assert.Equal(1, firstRetry.Snapshot.AttemptHistory.Count);
		Assert.Equal(20, firstRetry.Snapshot.AttemptHistory.First.TimeoutMs);
		Assert.Equal(1, firstRetry.NextRetry);
		Assert.Equal(2, firstRetry.NextAttempt);
		Assert.True(firstRetry.Snapshot.TotalElapsedMs >= firstRetry.Snapshot.AttemptHistory.First.ElapsedMs);
	}

	[Fact]
	public async Task ErrorResponseRetriesAndLaterSuccessCompletesOnce()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 9, 500, true, default);
		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);

		endpoint.Reply(new(request.RequestId, false, 0));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 2);
		endpoint.Reply(new(request.RequestId, true, 99, 1));

		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(2, completion.AttemptCount);
		Assert.Equal(1, completion.RetryCount);
		Assert.Equal(99, completion.Response.Value);
		Assert.Equal(2, completion.AttemptHistory.Count);
		Assert.Equal(NetworkFailureKind.Remote, completion.AttemptHistory.First.FailureKind);
		Assert.Equal(NetworkFailureKind.None, completion.AttemptHistory.Second.FailureKind);
		Assert.Equal(0, completion.AttemptHistory.First.RetryCount);
		Assert.Equal(1, completion.AttemptHistory.Second.RetryCount);
		Assert.True(completion.TotalElapsedMs >=
			completion.AttemptHistory.First.ElapsedMs + completion.AttemptHistory.Second.ElapsedMs);

		endpoint.Reply(new(request.RequestId, true, 100));
		await Task.Delay(30);
		Assert.Equal(0, endpoint.ReceiveQueueLength);
	}

	[Fact]
	public async Task FailureFromOlderAttemptDoesNotRetryCurrentAttempt()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 5, 500, true, default);
		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 2);

		endpoint.Reply(new(request.RequestId, false, 0, 0));
		await Task.Delay(10);
		Assert.Equal(2, endpoint.AttemptCount(request.RequestId));
		endpoint.Reply(new(request.RequestId, true, 10, 1));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(1, completion.RetryCount);
	}

	[Fact]
	public async Task FullReceiveFifoRetainsSecondCompletionUntilSpaceExists()
	{
		using var endpoint = new FakeEndpoint(maxReceiveQueueLength: 1);
		var first = new FakeRequest(Guid.NewGuid(), 1, 500, true, default);
		var second = new FakeRequest(Guid.NewGuid(), 2, 500, true, default);
		Assert.True(endpoint.TrySend(first));
		Assert.True(endpoint.TrySend(second));
		await WaitUntilAsync(() => endpoint.AttemptCount(first.RequestId) == 1 && endpoint.AttemptCount(second.RequestId) == 1);

		endpoint.Reply(new(first.RequestId, true, 1));
		endpoint.Reply(new(second.RequestId, true, 2));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.Equal(1, endpoint.PendingCount);

		Assert.True(endpoint.TryReadReceived(out var firstCompletion));
		Assert.Equal(1, firstCompletion.Response.Value);
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var secondCompletion));
		Assert.Equal(2, secondCompletion.Response.Value);
		Assert.Equal(0, endpoint.PendingCount);
	}

	[Fact]
	public async Task BatchSendAndBatchReceivePreserveAllCompletions()
	{
		using var endpoint = new FakeEndpoint();
		var requests = Enumerable.Range(1, 4)
			.Select(value => new FakeRequest(Guid.NewGuid(), value, 500, true, default))
			.ToArray();
		Assert.Equal(requests.Length, endpoint.Send(requests));
		await WaitUntilAsync(() => requests.All(request => endpoint.AttemptCount(request.RequestId) == 1));

		foreach (var request in requests)
			endpoint.Reply(new(request.RequestId, true, request.Correlation));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == requests.Length);

		Span<TrackedRequestCompletion<FakeRequest, FakeResponse, int>> buffer =
			stackalloc TrackedRequestCompletion<FakeRequest, FakeResponse, int>[4];
		var read = endpoint.TryReadReceived(buffer);
		Assert.Equal(4, read);
		Assert.Equal([1, 2, 3, 4], buffer[..read].ToArray().Select(value => value.Response.Value));
	}

	[Fact]
	public async Task EachFifoItemPreservesItsOwnRequestPlanToken()
	{
		using var endpoint = new FakeEndpoint();
		const int firstPlanToken = 7;
		const int secondPlanToken = 23;
		var first = new FakeRequest(Guid.NewGuid(), 1, 500, true, firstPlanToken);
		var second = new FakeRequest(Guid.NewGuid(), 2, 500, true, secondPlanToken);

		Assert.Equal(2, endpoint.Send([first, second]));
		await WaitUntilAsync(() => endpoint.AttemptCount(first.RequestId) == 1 && endpoint.AttemptCount(second.RequestId) == 1);
		endpoint.Reply(new(first.RequestId, true, 1));
		endpoint.Reply(new(second.RequestId, true, 2));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 2);
		Assert.True(endpoint.TryReadReceived(out var firstCompletion));
		Assert.True(endpoint.TryReadReceived(out var secondCompletion));
		Assert.Equal(firstPlanToken, firstCompletion.Request.PlanToken);
		Assert.Equal(secondPlanToken, secondCompletion.Request.PlanToken);
	}

	[Fact]
	public void PendingCapacityAppliesBackpressureAfterSendFifoDrains()
	{
		using var endpoint = new FakeEndpoint(maxPendingCount: 2);
		Assert.True(endpoint.TrySend(new(Guid.NewGuid(), 1, 500, true, default)));
		Assert.True(endpoint.TrySend(new(Guid.NewGuid(), 2, 500, true, default)));
		Assert.False(endpoint.TrySend(new(Guid.NewGuid(), 3, 500, true, default)));
		Assert.Equal(2, endpoint.PendingCount);
	}

	[Fact]
	public async Task DuplicateBusinessKeysAreQueryableAndRemainIndependentByRequestId()
	{
		using var endpoint = new FakeEndpoint();
		var first = new FakeRequest(Guid.NewGuid(), 17, 500, true, default);
		var second = new FakeRequest(Guid.NewGuid(), 17, 500, true, default);
		Assert.True(endpoint.TrySend(first));
		Assert.True(endpoint.TrySend(second));
		Assert.Equal(2, endpoint.PendingCount);
		Assert.Equal(2, endpoint.CountPendingByKey(17));
		Assert.Equal(
			new[] { first.RequestId, second.RequestId }.Order(),
			endpoint.GetPendingByKey(17).Select(static item => item.RequestId).Order());

		await WaitUntilAsync(() => endpoint.AttemptCount(first.RequestId) == 1);
		endpoint.Reply(new(first.RequestId, true, 1, Correlation: 999));
		await WaitUntilAsync(() => endpoint.PendingCount == 1);
		Assert.Equal(second.RequestId, Assert.Single(endpoint.GetPendingByKey(17)).RequestId);
		Assert.True(endpoint.TryGetPending(second.RequestId, out _));

		endpoint.Reply(new(second.RequestId, true, 2, Correlation: 999));
		await WaitUntilAsync(() => endpoint.PendingCount == 0);
		Assert.Empty(endpoint.GetPendingByKey(17));
	}

	[Fact]
	public async Task ResponseKeyCannotRedirectTheGuidMatchedRequest()
	{
		using var endpoint = new FakeEndpoint();
		var first = new FakeRequest(Guid.NewGuid(), 101, 500, true, default);
		var second = new FakeRequest(Guid.NewGuid(), 202, 500, true, default);
		Assert.Equal(2, endpoint.Send([first, second]));
		await WaitUntilAsync(() => endpoint.AttemptCount(first.RequestId) == 1 && endpoint.AttemptCount(second.RequestId) == 1);

		endpoint.Reply(new(first.RequestId, true, 7, Correlation: second.Correlation));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(first.RequestId, completion.RequestId);
		Assert.Equal(first.Correlation, completion.Key);
		Assert.Equal(second.Correlation, completion.Response.Correlation);
		Assert.False(endpoint.TryGetPending(first.RequestId, out _));
		Assert.True(endpoint.TryGetPending(second.RequestId, out _));
	}

	[Fact]
	public async Task UnknownRequestIdIsLateEvenWhenItsKeyMatchesPendingWork()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 303, 500, true, default);
		var late = new TaskCompletionSource<TrackedLateResponseEventArgs<FakeResponse, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.LateResponseReceived += (_, args) => late.TrySetResult(args);
		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);

		var unknownRequestId = Guid.NewGuid();
		endpoint.Reply(new(unknownRequestId, true, 1, Correlation: request.Correlation));
		var observed = await late.Task.WaitAsync(TimeSpan.FromSeconds(3));
		Assert.Equal(unknownRequestId, observed.RequestId);
		Assert.Equal(request.Correlation, observed.Key);
		Assert.Equal("response-not-pending", observed.Reason);
		Assert.True(endpoint.TryGetPending(request.RequestId, out _));
		Assert.Equal(1, endpoint.CountPendingByKey(request.Correlation));
	}

	[Fact]
	public void DuplicateRequestIdIsRejectedEvenWhenBusinessKeysDiffer()
	{
		using var endpoint = new FakeEndpoint();
		var requestId = Guid.NewGuid();
		Assert.True(endpoint.TrySend(new(requestId, 1, 500, true, default)));
		Assert.False(endpoint.TrySend(new(requestId, 2, 500, true, default)));
		Assert.Equal(1, endpoint.PendingCount);
		Assert.Equal(1, endpoint.CountPendingByKey(1));
		Assert.Equal(0, endpoint.CountPendingByKey(2));
	}

	[Fact]
	public async Task FinalRequestIdCannotBeReusedInsideTheVisibleBoundary()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 1, 500, true, default);
		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);
		endpoint.Reply(new(request.RequestId, true, 1));
		await WaitUntilAsync(() => endpoint.PendingCount == 0);

		Assert.False(endpoint.TrySend(request with { Correlation = 2 }, out var rejection));
		Assert.Equal(NetworkAccessFailureCodes.RequestIdAlreadyFinal, rejection.Failure.Reason);
		Assert.Equal(0, endpoint.PendingCount);
	}

	[Fact]
	public void CallerKeyComparerControlsObservationWithoutChangingGuidIdentity()
	{
		using var endpoint = new FakeEndpoint(keyComparer: new AbsoluteValueComparer());
		var requestId = Guid.NewGuid();
		Assert.True(endpoint.TrySend(new(requestId, -17, 500, true, default)));
		Assert.Equal(requestId, Assert.Single(endpoint.GetPendingByKey(17)).RequestId);
		Assert.Equal(1, endpoint.CountPendingByKey(-17));
		Assert.Equal(1, endpoint.CountPendingByKey(17));
	}

	[Fact]
	public async Task EmptyRequestIdIsRejectedBeforeFifoAndPending()
	{
		using var endpoint = new FakeEndpoint();
		var rejected = new TaskCompletionSource<TrackedRequestRejection<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestRejected += (_, args) => rejected.TrySetResult(args.Rejection);

		Assert.False(endpoint.TrySend(new(Guid.Empty, 23, 500, true, default), out var synchronous));
		var observed = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkAccessFailureCodes.RequestIdEmpty, synchronous.Failure.Reason);
		Assert.Equal(synchronous, observed);
		Assert.Equal(0, endpoint.SendQueueLength);
		Assert.Equal(0, endpoint.PendingCount);
		Assert.False(endpoint.IsRunning);
	}

	[Fact]
	public async Task SinglePathCompletionCarriesCompleteFourLevelIdentity()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 44, 500, true, default);
		var observedResponse = new TaskCompletionSource<NetworkExecutionIdentity>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.ResponseObserved += (_, args) => observedResponse.TrySetResult(args.Identity);
		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);

		endpoint.Reply(new(request.RequestId, true, 1));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.True(completion.Identity.HasResponse);
		Assert.Equal(request.RequestId, completion.Identity.RequestId);
		Assert.NotEqual(Guid.Empty, completion.Identity.AttemptId);
		Assert.NotEqual(Guid.Empty, completion.Identity.BranchId);
		Assert.NotEqual(Guid.Empty, completion.Identity.ResponseId);
		Assert.Equal(completion.Identity.AttemptId, completion.AttemptHistory.First.Identity.AttemptId);
		Assert.Equal(1, completion.AttemptHistory.First.BranchCount);
		Assert.Equal(completion.Identity, await observedResponse.Task.WaitAsync(TimeSpan.FromSeconds(3)));
	}

	[Fact]
	public async Task MultiBranchAttemptUsesOneAttemptAndDistinctBranches()
	{
		using var endpoint = new FakeEndpoint(branchCount: 2);
		var request = new FakeRequest(Guid.NewGuid(), 51, 500, true, default);
		var branchTerminals = new ConcurrentQueue<NetworkBranchTerminal>();
		var attemptTerminals = new ConcurrentQueue<NetworkAttemptTerminal>();
		endpoint.BranchTerminated += (_, args) => branchTerminals.Enqueue(args.Terminal);
		endpoint.AttemptTerminated += (_, args) => attemptTerminals.Enqueue(args.Terminal);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.BranchIdentities.Count == 2);
		var branches = endpoint.BranchIdentities.ToArray();
		var pendingBranches = endpoint.GetCurrentBranches(request.RequestId);
		Assert.Equal(2, pendingBranches.Length);
		Assert.All(pendingBranches, item => Assert.False(item.IsTerminal));
		Assert.Equal(branches[0].AttemptId, branches[1].AttemptId);
		Assert.NotEqual(branches[0].BranchId, branches[1].BranchId);

		endpoint.Reply(new(request.RequestId, true, 9), branches[1].BranchId);
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1 && branchTerminals.Count == 2);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(branches[1].BranchId, completion.Identity.BranchId);
		Assert.Contains(branchTerminals, item => item.State == NetworkExecutionTerminalState.Succeeded);
		var superseded = Assert.Single(branchTerminals, item => item.State == NetworkExecutionTerminalState.Superseded);
		Assert.Equal(AccessCompliance.NotApplicable, superseded.AccessCompliance);
		var attemptTerminal = Assert.Single(attemptTerminals);
		Assert.Equal(2, attemptTerminal.BranchCount);
		Assert.Equal(ProtocolOutcome.Succeeded, attemptTerminal.ProtocolOutcome);
		Assert.Equal(AccessCompliance.Satisfied, attemptTerminal.AccessCompliance);
	}

	[Fact]
	public async Task LateResponseRetainsOriginalAttemptAndBranchParentChain()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 61, 500, true, default);
		var late = new TaskCompletionSource<TrackedLateResponseEventArgs<FakeResponse, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var branchTerminalCount = 0;
		var attemptTerminalCount = 0;
		var requestTerminalCount = 0;
		endpoint.LateResponseReceived += (_, args) => late.TrySetResult(args);
		endpoint.BranchTerminated += (_, _) => Interlocked.Increment(ref branchTerminalCount);
		endpoint.AttemptTerminated += (_, _) => Interlocked.Increment(ref attemptTerminalCount);
		endpoint.RequestTerminated += (_, _) => Interlocked.Increment(ref requestTerminalCount);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);
		endpoint.Reply(new(request.RequestId, true, 1));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		endpoint.Reply(new(request.RequestId, true, 2));

		var observed = await late.Task.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() =>
			Volatile.Read(ref branchTerminalCount) == 1 &&
			Volatile.Read(ref attemptTerminalCount) == 1 &&
			Volatile.Read(ref requestTerminalCount) == 1);
		Assert.True(observed.Identity.HasResponse);
		Assert.Equal(completion.Identity.RequestId, observed.Identity.RequestId);
		Assert.Equal(completion.Identity.AttemptId, observed.Identity.AttemptId);
		Assert.Equal(completion.Identity.BranchId, observed.Identity.BranchId);
		Assert.NotEqual(completion.Identity.ResponseId, observed.Identity.ResponseId);
		Assert.Equal(1, Volatile.Read(ref branchTerminalCount));
		Assert.Equal(1, Volatile.Read(ref attemptTerminalCount));
		Assert.Equal(1, Volatile.Read(ref requestTerminalCount));
	}

	[Fact]
	public async Task CollectionWindowPublishesEveryResponseAndCreatesOneTerminal()
	{
		using var endpoint = new FakeEndpoint(responsePolicy:
			TrackedResponseCollectionPolicy.CollectUntilWindowEnds(60, 8));
		var request = new FakeRequest(Guid.NewGuid(), 81, 500, false, default);
		var observed = new ConcurrentQueue<NetworkExecutionIdentity>();
		var branchTerminals = new ConcurrentQueue<NetworkBranchTerminal>();
		var attemptTerminals = new ConcurrentQueue<NetworkAttemptTerminal>();
		var requestTerminals = new ConcurrentQueue<NetworkRequestTerminal>();
		endpoint.ResponseObserved += (_, args) => observed.Enqueue(args.Identity);
		endpoint.BranchTerminated += (_, args) => branchTerminals.Enqueue(args.Terminal);
		endpoint.AttemptTerminated += (_, args) => attemptTerminals.Enqueue(args.Terminal);
		endpoint.RequestTerminated += (_, args) => requestTerminals.Enqueue(args.Terminal);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.BranchIdentities.Count == 1);
		var branch = Assert.Single(endpoint.BranchIdentities);
		endpoint.Reply(new(request.RequestId, true, 1, Correlation: 81), branch.BranchId);
		endpoint.Reply(new(request.RequestId, true, 2, Correlation: 81), branch.BranchId);
		await WaitUntilAsync(() => observed.Count == 2);
		Assert.Equal(0, endpoint.ReceiveQueueLength);

		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(2, completion.Response.Value);
		var identities = observed.ToArray();
		Assert.All(identities, identity =>
		{
			Assert.Equal(request.RequestId, identity.RequestId);
			Assert.Equal(branch.AttemptId, identity.AttemptId);
			Assert.Equal(branch.BranchId, identity.BranchId);
			Assert.True(identity.HasResponse);
		});
		Assert.NotEqual(identities[0].ResponseId, identities[1].ResponseId);
		Assert.Single(branchTerminals);
		Assert.Single(attemptTerminals);
		Assert.Single(requestTerminals);
	}

	[Fact]
	public async Task CollectionMaximumClosesImmediatelyAndLaterResponseIsLate()
	{
		using var endpoint = new FakeEndpoint(responsePolicy:
			TrackedResponseCollectionPolicy.CollectUntilWindowEnds(1000, 2));
		var request = new FakeRequest(Guid.NewGuid(), 82, 1500, false, default);
		var late = new TaskCompletionSource<TrackedLateResponseEventArgs<FakeResponse, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.LateResponseReceived += (_, args) => late.TrySetResult(args);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.BranchIdentities.Count == 1);
		var branch = Assert.Single(endpoint.BranchIdentities);
		endpoint.Reply(new(request.RequestId, true, 1, Correlation: 82), branch.BranchId);
		endpoint.Reply(new(request.RequestId, true, 2, Correlation: 82), branch.BranchId);
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.Equal(2, completion.Response.Value);

		endpoint.Reply(new(request.RequestId, true, 3, Correlation: 82), branch.BranchId);
		var lateResponse = await late.Task.WaitAsync(TimeSpan.FromSeconds(3));
		Assert.Equal(NetworkAccessFailureCodes.ResponseNotPending, lateResponse.Reason);
		Assert.Equal(branch.BranchId, lateResponse.Identity.BranchId);
		Assert.True(lateResponse.Identity.HasResponse);
	}

	[Fact]
	public async Task EmptyCollectionWindowFailsWithoutFabricatingResponse()
	{
		using var endpoint = new FakeEndpoint(responsePolicy:
			TrackedResponseCollectionPolicy.CollectUntilWindowEnds(40, 4));
		var request = new FakeRequest(Guid.NewGuid(), 83, 500, false, default);
		var failureSource = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failureSource.TrySetResult(args.Failure);

		Assert.True(endpoint.TrySend(request));
		var failure = await failureSource.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkAccessFailureCodes.ResponseCollectionWindowEmpty, failure.Failure.Reason);
		Assert.Equal(NetworkFailureKind.Timeout, failure.Failure.Kind);
		Assert.Equal(0, endpoint.ReceiveQueueLength);
	}

	[Fact]
	public async Task CollectionWindowPreservesLastConcreteFailureWhenNoValidResponseArrives()
	{
		using var endpoint = new FakeEndpoint(responsePolicy:
			TrackedResponseCollectionPolicy.CollectUntilWindowEnds(50, 4));
		var request = new FakeRequest(Guid.NewGuid(), 85, 500, false, default);
		var failed = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.BranchIdentities.Count == 1);
		var branch = Assert.Single(endpoint.BranchIdentities);
		endpoint.Reply(new(request.RequestId, false, 0, Correlation: 85), branch.BranchId);
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkFailureKind.Remote, failure.Failure.Kind);
		Assert.Equal("fake-error", failure.Failure.Reason);
	}

	[Fact]
	public async Task InvalidCollectionPolicyUsesStableRejectionReason()
	{
		using var endpoint = new FakeEndpoint(responsePolicy: new(
			TrackedResponseCollectionMode.CollectUntilWindowEnds, 0, 4));
		var request = new FakeRequest(Guid.NewGuid(), 84, 500, false, default);
		var failed = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);

		Assert.True(endpoint.TrySend(request));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkFailureKind.Rejected, failure.Failure.Kind);
		Assert.Equal(NetworkAccessFailureCodes.ResponseCollectionPolicyInvalid, failure.Failure.Reason);
	}

	[Fact]
	public async Task NoResponseBranchPreservesTimedOutAndSatisfiedFacts()
	{
		using var endpoint = new FakeEndpoint(maxAttemptCount: 1);
		var request = new FakeRequest(Guid.NewGuid(), 70, 1000, false, default);
		var branchTerminal = new TaskCompletionSource<NetworkBranchTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
		var attemptTerminal = new TaskCompletionSource<NetworkAttemptTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
		var requestTerminal = new TaskCompletionSource<NetworkRequestTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.BranchTerminated += (_, args) => branchTerminal.TrySetResult(args.Terminal);
		endpoint.AttemptTerminated += (_, args) => attemptTerminal.TrySetResult(args.Terminal);
		endpoint.RequestTerminated += (_, args) => requestTerminal.TrySetResult(args.Terminal);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.BranchIdentities.TryPeek(out _));
		Assert.True(endpoint.BranchIdentities.TryPeek(out var identity));
		endpoint.CloseBranch(
			request.RequestId,
			identity.BranchId,
			ProtocolOutcome.TimedOut,
			AccessCompliance.Satisfied,
			new NetworkFailure(NetworkFailureKind.Timeout, 0, "response-window-empty", default));

		var branch = await branchTerminal.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var attempt = await attemptTerminal.Task.WaitAsync(TimeSpan.FromSeconds(3));
		var terminal = await requestTerminal.Task.WaitAsync(TimeSpan.FromSeconds(3));
		Assert.Equal(ProtocolOutcome.TimedOut, branch.ProtocolOutcome);
		Assert.Equal(AccessCompliance.Satisfied, branch.AccessCompliance);
		Assert.Equal(ProtocolOutcome.TimedOut, attempt.ProtocolOutcome);
		Assert.Equal(AccessCompliance.Satisfied, attempt.AccessCompliance);
		Assert.Equal(ProtocolOutcome.TimedOut, terminal.ProtocolOutcome);
		Assert.Equal(AccessCompliance.Satisfied, terminal.AccessCompliance);
	}

	[Fact]
	public async Task StopCancelsEachExecutionLevelExactlyOnce()
	{
		using var endpoint = new FakeEndpoint();
		var request = new FakeRequest(Guid.NewGuid(), 71, 5000, true, default);
		var failed = new TaskCompletionSource<TrackedRequestFailure<FakeRequest, int>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var branchTerminals = new ConcurrentQueue<NetworkBranchTerminal>();
		var attemptTerminals = new ConcurrentQueue<NetworkAttemptTerminal>();
		var requestTerminals = new ConcurrentQueue<NetworkRequestTerminal>();
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		endpoint.BranchTerminated += (_, args) => branchTerminals.Enqueue(args.Terminal);
		endpoint.AttemptTerminated += (_, args) => attemptTerminals.Enqueue(args.Terminal);
		endpoint.RequestTerminated += (_, args) => requestTerminals.Enqueue(args.Terminal);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.AttemptCount(request.RequestId) == 1);
		endpoint.Stop();
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => branchTerminals.Count == 1 && attemptTerminals.Count == 1 && requestTerminals.Count == 1);

		Assert.Equal(NetworkFailureKind.Stopped, failure.Failure.Kind);
		Assert.Equal(NetworkExecutionTerminalState.Cancelled, Assert.Single(branchTerminals).State);
		Assert.Equal(NetworkExecutionTerminalState.Cancelled, Assert.Single(attemptTerminals).State);
		Assert.Equal(NetworkExecutionTerminalState.Cancelled, Assert.Single(requestTerminals).State);
		endpoint.Reply(new(request.RequestId, true, 1));
		await Task.Delay(20);
		Assert.Single(branchTerminals);
		Assert.Single(attemptTerminals);
		Assert.Single(requestTerminals);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(3);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline) throw new TimeoutException("test-condition-timeout");
			await Task.Delay(5);
		}
	}

	private readonly record struct FakeRequest(
		Guid RequestId,
		int Correlation,
		int? TimeoutMs,
		bool AllowRetry,
		int PlanToken);

	private readonly record struct FakeResponse(
		Guid RequestId,
		bool Success,
		int Value,
		byte RetryCount = 0,
		int Correlation = 0);

	private sealed class FakeEndpoint
		: TrackedRequestReplyEndpointBase<FakeRequest, FakeResponse, int>
	{
		private readonly ConcurrentDictionary<Guid, int> _attempts = new();
		private readonly byte _branchCount;
		private readonly TrackedResponseCollectionPolicy _responsePolicy;
		private int _planningFailureCount;

		public FakeEndpoint(
			int maxAttemptCount = 3,
			int maxReceiveQueueLength = 16,
			int maxPendingCount = 64,
			IEqualityComparer<int>? keyComparer = null,
			int planningFailureCount = 0,
			byte branchCount = 1,
			TrackedResponseCollectionPolicy? responsePolicy = null)
			: base(
				keyComparer ?? EqualityComparer<int>.Default,
				64,
				maxReceiveQueueLength,
				maxAttemptCount,
				defaultTimeoutMs: 100,
				pendingSweepIntervalMs: 10,
				maxPendingCount: maxPendingCount)
		{
			_planningFailureCount = planningFailureCount;
			_branchCount = branchCount;
			_responsePolicy = responsePolicy ?? TrackedResponseCollectionPolicy.FirstValidResponse();
		}

		public ConcurrentQueue<NetworkExecutionIdentity> BranchIdentities { get; } = new();
		public int AttemptCount(Guid requestId) => _attempts.TryGetValue(requestId, out var value) ? value : 0;
		public void Reply(FakeResponse response) => PublishResponse(response);
		public void Reply(FakeResponse response, Guid branchId) => PublishResponse(response, branchId);
		public void CloseBranch(
			Guid requestId,
			Guid branchId,
			ProtocolOutcome outcome,
			AccessCompliance compliance,
			NetworkFailure failure) => PublishBranchTerminal(requestId, branchId, outcome, compliance, failure);
		protected override Guid GetRequestId(FakeRequest request) => request.RequestId;
		protected override int GetRequestKey(FakeRequest request) => request.Correlation;
		protected override Guid GetResponseRequestId(FakeResponse response) => response.RequestId;
		protected override int GetResponseKey(FakeResponse response) => response.Correlation;
		protected override int? GetRequestTimeoutOverrideMs(FakeRequest request) => request.TimeoutMs;
		protected override bool CanRetry(FakeRequest request, NetworkFailure failure) => request.AllowRetry;
		protected override byte? GetResponseRetryCount(FakeResponse response) => response.RetryCount;
		protected override TrackedResponseCollectionPolicy GetResponseCollectionPolicy(FakeRequest request)
		{
			if (Interlocked.Decrement(ref _planningFailureCount) >= 0)
				throw new InvalidOperationException("attempt-planning-test-failure");
			return _responsePolicy;
		}
		protected override bool IsSuccessfulResponse(FakeResponse response, out NetworkFailure failure)
		{
			failure = response.Success
				? NetworkFailure.None
				: new(NetworkFailureKind.Remote, 1, "fake-error", default);
			return response.Success;
		}
		protected override byte GetAttemptBranchCount(FakeRequest request, byte retryCount)
			=> _branchCount;
		protected override TrackedAttemptStartResult OnStartBranch(
			FakeRequest request,
			byte retryCount,
			byte branchNumber,
			NetworkExecutionIdentity identity,
			int timeoutMs,
			CancellationToken cancellationToken)
		{
			BranchIdentities.Enqueue(identity);
			return branchNumber == 1
				? OnStartAttempt(request, retryCount, timeoutMs, cancellationToken)
				: TrackedAttemptStartResult.Accepted();
		}
		protected override TrackedAttemptStartResult OnStartAttempt(
			FakeRequest request,
			byte retryCount,
			int timeoutMs,
			CancellationToken cancellationToken)
		{
			_attempts.AddOrUpdate(request.RequestId, 1, static (_, current) => current + 1);
			return TrackedAttemptStartResult.Accepted();
		}
	}

	private sealed class ConcurrentStartEndpoint
		: TrackedRequestReplyEndpointBase<FakeRequest, FakeResponse, int>
	{
		private readonly ManualResetEventSlim _release = new(false);
		private int _startedCount;

		public ConcurrentStartEndpoint()
			: base(defaultTimeoutMs: 3000)
		{
		}

		public int StartedCount => Volatile.Read(ref _startedCount);
		public void ReleaseStarts() => _release.Set();
		protected override bool StartAttemptsConcurrently => true;
		protected override Guid GetRequestId(FakeRequest request) => request.RequestId;
		protected override int GetRequestKey(FakeRequest request) => request.Correlation;
		protected override Guid GetResponseRequestId(FakeResponse response) => response.RequestId;
		protected override int GetResponseKey(FakeResponse response) => response.Correlation;
		protected override int? GetRequestTimeoutOverrideMs(FakeRequest request) => request.TimeoutMs;
		protected override TrackedAttemptStartResult OnStartAttempt(
			FakeRequest request,
			byte retryCount,
			int timeoutMs,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _startedCount);
			_release.Wait(cancellationToken);
			return TrackedAttemptStartResult.Accepted();
		}

	}

	private sealed class AbsoluteValueComparer : IEqualityComparer<int>
	{
		public bool Equals(int x, int y) => Math.Abs((long)x) == Math.Abs((long)y);
		public int GetHashCode(int value) => Math.Abs((long)value).GetHashCode();
	}
}
