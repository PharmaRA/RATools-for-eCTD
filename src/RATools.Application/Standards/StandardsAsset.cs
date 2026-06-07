namespace RATools.Application.Standards;

public sealed record StandardsAsset(
    string Key,
    string DisplayName,
    string Category,
    string Version,
    string LocalRelativePath,
    string SourceUrl,
    DateOnly? SupportedFrom,
    string Sha256);
