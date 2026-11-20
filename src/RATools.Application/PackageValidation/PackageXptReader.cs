using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RATools.Application.PackageValidation;

internal static class PackageXptReader
{
    internal static PackageXptFacts Read(ReadOnlyMemory<byte> bytes, string expectedSha256,
        XptTextEncoding encoding = XptTextEncoding.AsciiOnly, XptReadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        limits ??= new();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        if (limits.MaxBytes < 0 || limits.MaxVariables < 0 || limits.MaxRecordBytes < 0 || limits.MaxRows < 0 || limits.MaxCells < 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
        if (bytes.Length > limits.MaxBytes) return Result(XptReadStatus.LimitExceeded, "Input byte budget exceeded.");
        // The caller's memory need not be immutable. Bind and parse the same owned copy.
        var data = bytes.ToArray();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var offset = 0; offset < data.Length; offset += 65536)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(data, offset, Math.Min(65536, data.Length - offset));
        }
        var digest = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(digest, expectedSha256, StringComparison.Ordinal)) return Result(XptReadStatus.InputMismatch, "Captured input digest differs.", digest);
        if (data.Length < 80) return Result(XptReadStatus.Invalid, "Truncated library header.", digest);
        if (!HeaderPrefix(data, 0, "LIBRARY")) return Result(XptReadStatus.Unsupported, "Not the supported XPORT v5 library layout.", digest);
        if (!Header(data, 0, "LIBRARY")) return Result(XptReadStatus.Invalid, "Invalid library header control fields.", digest);
        if (data.Length % 80 != 0 || data.Length < 720) return Result(XptReadStatus.Invalid, "Incomplete physical records or member headers.", digest);
        if (!AsciiEquals(data, 80, "SAS     SAS     SASLIB  ") || !Header(data, 240, "MEMBER ") ||
            !Header(data, 320, "DSCRPTR") || !AsciiEquals(data, 400, "SAS     ") || !AsciiEquals(data, 416, "SASDATA ") ||
            !Header(data, 560, "NAMESTR")) return Result(XptReadStatus.Invalid, "Invalid v5 header sequence.", digest);
        var descriptorBytes = Decimal(data.AsSpan(314, 4));
        if (descriptorBytes is not (136 or 140)) return Result(XptReadStatus.Unsupported, "Unsupported NAMESTR width.", digest);
        var count = Decimal(data.AsSpan(614, 4));
        if (count <= 0) return Result(XptReadStatus.Invalid, "A positive variable count is required.", digest);
        if (count > limits.MaxVariables) return Result(XptReadStatus.LimitExceeded, "Variable budget exceeded.", digest);
        var descriptorEnd = 640L + (long)count * descriptorBytes;
        var observationHeader = (descriptorEnd + 79) / 80 * 80;
        if (observationHeader + 80 > data.Length) return Result(XptReadStatus.Invalid, "Truncated variable descriptors.", digest);
        if (!Blank(data.AsSpan((int)descriptorEnd, (int)(observationHeader - descriptorEnd))) || !Header(data, (int)observationHeader, "OBS    "))
            return Result(XptReadStatus.Invalid, "Invalid descriptor padding or observation header.", digest);
        var memberName = AsciiText(data.AsSpan(408, 8));
        var memberLabel = AsciiText(data.AsSpan(512, 40));
        if (string.IsNullOrEmpty(memberName) || memberLabel is null) return Result(XptReadStatus.Invalid, "Unqualified ASCII member metadata.", digest);
        var variables = new List<XptVariable>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<int>();
        var recordBytes = 0;
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = 640 + i * descriptorBytes;
            var descriptor = data.AsSpan(offset, descriptorBytes);
            var type = BinaryPrimitives.ReadInt16BigEndian(descriptor);
            var length = BinaryPrimitives.ReadInt16BigEndian(descriptor[4..]);
            var number = BinaryPrimitives.ReadInt16BigEndian(descriptor[6..]);
            var name = AsciiText(descriptor.Slice(8, 8));
            var label = AsciiText(descriptor.Slice(16, 40));
            var position = BinaryPrimitives.ReadInt32BigEndian(descriptor[84..]);
            if (type is not (1 or 2) || length <= 0 || type == 1 && length is < 2 or > 8 ||
                number < 1 || number > count || !numbers.Add(number) || string.IsNullOrEmpty(name) || !names.Add(name) ||
                label is null || position < 0 || (long)position + length > int.MaxValue)
                return Result(XptReadStatus.Invalid, "Invalid variable type, length, identity or position.", digest);
            recordBytes = Math.Max(recordBytes, position + length);
            if (recordBytes > limits.MaxRecordBytes) return Result(XptReadStatus.LimitExceeded, "Record span budget exceeded.", digest);
            variables.Add(new(name, label, type, length, position, number, offset));
        }
        var ordered = variables.OrderBy(variable => variable.Position).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ordered[i - 1].Position + ordered[i - 1].Length > ordered[i].Position)
                return Result(XptReadStatus.Invalid, "Overlapping variable storage intervals.", digest);
        }
        var start = (int)observationHeader + 80;
        // V5 provides no row count. A second member is considered only on physical block
        // boundaries with a viable preceding record/padding boundary and its structural headers.
        // A perfect header-shaped character payload is ambiguous, never silently called single-member.
        for (var offset = start; offset <= data.Length - 160; offset += 80)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Header(data, offset, "MEMBER ") && Header(data, offset + 80, "DSCRPTR") &&
                Bounds(data, start, offset, recordBytes).Minimum is not null)
                return Result(XptReadStatus.Unsupported, "Multiple or ambiguous member boundary; single-member scope is not established.", digest);
        }
        var bounds = Bounds(data, start, data.Length, recordBytes);
        if (bounds.Minimum is not { } minimum || bounds.Maximum is not { } maximum)
            return Result(XptReadStatus.Invalid, "No complete record count has legal trailing padding.", digest);
        if (maximum > limits.MaxRows || (long)maximum * count > limits.MaxCells)
            return Result(XptReadStatus.LimitExceeded, "Output row/cell budget exceeded; no complete row bound is published.", digest);
        var rows = new List<XptRow>();
        var valuesComplete = true;
        for (var row = 0; row < maximum; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = start + row * recordBytes;
            var cells = new List<XptCell>();
            foreach (var variable in variables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = data.AsSpan(offset + variable.Position, variable.Length);
                var cell = variable.Type == 1 ? Numeric(raw) : Character(raw, encoding);
                valuesComplete &= cell.Decoded;
                cells.Add(cell);
            }
            rows.Add(new(row, offset, row < minimum, cells));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(XptReadStatus.Read, "Read bounded single-member XPORT v5 facts; row-count and encoding uncertainty remain explicit.", digest,
            encoding, true, valuesComplete, memberName, memberLabel, recordBytes, minimum, maximum, variables, rows);

        PackageXptFacts Result(XptReadStatus status, string reason, string? sha256 = null) => new(status, reason, sha256, encoding);
    }

    private static bool HeaderPrefix(byte[] data, int offset, string name) =>
        offset >= 0 && offset <= data.Length - 80 && AsciiEquals(data, offset, "HEADER RECORD*******" + name + " HEADER RECORD!!!!!!!");
    private static bool Header(byte[] data, int offset, string name)
    {
        if (!HeaderPrefix(data, offset, name)) return false;
        var expected = name == "MEMBER " ? "000000000000000001600000000140" : new string('0', 30);
        for (var i = 48; i < 78; i++)
        {
            if (name == "MEMBER " && i >= 74 || name == "NAMESTR" && i is >= 54 and < 58) continue;
            if (data[offset + i] != expected[i - 48]) return false;
        }
        if (name == "MEMBER " && Decimal(data.AsSpan(offset + 74, 4)) < 0 ||
            name == "NAMESTR" && Decimal(data.AsSpan(offset + 54, 4)) < 0) return false;
        return data[offset + 78] == 32 && data[offset + 79] == 32;
    }
    private static bool AsciiEquals(byte[] data, int offset, string text) => offset >= 0 && offset + text.Length <= data.Length &&
        data.AsSpan(offset, text.Length).SequenceEqual(Encoding.ASCII.GetBytes(text));
    private static int Decimal(ReadOnlySpan<byte> bytes)
    {
        var value = 0;
        var started = false;
        foreach (var digit in bytes)
        {
            if (digit == 32 && !started) continue;
            if (digit is < (byte)'0' or > (byte)'9') return -1;
            started = true;
            value = value * 10 + digit - '0';
        }
        return started ? value : -1;
    }
    private static bool Blank(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) if (value != 32) return false;
        return true;
    }
    private static string? AsciiText(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) if (value is < 32 or > 126) return null;
        return Encoding.ASCII.GetString(bytes).TrimEnd(' ');
    }
    private static (int? Minimum, int? Maximum) Bounds(byte[] bytes, int start, int end, int rowBytes)
    {
        int? minimum = null, maximum = null;
        var length = end - start;
        for (var padding = 0; padding < 80 && padding <= length; padding++)
        {
            if ((length - padding) % rowBytes != 0 || !Blank(bytes.AsSpan(end - padding, padding))) continue;
            var rows = (length - padding) / rowBytes;
            minimum = minimum is null ? rows : Math.Min(minimum.Value, rows);
            maximum = maximum is null ? rows : Math.Max(maximum.Value, rows);
        }
        return (minimum, maximum);
    }
    private static XptCell Character(ReadOnlySpan<byte> raw, XptTextEncoding encoding)
    {
        var hex = Convert.ToHexString(raw);
        var length = raw.Length;
        while (length > 0 && raw[length - 1] == 32) length--;
        try
        {
            if (encoding == XptTextEncoding.AsciiOnly)
            {
                foreach (var value in raw[..length]) if (value > 127) return new(hex, null, null, null, false);
                return new(hex, Encoding.ASCII.GetString(raw[..length]), null, null, true);
            }
            return new(hex, new UTF8Encoding(false, true).GetString(raw[..length]), null, null, true);
        }
        catch (DecoderFallbackException) { return new(hex, null, null, null, false); }
    }
    private static XptCell Numeric(ReadOnlySpan<byte> raw)
    {
        var hex = Convert.ToHexString(raw);
        ulong fraction = 0;
        for (var i = 1; i < 8; i++) fraction = (fraction << 8) | (i < raw.Length ? raw[i] : 0u);
        if (fraction == 0)
        {
            if (raw[0] == 0) return new(hex, null, 0, null, true);
            if (raw[0] is (byte)'.' or (byte)'_' or >= (byte)'A' and <= (byte)'Z') return new(hex, null, null, ((char)raw[0]).ToString(), true);
            return new(hex, null, null, null, false);
        }
        var number = Math.ScaleB((double)fraction, 4 * ((raw[0] & 0x7f) - 64) - 56);
        if ((raw[0] & 0x80) != 0) number = -number;
        return new(hex, null, number, null, true);
    }
}
