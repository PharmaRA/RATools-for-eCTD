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
        if (context.Type == typeof(RATools.Application.DocumentProcessing.ProcessingJobView))
        {
            var adoption = schema.Properties["adoption"];
            schema.Properties["adoption"] = new OpenApiSchema
            {
                Nullable = true,
                AllOf = [new OpenApiSchema { Reference = adoption.Reference }]
            };
        }
        if (context.Type == typeof(PackageRunSummary))
        {
            // OpenAPI 3.0 ignores nullable next to a bare $ref. Pending and legacy
            // runs really return null, so wrap the reference for generated clients.
            var summary = schema.Properties["coverageSummary"];
            schema.Properties["coverageSummary"] = new OpenApiSchema
            {
                Nullable = true,
                AllOf = [new OpenApiSchema { Reference = summary.Reference }]
            };
        }
        if (!context.Type.IsEnum || context.Type.Namespace != "RATools.Domain.PackageValidation") return;
        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(context.Type).Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
    }
}
