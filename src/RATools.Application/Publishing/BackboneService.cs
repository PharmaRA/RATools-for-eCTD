using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Requests;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Application.Publishing;

public sealed class BackboneService(
    IEctdPackageModelBuilder packageModelBuilder,
    IIchIndexXmlWriter ichIndexXmlWriter,
    IUsRegionalXmlWriter usRegionalXmlWriter,
    IBackboneFileWriter backboneFileWriter) : IBackboneService
{
    public async Task<GeneratedBackboneDto> GenerateAsync(GenerateBackboneRequest request, CancellationToken cancellationToken = default)
    {
        var package = await packageModelBuilder.BuildAsync(
            new BuildEctdPackageRequest(request.ApplicationId, request.SequenceNumber),
            cancellationToken);
        var indexXml = ichIndexXmlWriter.Write(package);
        var usRegionalXml = usRegionalXmlWriter.Write(package);
        var output = await backboneFileWriter.SaveAsync(
            package.ApplicationNumber,
            package.SequenceNumber,
            request.PublishJobId,
            request.OutputDirectoryPath,
            [
                new BackboneGeneratedFile(indexXml.FileName, indexXml.XmlContent),
                new BackboneGeneratedFile(usRegionalXml.RelativePath, usRegionalXml.XmlContent)
            ],
            request.ReportFileName,
            request.PackageFileName,
            "{}",
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
