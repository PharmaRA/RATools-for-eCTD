using RATools.Application.Validation;

namespace RATools.Application.Validation.Profiles;

public static class FdaEctd322
{
    public const string ProfileName = "fda-ectd-3.2-manual";

    public static readonly SectionDictionaryManualNode Root = Node(
        elementName: "ectd:ectd",
        sectionPath: string.Empty,
        title: "eCTD",
        folderName: null,
        children:
        [
            Node(
                "m1-administrative-information-and-prescribing-information",
                "m1",
                "Module 1 Administrative Information And Prescribing Information",
                "m1",
                [
                    Node("m1-1-forms", "m1.1", "1.1 Forms", "11-forms", []),
                    Node("m1-2-cover-letters", "m1.2", "1.2 Cover Letters", "12-cover-letters", []),
                    Node("m1-3-administrative-information", "m1.3", "1.3 Administrative Information", "13-administrative-information", []),
                    Node("m1-4-references", "m1.4", "1.4 References", "14-references", []),
                    Node("m1-5-application-status", "m1.5", "1.5 Application Status", "15-application-status", []),
                    Node("m1-6-meetings", "m1.6", "1.6 Meetings", "16-meetings", []),
                    Node("m1-7-fast-track", "m1.7", "1.7 Fast Track", "17-fast-track", []),
                    Node("m1-8-special-protocol-assessment-request", "m1.8", "1.8 Special Protocol Assessment Request", "18-special-protocol-assessment-request", []),
                    Node("m1-9-pediatric-administrative-information", "m1.9", "1.9 Pediatric Administrative Information", "19-pediatric-administrative-information", []),
                    Node("m1-10-dispute-resolution", "m1.10", "1.10 Dispute Resolution", "110-dispute-resolution", []),
                    Node("m1-11-information-amendment-information-not-covered-under-modules-2-to-5", "m1.11", "1.11 Information Amendment Information Not Covered Under Modules 2 To 5", "111-information-amendment-information-not-covered-under-modules-2-to-5", []),
                    Node("m1-12-other-correspondence", "m1.12", "1.12 Other Correspondence", "112-other-correspondence", []),
                    Node("m1-13-annual-report", "m1.13", "1.13 Annual Report", "113-annual-report", []),
                    Node("m1-14-labeling", "m1.14", "1.14 Labeling", "114-labeling", []),
                    Node(
                        "m1-15-promotional-material",
                        "m1.15",
                        "1.15 Promotional Material",
                        "115-promotional-material",
                        [
                            Node(
                                "m1-15-2-materials",
                                "m1.15.2",
                                "1.15.2 Materials",
                                "1152-materials",
                                [
                                    Node(
                                        "m1-15-2-1-material",
                                        "m1.15.2.1",
                                        "1.15.2.1 Material",
                                        "11521-material",
                                        [
                                            Node("m1-15-2-1-1-clean-version", "m1.15.2.1.1", "1.15.2.1.1 Clean Version", "115211-clean-version", [])
                                        ])
                                ]),
                        ]),
                    Node("m1-16-risk-management-plan", "m1.16", "1.16 Risk Management Plan", "116-risk-management-plan", []),
                    Node("m1-17-postmarketing-studies", "m1.17", "1.17 Postmarketing Studies", "117-postmarketing-studies", []),
                    Node("m1-18-proprietary-names", "m1.18", "1.18 Proprietary Names", "118-proprietary-names", []),
                    Node("m1-19-pre-eua-and-eua", "m1.19", "1.19 Pre EUA And EUA", "119-pre-eua-and-eua", []),
                    Node("m1-20-general-investigational-plan-for-initial-ind", "m1.20", "1.20 General Investigational Plan For Initial IND", "120-general-investigational-plan-for-initial-ind", [])
                ]),
            Node(
                "m2-common-technical-document-summaries",
                "m2",
                "Module 2 Common Technical Document Summaries",
                "m2",
                [
                    Node("m2-2-introduction", "m2.2", "2.2 Introduction", "22-introduction", []),
                    Node(
                        "m2-3-quality-overall-summary",
                        "m2.3",
                        "2.3 Quality Overall Summary",
                        "23-quality-overall-summary",
                        [
                            Node("m2-3-s-drug-substance", "m2.3.s", "2.3.S Drug Substance", "23s-drug-substance", []),
                            Node("m2-3-p-drug-product", "m2.3.p", "2.3.P Drug Product", "23p-drug-product", []),
                            Node("m2-3-a-appendices", "m2.3.a", "2.3.A Appendices", "23a-appendices", []),
                            Node("m2-3-r-regional-information", "m2.3.r", "2.3.R Regional Information", "23r-regional-information", [])
                        ]),
                    Node("m2-4-nonclinical-overview", "m2.4", "2.4 Nonclinical Overview", "24-nonclinical-overview", []),
                    Node("m2-5-clinical-overview", "m2.5", "2.5 Clinical Overview", "25-clinical-overview", []),
                    Node("m2-6-nonclinical-written-and-tabulated-summaries", "m2.6", "2.6 Nonclinical Written And Tabulated Summaries", "26-nonclinical-written-and-tabulated-summaries", []),
                    Node("m2-7-clinical-summary", "m2.7", "2.7 Clinical Summary", "27-clinical-summary", [])
                ]),
            Node(
                "m3-quality",
                "m3",
                "Module 3 Quality",
                "m3",
                [
                    Node(
                        "m3-2-body-of-data",
                        "m3.2",
                        "3.2 Body Of Data",
                        "32-body-of-data",
                        [
                            Node("m3-2-s-drug-substance", "m3.2.s", "3.2.S Drug Substance", "32s-drug-substance", []),
                            Node(
                                "m3-2-p-drug-product",
                                "m3.2.p",
                                "3.2.P Drug Product",
                                "32p-drug-product",
                                [
                                    Node(
                                        "m3-2-p-4-control-of-excipients",
                                        "m3.2.p.4",
                                        "3.2.P.4 Control Of Excipients",
                                        "32p4-control-of-excipients",
                                        [
                                            Node("m3-2-p-4-5-excipients-of-human-or-animal-origin", "m3.2.p.4.5", "3.2.P.4.5 Excipients Of Human Or Animal Origin", "32p45-excipients-of-human-or-animal-origin", [])
                                        ])
                                ]),
                            Node(
                                "m3-2-a-appendices",
                                "m3.2.a",
                                "3.2.A Appendices",
                                "32a-appendices",
                                [
                                    Node("m3-2-a-1-facilities-and-equipment", "m3.2.a.1", "3.2.A.1 Facilities And Equipment", "32a1-facilities-and-equipment", [])
                                ]),
                            Node("m3-2-r-regional-information", "m3.2.r", "3.2.R Regional Information", "32r-regional-information", [])
                        ]),
                    Node("m3-3-literature-references", "m3.3", "3.3 Literature References", "33-literature-references", [])
                ]),
            Node(
                "m4-nonclinical-study-reports",
                "m4",
                "Module 4 Nonclinical Study Reports",
                "m4",
                [
                    Node(
                        "m4-2-study-reports",
                        "m4.2",
                        "4.2 Study Reports",
                        "42-study-reports",
                        [
                            Node("m4-2-1-pharmacology", "m4.2.1", "4.2.1 Pharmacology", "421-pharmacology", []),
                            Node(
                                "m4-2-2-pharmacokinetics",
                                "m4.2.2",
                                "4.2.2 Pharmacokinetics",
                                "422-pharmacokinetics",
                                [
                                    Node("m4-2-2-6-pharmacokinetic-drug-interactions", "m4.2.2.6", "4.2.2.6 Pharmacokinetic Drug Interactions", "4226-pharmacokinetic-drug-interactions", [])
                                ])
                        ]),
                    Node("m4-3-literature-references", "m4.3", "4.3 Literature References", "43-literature-references", [])
                ]),
            Node(
                "m5-clinical-study-reports",
                "m5",
                "Module 5 Clinical Study Reports",
                "m5",
                [
                    Node("m5-2-tabular-listing-of-all-clinical-studies", "m5.2", "5.2 Tabular Listing Of All Clinical Studies", "52-tabular-listing-of-all-clinical-studies", []),
                    Node(
                        "m5-3-clinical-study-reports",
                        "m5.3",
                        "5.3 Clinical Study Reports",
                        "53-clinical-study-reports",
                        [
                            Node("m5-3-1-reports-of-biopharmaceutic-studies", "m5.3.1", "5.3.1 Reports Of Biopharmaceutic Studies", "531-reports-of-biopharmaceutic-studies", []),
                            Node("m5-3-2-reports-of-studies-pertinent-to-pharmacokinetics-using-human-biomaterials", "m5.3.2", "5.3.2 Reports Of Studies Pertinent To Pharmacokinetics Using Human Biomaterials", "532-reports-of-studies-pertinent-to-pharmacokinetics-using-human-biomaterials", []),
                            Node("m5-3-3-reports-of-human-pharmacokinetics-pk-studies", "m5.3.3", "5.3.3 Reports Of Human Pharmacokinetics PK Studies", "533-reports-of-human-pharmacokinetics-pk-studies", []),
                            Node("m5-3-4-reports-of-human-pharmacodynamics-pd-studies", "m5.3.4", "5.3.4 Reports Of Human Pharmacodynamics PD Studies", "534-reports-of-human-pharmacodynamics-pd-studies", []),
                            Node(
                                "m5-3-5-reports-of-efficacy-and-safety-studies",
                                "m5.3.5",
                                "5.3.5 Reports Of Efficacy And Safety Studies",
                                "535-reports-of-efficacy-and-safety-studies",
                                [
                                    Node("m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication", "m5.3.5.1", "5.3.5.1 Controlled Clinical Studies", "5351-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication", []),
                                    Node("m5-3-5-2-study-reports-of-uncontrolled-clinical-studies", "m5.3.5.2", "5.3.5.2 Uncontrolled Clinical Studies", "5352-study-reports-of-uncontrolled-clinical-studies", []),
                                    Node("m5-3-5-3-reports-of-analyses-of-data-from-more-than-one-study", "m5.3.5.3", "5.3.5.3 Analyses Of Data From More Than One Study", "5353-reports-of-analyses-of-data-from-more-than-one-study", []),
                                    Node("m5-3-5-4-other-study-reports", "m5.3.5.4", "5.3.5.4 Other Study Reports", "5354-other-study-reports", [])
                                ]),
                            Node("m5-3-6-reports-of-postmarketing-experience", "m5.3.6", "5.3.6 Reports Of Postmarketing Experience", "536-reports-of-postmarketing-experience", []),
                            Node("m5-3-7-case-report-forms-and-individual-patient-listings", "m5.3.7", "5.3.7 Case Report Forms And Individual Patient Listings", "537-case-report-forms-and-individual-patient-listings", [])
                        ]),
                    Node("m5-4-literature-references", "m5.4", "5.4 Literature References", "54-literature-references", [])
                ])
        ]);

