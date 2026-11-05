using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Domain.RegionalMetadata;

namespace RATools.Application.Publishing.UsRegional;

public sealed partial class UsRegionalXmlWriter
{
    internal static void ValidateTypedMetadata(EctdSequencePackage package)
    {
        var revision = package.RegionalMetadata!;
        revision.Verify();
        if (revision.ApplicationId != package.ApplicationId || revision.SequenceNumber != package.SequenceNumber ||
            revision.RegionKey != "US" || package.Application.TemplateKey != "us-fda-ectd-3.2.2" ||
            revision.WorkspaceRevision > package.WorkspaceRevision)
            throw MetadataError(package, "regionalMetadata", "does not match the exact application, sequence, region or workspace scope");
        var us = revision.Content!.Us!;
        foreach (var (value, field) in new[] { (us.ApplicantIdentifier, "applicantIdentifier"), (us.SubmissionIdentifier, "submissionIdentifier"),
            (us.ApplicationIdentifier, "applicationIdentifier"), (us.ApplicationType, "applicationType"), (us.ApplicantName, "applicantName"),
            (us.SubmissionType, "submissionType"), (us.SubmissionSubtype, "submissionSubtype"), (us.Description, "description") })
            Require(package, "us." + field, value);
        if (us.Contacts.Count == 0) throw MetadataError(package, "us.contacts", "requires one or more explicit contacts");
        for (var index = 0; index < us.Contacts.Count; index++)
        {
            var contact = us.Contacts[index]; var path = $"us.contacts[{index}]";
            Require(package, path + ".name", contact.Name); Require(package, path + ".type", contact.Type);
            if (contact.Phones.Count == 0) throw MetadataError(package, path + ".phones", "requires one or more explicit telephones");
            if (contact.Emails.Count == 0) throw MetadataError(package, path + ".emails", "requires one or more explicit emails");
            for (var phone = 0; phone < contact.Phones.Count; phone++)
            { Require(package, path + $".phones[{phone}].number", contact.Phones[phone].Number); Require(package, path + $".phones[{phone}].type", contact.Phones[phone].Type); }
            for (var email = 0; email < contact.Emails.Count; email++) Require(package, path + $".emails[{email}].value", contact.Emails[email].Value);
        }
        for (var index = 0; index < revision.DocumentContexts.Count; index++)
        {
            var context = revision.DocumentContexts[index]; var path = $"documentContexts[{index}]";
            var leaf = package.Module1Leaves.Concat(package.IchBackboneLeaves).SingleOrDefault(item => item.PlacementId == context.PlacementId);
            if (leaf is null) throw MetadataError(package, path + ".placementId", $"references placement {context.PlacementId} outside this package");
            if (leaf.NodeInstanceId != context.NodeInstanceId) throw ContextError(package, leaf, path + ".nodeInstanceId", "does not match the exact placement node");
            ValidateContext(package, leaf, path, context.Country, context.Language, context.ProductInformationType, context.FormType);
        }
        for (var index = 0; index < revision.NodeContexts.Count; index++)
        {
            var context = revision.NodeContexts[index]; var path = $"nodeContexts[{index}]";
            var leaves = package.Module1Leaves.Concat(package.IchBackboneLeaves).Where(leaf => leaf.NodeInstanceId == context.NodeInstanceId).ToArray();
            if (leaves.Length == 0 && package.Nodes?.All(node => node.NodeInstanceId != context.NodeInstanceId) != false)
                throw MetadataError(package, path + ".nodeInstanceId", $"references node {context.NodeInstanceId} outside this package");
            foreach (var leaf in leaves) ValidateContext(package, leaf, path, context.Country, context.Language, context.ProductInformationType, context.FormType);
        }
        foreach (var leaf in package.Module1Leaves.Where(leaf => string.Equals(leaf.CtdSection, "m1.1", StringComparison.OrdinalIgnoreCase))) _ = ResolveForm(package, leaf);
    }

