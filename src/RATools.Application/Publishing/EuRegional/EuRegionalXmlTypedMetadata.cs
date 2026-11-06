using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Domain.RegionalMetadata;

namespace RATools.Application.Publishing.EuRegional;

public sealed partial class EuRegionalXmlWriter
{
    private static readonly HashSet<string> SpecificSections = new(StringComparer.OrdinalIgnoreCase)
    { "m1.0", "m1.2", "m1.3.2", "m1.3.3", "m1.3.4", "m1.3.5", "m1.responses", "m1.additional-data" };

    internal static void ValidateTypedMetadata(EctdSequencePackage package)
    {
        var revision = package.RegionalMetadata!; revision.Verify();
        if (revision.ApplicationId != package.ApplicationId || revision.SequenceNumber != package.SequenceNumber || revision.RegionKey != "EU" ||
            package.Application.Region != "EU" || package.Application.TemplateKey != "eu-ectd-3.2.2" || revision.WorkspaceRevision > package.WorkspaceRevision)
            throw MetadataError("regionalMetadata", "does not match the exact application, sequence, region or workspace scope");
        var eu = revision.Content!.Eu!;
        if (eu.Envelopes.Count == 0) throw MetadataError("eu.envelopes", "requires one or more explicit ordered envelopes");
        for (var index = 0; index < eu.Envelopes.Count; index++)
        {
            var value = eu.Envelopes[index]; var path = $"eu.envelopes[{index}]";
            foreach (var (field, name) in new[] { (value.Identifier, "identifier"), (value.Country, "country"), (value.Agency, "agency"), (value.Procedure, "procedure"),
                (value.SubmissionType, "submissionType"), (value.SubmissionUnit, "submissionUnit"), (value.Applicant, "applicant"), (value.Description, "description") })
                Require(path + "." + name, field);
            TrialCountry(value.Country, path + ".country");
            if (value.Procedure != "national") throw MetadataError(path + ".procedure", "is outside the current national DE/FR/BE trial writer scope");
            Texts(value.ProcedureTrackingNumbers, path + ".procedureTrackingNumbers", required: true);
            Texts(value.InventedNames, path + ".inventedNames", required: true); Texts(value.Inns, path + ".inns", required: false);
            if (value.RelatedSequences.Count == 0) throw MetadataError(path + ".relatedSequences", "requires explicit related sequence references");
            for (var relatedIndex = 0; relatedIndex < value.RelatedSequences.Count; relatedIndex++)
            {
                var related = value.RelatedSequences[relatedIndex].SequenceNumber;
                var initial = value.SubmissionUnit is "initial" or "reformat";
                if (initial ? related != package.SequenceNumber : string.CompareOrdinal(related, package.SequenceNumber) >= 0)
                    throw MetadataError(path + $".relatedSequences[{relatedIndex}].sequenceNumber", "requires the current sequence for initial/reformat and an earlier existing sequence for other units");
            }
        }
        var allLeaves = package.Module1Leaves.Concat(package.IchBackboneLeaves).ToArray();
        for (var index = 0; index < revision.DocumentContexts.Count; index++)
        {
            var context = revision.DocumentContexts[index]; var path = $"documentContexts[{index}]";
            var leaf = allLeaves.SingleOrDefault(item => item.PlacementId == context.PlacementId)
                ?? throw MetadataError(path + ".placementId", "references a placement outside this package");
            if (leaf.NodeInstanceId != context.NodeInstanceId) throw MetadataError(path + ".nodeInstanceId", "does not match the exact placement node");
            ValidateContext(leaf.CtdSection, new(context.Country, context.Language, context.ProductInformationType, context.FormType, path));
        }
        for (var index = 0; index < revision.NodeContexts.Count; index++)
        {
            var context = revision.NodeContexts[index]; var path = $"nodeContexts[{index}]";
            var leaves = allLeaves.Where(leaf => leaf.NodeInstanceId == context.NodeInstanceId).ToArray();
            if (leaves.Length == 0 && package.Nodes?.All(node => node.NodeInstanceId != context.NodeInstanceId) != false)
                throw MetadataError(path + ".nodeInstanceId", "references a node outside this package");
            var value = new LeafContext(context.Country, context.Language, context.ProductInformationType, context.FormType, path);
            if (leaves.Length == 0) ValidateContext("", value);
            foreach (var leaf in leaves) ValidateContext(leaf.CtdSection, value);
        }
        if (!package.Module1Leaves.Any(leaf => string.Equals(leaf.CtdSection, "m1.0", StringComparison.OrdinalIgnoreCase)))
            throw MetadataError("m1.0.country", "requires a real cover placement with an explicit country context; empty EU cover nodes are not represented by the current node model");
        foreach (var leaf in package.Module1Leaves)
        {
            var context = ResolveContext(package, leaf);
            if (SpecificSections.Contains(leaf.CtdSection) || IsPi(leaf.CtdSection)) Require(context.Path + ".country", context.Country);
            if (IsPi(leaf.CtdSection))
            { Require(context.Path + ".language", context.Language); Require(context.Path + ".productInformationType", context.ProductInformationType); }
        }
    }

