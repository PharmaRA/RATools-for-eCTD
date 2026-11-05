using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RATools.Api.OpenApi;

public sealed class NonNullablePropertiesRequiredSchemaFilter : ISchemaFilter
{
    private static readonly NullabilityInfoContext NullabilityContext = new();

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        // Positional response records always serialize every constructor property,
        // including nullable values. The compiler-generated clone method identifies them.
        var isRecord = context.Type.GetMethod(
            "<Clone>$",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

        foreach (var property in context.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            var nullability = NullabilityContext.Create(property).ReadState;
            var isRequired = isRecord || (property.PropertyType.IsValueType
                ? Nullable.GetUnderlyingType(property.PropertyType) is null
                : nullability == NullabilityState.NotNull);
            if (!isRequired)
            {
                continue;
            }

            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (schema.Properties.TryGetValue(jsonName, out var propertySchema))
            {
                schema.Required.Add(jsonName);
                if (nullability == NullabilityState.Nullable && propertySchema.Reference is { } reference)
                {
                    // OpenAPI 3.0 ignores nullable beside a bare $ref. Compose the
                    // nullable response value so record properties such as Adoption
                    // remain present while explicitly allowing their pending null.
                    schema.Properties[jsonName] = new OpenApiSchema
                    {
                        AllOf = [new OpenApiSchema { Reference = reference }], Nullable = true
                    };
                }
            }
        }
    }
}
