using System.Buffers.Binary;
using RATools.Application.PackageValidation;

namespace RATools.Infrastructure.PackageValidation;

// Bound metadata before ZipArchive materializes the central-directory entries.
internal static class ZipDirectoryGuard
{
    public static void Validate(Stream file, PackageReadLimits limits)
    {
        var tail = new byte[(int)Math.Min(file.Length, 65_557)];
        file.Position = file.Length - tail.Length;
        file.ReadExactly(tail);
        var end = -1;
        for (var offset = tail.Length - 22; offset >= 0; offset--)
            if (U32(tail.AsSpan(offset)) == 0x06054b50 && offset + 22 + U16(tail.AsSpan(offset + 20)) == tail.Length)
            { end = offset; break; }
        if (end < 0) throw new InvalidDataException("ZIP end-of-directory record is missing or truncated.");
        var record = tail.AsSpan(end);
        if (U16(record[4..]) != 0 || U16(record[6..]) != 0 || U16(record[8..]) != U16(record[10..]))
            throw new InvalidDataException("Multi-volume ZIP inputs are not supported.");
        ulong count = U16(record[10..]);
        ulong size = U32(record[12..]);
        ulong start = U32(record[16..]);
        if (count == ushort.MaxValue || size == uint.MaxValue || start == uint.MaxValue)
        {
            var absoluteEnd = file.Length - tail.Length + end;
            if (absoluteEnd < 20) throw new InvalidDataException("ZIP64 locator is missing.");
            file.Position = absoluteEnd - 20;
            Span<byte> locator = stackalloc byte[20];
            file.ReadExactly(locator);
            if (U32(locator) != 0x07064b50 || U32(locator[4..]) != 0 || U32(locator[16..]) != 1)
                throw new InvalidDataException("Invalid or multi-volume ZIP64 locator.");
            var position = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
            if (position > (ulong)Math.Max(0, file.Length - 56)) throw new InvalidDataException("ZIP64 record is out of bounds.");
            file.Position = (long)position;
            Span<byte> zip64 = stackalloc byte[56];
            file.ReadExactly(zip64);
            if (U32(zip64) != 0x06064b50 || BinaryPrimitives.ReadUInt64LittleEndian(zip64[4..]) < 44 ||
                U32(zip64[16..]) != 0 || U32(zip64[20..]) != 0 ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64[24..]) != BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]))
                throw new InvalidDataException("Invalid ZIP64 directory record.");
            count = BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]);
            size = BinaryPrimitives.ReadUInt64LittleEndian(zip64[40..]);
            start = BinaryPrimitives.ReadUInt64LittleEndian(zip64[48..]);
        }
        if (count > (ulong)limits.MaxEntries || size > (ulong)limits.MaxCentralDirectoryBytes)
            throw new PackageInputException("ZIP-LIMITS", "ZIP_METADATA_LIMIT", "ZIP directory metadata exceeds the configured limits.");
        if (start > (ulong)file.Length || size > (ulong)file.Length - start)
            throw new InvalidDataException("ZIP central directory lies outside the input.");
        // Validate actual record count as well; a forged EOCD count must not let
        // unbounded entry objects be allocated by the framework reader.
        file.Position = (long)start;
        ulong consumed = 0, actual = 0;
        Span<byte> header = stackalloc byte[46];
        while (consumed < size)
        {
            if (size - consumed < 46) throw new InvalidDataException("Truncated ZIP directory entry.");
            file.ReadExactly(header);
            if (U32(header) != 0x02014b50) throw new InvalidDataException("Invalid ZIP central directory signature.");
            if ((U16(header[8..]) & 1) != 0) throw new InvalidDataException("Encrypted ZIP entries cannot be inspected.");
            var variableLength = (ulong)(U16(header[28..]) + U16(header[30..]) + U16(header[32..]));
            consumed += 46 + variableLength;
            if (consumed > size || ++actual > (ulong)limits.MaxEntries) throw new InvalidDataException("ZIP directory count or length exceeds its declared bounds.");
            file.Seek((long)variableLength, SeekOrigin.Current);
        }
        if (actual != count) throw new InvalidDataException("ZIP central directory count does not match its records.");
    }

    private static ushort U16(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt16LittleEndian(value);
    private static uint U32(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32LittleEndian(value);
}
