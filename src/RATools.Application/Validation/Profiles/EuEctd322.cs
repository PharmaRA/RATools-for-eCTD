using RATools.Application.Validation;

namespace RATools.Application.Validation.Profiles;

/// <summary>
/// EU M1 v3.1.1 Appendix 2 section and directory structure, followed by the
/// shared ICH eCTD 3.2.2 Modules 2-5 tree.
/// </summary>
public static class EuEctd322
{
    public const string ProfileName = "eu-ectd-3.2.2";

    public static readonly SectionDictionaryManualNode Root = Node(
        elementName: "ectd:ectd",
        sectionPath: string.Empty,
        title: "eCTD",
        folderName: null,
        children:
        [
            Node(
                "m1-eu",
                "m1",
                "Module 1 EU Administrative Information",
                "m1",
                [
                    Node("m1-0-cover", "m1.0", "1.0 Cover Letter", "10-cover", []),
                    Node("m1-2-form", "m1.2", "1.2 Application Form", "12-form", []),
                    Node(
                        "m1-3-pi",
                        "m1.3",
                        "1.3 Product Information",
                        "13-pi",
                        [
                            Node("m1-3-1-spc-label-pl", "m1.3.1", "1.3.1 SmPC, Labelling and Package Leaflet", "131-spclabelpl", []),
                            Node("m1-3-2-mockup", "m1.3.2", "1.3.2 Mock-up", "132-mockup", []),
                            Node("m1-3-3-specimen", "m1.3.3", "1.3.3 Specimen", "133-specimen", []),
                            Node("m1-3-4-consultation", "m1.3.4", "1.3.4 Consultation with Target Patient Groups", "134-consultation", []),
                            Node("m1-3-5-approved", "m1.3.5", "1.3.5 Product Information Already Approved in the Member States", "135-approved", []),
                            Node("m1-3-6-braille", "m1.3.6", "1.3.6 Braille", "136-braille", []),
                        ]),
                    Node(
                        "m1-4-expert",
                        "m1.4",
                        "1.4 Information about the Experts",
                        "14-expert",
                        [
                            Node("m1-4-1-quality", "m1.4.1", "1.4.1 Quality", "141-quality", []),
                            Node("m1-4-2-non-clinical", "m1.4.2", "1.4.2 Non-Clinical", "142-nonclinical", []),
                            Node("m1-4-3-clinical", "m1.4.3", "1.4.3 Clinical", "143-clinical", []),
                        ]),
                    Node(
                        "m1-5-specific",
                        "m1.5",
                        "1.5 Specific Requirements for Different Types of Applications",
                        "15-specific",
                        [
                            Node("m1-5-1-bibliographic", "m1.5.1", "1.5.1 Information for Bibliographical Applications", "151-bibliographic", []),
                            Node("m1-5-2-generic-hybrid-bio-similar", "m1.5.2", "1.5.2 Information for Generic, Hybrid or Bio-similar Applications", "152-generic-hybrid-bio-similar", []),
                            Node("m1-5-3-data-market-exclusivity", "m1.5.3", "1.5.3 Data or Market Exclusivity", "153-data-market-exclusivity", []),
                            Node("m1-5-4-exceptional-circumstances", "m1.5.4", "1.5.4 Exceptional Circumstances", "154-exceptional", []),
                            Node("m1-5-5-conditional-ma", "m1.5.5", "1.5.5 Conditional Marketing Authorisation", "155-conditional-ma", []),
                        ]),
                    Node(
                        "m1-6-environrisk",
                        "m1.6",
                        "1.6 Environmental Risk Assessment",
                        "16-environrisk",
                        [
                            Node("m1-6-1-non-gmo", "m1.6.1", "1.6.1 Non-GMO", "161-nongmo", []),
                            Node("m1-6-2-gmo", "m1.6.2", "1.6.2 GMO", "162-gmo", []),
                        ]),
                    Node(
                        "m1-7-orphan",
                        "m1.7",
                        "1.7 Information Relating to Orphan Market Exclusivity",
                        "17-orphan",
                        [
                            Node("m1-7-1-similarity", "m1.7.1", "1.7.1 Similarity", "171-similarity", []),
                            Node("m1-7-2-market-exclusivity", "m1.7.2", "1.7.2 Market Exclusivity", "172-market-exclusivity", []),
                        ]),
                    Node(
                        "m1-8-pharmacovigilance",
                        "m1.8",
                        "1.8 Information Relating to Pharmacovigilance",
                        "18-pharmacovigilance",
                        [
                            Node("m1-8-1-pharmacovigilance-system", "m1.8.1", "1.8.1 Pharmacovigilance System", "181-phvig-system", []),
                            Node("m1-8-2-risk-management-system", "m1.8.2", "1.8.2 Risk-management System", "182-riskmgt-system", []),
                        ]),
                    Node("m1-9-clinical-trials", "m1.9", "1.9 Information Relating to Clinical Trials", "19-clinical-trials", []),
                    Node("m1-10-paediatrics", "m1.10", "1.10 Information Relating to Paediatrics", "110-paediatrics", []),
                    Node("m1-responses", "m1.responses", "Responses to Questions", "responses", []),
                    Node("m1-additional-data", "m1.additional-data", "Additional Data", "additional-data", []),
                ]),
            .. FdaEctd322.Root.Children.Skip(1),
        ]);

