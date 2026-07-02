namespace RATools.Application.Standards;

public sealed record BackboneXmlProfile(
    BackboneXmlFileProfile Ich,
    BackboneXmlFileProfile Regional);

public sealed record BackboneXmlFileProfile(
    string DocumentTypeName,
    string RootElementName,
    string Namespace,
    string DtdVersion,
    string DtdSystemId,
    string? RelativePath = null);

public static class BackboneXmlProfiles
{
    public static BackboneXmlProfile FdaEctd322UsRegional33 { get; } = new(
        new BackboneXmlFileProfile(
            "ectd:ectd",
            "ectd",
            "http://www.ich.org/ectd",
            "3.2",
            "util/dtd/ich-ectd-3-2.dtd"),
        new BackboneXmlFileProfile(
            "fda-regional:fda-regional",
            "fda-regional",
            "http://www.ich.org/fda",
            "3.3",
            "../../util/dtd/us-regional-v3-3.dtd",
            "m1/us/us-regional.xml"));

    public static BackboneXmlProfile EuEctd322Regional { get; } = new(
        new BackboneXmlFileProfile(
            "ectd:ectd",
            "ectd",
            "http://www.ich.org/ectd",
            "3.2",
            "util/dtd/ich-ectd-3-2.dtd"),
        new BackboneXmlFileProfile(
            "eu-regional:eu-regional",
            "eu-regional",
            "http://www.ema.europa.eu/eu-ectd",
            "EU M1",
            "../../util/dtd/eu-regional.dtd",
            "m1/eu/eu-regional.xml"));
}
