using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class PackageDeliveryInspector(IPackagePdfInspector pdfInspector, TimeProvider? timeProvider = null)
{
    public async Task<PackageDeliveryInspection> InspectAsync(PackageDeliveryInput input, string profileSnapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(input.Packages.Target.Limits.MaxElapsedSeconds), timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var lifecycle = await new PackageLifecycleInspector(timeProvider).InspectAsync(input.Packages, profileSnapshotId, linked.Token).ConfigureAwait(false);
            var files = await new PackageFileInspector(timeProvider).InspectAsync(input, lifecycle, linked.Token).ConfigureAwait(false);
            var pdf = await new PackagePdfValidator(pdfInspector, timeProvider).InspectAsync(input, files, linked.Token).ConfigureAwait(false);
            var structure = InspectReachability(input, lifecycle, files, pdf, linked.Token);
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
            return new(input, lifecycle, files, pdf, structure);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new PackageInputException("INPUT-LIMITS", "DELIVERY_INSPECTION_TIMEOUT", "The complete delivery inspection exceeded its configured deadline."); }
    }

    private static RuleCheckResult InspectReachability(PackageDeliveryInput input, PackageLifecycleInspection lifecycle,
        PackageFileInspection files, PackagePdfInspection pdf, CancellationToken cancellationToken)
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
            try { profiles = PackageXmlAssets.Current.ForProfile(profile); }
            catch (ArgumentException) { continue; }
            foreach (var id in profiles.SelectMany(profile => profile.AllowedAssetIds.Concat(profile.StylesheetAssetIds)).Distinct(StringComparer.Ordinal))
                roots.Add(number + "/" + PackageXmlAssets.Current.Assets[id].LogicalPath);
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
    internal PackageDeliveryInspection(PackageDeliveryInput input, PackageLifecycleInspection lifecycle, PackageFileInspection files,
        PackagePdfInspection pdf, RuleCheckResult structure)
    {
        InputDigest = input.InputDigest;
        Lifecycle = lifecycle;
        Files = files;
        Pdf = pdf;
        var scoped = PackageValidationCatalog.Current.ForProfile(lifecycle.ProfileSnapshotId).Select(rule => rule.InternalRuleId).ToHashSet(StringComparer.Ordinal);
        var observations = lifecycle.XmlInspections.SelectMany(xml => xml.Checks).Where(check => scoped.Contains(check.RuleId))
            .Concat(lifecycle.Checks).Concat(files.Checks.Where(check => check.RuleId != "DELIVERY-STRUCTURE")).Concat(pdf.Checks).Append(structure);
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
    public string EngineVersion => "package-delivery-v1;" + Lifecycle.EngineVersion + ";" + Files.EngineVersion + ";" + Pdf.EngineVersion;
    public PackageLifecycleInspection Lifecycle { get; }
    public PackageFileInspection Files { get; }
    public PackagePdfInspection Pdf { get; }
    public IReadOnlyList<RuleCheckResult> Checks { get; }
    public ValidationReport CreateReport(PackageValidationMode mode) => ValidationReport.Create(PackageValidationCatalog.Current,
        new(InputDigest, HistoryManifestDigest, ProfileSnapshotId, RulesDigest, EngineVersion, mode, LimitsDigest), ValidationRunStatus.Completed, Checks);
}
