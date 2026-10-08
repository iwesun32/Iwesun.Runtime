using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Release tooling emits bounded build summaries, not Runtime diagnostic logs.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
if (!File.Exists(Path.Combine(root, "Iwesun.Runtime.slnx")))
    throw new InvalidOperationException("Run the documentation tool from its repository build output.");
var action = args.FirstOrDefault() ?? "audit";
var tracked = Git("ls-files", "--cached", "--others", "--exclude-standard", "-z")
    .Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order().ToArray();
var paths = tracked.Where(IsDocument).Where(p => File.Exists(Full(p))).ToArray();
var records = paths.Select(Describe).ToArray();
var links = FindLinks(records.Where(r => r.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))).ToArray();
var missing = links.Where(l => l.Kind == "missing").ToArray();
if (action == "audit")
{
    Print(new
    {
        files = records.Length,
        markdown = records.Count(r => r.Path.EndsWith(".md")),
        lines = records.Sum(r => r.Lines),
        bytes = records.Sum(r => r.Bytes),
        groups = records.GroupBy(r => r.Group).Select(g => new { name = g.Key, files = g.Count() }),
        localLinks = links.Length,
        missingReferences = missing.Length,
        repositoryOnlyReferences = links.Count(l => l.Kind == "repository-only"),
        outsideRepositoryReferences = links.Count(l => l.Kind == "outside-scope"),
        sample = missing.Take(25),
        referenceChecks = "Relative Markdown file/directory targets only; code fences and the MSI-layout index are excluded. URL reachability and heading anchors are not checked.",
        excluded = new[] { "Tables", "Web development project", "archives", "private handoffs", "build outputs" }
    });
    Environment.ExitCode = missing.Length == 0 ? 0 : 1;
    return;
}
if (action == "catalog")
{
    var result = new StringBuilder("# 完整文档目录 / Complete documentation catalog\n\n");
    result.AppendLine("[规范总纲](SPECIFICATION.md) · [Specification framework](en/SPECIFICATION.md)\n");
    result.AppendLine("本表枚举公开范围内的 Markdown 正文；另附模板、命令配置与许可资料。历史记录不因列入目录而成为现行规范。\n");
    result.AppendLine("This lists the complete public Markdown corpus. Most detailed references are Chinese originals, not full English translations. Historical records are not current contracts.\n");
    result.AppendLine("| 分组 / Group | 文档 / Document |\n| --- | --- |");
    foreach (var record in records.Where(r => r.Path.EndsWith(".md")))
    {
        var link = record.Path.StartsWith("docs/") ? record.Path[5..] : "../" + record.Path;
        result.AppendLine($"| {record.Group} | [{Escape(record.Title)}]({EncodePath(link)}) |");
    }
    result.AppendLine("\n## 配套文本 / Supporting text\n\n| 分组 / Group | 文件 / File |\n| --- | --- |");
    foreach (var record in records.Where(r => !r.Path.EndsWith(".md")))
        result.AppendLine($"| {record.Group} | [{Escape(record.Path)}]({EncodePath("../" + record.Path)}) |");
    Console.Out.Write(result.ToString());
    return;
}
if (action == "package")
{
    if (args.Length != 3 || !Regex.IsMatch(args[1], "^[A-Za-z0-9.-]+$") || !Regex.IsMatch(args[2], "^[A-Za-z0-9.-]+$"))
        throw new ArgumentException("Use: package <revision> <tag>.");
    if (Git("status", "--porcelain").Length != 0)
        throw new InvalidOperationException("Commit the complete documentation before packaging.");
    if (missing.Length != 0)
        throw new InvalidOperationException("Resolve missing documentation references before packaging.");
    var commit = Git("rev-parse", "HEAD").Trim();
    var expected = Git("rev-list", "-n", "1", args[2]).Trim();
    if (commit != expected) throw new InvalidOperationException("The documentation tag must identify HEAD.");
    var packageName = "Iwesun.Runtime.Documentation." + args[1];
    var output = Full("artifacts/documentation/" + packageName);
    if (Directory.Exists(output)) throw new IOException("A documentation candidate already exists; never overwrite it.");
    Directory.CreateDirectory(output);
    var stage = Path.Combine(output, "content");
    Directory.CreateDirectory(stage);
    foreach (var record in records)
    {
        var target = Path.Combine(stage, record.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Full(record.Path), target, overwrite: false);
        if (Hash(target) != record.Sha256) throw new IOException("Copy hash mismatch: " + record.Path);
    }
    var manifest = new
    {
        schema = 1, revision = args[1], runtime = "1.0.47-beta.1", networks = "3.0.0-beta.6",
        sourceCommit = commit, sourceTag = args[2], sourceRepository = "https://github.com/iwesun32/Iwesun.Runtime",
        fileCount = records.Length, markdownCount = records.Count(r => r.Path.EndsWith(".md")),
        sourceLines = records.Sum(r => r.Lines), files = records,
        references = new { sourceLayoutPreserved = true, missingReferences = missing.Length, unresolved = missing,
            repositoryOnly = links.Where(l => l.Kind == "repository-only"), outsideRepository = links.Where(l => l.Kind == "outside-scope") },
        limits = new[] { "Detailed references remain primarily Chinese.", "Some source-code and historical references require the tagged repository.", "Installer-layout indexes apply to the MSI/portable runtime payload, not this source-layout archive.", "No Tables, Web development project, private handoffs, binary payloads or ignored archives." }
    };
    var manifestName = packageName + ".manifest.json";
    File.WriteAllText(Path.Combine(stage, "MANIFEST.json"), JsonSerializer.Serialize(manifest, JsonOptions()), new UTF8Encoding(false));
    var index = new StringBuilder("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>iwesun Runtime · 完整规范 / Complete specification</title><style>body{font:16px/1.65 system-ui;max-width:1150px;margin:40px auto;padding:0 20px;color:#152438}a{color:#1558a3}table{border-collapse:collapse;width:100%}td,th{padding:8px;border-bottom:1px solid #ddd;text-align:left}small{color:#546577}code{overflow-wrap:anywhere}</style><h1>iwesun Runtime 完整规范文档</h1><p>Complete specification and documentation set</p>");
    index.AppendLine($"<p>Runtime 1.0.47-beta.1 · docs {WebUtility.HtmlEncode(args[1])} · <code>{commit}</code></p>");
    index.AppendLine("<p><a href=\"docs/SPECIFICATION.md\">中文规范总纲</a> · <a href=\"docs/en/SPECIFICATION.md\">English specification framework</a> · <a href=\"docs/DOCUMENTATION_CATALOG.md\">完整目录 / Full catalog</a></p>");
    index.AppendLine("<p>保留全文与原始目录。离线文件请用 Markdown 阅读器打开；Online 链接读取固定标签的网页版本。大部分深入规范仍为中文原文。源码链接可通过对应标签源代码包阅读；安装布局索引仅适用于安装包。</p><p>Full texts and source paths are preserved. Open local files in a Markdown reader, or use Online for the pinned web copy. This is a specification set, not a zero-configuration quickstart.</p><table><tr><th>Domain</th><th>Local text</th><th>Web</th></tr>");
    foreach (var record in records)
        index.AppendLine($"<tr><td>{WebUtility.HtmlEncode(record.Group)}</td><td><a href=\"{EncodePath(record.Path)}\">{WebUtility.HtmlEncode(record.Title)}</a></td><td><a href=\"https://github.com/iwesun32/Iwesun.Runtime/blob/{args[2]}/{EncodePath(record.Path)}\">Online</a></td></tr>");
    index.AppendLine("</table><p>See MANIFEST.json for per-file hashes and reference limitations. Copyright © 2026 iwesun. LICENSE applies.</p></html>");
    File.WriteAllText(Path.Combine(stage, "INDEX.html"), index.ToString(), new UTF8Encoding(false));
    var zip = Path.Combine(output, packageName + ".zip");
    ZipFile.CreateFromDirectory(stage, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
    File.Copy(Path.Combine(stage, "MANIFEST.json"), Path.Combine(output, manifestName));
    var sums = new[] { zip, Path.Combine(output, manifestName) }.Select(p => $"{Hash(p)}  {Path.GetFileName(p)}");
    File.WriteAllLines(Path.Combine(output, packageName + ".SHA256SUMS.txt"), sums, new UTF8Encoding(false));
    Print(new { success = true, sourceCommit = commit, output, files = records.Length, markdown = records.Count(r => r.Path.EndsWith(".md")), copyHashesVerified = records.Length, bytes = new FileInfo(zip).Length, sha256 = Hash(zip), missingReferences = missing.Length });
    return;
}
throw new ArgumentException("Actions: audit, catalog, package.");

string Git(params string[] arguments)
{
    var info = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var value in arguments) info.ArgumentList.Add(value);
    using var process = Process.Start(info) ?? throw new IOException("Unable to start git.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException("Git inventory failed: " + error);
    return output;
}
string Full(string relative)
{
    var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escaped repository.");
    return path;
}
bool IsDocument(string path)
{
    if (Regex.IsMatch(path, @"(^|/)(bin|obj|artifacts[^/]*|archive|ai-ignore|_ai_ignore|node_modules|packages|\.git)/") || path.Contains("/HANDOFF_", StringComparison.Ordinal)) return false;
    if (path.StartsWith("modules/Tables/") || path.StartsWith("modules/Web/")) return false;
    if (!path.Contains('/')) return path.EndsWith(".md") || path is "LICENSE" or "NOTICE" or ".copilotignore";
    if (path.StartsWith("legal/third-party/")) return path.EndsWith(".md") || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    if (path.StartsWith(".github/instructions/")) return path.EndsWith(".md");
    if (path == "scripts/release/DocumentationTool/README.md") return true;
    if (path.StartsWith("skills/iwesun-runtime-integration/")) return path.EndsWith(".md") || path.EndsWith(".yaml");
    if (path.StartsWith("docs/")) return path.EndsWith(".md") && !path.Contains("IWESUN_RUNTIME_PUBLIC_RELEASE_DESIGN") && !path.Contains("IWESUN_RUNTIME_SOURCE_LICENSE_GOVERNANCE_DRAFT");
    if (!Regex.IsMatch(path, @"^modules/(Diagnostics|Data|Networks|WebView2|Cli|RemoteConsole|Packaging)/")) return false;
    return path.EndsWith(".md") || path.Contains("/templates/") && path.EndsWith(".txt")
        || path is "modules/Cli/src/Iwesun.Runtime.Cli/RuntimeCliSystemConfig.json" or "modules/Cli/src/Iwesun.Runtime.Cli/RuntimeCliSystemMetadata.json" or "modules/Cli/src/Iwesun.Runtime.Cli/RuntimeCliUserConfig.example.json";
}
Document Describe(string path)
{
    var text = File.ReadAllText(Full(path));
    var title = text.Split('\n').Take(12).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# "))?[2..] ?? Path.GetFileName(path);
    var group = path.StartsWith("docs/") ? "Runtime" : path.StartsWith("modules/") ? path.Split('/')[1] : path.StartsWith("skills/") ? "AI skill" : path.StartsWith("legal/") ? "Third-party" : "Project";
    return new(path, title, group, new FileInfo(Full(path)).Length, text.Count(c => c == '\n') + (text.EndsWith('\n') ? 0 : 1), Hash(Full(path)));
}
IEnumerable<Link> FindLinks(IEnumerable<Document> documents)
{
    foreach (var document in documents)
    {
        if (document.Path == "docs/IWESUN_RUNTIME_RELEASE_INDEX.md") continue;
        var lineNumber = 0;
        var fenced = false;
        foreach (var line in File.ReadLines(Full(document.Path)))
        {
            lineNumber++;
            if (line.TrimStart().StartsWith("```")) { fenced = !fenced; continue; }
            if (fenced) continue;
            foreach (Match match in Regex.Matches(line, @"\]\(([^\s)]+)(?:\s+""[^""]*"")?\)"))
            {
                var target = Uri.UnescapeDataString(match.Groups[1].Value.Trim('<', '>').Split('#')[0]);
                if (target.Length == 0 || Regex.IsMatch(target, @"^[a-zA-Z][a-zA-Z0-9+.-]*:") || target.StartsWith("//")) continue;
                var absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Full(document.Path))!, target.Replace('/', Path.DirectorySeparatorChar)));
                var kind = !absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "outside-scope" : File.Exists(absolute) || Directory.Exists(absolute) ? "present" : "missing";
                if (kind == "present")
                {
                    var relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
                    if (!paths.Contains(relative, StringComparer.OrdinalIgnoreCase)
                        && !paths.Any(p => p.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase))) kind = "repository-only";
                }
                yield return new(document.Path, lineNumber, target, kind);
            }
        }
    }
}
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
string Escape(string value) => value.Replace("|", "\\|").Replace("[", "\\[").Replace("]", "\\]");
string EncodePath(string value) => string.Join('/', value.Split('/').Select(s => s is "." or ".." ? s : Uri.EscapeDataString(s)));
JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
void Print(object value) => Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions()));
record Document(string Path, string Title, string Group, long Bytes, int Lines, string Sha256);
record Link(string Document, int Line, string Target, string Kind);
