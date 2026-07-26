using System.Xml.Linq;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;

namespace RATools.Tests.Publishing;

public sealed class BackboneServiceTests
{
    [Fact]
    public async Task GenerateAsync_BuildsPackageGeneratesBothXmlFilesAndWritesPackage()
    {
        var applicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var publishJobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var package = CreatePackage(applicationId);
        var packageBuilder = new RecordingPackageModelBuilder(package);
        var ichWriter = new RecordingIchIndexXmlWriter();
        var usRegionalWriter = new RecordingUsRegionalXmlWriter();
        var regionalWriterRegistry = new RegionalBackboneWriterRegistry([new UsRegionalBackboneWriter(usRegionalWriter)]);
        var validator = new RecordingEctdXmlValidator();
        var fileWriter = new RecordingBackboneFileWriter(validator);
        var standardsProfileProvider = new FdaEctd322StandardsProfileProvider();
        var service = new BackboneService(packageBuilder, ichWriter, regionalWriterRegistry, validator, standardsProfileProvider, fileWriter);

        var result = await service.GenerateAsync(new GenerateBackboneRequest(
            applicationId,
            "0001",
            publishJobId,
            "C:/publish-root",
            "publish-report-0001.json",
            "0001.zip"));

        Assert.Equal(new BuildEctdPackageRequest(applicationId, "0001"), packageBuilder.Request);
        Assert.Same(package, ichWriter.Package);
        Assert.Same(package, usRegionalWriter.Package);
        Assert.Equal("ANDA123456", fileWriter.ApplicationNumber);
        Assert.Equal("0001", fileWriter.SequenceNumber);
        Assert.Equal(publishJobId, fileWriter.PublishJobId);
        Assert.Equal("C:/publish-root", fileWriter.OutputDirectoryPath);
        Assert.Equal("publish-report-0001.json", fileWriter.ReportFileName);
        Assert.Equal("0001.zip", fileWriter.PackageFileName);
        Assert.Contains(fileWriter.GeneratedFiles, x => x.RelativePath == "index.xml" && x.Content == "<ich />");
        Assert.Contains(fileWriter.GeneratedFiles, x => x.RelativePath == "m1/us/us-regional.xml" && x.Content == "<regional />");
        Assert.Equal(["index.xml", "m1/us/us-regional.xml"], validator.ValidatedRelativePaths);
        Assert.Equal(2, validator.ValidatedBeforeWrite.Count(x => x));
        // 每次校验都必须携带 standards profile：不传时 DTD 白名单回退到 ICH/US 静态列表，
        // EU 发布会在此处失败（readiness 与 publish 行为分歧的根因）。
        Assert.All(validator.ValidatedProfiles, Assert.NotNull);
        Assert.Same(package.PublishedFiles, fileWriter.PublishedFiles);
        Assert.Equal(applicationId, result.ApplicationId);
        Assert.Equal("0001", result.SequenceNumber);
        Assert.Equal("index.xml", result.FileName);
        Assert.Equal("C:/out/index.xml", result.FilePath);
        Assert.Equal("C:/out/report.json", result.ReportPath);
        Assert.Equal("C:/out/package.zip", result.PackagePath);
        Assert.Equal("<ich />", result.XmlContent);
    }

    private static EctdSequencePackage CreatePackage(Guid applicationId)
    {
        var publishedFiles = new[]
        {
            new EctdPublishedFile(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "C:/source/m3/32-body-of-data/quality.pdf",
                "m3/32-body-of-data/quality.pdf",
                "quality.pdf",
                123,
                "sha-quality",
                "md5-quality")
        };

        return new EctdSequencePackage(
            applicationId,
            "ANDA123456",
            "0001",
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "3.2.2",
            "3.3",
            BackboneXmlProfiles.FdaEctd322UsRegional33,
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-3.2.2", "anda"),
            new EctdSequenceMetadata("0001", "original-application", "initial", "Initial sequence", "Acme Pharma", "356h"),
            new EctdUsRegionalMetadata(
                "ANDA123456",
                "Acme Pharma",
                "Initial sequence",
                "Jane Regulatory",
                "regulatory",
                "301-555-0100",
                "office",
                "jane.regulatory@example.test",
                "anda",
                "original-application",
                "initial",
                "356h"),
            [],
            [],
            publishedFiles);
    }

