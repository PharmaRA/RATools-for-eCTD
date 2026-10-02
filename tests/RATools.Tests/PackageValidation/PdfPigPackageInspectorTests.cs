using System.Globalization;
using System.Text;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PdfPigPackageInspectorTests
{
    [Theory]
    [InlineData("/Dest [3 0 R /Fit]", 1, null)]
    [InlineData("/A << /S /GoTo /D [3 0 R /Fit] >>", 1, null)]
    [InlineData("/Dest (section)", null, "section")]
    [InlineData("/A << /S /GoTo /D /section >>", null, "section")]
    public void LocalDestinationsRetainPagesAndNames(string annotation, int? page, string? name)
    {
        var result = Inspect(Build(annotation));
        Assert.Null(result.Basics.ParseError);
        var link = Assert.Single(result.Links);
        Assert.Equal(1, link.SourcePageNumber);
        Assert.Equal(page, link.Target.PageNumber);
        Assert.Equal(name, link.Target.NamedDestination);
        Assert.False(link.Target.Invalid);
        Assert.True(result.NavigationComplete);
    }

    [Theory]
    [InlineData("[0 /Fit]", 1, null)]
    [InlineData("[4 /Fit]", 5, null)]
    [InlineData("(chapter)", null, "chapter")]
    public void RemoteDestinationUsesPdfPageIndexAndLiteralFileSpecification(string destination, int? page, string? name)
    {
        var result = Inspect(Build("/A << /S /GoToR /F (../0000/a%20b.pdf) /D " + destination + " >>"));
        Assert.Null(result.Basics.ParseError);
        var link = Assert.Single(result.Links);
        Assert.Equal("../0000/a%20b.pdf", link.Target.FileSpecification);
        Assert.Equal(page, link.Target.PageNumber);
        Assert.Equal(name, link.Target.NamedDestination);
    }

    [Theory]
    [InlineData("/Dests << /section [3 0 R /Fit] >>")]
    [InlineData("/Names << /Dests << /Names [(section) [3 0 R /Fit]] >> >>")]
    [InlineData("/Names << /Dests << /Kids [7 0 R] >> >>")]
    public void OldAndNewNamedDestinationTablesAreRead(string catalog)
    {
        var result = Inspect(Build("/Dest (section)", catalog, "<< /Names [(section) << /D [3 0 R /Fit] >>] >>"));
        Assert.Null(result.Basics.ParseError);
        var destination = Assert.Single(result.Destinations);
        Assert.Equal("section", destination.Name);
        Assert.Equal(1, destination.PageNumber);
        Assert.True(result.DestinationsComplete);
    }

    [Fact]
    public void UriBookmarksAndOpenActionsAreNotLostByUriOnlyExtraction()
    {
        var result = Inspect(Build("/A << /S /URI /URI (../0000/target.pdf#page=2) >>",
            "/Outlines 7 0 R /OpenAction [3 0 R /Fit]", "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
            "<< /Title (Go to section) /Parent 7 0 R /Dest [3 0 R /Fit] >>"));
        Assert.Null(result.Basics.ParseError);
        Assert.Equal(3, result.Links.Count);
        Assert.Contains(result.Links, link => link.Target.Uri == "../0000/target.pdf#page=2");
        Assert.Equal(2, result.Links.Count(link => link.Target.PageNumber == 1));
    }

    [Fact]
    public void UnresolvablePageReferenceRemainsAnInvalidLink()
    {
        var result = Inspect(Build("/Dest [99 0 R /Fit]"));
        Assert.Null(result.Basics.ParseError);
        Assert.True(Assert.Single(result.Links).Target.Invalid);
    }

    [Fact]
    public void DuplicateDestinationNamesRemainAmbiguous()
    {
        var result = Inspect(Build("/Dest (section)", "/Dests << /section [3 0 R /Fit] >> /Names << /Dests << /Names [(section) [3 0 R /Fit]] >> >>"));
        Assert.Null(result.Basics.ParseError);
        Assert.Equal(2, result.Destinations.Count(destination => destination.Name == "section"));
    }

    [Theory]
    [InlineData("/A << /S /JavaScript /JS (unexecuted) >>")]
    [InlineData("/A << /S /GoTo /D [3 0 R /Fit] /Next << /S /GoTo /D [3 0 R /Fit] >> >>")]
    public void UnsupportedActionsCannotProduceCompleteNavigationCoverage(string annotation)
    {
        var result = Inspect(Build(annotation));
        Assert.False(result.NavigationComplete);
        Assert.NotEmpty(result.IncompleteReasons);
    }

    [Fact]
    public void InvalidPdfAndCancellationDoNotProduceCompleteFacts()
    {
        var result = Inspect(Encoding.ASCII.GetBytes("not a pdf"));
        Assert.NotNull(result.Basics.ParseError);
        Assert.Null(result.Basics.AllFontsEmbedded);
        Assert.False(result.NavigationComplete);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream(Build("/Dest [3 0 R /Fit]"));
        Assert.ThrowsAny<OperationCanceledException>(() => new PdfPigPackageInspector().Inspect(stream, "0000/test.pdf", new(), cancellation.Token));
    }

    [Fact]
    public void ResourceLimitsAreBoundAndEnforcedBeforeNavigationCompletes()
    {
        var limits = new PackageReadLimits { MaxPdfObjects = 1 };
        Assert.NotEqual(new PackageReadLimits().Digest(), limits.Digest());
        using var stream = new MemoryStream(Build("/Dest [3 0 R /Fit]"));
        Assert.Throws<PdfInspectionLimitException>(() => new PdfPigPackageInspector().Inspect(stream, "0000/test.pdf", limits, CancellationToken.None));
    }

    private static PackagePdfFacts Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new PdfPigPackageInspector().Inspect(stream, "0000/test.pdf", new(), CancellationToken.None);
    }

    // Hand-authored object graph, independent of the publishing package and production PDF parser.
    internal static byte[] Build(string annotation, string catalog = "", params string[] extraObjects)
    {
        const string content = "BT /F1 12 Tf 100 700 Td (Synthetic navigation fixture) Tj ET";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R " + catalog + " >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R /Annots [6 0 R] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream",
            "<< /Type /Annot /Subtype /Link /Rect [0 0 100 100] " + annotation + " >>"
        };
        objects.AddRange(extraObjects);
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(text.ToString()));
            text.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(text.ToString());
        text.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
