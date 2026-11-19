using System.Text;
using System.Xml.Linq;
using RATools.Application.PackageValidation;
using RATools.Application.Standards;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class FdaStfRequiredCategoryTests
{
    [Theory]
    [InlineData("single", "", "species,route-of-admin")]
    [InlineData("single", "species", "route-of-admin")]
    [InlineData("repeat", "route-of-admin", "species")]
    [InlineData("long-term", "", "species")]
    [InlineData("controlled", "", "type-of-control")]
    public async Task MissingCategoriesAreBoundToActualIchSection(string section, string present, string missing)
    {
        var result = await Inspect(section, Categories(present));
        var check = Check(result);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal(missing, string.Join(',', check.Findings.Select(item => item.Expected)));
        Assert.All(check.Findings, finding =>
        {
            Assert.Equal("0001/m4/study/stf-a.xml", finding.Location.LogicalPath);
            Assert.Equal(ValidationSeverity.Warning, finding.Severity);
            Assert.Null(finding.Location.LeafId);
            Assert.Contains("study-identifier", finding.Location.NodePath, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("single", "species,route-of-admin", CheckStatus.Pass)]
    [InlineData("repeat", "species,route-of-admin", CheckStatus.NotEvaluated)]
    [InlineData("repeat", "species,route-of-admin,duration", CheckStatus.Pass)]
    [InlineData("long-term", "species", CheckStatus.Pass)]
    [InlineData("controlled", "type-of-control", CheckStatus.Pass)]
    [InlineData("other", "", CheckStatus.Pass)]
    public async Task ScopeAndConditionalDurationRemainDistinct(string section, string present, CheckStatus expected)
    {
        var result = await Inspect(section, Categories(present));
        Assert.Equal(expected, Check(result).Status);
        Assert.Empty(Check(result).Findings);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(result.CreateReport(PackageValidationMode.Development).Coverage,
            row => row.RuleId == "FDA47-1357").CheckStatus);
    }

    [Theory]
    [InlineData("", CheckStatus.Fail)]
    [InlineData("   ", CheckStatus.Fail)]
    [InlineData("placebo", CheckStatus.Pass)]
    public async Task PresentRequiredCategoryNeedsNonblankContent(string value, CheckStatus expected)
    {
        var result = await Inspect("controlled", "<category name='type-of-control' info-type='ich'>" + value + "</category>");
        Assert.Equal(expected, Check(result).Status);
    }

    [Theory]
    [InlineData("regional-category")]
    [InlineData("wrong-position")]
    [InlineData("unreferenced")]
    [InlineData("missing-stf")]
    [InlineData("truncated-stf")]
    [InlineData("missing-spec")]
    public async Task UnqualifiedPlacementOrEvidenceCannotProveMissingCategory(string variant)
    {
        var result = await Inspect("controlled", variant == "regional-category"
            ? "<category name='type-of-control' info-type='us'>regional-choice</category>" : "", variant);
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Theory]
    [InlineData("valid", "2008-03-09", CheckStatus.NotApplicable)]
    [InlineData("valid", null, CheckStatus.NotEvaluated)]
    [InlineData("missing-source", "2026-11-20", CheckStatus.NotEvaluated)]
    [InlineData("budget", "2026-11-20", CheckStatus.NotEvaluated)]
    public async Task SourceDateAndBudgetRemainBound(string variant, string? date, CheckStatus expected)
    {
        var result = await Inspect("controlled", "", variant, date);
        Assert.Equal(expected, Check(result).Status);
        if (variant == "budget") Assert.True(result.Files.Checks.Sum(check => check.Findings.Count) <= 2);
    }

    [Fact]
    public async Task MultipleReferencesUnionRequirementsWithoutDuplicateFindingsAndAllowExtensions()
    {
        var result = await Inspect("single", "", afterIndex: index =>
        {
            var leaf = index.Descendants("leaf").Single(item => (string?)item.Attribute("ID") == "stf");
            var duplicate = new XElement(leaf); duplicate.SetAttributeValue("ID", "stf-repeat");
            var extended = new XElement(leaf);
            leaf.ReplaceWith(new XElement("node-extension", new XAttribute("ID", "extension"), new XElement("title", "Study"), extended, duplicate));
            var clinical = new XElement(duplicate); clinical.SetAttributeValue("ID", "stf-clinical");
            index.Root!.Add(new XElement("m5-clinical-study-reports", new XElement("m5-3-clinical-study-reports",
                new XElement("m5-3-5-reports-of-efficacy-and-safety-studies", new XAttribute("indication", "Valid"),
                    new XElement("m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication", clinical)))));
        });
        var check = Check(result);
        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Equal("route-of-admin,species,type-of-control", string.Join(',', check.Findings.Select(item => item.Expected).Order(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("ich")]
    [InlineData("us")]
    public async Task DuplicateOrMixedRealmCategoriesRemainUnevaluated(string secondRealm)
    {
        var result = await Inspect("controlled", Categories("type-of-control") +
            "<category name='type-of-control' info-type='" + secondRealm + "'>placebo</category>");
        Assert.Equal(CheckStatus.NotEvaluated, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Fact]
    public async Task DtdDefaultNamespaceStillSupportsCategoryAssessment()
    {
        var result = await Inspect("controlled", Categories("type-of-control"), variant: "default-namespace");
        var stf = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(xml => xml.StudyTaggingFiles.Documents));
        Assert.True(stf.Elements[0].Attributes["xmlns:ectd"].IsDefault);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
    }

    [Fact]
    public async Task KnownHeadingFailureSurvivesUnreadStf()
    {
        var result = await Inspect("controlled", "", "truncated-stf", afterIndex: index =>
            index.Descendants("m5-3-5-reports-of-efficacy-and-safety-studies").Single().Attribute("indication")!.Remove());
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Equal("@indication", Assert.Single(Check(result).Findings).Location.FieldPath);
    }

    [Fact]
    public async Task KnownStfMissingCategorySurvivesAnotherTruncatedStf()
    {
        var result = await Inspect("controlled", "", afterSource: root =>
            File.WriteAllText(Path.Combine(root, "0001/m4/study/stf-z.xml"), "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study"));
        Assert.False(Assert.Single(result.Lifecycle.XmlInspections).StudyTaggingFiles.InventoryComplete);
        Assert.Equal(CheckStatus.Fail, Check(result).Status);
        Assert.Equal("type-of-control", Assert.Single(Check(result).Findings).Expected);
    }

    [Fact]
    public async Task HistoricalMissingCategoriesDoNotChangeTargetRequirements()
    {
        var result = await Inspect("other", "", "history-categories", afterSource: root =>
        {
            var oldIndex = Path.Combine(root, "0000/index.xml");
            var old = XDocument.Load(oldIndex);
            var stf = new XElement("leaf", new XAttribute("ID", "old-stf"), new XAttribute("operation", "new"),
                new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
                new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-old.xml"), new XElement("title", "Old"));
            foreach (var leaf in old.Descendants("leaf")) { leaf.SetAttributeValue("checksum", new string('0', 32)); leaf.SetAttributeValue("checksum-type", "md5"); }
            old.Root!.Add(new XElement("m4-nonclinical-study-reports", new XElement("m4-2-study-reports", new XElement("m4-2-3-toxicology", new XElement("m4-2-3-1-single-dose-toxicity", stf)))));
            old.Save(oldIndex);
            var path = Path.Combine(root, "0000/m4/study/stf-old.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'><ectd:study xmlns:ectd='http://www.ich.org/ectd'><study-identifier><title>Old</title><study-id>Old</study-id></study-identifier><study-document/></ectd:study>");
        });
        var historical = Assert.Single(result.Lifecycle.XmlInspections, xml => xml.InputDigest != result.Lifecycle.InputDigest);
        Assert.Equal(CheckStatus.Pass, Assert.Single(historical.Backbones, document => document.Kind == BackboneKind.Ich).DtdStatus);
        Assert.True(Assert.Single(historical.StudyTaggingFiles.Documents).ReadComplete);
        Assert.Equal(CheckStatus.Pass, Check(result).Status);
        Assert.Empty(Check(result).Findings);
    }

    [Fact]
    public async Task OneRemainingFindingSlotDoesNotPublishTwoRequiredCategories()
    {
        var result = await Inspect("single", "");
        var snapshot = await new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        var context = PackageStandardsContext.FromSnapshot(snapshot, selectionDate: new(2026, 11, 20));
        var xml = Assert.Single(result.Lifecycle.XmlInspections);
        var rule = context.Catalog.Rules["FDA47-1357"];
        var one = FdaStfRequiredCategoryRule.Inspect(new ObservationInput(xml.InputDigest), xml, context, rule, 1, default);
        Assert.Equal(CheckStatus.Fail, one.Status);
        Assert.Single(one.Findings);
        var none = FdaStfRequiredCategoryRule.Inspect(new ObservationInput(xml.InputDigest), xml, context, rule, 0, default);
        Assert.Equal(CheckStatus.NotEvaluated, none.Status);
        Assert.Empty(none.Findings);
    }

    private static RuleCheckResult Check(PackageDeliveryInspection result) => Assert.Single(result.Files.Checks, item => item.RuleId == "FDA47-1357");
    private static string Categories(string names) => string.Join("", names.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(name =>
        "<category name='" + name + "' info-type='" + (name == "duration" ? "us" : "ich") + "'>" +
        (name switch { "species" => "rat", "route-of-admin" => "oral", "duration" => "long", _ => "placebo" }) + "</category>"));

    internal static async Task<PackageDeliveryInspection> Inspect(string section, string categories, string variant = "valid", string? date = "2026-11-20",
        Action<XDocument>? afterIndex = null, Action<string>? afterSource = null)
    {
        var xml = "<!DOCTYPE ectd:study SYSTEM '../../util/dtd/ich-stf-v2-2.dtd'>" +
            "<ectd:study xmlns:ectd='http://www.ich.org/ectd'><study-identifier><title>A</title><study-id>A</study-id>" +
            categories + "</study-identifier><study-document/></ectd:study>";
        if (variant == "truncated-stf") xml = xml[..^12];
        if (variant == "default-namespace") xml = xml.Replace(" xmlns:ectd='http://www.ich.org/ectd'", "", StringComparison.Ordinal);
        var result = await FdaExtraneousLeafPathRulesTests.Inspect("new", "m1/us/us-regional.xml", null, variant, date,
            editSource: afterSource,
            capturedName: variant == "missing-stf" ? null : "m4/study/stf-a.xml", capturedBytes: Encoding.UTF8.GetBytes(xml),
            editSnapshot: variant != "missing-spec" ? null : snapshot =>
            {
                var source = snapshot.Resources.Single(item => item.Key == "official-fda-stf-specification");
                return RATools.Domain.Standards.StandardsProfileSnapshot.Create(snapshot.Descriptor with
                { Provenance = snapshot.Descriptor.Provenance.Where(item => item.Sha256 != source.Sha256).ToArray() },
                    snapshot.Resources.Where(item => item.Key != source.Key), snapshot.AcquiredUtc);
            },
            editIndex: index =>
            {
                foreach (var leaf in index.Descendants("leaf"))
                {
                    leaf.SetAttributeValue("checksum", new string('0', 32));
                    leaf.SetAttributeValue("checksum-type", "md5");
                }
                if (variant == "unreferenced") return;
                string[] chain = section switch
                {
                    "single" => ["m4-nonclinical-study-reports", "m4-2-study-reports", "m4-2-3-toxicology", "m4-2-3-1-single-dose-toxicity"],
                    "repeat" => ["m4-nonclinical-study-reports", "m4-2-study-reports", "m4-2-3-toxicology", "m4-2-3-2-repeat-dose-toxicity"],
                    "long-term" => ["m4-nonclinical-study-reports", "m4-2-study-reports", "m4-2-3-toxicology", "m4-2-3-4-carcinogenicity", "m4-2-3-4-1-long-term-studies"],
                    "controlled" => ["m5-clinical-study-reports", "m5-3-clinical-study-reports", "m5-3-5-reports-of-efficacy-and-safety-studies", "m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication"],
                    _ => ["m4-nonclinical-study-reports", "m4-3-literature-references"]
                };
                var parent = index.Root!;
                foreach (var name in chain)
                {
                    var child = new XElement(name);
                    if (name == "m5-3-5-reports-of-efficacy-and-safety-studies") child.SetAttributeValue("indication", "Valid");
                    parent.Add(child); parent = child;
                }
                if (variant == "wrong-position") { parent.Remove(); index.Root!.Add(parent); }
                parent.Add(new XElement("leaf", new XAttribute("ID", "stf"), new XAttribute("operation", "new"),
                    new XAttribute("checksum", new string('0', 32)), new XAttribute("checksum-type", "md5"),
                    new XAttribute(XName.Get("href", "http://www.w3c.org/1999/xlink"), "m4/study/stf-a.xml"), new XElement("title", "Study")));
                afterIndex?.Invoke(index);
            });
        if (afterIndex is null && variant is "valid" or "regional-category" or "unreferenced" or "missing-stf" or "truncated-stf" or "missing-spec" or "default-namespace")
        {
            var index = Assert.Single(result.Lifecycle.XmlInspections.SelectMany(item => item.Backbones), item => item.Kind == BackboneKind.Ich);
            Assert.True(index.ReadComplete);
            Assert.Equal(CheckStatus.Pass, index.DtdStatus);
        }
        return result;
    }

    // Only the already-bound XML observations are used by this internal rule; content may not be reopened.
    private sealed class ObservationInput(string digest) : ICapturedPackageInput
    {
        public string SourceId => "category-observations";
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
