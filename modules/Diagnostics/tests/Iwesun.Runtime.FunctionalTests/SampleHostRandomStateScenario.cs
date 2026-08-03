using Iwesun.Runtime.SampleHost;

internal static class SampleHostRandomStateScenario
{
    public static FunctionalScenarioResult Run()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var state = new SampleHostRandomState();
        var eventCount = 0;
        state.Updated += (_, _) => eventCount++;

        state.Record(17);
        state.Record(83);
        var snapshot = state.Snapshot();

        Check(snapshot.CurrentValue == 83, "random-current-value");
        Check(snapshot.PreviousValue == 17, "random-previous-value");
        Check(snapshot.MinimumValue == 17, "random-minimum-value");
        Check(snapshot.MaximumValue == 83, "random-maximum-value");
        Check(snapshot.SampleCount == 2, "random-sample-count");
        Check(snapshot.UpdatedAt > DateTimeOffset.MinValue, "random-updated-at");
        Check(eventCount == 2, "random-updated-event");

        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("sample-host-random-state", checks.ToArray())
            : FunctionalScenarioResult.Fail("sample-host-random-state", checks, failures);

        void Check(bool condition, string name)
        {
            if (condition) checks.Add(name);
            else failures.Add(name);
        }
    }
}
