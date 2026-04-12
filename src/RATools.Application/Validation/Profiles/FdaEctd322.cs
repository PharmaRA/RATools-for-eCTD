namespace RATools.Application.Validation.Profiles;

public static class FdaEctd322
{
    public const string ProfileName = "fda-ectd-3.2-manual";

    public static readonly SectionDictionaryManualNode Root = new(
        ElementName: "ectd:ectd",
        SectionPath: string.Empty,
        Title: "eCTD",
        Children:
        [
            new(
                ElementName: "m1-administrative-information-and-prescribing-information",
                SectionPath: "m1",
                Title: "Module 1 Administrative Information And Prescribing Information",
                Children:
                [
                    new("m1-1-forms", "m1.1", "1.1 Forms", []),
                    new("m1-2-cover-letters", "m1.2", "1.2 Cover Letters", []),
                    new("m1-3-administrative-information", "m1.3", "1.3 Administrative Information", []),
                    new("m1-4-references", "m1.4", "1.4 References", []),
                    new("m1-5-application-status", "m1.5", "1.5 Application Status", []),
                    new("m1-6-meetings", "m1.6", "1.6 Meetings", []),
                    new("m1-7-fast-track", "m1.7", "1.7 Fast Track", []),
                    new("m1-8-special-protocol-assessment-request", "m1.8", "1.8 Special Protocol Assessment Request", []),
                    new("m1-9-pediatric-administrative-information", "m1.9", "1.9 Pediatric Administrative Information", []),
                    new("m1-10-dispute-resolution", "m1.10", "1.10 Dispute Resolution", []),
                    new("m1-11-information-amendment-information-not-covered-under-modules-2-to-5", "m1.11", "1.11 Information Amendment Information Not Covered Under Modules 2 To 5", []),
                    new("m1-12-other-correspondence", "m1.12", "1.12 Other Correspondence", []),
                    new("m1-13-annual-report", "m1.13", "1.13 Annual Report", []),
                    new("m1-14-labeling", "m1.14", "1.14 Labeling", []),
                    new("m1-15-promotional-material", "m1.15", "1.15 Promotional Material", []),
                    new("m1-16-risk-management-plan", "m1.16", "1.16 Risk Management Plan", []),
                    new("m1-17-postmarketing-studies", "m1.17", "1.17 Postmarketing Studies", []),
                    new("m1-18-proprietary-names", "m1.18", "1.18 Proprietary Names", []),
                    new("m1-19-pre-eua-and-eua", "m1.19", "1.19 Pre EUA And EUA", []),
                    new("m1-20-general-investigational-plan-for-initial-ind", "m1.20", "1.20 General Investigational Plan For Initial IND", [])
                ]),
            new(
                ElementName: "m2-common-technical-document-summaries",
                SectionPath: "m2",
                Title: "Module 2 Common Technical Document Summaries",
                Children:
                [
                    new("m2-2-introduction", "m2.2", "2.2 Introduction", []),
                    new(
                        "m2-3-quality-overall-summary",
                        "m2.3",
                        "2.3 Quality Overall Summary",
                        [
                            new("m2-3-s-drug-substance", "m2.3.s", "2.3.S Drug Substance", []),
                            new("m2-3-p-drug-product", "m2.3.p", "2.3.P Drug Product", []),
                            new("m2-3-a-appendices", "m2.3.a", "2.3.A Appendices", []),
                            new("m2-3-r-regional-information", "m2.3.r", "2.3.R Regional Information", [])
                        ]),
                    new("m2-4-nonclinical-overview", "m2.4", "2.4 Nonclinical Overview", []),
                    new("m2-5-clinical-overview", "m2.5", "2.5 Clinical Overview", []),
                    new("m2-6-nonclinical-written-and-tabulated-summaries", "m2.6", "2.6 Nonclinical Written And Tabulated Summaries", []),
                    new("m2-7-clinical-summary", "m2.7", "2.7 Clinical Summary", [])
                ]),
            new(
                ElementName: "m3-quality",
                SectionPath: "m3",
                Title: "Module 3 Quality",
                Children:
                [
                    new(
                        "m3-2-body-of-data",
                        "m3.2",
                        "3.2 Body Of Data",
                        [
                            new("m3-2-s-drug-substance", "m3.2.s", "3.2.S Drug Substance", []),
                            new("m3-2-p-drug-product", "m3.2.p", "3.2.P Drug Product", []),
                            new("m3-2-a-appendices", "m3.2.a", "3.2.A Appendices", []),
                            new("m3-2-r-regional-information", "m3.2.r", "3.2.R Regional Information", [])
                        ]),
                    new("m3-3-literature-references", "m3.3", "3.3 Literature References", [])
                ]),
            new(
                ElementName: "m4-nonclinical-study-reports",
                SectionPath: "m4",
                Title: "Module 4 Nonclinical Study Reports",
                Children:
                [
                    new("m4-2-study-reports", "m4.2", "4.2 Study Reports", []),
                    new("m4-3-literature-references", "m4.3", "4.3 Literature References", [])
                ]),
            new(
                ElementName: "m5-clinical-study-reports",
                SectionPath: "m5",
                Title: "Module 5 Clinical Study Reports",
                Children:
                [
                    new("m5-2-tabular-listing-of-all-clinical-studies", "m5.2", "5.2 Tabular Listing Of All Clinical Studies", []),
                    new(
                        "m5-3-clinical-study-reports",
                        "m5.3",
                        "5.3 Clinical Study Reports",
                        [
                            new("m5-3-1-reports-of-biopharmaceutic-studies", "m5.3.1", "5.3.1 Reports Of Biopharmaceutic Studies", []),
                            new("m5-3-2-reports-of-studies-pertinent-to-pharmacokinetics-using-human-biomaterials", "m5.3.2", "5.3.2 Reports Of Studies Pertinent To Pharmacokinetics Using Human Biomaterials", []),
                            new("m5-3-3-reports-of-human-pharmacokinetics-pk-studies", "m5.3.3", "5.3.3 Reports Of Human Pharmacokinetics PK Studies", []),
                            new("m5-3-4-reports-of-human-pharmacodynamics-pd-studies", "m5.3.4", "5.3.4 Reports Of Human Pharmacodynamics PD Studies", []),
                            new(
                                "m5-3-5-reports-of-efficacy-and-safety-studies",
                                "m5.3.5",
                                "5.3.5 Reports Of Efficacy And Safety Studies",
                                [
                                    new("m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication", "m5.3.5.1", "5.3.5.1 Controlled Clinical Studies", []),
                                    new("m5-3-5-2-study-reports-of-uncontrolled-clinical-studies", "m5.3.5.2", "5.3.5.2 Uncontrolled Clinical Studies", []),
                                    new("m5-3-5-3-reports-of-analyses-of-data-from-more-than-one-study", "m5.3.5.3", "5.3.5.3 Analyses Of Data From More Than One Study", []),
                                    new("m5-3-5-4-other-study-reports", "m5.3.5.4", "5.3.5.4 Other Study Reports", [])
                                ]),
                            new("m5-3-6-reports-of-postmarketing-experience", "m5.3.6", "5.3.6 Reports Of Postmarketing Experience", []),
                            new("m5-3-7-case-report-forms-and-individual-patient-listings", "m5.3.7", "5.3.7 Case Report Forms And Individual Patient Listings", [])
                        ]),
                    new("m5-4-literature-references", "m5.4", "5.4 Literature References", [])
                ])
        ]);

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
    IReadOnlyCollection<SectionDictionaryManualNode> Children);
