using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Dtos;
using RATools.Application.Validation.Requests;
using RATools.Application.Validation.Rules;

namespace RATools.Tests.Publishing.EuRegional;

public sealed class ScopedEuReadinessTests
{
    [Theory]
    [InlineData(false, "regionalMetadata.eu")]
    [InlineData(true, "documentContexts[0].country")]
    public async Task MissingEuMetadataBecomesStructuredBlockedReadiness(bool missingContext, string field)
    {
        var leaf = ScopedEuRegionalWriterTests.Leaf(1, "m1.3.1");
        var package = missingContext
            ? ScopedEuRegionalWriterTests.Package([leaf], documents: [new(leaf.PlacementId, null, null, "de", "spc", null)])
            : ScopedEuRegionalWriterTests.Package() with { RegionalMetadata = null };
        var service = Service(new CapturedPackage(package));
        var report = await service.GetAsync(new(package.ApplicationId, package.SequenceNumber));
        Assert.False(report.IsReady); Assert.Equal("Blocked", report.Status);
        var finding = Assert.Single(report.Findings);
        Assert.Equal("EU_REGIONAL_METADATA_MISSING", finding.Code);
        Assert.Equal("Error", finding.Severity); Assert.Equal("RegionalMetadata", finding.Category);
        Assert.Equal(field, finding.FieldName); Assert.Equal(new[] { field }, report.MissingMetadataFields);
        Assert.Contains(field, finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EuBuilderRefusalAlsoBecomesStructuredBlockedReadiness()
    {
        var invalid = ScopedEuRegionalWriterTests.Package(content: new(1, "EU", Eu: new([])));
        var exception = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(invalid));
        var report = await Service(new RefusedPackage(exception)).GetAsync(new(invalid.ApplicationId, "0001"));
        Assert.False(report.IsReady);
        Assert.Equal("eu.envelopes", Assert.Single(report.Findings).FieldName);
    }

    private static PublishReadinessService Service(IEctdPackageModelBuilder builder) => new(new ValidSequence(), builder, new IchIndexXmlWriter(),
        new RegionalBackboneWriterRegistry([new EuRegionalBackboneWriter(new EuRegionalXmlWriter())]), new EctdXmlValidator(),
        new EuEctd322StandardsProfileProvider(), new EctdValidationEngine(new RegionalEctdRuleSetProvider([])));

    private sealed class ValidSequence : ISequenceValidationService
    {
        public Task<ValidationReportDto> ValidateAsync(ValidateSequenceRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ValidationReportDto(request.ApplicationId, request.SequenceNumber, "explicit-test", true, [], [], []));
    }
    private sealed class CapturedPackage(EctdSequencePackage package) : IEctdPackageModelBuilder
    {
        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default) => Task.FromResult(package);
    }
    private sealed class RefusedPackage(EuRegionalXmlWriterException error) : IEctdPackageModelBuilder
    {
        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default) => Task.FromException<EctdSequencePackage>(error);
    }
}
