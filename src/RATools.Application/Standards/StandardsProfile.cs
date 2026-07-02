namespace RATools.Application.Standards;

public sealed record StandardsProfile(
    string TemplateKey,
    string DisplayName,
    string RegulatoryAgency,
    string Region,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    string TechnicalConformanceGuideVersion,
    string ValidationCriteriaVersion,
    IReadOnlyCollection<string> OfficialReferences,
    IReadOnlyCollection<StandardsAsset> Assets,
    BackboneXmlProfile? BackboneXml = null);
