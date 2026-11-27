using System.Security.Cryptography;
using System.Text;
using RATools.Application.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageDefineXmlReaderTests
{
    [Theory]
    [InlineData("SDTMIG")]
    [InlineData("SENDIG")]
    [InlineData("ADaMIG")]
    public void ReadsDeclarationsWithoutGuessingDatasetConformance(string name)
    {
        var result = Read(Sample(name));
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        Assert.True(result.ReadComplete);
        Assert.Equal("STUDY.A", result.StudyOid);
        Assert.Equal("MDV.A", result.MetadataOid);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal("DM", dataset.Name);
        Assert.Equal(name, Assert.Single(result.Standards).Name);
        Assert.Equal(DefineReferenceStatus.Resolved, dataset.StandardReference);
        Assert.Equal(name, dataset.Standard!.Name);
        Assert.Equal(DefineReferenceStatus.Resolved, dataset.FileReference);
        Assert.Equal("dm.xpt", dataset.File!.Href);
    }

    [Fact]
    public void AHashMismatchCannotPublishDeclarations()
    {
        var result = PackageDefineXmlReader.Read(Encoding.UTF8.GetBytes(Sample()), new string('0', 64));
        Assert.Equal(DefineXmlReadStatus.InputMismatch, result.Status);
        Assert.False(result.ReadComplete);
        Assert.Empty(result.Datasets);
    }

    [Fact]
    public void DuplicateStandardIdsDoNotChooseAnArbitraryModel()
    {
        var xml = Sample().Replace("</def:Standards>", "<def:Standard OID='STD' Name='ADaMIG' Type='IG' Version='1.0' Status='Final'/></def:Standards>", StringComparison.Ordinal);
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(DefineReferenceStatus.Ambiguous, dataset.StandardReference);
        Assert.Null(dataset.Standard);
    }

    [Theory]
    [InlineData("wrong-root")]
    [InlineData("wrong-odm")]
    [InlineData("wrong-define")]
    [InlineData("wrong-version")]
    [InlineData("missing-version")]
    [InlineData("unqualified-version")]
    [InlineData("two-studies")]
    [InlineData("two-versions")]
    [InlineData("nested-version")]
    [InlineData("two-standards")]
    [InlineData("include")]
    [InlineData("xml-base")]
    public void UnsupportedScopesDoNotPublishPartialDeclarations(string scenario)
    {
        var xml = Sample();
        xml = scenario switch
        {
            "wrong-root" => xml.Replace("<ODM ", "<Other ", StringComparison.Ordinal).Replace("</ODM>", "</Other>", StringComparison.Ordinal),
            "wrong-odm" => xml.Replace("http://www.cdisc.org/ns/odm/v1.3", "urn:other", StringComparison.Ordinal),
            "wrong-define" => xml.Replace("http://www.cdisc.org/ns/def/v2.1", "urn:other", StringComparison.Ordinal),
            "wrong-version" => xml.Replace("2.1.0", "2.0.0", StringComparison.Ordinal),
            "missing-version" => xml.Replace(" def:DefineVersion='2.1.0'", "", StringComparison.Ordinal),
            "unqualified-version" => xml.Replace("def:DefineVersion", "DefineVersion", StringComparison.Ordinal),
            "two-studies" => xml.Replace("</ODM>", "<Study OID='B'/></ODM>", StringComparison.Ordinal),
            "two-versions" => xml.Replace("</Study>", "<MetaDataVersion OID='B' def:DefineVersion='2.1.0'/></Study>", StringComparison.Ordinal),
            "nested-version" => xml.Replace("<MetaDataVersion ", "<Wrapper><MetaDataVersion ", StringComparison.Ordinal).Replace("</MetaDataVersion>", "</MetaDataVersion></Wrapper>", StringComparison.Ordinal),
            "two-standards" => xml.Replace("</def:Standards>", "</def:Standards><def:Standards/>", StringComparison.Ordinal),
            "include" => xml.Replace("</MetaDataVersion>", "<Include StudyOID='OTHER' MetaDataVersionOID='OLD'/></MetaDataVersion>", StringComparison.Ordinal),
            "xml-base" => xml.Replace("<ItemGroupDef ", "<ItemGroupDef xml:base='https://example.invalid/' ", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Unsupported, result.Status);
        Assert.False(result.ReadComplete);
        Assert.Empty(result.Standards); Assert.Empty(result.Leaves); Assert.Empty(result.Datasets);
    }

    [Theory]
    [InlineData("doctype")]
    [InlineData("external-entity")]
    [InlineData("truncated")]
    [InlineData("bad-tail")]
    [InlineData("duplicate-attribute")]
    [InlineData("empty")]
    public void InvalidXmlCannotLeakPreviouslyReadDeclarations(string scenario)
    {
        var xml = scenario switch
        {
            "doctype" => "<!DOCTYPE ODM SYSTEM 'https://example.invalid/never-fetch.dtd'>" + Sample(),
            "external-entity" => "<!DOCTYPE ODM [<!ENTITY external SYSTEM 'file:///never-read.xml'>]>" + Sample().Replace("Demographics", "&external;", StringComparison.Ordinal),
            "truncated" => Sample()[..^6], "bad-tail" => Sample() + "<broken",
            "duplicate-attribute" => Sample().Replace("OID='STUDY.A'", "OID='STUDY.A' OID='B'", StringComparison.Ordinal),
            "empty" => "", _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Invalid, result.Status);
        Assert.False(result.ReadComplete);
        Assert.Empty(result.Datasets);
    }

    [Theory]
    [InlineData("unspecified", "Unspecified")]
    [InlineData("empty", "Missing")]
    [InlineData("case", "Missing")]
    [InlineData("space", "Missing")]
    [InlineData("duplicate", "Ambiguous")]
    [InlineData("foreign-leaf", "Missing")]
    [InlineData("metadata-leaf", "Resolved")]
    public void FileReferencesAreExactAndNeverInferredFromTheFilename(string scenario, string expected)
    {
        const string leaf = "<def:leaf ID='LF.DM' xlink:href='dm.xpt'><def:title>Demographics</def:title></def:leaf>";
        var xml = scenario switch
        {
            "unspecified" => Sample().Replace(" def:ArchiveLocationID='LF.DM'", "", StringComparison.Ordinal),
            "empty" => Sample().Replace("def:ArchiveLocationID='LF.DM'", "def:ArchiveLocationID=''", StringComparison.Ordinal),
            "case" => Sample().Replace("def:ArchiveLocationID='LF.DM'", "def:ArchiveLocationID='lf.dm'", StringComparison.Ordinal),
            "space" => Sample().Replace("def:ArchiveLocationID='LF.DM'", "def:ArchiveLocationID=' LF.DM '", StringComparison.Ordinal),
            "duplicate" => Sample().Replace("</MetaDataVersion>", leaf + "</MetaDataVersion>", StringComparison.Ordinal),
            "foreign-leaf" => Sample().Replace("<def:leaf ", "<def:leaf xmlns:def='urn:other' ", StringComparison.Ordinal),
            "metadata-leaf" => Sample().Replace(leaf, "", StringComparison.Ordinal).Replace("</MetaDataVersion>", leaf + "</MetaDataVersion>", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(Enum.Parse<DefineReferenceStatus>(expected), dataset.FileReference);
        if (expected != "Resolved") Assert.Null(dataset.File);
    }

    [Theory]
    [InlineData("missing", "Unspecified")]
    [InlineData("empty", "Missing")]
    [InlineData("case", "Missing")]
    [InlineData("foreign-standard", "Missing")]
    [InlineData("no-standards", "Missing")]
    public void UnspecifiedStandardDoesNotBorrowTheOnlyDeclaredModel(string scenario, string expected)
    {
        var xml = scenario switch
        {
            "missing" => Sample().Replace(" def:StandardOID='STD'", "", StringComparison.Ordinal),
            "empty" => Sample().Replace("def:StandardOID='STD'", "def:StandardOID=''", StringComparison.Ordinal),
            "case" => Sample().Replace("def:StandardOID='STD'", "def:StandardOID='std'", StringComparison.Ordinal),
            "foreign-standard" => Sample().Replace("<def:Standard ", "<def:Standard xmlns:def='urn:other' ", StringComparison.Ordinal),
            "no-standards" => Sample().Replace("<def:Standards>", "<other>", StringComparison.Ordinal).Replace("</def:Standards>", "</other>", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(Enum.Parse<DefineReferenceStatus>(expected), dataset.StandardReference);
        Assert.Null(dataset.Standard);
    }

    [Theory]
    [InlineData("https://example.invalid/dm.xpt")]
    [InlineData("../other/dm.xpt?query#fragment")]
    [InlineData("file:///never-read.xpt")]
    public void HrefsRemainUnresolvedDeclarations(string href)
    {
        var result = Read(Sample("Custom Model").Replace("dm.xpt", href, StringComparison.Ordinal));
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        Assert.Equal(href, Assert.Single(result.Datasets).File!.Href);
        Assert.Equal("Custom Model", Assert.Single(result.Standards).Name);
    }

    [Fact]
    public void NamespaceAliasesEntitiesAndMissingValuesAreNotNormalizedIntoPolicy()
    {
        var xml = Sample().Replace("def:", "d:", StringComparison.Ordinal).Replace("xmlns:def=", "xmlns:d=", StringComparison.Ordinal)
            .Replace("Name='SDTMIG'", "Name=' SDTM&#73;G '", StringComparison.Ordinal)
            .Replace("Domain='DM'", "Domain='' d:IsNonStandard='Yes' d:HasNoData='Yes'", StringComparison.Ordinal)
            .Replace(" SASDatasetName='DM'", "", StringComparison.Ordinal);
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        Assert.Equal(" SDTMIG ", Assert.Single(result.Standards).Name);
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal("", dataset.Domain); Assert.Null(dataset.SasDatasetName);
        Assert.Equal("Yes", dataset.IsNonStandard); Assert.Equal("Yes", dataset.HasNoData);
    }

    [Fact]
    public void ForeignOrNestedGroupsCannotBecomeDatasets()
    {
        var xml = Sample().Replace("<ItemGroupDef ", "<Wrapper><ItemGroupDef ", StringComparison.Ordinal)
            .Replace("</ItemGroupDef>", "</ItemGroupDef></Wrapper><ItemGroupDef xmlns='urn:other' OID='FOREIGN'/>", StringComparison.Ordinal);
        var result = Read(xml);
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        Assert.Empty(result.Datasets); Assert.Empty(result.Leaves);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("nodes")]
    [InlineData("depth")]
    [InlineData("characters")]
    public void EachBudgetHasAnExactBoundaryAndNoPartialPublication(string budget)
    {
        var xml = Sample();
        var limits = new DefineXmlReadLimits();
        var upper = budget == "bytes" ? Encoding.UTF8.GetByteCount(xml) : 4096;
        var low = 0;
        while (low < upper)
        {
            var middle = low + (upper - low) / 2;
            if (Read(xml, Limit(middle)).Status == DefineXmlReadStatus.Read) upper = middle;
            else low = middle + 1;
        }
        Assert.True(low > 0);
        Assert.Equal(DefineXmlReadStatus.Read, Read(xml, Limit(low)).Status);
        var rejected = Read(xml, Limit(low - 1));
        Assert.Equal(DefineXmlReadStatus.LimitExceeded, rejected.Status);
        Assert.False(rejected.ReadComplete); Assert.Empty(rejected.Standards); Assert.Empty(rejected.Datasets);
        DefineXmlReadLimits Limit(int value) => budget switch
        {
            "bytes" => limits with { MaxBytes = value }, "nodes" => limits with { MaxNodes = value },
            "depth" => limits with { MaxDepth = value }, _ => limits with { MaxCharacters = value }
        };
    }

    [Fact]
    public void CancellationAndInvalidLimitsAreNotHidden()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PackageDefineXmlReader.Read(ReadOnlyMemory<byte>.Empty, "", token: cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(Sample(), new(MaxNodes: -1)));
    }

    [Fact]
    public void CompleteFactsAreReadOnlyAndBoundToTheExactInputBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(Sample());
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var result = PackageDefineXmlReader.Read(bytes, hash);
        Array.Fill(bytes, (byte)0);
        Assert.Equal(hash, result.Sha256);
        Assert.Equal("DM", Assert.Single(result.Datasets).Name);
        Assert.Throws<NotSupportedException>(() => ((IList<DefineDatasetDeclaration>)result.Datasets).Clear());
    }

    [Theory]
    [InlineData("http://www.w3c.org/1999/xlink")]
    [InlineData("urn:other")]
    public void ALeafIdDoesNotMakeAnUnqualifiedHrefAvailable(string wrongNamespace)
    {
        var result = Read(Sample().Replace("http://www.w3.org/1999/xlink", wrongNamespace, StringComparison.Ordinal));
        var dataset = Assert.Single(result.Datasets);
        Assert.Equal(DefineReferenceStatus.Resolved, dataset.FileReference);
        Assert.Equal("LF.DM", dataset.File!.Id);
        Assert.Null(dataset.File.Href); // Resolved means the declaration ID, not a usable file target.
    }

    [Fact]
    public void DuplicateDatasetDeclarationsRemainSeparateAndCtDraftNamesAreNotQualified()
    {
        var xml = Sample("Custom Model").Replace("Type='IG'", "Type='CT'", StringComparison.Ordinal)
            .Replace("Status='Final'", "Status='Draft'", StringComparison.Ordinal)
            .Replace("</MetaDataVersion>", "<ItemGroupDef OID='IG.DM' Name='OTHER'/></MetaDataVersion>", StringComparison.Ordinal);
        var result = Read(xml);
        Assert.Equal(2, result.Datasets.Count);
        Assert.Equal("CT", Assert.Single(result.Standards).Type);
        Assert.Equal("Draft", Assert.Single(result.Standards).Status);
        Assert.Null(result.Datasets[1].Standard);
        Assert.Equal(DefineReferenceStatus.Unspecified, result.Datasets[1].StandardReference);
    }

    [Fact]
    public void Utf16DeclarationUsesTheActualBytesAndDoesNotRewriteNames()
    {
        var xml = "<?xml version='1.0' encoding='utf-16'?>" + Sample("研究");
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();
        var result = PackageDefineXmlReader.Read(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(DefineXmlReadStatus.Read, result.Status);
        Assert.Equal("研究", Assert.Single(result.Standards).Name);
    }

    [Fact]
    public void InvalidUtf8CannotBecomeReplacementCharacters()
    {
        var bytes = Encoding.UTF8.GetBytes(Sample());
        bytes[Array.IndexOf(bytes, (byte)'S')] = 0xff;
        var result = PackageDefineXmlReader.Read(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(DefineXmlReadStatus.Invalid, result.Status);
        Assert.Empty(result.Datasets);
    }

    [Fact]
    public void ProcessingInstructionsAndCommentsCannotCreateDeclarations()
    {
        var xml = "<?xml-stylesheet href='https://example.invalid/never-fetch.xsl'?>" + Sample()
            .Replace("</MetaDataVersion>", "<!-- <ItemGroupDef OID='FAKE'/> --></MetaDataVersion>", StringComparison.Ordinal);
        Assert.Single(Read(xml).Datasets);
    }

    internal static string Sample(string standard = "SDTMIG") =>
        "<ODM xmlns='http://www.cdisc.org/ns/odm/v1.3' xmlns:def='http://www.cdisc.org/ns/def/v2.1' xmlns:xlink='http://www.w3.org/1999/xlink'>" +
        "<Study OID='STUDY.A'><MetaDataVersion OID='MDV.A' def:DefineVersion='2.1.0'>" +
        "<def:Standards><def:Standard OID='STD' Name='" + standard + "' Type='IG' Version='3.2' Status='Final'/></def:Standards>" +
        "<ItemGroupDef OID='IG.DM' Name='DM' SASDatasetName='DM' Domain='DM' def:StandardOID='STD' def:ArchiveLocationID='LF.DM'>" +
        "<def:leaf ID='LF.DM' xlink:href='dm.xpt'><def:title>Demographics</def:title></def:leaf></ItemGroupDef>" +
        "</MetaDataVersion></Study></ODM>";

    internal static PackageDefineXmlFacts Read(string xml, DefineXmlReadLimits? limits = null)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        return PackageDefineXmlReader.Read(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), limits);
    }
}
