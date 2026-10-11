using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RATools.Domain.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

internal static class PackageValidationJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException("A persisted validation value is null.");
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string VerifyReport(string json, string expectedHash, ValidationBinding binding)
    {
        if (Hash(json) != expectedHash) throw new InvalidDataException("The archived validation report digest differs from its committed run.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
            Read<ValidationBinding>(document.RootElement.GetProperty("binding").GetRawText()) != binding)
            throw new InvalidDataException("The archived report schema or binding differs from its run.");
        return json;
    }
}
