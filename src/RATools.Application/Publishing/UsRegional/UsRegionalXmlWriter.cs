using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.UsRegional;

public sealed class UsRegionalXmlWriter : IUsRegionalXmlWriter
{
    private static readonly XNamespace FdaRegionalNamespace = "http://www.ich.org/fda";
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";

    public UsRegionalXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateRequiredMetadata(package);

        var root = new XElement(FdaRegionalNamespace + "fda-regional",
            new XAttribute(XNamespace.Xmlns + "fda-regional", FdaRegionalNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", "3.3"),
            BuildAdminElement(package));
        var m1Regional = BuildM1RegionalElement(package);
        if (m1Regional is not null)
        {
            root.Add(m1Regional);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType("fda-regional:fda-regional", null, "../../util/dtd/us-regional-v3-3.dtd", null),
            root);

        return new UsRegionalXmlWriteResult(
            "us-regional.xml",
            "m1/us/us-regional.xml",
            document,
            document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement BuildAdminElement(EctdSequencePackage package)
    {
        var metadata = package.UsRegional;
        var submissionInformationChildren = new List<object>
        {
            new XElement("submission-id",
                new XAttribute("submission-type", metadata.SubmissionType),
                package.SequenceNumber),
            new XElement("sequence-number",
                new XAttribute("submission-sub-type", metadata.SubmissionSubtype),
                package.SequenceNumber)
        };

        if (!string.IsNullOrWhiteSpace(metadata.FormType))
        {
            submissionInformationChildren.Add(new XElement("form",
                new XAttribute("form-type", metadata.FormType)));
        }

        return new XElement("admin",
            new XElement("applicant-info",
                new XElement("id", metadata.ApplicantId),
                new XElement("company-name", metadata.CompanyName),
                new XElement("submission-description", metadata.SubmissionDescription),
                new XElement("applicant-contacts",
                    new XElement("applicant-contact",
                        new XElement("applicant-contact-name",
                            new XAttribute("applicant-contact-type", metadata.ApplicantContactType),
                            metadata.ApplicantContactName),
                        new XElement("telephones",
                            new XElement("telephone",
                                new XAttribute("telephone-number-type", metadata.TelephoneNumberType),
                                metadata.Telephone)),
                        new XElement("emails",
                            new XElement("email", metadata.Email))))),
            new XElement("application-set",
                new XElement("application",
                    new XAttribute("application-containing-files", package.Module1Leaves.Count > 0 ? "true" : "false"),
                    new XElement("application-information",
                        new XElement("application-number",
                            new XAttribute("application-type", metadata.ApplicationType),
                            package.ApplicationNumber)),
                    new XElement("submission-information", submissionInformationChildren))));
    }

    private static XElement? BuildM1RegionalElement(EctdSequencePackage package)
    {
        ValidateLeaves(package);

        var leavesBySection = package.Module1Leaves
            .Select((leaf, index) => new IndexedLeaf(leaf, index))
            .GroupBy(x => x.Leaf.CtdSection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(leaf => leaf.Index).ThenBy(leaf => leaf.Leaf.LeafId, StringComparer.OrdinalIgnoreCase).Select(leaf => leaf.Leaf).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var childElements = UsRegionalM1V33.Root.Children
            .Select(child => BuildSectionElement(child, leavesBySection))
            .Where(child => child is not null)
            .Cast<XElement>()
            .ToArray();

        return childElements.Length == 0
            ? null
            : new XElement("m1-regional", childElements);
    }

    private static XElement? BuildSectionElement(
        UsRegionalSectionNode node,
        IReadOnlyDictionary<string, EctdLeaf[]> leavesBySection)
    {
        leavesBySection.TryGetValue(node.SectionPath, out var leaves);
        var childElements = node.Children
            .Select(child => BuildSectionElement(child, leavesBySection))
            .Where(child => child is not null)
            .Cast<XElement>()
            .ToArray();

        if ((leaves is null || leaves.Length == 0) && childElements.Length == 0)
        {
            return null;
        }

        var element = new XElement(node.ElementName);
        if (leaves is not null)
        {
            foreach (var leaf in leaves)
            {
                element.Add(BuildLeafElement(leaf));
            }
        }

        element.Add(childElements);
        return element;
    }

    private static XElement BuildLeafElement(EctdLeaf leaf)
    {
        var attributes = new List<object>
        {
            new XAttribute("ID", leaf.LeafId),
            new XAttribute("operation", leaf.Operation),
            new XAttribute("checksum", leaf.Sha256),
            new XAttribute("checksum-type", "sha256"),
            new XAttribute(XlinkNamespace + "type", "simple"),
            new XAttribute(XlinkNamespace + "href", BuildRegionalHref(leaf.Href))
        };

        if (leaf.Lifecycle is not null)
        {
            attributes.Add(new XAttribute("modified-file", BuildModifiedFileHref(leaf.Lifecycle)));
        }

        return new XElement("leaf",
            attributes,
            new XElement("title", leaf.Title));
    }

    private static string BuildRegionalHref(string sequenceRootHref)
    {
        const string module1Prefix = "m1/us/";
        return sequenceRootHref.StartsWith(module1Prefix, StringComparison.OrdinalIgnoreCase)
            ? sequenceRootHref[module1Prefix.Length..]
            : sequenceRootHref;
    }

    private static string BuildModifiedFileHref(EctdLifecycleReference lifecycle)
        => $"../../../{lifecycle.TargetSequenceNumber}/{lifecycle.ModifiedFileHref}";

    private static void ValidateRequiredMetadata(EctdSequencePackage package)
    {
        var metadata = package.UsRegional;
        Require(package, nameof(metadata.ApplicantId), metadata.ApplicantId);
        Require(package, nameof(metadata.CompanyName), metadata.CompanyName);
        Require(package, nameof(metadata.SubmissionDescription), metadata.SubmissionDescription);
        Require(package, nameof(metadata.ApplicantContactName), metadata.ApplicantContactName);
        Require(package, nameof(metadata.ApplicantContactType), metadata.ApplicantContactType);
        Require(package, nameof(metadata.Telephone), metadata.Telephone);
        Require(package, nameof(metadata.TelephoneNumberType), metadata.TelephoneNumberType);
        Require(package, nameof(metadata.Email), metadata.Email);
        Require(package, nameof(metadata.ApplicationType), metadata.ApplicationType);
        Require(package, nameof(metadata.SubmissionType), metadata.SubmissionType);
        Require(package, nameof(metadata.SubmissionSubtype), metadata.SubmissionSubtype);
    }

    private static void ValidateLeaves(EctdSequencePackage package)
    {
        foreach (var leaf in package.Module1Leaves)
        {
            if (!string.Equals(leaf.Module, "m1", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsRegionalXmlSectionMappingException(
                    package.ApplicationId,
                    package.SequenceNumber,
                    leaf.PlacementId,
                    leaf.CtdSection,
                    "leaf is not a Module 1 leaf");
            }
        }
    }

    private static void Require(EctdSequencePackage package, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsRegionalXmlMetadataException(package.ApplicationId, package.SequenceNumber, fieldName, "is required");
        }
    }

    private sealed record IndexedLeaf(EctdLeaf Leaf, int Index);
}
