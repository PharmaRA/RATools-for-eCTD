using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class PackageDeliveryInspector(IPackagePdfInspector pdfInspector, TimeProvider? timeProvider = null, PackageStandardsContext? standards = null)
{
    public const string DeliveryEngineVersion = "package-delivery-v4";
    public string EngineVersion => VersionFor(pdfInspector.EngineVersion);
    public static string VersionFor(string pdfInspectorVersion) => DeliveryEngineVersion + ";package-lifecycle-inspector-v1;package-file-inspector-v2;package-pdf-validator-v2;" + pdfInspectorVersion + ";" + PackageRegionalInspector.RegionalEngineVersion;
    public PackageDeliveryInspector WithStandards(PackageStandardsContext context) => new(pdfInspector, timeProvider, context);
    public async Task<PackageDeliveryInspection> InspectAsync(PackageDeliveryInput input, string profileSnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(input.Packages.Target.Limits.MaxElapsedSeconds), timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var context = standards ?? PackageStandardsContext.Current;
            var lifecycle = await new PackageLifecycleInspector(timeProvider, context).InspectAsync(input.Packages, profileSnapshotId, linked.Token).ConfigureAwait(false);
            var files = await new PackageFileInspector(timeProvider, context).InspectAsync(input, lifecycle, linked.Token).ConfigureAwait(false);
            var pdf = await new PackagePdfValidator(pdfInspector, timeProvider, context).InspectAsync(input, files, linked.Token).ConfigureAwait(false);
            var regional = new PackageRegionalInspector(context).Inspect(input.Packages, lifecycle, linked.Token);
            var structure = InspectReachability(input, lifecycle, files, pdf, context, linked.Token);
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
            return new(input, lifecycle, files, pdf, structure, context.Catalog, regional);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new PackageInputException("INPUT-LIMITS", "DELIVERY_INSPECTION_TIMEOUT", "The complete delivery inspection exceeded its configured deadline."); }
    }

    private static RuleCheckResult InspectReachability(PackageDeliveryInput input, PackageLifecycleInspection lifecycle,
        PackageFileInspection files, PackagePdfInspection pdf, PackageStandardsContext context, CancellationToken cancellationToken)
    {
        var inventory = input.Packages.Sequences.Values.SelectMany(capture => capture.Manifest.Files).Select(file => file.LogicalPath).ToHashSet(StringComparer.Ordinal);
        var edges = files.References.Where(reference => reference.Exists && reference.Resolved?.LogicalPath is not null)
            .Select(reference => (Source: reference.SourceLogicalPath, Target: reference.Resolved!.LogicalPath!))
            .Concat(pdf.Links.Where(link => link.TargetLogicalPath is not null).Select(link => (Source: link.SourceLogicalPath, Target: link.TargetLogicalPath!)))
            .ToLookup(edge => edge.Source, edge => edge.Target, StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capture in input.Packages.Sequences.Values)
        {
            var number = capture.Manifest.SequenceNumber;
            roots.Add(number + "/index.xml");
            roots.Add(number + "/index-md5.txt");
            var profile = input.Packages.Baseline.Entries.SingleOrDefault(entry => entry.SequenceNumber == number)?.ProfileSnapshotId ?? lifecycle.ProfileSnapshotId;
            IReadOnlyList<PackageXmlBackboneProfile> profiles;
            var assets = context.ForSequence(number).Assets;
            try { profiles = assets.ForProfile(profile); }
            catch (ArgumentException) { continue; }
            foreach (var id in profiles.SelectMany(profile => profile.AllowedAssetIds.Concat(profile.StylesheetAssetIds)).Distinct(StringComparer.Ordinal))
                roots.Add(number + "/" + assets.Assets[id].LogicalPath);
        }
        var queue = new Queue<string>(roots);
        while (queue.TryDequeue(out var source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var target in edges[source]) if (roots.Add(target)) queue.Enqueue(target);
        }
        var original = files.Checks.Single(check => check.RuleId == "DELIVERY-STRUCTURE");
        var findings = original.Findings.ToList();
        foreach (var path in inventory.Except(roots, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (findings.Count >= input.Packages.Target.Limits.MaxXmlFindings - 1)
            {
                findings.Add(new("DELIVERY-STRUCTURE", CheckStatus.NotEvaluated, ValidationSeverity.Warning, "STRUCTURE_FINDING_LIMIT",
                    "Remaining unreferenced-file observations exceed the configured finding limit.", new()));
                break;
            }
            findings.Add(new("DELIVERY-STRUCTURE", CheckStatus.NotEvaluated, ValidationSeverity.Warning, "UNREFERENCED_DELIVERY_FILE",
                "No inspected reference chain reaches this delivered file; review its role under the applicable profile.", new(path.Split('/')[0], path)));
        }
        var incomplete = original.Status == CheckStatus.NotEvaluated || findings.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) ||
            files.Checks.Any(check => check.RuleId == "FILE-HREF" && check.Status == CheckStatus.NotEvaluated) ||
            pdf.Checks.Any(check => check.RuleId == "PDF-LINKS" && check.Status == CheckStatus.NotEvaluated);
        return new("DELIVERY-STRUCTURE", original.Status == CheckStatus.Fail ? CheckStatus.Fail : incomplete ? CheckStatus.NotEvaluated : CheckStatus.Pass,
            "Reference reachability starts at selected backbones and known utility assets; orphan cycles do not validate themselves.", findings);
    }
}

