using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

internal sealed record PublishArtifactDescriptor(
    string Name,
    string Type,
    string ContentType,
    Func<PublishJob, string?> ResolvePath);

internal static class PublishArtifactDescriptorCatalog
{
    public static IReadOnlyCollection<PublishArtifactDescriptor> All { get; } =
    [
        new(
            "BackboneXml",
            "file",
            "application/xml",
            job => job.OutputPath),
        new(
            "PublishReport",
            "file",
            "application/json",
            job => job.OutputPath is null
                ? null
                : PublishOutputNaming.BuildPublishReportPath(job.OutputPath, job.SequenceNumber, job.Id)),
        new(
            "PackageZip",
            "file",
            "application/zip",
            job => job.PackagePath),
    ];

    public static PublishArtifactDescriptor? Find(string artifactName)
        => All.SingleOrDefault(x => string.Equals(x.Name, artifactName, StringComparison.OrdinalIgnoreCase));
}