    public static readonly IReadOnlyDictionary<string, EctdWorkspacePathResolution> CanonicalWorkspaceFolders =
        BuildCanonicalWorkspaceFolders();

    public static SectionDictionaryProfile ToProfile()
    {
        var nodes = Flatten(Root)
            .Where(x => !string.IsNullOrWhiteSpace(x.SectionPath))
            .ToArray();

        return new SectionDictionaryProfile
        {
            Name = ProfileName,
            ByElementName = nodes.ToDictionary(
                x => x.ElementName,
                x => new SectionDictionaryEntry(x.ElementName, x.SectionPath, ProfileName),
                StringComparer.OrdinalIgnoreCase),
            BySectionPath = nodes
                .GroupBy(x => x.SectionPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(n => new SectionDictionaryEntry(n.ElementName, n.SectionPath, ProfileName)).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static SectionDictionaryManualNode Node(
        string elementName,
        string sectionPath,
        string title,
        string? folderName,
        IReadOnlyCollection<SectionDictionaryManualNode> children)
        => new(elementName, sectionPath, title, children, folderName);

    private static IEnumerable<SectionDictionaryManualNode> Flatten(SectionDictionaryManualNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static Dictionary<string, EctdWorkspacePathResolution> BuildCanonicalWorkspaceFolders()
    {
        var folders = new Dictionary<string, EctdWorkspacePathResolution>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in Root.Children)
        {
            AddCanonicalWorkspaceFolders(module, [], folders);
        }

        return folders;
    }

    private static void AddCanonicalWorkspaceFolders(
        SectionDictionaryManualNode node,
        IReadOnlyList<SectionDictionaryManualNode> ancestors,
        Dictionary<string, EctdWorkspacePathResolution> folders)
    {
        if (string.IsNullOrWhiteSpace(node.FolderName))
        {
            throw new InvalidOperationException($"Section '{node.SectionPath}' is missing canonical folder metadata.");
        }

        var nodePath = ancestors.Concat([node]).ToArray();
        folders[node.SectionPath] = new EctdWorkspacePathResolution(
            "EU",
            node.SectionPath,
            node.ElementName,
            BuildRelativeFolderPath(nodePath));

        foreach (var child in node.Children)
        {
            AddCanonicalWorkspaceFolders(child, nodePath, folders);
        }
    }

    private static string BuildRelativeFolderPath(SectionDictionaryManualNode[] nodePath)
    {
        if (string.Equals(nodePath[0].SectionPath, "m1", StringComparison.OrdinalIgnoreCase))
        {
            return nodePath.Length == 1
                ? Path.Combine("m1", "eu")
                : Path.Combine(["m1", "eu", .. nodePath.Skip(1).Select(x => x.FolderName!)]);
        }

        return Path.Combine(nodePath.Select(x => x.FolderName!).ToArray());
    }
}
