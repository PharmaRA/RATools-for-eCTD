using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;

namespace RATools.Application.Validation;

public sealed class PublishReadinessService(
    ISequenceValidationService validationService,
    IEctdPackageModelBuilder packageModelBuilder,
    IIchIndexXmlWriter ichIndexXmlWriter,
    IUsRegionalXmlWriter usRegionalXmlWriter,
    IEctdXmlValidator ectdXmlValidator) : IPublishReadinessService
{
    public async Task<PublishReadinessReportDto> GetAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
    {
        var validationReport = await validationService.ValidateAsync(request, cancellationToken);
        return await GetAsync(request, validationReport, cancellationToken);
    }

    public async Task<PublishReadinessReportDto> GetAsync(
        ValidateSequenceRequest request,
        ValidationReportDto validationReport,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<PublishReadinessFindingDto>();

        findings.AddRange(validationReport.Issues.Select(issue => new PublishReadinessFindingDto(
            "Validation",
            issue.Severity,
            issue.Code,
            issue.Message,
            null,
            issue.SectionPath,
            issue.DocumentId,
            issue.PlacementId)));

        if (validationReport.IsValid)
        {
            try
            {
                var package = await packageModelBuilder.BuildAsync(
                    new BuildEctdPackageRequest(request.ApplicationId, request.SequenceNumber),
                    cancellationToken);

                var ichResult = ichIndexXmlWriter.Write(package);
                ectdXmlValidator.Validate(new BackboneGeneratedFile(ichResult.FileName, ichResult.XmlContent));

                var regionalResult = usRegionalXmlWriter.Write(package);
                ectdXmlValidator.Validate(new BackboneGeneratedFile(regionalResult.RelativePath, regionalResult.XmlContent));
            }
            catch (UsRegionalXmlMetadataException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "US_REGIONAL_METADATA_MISSING",
                    exception.Message,
                    exception.FieldName));
            }
            catch (UsRegionalXmlSectionMappingException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "US_REGIONAL_SECTION_UNSUPPORTED",
                    exception.Message,
                    null,
                    exception.CtdSection,
                    null,
                    exception.PlacementId));
            }
            catch (IchIndexXmlSectionMappingException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ICH_SECTION_UNSUPPORTED",
                    exception.Message,
                    null,
                    exception.CtdSection,
                    null,
                    exception.PlacementId));
            }
            catch (EctdXmlValidationException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_XML_DTD_VALIDATION_FAILED",
                    exception.Message));
            }
            catch (EctdPackageDocumentNotFoundException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_PACKAGE_DOCUMENT_NOT_FOUND",
                    exception.Message,
                    null,
                    null,
                    exception.DocumentId,
                    exception.PlacementId));
            }
            catch (EctdPackageLifecycleTargetException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_PACKAGE_LIFECYCLE_TARGET_INVALID",
                    exception.Message,
                    null,
                    null,
                    null,
                    exception.PlacementId));
            }
            catch (EctdPackageInvalidSectionException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_PACKAGE_INVALID_SECTION",
                    exception.Message,
                    null,
                    exception.CtdSection,
                    null,
                    exception.PlacementId));
            }
            catch (EctdPackageUnsupportedOperationException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_PACKAGE_UNSUPPORTED_OPERATION",
                    exception.Message,
                    null,
                    null,
                    null,
                    exception.PlacementId));
            }
            catch (EctdPackageException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_PACKAGE_BUILD_FAILED",
                    exception.Message));
            }
        }

        var blockingErrorCount = findings.Count(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var warningCount = findings.Count(x => string.Equals(x.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        var isReady = blockingErrorCount == 0;

        return new PublishReadinessReportDto(
            request.ApplicationId,
            request.SequenceNumber,
            isReady,
            isReady ? "Ready" : "Blocked",
            blockingErrorCount,
            warningCount,
            validationReport,
            findings);
    }
}
