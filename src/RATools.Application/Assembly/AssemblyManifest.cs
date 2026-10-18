using System.Text;
using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Application.Assembly;

public enum AssemblyRowStatus { Valid, Invalid }

public sealed record AssemblyDiagnostic(string Code, string Message, int? LineNumber = null);

// One previewed manifest row. Nothing here has been written: a valid row means it could
// be executed against the named revision, not that it was. Invalid rows are reported in
// full instead of being silently skipped.
public sealed record AssemblyPlanRow(int LineNumber, string? DocumentId, string? NodeInstanceId,
    string? Title, string Operation, string? TargetLeafAddress, int SortOrder,
    AssemblyRowStatus Status, IReadOnlyList<AssemblyDiagnostic> Diagnostics);

public sealed record AssemblyPlan(Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    string PlanDigest, int TotalRows, int ValidRows, int InvalidRows, IReadOnlyList<AssemblyDiagnostic> Diagnostics,
    IReadOnlyList<AssemblyPlanRow> Rows, string CsvVersion = AssemblyManifest.SchemaVersion);

public sealed record AssemblyManifestParseResult(IReadOnlyList<AssemblyManifestRow> Rows,
    IReadOnlyList<AssemblyDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(diagnostic => diagnostic.Code is not (
        "ASSEMBLY_HEADER_MISSING" or "ASSEMBLY_ROW_LIMIT" or "ASSEMBLY_CSV_UNTERMINATED_QUOTE"));
}

public sealed record AssemblyManifestRow(int LineNumber, IReadOnlyDictionary<string, string> Fields);

// Strict, bounded CSV parsing for the bulk assembly manifest. It accepts UTF-8 with a BOM,
// RFC 4180 quoting (embedded commas, quotes and newlines) and CRLF or LF line endings.
// Unknown columns are reported; missing required columns reject the whole manifest.
public static class AssemblyManifest
{
    public const string SchemaVersion = "assembly-manifest-v1";
    public const int MaxRows = 1000;
    public const int MaxCharacters = 2 * 1024 * 1024;
    public static readonly string[] RequiredColumns =
        ["documentId", "nodeInstanceId", "operation"];
    public static readonly string[] KnownColumns =
        ["documentId", "nodeInstanceId", "operation", "title", "targetLeafAddress", "sortOrder"];

    public static AssemblyManifestParseResult Parse(string? csv)
    {
        var diagnostics = new List<AssemblyDiagnostic>();
        if (string.IsNullOrWhiteSpace(csv))
            return Failed("ASSEMBLY_HEADER_MISSING", "The manifest is empty.");
        if (csv.Length > MaxCharacters)
            return Failed("ASSEMBLY_ROW_LIMIT", $"The manifest exceeds {MaxCharacters} characters.");
        if (csv[0] == '﻿') csv = csv[1..];

        var records = new List<(int Line, List<string> Fields)>();
        if (!TryReadRecords(csv, records, diagnostics)) return new([], diagnostics);

        var header = records.FirstOrDefault();
        if (header.Fields is null || header.Fields.Count == 0)
            return Failed("ASSEMBLY_HEADER_MISSING", "The manifest has no header row.");
        var columns = header.Fields.Select(value => value.Trim()).ToArray();
        if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
            return Failed("ASSEMBLY_HEADER_DUPLICATE", "The manifest header contains duplicate column names.");
        foreach (var missing in RequiredColumns.Where(column => !columns.Contains(column, StringComparer.Ordinal)))
            diagnostics.Add(new("ASSEMBLY_HEADER_MISSING", $"The manifest is missing the required column '{missing}'."));
        if (diagnostics.Count > 0) return new([], diagnostics);
        foreach (var unknown in columns.Where(column => !KnownColumns.Contains(column, StringComparer.Ordinal)))
            diagnostics.Add(new("ASSEMBLY_COLUMN_UNKNOWN", $"Column '{unknown}' is not part of the manifest contract."));

        var rows = new List<AssemblyManifestRow>();
        foreach (var (line, fields) in records.Skip(1))
        {
            // A completely empty record is a formatting artifact, not a row.
            if (fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0])) continue;
            if (rows.Count == MaxRows)
                return Failed("ASSEMBLY_ROW_LIMIT", $"The manifest exceeds {MaxRows} data rows.");
            if (fields.Count > columns.Length)
                diagnostics.Add(new("ASSEMBLY_ROW_EXTRA_FIELDS",
                    $"Row {line} has {fields.Count} fields but the header declares {columns.Length}.", line));
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < columns.Length; index++)
                values[columns[index]] = index < fields.Count ? fields[index].Trim() : string.Empty;
            rows.Add(new AssemblyManifestRow(line, values));
        }
        return new(rows, diagnostics);
    }

    private static bool TryReadRecords(string csv, List<(int, List<string>)> records, List<AssemblyDiagnostic> diagnostics)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else inQuotes = false;
                    continue;
                }
                if (character == '\n') line++;
                field.Append(character);
                continue;
            }
            switch (character)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case '"':
                    diagnostics.Add(new("ASSEMBLY_CSV_UNEXPECTED_QUOTE",
                        $"A quote appears inside an unquoted field on line {line}.", line));
                    return false;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add((recordLine, fields));
                    fields = [];
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }
        if (inQuotes)
        {
            diagnostics.Add(new("ASSEMBLY_CSV_UNTERMINATED_QUOTE", "The manifest ends inside a quoted field.", line));
            return false;
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }
        return true;
    }

    private static AssemblyManifestParseResult Failed(string code, string message) =>
        new([], [new AssemblyDiagnostic(code, message)]);

    public static string ComputeDigest(Guid applicationId, string sequenceNumber, long expectedRevision,
        IReadOnlyList<AssemblyPlanRow> rows)
    {
        var normalized = rows.Select(row => new
        {
            row.LineNumber, row.DocumentId, row.NodeInstanceId, row.Title,
            row.Operation, row.TargetLeafAddress, row.SortOrder
        }).ToArray();
        return CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = SchemaVersion, applicationId = applicationId.ToString("D"), sequenceNumber,
            expectedRevision, rows = normalized
        }));
    }
}
