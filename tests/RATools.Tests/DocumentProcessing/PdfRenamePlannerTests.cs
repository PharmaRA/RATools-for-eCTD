using RATools.Application.DocumentProcessing;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Validation.Pdf;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfRenamePlannerTests
{
    private static readonly Guid ApplicationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("20000000-0000-0000-0000-000000000003");

    [Fact]
    public void TargetRenameRequiresExplicitConfirmationOfEveryReferencingSource()
    {
        var docs = new[] { Document(A, "m5/a.pdf", Remote("b.pdf")), Document(B, "m5/b.pdf") };
        var preview = Plan(docs, [new(B, "m5/renamed.pdf")], [B]);
        Assert.False(preview.CanExecute);
        Assert.Contains(preview.Issues, issue => issue.Code == "SOURCE_NOT_CONFIRMED" && issue.PlacementId == A);
        var impact = Assert.Single(preview.Impacts);
        Assert.Equal(A, impact.SourcePlacementId);
        Assert.Equal(B, impact.TargetPlacementId);
        Assert.Equal("renamed.pdf", impact.NewReference);
        Assert.Equal("overview", impact.NamedDestination);
        Assert.Equal("b.pdf", docs[0].Facts.Links.Single().Target.FileSpecification);
        Assert.True(Plan(docs, [new(B, "m5/renamed.pdf")], [A, B]).CanExecute);
    }

    [Fact]
    public void MovingTheSourceRebasesItsOutgoingReferencesEvenWhenTargetStaysPut()
    {
        var preview = Plan([Document(A, "m5/a.pdf", Remote("../m3/b.pdf", page: 2)), Document(B, "m3/b.pdf")],
            [new(A, "m5/study/a.pdf")], [A]);
        Assert.True(preview.CanExecute);
        var impact = Assert.Single(preview.Impacts);
        Assert.Equal("../../m3/b.pdf", impact.NewReference);
        Assert.Equal(2, impact.TargetPageNumber);
        Assert.Null(impact.NamedDestination);
    }

    [Fact]
    public void SimultaneousMovesUseFinalPathsForCyclesAndSelfReferences()
    {
        var a = Document(A, "m5/a.pdf", Remote("b.pdf"), Remote("a.pdf", objectPath: "page[1]/annotation[2]/GoToR"));
        var b = Document(B, "m5/b.pdf", Remote("a.pdf"));
        var plan = Plan([a, b], [new(A, "m3/a.pdf"), new(B, "m4/b.pdf")], [A, B]);
        Assert.True(plan.CanExecute);
        Assert.Contains(plan.Impacts, impact => impact.SourcePlacementId == A && impact.TargetPlacementId == B && impact.NewReference == "../m4/b.pdf");
        Assert.Contains(plan.Impacts, impact => impact.SourcePlacementId == B && impact.TargetPlacementId == A && impact.NewReference == "../m3/a.pdf");
        Assert.DoesNotContain(plan.Impacts, impact => impact.SourcePlacementId == A && impact.TargetPlacementId == A);
    }

    [Fact]
    public void HistoricalReferencesAreNotRewrittenAndCannotBeMadeEditableByConfirmation()
    {
        var history = Document(C, "m5/history.pdf", Remote("../../0001/m5/b.pdf")) with { SequenceNumber = "0000", Editable = false };
        var plan = Plan([Document(B, "m5/b.pdf"), history], [new(B, "m5/new.pdf")], [B, C]);
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Issues, issue => issue.Code == "SOURCE_NOT_EDITABLE" && issue.PlacementId == C);
        Assert.False(Plan([history], [new(C, "m5/new.pdf")], [C]).CanExecute);
    }

    [Fact]
    public void RelativeUriPreservesQueryFragmentAndEncodesEachPathSegmentExactlyOnce()
    {
        var uri = new PdfNavigationLink("page[1]/annotation[1]/URI", 1, new(Uri: "b%20old.pdf?view=fit#overview"));
        var plan = Plan([Document(A, "m5/a.pdf", uri), Document(B, "m5/b old.pdf")], [new(B, "m5/b%new.pdf")], [A, B]);
        var impact = Assert.Single(plan.Impacts);
        Assert.True(impact.IsUri);
        Assert.Equal("b%25new.pdf?view=fit#overview", impact.NewReference);
    }

    [Fact]
    public void IncompleteInspectionAndCaseInsensitiveDestinationCollisionsBlockExecution()
    {
        var unknown = Document(A, "m5/a.pdf") with { Facts = Facts([], complete: false) };
        Assert.Contains(Plan([unknown, Document(B, "m5/b.pdf")], [new(B, "m5/new.pdf")], [A, B]).Issues,
            issue => issue.Code == "INSPECTION_INCOMPLETE");
        Assert.Contains(Plan([Document(A, "m5/a.pdf"), Document(B, "m5/b.pdf")], [new(B, "m5/A.pdf")], [A, B]).Issues,
            issue => issue.Code == "PATH_COLLISION");
    }

    [Fact]
    public void DigestBindsAllInventoryBytesAndConfirmationButNotEnumerationOrder()
    {
        var docs = new[] { Document(A, "m5/a.pdf", Remote("b.pdf")), Document(B, "m5/b.pdf") };
        var plan = Plan(docs, [new(B, "m5/new.pdf")], [A, B]);
        Assert.Equal(64, plan.PlanDigest.Length);
        Assert.Equal(plan.PlanDigest, Plan(docs.Reverse(), [new(B, "m5/new.pdf")], [B, A]).PlanDigest);
        Assert.NotEqual(plan.PlanDigest, Plan([docs[0] with { Sha256 = new string('c', 64) }, docs[1]], [new(B, "m5/new.pdf")], [A, B]).PlanDigest);
        Assert.NotEqual(plan.PlanDigest, Plan(docs, [new(B, "m5/new.pdf")], [B]).PlanDigest);
    }

    [Fact]
    public void NodeIdentityIsBoundEvenWhenTheResolvedDeliveryPathIsIdentical()
    {
        var documents = new[] { Document(B, "m5/b.pdf") };
        var first = Plan(documents, [new(B, "m5/new.pdf", A)], [B]);
        var other = Plan(documents, [new(B, "m5/new.pdf", C)], [B]);
        Assert.NotEqual(first.PlanDigest, other.PlanDigest);
        Assert.False(Plan(documents, [new(B, "m5/b.pdf")], [B]).CanExecute);
    }

    private static PdfRenamePlan Plan(IEnumerable<PdfRenameDocument> documents, IEnumerable<PdfRenameRequest> changes, IEnumerable<Guid> confirmed)
        => PdfRenamePlanner.Create(ApplicationId, "0001", 7, documents, changes, confirmed);
    private static PdfRenameDocument Document(Guid id, string href, params PdfNavigationLink[] links)
        => new(id, id, id, "0001", href, new string('a', 64), true, Facts(links));
    private static PdfNavigationLink Remote(string file, int? page = null, string objectPath = "page[1]/annotation[1]/GoToR")
        => new(objectPath, 1, new(FileSpecification: file, PageNumber: page, NamedDestination: page is null ? "overview" : null));
    private static PackagePdfFacts Facts(IEnumerable<PdfNavigationLink> links, bool complete = true)
        => new(new PdfInspectionResult("1.7", false, false, true, null, [], false, [], PageCount: 2),
            links, [new("overview", 1)], complete, complete);
}
