using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageXptReaderTests
{
    [Theory]
    [InlineData(140)]
    [InlineData(136)]
    public void ReadsOfficialIbmValuesAndDescriptorWidths(int descriptorBytes)
    {
        // TS-140 pp. 7-10: X numeric length 8, Y character length 8; IBM 1/2/./.A.
        var data = Sample([new("X", 1, 8), new("Y", 2, 8)],
            [Row("4110000000000000", "a"), Row("4120000000000000", "B"), Row("2E00000000000000", "."), Row("4100000000000000", "*")], descriptorBytes);
        var result = Read(data);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.True(result.StructureComplete);
        Assert.True(result.ValuesComplete);
        Assert.Equal("TS", result.MemberName);
        Assert.Equal(4, result.MinPossibleRows);
        Assert.Equal(5, result.MaxPossibleRows); // 16 bytes of blank padding are indistinguishable from another record.
        Assert.Equal(16, result.RecordBytes);
        Assert.Equal(1d, result.Rows[0].Cells[0].Number);
        Assert.Equal(2d, result.Rows[1].Cells[0].Number);
        Assert.Equal(".", result.Rows[2].Cells[0].MissingCode);
        Assert.Equal("A", result.Rows[3].Cells[0].MissingCode);
        Assert.Equal("a", result.Rows[0].Cells[1].Text);
        Assert.All(result.Rows.Take(4), row => Assert.True(row.Certain));
        Assert.False(result.Rows[4].Certain);
    }

    [Fact]
    public void TsRowsPreserveStudyIdParameterAndNaWithoutInventingBusinessMeaning()
    {
        var columns = new[] { new Column("STUDYID", 2, 16), new Column("TSPARMCD", 2, 8), new Column("TSVAL", 2, 40), new Column("TSVALNF", 2, 8) };
        var data = Sample(columns, [TextRow(columns, "Study A", "STSTDTC", "2020-02-29", ""), TextRow(columns, "Study A", "SPREFID", "STF-A", ""), TextRow(columns, "Study A", "OTHER", "", "NA")]);
        var result = Read(data);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.Equal(3, result.MinPossibleRows);
        Assert.Equal(3, result.MaxPossibleRows);
        Assert.Equal("SPREFID", result.Rows[1].Cells[1].Text);
        Assert.Equal("STF-A", result.Rows[1].Cells[2].Text);
        Assert.Equal("NA", result.Rows[2].Cells[3].Text);
        Assert.Null(result.Rows[2].Cells[3].MissingCode);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 8)]
    [InlineData(2, 2, 8)]
    public void PaddingDoesNotErasePossibleBlankRows(int nonblankRows, int minimum, int maximum)
    {
        var columns = new[] { new Column("VALUE", 2, 10) };
        var data = Sample(columns, Enumerable.Range(0, nonblankRows).Select(_ => TextRow(columns, "value")).ToArray());
        var result = Read(data);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.Equal(minimum, result.MinPossibleRows);
        Assert.Equal(maximum, result.MaxPossibleRows);
        Assert.Equal(nonblankRows, result.Rows.Count(row => row.Certain));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf8RequiresExplicitInterpretation(bool decoded)
    {
        var encoding = decoded ? XptTextEncoding.Utf8 : XptTextEncoding.AsciiOnly;
        var columns = new[] { new Column("VALUE", 2, 80) };
        var data = Sample(columns, [TextRow(columns, "研究")]);
        var result = Read(data, encoding);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.True(result.StructureComplete);
        Assert.Equal(decoded, result.ValuesComplete);
        Assert.Equal(decoded ? "研究" : null, result.Rows[0].Cells[0].Text);
        Assert.StartsWith("E7A094E7A9B6", result.Rows[0].Cells[0].RawHex, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUtf8IsNotSilentlyReplaced()
    {
        var raw = Enumerable.Repeat((byte)32, 80).ToArray(); raw[0] = 0xc3; raw[1] = 0x28;
        var result = Read(Sample([new("VALUE", 2, 80)], [raw]), XptTextEncoding.Utf8);
        Assert.True(result.StructureComplete);
        Assert.False(result.ValuesComplete);
        Assert.False(result.Rows[0].Cells[0].Decoded);
        Assert.Null(result.Rows[0].Cells[0].Text);
    }

    [Theory]
    [InlineData("0000000000000000", 0d, null)]
    [InlineData("C110000000000000", -1d, null)]
    [InlineData("4080000000000000", 0.5d, null)]
    [InlineData("5F00000000000000", null, "_")]
    [InlineData("5A00000000000000", null, "Z")]
    public void NumericZeroSignFractionAndMissingStayDistinct(string hex, double? value, string? missing)
    {
        var result = Read(Sample([new("VALUE", 1, 8)], [Convert.FromHexString(hex)]));
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.Equal(value, result.Rows[0].Cells[0].Number);
        Assert.Equal(missing, result.Rows[0].Cells[0].MissingCode);
        Assert.Equal(hex, result.Rows[0].Cells[0].RawHex);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("descriptor-type")]
    [InlineData("zero-length")]
    [InlineData("overlap")]
    [InlineData("negative-position")]
    [InlineData("non-ascii-name")]
    [InlineData("duplicate-name")]
    public void InvalidStructureDoesNotBecomeEmptyCompleteData(string scenario)
    {
        var data = Sample([new("X", 2, 40), new("Y", 2, 40)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        switch (scenario)
        {
            case "truncated": data = data[..^1]; break;
            case "descriptor-type": data[641] = 3; break;
            case "zero-length": data[644] = 0; data[645] = 0; break;
            case "overlap": BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(640 + 140 + 84), 1); break;
            case "negative-position": BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(640 + 84), -1); break;
            case "non-ascii-name": data[648] = 0xff; break;
            case "duplicate-name": data[640 + 140 + 8] = (byte)'X'; break;
        }
        var result = Read(data);
        Assert.Equal(XptReadStatus.Invalid, result.Status);
        Assert.False(result.StructureComplete);
        Assert.Null(result.MaxPossibleRows);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("variables")]
    [InlineData("record")]
    [InlineData("rows")]
    [InlineData("cells")]
    public void LimitsNeverReturnFalseCompleteOrExactRowCount(string scenario)
    {
        var data = Sample([new("X", 2, 40), new("Y", 2, 40)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        var limits = scenario switch
        {
            "bytes" => new XptReadLimits(MaxBytes: data.Length - 1),
            "variables" => new XptReadLimits(MaxVariables: 1),
            "record" => new XptReadLimits(MaxRecordBytes: 79),
            "rows" => new XptReadLimits(MaxRows: 0),
            _ => new XptReadLimits(MaxCells: 1)
        };
        var result = PackageXptReader.Read(data, Hash(data), limits: limits);
        Assert.Equal(XptReadStatus.LimitExceeded, result.Status);
        Assert.False(result.StructureComplete);
        Assert.Null(result.MaxPossibleRows);
    }

    [Fact]
    public void DigestAndCancellationAreBound()
    {
        var data = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        Assert.Equal(XptReadStatus.InputMismatch, PackageXptReader.Read(data, new string('0', 64)).Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackageXptReader.Read(data, Hash(data), cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("v8")]
    [InlineData("cport")]
    [InlineData("namestr")]
    public void UnsupportedFormatsDoNotPretendToBeV5(string scenario)
    {
        var data = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        if (scenario == "v8") Encoding.ASCII.GetBytes("LIBV8  ").CopyTo(data, 20);
        if (scenario == "cport") Encoding.ASCII.GetBytes("**COMPRESSED**").CopyTo(data, 0);
        if (scenario == "namestr") Encoding.ASCII.GetBytes("0120").CopyTo(data, 314);
        var result = Read(data);
        Assert.Equal(XptReadStatus.Unsupported, result.Status);
        Assert.False(result.StructureComplete);
    }

    [Fact]
    public void AHeaderLikeCharacterValueDoesNotCreateAnotherMember()
    {
        var columns = new[] { new Column("VALUE", 2, 80) };
        var data = Sample(columns, [TextRow(columns, "HEADER RECORD*******MEMBER  HEADER RECORD!!!!!!!000000000000000001600000000140")]);
        var result = Read(data);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.Equal(1, result.MinPossibleRows);
        Assert.Equal(1, result.MaxPossibleRows);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(288)]
    [InlineData(368)]
    [InlineData(608)]
    [InlineData(848)]
    [InlineData(79)]
    public void CorruptFixedHeaderControlFieldsCannotBeComplete(int offset)
    {
        var data = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        data[offset] = (byte)'X';
        var result = Read(data);
        Assert.Equal(XptReadStatus.Invalid, result.Status);
        Assert.False(result.StructureComplete);
        Assert.Null(result.MaxPossibleRows);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(240)]
    [InlineData(320)]
    public void TruncatedSecondMemberCannotBecomeOrdinaryRows(int extraBytes)
    {
        var original = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        var data = original.Concat(original.Skip(240).Take(extraBytes)).ToArray();
        var result = Read(data);
        Assert.Equal(XptReadStatus.Unsupported, result.Status);
        Assert.False(result.StructureComplete);
        Assert.Null(result.MaxPossibleRows);
    }

    [Fact]
    public void FullSecondMemberCannotBecomeOrdinaryRows()
    {
        var original = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        var result = Read(original.Concat(original.Skip(240)).ToArray());
        Assert.Equal(XptReadStatus.Unsupported, result.Status);
        Assert.False(result.StructureComplete);
    }

    [Fact]
    public void OfficialBlankPaddedVariableCountIsAccepted()
    {
        var data = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        Encoding.ASCII.GetBytes("   1").CopyTo(data, 614);
        var result = Read(data);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.Single(result.Variables);
        Assert.True(result.StructureComplete);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    public void ShortIbmNumbersAreRightPaddedWithZeroBytes(int length)
    {
        var raw = Convert.FromHexString("4110000000000000")[..length];
        var result = Read(Sample([new("VALUE", 1, length)], [raw]));
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.Equal(1d, result.Rows[0].Cells[0].Number);
        Assert.Equal(Convert.ToHexString(raw), result.Rows[0].Cells[0].RawHex);
    }

    [Fact]
    public void Utf8CharacterMayCrossPhysicalBlockButNotFieldBoundary()
    {
        var columns = new[] { new Column("LEADING", 2, 79), new Column("VALUE", 2, 4) };
        var result = Read(Sample(columns, [TextRow(columns, "prefix", "研")]), XptTextEncoding.Utf8);
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.True(result.ValuesComplete);
        Assert.Equal("研", result.Rows[0].Cells[1].Text);
        var invalid = new byte[83]; Array.Fill(invalid, (byte)32);
        invalid[78] = 0xe7; invalid[79] = 0xa0; invalid[80] = 0x94;
        var broken = Read(Sample(columns, [invalid]), XptTextEncoding.Utf8);
        Assert.True(broken.StructureComplete);
        Assert.False(broken.ValuesComplete);
    }

    [Fact]
    public void FactsRemainImmutableWhenCallerChangesBytes()
    {
        var columns = new[] { new Column("VALUE", 2, 80) };
        var data = Sample(columns, [TextRow(columns, " original ")]);
        var result = Read(data);
        Array.Fill(data, (byte)0);
        Assert.Equal(" original", result.Rows[0].Cells[0].Text); // only format padding, never leading spaces
        Assert.Throws<NotSupportedException>(() => ((IList<XptVariable>)result.Variables).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<XptRow>)result.Rows).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<XptCell>)result.Rows[0].Cells).Clear());
    }

    [Fact]
    public void ExactBudgetsPermitTheQualifiedRecord()
    {
        var data = Sample([new("VALUE", 2, 80)], [Encoding.ASCII.GetBytes(new string('v', 80))]);
        var result = PackageXptReader.Read(data, Hash(data), limits: new(data.Length, 1, 80, 1, 1));
        Assert.Equal(XptReadStatus.Read, result.Status);
        Assert.True(result.StructureComplete);
        Assert.Equal(1, result.MinPossibleRows);
        Assert.Equal(1, result.MaxPossibleRows);
    }

    private static PackageXptFacts Read(byte[] data, XptTextEncoding encoding = XptTextEncoding.AsciiOnly) => PackageXptReader.Read(data, Hash(data), encoding);
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static byte[] Row(string number, string text) => [.. Convert.FromHexString(number), .. Encoding.ASCII.GetBytes(text.PadRight(8))];
    internal sealed record Column(string Name, int Type, int Length);
    internal static byte[] TextRow(Column[] columns, params string[] values)
    {
        using var output = new MemoryStream();
        for (var i = 0; i < columns.Length; i++)
        {
            var encoded = Encoding.UTF8.GetBytes(values[i]);
            output.Write(encoded); output.Write(Enumerable.Repeat((byte)32, columns[i].Length - encoded.Length).ToArray());
        }
        return output.ToArray();
    }
    internal static byte[] Sample(Column[] columns, byte[][] rows, int descriptorBytes = 140)
    {
        using var output = new MemoryStream();
        void Record(string text)
        {
            Assert.True(text.Length <= 80, "Fixture header exceeds a physical record.");
            output.Write(Encoding.ASCII.GetBytes(text.PadRight(80)));
        }
        Record("HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!000000000000000000000000000000");
        Record("SAS     SAS     SASLIB  6.06    WIN     " + new string(' ', 24) + "01JAN26:00:00:00");
        Record("01JAN26:00:00:00");
        Record("HEADER RECORD*******MEMBER  HEADER RECORD!!!!!!!000000000000000001600000000" + descriptorBytes.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        Record("HEADER RECORD*******DSCRPTR HEADER RECORD!!!!!!!000000000000000000000000000000");
        Record("SAS     TS      SASDATA 6.06    WIN     " + new string(' ', 24) + "01JAN26:00:00:00");
        Record("01JAN26:00:00:00" + new string(' ', 16) + "Trial summary".PadRight(40) + "        ");
        Record("HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!000000" + columns.Length.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + new string('0', 20));
        var position = 0;
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i]; var descriptor = new byte[descriptorBytes];
            BinaryPrimitives.WriteInt16BigEndian(descriptor, (short)column.Type);
            BinaryPrimitives.WriteInt16BigEndian(descriptor.AsSpan(4), (short)column.Length);
            BinaryPrimitives.WriteInt16BigEndian(descriptor.AsSpan(6), (short)(i + 1));
            Encoding.ASCII.GetBytes(column.Name.PadRight(8)).CopyTo(descriptor, 8);
            Encoding.ASCII.GetBytes(new string(' ', 48)).CopyTo(descriptor, 16);
            Encoding.ASCII.GetBytes(new string(' ', 8)).CopyTo(descriptor, 72);
            BinaryPrimitives.WriteInt32BigEndian(descriptor.AsSpan(84), position);
            position += column.Length; output.Write(descriptor);
        }
        while (output.Length % 80 != 0) output.WriteByte(32);
        Record("HEADER RECORD*******OBS     HEADER RECORD!!!!!!!000000000000000000000000000000");
        foreach (var row in rows) output.Write(row);
        while (output.Length % 80 != 0) output.WriteByte(32);
        return output.ToArray();
    }
}
