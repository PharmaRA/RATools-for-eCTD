using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed class PackageXmlInspector(TimeProvider? timeProvider = null)
{
    private static readonly string[] CommonRuleIds = ["XML-OFFLINE", "XML-LIMITS", "PROFILE-VERSION", "XML-IDS", "NODE-CONTEXT", "NODE-EXTENSIONS"];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<PackageXmlInspection> InspectAsync(ICapturedPackageInput input, string profileSnapshotId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var assets = PackageXmlAssets.Current;
        var profiles = assets.ForProfile(profileSnapshotId);
        input.Limits.Validate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(input.Limits.MaxElapsedSeconds), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var findings = new XmlInspectionFindings(input.Limits);
        var documents = new List<ParsedBackbone>();
        var interrupted = false;
        try
        {
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
            var ichProfile = profiles.Single(profile => profile.Kind == BackboneKind.Ich);
            var ich = await Read(input.Manifest.SequenceNumber + "/index.xml", ichProfile).ConfigureAwait(false);
            documents.Add(ich);
            if (ich.ReadComplete)
            {
                var regionalProfile = profiles.SingleOrDefault(profile => profile.Kind != BackboneKind.Ich);
                var regionalPaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var leaf in ich.Leaves.Where(leaf => leaf.IsRegionalReference && leaf.Operation != "delete"))
                {
                    if (regionalProfile is null)
                    {
                        findings.Add(new("PROFILE-VERSION", CheckStatus.NotEvaluated, ValidationSeverity.Warning, "REGIONAL_PROFILE_REQUIRED",
                            "An ICH-only profile cannot inspect a referenced regional backbone. Select the explicit regional profile.", leaf.Location));
                        continue;
                    }
                    PackageReference? reference = null;
                    try { if (leaf.Href is not null) reference = PackageLogicalPath.ResolveReference(ich.LogicalPath, leaf.Href); }
                    catch (ArgumentException) { /* Report the original location and value below. */ }
                    if (reference?.LogicalPath is not { } path || reference.Query is not null || reference.Fragment is not null ||
                        !path.StartsWith(input.Manifest.SequenceNumber + "/", StringComparison.Ordinal) || !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new(regionalProfile.RuleId, CheckStatus.Fail, ValidationSeverity.Error, "INVALID_REGIONAL_BACKBONE_REFERENCE",
                            "The regional backbone must resolve to an exact XML file in the selected sequence.", leaf.Location, Actual: leaf.Href));
                        continue;
                    }
                    regionalPaths.Add(path);
                }
                if (regionalProfile is not null)
                {
                    var defaultPath = input.Manifest.SequenceNumber + "/" + regionalProfile.DefaultRelativePath;
                    if (input.Manifest.Files.Any(file => file.LogicalPath == defaultPath)) regionalPaths.Add(defaultPath);
                    foreach (var path in regionalPaths.Order(StringComparer.Ordinal))
                    {
                        documents.Add(await Read(path, regionalProfile).ConfigureAwait(false));
                        if (findings.StructureLimitReached || findings.Truncated) break;
                    }
                }
            }
            await input.VerifyUnchangedAsync(linked.Token).ConfigureAwait(false);
        }
        catch (XmlInspectionLimitException) { interrupted = true; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            interrupted = true;
            try { findings.Add(new("XML-LIMITS", CheckStatus.Fail, ValidationSeverity.Error, "XML_READ_TIMEOUT", "XML inspection exceeded its configured deadline.", new(input.Manifest.SequenceNumber))); }
            catch (XmlInspectionLimitException) { }
        }

        var complete = !interrupted && !findings.Truncated && documents.Count > 0 && documents.All(document => document.ReadComplete);
        var results = new List<RuleCheckResult>();
        foreach (var ruleId in CommonRuleIds.Concat(profiles.Select(profile => profile.RuleId)))
        {
            var observations = findings.Items.Where(finding => finding.RuleId == ruleId).ToArray();
            var status = observations.Any(finding => finding.CheckStatus == CheckStatus.Fail) ? CheckStatus.Fail :
                observations.Any(finding => finding.CheckStatus == CheckStatus.NotEvaluated) || !complete ? CheckStatus.NotEvaluated : CheckStatus.Pass;
            var reason = status == CheckStatus.Pass ? "The selected XML inspection completed for all observed backbones." : "See located observations and incomplete backbone checks.";
            var regional = profiles.FirstOrDefault(profile => profile.RuleId == ruleId && profile.Kind != BackboneKind.Ich);
            if (regional is not null && complete && documents.All(document => document.Kind != regional.Kind) && observations.Length == 0)
            {
                status = CheckStatus.NotApplicable;
                reason = "No regional backbone is referenced or delivered in this input. Regional completeness criteria remain separate, unqualified catalog checks.";
            }
            if (ruleId == "NODE-EXTENSIONS" && status == CheckStatus.Pass && documents.Any(document => document.Nodes.Any(node => node.Kind == Domain.Ctd.CtdNodeKind.Extension)))
            {
                status = CheckStatus.NotEvaluated;
                reason = "Extension structure was inspected; region-specific extension acceptance is not yet qualified.";
            }
            results.Add(new(ruleId, status, reason, observations));
        }
        return new(input.InputDigest, profileSnapshotId, assets.Digest, input.Limits.Digest(), documents, results);

        async Task<ParsedBackbone> Read(string path, PackageXmlBackboneProfile profile)
        {
            linked.Token.ThrowIfCancellationRequested();
            if (documents.Count >= input.Limits.MaxBackbones)
            {
                findings.Add(new("XML-LIMITS", CheckStatus.Fail, ValidationSeverity.Error, "BACKBONE_COUNT_LIMIT", "Too many backbone XML documents.", new(LogicalPath: path)));
                throw new XmlInspectionLimitException("Backbone count limit exceeded.");
            }
            var document = await PackageXmlDocumentReader.ReadAsync(input, path, assets, profile, findings, linked.Token).ConfigureAwait(false);
            return PackageXmlIdentityInspector.Inspect(document, input.Manifest.ApplicationId, input.Manifest.SequenceNumber,
                path, profileSnapshotId, profile.Kind, profile.RuleId, findings, linked.Token);
        }
    }
}
