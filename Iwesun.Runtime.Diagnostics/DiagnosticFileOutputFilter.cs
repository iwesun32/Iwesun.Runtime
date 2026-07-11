using System.Text;
using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Format options for diagnostic file output.
/// Configured via <see cref="DiagnosticFileOutputAttribute"/> or the JSON config <c>fileFormat</c> field.
/// </summary>
public enum DiagnosticFileFormat
{
    /// <summary>Compact JSON – one JSON object per line (JSONL). Default.</summary>
    CompactJson,
    /// <summary>Indented JSON block per entry, entries separated by a blank line.</summary>
    PrettyJson,
    /// <summary>Human-readable text: timestamp | section | kind | message, payload indented on the next line.</summary>
    PlainText,
}

/// <summary>
/// Flattened view of a diagnostic entry passed to the file output filter.
/// Decouples the filter from the internal FIFO envelope record.
/// </summary>
public sealed record DiagnosticFileRecord(
    string? OutputPointId,
    string Section,
    string Kind,
    string Message,
    JsonElement? Payload,
    DateTimeOffset Timestamp,
    int ProcessId,
    int ManagedThreadId,
    string StatementId);

/// <summary>
/// Formats diagnostic entries for file output.
/// Implement auto-formatting (newlines, alignment, indentation) according to the selected <see cref="DiagnosticFileFormat"/>.
/// </summary>
public static class DiagnosticFileOutputFilter
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Format a record according to the specified <paramref name="format"/>.
    /// Returns the complete string to write to the file (not including the trailing newline).
    /// </summary>
    public static string Format(DiagnosticFileRecord record, DiagnosticFileFormat format)
    {
        return format switch
        {
            DiagnosticFileFormat.PrettyJson => FormatPrettyJson(record),
            DiagnosticFileFormat.PlainText  => FormatPlainText(record),
            _                               => FormatCompactJson(record),
        };
    }

    // ── CompactJson ──────────────────────────────────────────────────────────

    private static string FormatCompactJson(DiagnosticFileRecord r)
    {
        return JsonSerializer.Serialize(new
        {
            timestamp         = r.Timestamp,
            section           = r.Section,
            kind              = r.Kind,
            message           = r.Message,
            statementId       = r.StatementId,
            outputPointId     = r.OutputPointId,
            processId         = r.ProcessId,
            managedThreadId   = r.ManagedThreadId,
            payload           = r.Payload,
        });
    }

    // ── PrettyJson ───────────────────────────────────────────────────────────

    private static string FormatPrettyJson(DiagnosticFileRecord r)
    {
        var json = JsonSerializer.Serialize(new
        {
            timestamp         = r.Timestamp,
            section           = r.Section,
            kind              = r.Kind,
            message           = r.Message,
            statementId       = r.StatementId,
            outputPointId     = r.OutputPointId,
            processId         = r.ProcessId,
            managedThreadId   = r.ManagedThreadId,
            payload           = r.Payload,
        }, PrettyJsonOptions);

        // Add a trailing blank line so consecutive blocks are visually separated.
        return json + Environment.NewLine;
    }

    // ── PlainText ────────────────────────────────────────────────────────────

    private static string FormatPlainText(DiagnosticFileRecord r)
    {
        var sb = new StringBuilder(256);

        // Header line:  2025-07-12T10:30:45Z  [section]  [kind]  message
        var ts = r.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        sb.Append(ts);
        sb.Append("  [");
        sb.Append(PadRight(r.Section, 16));
        sb.Append("] [");
        sb.Append(PadRight(r.Kind, 18));
        sb.Append("]  ");
        sb.AppendLine(r.Message);

        // Meta line:  pid:1234  tid:5  stmt:a1b2c3d4  pt:output.point.id
        sb.Append("  pid:");
        sb.Append(PadRight(r.ProcessId.ToString(), 6));
        sb.Append("  tid:");
        sb.Append(PadRight(r.ManagedThreadId.ToString(), 4));
        sb.Append("  stmt:");
        sb.Append(PadRight(r.StatementId, 16));
        if (!string.IsNullOrWhiteSpace(r.OutputPointId))
        {
            sb.Append("  pt:");
            sb.Append(r.OutputPointId);
        }
        sb.AppendLine();

        // Payload line (compact JSON, indented two spaces):
        if (r.Payload.HasValue && r.Payload.Value.ValueKind != JsonValueKind.Null)
        {
            sb.Append("  payload: ");
            sb.AppendLine(r.Payload.Value.GetRawText());
        }

        return sb.ToString().TrimEnd();
    }

    private static string PadRight(string value, int width)
        => value.Length >= width ? value : value + new string(' ', width - value.Length);
}
