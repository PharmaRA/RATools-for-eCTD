using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;

namespace RATools.Application.Publishing;

public sealed class BackboneService(
    IEctdPackageModelBuilder packageModelBuilder,
    IIchIndexXmlWriter ichIndexXmlWriter,
    IRegionalBackboneWriterRegistry regionalBackboneWriterRegistry,
    IEctdXmlValidator ectdXmlValidator,
    IStandardsProfileProvider standardsProfileProvider,
    IBackboneFileWriter backboneFileWriter) : IBackboneService
{
    public async Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
    {
        var package = await packageModelBuilder.BuildAsync(
            new BuildEctdPackageRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);
        // 必须传 standards profile：不传时校验器回退到只含 ICH/US 的静态 DTD 白名单，
        // EU 的 eu-regional.dtd 会被拒 —— readiness（传 profile）绿、publish 红的分歧即源于此。
        var profile = standardsProfileProvider.GetProfile(package.Application.TemplateKey);
        var indexXml = ichIndexXmlWriter.Write(package);
        var regionalBackboneWriter = regionalBackboneWriterRegistry.Resolve(package.Application.Region);
        var regionalFiles = regionalBackboneWriter.WriteRegionalBackbones(package);
        BackboneGeneratedFile[] generatedFiles =
        [
            new BackboneGeneratedFile(indexXml.FileName, indexXml.XmlContent),
            .. regionalFiles
        ];

        foreach (var generatedFile in generatedFiles)
        {
            ectdXmlValidator.Validate(generatedFile, profile);
        }

        var output = await backboneFileWriter.SaveAsync(
            package.ApplicationId,
            package.SequenceNumber,
            request.PublishJobId,
            generatedFiles,
            request.ReportFileName,
            request.PackageFileName,
            package.PublishedFiles,
            cancellationToken);

        return new GeneratedBackboneDto(
            request.ApplicationId,
            request.SequenceNumber,
            indexXml.FileName,
            output.FilePath,
            output.ReportPath,
            output.PackagePath,
            indexXml.XmlContent);
    }
}
