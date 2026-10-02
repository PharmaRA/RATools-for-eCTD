using System.Text;
using RATools.Domain.Common;

namespace RATools.Application.PackageValidation;

public sealed record PackageReference(string? LogicalPath, string? Query, string? Fragment, string? ExternalUri = null);

internal sealed class PackageReferenceScopeException(string message, string parameter) : ArgumentException(message, parameter);

public static class PackageLogicalPath
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Raw inventory names are never URI-decoded. Only ResolveReference decodes,
    // once, before applying the same portable segment checks and scope check.
    public static string Validate(string path, int maxLength = 1024, int maxDepth = 64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > maxLength || path.Contains('\\') || path.StartsWith('/') || path.EndsWith('/'))
            throw new ArgumentException("Expected a portable relative logical file or directory path.", nameof(path));
        var segments = path.Split('/');
        if (segments.Length > maxDepth) throw new ArgumentException("Logical path nesting exceeds the read limit.", nameof(path));
        foreach (var segment in segments)
            if (!string.Equals(segment, PortablePathSegment.NormalizeAndValidate(segment, nameof(path)), StringComparison.Ordinal))
                throw new ArgumentException("Logical path segments must not require trimming.", nameof(path));
        _ = StrictUtf8.GetByteCount(path);
        return path;
    }

    public static string CollisionKey(string path, int maxLength = 1024, int maxDepth = 64) =>
        Validate(path, maxLength, maxDepth).Normalize(NormalizationForm.FormC).ToUpperInvariant();

    public static PackageReference ResolveReference(string sourceLogicalPath, string reference)
    {
        Validate(sourceLogicalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (reference.Contains('\\') || reference.StartsWith('/'))
            throw new ArgumentException("Absolute or platform-specific reference paths are not allowed.", nameof(reference));
        var colon = reference.IndexOf(':');
        var boundary = reference.IndexOfAny(['/', '?', '#']);
        if (colon >= 0 && (boundary < 0 || colon < boundary))
        {
            if (Uri.TryCreate(reference, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
                return new(null, null, null, reference);
            throw new ArgumentException("Unsupported reference scheme.", nameof(reference));
        }
        var hash = reference.IndexOf('#');
        var fragment = hash < 0 ? null : Decode(reference[(hash + 1)..]);
        var beforeFragment = hash < 0 ? reference : reference[..hash];
        var queryIndex = beforeFragment.IndexOf('?');
        var query = queryIndex < 0 ? null : beforeFragment[(queryIndex + 1)..];
        var path = queryIndex < 0 ? beforeFragment : beforeFragment[..queryIndex];
        if (path.Length == 0) return new(sourceLogicalPath, query, fragment);
        var result = sourceLogicalPath.Split('/').SkipLast(1).ToList();
        foreach (var raw in path.Split('/'))
        {
            var part = Decode(raw);
            if (part == ".") continue;
            if (part == "..")
            {
                if (result.Count == 0) throw new PackageReferenceScopeException("Reference escapes the selected application.", nameof(reference));
                result.RemoveAt(result.Count - 1);
            }
            else
            {
                if (part.Contains('/')) throw new ArgumentException("Encoded separators are not allowed.", nameof(reference));
                Validate(part);
                result.Add(part);
            }
        }
        return new(Validate(string.Join('/', result)), query, fragment);
    }

    private static string Decode(string value)
    {
        var result = new StringBuilder();
        for (var index = 0; index < value.Length;)
        {
            if (value[index] != '%') { result.Append(value[index++]); continue; }
            var bytes = new List<byte>();
            while (index < value.Length && value[index] == '%')
            {
                if (index + 2 >= value.Length || !byte.TryParse(value.AsSpan(index + 1, 2),
                    System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    throw new ArgumentException("Malformed percent encoding.", nameof(value));
                bytes.Add(parsed);
                index += 3;
            }
            result.Append(StrictUtf8.GetString(bytes.ToArray()));
        }
        return result.ToString();
    }
}
