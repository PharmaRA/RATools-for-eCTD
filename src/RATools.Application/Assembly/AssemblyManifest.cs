using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using RATools.Domain.Common;

namespace RATools.Application.Assembly;

public enum AssemblyRowStatus { Valid, Invalid }

public sealed record AssemblyDiagnostic(string Code, string Message, int? LineNumber = null);

// One previewed manifest row. Nothing here has been written: a valid row means it could
// be executed against the named revision, not that it was. Invalid rows are reported in
// full instead of being silently skipped.
public sealed record AssemblyPlanRow(int LineNumber, string? DocumentId, string? NodeInstanceId,
    string? Title, string Operation, string? TargetLeafAddress, int SortOrder,
    AssemblyRowStatus Status, IReadOnlyList<AssemblyDiagnostic> Diagnostics,
    string? SourceFile = null, string? FileName = null, string? SourceSha256 = null, long? SourceLength = null,
    string? TargetHref = null);

public sealed record AssemblyPlan(Guid ApplicationId, string SequenceNumber, long ExpectedRevision,
    string PlanDigest, int TotalRows, int ValidRows, int InvalidRows, IReadOnlyList<AssemblyDiagnostic> Diagnostics,
    IReadOnlyList<AssemblyPlanRow> Rows, string CsvVersion = AssemblyManifest.SchemaVersion,
    Guid? Id = null, DateTime? ExpiresUtc = null, string? ManifestSha256 = null,
    IReadOnlyList<int>? SelectedRows = null);

public sealed record AssemblyManifestParseResult(IReadOnlyList<AssemblyManifestRow> Rows,
    IReadOnlyList<AssemblyDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(diagnostic => diagnostic.Code == "ASSEMBLY_COLUMN_UNKNOWN");
}

public sealed record AssemblyManifestRow(int LineNumber, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<AssemblyDiagnostic>? Diagnostics = null);

