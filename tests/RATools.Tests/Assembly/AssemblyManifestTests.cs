using RATools.Application.Assembly;
using System.IO.Compression;
using System.Text;

namespace RATools.Tests.Assembly;

public sealed class AssemblyManifestTests
{
    [Fact]
    public void QuotedFieldsKeepCommasQuotesAndNewlinesAndBomAndCrlfAreHandled()
    {
        const string csv = "﻿documentId,nodeInstanceId,operation,title\r\n" +
            "id-1,node-1,new,\"Specification, part 1\"\r\n" +
            "id-2,node-2,new,\"A \"\"quoted\"\" title with\r\na newline\"\r\n";
        var parsed = AssemblyManifest.Parse(csv);
        Assert.True(parsed.Succeeded);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("Specification, part 1", parsed.Rows[0].Fields["title"]);
        Assert.Equal("A \"quoted\" title with\r\na newline", parsed.Rows[1].Fields["title"]);
        // Line numbers account for newlines inside quoted values: the second record
        // starts on line 3 because its quoted title contains a newline.
        Assert.Equal(2, parsed.Rows[0].LineNumber);
        Assert.Equal(3, parsed.Rows[1].LineNumber);
    }

    [Fact]
    public void MissingRequiredColumnsOrUnknownColumnsAreReported()
    {
        var missing = AssemblyManifest.Parse("sourceFile,nodeInstanceId,operation\nfile.pdf,new\n");
        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "ASSEMBLY_HEADER_MISSING");

