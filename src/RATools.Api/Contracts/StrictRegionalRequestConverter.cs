using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.Common;

namespace RATools.Api.Contracts;

// The typed action parameter remains meaningful to OpenAPI. Validate the untouched
// JSON before binding individual properties, including nested duplicates/case aliases.
public sealed class StrictRegionalRequestConverter<T> : JsonConverter<T> where T : class, new()
{
    private static readonly JsonSerializerOptions StrictOptions = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || System.Text.Encoding.UTF8.GetByteCount(root.GetRawText()) > 2 * 1024 * 1024)
            throw new JsonException("Regional requests require an object within the 2 MiB budget.");
        try { _ = CanonicalJson.Digest(root); }
        catch (ArgumentException error) { throw new JsonException("Duplicate or malformed JSON members are not allowed.", error); }
        catch (InvalidDataException error) { throw new JsonException("Duplicate or malformed JSON members are not allowed.", error); }
        var properties = typeof(T).GetProperties().ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), StringComparer.Ordinal);
        var result = new T();
        foreach (var input in root.EnumerateObject())
        {
            if (!properties.TryGetValue(input.Name, out var property)) throw new JsonException("Unknown request member: " + input.Name);
            property.SetValue(result, input.Value.Deserialize(property.PropertyType, StrictOptions));
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var property in typeof(T).GetProperties())
        {
            writer.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            JsonSerializer.Serialize(writer, property.GetValue(value), property.PropertyType, StrictOptions);
        }
        writer.WriteEndObject();
    }
}
