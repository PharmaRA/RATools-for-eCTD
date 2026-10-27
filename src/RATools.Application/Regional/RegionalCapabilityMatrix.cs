using RATools.Application.Standards;
using RATools.Domain.PackageValidation;

namespace RATools.Application.Regional;

// Capability states are explicit and conservative. "Supported" requires implemented,
// qualified evidence; a placeholder field is never rendered as support. P6's detailed
// application/procedure scoping is not yet sourced, so those fields stay NotEvaluated.
public enum RegionalCapabilityState { Supported, Blocked, NotImplemented, NotEvaluated }

public sealed record RegionalCapability(
    string Region,
    string TemplateKey,
    string ProfileSnapshotId,
    string ApplicationType,
    string ProcedureType,
    string SectionScope,
    RegionalCapabilityState CreateDraft,
    RegionalCapabilityState TrialPublish,
    RegionalCapabilityState Finalize,
    string StandardsStatus,
    string EvidenceStatus,
    IReadOnlyList<string> Limitations);

// Builds the region/profile capability matrix from the shipped standards profiles and the
// validation catalog. This is deliberately a projection of what is true today: no current
// profile is qualified for finalization and EU M1 assets are acquired but not active, so
// neither region can claim formal delivery support.
public static class RegionalCapabilityMatrix
{
    public static IReadOnlyList<RegionalCapability> Build(IStandardsProfileProvider standards,
        ValidationRuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(standards);
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Profiles.Values.OrderBy(profile => profile.ProfileSnapshotId, StringComparer.Ordinal)
            .Select(profile => BuildOne(standards, profile)).ToArray();
    }

    private static RegionalCapability BuildOne(IStandardsProfileProvider standards, ValidationProfileScope profile)
    {
        var region = Region(profile.TemplateKey);
        StandardsProfile? standardsProfile;
        try { standardsProfile = standards.GetProfile(profile.TemplateKey); }
        catch (StandardsProfileNotFoundException)
        {
            // The ICH catalog profile is reserved for independent inspection and has no
            // publishing standards profile; it must not borrow another region's scope.
            standardsProfile = null;
        }
        var lifecycle = standardsProfile?.Lifecycle;
        var standardsStatus = lifecycle is null ? "Unknown" : lifecycle.Status.ToString();
        var limitations = new List<string>();
        if (!profile.QualifiedForFinalization) limitations.Add(profile.QualificationReason);
        if (lifecycle?.Status == StandardsLifecycleStatus.AcquiredNotActive)
            limitations.Add("The regional standard assets are acquired but not active for formal delivery.");
        if (lifecycle?.Status == StandardsLifecycleStatus.Retired)
            limitations.Add("This standards version is retired; a new sequence requires an active snapshot.");
        if (standardsProfile is null) limitations.Add("The standards profile could not be resolved.");

        // A regional M1 exists only where the profile explicitly declares one. ICH-only
        // scope must not inherit a regional claim.
        var sectionScope = standardsProfile?.BackboneXml?.Regional is null
            ? "ICH M2-M5"
            : $"ICH M2-M5 + {region} regional M1";
        var evidence = profile.QualifiedForFinalization ? "Qualified" : "NotEvaluated";
        return new RegionalCapability(
            Region: region,
            TemplateKey: profile.TemplateKey,
            ProfileSnapshotId: profile.ProfileSnapshotId,
            // Fields required by P6-01 before any regional statement can be made.
            ApplicationType: "NotEvaluated",
            ProcedureType: "NotEvaluated",
            SectionScope: sectionScope,
            // An inspection-only catalog entry does not provide a publishing implementation.
            CreateDraft: standardsProfile is null ? RegionalCapabilityState.NotImplemented : RegionalCapabilityState.Supported,
            TrialPublish: standardsProfile is null ? RegionalCapabilityState.NotImplemented : RegionalCapabilityState.Supported,
            Finalize: standardsProfile is not null && profile.QualifiedForFinalization
                ? RegionalCapabilityState.Supported : RegionalCapabilityState.Blocked,
            StandardsStatus: standardsStatus,
            EvidenceStatus: evidence,
            Limitations: limitations);
    }

    private static string Region(string templateKey) => templateKey switch
    {
        "us-fda-ectd-3.2.2" => "US",
        "eu-ectd-3.2.2" => "EU",
        "ich-ectd-3.2.2" => "ICH",
        _ => "Unknown"
    };
}