// Strict, bounded CSV parsing for the bulk assembly manifest. It accepts UTF-8 with a BOM,
// RFC 4180 quoting (embedded commas, quotes and newlines) and CRLF or LF line endings.
// Unknown columns are reported; missing required columns reject the whole manifest.
public static class AssemblyManifest
{
    public const string SchemaVersion = "assembly-manifest-v2";
    public const int MaxRows = 1000;
    public const int MaxCharacters = 2 * 1024 * 1024;
    public static readonly string[] RequiredColumns =
        ["sourceFile", "nodeInstanceId", "fileName", "operation"];
    private static readonly string[] CommonRequiredColumns = ["nodeInstanceId", "operation"];
    public static readonly string[] KnownColumns =
        ["sourceFile", "documentId", "nodeInstanceId", "fileName", "operation", "title", "targetLeafAddress", "sortOrder"];

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
        foreach (var missing in CommonRequiredColumns.Where(column => !columns.Contains(column, StringComparer.Ordinal)))
            diagnostics.Add(new("ASSEMBLY_HEADER_MISSING", $"The manifest is missing the required column '{missing}'."));
        if (!columns.Contains("sourceFile", StringComparer.Ordinal) && !columns.Contains("documentId", StringComparer.Ordinal))
            diagnostics.Add(new("ASSEMBLY_HEADER_MISSING", "The manifest must declare sourceFile (or the legacy documentId alias)."));
        if (columns.Contains("sourceFile", StringComparer.Ordinal) && !columns.Contains("fileName", StringComparer.Ordinal))
            diagnostics.Add(new("ASSEMBLY_HEADER_MISSING", "A sourceFile manifest must declare fileName."));
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
        var quoteClosed = false;
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
                    else
                    {
                        inQuotes = false;
                        quoteClosed = true;
                    }
                    continue;
                }
                if (character == '\n') line++;
                field.Append(character);
                continue;
            }
            if (quoteClosed && character is not (',' or '\r' or '\n'))
            {
                diagnostics.Add(new("ASSEMBLY_CSV_CHARACTERS_AFTER_QUOTE",
                    $"Unexpected text follows a quoted field on line {line}.", line));
                return false;
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
                    quoteClosed = false;
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add((recordLine, fields));
                    fields = [];
                    quoteClosed = false;
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

    public static AssemblyManifestParseResult Parse(byte[] bytes, string format)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxCharacters) return Failed("ASSEMBLY_INPUT_LIMIT", "The manifest exceeds the 2 MiB input limit.");
        if (format == "csv")
        {
            try { return Parse(new UTF8Encoding(false, true).GetString(bytes)); }
            catch (DecoderFallbackException) { return Failed("ASSEMBLY_ENCODING_INVALID", "CSV must be valid UTF-8."); }
        }
        if (format != "xlsx") return Failed("ASSEMBLY_FORMAT_UNSUPPORTED", "Use csv or xlsx.");
        try { return ReadWorkbook(bytes); }
        catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException or IOException)
        { return Failed("ASSEMBLY_XLSX_INVALID", "The workbook is malformed or exceeds its data-only limits."); }
    }

    private static AssemblyManifestParseResult ReadWorkbook(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        const long expandedLimit = 8 * 1024 * 1024;
        if (zip.Entries.Count > 64 || zip.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count ||
            zip.Entries.Sum(entry => entry.Length) > expandedLimit || zip.Entries.Any(entry =>
            entry.FullName.StartsWith('/') || entry.FullName.Contains('\\') || entry.FullName.Split('/').Contains("..") ||
            entry.Length > expandedLimit || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000))
            throw new InvalidDataException();
        if (zip.Entries.Any(entry => entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)))
            return Failed("ASSEMBLY_XLSX_ACTIVE_CONTENT", "Macros and external workbook links are not supported.");
        XDocument Read(string path)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException();
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = expandedLimit, MaxCharactersFromEntities = 0 });
            return XDocument.Load(reader);
        }
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (var relationships in zip.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            if (Read(relationships.FullName).Descendants().Any(item => string.Equals(item.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase)))
                return Failed("ASSEMBLY_XLSX_ACTIVE_CONTENT", "External workbook relationships are not supported.");
        var workbook = Read("xl/workbook.xml");
        var sheets = workbook.Descendants(main + "sheet").ToArray();
        if (sheets.Length != 1) return Failed("ASSEMBLY_XLSX_SHEET_LIMIT", "Exactly one data worksheet is supported.");
        var relations = Read("xl/_rels/workbook.xml.rels");
        var matchingRelations = relations.Descendants().Where(item => item.Attribute("Id")?.Value == sheets[0].Attribute(rel + "id")?.Value).Take(2).ToArray();
        if (matchingRelations.Length != 1) throw new InvalidDataException();
        var relation = matchingRelations[0];
        var target = relation?.Attribute("Target")?.Value ?? throw new InvalidDataException();
        if (target.StartsWith('/') || target.Contains('\\') || target.Split('/').Any(part => part is ".." or "." or ""))
            throw new InvalidDataException();
        var path = target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
        var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Read("xl/sharedStrings.xml").Descendants(main + "si")
            .Select(item => string.Concat(item.Descendants(main + "t").Select(text => text.Value))).ToArray();
        var worksheet = Read(path);
        var allRows = worksheet.Descendants(main + "row").ToArray();
        if (allRows.Length > MaxRows + 1 || worksheet.Descendants(main + "c").Count() > (MaxRows + 1) * 32)
            return Failed("ASSEMBLY_ROW_LIMIT", "The workbook exceeds row or cell limits.");
        var csv = new StringBuilder();
        var issues = new List<IReadOnlyList<AssemblyDiagnostic>>();
        var previousRow = 0;
        foreach (var row in allRows)
        {
            var rowIssues = new List<AssemblyDiagnostic>();
            if (!int.TryParse(row.Attribute("r")?.Value, out var worksheetRow) || worksheetRow <= previousRow || worksheetRow > MaxRows + 1)
                throw new InvalidDataException();
            previousRow = worksheetRow;
            var values = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(main + "c"))
            {
                var address = cell.Attribute("r")?.Value ?? throw new InvalidDataException();
                var column = 0; var cursor = 0;
                while (cursor < address.Length && address[cursor] is >= 'A' and <= 'Z')
                {
                    column = column * 26 + address[cursor++] - 'A' + 1;
                    if (column > 32) throw new InvalidDataException();
                }
                if (column < 1 || cursor == address.Length || !int.TryParse(address[cursor..], out var rowNumber) || rowNumber != worksheetRow)
                    throw new InvalidDataException();
                var type = cell.Attribute("t")?.Value;
                var value = cell.Element(main + "v")?.Value ?? "";
                if (type == "s") value = int.TryParse(value, out var index) && index >= 0 && index < shared.Length ? shared[index] : throw new InvalidDataException();
                else if (type == "inlineStr") value = string.Concat(cell.Descendants(main + "t").Select(item => item.Value));
                else if (type is not (null or "str" or "n"))
                {
                    rowIssues.Add(new("ASSEMBLY_XLSX_CELL_UNSUPPORTED", "Boolean, error and other unsupported cells require explicit text values.", worksheetRow));
                }
                if (cell.Element(main + "f") is not null)
                {
                    rowIssues.Add(new("ASSEMBLY_XLSX_FORMULA_UNSUPPORTED", "Formula cells are never evaluated; replace them with explicit data.", worksheetRow));
                }
                if (!values.TryAdd(column, value)) throw new InvalidDataException();
            }
            issues.Add(rowIssues);
            for (var column = 1; column <= values.Keys.DefaultIfEmpty(0).Max(); column++)
            {
                if (column > 1) csv.Append(',');
                csv.Append('"').Append(values.GetValueOrDefault(column, "").Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            }
            csv.Append('\n');
        }
        var parsed = Parse(csv.ToString());
        if (issues.Count > 0 && issues[0].Count > 0) return Failed("ASSEMBLY_XLSX_HEADER_INVALID", "The header must contain literal text values.");
        // CSV physical line numbers change when a legal XLSX cell contains a
        // newline. Associate diagnostics by record ordinal, never physical CSV line.
        if (parsed.Rows.Count != Math.Max(0, allRows.Length - 1))
            return Failed("ASSEMBLY_XLSX_EMPTY_ROW_UNSUPPORTED", "Remove completely empty worksheet rows before import.");
        return parsed with { Rows = parsed.Rows.Select((row, index) => row with { LineNumber = int.TryParse(allRows[index + 1].Attribute("r")?.Value, out var number) ? number : index + 2,
            Diagnostics = issues[index + 1] }).ToArray() };
    }

    private static AssemblyManifestParseResult Failed(string code, string message) =>
        new([], [new AssemblyDiagnostic(code, message)]);

    public static string ComputeDigest(Guid applicationId, string sequenceNumber, long expectedRevision,
        IReadOnlyList<AssemblyPlanRow> rows)
    {
        var normalized = rows.Select(row => new
        {
            row.LineNumber, row.DocumentId, row.NodeInstanceId, row.Title,
            row.Operation, row.TargetLeafAddress, row.SortOrder, row.SourceFile, row.FileName,
            row.SourceSha256, row.SourceLength, row.TargetHref
        }).ToArray();
        return CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = SchemaVersion, applicationId = applicationId.ToString("D"), sequenceNumber,
            expectedRevision, rows = normalized
        }));
    }
}
