namespace RATools.Application.Publishing.UsRegional;

public static class UsRegionalM1V33
{
    public static readonly UsRegionalSectionNode Root = Branch(
        "m1-regional",
        "m1",
        [
            Branch("m1-1-forms", "m1.1"),
            Leaf("m1-2-cover-letters", "m1.2"),
            Branch("m1-3-administrative-information", "m1.3",
            [
                Branch("m1-3-1-contact-sponsor-applicant-information", "m1.3.1",
                [
                    Leaf("m1-3-1-1-change-of-address-or-corporate-name", "m1.3.1.1"),
                    Leaf("m1-3-1-2-change-in-contact-agent", "m1.3.1.2"),
                    Leaf("m1-3-1-3-change-in-sponsor", "m1.3.1.3"),
                    Leaf("m1-3-1-4-transfer-of-obligation", "m1.3.1.4"),
                    Leaf("m1-3-1-5-change-in-ownership-of-an-application-or-reissuance-of-license", "m1.3.1.5")
                ]),
                Leaf("m1-3-2-field-copy-certification", "m1.3.2"),
                Leaf("m1-3-3-debarment-certification", "m1.3.3"),
                Leaf("m1-3-4-financial-certification-and-disclosure", "m1.3.4"),
                Branch("m1-3-5-patent-and-exclusivity", "m1.3.5",
                [
                    Leaf("m1-3-5-1-patent-information", "m1.3.5.1"),
                    Leaf("m1-3-5-2-patent-certification", "m1.3.5.2"),
                    Leaf("m1-3-5-3-exclusivity-claim", "m1.3.5.3")
                ]),
                Leaf("m1-3-6-tropical-disease-priority-review-voucher", "m1.3.6")
            ]),
            Branch("m1-4-references", "m1.4",
            [
                Leaf("m1-4-1-letter-of-authorization", "m1.4.1"),
                Leaf("m1-4-2-statement-of-right-of-reference", "m1.4.2"),
                Leaf("m1-4-3-list-of-authorized-persons-to-incorporate-by-reference", "m1.4.3"),
                Leaf("m1-4-4-cross-reference-to-previously-submitted-information", "m1.4.4")
            ]),
            Branch("m1-5-application-status", "m1.5",
            [
                Leaf("m1-5-1-withdrawal-of-an-ind", "m1.5.1"),
                Leaf("m1-5-2-inactivation-request", "m1.5.2"),
                Leaf("m1-5-3-reactivation-request", "m1.5.3"),
                Leaf("m1-5-4-reinstatement-request", "m1.5.4"),
                Leaf("m1-5-5-withdrawal-of-an-unapproved-bla-nda-anda-or-supplement", "m1.5.5"),
                Leaf("m1-5-6-withdrawal-of-listed-drug", "m1.5.6"),
                Leaf("m1-5-7-withdrawal-of-approval-of-an-application-or-revocation-of-license", "m1.5.7")
            ]),
            Branch("m1-6-meetings", "m1.6",
            [
                Leaf("m1-6-1-meeting-request", "m1.6.1"),
                Leaf("m1-6-2-meeting-background-materials", "m1.6.2"),
                Leaf("m1-6-3-correspondence-regarding-meetings", "m1.6.3")
            ]),
            Branch("m1-7-fast-track", "m1.7",
            [
                Leaf("m1-7-1-fast-track-designation-request", "m1.7.1"),
                Leaf("m1-7-2-fast-track-designation-withdrawal-request", "m1.7.2"),
                Leaf("m1-7-3-rolling-review-request", "m1.7.3"),
                Leaf("m1-7-4-correspondence-regarding-fast-track-rolling-review", "m1.7.4")
            ]),
            Branch("m1-8-special-protocol-assessment-request", "m1.8",
            [
                Leaf("m1-8-1-clinical-study", "m1.8.1"),
                Leaf("m1-8-2-carcinogenicity-study", "m1.8.2"),
                Leaf("m1-8-3-stability-study", "m1.8.3"),
                Leaf("m1-8-4-animal-efficacy-study-for-approval-under-the-animal-rule", "m1.8.4")
            ]),
            Branch("m1-9-pediatric-administrative-information", "m1.9",
            [
                Leaf("m1-9-1-request-for-waiver-of-pediatric-studies", "m1.9.1"),
                Leaf("m1-9-2-request-for-deferral-of-pediatric-studies", "m1.9.2"),
                Leaf("m1-9-3-request-for-pediatric-exclusivity-determination", "m1.9.3"),
                Leaf("m1-9-4-proposed-pediatric-study-request-and-amendments", "m1.9.4"),
                Leaf("m1-9-6-other-correspondence-regarding-pediatric-exclusivity-or-study-plans", "m1.9.6")
            ]),
            Branch("m1-10-dispute-resolution", "m1.10",
            [
                Leaf("m1-10-1-request-for-dispute-resolution", "m1.10.1"),
                Leaf("m1-10-2-correspondence-related-to-dispute-resolution", "m1.10.2")
            ]),
            Branch("m1-11-information-amendment-information-not-covered-under-modules-2-to-5", "m1.11",
            [
                Leaf("m1-11-1-quality-information-amendment", "m1.11.1"),
                Leaf("m1-11-2-nonclinical-information-amendment", "m1.11.2"),
                Leaf("m1-11-3-clinical-information-amendment", "m1.11.3"),
                Leaf("m1-11-4-multiple-module-information-amendment", "m1.11.4")
            ]),
            Branch("m1-12-other-correspondence", "m1.12",
            [
                Leaf("m1-12-1-pre-ind-correspondence", "m1.12.1"),
                Leaf("m1-12-2-request-to-charge-for-clinical-trial", "m1.12.2"),
                Leaf("m1-12-3-request-to-charge-for-expanded-access", "m1.12.3"),
                Leaf("m1-12-4-request-for-comments-and-advice", "m1.12.4"),
                Leaf("m1-12-5-request-for-a-waiver", "m1.12.5"),
                Leaf("m1-12-6-exception-from-informed-consent-for-emergency-research", "m1.12.6"),
                Leaf("m1-12-7-public-disclosure-statement-for-exception-from-informed-consent-for-emergency-research", "m1.12.7"),
                Leaf("m1-12-8-correspondence-regarding-exception-from-informed-consent-for-emergency-research", "m1.12.8"),
                Leaf("m1-12-9-notification-of-discontinuation-of-clinical-trial", "m1.12.9"),
                Leaf("m1-12-10-generic-drug-enforcement-act-statement", "m1.12.10"),
                Leaf("m1-12-11-anda-basis-for-submission-statement", "m1.12.11"),
                Leaf("m1-12-12-comparison-of-generic-drug-and-reference-listed-drug", "m1.12.12"),
                Leaf("m1-12-13-request-for-waiver-for-in-vivo-studies", "m1.12.13"),
                Leaf("m1-12-14-environmental-analysis", "m1.12.14"),
                Leaf("m1-12-15-request-for-waiver-of-in-vivo-bioavailability-studies", "m1.12.15"),
                Leaf("m1-12-16-field-alert-reports", "m1.12.16"),
                Leaf("m1-12-17-orphan-drug-designation", "m1.12.17")
            ]),
            Branch("m1-13-annual-report", "m1.13",
            [
                Leaf("m1-13-1-summary-for-nonclinical-studies", "m1.13.1"),
                Leaf("m1-13-2-summary-of-clinical-pharmacology-information", "m1.13.2"),
                Leaf("m1-13-3-summary-of-safety-information", "m1.13.3"),
                Leaf("m1-13-4-summary-of-labeling-changes", "m1.13.4"),
                Leaf("m1-13-5-summary-of-manufacturing-changes", "m1.13.5"),
                Leaf("m1-13-6-summary-of-microbiological-changes", "m1.13.6"),
                Leaf("m1-13-7-summary-of-other-significant-new-information", "m1.13.7"),
                Leaf("m1-13-8-individual-study-information", "m1.13.8"),
                Leaf("m1-13-9-general-investigational-plan", "m1.13.9"),
                Leaf("m1-13-10-foreign-marketing", "m1.13.10"),
                Leaf("m1-13-11-distribution-data", "m1.13.11"),
                Leaf("m1-13-12-status-of-postmarketing-study-commitments-and-requirements", "m1.13.12"),
                Leaf("m1-13-13-status-of-other-postmarketing-studies-and-requirements", "m1.13.13"),
                Leaf("m1-13-14-log-of-outstanding-regulatory-business", "m1.13.14"),
                Leaf("m1-13-15-development-safety-update-report-dsur", "m1.13.15")
            ]),
            Branch("m1-14-labeling", "m1.14",
            [
                Branch("m1-14-1-draft-labeling", "m1.14.1",
                [
                    Leaf("m1-14-1-1-draft-carton-and-container-labels", "m1.14.1.1"),
                    Leaf("m1-14-1-2-annotated-draft-labeling-text", "m1.14.1.2"),
                    Leaf("m1-14-1-3-draft-labeling-text", "m1.14.1.3"),
                    Leaf("m1-14-1-4-label-comprehension-studies", "m1.14.1.4"),
                    Leaf("m1-14-1-5-labeling-history", "m1.14.1.5")
                ]),
                Branch("m1-14-2-final-labeling", "m1.14.2",
                [
                    Leaf("m1-14-2-1-final-carton-or-container-labels", "m1.14.2.1"),
                    Leaf("m1-14-2-2-final-package-insert-package-inserts-patient-information-medication-guides", "m1.14.2.2"),
                    Leaf("m1-14-2-3-final-labeling-text", "m1.14.2.3")
                ]),
                Branch("m1-14-3-listed-drug-labeling", "m1.14.3",
                [
                    Leaf("m1-14-3-1-annotated-comparison-with-listed-drug", "m1.14.3.1"),
                    Leaf("m1-14-3-2-approved-labeling-text-for-listed-drug", "m1.14.3.2"),
                    Leaf("m1-14-3-3-labeling-text-for-reference-listed-drug", "m1.14.3.3")
                ]),
                Branch("m1-14-4-investigational-drug-labeling", "m1.14.4",
                [
                    Leaf("m1-14-4-1-investigational-brochure", "m1.14.4.1"),
                    Leaf("m1-14-4-2-investigational-drug-labeling", "m1.14.4.2")
                ]),
                Leaf("m1-14-5-foreign-labeling", "m1.14.5"),
                Leaf("m1-14-6-product-labeling-for-2253-submissions", "m1.14.6")
            ]),
            UnsupportedBranch("m1-15-promotional-material", "m1.15",
            [
                UnsupportedBranch("m1-15-1-correspondence-relating-to-promotional-materials", "m1.15.1",
                [
                    UnsupportedLeaf("m1-15-1-1-request-for-advisory-comments-on-launch-materials", "m1.15.1.1"),
                    UnsupportedLeaf("m1-15-1-2-request-for-advisory-comments-on-non-launch-materials", "m1.15.1.2"),
                    UnsupportedLeaf("m1-15-1-3-pre-submission-of-launch-promotional-materials-for-accelerated-approval-products", "m1.15.1.3"),
                    UnsupportedLeaf("m1-15-1-4-pre-submission-of-non-launch-promotional-materials-for-accelerated-approval-products", "m1.15.1.4"),
                    UnsupportedLeaf("m1-15-1-5-pre-dissemination-review-of-television-ads", "m1.15.1.5"),
                    UnsupportedLeaf("m1-15-1-6-response-to-untitled-letter-or-warning-letter", "m1.15.1.6"),
                    UnsupportedLeaf("m1-15-1-7-response-to-information-request", "m1.15.1.7"),
                    UnsupportedLeaf("m1-15-1-8-correspondence-accompanying-materials-previously-missing-or-rejected", "m1.15.1.8"),
                    UnsupportedLeaf("m1-15-1-9-withdrawal-request", "m1.15.1.9"),
                    UnsupportedLeaf("m1-15-1-10-submission-of-annotated-references", "m1.15.1.10"),
                    UnsupportedLeaf("m1-15-1-11-general-correspondence", "m1.15.1.11")
                ]),
                UnsupportedBranch("m1-15-2-materials", "m1.15.2",
                [
                    UnsupportedBranch("m1-15-2-1-material", "m1.15.2.1",
                    [
                        UnsupportedLeaf("m1-15-2-1-1-clean-version", "m1.15.2.1.1"),
                        UnsupportedLeaf("m1-15-2-1-2-annotated-version", "m1.15.2.1.2"),
                        UnsupportedLeaf("m1-15-2-1-3-annotated-labeling-version", "m1.15.2.1.3"),
                        UnsupportedLeaf("m1-15-2-1-4-annotated-references", "m1.15.2.1.4")
                    ])
                ])
            ]),
            Branch("m1-16-risk-management-plan", "m1.16",
            [
                Leaf("m1-16-1-risk-management-non-rems", "m1.16.1"),
                Branch("m1-16-2-risk-evaluation-and-mitigation-strategies-rems", "m1.16.2",
                [
                    Leaf("m1-16-2-1-final-rems", "m1.16.2.1"),
                    Leaf("m1-16-2-2-draft-rems", "m1.16.2.2"),
                    Leaf("m1-16-2-3-rems-assessment", "m1.16.2.3"),
                    Leaf("m1-16-2-4-rems-assessment-methodology", "m1.16.2.4"),
                    Leaf("m1-16-2-5-rems-correspondence", "m1.16.2.5"),
                    Leaf("m1-16-2-6-rems-modification-history", "m1.16.2.6")
                ])
            ]),
            Branch("m1-17-postmarketing-studies", "m1.17",
            [
                Leaf("m1-17-1-correspondence-regarding-postmarketing-commitments", "m1.17.1"),
                Leaf("m1-17-2-correspondence-regarding-postmarketing-requirements", "m1.17.2")
            ]),
            Leaf("m1-18-proprietary-names", "m1.18"),
            Leaf("m1-19-pre-eua-and-eua", "m1.19"),
            Leaf("m1-20-general-investigational-plan-for-initial-ind", "m1.20")
        ]);

