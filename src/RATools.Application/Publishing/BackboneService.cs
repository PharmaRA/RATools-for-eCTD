using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;

namespace RATools.Application.Publishing;

public sealed class BackboneService(
    IEctdPackageModelBuilder packageModelBuilder,
    IIchIndexXmlWriter ichIndexXmlWriter,
    IUsRegionalXmlWriter usRegionalXmlWriter,
    IEctdXmlValidator ectdXmlValidator,
    IBackboneFileWriter backboneFileWriter) : IBackboneService
{
    public async Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
    {
        var package = await packageModelBuilder.BuildAsync(
            new BuildEctdPackageRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);
        var indexXml = ichIndexXmlWriter.Write(package);
        var usRegionalXml = usRegionalXmlWriter.Write(package);
        BackboneGeneratedFile[] generatedFiles =
        [
            new BackboneGeneratedFile(indexXml.FileName, indexXml.XmlContent),
            new BackboneGeneratedFile(usRegionalXml.RelativePath, usRegionalXml.XmlContent)
        ];

        foreach (var generatedFile in generatedFiles)
        {
            ectdXmlValidator.Validate(generatedFile);
        }

        var output = await backboneFileWriter.SaveAsync(
            package.ApplicationNumber,
            package.SequenceNumber,
            request.PublishJobId,
            request.OutputDirectoryPath,
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
