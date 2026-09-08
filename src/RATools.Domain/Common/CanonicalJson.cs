using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RATools.Domain.Common;

public static class CanonicalJson
{
    public const string Version = "ratools-canonical-json-v1";
    private const long LargestInteger = 9007199254740991;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encode(JsonElement value)
    {
        var output = new StringBuilder();
        Append(value, output, 0);
        return Utf8.GetBytes(output.ToString());
    }

    public static string Digest(JsonElement value) => Convert.ToHexString(SHA256.HashData(Encode(value))).ToLowerInvariant();

    private static void Append(JsonElement value, StringBuilder output, int depth)
    {
        if (depth > 128) throw new ArgumentException("Canonical JSON nesting exceeds the supported limit.", nameof(value));
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ArgumentException("Duplicate JSON object keys are not supported.", nameof(value));
                output.Append('{');
                for (var index = 0; index < properties.Length; index++)
                {
                    if (index > 0) output.Append(',');
                    AppendString(properties[index].Name, output);
                    output.Append(':');
                    Append(properties[index].Value, output, depth + 1);
                }
                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first) output.Append(',');
                    Append(item, output, depth + 1);
                    first = false;
                }
                output.Append(']');
                break;
            case JsonValueKind.String:
                AppendString(value.GetString()!, output);
                break;
            case JsonValueKind.Number:
                if (value.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0 || !value.TryGetInt64(out var number) ||
                    number < -LargestInteger || number > LargestInteger)
                    throw new ArgumentException("Canonical JSON supports only safe integers.", nameof(value));
                output.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: throw new ArgumentException("Unsupported canonical JSON value.", nameof(value));
        }
    }

    private static void AppendString(string value, StringBuilder output)
    {
        output.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            switch (character)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\t': output.Append("\\t"); break;
                case '\n': output.Append("\\n"); break;
                case '\f': output.Append("\\f"); break;
                case '\r': output.Append("\\r"); break;
                default:
                    if (character < ' ') output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else if (char.IsSurrogate(character))
                    {
                        if (!char.IsHighSurrogate(character) || index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                            throw new ArgumentException("Unpaired surrogates are not supported.", nameof(value));
                        output.Append(character).Append(value[++index]);
                    }
                    else output.Append(character);
                    break;
            }
        }
        output.Append('"');
    }
}
