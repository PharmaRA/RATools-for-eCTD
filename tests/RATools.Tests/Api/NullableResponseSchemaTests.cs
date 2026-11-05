using System.Text.Json;
using RATools.Api.OpenApi;
using RATools.Application.DocumentProcessing;
using RATools.Application.PackageValidation;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RATools.Tests.Api;

public sealed class NullableResponseSchemaTests
{
    [Theory]
    [InlineData(typeof(ProcessingJobView), "adoption", "PdfProcessingAdoption")]
    [InlineData(typeof(PdfRenameJobView), "adoption", "PdfRenameAdoption")]
    [InlineData(typeof(PackageRunSummary), "coverageSummary", "ValidationCoverageSummary")]
    public void Nullable_record_reference_preserves_type_through_both_filters(Type responseType, string property, string target)
    {
        var options = new SchemaGeneratorOptions { SupportNonNullableReferenceTypes = true };
        options.SchemaFilters.Add(new NonNullablePropertiesRequiredSchemaFilter());
        options.SchemaFilters.Add(new PackageValidationEnumSchemaFilter());
        var generator = new SchemaGenerator(options, new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var repository = new SchemaRepository();
        var reference = generator.GenerateSchema(responseType, repository);
        var response = repository.Schemas[reference.Reference.Id];
        Assert.Contains(property, response.Required);
        var value = response.Properties[property];
        Assert.True(value.Nullable);
        Assert.Equal(target, Assert.Single(value.AllOf).Reference?.Id);
    }
}
