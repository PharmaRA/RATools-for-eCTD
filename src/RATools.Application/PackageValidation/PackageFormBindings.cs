using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

public sealed record PackageFormApplicationNumber(string NodePath, string Number,
    ParsedXmlAttributeValue? Type, ValidationLocation Location);

public sealed class PackageFormApplication(string nodePath, ParsedXmlAttributeValue? containingFiles,
    IEnumerable<PackageFormApplicationNumber> numbers)
{
    public string NodePath { get; } = nodePath;
    public ParsedXmlAttributeValue? ContainingFiles { get; } = containingFiles;
    public IReadOnlyList<PackageFormApplicationNumber> Numbers { get; } = Array.AsReadOnly(numbers.ToArray());
}

// These are XML candidate groups, not assertions that every leaf is a main form.
// The official M1 specification permits attachments under the same form element.
public sealed class PackageFormGroup(string sourceLogicalPath, string nodePath, string scope,
    ParsedXmlAttributeValue? formType, bool xmlReadComplete, CheckStatus dtdStatus,
    IEnumerable<PackageFormApplication> applications, IEnumerable<ParsedPackageLeaf> leaves)
{
    public string SourceLogicalPath { get; } = sourceLogicalPath;
    public string NodePath { get; } = nodePath;
    public string Scope { get; } = scope;
    public ParsedXmlAttributeValue? FormType { get; } = formType;
    public bool XmlReadComplete { get; } = xmlReadComplete;
    public CheckStatus DtdStatus { get; } = dtdStatus;
    public IReadOnlyList<PackageFormApplication> Applications { get; } = Array.AsReadOnly(applications.ToArray());
    public IReadOnlyList<ParsedPackageLeaf> Leaves { get; } = Array.AsReadOnly(leaves.ToArray());
}

public sealed class PackageFormBindings(bool inventoryComplete, IEnumerable<PackageFormGroup> groups)
{
    public bool InventoryComplete { get; } = inventoryComplete;
    public IReadOnlyList<PackageFormGroup> Groups { get; } = Array.AsReadOnly(groups.ToArray());
    public static PackageFormBindings Unknown { get; } = new(false, []);
}