        var unknown = AssemblyManifest.Parse("sourceFile,nodeInstanceId,fileName,operation,unknown\nfile.pdf,node-1,x.pdf,new,value\n");
        Assert.True(unknown.Succeeded);
        Assert.Contains(unknown.Diagnostics, diagnostic => diagnostic.Code == "ASSEMBLY_COLUMN_UNKNOWN");
    }

    [Fact]
    public void NewSourceAndFilenameFieldsAreRetainedAndDigestBound()
    {
        var parsed = AssemblyManifest.Parse("sourceFile,nodeInstanceId,fileName,operation\nuploads/a.pdf,node-1,final.pdf,new\n");
        Assert.True(parsed.Succeeded);
        Assert.Equal("uploads/a.pdf", parsed.Rows[0].Fields["sourceFile"]);
        Assert.Equal("final.pdf", parsed.Rows[0].Fields["fileName"]);
        var first = new AssemblyPlanRow(2, null, "node-1", null, "new", null, 0, AssemblyRowStatus.Valid, [],
            "uploads/a.pdf", "final.pdf", "a".PadLeft(64, 'a'), 10);
        var second = first with { FileName = "other.pdf" };
        var appId = Guid.NewGuid();
        Assert.NotEqual(AssemblyManifest.ComputeDigest(appId, "0001", 0, [first]),
            AssemblyManifest.ComputeDigest(appId, "0001", 0, [second]));
    }

    [Fact]
    public void MalformedQuotingAndRowLimitsAreRejectedInsteadOfPartiallyParsed()
    {
        Assert.Contains(AssemblyManifest.Parse("a,b\n\"unterminated,1\n").Diagnostics,
            diagnostic => diagnostic.Code == "ASSEMBLY_CSV_UNTERMINATED_QUOTE");
        Assert.Contains(AssemblyManifest.Parse("a,b\nx\"y,1\n").Diagnostics,
            diagnostic => diagnostic.Code == "ASSEMBLY_CSV_UNEXPECTED_QUOTE");
        var header = "documentId,nodeInstanceId,operation\n";
        var tooMany = header + string.Concat(Enumerable.Range(0, AssemblyManifest.MaxRows + 1)
            .Select(index => $"id-{index},node-{index},new\n"));
        Assert.Contains(AssemblyManifest.Parse(tooMany).Diagnostics,
            diagnostic => diagnostic.Code == "ASSEMBLY_ROW_LIMIT");
    }

    [Fact]
    public void PlanDigestChangesWithEveryRowFieldAndTheRevision()
    {
        var rows = new[]
        {
            new AssemblyPlanRow(2, "doc", "node", "Title", "new", null, 0, AssemblyRowStatus.Valid, [])
        };
        var app = Guid.NewGuid();
        var digest = AssemblyManifest.ComputeDigest(app, "0001", 4, rows);
        Assert.Equal(digest, AssemblyManifest.ComputeDigest(app, "0001", 4, rows));
        Assert.NotEqual(digest, AssemblyManifest.ComputeDigest(app, "0001", 5, rows));
        Assert.NotEqual(digest, AssemblyManifest.ComputeDigest(app, "0001", 4,
            [rows[0] with { SortOrder = 1 }]));
    }

    [Theory]
    [InlineData("documentId,nodeInstanceId,operation,operation\nid,node,new,new")]
    [InlineData("documentId,nodeInstanceId,operation\nid,node,n\"ew")]
    [InlineData("documentId,nodeInstanceId,operation\nid,node,new,discarded")]
    [InlineData("documentId,nodeInstanceId,operation\nid,node,\"new\"discarded")]
    public void MalformedManifestsCannotBeExecutedAsSuccessfulParses(string csv)
    {
        var parsed = AssemblyManifest.Parse(csv);
        Assert.False(parsed.Succeeded);
        Assert.NotEmpty(parsed.Diagnostics);
    }

    [Fact]
    public void XlsxDataCellsAreReadWithoutExecutingFormulas()
    {
        var parsed = AssemblyManifest.Parse(Workbook(), "xlsx");
        Assert.True(parsed.Succeeded); Assert.Single(parsed.Rows);
        Assert.Equal("document:00000000-0000-0000-0000-000000000001", parsed.Rows[0].Fields["sourceFile"]);
        Assert.Equal("final.pdf", parsed.Rows[0].Fields["fileName"]); Assert.Empty(parsed.Rows[0].Diagnostics!);
        var formula = AssemblyManifest.Parse(Workbook(formula: true), "xlsx");
        Assert.True(formula.Succeeded);
        Assert.Contains(formula.Rows[0].Diagnostics!, issue => issue.Code == "ASSEMBLY_XLSX_FORMULA_UNSUPPORTED");
    }

    [Theory]
    [InlineData("macro", "ASSEMBLY_XLSX_ACTIVE_CONTENT")]
    [InlineData("external", "ASSEMBLY_XLSX_ACTIVE_CONTENT")]
    [InlineData("sheets", "ASSEMBLY_XLSX_SHEET_LIMIT")]
    public void XlsxActiveContentAndExtraSheetsAreRejected(string mode, string code)
    {
        var parsed = AssemblyManifest.Parse(Workbook(mode: mode), "xlsx");
        Assert.False(parsed.Succeeded); Assert.Contains(parsed.Diagnostics, issue => issue.Code == code);
    }

    [Fact]
    public void InvalidUtf8AndExpandedWorkbookLimitsFailClosed()
    {
        Assert.False(AssemblyManifest.Parse([0xff, 0xfe, 0xff], "csv").Succeeded);
        Assert.False(AssemblyManifest.Parse(Encoding.UTF8.GetBytes("data"), "exe").Succeeded);
        var workbook = Workbook(mode: "oversize");
        Assert.True(workbook.Length < AssemblyManifest.MaxCharacters);
        Assert.Contains(AssemblyManifest.Parse(workbook, "xlsx").Diagnostics, issue => issue.Code == "ASSEMBLY_XLSX_INVALID");
    }

    [Theory]
    [InlineData("formula", "ASSEMBLY_XLSX_FORMULA_UNSUPPORTED")]
    [InlineData("boolean", "ASSEMBLY_XLSX_CELL_UNSUPPORTED")]
    [InlineData("error", "ASSEMBLY_XLSX_CELL_UNSUPPORTED")]
    public void MultilineTextCannotDisplaceLaterWorkbookCellDiagnostics(string cell, string code)
    {
        var parsed = AssemblyManifest.Parse(Workbook(mode: "multiline-" + cell), "xlsx");
        Assert.True(parsed.Succeeded); Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal(3, parsed.Rows[1].LineNumber);
        Assert.Contains(parsed.Rows[1].Diagnostics!, issue => issue.Code == code && issue.LineNumber == 3);
    }

    [Fact]
    public void WorksheetExternalHyperlinkIsRejectedWithoutFollowingIt()
    {
        var parsed = AssemblyManifest.Parse(Workbook(mode: "worksheet-external"), "xlsx");
        Assert.False(parsed.Succeeded);
        Assert.Contains(parsed.Diagnostics, issue => issue.Code == "ASSEMBLY_XLSX_ACTIVE_CONTENT");
    }

    [Theory]
    [InlineData("duplicate-relation")]
    [InlineData("overlong-address")]
    [InlineData("duplicate-entry")]
    [InlineData("duplicate-row")]
    [InlineData("mismatched-cell-row")]
    public void AmbiguousOrMalformedWorkbookIdentitiesProduceDiagnostics(string mode)
    {
        var parsed = AssemblyManifest.Parse(Workbook(mode: mode), "xlsx");
        Assert.False(parsed.Succeeded);
        Assert.Contains(parsed.Diagnostics, issue => issue.Code == "ASSEMBLY_XLSX_INVALID");
    }

    private static byte[] Workbook(bool formula = false, string mode = "normal")
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Write(string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); writer.Write(text); }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Write("xl/workbook.xml", $"<workbook xmlns='{ns}' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Data' sheetId='1' r:id='rId1'/>{(mode == "sheets" ? "<sheet name='Other' sheetId='2' r:id='rId2'/>" : "")}</sheets></workbook>");
            Write("xl/_rels/workbook.xml.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Target='worksheets/sheet1.xml' {(mode == "external" ? "TargetMode='External'" : "")}/>{(mode == "duplicate-relation" ? "<Relationship Id='rId1' Target='worksheets/sheet1.xml'/>" : "")}</Relationships>");
            string Cell(string address, string value) => $"<c r='{address}' t='inlineStr'><is><t>{value}</t></is></c>";
            var address = mode == "overlong-address" ? "AAAAAAAAAAAAAAAAAAA2" : mode == "mismatched-cell-row" ? "A3" : "A2";
            var extra = mode.StartsWith("multiline-", StringComparison.Ordinal)
                ? $"<row r='3'>{Cell("A3", "document:00000000-0000-0000-0000-000000000001")}{Cell("B3", "node")}{Cell("C3", "second.pdf")}" +
                    (mode == "multiline-formula" ? "<c r='D3' t='str'><f>CONCAT(&quot;n&quot;,&quot;ew&quot;)</f><v>new</v></c>" :
                     mode == "multiline-boolean" ? "<c r='D3' t='b'><v>1</v></c>" : "<c r='D3' t='e'><v>#VALUE!</v></c>") + "</row>"
                : mode == "duplicate-row" ? $"<row r='2'>{Cell("A2", "duplicate")}</row>" : "";
            Write("xl/worksheets/sheet1.xml", $"<worksheet xmlns='{ns}'><sheetData><row r='1'>{Cell("A1", "sourceFile")}{Cell("B1", "nodeInstanceId")}{Cell("C1", "fileName")}{Cell("D1", "operation")}{Cell("E1", "title")}</row><row r='2'>{Cell(address, "document:00000000-0000-0000-0000-000000000001")}{Cell("B2", "node")}{Cell("C2", "final.pdf")}{(formula ? "<c r='D2' t='str'><f>CONCAT(&quot;n&quot;,&quot;ew&quot;)</f><v>new</v></c>" : Cell("D2", "new"))}{Cell("E2", mode.StartsWith("multiline-", StringComparison.Ordinal) ? "A legal\nmultiline title" : "Title")}</row>{extra}</sheetData></worksheet>");
            if (mode == "macro") Write("xl/vbaProject.bin", "macro bytes");
            if (mode == "oversize") Write("xl/oversize.xml", new string('x', 8 * 1024 * 1024));
            if (mode == "duplicate-entry") Write("xl/workbook.xml", "<different/>");
            if (mode == "worksheet-external") Write("xl/worksheets/_rels/sheet1.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='link1' Target='https://example.invalid/source' TargetMode='External'/></Relationships>");
        }
        return bytes.ToArray();
    }
}
