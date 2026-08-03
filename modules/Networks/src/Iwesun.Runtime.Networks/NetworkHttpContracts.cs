namespace Iwesun.Runtime.Networks;

public enum NetworkHttpConnectionReusePolicy : byte
{
	Unspecified = 0,
	Reusable = 1,
	NoReuseRequestPolicy = 2,
}

/// <summary>Protocol and TLS identity kept separate from transport path selectors.</summary>
public readonly record struct NetworkHttpProtocolIdentity(
	string Scheme,
	string LogicalHost,
	string ServerNameIndication,
	string CertificateValidationName,
	ushort Port,
	byte HttpMajorVersion,
	NetworkHttpConnectionReusePolicy ReusePolicy,
	string CertificatePinSha256 = "")
{
	public bool IsValid =>
		Scheme is "http" or "https" &&
		!string.IsNullOrWhiteSpace(LogicalHost) &&
		Port > 0 && HttpMajorVersion > 0 &&
		ReusePolicy != NetworkHttpConnectionReusePolicy.Unspecified &&
		(IsValidPin(CertificatePinSha256)) &&
		(Scheme == "http" ||
		 (!string.IsNullOrWhiteSpace(ServerNameIndication) &&
		  !string.IsNullOrWhiteSpace(CertificateValidationName)));

	public static bool TryCreate(
		Uri uri,
		string? logicalHost,
		string? serverNameIndication,
		string? certificateValidationName,
		byte httpMajorVersion,
		NetworkHttpConnectionReusePolicy reusePolicy,
		out NetworkHttpProtocolIdentity identity,
		out string reason)
	{
		ArgumentNullException.ThrowIfNull(uri);
		var scheme = uri.Scheme.Trim().ToLowerInvariant();
		var uriHost = NormalizeHost(uri.IdnHost);
		var host = NormalizeHost(string.IsNullOrWhiteSpace(logicalHost) ? uriHost : logicalHost);
		var sni = scheme == Uri.UriSchemeHttps
			? NormalizeHost(string.IsNullOrWhiteSpace(serverNameIndication) ? host : serverNameIndication)
			: string.Empty;
		var certificateName = scheme == Uri.UriSchemeHttps
			? NormalizeHost(string.IsNullOrWhiteSpace(certificateValidationName) ? sni : certificateValidationName)
			: string.Empty;
		var port = checked((ushort)(uri.IsDefaultPort
			? scheme == Uri.UriSchemeHttps ? 443 : 80
			: uri.Port));
		identity = new NetworkHttpProtocolIdentity(
			scheme, host, sni, certificateName, port, httpMajorVersion, reusePolicy);
		if (!identity.IsValid || (scheme != Uri.UriSchemeHttp && scheme != Uri.UriSchemeHttps))
		{
			identity = default;
			reason = NetworkAccessFailureCodes.HttpProtocolIdentityInvalid;
			return false;
		}
		reason = string.Empty;
		return true;
	}

	public NetworkAccessSecurityBoundary ToSecurityBoundary(NetworkAccessLeg leg, string resolverIdentity) => new(
		leg,
		LogicalHost,
		ServerNameIndication,
		CertificateValidationName,
		resolverIdentity?.Trim().ToLowerInvariant() ?? string.Empty);

	private static string NormalizeHost(string? value) =>
		(value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

	private static bool IsValidPin(string value) => string.IsNullOrEmpty(value) ||
		(value.Length == 64 && value.All(static character =>
			character is >= '0' and <= '9' or >= 'a' and <= 'f'));
}

/// <summary>Stable HttpClient/connection-pool key; no tracking or attempt policy fields are accepted.</summary>
public readonly record struct NetworkHttpConnectionPoolKey(
	NetworkAccessStableKey AccessKey,
	string Scheme,
	string LogicalHost,
	string ServerNameIndication,
	string CertificateValidationName,
	ushort Port,
	byte HttpMajorVersion,
	NetworkHttpConnectionReusePolicy ReusePolicy,
	string CertificatePinSha256,
	string PathProviderIdentity)
{
	public bool IsValid => AccessKey.IsValid &&
		!string.IsNullOrWhiteSpace(Scheme) &&
		!string.IsNullOrWhiteSpace(LogicalHost) &&
		Port > 0 && HttpMajorVersion > 0 &&
		ReusePolicy != NetworkHttpConnectionReusePolicy.Unspecified;

	public static bool TryCreate(
		in RequestedAccessPlan plan,
		in NetworkHttpProtocolIdentity protocol,
		string resolverIdentity,
		out NetworkHttpConnectionPoolKey key,
		out string reason) => TryCreate(
			plan, protocol, resolverIdentity, string.Empty, out key, out reason);

	public static bool TryCreate(
		in RequestedAccessPlan plan,
		in NetworkHttpProtocolIdentity protocol,
		string resolverIdentity,
		string pathProviderIdentity,
		out NetworkHttpConnectionPoolKey key,
		out string reason)
	{
		reason = string.Empty;
		if (!protocol.IsValid ||
			!NetworkAccessStableKey.TryCreate(
				plan,
				protocol.ToSecurityBoundary(
					plan.PathProvider is NetworkPathProvider.RouteAdapter or NetworkPathProvider.SystemProxy
						? NetworkAccessLeg.ClientLeg
						: NetworkAccessLeg.Direct,
					resolverIdentity),
				out var accessKey,
				out reason))
		{
			key = default;
			if (string.IsNullOrEmpty(reason)) reason = NetworkAccessFailureCodes.HttpProtocolIdentityInvalid;
			return false;
		}

		key = new NetworkHttpConnectionPoolKey(
			accessKey,
			protocol.Scheme,
			protocol.LogicalHost,
			protocol.ServerNameIndication,
			protocol.CertificateValidationName,
			protocol.Port,
			protocol.HttpMajorVersion,
			protocol.ReusePolicy,
			protocol.CertificatePinSha256,
			pathProviderIdentity?.Trim().ToLowerInvariant() ?? string.Empty);
		reason = string.Empty;
		return true;
	}
}

public readonly record struct NetworkHttpPathEvidence(
	NetworkExecutionIdentity Identity,
	NetworkRouteLegEvidence ClientLeg,
	NetworkRouteLegEvidence EgressLeg)
{
	public bool IsValid => Identity.HasBranch && ClientLeg.IsValid && EgressLeg.IsValid;
}
