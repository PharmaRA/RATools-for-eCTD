using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfExtraneousCategoryTests
{
    [Theory]
    [InlineData("controlled", "species")]
    [InlineData("controlled", "route-of-admin")]
    [InlineData("single", "type-of-control")]
    [InlineData("long-term", "route-of-admin")]
    [InlineData("other", "species")]
    public async Task KnownExtraneousCategoryUsesActualSectionNotPhysicalDirectory(string section, string name)
    {
        var result = await Inspect(section, Category(name));
        var check = Check(result);
        Assert.Equal(CheckStatus.Fail, check.Status);
        var finding = Assert.Single(check.Findings);
        Assert.Equal("0001/m4/study/stf-a.xml", finding.Location.LogicalPath);
        Assert.Equal("@name", finding.Location.FieldPath);
        Assert.Equal(name, finding.Actual);
        Assert.Equal(ValidationSeverity.Info, finding.Severity);
        Assert.Null(finding.Location.LeafId);
        Assert.True(finding.Location.Line > 0);
    }

    [Theory]
    [InlineData("single", "species", "ich", CheckStatus.Pass)]
    [InlineData("repeat", "route-of-admin", "ich", CheckStatus.Pass)]
    [InlineData("long-term", "species", "ich", CheckStatus.Pass)]
    [InlineData("controlled", "type-of-control", "ich", CheckStatus.Pass)]
    [InlineData("repeat", "duration", "us", CheckStatus.Pass)]
    [InlineData("long-term", "duration", "us", CheckStatus.Pass)]
    [InlineData("controlled", "duration", "us", CheckStatus.NotEvaluated)]
    [InlineData("controlled", "species", "us", CheckStatus.NotEvaluated)]
    [InlineData("controlled", "custom", "ich", CheckStatus.NotEvaluated)]
    public async Task AllowedAndUnqualifiedCategoriesRemainDistinct(string section, string name, string realm, CheckStatus expected)
    {
        var result = await Inspect(section, Category(name, realm));
        Assert.Equal(expected, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Fact]
    public async Task SharedStfUsesUnionOfAllQualifiedSectionsIncludingExtensions()
    {
        var result = await Inspect("controlled", Category("species") + Category("route-of-admin") + Category("type-of-control"),
            afterIndex: index =>
            {
                var leaf = new XElement(index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "stf"));
                leaf.SetAttributeValue("ID", "nonclinical");
                index.Root!.Element("m5-clinical-study-reports")!.AddBeforeSelf(new XElement("m4-nonclinical-study-reports",
                    new XElement("m4-2-study-reports", new XElement("m4-2-3-toxicology", new XElement("m4-2-3-1-single-dose-toxicity",
                        new XElement("node-extension", new XAttribute("ID", "ext"), new XElement("title", "Study"), leaf))))));
            });
        var target = Assert.Single(result.Lifecycle.XmlInspections);
        Assert.Equal(CheckStatus.Pass, Assert.Single(target.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Theory]
    [InlineData("unreferenced")]
    [InlineData("wrong-position")]
    [InlineData("missing-stf")]
    [InlineData("truncated-stf")]
    [InlineData("missing-spec")]
    public async Task IncompletePlacementOrEvidenceCannotProveExtraneousCategory(string variant)
    {
        var result = await Inspect("controlled", Category("species"), variant);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Theory]
    [InlineData("?unknown")]
    [InlineData("#unknown")]
    public async Task UnresolvedAdditionalReferenceCannotEstablishCompleteAllowedSet(string suffix)
    {
        var result = await Inspect("controlled", Category("species"), afterIndex: index =>
        {
            var original = index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "stf");
            var unresolved = new XElement(original);
            unresolved.SetAttributeValue("ID", "query");
            unresolved.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml" + suffix);
            original.AddAfterSelf(unresolved);
        });
        AssertIndexValid(result);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Fact]
    public async Task UncertainPathDoesNotSuppressAnotherStfsKnownFailure()
    {
        var result = await Inspect("controlled", Category("species"), afterIndex: index =>
        {
            var original = index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "stf");
            var uncertain = new XElement(original);
            uncertain.SetAttributeValue("ID", "uncertain-a");
            uncertain.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml?unknown");
            var other = new XElement(original);
            other.SetAttributeValue("ID", "certain-b");
            other.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-b.xml");
            original.AddAfterSelf(uncertain, other);
        }, afterSource: root => File.Copy(Path.Combine(root, "0001/m4/study/stf-a.xml"), Path.Combine(root, "0001/m4/study/stf-b.xml")));
        AssertIndexValid(result);
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Equal("0001/m4/study/stf-b.xml", Assert.Single(Check(result).Findings).Location.LogicalPath);
    }

    [Fact]
    public async Task UnresolvableContentReferenceMakesThePlacementSetUnknown()
    {
        var result = await Inspect("controlled", Category("species"), afterIndex: index =>
        {
            var original = index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "stf");
            var other = new XElement(original);
            other.SetAttributeValue("ID", "unresolved");
            other.SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/%ZZ.xml");
            original.AddAfterSelf(other);
        });
        AssertIndexValid(result);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Fact]
    public async Task HeadingFailureSurvivesUnresolvedStfPlacements()
    {
        var result = await Inspect("controlled", Category("species"), afterIndex: index =>
        {
            index.Root!.Element("m5-clinical-study-reports")!.SetAttributeValue("manufacturer", "Acme");
            index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "stf")
                .SetAttributeValue(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml?unknown");
        });
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        var finding = Assert.Single(Check(result).Findings);
        Assert.Equal("0001/index.xml", finding.Location.LogicalPath);
        Assert.Equal("@manufacturer", finding.Location.FieldPath);
    }

    [Fact]
    public async Task HistoricalNonclinicalPlacementDoesNotExpandTargetAllowedSet()
    {
        var result = await Inspect("controlled", Category("species"), "history-extraneous", afterSource: root =>
        {
            var path = Path.Combine(root, "0000/index.xml");
            var index = XDocument.Load(path);
            foreach (var leaf in index.Descendants("leaf"))
            { leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5"); }
            var stf = new XElement("leaf", new XAttribute("ID", "historical-stf"), new XAttribute("operation", "new"),
                new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
                new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml"), new XElement("title", "Study"));
            index.Root!.Add(new XElement("m4-nonclinical-study-reports", new XElement("m4-2-study-reports",
                new XElement("m4-2-3-toxicology", new XElement("m4-2-3-1-single-dose-toxicity", stf)))));
            index.Save(path);
            var oldStf = Path.Combine(root, "0000/m4/study/stf-a.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(oldStf)!);
            File.Copy(Path.Combine(root, "0001/m4/study/stf-a.xml"), oldStf);
        });
        var historical = Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest != result.Lifecycle.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(historical.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
        var old = Assert.Single(historical.StudyTaggingFiles.Documents);
        Assert.True(old.ReadComplete);
        Assert.Equal(CheckStatus.Pass, old.DtdStatus);
        AssertIndexValid(result);
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Equal("0001/m4/study/stf-a.xml", Assert.Single(Check(result).Findings).Location.LogicalPath);
    }

    [Theory]
    [InlineData(1, CheckStatus.Fail)]
    [InlineData(0, CheckStatus.NotEvaluated)]
    public async Task RemainingBudgetIsAppliedByThisRule(int remaining, CheckStatus expected)
    {
        var result = await Inspect("controlled", Category("species") + Category("route-of-admin"));
        Assert.Equal(2, Check(result).Findings.Count);
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new(2026, 11, 20));
        var check = FdaStfExtraneousCategoryRule.Inspect(new ObservationInput(result.Lifecycle.InputDigest),
            Assert.Single(result.Lifecycle.XmlInspections), context, context.Catalog.Rules["FDA47-1368"], remaining, default);
        Assert.Equal(expected, check.Status);
        Assert.Equal(remaining, check.Findings.Count);
        if (remaining > 0) Assert.Equal("species", Assert.Single(check.Findings).Actual);
    }

    [Fact]
    public async Task KnownFailureSurvivesUnrelatedUnreadStfAndUnknownRealm()
    {
        var result = await Inspect("controlled", Category("species") + Category("custom", "us"), afterSource: root =>
            File.WriteAllText(Path.Combine(root, "0001/m4/study/stf-z.xml"), "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study"));
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Equal("species", Assert.Single(Check(result).Findings).Actual);
    }

    [Fact]
    public async Task DtdDefaultNamespaceSupportsTheSameCategoryDecision()
    {
        var result = await Inspect("controlled", Category("species"), "default-namespace");
        Assert.True(Assert.Single(Assert.Single(result.Lifecycle.XmlInspections).StudyTaggingFiles.Documents).Elements[0].Attributes["xmlns:ectd"].IsDefault);
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
    }

    [Theory]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceDateAndBudgetRemainBound(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("controlled", Category("species"), variant, date);
        Assert.Equal(expected, Check(result).Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    private static string Category(string name, string realm = "ich") => "<category name='" + name + "' info-type='" + realm + "'>value</category>";
    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1368");
    private static void AssertIndexValid(PackageDeliveryInspection result) => Assert.Equal(CheckStatus.Pass,
        Assert.Single(Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest == result.Lifecycle.InputDigest).Backbones,
            document => document.Kind == BackboneKind.Ich).DtdStatus);
    private static Task<PackageDeliveryInspection> Inspect(string section, string categories, string variant = "valid", string? date = "2026-11-20",
        Action<XDocument>? afterIndex = null, Action<string>? afterSource = null) =>
        FdaStfRequiredCategoryTests.Inspect(section, categories, variant, date, afterIndex, afterSource);

    private sealed class ObservationInput(string digest) : ICapturedPackageInput
    {
        public string SourceId => "extraneous-category-observations";
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
