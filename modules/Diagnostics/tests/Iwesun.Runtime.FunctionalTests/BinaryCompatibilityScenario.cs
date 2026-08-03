using System.Reflection;

internal static class BinaryCompatibilityScenario
{
	public static Task<FunctionalScenarioResult> RunAsync()
	{
		const string scenario = "binary-compatibility";
		var assemblyPath = Environment.GetEnvironmentVariable("IWESUN_RUNTIME_BINARY_COMPAT_HOST");
		if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
		{
			return Task.FromResult(FunctionalScenarioResult.Fail(
				scenario,
				Array.Empty<string>(),
				new[] { "IWESUN_RUNTIME_BINARY_COMPAT_HOST must identify the host compiled against the previous Runtime release." }));
		}

		try
		{
			var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
			var diagnosticsReference = assembly.GetReferencedAssemblies()
				.SingleOrDefault(reference => reference.Name == "Iwesun.Runtime.Diagnostics");
			if (diagnosticsReference?.Version != new Version(1, 0, 42, 0))
			{
				return Task.FromResult(FunctionalScenarioResult.Fail(
					scenario,
					Array.Empty<string>(),
					new[] { $"Compatibility host must reference Iwesun.Runtime.Diagnostics 1.0.42.0, actual={diagnosticsReference?.Version}." }));
			}
			var consumer = assembly.GetType("Iwesun.Runtime.BinaryCompatibilityHost.LegacyConstructorConsumer", throwOnError: true)!;
			var result = consumer.GetMethod("CreateTargets", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null) as string;
			if (result != "legacy-constructors-bound")
				return Task.FromResult(FunctionalScenarioResult.Fail(scenario, Array.Empty<string>(), new[] { $"Unexpected compatibility result: {result}" }));

			return CompleteHostValidationAsync(consumer);
		}
		catch (TargetInvocationException ex)
		{
			return Task.FromResult(FunctionalScenarioResult.Fail(
				scenario,
				Array.Empty<string>(),
				new[] { $"Legacy host invocation failed: {ex.InnerException ?? ex}" }));
		}
	}

	private static async Task<FunctionalScenarioResult> CompleteHostValidationAsync(Type consumer)
	{
		var invocation = consumer.GetMethod("RunHostShutdownAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
		if (invocation is not Task<string> hostTask)
			return FunctionalScenarioResult.Fail("binary-compatibility", Array.Empty<string>(), new[] { "Legacy host lifecycle method did not return Task<string>." });
		var result = await hostTask.ConfigureAwait(false);
		return result == "legacy-host-shutdown-0"
			? FunctionalScenarioResult.Pass(
				"binary-compatibility",
				"previous-release-reference-version-verified",
				"previous-release-host-loaded",
				"legacy-target-constructors-bound-to-current-runtime",
				"legacy-host-di-activate-shutdown-exit-0")
			: FunctionalScenarioResult.Fail("binary-compatibility", Array.Empty<string>(), new[] { result });
	}
}
