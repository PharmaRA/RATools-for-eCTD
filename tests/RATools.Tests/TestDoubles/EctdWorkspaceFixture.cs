using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Documents;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.TestDoubles;

internal sealed class EctdWorkspaceFixture : IDisposable
{
    private readonly IStandardsProfileProvider standards;

    public EctdWorkspaceFixture(string templateKey)
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"ectd-workspace-{Guid.NewGuid():N}");
        var workspace = Path.Combine(RootPath, "application");
        Directory.CreateDirectory(workspace);
        var isEu = templateKey.StartsWith("eu-", StringComparison.Ordinal);
        Application = new SubmissionApplication("IND000001", isEu ? "EU" : "US", "Test sponsor", workspace, templateKey);
        standards = isEu ? new EuEctd322StandardsProfileProvider() : new FdaEctd322StandardsProfileProvider();
        Profile = standards.GetProfile(templateKey);
        PathPolicy = new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [RootPath] }));
    }

    public string RootPath { get; }
    public SubmissionApplication Application { get; }
    public InMemoryApplicationRepository Applications { get; } = new();
    public InMemoryDocumentRepository Documents { get; } = new();
    public InMemoryDocumentPlacementRepository Placements { get; } = new();
    public ConfiguredWorkspacePathPolicy PathPolicy { get; }
    public StandardsProfile Profile { get; }

    public async Task AddSequenceAsync(string number)
    {
        var sequence = Application.CreateSequence(number, "original-application", "Test sequence");
        sequence.RevisePublishingMetadata(SequencePublishingMetadata.Create(
            "ind", "original-application", "initial", "Test sequence", "Test sponsor", null,
            "Test contact", "regulatory", "301-555-0100", "office", "test@example.test"));
        Directory.CreateDirectory(Path.Combine(Application.WorkingDirectoryPath, number));
        if (await Applications.GetAsync(Application.Id) is null)
        {
            await Applications.AddAsync(Application);
        }
        else
        {
            await Applications.UpdateAsync(Application);
        }
    }

    public async Task<(SubmissionDocument Document, DocumentPlacement Placement)> AddDocumentAsync(
        string sequence,
        string section,
        string fileName,
        string content,
        DocumentPlacementOperation operation = DocumentPlacementOperation.New,
        Guid? targetPlacementId = null)
    {
        var folder = new EctdWorkspacePathResolver().Resolve(Application.EctdTemplateKey, section);
        var directory = Path.Combine(Application.WorkingDirectoryPath, sequence, folder.RelativeFolderPath);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var bytes = Encoding.UTF8.GetBytes(content);
        await File.WriteAllBytesAsync(path, bytes);
        var document = new SubmissionDocument(fileName, "text/plain", bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
        var placement = new DocumentPlacement(document.Id, Application.Id, sequence, section, operation, fileName);
        placement.ReviseLifecycleTarget(targetPlacementId);
        await Documents.AddAsync(document);
        await Placements.AddAsync(placement);
        return (document, placement);
    }

    public Task<EctdSequencePackage> BuildAsync(string sequence)
        => new EctdPackageModelBuilder(Applications, Placements, Documents, standards, new DocumentStorageBoundary(PathPolicy))
            .BuildAsync(new BuildEctdPackageRequest(Application.Id, sequence));

    public async Task<EctdSequencePackage> WriteSequenceAsync(string sequence)
    {
        var package = await BuildAsync(sequence);
        var ich = new IchIndexXmlWriter().Write(package);
        var files = new List<BackboneGeneratedFile> { new(ich.FileName, ich.XmlContent) };
        if (Application.Region == "EU")
        {
            var regional = new EuRegionalXmlWriter().Write(package);
            files.Add(new BackboneGeneratedFile(regional.RelativePath, regional.XmlContent));
        }
        else
        {
            var regional = new UsRegionalXmlWriter().Write(package);
            files.Add(new BackboneGeneratedFile(regional.RelativePath, regional.XmlContent));
        }

        foreach (var file in files)
        {
            new EctdXmlValidator().Validate(file, Profile);
            var path = Path.Combine(Application.WorkingDirectoryPath, sequence, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content);
        }

        return package;
    }

    public static XDocument ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        return XDocument.Load(reader);
    }

    public void Dispose()
    {
        Directory.Delete(RootPath, recursive: true);
    }
}
