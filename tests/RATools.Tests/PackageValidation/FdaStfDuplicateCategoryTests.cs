using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfDuplicateCategoryTests
{
    [Theory]
    [InlineData("type-of-control", "ich", "placebo", "placebo")]
    [InlineData("type-of-control", "ich", "placebo", "external")]
    [InlineData("species", "ich", "rat", "mouse")]
    [InlineData("route-of-admin", "ich", "oral", "intravenous")]
    [InlineData("duration", "us", "short", "long")]
    public async Task RepeatedKnownCategoryIsLocatedAtSecondOccurrence(string name, string realm, string first, string second)
    {
        var result = await Inspect(Category(name, realm, first) + Category(name, realm, second));
        var check = Check(result);
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("0001/m4/study/stf-a.xml", finding.Location.LogicalPath);
        Assert.Equal("@name", finding.Location.FieldPath);
        Assert.Contains("category[2]", finding.Location.NodePath, StringComparison.Ordinal);
        Assert.Equal(name, finding.Actual);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.True(finding.Location.Line > 0);
        Assert.Null(finding.Location.LeafId);
        Assert.Equal("0001/m4/study/stf-a.xml", Assert.Single(finding.Location.Files!).LogicalPath);
    }

    [Theory]
    [InlineData("", CheckStatus.Pass)]
    [InlineData("<category name='species' info-type='ich'>rat</category><category name='route-of-admin' info-type='ich'>oral</category>", CheckStatus.Pass)]
    [InlineData("<category name='species' info-type='ich'>rat</category><category name='species' info-type='us'>regional</category>", CheckStatus.NotEvaluated)]
    [InlineData("<category name='custom' info-type='us'>a</category><category name='custom' info-type='us'>b</category>", CheckStatus.NotEvaluated)]
    [InlineData("<category name='species' info-type='ich'>rat</category><category name=' species' info-type='ich'>mouse</category>", CheckStatus.NotEvaluated)]
    public async Task DifferentCategoriesAndUnqualifiedIdentitiesDoNotCreateDuplicateFindings(string categories, CheckStatus expected)
    {
        var result = await Inspect(categories);
        Assert.Equal(expected, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Theory]
    [InlineData("truncated-stf")]
    [InlineData("missing-stf")]
    [InlineData("missing-spec")]
    [InlineData("missing-source")]
    public async Task MissingPrerequisitesRemainUnevaluated(string variant)
    {
        var result = await Inspect(Category("species", "ich", "rat") + Category("species", "ich", "rat"), variant);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
    }

    [Fact]
    public async Task NoStfCanBeVacuouslyCheckedWithoutPretendingTheRuleFullyQualified()
    {
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
            item => item.RuleId == "FDA47-1362").CheckStatus);
    }

    [Fact]
    public async Task TwoDocumentsDoNotShareCategoryIdentity()
    {
        var result = await Inspect(Category("species", "ich", "rat"), afterSource: root =>
        {
            var path = Path.Combine(root, "0001/m4/study/stf-b.xml");
            File.WriteAllText(path, Xml(Category("species", "ich", "mouse")));
        });
        Assert.Equal(2, Assert.Single(result.Lifecycle.XmlInspections).StudyTaggingFiles.Documents.Count);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
    }

    [Fact]
    public async Task RepeatedFileTagsAndPropertiesDoNotCountAsDuplicateStudyCategories()
    {
        var result = await Inspect("", afterSource: root =>
        {
            var path = Path.Combine(root, "0001/m4/study/stf-a.xml");
            var document = Xml("").Replace("<study-document/>",
                "<study-document><doc-content xlink:href='../../index.xml#stf'><property name='site-identifier' info-type='us'>A</property><property name='site-identifier' info-type='us'>B</property><file-tag name='synopsis' info-type='ich'/><file-tag name='synopsis' info-type='ich'/></doc-content></study-document>", StringComparison.Ordinal);
            File.WriteAllText(path, document);
        });
        Assert.Equal(CheckStatus.Pass, Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).StudyTaggingFiles.Documents).DtdStatus);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Fact]
    public async Task KnownDuplicateSurvivesOtherIncompleteDocument()
    {
        var result = await Inspect(Category("species", "ich", "rat") + Category("species", "ich", "mouse"), afterSource: root =>
            File.WriteAllText(Path.Combine(root, "0001/m4/study/stf-z.xml"), "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study"));
        Assert.False(Assert.Single(result.Lifecycle.XmlInspections).StudyTaggingFiles.InventoryComplete);
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Single(Check(result).Findings);
    }

    [Fact]
    public async Task HistoricalDuplicateDoesNotBecomeATargetFinding()
    {
        var result = await Inspect(Category("species", "ich", "rat"), "history-1362", afterSource: root =>
        {
            var path = Path.Combine(root, "0000/m4/study/stf-old.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Xml(Category("species", "ich", "rat") + Category("species", "ich", "rat")));
        });
        var old = Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest != result.Lifecycle.InputDigest);
        var historical = Assert.Single(old.StudyTaggingFiles.Documents);
        Assert.True(historical.ReadComplete);
        Assert.Equal(CheckStatus.Pass, historical.DtdStatus);
        Assert.Equal(2, historical.Elements.Count(element => element.LocalName == "category" &&
            element.Attribute("name", "") == "species" && element.Attribute("info-type", "") == "ich"));
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
    }

    [Theory]
    [InlineData(2, 2, CheckStatus.Fail)]
    [InlineData(1, 1, CheckStatus.Fail)]
    [InlineData(0, 0, CheckStatus.NotEvaluated)]
    public async Task ThreeOccurrencesRespectThisRulesRemainingBudget(int remaining, int count, CheckStatus expected)
    {
        var result = await Inspect(string.Concat(Enumerable.Repeat(Category("species", "ich", "rat"), 3)));
        Assert.Equal(2, Check(result).Findings.Count);
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new(2026, 11, 20));
        var check = Assert.Single(FdaStfDuplicateCategoryRule.Inspect(new ObservationInput(result.Lifecycle.InputDigest),
            result.Lifecycle, context, remaining, default));
        Assert.Equal(expected, check.Status);
        Assert.Equal(count, check.Findings.Count);
        for (var index = 0; index < count; index++)
            Assert.Contains("category[" + (index + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]",
                check.Findings[index].Location.NodePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("valid", "2008-03-10", CheckStatus.Fail)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task DateAndSharedBudgetRemainExplicit(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect(Category("species", "ich", "rat") + Category("species", "ich", "rat"), variant, date);
        Assert.Equal(expected, Check(result).Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    private static string Category(string name, string realm, string value) => "<category name='" + name + "' info-type='" + realm + "'>" + value + "</category>";
    private static string Xml(string categories) => "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
        "<ectd:study xmlns:ectd='http://www.ich.org/ectd' xmlns:xlink='http://www.w3.org/1999/xlink'><study-identifier><title>A</title><study-id>A</study-id>" + categories + "</study-identifier><study-document/></ectd:study>";
    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1362");
    private static Task<PackageDeliveryInspection> Inspect(string categories, string variant = "valid", string? date = "2026-11-20", Action<string>? afterSource = null) =>
        FdaStfRequiredCategoryTests.Inspect("other", categories, variant, date, afterSource: afterSource);

    private sealed class ObservationInput(string digest) : ICapturedPackageInput
    {
        public string SourceId => "duplicate-category-observations";
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public PackageInputManifest Manifest { get; } = new(Guid.NewGuid(), "0001", [], [], [], []);
        public string? ArchiveSha256 => null;
        public string InputDigest => digest;
        public PackageReadLimits Limits { get; } = new();
        public Stream OpenRead(string logicalPath) => throw new InvalidOperationException("No content reread expected.");
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Rule uses bound facts only.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
