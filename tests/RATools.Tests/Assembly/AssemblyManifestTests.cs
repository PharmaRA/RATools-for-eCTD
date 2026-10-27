using RATools.Application.Assembly;

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
        var missing = AssemblyManifest.Parse("documentId,operation\nid-1,new\n");
        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "ASSEMBLY_HEADER_MISSING");

        var unknown = AssemblyManifest.Parse("documentId,nodeInstanceId,operation,fileName\nid-1,node-1,new,x.pdf\n");
        Assert.True(unknown.Succeeded);
        Assert.Contains(unknown.Diagnostics, diagnostic => diagnostic.Code == "ASSEMBLY_COLUMN_UNKNOWN");
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
}