    public static readonly IReadOnlyDictionary<string, EctdWorkspacePathResolution> CanonicalWorkspaceFolders = BuildCanonicalWorkspaceFolders();

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

    private static IReadOnlyDictionary<string, EctdWorkspacePathResolution> BuildCanonicalWorkspaceFolders()
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
            "US",
            node.SectionPath,
            node.ElementName,
            BuildRelativeFolderPath(nodePath));

        foreach (var child in node.Children)
        {
            AddCanonicalWorkspaceFolders(child, nodePath, folders);
        }
    }

    private static string BuildRelativeFolderPath(IReadOnlyList<SectionDictionaryManualNode> nodePath)
    {
        if (string.Equals(nodePath[0].SectionPath, "m1", StringComparison.OrdinalIgnoreCase))
        {
            return nodePath.Count == 1
                ? Path.Combine("m1", "us")
                : Path.Combine("m1", "us", nodePath[^1].FolderName!);
        }

        return Path.Combine(nodePath.Select(x => x.FolderName!).ToArray());
    }

    private static IEnumerable<SectionDictionaryManualNode> Flatten(SectionDictionaryManualNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}

public sealed record SectionDictionaryManualNode(
    string ElementName,
    string SectionPath,
    string Title,
    IReadOnlyCollection<SectionDictionaryManualNode> Children,
    string? FolderName);