    private static readonly IReadOnlyDictionary<string, UsRegionalSectionNode> BySectionPath = Flatten()
        .ToDictionary(x => x.SectionPath, x => x, StringComparer.OrdinalIgnoreCase);

    public static bool TryFind(string sectionPath, out UsRegionalSectionNode? node)
        => BySectionPath.TryGetValue(sectionPath, out node);

    public static IEnumerable<UsRegionalSectionNode> Flatten()
        => Flatten(Root);

    private static IEnumerable<UsRegionalSectionNode> Flatten(UsRegionalSectionNode node)
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

    private static UsRegionalSectionNode Branch(
        string elementName,
        string sectionPath,
        IReadOnlyCollection<UsRegionalSectionNode>? children = null)
        => new(elementName, sectionPath, AcceptsLeaves: false, RequiresUnsupportedAttributes: false, children ?? []);

    private static UsRegionalSectionNode Leaf(string elementName, string sectionPath)
        => new(elementName, sectionPath, AcceptsLeaves: true, RequiresUnsupportedAttributes: false, []);

    private static UsRegionalSectionNode UnsupportedBranch(
        string elementName,
        string sectionPath,
        IReadOnlyCollection<UsRegionalSectionNode>? children = null)
        => new(elementName, sectionPath, AcceptsLeaves: false, RequiresUnsupportedAttributes: true, children ?? []);

    private static UsRegionalSectionNode UnsupportedLeaf(string elementName, string sectionPath)
        => new(elementName, sectionPath, AcceptsLeaves: true, RequiresUnsupportedAttributes: true, []);
}

public sealed record UsRegionalSectionNode(
    string ElementName,
    string SectionPath,
    bool AcceptsLeaves,
    bool RequiresUnsupportedAttributes,
    IReadOnlyCollection<UsRegionalSectionNode> Children);
