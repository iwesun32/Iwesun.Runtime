using Microsoft.Extensions.Logging;

namespace Iwesun.Runtime.Diagnostics;

[ProviderAlias("RuntimeDiagnostics")]
public sealed class RuntimeDiagnosticLoggerProvider : ILoggerProvider
{
	private readonly RuntimeDiagnosticLoggerOptions _options;

	public RuntimeDiagnosticLoggerProvider(RuntimeDiagnosticLoggerOptions? options = null)
	{
		_options = options ?? new RuntimeDiagnosticLoggerOptions();
	}

	public ILogger CreateLogger(string categoryName) =>
		new RuntimeDiagnosticLogger(categoryName, _options);

	public void Dispose()
	{
	}
}

public sealed class RuntimeDiagnosticLoggerOptions
{
	public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;
	public bool IncludeScopes { get; set; } = true;
}

internal sealed class RuntimeDiagnosticLogger : ILogger
{
	private readonly string _categoryName;
	private readonly RuntimeDiagnosticLoggerOptions _options;

	public RuntimeDiagnosticLogger(string categoryName, RuntimeDiagnosticLoggerOptions options)
	{
		_categoryName = categoryName;
		_options = options;
	}

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

	public bool IsEnabled(LogLevel logLevel) =>
		logLevel != LogLevel.None
		&& logLevel >= _options.MinimumLevel
		&& RuntimeOutputSwitch.Enabled;

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
			return;

		var message = formatter(state, exception);
		if (string.IsNullOrWhiteSpace(message) && exception == null)
			return;

		var section = RuntimeDiagnosticLogSection.Infer(_categoryName, message);
		var outputPointId = RuntimeDiagnosticLogSection.InferOutputPointId(section);
		var kind = $"log.{logLevel.ToString().ToLowerInvariant()}";
		DiagnosticSwitchboard.ReportPoint(outputPointId, section, kind, message, new
		{
			category = _categoryName,
			level = logLevel.ToString(),
			eventId = eventId.Id,
			eventName = eventId.Name,
			exception = exception == null ? null : new
			{
				type = exception.GetType().FullName,
				exception.Message,
				stackTrace = exception.StackTrace
			},
			state = CaptureState(state)
		});
	}

	private static IReadOnlyDictionary<string, object?>? CaptureState<TState>(TState state)
	{
		if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
			return null;

		var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in pairs)
		{
			if (pair.Key == "{OriginalFormat}")
				continue;
			values[pair.Key] = pair.Value;
		}

		return values.Count == 0 ? null : values;
	}

	private sealed class NullScope : IDisposable
	{
		public static readonly NullScope Instance = new();

		public void Dispose()
		{
		}
	}
}

internal static class RuntimeDiagnosticLogSection
{
	public static string Infer(string categoryName, string message)
	{
		var category = categoryName ?? "";
		var text = message ?? "";

		if (Contains(category, "Pipeline") || StartsWithAny(text, "Collect:", "DnsAnalysis:", "DnsQuery:", "DnsStateJudge:", "DnsUpdate:", "DnsVerifyQuery:", "FileLoad:", "MergeTreeSources:", "PublicIpVerify:", "PublishConsume:"))
			return "pipeline";
		if (Contains(category, "DnsProvider") || Contains(category, "DnsProviders") || StartsWithAny(text, "Dns"))
			return "dns";
		if (Contains(category, "Network") || Contains(category, "IpDetection") || Contains(category, "Scanner"))
			return "network";
		if (Contains(category, "PeerSync"))
			return "peer-sync";
		if (Contains(category, "Agent") && !Contains(category, "Agent.UI"))
			return "agent-sync";
		if (Contains(category, "AddressProbing") || Contains(category, "ConnectivityProber"))
			return "address-probing";
		if (Contains(category, "Configuration") || Contains(category, "ConfigStore"))
			return "configuration";
		if (Contains(category, "Security") || Contains(category, "Token") || Contains(category, "Certificate"))
			return "security";
		if (Contains(category, "DdnsSnap.Agent"))
			return "agent";
		if (Contains(category, "DdnsSnap.UI") || Contains(category, "DdnsSnap.Agent.UI"))
			return "ui";
		if (Contains(category, "DdnsSnap.Core"))
			return "core";
		if (Contains(category, "DdnsSnap.Service"))
			return "service";

		return "console";
	}

	public static string InferOutputPointId(string section) =>
		section.ToLowerInvariant() switch
		{
			"pipeline" => "log.pipeline",
			"dns" => "log.dns",
			"network" => "log.network",
			"peer-sync" => "log.peer-sync",
			"agent-sync" => "log.agent-sync",
			"address-probing" => "log.address-probing",
			"configuration" => "log.configuration",
			"security" => "log.security",
			"agent" => "log.agent",
			"ui" => "log.ui",
			"core" => "log.core",
			"service" => "log.service",
			_ => "log.console"
		};

	private static bool Contains(string value, string fragment) =>
		value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

	private static bool StartsWithAny(string value, params string[] prefixes) =>
		prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
