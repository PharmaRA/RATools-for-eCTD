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
        RejectUnmappedModule1Leaves(package);

        var root = new XElement(FdaRegionalNamespace + "fda-regional",
            new XAttribute(XNamespace.Xmlns + "fda-regional", FdaRegionalNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", "3.3"),
            BuildAdminElement(package));

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

    private static void RejectUnmappedModule1Leaves(EctdSequencePackage package)
    {
        var leaf = package.Module1Leaves.FirstOrDefault();
        if (leaf is not null)
        {
            throw new UsRegionalXmlSectionMappingException(
                package.ApplicationId,
                package.SequenceNumber,
                leaf.PlacementId,
                leaf.CtdSection,
                "Module 1 leaf mapping is not implemented");
        }
    }

    private static void Require(EctdSequencePackage package, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsRegionalXmlMetadataException(package.ApplicationId, package.SequenceNumber, fieldName, "is required");
        }
    }
}
