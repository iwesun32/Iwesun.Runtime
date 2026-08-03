namespace Iwesun.Runtime.Networks;

/// <summary>Stable machine-readable failure reasons for Networks 3.0 contracts.</summary>
public static class NetworkAccessFailureCodes
{
	public const string RequestIdEmpty = "request-id-empty";
	public const string RequestIdDuplicate = "request-id-duplicate";
	public const string RequestIdAlreadyFinal = "request-id-already-final";
	public const string PendingCapacityReached = "pending-capacity-reached";
	public const string NetworkFlowCapacityReached = "network-flow-capacity-reached";
	public const string SendFifoOverflow = "send-fifo-overflow";
	public const string AttemptBranchCountZero = "attempt-branch-count-zero";
	public const string AttemptPlanThrew = "attempt-plan-threw";
	public const string AttemptProcessingThrew = "attempt-processing-threw";
	public const string BranchStartNotSupported = "branch-start-not-supported";
	public const string ResponseNotPending = "response-not-pending";
	public const string ResponseFromOlderAttempt = "response-from-older-attempt";
	public const string ResponseBranchAmbiguous = "response-branch-ambiguous";
	public const string ResponseBranchUnknown = "response-branch-unknown";
	public const string ResponseAlreadyFinal = "response-already-final";
	public const string ResponseCollectionPolicyInvalid = "response-collection-policy-invalid";
	public const string ResponseCollectionWindowEmpty = "response-collection-window-empty";
	public const string PathProviderUnspecified = "path-provider-unspecified";
	public const string SelectorUnspecified = "selector-unspecified";
	public const string SelectorSetEmpty = "selector-set-empty";
	public const string SelectorValueInvalid = "selector-value-invalid";
	public const string SelectorAddressFamilyMismatch = "selector-address-family-mismatch";
	public const string SelectorDerivedSourceInvalid = "selector-derived-source-invalid";
	public const string SelectorDerivedCycle = "selector-derived-cycle";
	public const string SelectorNotApplicableUnsupported = "selector-not-applicable-unsupported";
	public const string AutomaticExactConstraintUnsupported = "automatic-exact-constraint-unsupported";
	public const string LocalEndpointInvalid = "local-endpoint-invalid";
	public const string RouteScopeInvalid = "route-scope-invalid";
	public const string PacketPolicyUnspecified = "packet-policy-unspecified";
	public const string RouteAdapterIdentityMissing = "route-adapter-identity-missing";
	public const string CapabilityUnsupported = "capability-unsupported";
	public const string PlatformPermissionMissing = "platform-permission-missing";
	public const string AccessEvidenceIncomplete = "access-evidence-incomplete";
	public const string AccessConstraintViolated = "access-constraint-violated";
	public const string InterfaceCatalogInvalid = "interface-catalog-invalid";
	public const string InterfaceIdentityNotFound = "interface-identity-not-found";
	public const string InterfaceIdentityAmbiguous = "interface-identity-ambiguous";
	public const string RouteSnapshotInvalid = "route-snapshot-invalid";
	public const string ConstraintUnresolved = "constraint-unresolved";
	public const string SourceInterfaceConflict = "source-interface-conflict";
	public const string SelectorDerivedUnresolved = "selector-derived-unresolved";
	public const string SocketRequestInvalid = "socket-request-invalid";
	public const string ResolvedPlanStale = "resolved-plan-stale";
	public const string ResolvedPlanMismatch = "resolved-plan-mismatch";
	public const string ExactNextHopBackendUnavailable = "exact-next-hop-backend-unavailable";
	public const string WfpPolicyRequestInvalid = "wfp-policy-request-invalid";
	public const string WfpPolicyIsolationCollision = "wfp-policy-isolation-collision";
	public const string WfpPolicyAddFailed = "wfp-policy-add-failed";
	public const string WfpPolicyCleanupFailed = "wfp-policy-cleanup-failed";
	public const string WfpPolicyConnectionReuseUnsupported = "wfp-policy-connection-reuse-unsupported";
	public const string RouteScopeBackendUnavailable = "route-scope-backend-unavailable";
	public const string SocketOperationFailed = "socket-operation-failed";
	public const string PlatformNotSupported = "platform-not-supported";
	public const string RouteQueryFailed = "route-query-failed";
	public const string PingRequestInvalid = "ping-request-invalid";
	public const string PingPacketPolicyBackendUnavailable = "ping-packet-policy-backend-unavailable";
	public const string PingMultipleResponsesUnsupported = "ping-multiple-responses-unsupported";
	public const string DnsPtrRequestInvalid = "dns-ptr-request-invalid";
	public const string DnsResolverUnavailable = "dns-resolver-unavailable";
	public const string DnsPtrResponseInvalid = "dns-ptr-response-invalid";
	public const string NetBiosRequestInvalid = "netbios-request-invalid";
	public const string NetBiosMultipleResponsesUnsupported = "netbios-multiple-responses-unsupported";
	public const string NetBiosResponseInvalid = "netbios-response-invalid";
	public const string HttpProtocolIdentityInvalid = "http-protocol-identity-invalid";
	public const string RouteAdapterCapabilityVersionMismatch = "route-adapter-capability-version-mismatch";
	public const string RouteAdapterTransportUnsupported = "route-adapter-transport-unsupported";
	public const string RouteAdapterEgressConstraintUnsupported = "route-adapter-egress-constraint-unsupported";
	public const string RouteAdapterConnectionInvalid = "route-adapter-connection-invalid";
	public const string HttpRequestInvalid = "http-request-invalid";
	public const string SystemProxyConstraintUnsupported = "system-proxy-constraint-unsupported";
	public const string DohRequestInvalid = "doh-request-invalid";
	public const string DohAnswerEmpty = "doh-answer-empty";
}

/// <summary>Structured platform error data; ReasonCode remains platform-independent.</summary>
public readonly record struct NetworkPlatformError(
	string Platform,
	int NativeCode,
	int HResult,
	string Category)
{
	public bool IsValid => !string.IsNullOrWhiteSpace(Platform) && !string.IsNullOrWhiteSpace(Category);
}

public readonly record struct NetworkAccessFailure(string ReasonCode, NetworkPlatformError PlatformError)
{
	public bool IsValid => !string.IsNullOrWhiteSpace(ReasonCode);
}
