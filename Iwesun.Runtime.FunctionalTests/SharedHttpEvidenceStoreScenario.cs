using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.WebView2;

internal static class SharedHttpEvidenceStoreScenario
{
    public static async Task<FunctionalScenarioResult> RunAsync()
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "iwesun-shared-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "source.json");
            var content = "{\"message\":\"shared\"}";
            await File.WriteAllTextAsync(source, content, new UTF8Encoding(false));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var input = new WebRuntimeSharedHttpResourceInput
            {
                Sequence = 1,
                CapturedAt = DateTimeOffset.UtcNow,
                Method = "GET",
                Url = "https://example.test/data.json",
                Status = 200,
                ContentType = "application/json",
                BodyFilePath = source,
                BodyLength = Encoding.UTF8.GetByteCount(content),
                BodySha256 = hash
            };

            var first = await WebRuntimeSharedHttpEvidenceStore.PreserveAsync(root, [input]);
            using (var unlinkedCatalog = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "catalog.json"))))
            {
                if (unlinkedCatalog.RootElement.GetProperty("resources")[0].GetProperty("references").GetArrayLength() == 0)
                    checks.Add("startup-preservation-without-snapshot-reference");
                else
                    failures.Add("Preserving a startup HTTP resource unexpectedly created a snapshot reference.");
            }
            await WebRuntimeSharedHttpEvidenceStore.LinkAsync(root, "page/version/main", first);
            var second = await WebRuntimeSharedHttpEvidenceStore.RegisterAsync(root, "page/version/variants/open", [input]);
            if (first.Count == 1 && second.Count == 1 && first[0].RecordId == second[0].RecordId)
                checks.Add("stable-record-id");
            else
                failures.Add("Equivalent HTTP inputs did not resolve to one stable resource record.");

            var bodies = Directory.EnumerateFiles(Path.Combine(root, "bodies")).ToArray();
            if (bodies.Length == 1)
                checks.Add("sha256-body-deduplication");
            else
                failures.Add($"Expected one shared body, found {bodies.Length}.");

            using var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "catalog.json")));
            var resources = catalog.RootElement.GetProperty("resources");
            var references = resources[0].GetProperty("references").EnumerateArray()
                .Select(item => item.GetString()).ToArray();
            if (resources.GetArrayLength() == 1
                && references.SequenceEqual(new[] { "page/version/main", "page/version/variants/open" }, StringComparer.Ordinal))
                checks.Add("snapshot-links-and-reverse-references");
            else
                failures.Add("Shared catalog did not retain both snapshot back-references.");
        }
        catch (Exception exception)
        {
            failures.Add(exception.ToString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        return failures.Count == 0
            ? FunctionalScenarioResult.Pass("shared-http-evidence", checks.ToArray())
            : FunctionalScenarioResult.Fail("shared-http-evidence", checks, failures);
    }
}
