using System.Text.Json;
using Iwesun.Runtime.Diagnostics;

internal static class RuntimeDiagnosticJsonScenario
{
	public static async Task<FunctionalScenarioResult> RunAsync()
	{
		const string scenario = "diagnostic-json";
		var checks = new List<string>();
		var failures = new List<string>();

		var sample = new DerivedDiagnosticPayload
		{
			Name = "layout",
			NotANumber = double.NaN,
			PositiveInfinity = double.PositiveInfinity,
			NegativeInfinity = float.NegativeInfinity
		};
		var sampleJson = sample.ToJsonElement();
		Check(sampleJson.GetProperty("notANumber").GetString() == "NaN", "NaN is represented explicitly.");
		Check(sampleJson.GetProperty("positiveInfinity").GetString() == "Infinity", "Positive infinity is represented explicitly.");
		Check(sampleJson.GetProperty("negativeInfinity").GetString() == "-Infinity", "Negative infinity is represented explicitly.");

		var cycle = new CyclicPayload { Name = "root" };
		cycle.Next = cycle;
		var cycleJson = RuntimeDiagnosticJson.SerializePayload(cycle);
		Check(cycleJson.GetProperty("name").GetString() == "root", "Cyclic payload keeps serializable members.");
		Check(cycleJson.GetProperty("next").ValueKind == JsonValueKind.Null, "Cyclic reference is cut without throwing.");

		var failureJson = RuntimeDiagnosticJson.SerializePayload(new ThrowingPayload());
		Check(failureJson.GetProperty("kind").GetString() == "serializationFailure", "Getter failure becomes a structured payload.");
		Check(failureJson.GetProperty("sourceType").GetString()?.Contains(nameof(ThrowingPayload), StringComparison.Ordinal) == true, "Failure payload retains source type.");

		var frame = new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader { Schema = RuntimeDiagnosticProtocol.V2Schema },
			Data = sampleJson
		};
		await using var stream = new MemoryStream();
		await RuntimeFramePipeCodec.WriteAsync(stream, frame, CancellationToken.None);
		stream.Position = 0;
		var roundTrip = await RuntimeFramePipeCodec.ReadAsync(stream, 1024 * 1024, CancellationToken.None);
		Check(roundTrip.Data?.GetProperty("positiveInfinity").GetString() == "Infinity", "Frame codec preserves named floating-point values.");

		return failures.Count == 0
			? FunctionalScenarioResult.Pass(scenario, checks.ToArray())
			: FunctionalScenarioResult.Fail(scenario, checks, failures);

		void Check(bool condition, string message)
		{
			if (condition)
				checks.Add(message);
			else
				failures.Add(message);
		}
	}

	private abstract class BaseDiagnosticPayload : RuntimeDiagnosticJsonModel
	{
		public string Name { get; init; } = "";
	}

	private sealed class DerivedDiagnosticPayload : BaseDiagnosticPayload
	{
		public double NotANumber { get; init; }
		public double PositiveInfinity { get; init; }
		public float NegativeInfinity { get; init; }
	}

	private sealed class CyclicPayload
	{
		public string Name { get; init; } = "";
		public CyclicPayload? Next { get; set; }
	}

	private sealed class ThrowingPayload
	{
		public string Value => throw new InvalidOperationException("getter failed");
	}
}