    private static void ValidateContext(string section, LeafContext context)
    {
        if (context.FormType is not null) throw MetadataError(context.Path + ".formType", "FDA form context is not applicable to EU output");
        if (context.ProductInformationType is not null && !IsPi(section))
            throw MetadataError(context.Path + ".productInformationType", "applies only to the exact m1.3.1 placement or node");
        if (context.Country is not null)
        {
            if (!SpecificSections.Contains(section) && !IsPi(section)) throw MetadataError(context.Path + ".country", "the exact section has no country wrapper");
            TrialCountry(context.Country, context.Path + ".country");
        }
    }

    private static LeafContext ResolveContext(EctdSequencePackage package, EctdLeaf leaf)
    {
        var revision = package.RegionalMetadata!;
        for (var index = 0; index < revision.DocumentContexts.Count; index++)
            if (revision.DocumentContexts[index] is { } context && context.PlacementId == leaf.PlacementId)
                return new(context.Country, context.Language, context.ProductInformationType, context.FormType, $"documentContexts[{index}]");
        for (var index = 0; index < revision.NodeContexts.Count; index++)
            if (revision.NodeContexts[index] is { } context && context.NodeInstanceId == leaf.NodeInstanceId)
                return new(context.Country, context.Language, context.ProductInformationType, context.FormType, $"nodeContexts[{index}]");
        return new(null, null, null, null, $"module1Leaves[{leaf.PlacementId:D}].context");
    }

    private static List<XElement> BuildContextWrappers(IEnumerable<EctdLeaf> leaves, EctdSequencePackage package, string relativePath, bool productInformation)
    {
        var wrappers = new List<XElement>(); XElement? wrapper = null;
        (string? Country, string? Language, string? Type)? previous = null;
        foreach (var leaf in leaves)
        {
            var context = ResolveContext(package, leaf);
            var key = (context.Country, productInformation ? context.Language : null, productInformation ? context.ProductInformationType : null);
            if (wrapper is null || key != previous)
            {
                wrapper = new XElement(productInformation ? "pi-doc" : "specific", new XAttribute("country", context.Country!));
                if (productInformation) wrapper.Add(new XAttribute(XNamespace.Xml + "lang", context.Language!), new XAttribute("type", context.ProductInformationType!));
                wrappers.Add(wrapper); previous = key;
            }
            wrapper.Add(BuildLeafElement(leaf, relativePath, package));
        }
        return wrappers;
    }

    private static XElement BuildTypedEnvelopes(EctdSequencePackage package)
        => new("eu-envelope", package.RegionalMetadata!.Content!.Eu!.Envelopes.Select(value =>
        {
            var submission = new XElement("submission", new XAttribute("type", value.SubmissionType!));
            if (value.SubmissionMode is not null) submission.Add(new XAttribute("mode", value.SubmissionMode));
            if (value.SubmissionNumber is not null) submission.Add(new XElement("number", value.SubmissionNumber));
            submission.Add(new XElement("procedure-tracking", value.ProcedureTrackingNumbers.Select(item => new XElement("number", item.Value))));
            return new XElement("envelope", new XAttribute("country", value.Country!), new XElement("identifier", value.Identifier), submission,
                new XElement("submission-unit", new XAttribute("type", value.SubmissionUnit!)), new XElement("applicant", value.Applicant),
                new XElement("agency", new XAttribute("code", value.Agency!)), new XElement("procedure", new XAttribute("type", value.Procedure!)),
                value.InventedNames.Select(item => new XElement("invented-name", item.Value)), value.Inns.Select(item => new XElement("inn", item.Value)),
                new XElement("sequence", package.SequenceNumber), value.RelatedSequences.Select(item => new XElement("related-sequence", item.SequenceNumber)),
                new XElement("submission-description", value.Description));
        }));

    private static void Texts(IReadOnlyList<RegionalTextEntry> values, string path, bool required)
    {
        if (required && values.Count == 0) throw MetadataError(path, "requires one or more explicit ordered values");
        for (var index = 0; index < values.Count; index++) Require(path + $"[{index}].value", values[index].Value);
    }
    private static void TrialCountry(string? country, string path)
    { if (country is not ("de" or "fr" or "be")) throw MetadataError(path, "is outside the current national DE/FR/BE trial writer scope"); }
    private static bool IsPi(string section) => string.Equals(section, "m1.3.1", StringComparison.OrdinalIgnoreCase);
    private static EuRegionalXmlWriterException MetadataError(string path, string reason) => new($"Unable to generate EU regional XML: {path} {reason}.", path);
    private sealed record LeafContext(string? Country, string? Language, string? ProductInformationType, string? FormType, string Path);
}