    private static void ValidateContext(EctdSequencePackage package, EctdLeaf leaf, string path, string? country, string? language, string? productType, string? formType)
    {
        foreach (var (value, field) in new[] { (country, "country"), (productType, "productInformationType") })
            if (value is not null) throw ContextError(package, leaf, path + "." + field, "is outside the scoped US form context");
        if (formType is not null && !string.Equals(leaf.CtdSection, "m1.1", StringComparison.OrdinalIgnoreCase)) throw ContextError(package, leaf, path + ".formType", "applies only to m1.1");
    }

    private static string ResolveForm(EctdSequencePackage package, EctdLeaf leaf)
    {
        var revision = package.RegionalMetadata!; string? value; string path;
        var documentIndex = revision.DocumentContexts.ToList().FindIndex(context => context.PlacementId == leaf.PlacementId);
        var nodeIndex = leaf.NodeInstanceId is { } node ? revision.NodeContexts.ToList().FindIndex(context => context.NodeInstanceId == node) : -1;
        if (documentIndex >= 0) { value = revision.DocumentContexts[documentIndex].FormType; path = $"documentContexts[{documentIndex}].formType"; }
        else if (nodeIndex >= 0) { value = revision.NodeContexts[nodeIndex].FormType; path = $"nodeContexts[{nodeIndex}].formType"; }
        else { value = revision.Content!.Us!.FormType; path = "us.formType"; }
        if (string.IsNullOrWhiteSpace(value)) throw ContextError(package, leaf, path, "requires an explicit form type; cleared context cannot inherit a default");
        return value;
    }

    private static XElement? BuildTypedForms(EctdSequencePackage package, string relativePath)
    {
        XElement? forms = null; XElement? wrapper = null; string? previousType = null;
        foreach (var leaf in package.Module1Leaves.Where(leaf => string.Equals(leaf.CtdSection, "m1.1", StringComparison.OrdinalIgnoreCase)))
        {
            var type = ResolveForm(package, leaf);
            forms ??= new XElement("m1-1-forms");
            if (wrapper is null || type != previousType)
            { wrapper = new XElement("form", new XAttribute("form-type", type)); forms.Add(wrapper); previousType = type; }
            wrapper.Add(BuildLeafElement(leaf, relativePath, package));
        }
        return forms;
    }

    private static XElement BuildTypedAdminElement(EctdSequencePackage package)
    {
        var us = package.RegionalMetadata!.Content!.Us!;
        return new XElement("admin",
            new XElement("applicant-info", new XElement("id", us.ApplicantIdentifier), new XElement("company-name", us.ApplicantName),
                new XElement("submission-description", us.Description),
                new XElement("applicant-contacts", us.Contacts.Select(contact => new XElement("applicant-contact",
                    new XElement("applicant-contact-name", new XAttribute("applicant-contact-type", contact.Type!), contact.Name),
                    new XElement("telephones", contact.Phones.Select(phone => new XElement("telephone", new XAttribute("telephone-number-type", phone.Type!), phone.Number))),
                    new XElement("emails", contact.Emails.Select(email => new XElement("email", email.Value))))))),
            new XElement("application-set", new XElement("application", new XAttribute("application-containing-files", package.Module1Leaves.Count > 0 ? "true" : "false"),
                new XElement("application-information", new XElement("application-number", new XAttribute("application-type", us.ApplicationType!), us.ApplicationIdentifier)),
                new XElement("submission-information", new XElement("submission-id", new XAttribute("submission-type", us.SubmissionType!), us.SubmissionIdentifier),
                    new XElement("sequence-number", new XAttribute("submission-sub-type", us.SubmissionSubtype!), package.SequenceNumber)))));
    }

    private static UsRegionalXmlMetadataException ContextError(EctdSequencePackage package, EctdLeaf leaf, string field, string reason) =>
        MetadataError(package, field, $"{reason} (placement {leaf.PlacementId}, node {leaf.NodeInstanceId?.ToString() ?? "unbound"})");
    private static UsRegionalXmlMetadataException MetadataError(EctdSequencePackage package, string field, string reason) =>
        new(package.ApplicationId, package.SequenceNumber, field, reason);
}
