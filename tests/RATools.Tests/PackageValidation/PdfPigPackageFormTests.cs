using System.Text;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PdfPigPackageFormTests
{
    [Fact]
    public void NoFormAndUnreadablePdfAreDifferent()
    {
        var absent = Inspect("").Form;
        Assert.False(absent.HasAcroForm);
        Assert.True(absent.Complete);
        using var invalid = new MemoryStream(Encoding.ASCII.GetBytes("not PDF"));
        var unknown = new PdfPigPackageInspector().Inspect(invalid, "0000/form.pdf", new(), default).Form;
        Assert.Null(unknown.HasAcroForm);
        Assert.False(unknown.Complete);
    }

    [Theory]
    [InlineData("/V (000123) /DV (999999)", true, "000123", "999999")]
    [InlineData("/DV (999999)", false, null, "999999")]
    [InlineData("/V ()", true, "", null)]
    [InlineData("/V <303030313233>", true, "000123", null)]
    public void CurrentValueAndDefaultAreSeparate(string value, bool hasValue, string? current, string? fallback)
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] >>", "<< /T (number) /FT /Tx " + value + " >>").Form;
        Assert.True(result.Complete);
        Assert.True(result.HasAcroForm);
        var field = Assert.Single(result.Fields);
        Assert.Equal("number", field.Name);
        Assert.Equal("Tx", field.FieldType);
        Assert.Equal(hasValue, field.HasValue);
        Assert.Equal(current is null ? [] : new[] { current }, field.Values);
        Assert.Equal(fallback is null ? [] : new[] { fallback }, field.DefaultValues);
    }

    [Fact]
    public void HierarchyInheritsFieldPropertiesButWidgetsDoNotDuplicateFields()
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] >>",
            "<< /T (form) /FT /Tx /Ff 1 /V (000123) /Kids [8 0 R] >>",
            "<< /T (application) /Parent 7 0 R /Kids [9 0 R 10 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 8 0 R >>",
            "<< /Type /Annot /Subtype /Widget /Parent 8 0 R >>").Form;
        Assert.True(result.Complete);
        var field = Assert.Single(result.Fields);
        Assert.Equal("form.application", field.Name);
        Assert.True(field.ReadOnly);
        Assert.Equal("000123", Assert.Single(field.Values));
    }

    [Theory]
    [InlineData("/Btn", "/V /NDA", "NDA")]
    [InlineData("/Ch", "/V [(NDA) (BLA)]", "NDA,BLA")]
    public void NameAndArrayValuesAreRetainedWithoutGuessingMeaning(string type, string value, string expected)
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] >>", "<< /T (type) /FT " + type + " " + value + " >>").Form;
        Assert.True(result.Complete);
        Assert.Equal(expected, string.Join(',', Assert.Single(result.Fields).Values));
    }

    [Theory]
    [InlineData("/AcroForm << /Fields [] /XFA (unread) >>")]
    [InlineData("/AcroForm << /Fields 99 0 R >>")]
    [InlineData("/AcroForm << >>")]
    [InlineData("/AcroForm /Broken")]
    public void UnsupportedOrMalformedFormDoesNotInvalidateBasicPdfButIsIncomplete(string catalog)
    {
        var result = Inspect(catalog);
        Assert.Null(result.Basics.ParseError);
        Assert.False(result.Form.Complete);
        Assert.NotEmpty(result.Form.IncompleteReasons);
    }

    [Theory]
    [InlineData("<< /T (bad) /FT /Tx /Kids [7 0 R] >>")]
    [InlineData("<< /T (bad) /FT /Tx /V << /Unexpected true >> >>")]
    [InlineData("<< /T (bad.name) /FT /Tx >>")]
    [InlineData("<< /T (bad) /FT /Unknown >>")]
    [InlineData("<< /T (bad) /FT /Tx /Ff -1 >>")]
    [InlineData("<< /T (bad) /FT /Tx /V 99 0 R >>")]
    public void DamagedFieldTreeDoesNotProveCompleteInventory(string field)
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] >>", field);
        Assert.Null(result.Basics.ParseError);
        Assert.False(result.Form.Complete);
    }

    [Fact]
    public void DuplicateNamesRemainVisibleAndIncomplete()
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R 8 0 R] >>",
            "<< /T (number) /FT /Tx /V (123456) >>", "<< /T (number) /FT /Tx /V (654321) >>").Form;
        Assert.False(result.Complete);
        Assert.Equal(2, result.Fields.Count);
    }

    [Theory]
    [InlineData("/V (4) /DV <34> /Ff 1", true)]
    [InlineData("/V (5)", false)]
    [InlineData("/DV (5)", false)]
    [InlineData("/Ff 0", false)]
    public void WidgetRedundancyMustMatchParent(string widgetProperties, bool complete)
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] >>",
            "<< /T (pageCount) /FT /Tx /V (4) /DV (4) /Ff 1 /Kids [8 0 R] >>",
            "<< /Subtype /Widget /Parent 7 0 R " + widgetProperties + " >>").Form;
        Assert.Equal(complete, result.Complete);
        Assert.Equal("4", Assert.Single(Assert.Single(result.Fields).Values));
    }

    [Theory]
    [InlineData("/Parent null /Kids null")]
    [InlineData("/V null /DV null /Ff null")]
    public void ReviewNullOptionalEntriesAreAbsent(string properties)
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] /XFA null >>",
            "<< /T (number) /FT /Tx " + properties + " >>").Form;
        Assert.True(result.Complete, string.Join(" ", result.IncompleteReasons));
        Assert.False(result.HasXfa);
    }

    [Theory]
    [InlineData("/FT /Bad /Ff 0")]
    [InlineData("/FT /Tx /Ff -1")]
    [InlineData("/FT /Tx /V << /Bad true >>")]
    public void ReviewInvalidParentCannotBeHiddenByChildOverride(string properties)
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] >>",
            "<< /T (parent) " + properties + " /Kids [8 0 R] >>",
            "<< /T (child) /Parent 7 0 R /FT /Tx /Ff 0 /V (123456) >>").Form;
        Assert.False(result.Complete);
    }

    [Fact]
    public void IndirectNullWidgetKeysAndXfaAreAbsent()
    {
        var result = Inspect("/AcroForm << /Fields [7 0 R] /XFA 10 0 R >>",
            "<< /T (number) /FT /Tx /Kids [8 0 R] >>",
            "<< /Subtype /Widget /Parent 7 0 R /T 10 0 R /FT null /Kids null /V null /Ff null >>",
            "null", "null").Form;
        Assert.True(result.Complete, string.Join(" ", result.IncompleteReasons));
        Assert.False(result.HasXfa);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void MissingAcroFormReferenceIsNotProvenAbsenceAndXfaIsExplicit()
    {
        var unresolved = Inspect("/AcroForm 99 0 R").Form;
        Assert.Null(unresolved.HasAcroForm);
        Assert.False(unresolved.Complete);
        var xfa = Inspect("/AcroForm << /Fields [] /XFA (unsupported) >>").Form;
        Assert.True(xfa.HasXfa);
        Assert.False(xfa.Complete);
    }

    [Fact]
    public void PublishedFormFactsDoNotRetainMutableCallerArrays()
    {
        var values = new[] { "123456" };
        var fields = new[] { new PackagePdfFormField("number", "Tx", values, [], true, false) };
        var reasons = new[] { "reason" };
        var facts = new PackagePdfFormFacts(true, false, false, fields, reasons);
        values[0] = "changed";
        fields[0] = new("other", "Tx", [], [], false, false);
        reasons[0] = "changed";
        Assert.Equal("123456", Assert.Single(Assert.Single(facts.Fields).Values));
        Assert.Equal("reason", Assert.Single(facts.IncompleteReasons));
        Assert.Throws<NotSupportedException>(() => ((IList<PackagePdfFormField>)facts.Fields).Clear());
    }

    [Fact]
    public void FormAndNavigationShareTheObjectBudget()
    {
        var fields = Enumerable.Range(0, 25).Select(n => "<< /T (f" + n + ") /FT /Tx >>");
        using var stream = new MemoryStream(PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]",
            "/AcroForm << /Fields [" + string.Join(' ', fields) + "] >>"));
        Assert.Throws<PdfInspectionLimitException>(() => new PdfPigPackageInspector().Inspect(stream,
            "0000/form.pdf", new() { MaxPdfObjects = 25 }, default));
    }

    [Theory]
    [InlineData("356h", "db_nda_bla_nmbr", "2e6cbb433b8e65095500469440b2d6ffe8a6fc103a6f5eb82b5d6a3fcc67a1b6")]
    [InlineData("1571", "db_ind_id", "1603aea6ed84bc237c7624dd6c978edfc25176e82749c86cfa1886ec96dc3de4")]
    public void PinnedOfficialBlankFormsExposeIdentityFields(string form, string fieldName, string sha256)
    {
        var root = Environment.GetEnvironmentVariable("RATOOLS_TEST_REPOSITORY_ROOT")
            ?? throw new InvalidOperationException("Explicit repository root required for pinned form sources.");
        var path = Path.Combine(root, "reference/publisher/official-sources/2026-11-20-forms/fda-form-" + form + ".pdf");
        using var stream = File.OpenRead(path);
        Assert.Equal(sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant());
        stream.Position = 0;
        var result = new PdfPigPackageInspector().Inspect(stream, "0000/form.pdf", new(), default);
        Assert.Null(result.Basics.ParseError);
        Assert.True(result.Form.HasAcroForm);
        Assert.False(result.Form.HasXfa);
        Assert.True(result.Form.Complete, string.Join(" ", result.Form.IncompleteReasons));
        Assert.Contains(result.Form.Fields, field => field.Name == fieldName && field.FieldType == "Tx");
        // Full template identification, calculations and application matching remain separate work.
    }

    private static PackagePdfFacts Inspect(string catalog, params string[] objects)
    {
        using var stream = new MemoryStream(PdfPigPackageInspectorTests.Build("/Dest [3 0 R /Fit]", catalog, objects));
        return new PdfPigPackageInspector().Inspect(stream, "0000/form.pdf", new(), default);
    }
}
