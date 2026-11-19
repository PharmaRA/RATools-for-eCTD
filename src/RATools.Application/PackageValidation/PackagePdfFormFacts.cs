namespace RATools.Application.PackageValidation;

public sealed class PackagePdfFormField
{
    public PackagePdfFormField(string name, string fieldType, IEnumerable<string> values,
        IEnumerable<string> defaultValues, bool hasValue, bool readOnly)
    {
        Name = name;
        FieldType = fieldType;
        Values = Array.AsReadOnly(values.ToArray());
        DefaultValues = Array.AsReadOnly(defaultValues.ToArray());
        HasValue = hasValue;
        ReadOnly = readOnly;
    }

    public string Name { get; }
    public string FieldType { get; }
    public IReadOnlyList<string> Values { get; }
    public IReadOnlyList<string> DefaultValues { get; }
    public bool HasValue { get; }
    public bool ReadOnly { get; }
}

public sealed class PackagePdfFormFacts(bool? hasAcroForm, bool hasXfa, bool complete,
    IEnumerable<PackagePdfFormField> fields, IEnumerable<string>? incompleteReasons = null)
{
    public bool? HasAcroForm { get; } = hasAcroForm;
    public bool HasXfa { get; } = hasXfa;
    public bool Complete { get; } = complete;
    public IReadOnlyList<PackagePdfFormField> Fields { get; } = Array.AsReadOnly(fields.ToArray());
    public IReadOnlyList<string> IncompleteReasons { get; } = Array.AsReadOnly((incompleteReasons ?? []).ToArray());
    public static PackagePdfFormFacts Unknown { get; } = new(null, false, false, [], ["Form inspection was not completed."]);
}