    private sealed class RecordingPackageModelBuilder(EctdSequencePackage package) : IEctdPackageModelBuilder
    {
        public BuildEctdPackageRequest? Request { get; private set; }

        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(package);
        }
    }

    private sealed class RecordingIchIndexXmlWriter : IIchIndexXmlWriter
    {
        public EctdSequencePackage? Package { get; private set; }

        public IchIndexXmlWriteResult Write(EctdSequencePackage package)
        {
            Package = package;
            return new IchIndexXmlWriteResult("index.xml", new XDocument(new XElement("ich")), "<ich />");
        }
    }

    private sealed class RecordingUsRegionalXmlWriter : IUsRegionalXmlWriter
    {
        public EctdSequencePackage? Package { get; private set; }

        public UsRegionalXmlWriteResult Write(EctdSequencePackage package)
        {
            Package = package;
            return new UsRegionalXmlWriteResult(
                "us-regional.xml",
                "m1/us/us-regional.xml",
                new XDocument(new XElement("regional")),
                "<regional />");
        }
    }

    private sealed class RecordingBackboneFileWriter : IBackboneFileWriter
    {
        private readonly RecordingEctdXmlValidator? _validator;

        public RecordingBackboneFileWriter()
        {
        }

        public RecordingBackboneFileWriter(RecordingEctdXmlValidator validator)
        {
            _validator = validator;
        }

        public string? ApplicationNumber { get; private set; }

        public string? SequenceNumber { get; private set; }

        public Guid PublishJobId { get; private set; }

        public string? OutputDirectoryPath { get; private set; }

        public string? ReportFileName { get; private set; }

        public string? PackageFileName { get; private set; }

        public IReadOnlyCollection<BackboneGeneratedFile> GeneratedFiles { get; private set; } = [];

        public IReadOnlyCollection<EctdPublishedFile> PublishedFiles { get; private set; } = [];

        public Task<(string FilePath, string ReportPath, string PackagePath)> SaveAsync(
            string applicationNumber,
            string sequenceNumber,
            Guid publishJobId,
            string outputDirectoryPath,
            IReadOnlyCollection<BackboneGeneratedFile> generatedFiles,
            string reportFileName,
            string packageFileName,
            IReadOnlyCollection<EctdPublishedFile> publishedFiles,
            CancellationToken cancellationToken = default)
        {
            if (_validator is not null)
            {
                _validator.FileWriterWasInvoked = true;
            }

            ApplicationNumber = applicationNumber;
            SequenceNumber = sequenceNumber;
            PublishJobId = publishJobId;
            OutputDirectoryPath = outputDirectoryPath;
            ReportFileName = reportFileName;
            PackageFileName = packageFileName;
            GeneratedFiles = generatedFiles;
            PublishedFiles = publishedFiles;
            return Task.FromResult(("C:/out/index.xml", "C:/out/report.json", "C:/out/package.zip"));
        }
    }

    private sealed class RecordingEctdXmlValidator : IEctdXmlValidator
    {
        public bool FileWriterWasInvoked { get; set; }

        public List<string> ValidatedRelativePaths { get; } = [];

        public List<bool> ValidatedBeforeWrite { get; } = [];

        public List<StandardsProfile?> ValidatedProfiles { get; } = [];

        public void Validate(BackboneGeneratedFile file, StandardsProfile? standardsProfile = null)
        {
            ValidatedRelativePaths.Add(file.RelativePath);
            ValidatedBeforeWrite.Add(!FileWriterWasInvoked);
            ValidatedProfiles.Add(standardsProfile);
        }
    }
}
