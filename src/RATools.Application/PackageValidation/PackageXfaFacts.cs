namespace RATools.Application.PackageValidation;

public sealed record PackageXfaPacket(string Name, string Sha256, long DecodedBytes);
public sealed record PackageXfaDataNode(string NodePath, string LocalName, string NamespaceUri,
    string DirectText, bool HasChildren);

// Physical packet/data observations only. This is not a rendered form, evaluated script,
// template identity, field binding or assertion of FDA form completeness.
public sealed class PackageXfaFacts(bool readComplete, IEnumerable<PackageXfaPacket> packets,
    IEnumerable<PackageXfaDataNode> data, IEnumerable<string>? incompleteReasons = null)
{
    public bool ReadComplete { get; } = readComplete;
    public IReadOnlyList<PackageXfaPacket> Packets { get; } = Array.AsReadOnly(packets.ToArray());
    public IReadOnlyList<PackageXfaDataNode> Data { get; } = Array.AsReadOnly(data.ToArray());
    public IReadOnlyList<string> IncompleteReasons { get; } = Array.AsReadOnly((incompleteReasons ?? []).ToArray());
    public static PackageXfaFacts Unknown { get; } = new(false, [], [], ["XFA packets were not inspected."]);
}