public sealed class PackageDeliveryInspection
{
    private readonly ValidationRuleCatalog _catalog;
    private static readonly string[] CapturedRuleIds =
        ["INPUT-LAYOUT", "INPUT-PATHS", "INPUT-LIMITS", "INPUT-STABLE", "ZIP-PATHS", "ZIP-LIMITS"];
    internal PackageDeliveryInspection(PackageDeliveryInput input, PackageLifecycleInspection lifecycle, PackageFileInspection files,
        PackagePdfInspection pdf, RuleCheckResult structure, ValidationRuleCatalog? catalog = null, PackageRegionalInspection? regional = null)
    {
        InputDigest = input.InputDigest;
        Lifecycle = lifecycle;
        Files = files;
        Pdf = pdf;
        Regional = regional ?? new([]);
        _catalog = catalog ?? PackageValidationCatalog.Current;
        var scoped = _catalog.ForProfile(lifecycle.ProfileSnapshotId).Select(rule => rule.InternalRuleId).ToHashSet(StringComparer.Ordinal);
        // ZIP-only rules are explicitly NotApplicable when every selected source is a
        // directory; otherwise an absent observation would read as an unexplained gap.
        var zipPresent = input.Comparison?.SourceKind == PackageSourceKind.Zip ||
            input.Packages.Sequences.Values.Any(capture => capture.SourceKind == PackageSourceKind.Zip);
        var capturedChecks = CapturedRuleIds.Select(rule =>
            new RuleCheckResult(rule, rule is "ZIP-PATHS" or "ZIP-LIMITS" && !zipPresent ? CheckStatus.NotApplicable : CheckStatus.Pass,
                "Controlled input capture and final revalidation completed for every explicitly selected source; resource limits bind this run."));
        var observations = lifecycle.XmlInspections.SelectMany(xml => xml.Checks).Where(check => scoped.Contains(check.RuleId))
            .Concat(lifecycle.Checks).Concat(files.Checks.Where(check => check.RuleId != "DELIVERY-STRUCTURE")).Concat(pdf.Checks).Concat(Regional.Checks).Append(structure).Concat(capturedChecks);
        Checks = Array.AsReadOnly(observations.GroupBy(check => check.RuleId, StringComparer.Ordinal).Select(group =>
        {
            var results = group.ToArray();
            var status = results.Any(check => check.Status == CheckStatus.Fail) ? CheckStatus.Fail :
                results.Any(check => check.Status == CheckStatus.NotEvaluated) ? CheckStatus.NotEvaluated :
                results.All(check => check.Status == CheckStatus.NotApplicable) ? CheckStatus.NotApplicable : CheckStatus.Pass;
            return new RuleCheckResult(group.Key, status, string.Join(" ", results.Select(check => check.Reason).Distinct(StringComparer.Ordinal)), results.SelectMany(check => check.Findings));
        }).ToArray());
    }
    public int SchemaVersion { get; } = 1;
    public string InputDigest { get; }
    public string HistoryManifestDigest => Lifecycle.HistoryManifestDigest;
    public string ProfileSnapshotId => Lifecycle.ProfileSnapshotId;
    public string RulesDigest => Lifecycle.RulesDigest;
    public string LimitsDigest => Lifecycle.LimitsDigest;
    public string EngineVersion => PackageDeliveryInspector.DeliveryEngineVersion + ";" + Lifecycle.EngineVersion + ";" + Files.EngineVersion + ";" + Pdf.EngineVersion + ";" + Regional.EngineVersion;
    public PackageLifecycleInspection Lifecycle { get; }
    public PackageFileInspection Files { get; }
    public PackagePdfInspection Pdf { get; }
    public PackageRegionalInspection Regional { get; }
    public IReadOnlyList<RuleCheckResult> Checks { get; }
    public ValidationReport CreateReport(PackageValidationMode mode, ValidationBinding? exactBinding = null, IReadOnlyList<string>? standardsBlockingReasons = null) => ValidationReport.Create(_catalog,
        exactBinding ?? new(InputDigest, HistoryManifestDigest, ProfileSnapshotId, RulesDigest, EngineVersion, mode, LimitsDigest), ValidationRunStatus.Completed, Checks).WithBlockingReasons(standardsBlockingReasons ?? []);
}
