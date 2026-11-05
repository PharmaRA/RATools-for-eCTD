using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using RATools.Application.PackageValidation;

namespace RATools.Api.OpenApi;

// Independent reports use named states on disk and over HTTP. Existing APIs keep
// their current enum contracts; this convention only applies to the new namespace.
public sealed class PackageValidationEnumJsonConverter : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _converter = new(allowIntegerValues: false);
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum &&
        typeToConvert.Namespace == "RATools.Domain.PackageValidation";
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => _converter.CreateConverter(typeToConvert, options);
}

public sealed class PackageValidationEnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(RATools.Application.DocumentProcessing.ProcessingJobView) &&
            schema.Properties["adoption"].Reference is { } adoptionReference)
        {
            schema.Properties["adoption"] = new OpenApiSchema
            {
                Nullable = true,
                AllOf = [new OpenApiSchema { Reference = adoptionReference }]
            };
        }
        if (context.Type == typeof(PackageRunSummary) &&
            schema.Properties["coverageSummary"].Reference is { } coverageReference)
        {
            // OpenAPI 3.0 ignores nullable next to a bare $ref. Pending and legacy
            // runs really return null, so wrap the reference for generated clients.
            schema.Properties["coverageSummary"] = new OpenApiSchema
            {
                Nullable = true,
                AllOf = [new OpenApiSchema { Reference = coverageReference }]
            };
        }
        if (!context.Type.IsEnum || context.Type.Namespace != "RATools.Domain.PackageValidation") return;
        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(context.Type).Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
    }
}
