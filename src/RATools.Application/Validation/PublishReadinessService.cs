using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;

namespace RATools.Application.Validation;

public sealed class PublishReadinessService(
    ISequenceValidationService validationService,
    IEctdPackageModelBuilder packageModelBuilder,
    IIchIndexXmlWriter ichIndexXmlWriter,
    IRegionalBackboneWriterRegistry regionalBackboneWriterRegistry,
    IEctdXmlValidator ectdXmlValidator,
    IStandardsProfileProvider standardsProfileProvider,
    IEctdValidationEngine validationEngine) : IPublishReadinessService
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

        findings.AddRange(validationReport.Issues.Select(MapValidationFinding));

        if (validationReport.IsValid)
        {
            try
            {
                var package = await packageModelBuilder.BuildAsync(
                    new BuildEctdPackageRequest(request.ApplicationId, request.SequenceNumber),
                    cancellationToken);
                var profile = standardsProfileProvider.GetProfile(package.Application.TemplateKey);

                var ichResult = ichIndexXmlWriter.Write(package);
                ectdXmlValidator.Validate(new BackboneGeneratedFile(ichResult.FileName, ichResult.XmlContent), profile);

                var regionalBackboneWriter = regionalBackboneWriterRegistry.Resolve(package.Application.Region);
                foreach (var regionalFile in regionalBackboneWriter.WriteRegionalBackbones(package))
                {
                    ectdXmlValidator.Validate(regionalFile, profile);
                }

                // dry-run 构建与 DTD 校验通过后，运行验证准则规则引擎，
                // 其分级 finding 并入 readiness finding 与分类统计。
                var ruleFindings = validationEngine.Evaluate(
                    new EctdValidationContext(profile, request, package, null));
                findings.AddRange(ruleFindings);
            }
            catch (UsRegionalXmlMetadataException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "US_REGIONAL_METADATA_MISSING",
                    exception.Message,
                    "RegionalMetadata",
                    "Populate the required US Regional publishing metadata field before publishing.",
                    exception.FieldName));
            }
            catch (UsRegionalXmlSectionMappingException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "US_REGIONAL_SECTION_UNSUPPORTED",
                    exception.Message,
                    "RegionalStructure",
                    "Move the document to a supported US Regional Module 1 section or extend the writer support before publishing.",
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
                    "IchStructure",
                    "Move the document to a supported ICH CTD section before publishing.",
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
                    exception.Message,
                    "XmlValidation",
                    "Correct the XML content or source metadata so the generated backbone passes bundled DTD validation."));
            }
            catch (EctdPackageDocumentNotFoundException exception)
            {
                findings.Add(new PublishReadinessFindingDto(
                    "PublishPreflight",
                    "Error",
                    "ECTD_PACKAGE_DOCUMENT_NOT_FOUND",
                    exception.Message,
                    "DocumentInventory",
                    "Restore the missing document record or relink the placement to an existing document before publishing.",
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
                    "Lifecycle",
                    "Select a valid historical lifecycle target in the same section before publishing.",
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
                    "SectionMapping",
                    "Assign the placement to a supported CTD section before publishing.",
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
                    "Lifecycle",
                    "Change the placement operation to a supported eCTD lifecycle action before publishing.",
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
                    exception.Message,
                    "PackageModel",
                    "Resolve the package model error and rerun publish readiness before publishing."));
            }
        }

        var findingSummary = PublishReadinessFindingSummary.Create(findings);
        var isReady = findingSummary.BlockingErrorCount == 0;
        var missingMetadataFields = findings
            .Where(x => string.Equals(x.Code, "US_REGIONAL_METADATA_MISSING", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.FieldName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return new PublishReadinessReportDto(
            request.ApplicationId,
            request.SequenceNumber,
            isReady,
            isReady ? "Ready" : "Blocked",
            findingSummary.BlockingErrorCount,
            findingSummary.WarningCount,
            validationReport,
            missingMetadataFields,
            findingSummary.CategorySummaries,
            findings);
    }

    private static PublishReadinessFindingDto MapValidationFinding(ValidationIssueDto issue)
    {
        var category = issue.Code switch
        {
            "NO_PLACEMENTS" => "SequenceContent",
            "FILE_MISSING" => "DocumentInventory",
            "DOCUMENT_NOT_FOUND" => "DocumentInventory",
            "INVALID_SECTION_PATH" => "SectionMapping",
            "DUPLICATE_PUBLISHED_DOCUMENT_PATH" => "DocumentInventory",
            "REPLACE_TARGET_NOT_FOUND" => "Lifecycle",
            "DELETE_TARGET_NOT_FOUND" => "Lifecycle",
            "APPEND_TARGET_NOT_FOUND" => "Lifecycle",
            "LIFECYCLE_TARGET_INVALID" => "Lifecycle",
            "UNSUPPORTED_OPERATION_VALUE" => "Lifecycle",
            _ => "Validation"
        };

        var recommendedAction = issue.Code switch
        {
            "NO_PLACEMENTS" => "Add at least one document placement to the sequence before publishing.",
            "FILE_MISSING" => "Restore the missing file on disk or update the document storage path before publishing.",
            "DOCUMENT_NOT_FOUND" => "Restore the missing document record or remove the broken placement before publishing.",
            "INVALID_SECTION_PATH" => "Correct the CTD section path so it matches the supported standards profile before publishing.",
            "DUPLICATE_PUBLISHED_DOCUMENT_PATH" => "Rename or relocate documents so each published path is unique before publishing.",
            "REPLACE_TARGET_NOT_FOUND" => "Select a valid historical replace target before publishing.",
            "DELETE_TARGET_NOT_FOUND" => "Select a valid historical delete target before publishing.",
            "APPEND_TARGET_NOT_FOUND" => "Select a valid historical append target before publishing.",
            "LIFECYCLE_TARGET_INVALID" => "Select a valid historical lifecycle target in the same section before publishing.",
            "UNSUPPORTED_OPERATION_VALUE" => "Change the placement operation to a supported eCTD lifecycle action before publishing.",
            _ => "Resolve the validation issue before publishing."
        };

        return new PublishReadinessFindingDto(
            "Validation",
            issue.Severity,
            issue.Code,
            issue.Message,
            category,
            recommendedAction,
            null,
            issue.SectionPath,
            issue.DocumentId,
            issue.PlacementId);
    }
}
