using System.Security.Cryptography;
using RATools.Application.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageTrialSummaryReaderTests
{
    [Fact]
    public void FullTsRetainsSprefidAsAParameterAndDoesNotRequireFourColumnsOneRow()
    {
        var columns = new[] { Column("STUDYID", 16), Column("TSPARMCD", 8), Column("TSVAL", 40), Column("TSVALNF", 8), Column("EXTRA", 8) };
        var capture = Capture(columns, [Values(columns, "Study A", "SSTDTC", "2020-02-29", "", "x"), Values(columns, "Study A", "SPREFID", "STF-A", "", "x")]);
        var result = PackageTrialSummaryReader.Read(capture);
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.True(result.StructureComplete);
        Assert.True(result.RequiredValuesDecoded);
        Assert.Equal(2, result.MinPossibleRows);
        Assert.Equal(2, result.MaxPossibleRows);
        Assert.Equal("SPREFID", result.Rows[1].Parameter);
        Assert.Equal("STF-A", result.Rows[1].Value);
        Assert.Equal("Study A", result.Rows[0].StudyId);
        Assert.Same(capture, result.Capture);
        Assert.Same(capture.Content!.Rows[1], result.Rows[1].Original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingColumnEmptyValueAndNaRemainDistinct(bool includeNullFlavor)
    {
        var columns = includeNullFlavor ? new[] { Column("STUDYID", 16), Column("TSPARMCD", 8), Column("TSVAL", 48), Column("TSVALNF", 8) } :
            [Column("STUDYID", 16), Column("TSPARMCD", 8), Column("TSVAL", 56)];
        var capture = Capture(columns, [Values(columns, includeNullFlavor ? ["A", "STSTDTC", "", "NA"] : ["A", "STSTDTC", ""])]);
        var result = PackageTrialSummaryReader.Read(capture);
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.Equal(includeNullFlavor, result.HasNullFlavorColumn);
        Assert.Equal("", result.Rows[0].Value);
        Assert.Equal(includeNullFlavor ? "NA" : null, result.Rows[0].NullFlavor);
        Assert.True(result.RequiredValuesDecoded);
    }

    [Fact]
    public void ContentDoesNotInventAValidDateOrTrimStudyIdentity()
    {
        var columns = new[] { Column("STUDYID", 24), Column("TSPARMCD", 8), Column("TSVAL", 40), Column("TSVALNF", 8) };
        var result = PackageTrialSummaryReader.Read(Capture(columns, [Values(columns, " Study A", "STSTDTC", "not-a-date", "WHY")]));
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.Equal(" Study A", result.Rows[0].StudyId);
        Assert.Equal("not-a-date", result.Rows[0].Value);
        Assert.Equal("WHY", result.Rows[0].NullFlavor);
    }

    [Theory]
    [InlineData("STUDYID")]
    [InlineData("TSPARMCD")]
    [InlineData("TSVAL")]
    public void MissingRequiredColumnIsInvalidShapeNotEmptyData(string missing)
    {
        var columns = new[] { Column("STUDYID", 24), Column("TSPARMCD", 16), Column("TSVAL", 40) }.Where(column => column.Name != missing).ToArray();
        var result = PackageTrialSummaryReader.Read(Capture(columns, [Values(columns, Enumerable.Repeat("A", columns.Length).ToArray())]));
        Assert.Equal(TrialSummaryStatus.InvalidShape, result.Status);
        Assert.False(result.StructureComplete);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void AnOrdinaryDatasetIsNotTrialSummaryEvenIfItsColumnsLookSimilar()
    {
        var columns = new[] { Column("STUDYID", 24), Column("TSPARMCD", 16), Column("TSVAL", 40) };
        var capture = Capture(columns, [Values(columns, "A", "STSTDTC", "2020-01-01")], member: "DM");
        var result = PackageTrialSummaryReader.Read(capture);
        Assert.Equal(TrialSummaryStatus.NotTrialSummary, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void AmbiguousTailRowsRemainPossibleRatherThanDefinite()
    {
        var columns = new[] { Column("STUDYID", 8), Column("TSPARMCD", 8), Column("TSVAL", 8) };
        var result = PackageTrialSummaryReader.Read(Capture(columns, [Values(columns, "A", "SPREFID", "B")]));
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.Equal(1, result.MinPossibleRows);
        Assert.Equal(3, result.MaxPossibleRows);
        Assert.True(result.Rows[0].Certain);
        Assert.All(result.Rows.Skip(1), row => Assert.False(row.Certain));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndecodableRequiredTextIsNotEmptyAndDoesNotEraseOtherRows(bool utf8)
    {
        var columns = new[] { Column("STUDYID", 24), Column("TSPARMCD", 16), Column("TSVAL", 40) };
        var capture = Capture(columns, [Values(columns, "A", "STSTDTC", "2020-01-01"), Values(columns, "研究", "SPREFID", "B")],
            encoding: utf8 ? XptTextEncoding.Utf8 : XptTextEncoding.AsciiOnly);
        var result = PackageTrialSummaryReader.Read(capture);
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.True(result.StructureComplete);
        Assert.Equal(utf8, result.RequiredValuesDecoded);
        Assert.True(result.Rows[0].ValuesDecoded);
        Assert.Equal(utf8, result.Rows[1].ValuesDecoded);
        Assert.Equal(utf8 ? "研究" : null, result.Rows[1].StudyId);
    }

    [Fact]
    public void ParserFailureAndCaptureChangeCannotProvideTsFacts()
    {
        var capture = new CapturedXptFacts(CapturedXptStatus.InputChanged, "source", new string('a', 64), "0001/ts.xpt", new string('b', 64), null);
        Assert.Equal(TrialSummaryStatus.Unavailable, PackageTrialSummaryReader.Read(capture).Status);
        var malformed = capture with { Status = CapturedXptStatus.Read, Content = new(XptReadStatus.Invalid, "bad", new string('c', 64), XptTextEncoding.AsciiOnly) };
        Assert.Equal(TrialSummaryStatus.Unavailable, PackageTrialSummaryReader.Read(malformed).Status);
    }

    [Fact]
    public void BoundsCancellationAndFrozenRowsRemainExplicit()
    {
        var columns = new[] { Column("STUDYID", 24), Column("TSPARMCD", 16), Column("TSVAL", 40) };
        var capture = Capture(columns, [Values(columns, "A", "STSTDTC", "2020-01-01")]);
        var limited = PackageTrialSummaryReader.Read(capture, maxRows: 0);
        Assert.Equal(TrialSummaryStatus.LimitExceeded, limited.Status);
        Assert.Null(limited.MaxPossibleRows);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackageTrialSummaryReader.Read(capture, cancellationToken: cancellation.Token));
        var result = PackageTrialSummaryReader.Read(capture, maxRows: 1);
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.Throws<NotSupportedException>(() => ((IList<TrialSummaryRow>)result.Rows).Clear());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("研究", false)]
    public void OptionalNullFlavorDoesNotChangeRequiredColumnDecoding(string nullFlavor, bool rowDecoded)
    {
        var columns = new[] { Column("STUDYID", 16), Column("TSPARMCD", 8), Column("TSVAL", 48), Column("TSVALNF", 8) };
        var result = PackageTrialSummaryReader.Read(Capture(columns, [Values(columns, "A", "STSTDTC", "2020-01-01", nullFlavor)]));
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.True(result.StructureComplete);
        Assert.True(result.RequiredValuesDecoded);
        Assert.True(result.HasNullFlavorColumn);
        Assert.Equal(rowDecoded, result.Rows[0].ValuesDecoded);
        Assert.Equal(rowDecoded ? "" : null, result.Rows[0].NullFlavor);
        Assert.Equal(rowDecoded, result.Rows[0].Original.Cells[3].Decoded);
    }

    [Fact]
    public void ReorderedMixedCaseFieldsAndUndecodableExtraPreserveRequiredValues()
    {
        var columns = new[] { Column("Tsval", 40), Column("EXTRA", 8), Column("studyid", 24), Column("Tsparmcd", 8) };
        var capture = Capture(columns, [Values(columns, "STF-A", "研究", " A", "SPREFID")], member: "ts");
        var result = PackageTrialSummaryReader.Read(capture);
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.False(capture.Content!.ValuesComplete);
        Assert.True(result.RequiredValuesDecoded);
        Assert.True(result.Rows[0].ValuesDecoded);
        Assert.Equal(" A", result.Rows[0].StudyId);
        Assert.Equal("SPREFID", result.Rows[0].Parameter);
        Assert.Equal("STF-A", result.Rows[0].Value);
        Assert.Equal("studyid", result.Capture.Content!.Variables[2].Name);
    }

    [Theory]
    [InlineData("STUDYID")]
    [InlineData("TSPARMCD")]
    [InlineData("TSVAL")]
    [InlineData("TSVALNF")]
    public void NumericTsFieldsAreInvalidShape(string numeric)
    {
        var columns = new[] { Column("STUDYID", 8), Column("TSPARMCD", 8), Column("TSVAL", 8), Column("TSVALNF", 8) }
            .Select(column => column.Name == numeric ? column with { Type = 1 } : column).ToArray();
        var result = PackageTrialSummaryReader.Read(Capture(columns, [Values(columns, "A", "SPREFID", "B", "")]));
        Assert.Equal(TrialSummaryStatus.InvalidShape, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void CaseFoldedDuplicateColumnIsAmbiguous()
    {
        var columns = new[] { Column("STUDYID", 16), Column("studyid", 16), Column("TSPARMCD", 8), Column("TSVAL", 40) };
        var result = PackageTrialSummaryReader.Read(Capture(columns, [Values(columns, "A", "B", "SPREFID", "C")]));
        Assert.Equal(TrialSummaryStatus.InvalidShape, result.Status);
    }

    [Fact]
    public void RowLimitCountsPossibleTailRowsAndPreservesLocations()
    {
        var columns = new[] { Column("STUDYID", 8), Column("TSPARMCD", 8), Column("TSVAL", 8) };
        var capture = Capture(columns, [Values(columns, "A", "SPREFID", "B")]);
        Assert.Equal(TrialSummaryStatus.LimitExceeded, PackageTrialSummaryReader.Read(capture, maxRows: 1).Status);
        var result = PackageTrialSummaryReader.Read(capture, maxRows: 3);
        Assert.Equal(TrialSummaryStatus.Read, result.Status);
        Assert.All(result.Rows, row =>
        {
            Assert.Equal(row.Original.Index, row.Index);
            Assert.Equal(row.Original.Offset, row.Offset);
            Assert.Equal(row.Original.Certain, row.Certain);
        });
    }

    [Theory]
    [InlineData("index")]
    [InlineData("certain")]
    [InlineData("cells")]
    public void ContradictoryFrozenRowFactsAreRejected(string defect)
    {
        var columns = new[] { Column("STUDYID", 24), Column("TSPARMCD", 16), Column("TSVAL", 40) };
        var capture = Capture(columns, [Values(columns, "A", "STSTDTC", "2020-01-01")]);
        var content = capture.Content!;
        var row = Assert.Single(content.Rows);
        var changed = new XptRow(defect == "index" ? 1 : row.Index, row.Offset, defect != "certain" && row.Certain,
            defect == "cells" ? row.Cells.Take(2) : row.Cells);
        var altered = new PackageXptFacts(content.Status, content.Reason, content.Sha256, content.Encoding, content.StructureComplete,
            content.ValuesComplete, content.MemberName, content.MemberLabel, content.RecordBytes, content.MinPossibleRows, content.MaxPossibleRows,
            content.Variables, [changed]);
        var result = PackageTrialSummaryReader.Read(capture with { Content = altered });
        Assert.Equal(TrialSummaryStatus.InvalidShape, result.Status);
        Assert.False(result.StructureComplete);
        Assert.Empty(result.Rows);
    }

    private static PackageXptReaderTests.Column Column(string name, int length) => new(name, 2, length);
    private static byte[] Values(PackageXptReaderTests.Column[] columns, params string[] values) => PackageXptReaderTests.TextRow(columns, values);
    private static CapturedXptFacts Capture(PackageXptReaderTests.Column[] columns, byte[][] rows, string member = "TS", XptTextEncoding encoding = XptTextEncoding.AsciiOnly)
    {
        var bytes = PackageXptReaderTests.Sample(columns, rows);
        System.Text.Encoding.ASCII.GetBytes(member.PadRight(8)).CopyTo(bytes, 408);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var content = PackageXptReader.Read(bytes, digest, encoding);
        Assert.Equal(XptReadStatus.Read, content.Status);
        Assert.True(content.StructureComplete);
        return new(CapturedXptStatus.Read, "ts-observations", new string('a', 64), "0001/data.bin", new string('b', 64), content);
    }
}
