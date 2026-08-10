namespace RATools.Domain.Common;

public static class PortablePathSegment
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM\u00B9", "COM\u00B2", "COM\u00B3", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3"
    };

    public static string NormalizeAndValidate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();
        if (normalized is "." or ".."
            || normalized.EndsWith('.')
            || normalized.Any(IsInvalidCharacter)
            || IsReservedDeviceName(normalized))
        {
            throw new ArgumentException(
                "Value must be a portable single path segment and must not be a reserved device name.",
                parameterName);
        }

        return normalized;
    }

    private static bool IsInvalidCharacter(char value)
        => value < ' '
           || value is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';

    private static bool IsReservedDeviceName(string value)
    {
        var extensionSeparator = value.IndexOf('.');
        var stem = extensionSeparator < 0 ? value : value[..extensionSeparator];
        return ReservedDeviceNames.Contains(stem.TrimEnd(' ', '.'));
    }
}
