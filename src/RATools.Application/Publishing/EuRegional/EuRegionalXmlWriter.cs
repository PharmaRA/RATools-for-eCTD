using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;

namespace RATools.Application.Publishing.EuRegional;

public sealed class EuRegionalXmlWriter : IEuRegionalXmlWriter
{
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";

    private static readonly string[] DirectTopLevelSections =
    [
        "m1.9",
        "m1.10"
    ];

    public EuRegionalXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var xmlProfile = package.BackboneXml.Regional;
        if (!ReferenceEquals(package.BackboneXml, BackboneXmlProfiles.EuEctd322Regional)
            && !string.Equals(xmlProfile.RelativePath, BackboneXmlProfiles.EuEctd322Regional.Regional.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new EuRegionalXmlWriterException("Unable to generate EU regional XML: package does not use an EU regional backbone profile.");
        }

        var relativePath = RequireRelativePath(xmlProfile.RelativePath);
        var metadata = package.EuRegional ?? BuildDefaultMetadata(package);
        ValidateMetadata(metadata);
        ValidateLeaves(package);

        XNamespace euNamespace = xmlProfile.Namespace;
        var root = new XElement(euNamespace + xmlProfile.RootElementName,
            new XAttribute(XNamespace.Xmlns + "eu", euNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", xmlProfile.DtdVersion),
            BuildEnvelope(metadata),
            BuildModuleOne(package, metadata, relativePath));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType(xmlProfile.DocumentTypeName, null, xmlProfile.DtdSystemId, null),
            root);

        return new EuRegionalXmlWriteResult(
            "eu-regional.xml",
            relativePath,
            document,
            document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement BuildEnvelope(EctdEuRegionalMetadata metadata)
    {
        var submission = new XElement("submission", new XAttribute("type", metadata.SubmissionType));
        if (!string.IsNullOrWhiteSpace(metadata.SubmissionMode))
        {
            submission.Add(new XAttribute("mode", metadata.SubmissionMode));
        }

        if (!string.IsNullOrWhiteSpace(metadata.SubmissionNumber))
        {
            submission.Add(new XElement("number", metadata.SubmissionNumber));
        }

        submission.Add(new XElement("procedure-tracking",
            metadata.ProcedureTrackingNumbers.Select(number => new XElement("number", number))));

        return new XElement("eu-envelope",
            new XElement("envelope",
                new XAttribute("country", metadata.Country),
                new XElement("identifier", metadata.Identifier),
                submission,
                new XElement("submission-unit", new XAttribute("type", metadata.SubmissionUnit)),
                new XElement("applicant", metadata.Applicant),
                new XElement("agency", new XAttribute("code", metadata.AgencyCode)),
                new XElement("procedure", new XAttribute("type", metadata.ProcedureType)),
                metadata.InventedNames.Select(name => new XElement("invented-name", name)),
                metadata.Inns.Select(inn => new XElement("inn", inn)),
                new XElement("sequence", metadata.SequenceNumber),
                metadata.RelatedSequences.Select(number => new XElement("related-sequence", number)),
                new XElement("submission-description", metadata.SubmissionDescription)));
    }

    private static XElement BuildModuleOne(
        EctdSequencePackage package,
        EctdEuRegionalMetadata metadata,
        string regionalRelativePath)
    {
        var leaves = package.Module1Leaves
            .OrderBy(leaf => leaf.CtdSection, StringComparer.OrdinalIgnoreCase)
            .ThenBy(leaf => leaf.LeafId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var moduleOne = new XElement("m1-eu");

        // m1-0-cover is mandatory in the official DTD, even when its country
        // wrapper contains no leaf in a lifecycle sequence.
        moduleOne.Add(BuildSpecificSection("m1-0-cover", "m1.0", leaves, metadata.DocumentCountry, regionalRelativePath, required: true));
        AddIfNotNull(moduleOne, BuildSpecificSection("m1-2-form", "m1.2", leaves, metadata.DocumentCountry, regionalRelativePath));

        var productInformation = new XElement("m1-3-pi");
        AddIfNotNull(productInformation, BuildProductInformationSection(leaves, metadata, regionalRelativePath));
        AddIfNotNull(productInformation, BuildSpecificSection("m1-3-2-mockup", "m1.3.2", leaves, metadata.DocumentCountry, regionalRelativePath));
        AddIfNotNull(productInformation, BuildSpecificSection("m1-3-3-specimen", "m1.3.3", leaves, metadata.DocumentCountry, regionalRelativePath));
        AddIfNotNull(productInformation, BuildSpecificSection("m1-3-4-consultation", "m1.3.4", leaves, metadata.DocumentCountry, regionalRelativePath));
        AddIfNotNull(productInformation, BuildSpecificSection("m1-3-5-approved", "m1.3.5", leaves, metadata.DocumentCountry, regionalRelativePath));
        AddIfNotNull(productInformation, BuildDirectSection("m1-3-6-braille", "m1.3.6", leaves, regionalRelativePath));
        AddIfHasChildren(moduleOne, productInformation);

        AddIfNotNull(moduleOne, BuildContainer("m1-4-expert", leaves, regionalRelativePath,
            ("m1-4-1-quality", "m1.4.1"),
            ("m1-4-2-non-clinical", "m1.4.2"),
            ("m1-4-3-clinical", "m1.4.3")));
        AddIfNotNull(moduleOne, BuildContainer("m1-5-specific", leaves, regionalRelativePath,
            ("m1-5-1-bibliographic", "m1.5.1"),
            ("m1-5-2-generic-hybrid-bio-similar", "m1.5.2"),
            ("m1-5-3-data-market-exclusivity", "m1.5.3"),
            ("m1-5-4-exceptional-circumstances", "m1.5.4"),
            ("m1-5-5-conditional-ma", "m1.5.5")));
        AddIfNotNull(moduleOne, BuildContainer("m1-6-environrisk", leaves, regionalRelativePath,
            ("m1-6-1-non-gmo", "m1.6.1"),
            ("m1-6-2-gmo", "m1.6.2")));
        AddIfNotNull(moduleOne, BuildContainer("m1-7-orphan", leaves, regionalRelativePath,
            ("m1-7-1-similarity", "m1.7.1"),
            ("m1-7-2-market-exclusivity", "m1.7.2")));
        AddIfNotNull(moduleOne, BuildContainer("m1-8-pharmacovigilance", leaves, regionalRelativePath,
            ("m1-8-1-pharmacovigilance-system", "m1.8.1"),
            ("m1-8-2-risk-management-system", "m1.8.2")));

        foreach (var sectionPath in DirectTopLevelSections)
        {
            var elementName = sectionPath == "m1.9" ? "m1-9-clinical-trials" : "m1-10-paediatrics";
            AddIfNotNull(moduleOne, BuildDirectSection(elementName, sectionPath, leaves, regionalRelativePath));
        }

        AddIfNotNull(moduleOne, BuildSpecificSection("m1-responses", "m1.responses", leaves, metadata.DocumentCountry, regionalRelativePath));
        AddIfNotNull(moduleOne, BuildSpecificSection("m1-additional-data", "m1.additional-data", leaves, metadata.DocumentCountry, regionalRelativePath));
        return moduleOne;
    }

    private static XElement? BuildContainer(
        string elementName,
        IReadOnlyCollection<EctdLeaf> leaves,
        string regionalRelativePath,
        params (string ElementName, string SectionPath)[] sections)
    {
        var container = new XElement(elementName);
        foreach (var section in sections)
        {
            AddIfNotNull(container, BuildDirectSection(section.ElementName, section.SectionPath, leaves, regionalRelativePath));
        }

        return container.HasElements ? container : null;
    }

    private static XElement? BuildSpecificSection(
        string elementName,
        string sectionPath,
        IReadOnlyCollection<EctdLeaf> leaves,
        string country,
        string regionalRelativePath,
        bool required = false)
    {
        var matching = FindLeaves(sectionPath, leaves);
        if (matching.Length == 0 && !required)
        {
            return null;
        }

        return new XElement(elementName,
            new XElement("specific",
                new XAttribute("country", country),
                matching.Select(leaf => BuildLeafElement(leaf, regionalRelativePath))));
    }

    private static XElement? BuildProductInformationSection(
        IReadOnlyCollection<EctdLeaf> leaves,
        EctdEuRegionalMetadata metadata,
        string regionalRelativePath)
    {
        var matching = FindLeaves("m1.3.1", leaves);
        return matching.Length == 0
            ? null
            : new XElement("m1-3-1-spc-label-pl",
                new XElement("pi-doc",
                    new XAttribute(XNamespace.Xml + "lang", metadata.ProductInformationLanguage),
                    new XAttribute("type", metadata.ProductInformationType),
                    new XAttribute("country", metadata.DocumentCountry),
                    matching.Select(leaf => BuildLeafElement(leaf, regionalRelativePath))));
    }

    private static XElement? BuildDirectSection(
        string elementName,
        string sectionPath,
        IReadOnlyCollection<EctdLeaf> leaves,
        string regionalRelativePath)
    {
        var matching = FindLeaves(sectionPath, leaves);
        return matching.Length == 0
            ? null
            : new XElement(elementName, matching.Select(leaf => BuildLeafElement(leaf, regionalRelativePath)));
    }

    private static EctdLeaf[] FindLeaves(string sectionPath, IReadOnlyCollection<EctdLeaf> leaves)
        => leaves.Where(leaf => string.Equals(leaf.CtdSection, sectionPath, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static XElement BuildLeafElement(EctdLeaf leaf, string regionalRelativePath)
    {
        var attributes = new List<object>
        {
            new XAttribute("ID", leaf.LeafId),
            new XAttribute("operation", leaf.Operation),
            new XAttribute("checksum", leaf.Md5),
            new XAttribute("checksum-type", "md5"),
            new XAttribute(XlinkNamespace + "type", "simple"),
        };

        if (!string.Equals(leaf.Operation, "delete", StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(new XAttribute(XlinkNamespace + "href", BuildRegionalHref(leaf.Href, regionalRelativePath)));
        }

        if (leaf.Lifecycle is not null)
        {
            attributes.Add(new XAttribute("modified-file", $"../../../{leaf.Lifecycle.TargetSequenceNumber}/{leaf.Lifecycle.ModifiedFileHref}"));
        }

        return new XElement("leaf", attributes, new XElement("title", leaf.Title));
    }

    private static string BuildRegionalHref(string sequenceRootHref, string regionalRelativePath)
    {
        var module1Prefix = GetDirectoryName(regionalRelativePath);
        return sequenceRootHref.StartsWith(module1Prefix, StringComparison.OrdinalIgnoreCase)
            ? sequenceRootHref[module1Prefix.Length..]
            : sequenceRootHref;
    }

    private static string GetDirectoryName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : normalized[..(slashIndex + 1)];
    }

    private static void ValidateLeaves(EctdSequencePackage package)
    {
        var supportedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "m1.0", "m1.2", "m1.3.1", "m1.3.2", "m1.3.3", "m1.3.4", "m1.3.5", "m1.3.6",
            "m1.4.1", "m1.4.2", "m1.4.3", "m1.5.1", "m1.5.2", "m1.5.3", "m1.5.4", "m1.5.5",
            "m1.6.1", "m1.6.2", "m1.7.1", "m1.7.2", "m1.8.1", "m1.8.2", "m1.9", "m1.10",
            "m1.responses", "m1.additional-data"
        };

        foreach (var leaf in package.Module1Leaves)
        {
            if (!string.Equals(leaf.Module, "m1", StringComparison.OrdinalIgnoreCase)
                || !supportedSections.Contains(leaf.CtdSection))
            {
                throw new EuRegionalXmlWriterException($"Unable to generate EU regional XML: section '{leaf.CtdSection}' does not directly accept leaves in EU M1 v3.1.");
            }
        }

        if (package.Module1Leaves.Any(leaf => string.Equals(leaf.CtdSection, "m1.6.1", StringComparison.OrdinalIgnoreCase))
            && package.Module1Leaves.Any(leaf => string.Equals(leaf.CtdSection, "m1.6.2", StringComparison.OrdinalIgnoreCase)))
        {
            throw new EuRegionalXmlWriterException("Unable to generate EU regional XML: sections m1.6.1 and m1.6.2 are mutually exclusive in one sequence.");
        }
    }

    private static void ValidateMetadata(EctdEuRegionalMetadata metadata)
    {
        Require(nameof(metadata.Identifier), metadata.Identifier);
        Require(nameof(metadata.Country), metadata.Country);
        Require(nameof(metadata.SubmissionType), metadata.SubmissionType);
        Require(nameof(metadata.SubmissionUnit), metadata.SubmissionUnit);
        Require(nameof(metadata.Applicant), metadata.Applicant);
        Require(nameof(metadata.AgencyCode), metadata.AgencyCode);
        Require(nameof(metadata.ProcedureType), metadata.ProcedureType);
        Require(nameof(metadata.SequenceNumber), metadata.SequenceNumber);
        Require(nameof(metadata.SubmissionDescription), metadata.SubmissionDescription);
        Require(nameof(metadata.DocumentCountry), metadata.DocumentCountry);
        Require(nameof(metadata.ProductInformationLanguage), metadata.ProductInformationLanguage);
        Require(nameof(metadata.ProductInformationType), metadata.ProductInformationType);
        if (metadata.ProcedureTrackingNumbers.Count == 0
            || metadata.InventedNames.Count == 0
            || metadata.RelatedSequences.Count == 0)
        {
            throw new EuRegionalXmlWriterException("Unable to generate EU regional XML: procedure tracking, invented name, and related sequence metadata are required.");
        }
    }

    private static EctdEuRegionalMetadata BuildDefaultMetadata(EctdSequencePackage package)
        => new(
            package.ApplicationId.ToString("D"),
            "ema",
            "maa",
            null,
            null,
            [package.ApplicationNumber],
            "initial",
            package.Sequence.ApplicantName,
            "EU-EMA",
            "centralised",
            [package.ApplicationNumber],
            [],
            package.SequenceNumber,
            [package.SequenceNumber],
            package.Sequence.Description,
            "ema",
            "en",
            "combined");

    private static void AddIfNotNull(XElement parent, XElement? child)
    {
        if (child is not null)
        {
            parent.Add(child);
        }
    }

    private static void AddIfHasChildren(XElement parent, XElement child)
    {
        if (child.HasElements)
        {
            parent.Add(child);
        }
    }

    private static void Require(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new EuRegionalXmlWriterException($"Unable to generate EU regional XML: {fieldName} is required.");
        }
    }

    private static string RequireRelativePath(string? relativePath)
        => string.IsNullOrWhiteSpace(relativePath)
            ? throw new EuRegionalXmlWriterException("Unable to generate EU regional XML: regional relative path is required.")
            : relativePath;
}
