# US Regional M1 v3.3 us-regional.xml Writer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a tested `IUsRegionalXmlWriter` that converts `EctdSequencePackage.Module1Leaves` and FDA regional package metadata into deterministic US Regional M1 v3.3 `us-regional.xml` content.

**Architecture:** Add a focused `RATools.Application.Publishing.UsRegional` module, extend the package model with `EctdUsRegionalMetadata`, and keep the writer independent from `BackboneService` for this batch. The writer should use a statically maintained US Regional M1 v3.3 section map and a DTD-coverage guard test so the map is proven against the bundled DTD rather than trusted by inspection.

**Tech Stack:** .NET 8, LINQ to XML, xUnit, existing package model, bundled `reference/dtd/us-regional-v3-3.dtd`.

---

## File Structure

- Modify `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`
  - Add `EctdUsRegionalMetadata`.
  - Add `UsRegional` to `EctdSequencePackage`.
- Modify `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`
  - Populate package-level US regional metadata from current application and sequence facts where available.
  - Leave unavailable contact/telephone/email facts blank; the writer will fail fast until real metadata capture is added.
- Modify existing tests that construct `EctdSequencePackage`
  - `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
  - `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
- Create `src/RATools.Application/Publishing/UsRegional/IUsRegionalXmlWriter.cs`
  - Writer service contract.
- Create `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriteResult.cs`
  - Result record holding file name, package-relative path, document, and serialized XML.
- Create `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriterException.cs`
  - Base writer exception plus metadata and section-mapping exceptions.
- Create `src/RATools.Application/Publishing/UsRegional/UsRegionalM1V33.cs`
  - Static DTD-derived M1 section map used by the writer.
- Create `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`
  - XML writer implementation.
- Modify `src/RATools.Application/DependencyInjection.cs`
  - Register `IUsRegionalXmlWriter`.
- Create `tests/RATools.Tests/Publishing/UsRegional/UsRegionalM1V33Tests.cs`
  - Guard tests proving the static section map covers bundled DTD leaf-accepting M1 elements.
- Create `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`
  - Writer contract, XML shape, metadata, section mapping, lifecycle, error, and DI tests.

## Task 1: Extend Package Model With US Regional Metadata

**Files:**
- Modify: `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`
- Modify: `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`
- Modify: `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
- Modify: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`

- [ ] **Step 1: Add failing package-record metadata assertions**

Update `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`.

In `PackageRecords_ExposeExpectedImmutableContract`, insert an `EctdUsRegionalMetadata` argument between `EctdSequenceMetadata` and `Module1Leaves`:

```csharp
new EctdUsRegionalMetadata(
    "ANDA123456",
    "Acme Pharma",
    "Initial sequence",
    "Jane Regulatory",
    "regulatory",
    "301-555-0100",
    "office",
    "jane.regulatory@example.test",
    "anda",
    "original-application",
    "initial",
    "356h"),
```

Then add assertions after the sequence metadata assertions:

```csharp
Assert.Equal("ANDA123456", package.UsRegional.ApplicantId);
Assert.Equal("Acme Pharma", package.UsRegional.CompanyName);
Assert.Equal("Initial sequence", package.UsRegional.SubmissionDescription);
Assert.Equal("Jane Regulatory", package.UsRegional.ApplicantContactName);
Assert.Equal("regulatory", package.UsRegional.ApplicantContactType);
Assert.Equal("301-555-0100", package.UsRegional.Telephone);
Assert.Equal("office", package.UsRegional.TelephoneNumberType);
Assert.Equal("jane.regulatory@example.test", package.UsRegional.Email);
Assert.Equal("anda", package.UsRegional.ApplicationType);
Assert.Equal("original-application", package.UsRegional.SubmissionType);
Assert.Equal("initial", package.UsRegional.SubmissionSubtype);
Assert.Equal("356h", package.UsRegional.FormType);
```

In `BuildAsync_BuildsPackageMetadataWithSequenceFallbacks`, add:

```csharp
Assert.Equal("ANDA123456", package.UsRegional.ApplicantId);
Assert.Equal("Acme Pharma", package.UsRegional.CompanyName);
Assert.Equal("Initial sequence", package.UsRegional.SubmissionDescription);
Assert.Equal(string.Empty, package.UsRegional.ApplicantContactName);
Assert.Equal(string.Empty, package.UsRegional.ApplicantContactType);
Assert.Equal(string.Empty, package.UsRegional.Telephone);
Assert.Equal(string.Empty, package.UsRegional.TelephoneNumberType);
Assert.Equal(string.Empty, package.UsRegional.Email);
Assert.Equal(string.Empty, package.UsRegional.ApplicationType);
Assert.Equal("original-application", package.UsRegional.SubmissionType);
Assert.Equal(string.Empty, package.UsRegional.SubmissionSubtype);
Assert.Null(package.UsRegional.FormType);
```

In `BuildAsync_UsesSequencePublishingMetadataWhenPresent`, add:

```csharp
Assert.Equal("Regulatory Applicant LLC", package.UsRegional.CompanyName);
Assert.Equal("Safety update", package.UsRegional.SubmissionDescription);
Assert.Equal("anda", package.UsRegional.ApplicationType);
Assert.Equal("supplement", package.UsRegional.SubmissionType);
Assert.Equal("efficacy", package.UsRegional.SubmissionSubtype);
Assert.Equal("356h", package.UsRegional.FormType);
```

- [ ] **Step 2: Run focused package tests to verify RED**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests
```

Expected: FAIL at compile time because `EctdUsRegionalMetadata` and `EctdSequencePackage.UsRegional` do not exist.

- [ ] **Step 3: Add package metadata record and package property**

Update `src/RATools.Application/Publishing/PackageModel/EctdPackageRecords.cs`:

```csharp
public sealed record EctdSequencePackage(
    Guid ApplicationId,
    string ApplicationNumber,
    string SequenceNumber,
    string StandardsProfile,
    string IchEctdVersion,
    string UsRegionalModule1Version,
    EctdApplicationMetadata Application,
    EctdSequenceMetadata Sequence,
    EctdUsRegionalMetadata UsRegional,
    IReadOnlyCollection<EctdLeaf> Module1Leaves,
    IReadOnlyCollection<EctdLeaf> IchBackboneLeaves,
    IReadOnlyCollection<EctdPublishedFile> PublishedFiles);

public sealed record EctdUsRegionalMetadata(
    string ApplicantId,
    string CompanyName,
    string SubmissionDescription,
    string ApplicantContactName,
    string ApplicantContactType,
    string Telephone,
    string TelephoneNumberType,
    string Email,
    string ApplicationType,
    string SubmissionType,
    string SubmissionSubtype,
    string? FormType);
```

Update `src/RATools.Application/Publishing/PackageModel/EctdPackageModelBuilder.cs`.

Create regional metadata after `sequenceMetadata`:

```csharp
var usRegionalMetadata = new EctdUsRegionalMetadata(
    application.ApplicationNumber,
    sequenceMetadata.ApplicantName,
    sequenceMetadata.Description,
    string.Empty,
    string.Empty,
    string.Empty,
    string.Empty,
    string.Empty,
    applicationMetadata.ApplicationType ?? string.Empty,
    sequenceMetadata.SubmissionType,
    sequenceMetadata.SubmissionSubtype ?? string.Empty,
    sequenceMetadata.FormType);
```

Pass it into the package constructor between `sequenceMetadata` and `module1Leaves`.

- [ ] **Step 4: Update existing package constructor call sites**

Update package construction helpers in:

- `tests/RATools.Tests/Publishing/PackageModel/EctdPackageModelBuilderTests.cs`
- `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`

Use this helper in writer tests where package metadata is not under test:

```csharp
new EctdUsRegionalMetadata(
    "ANDA123456",
    "Acme Pharma",
    "Initial sequence",
    "Jane Regulatory",
    "regulatory",
    "301-555-0100",
    "office",
    "jane.regulatory@example.test",
    "anda",
    "original-application",
    "initial",
    "356h")
```

- [ ] **Step 5: Run package and ICH writer tests to verify GREEN**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Publishing.PackageModel.EctdPackageModelBuilderTests|FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests"
```

Expected: PASS.

- [ ] **Step 6: Commit package model metadata**

Run:

```powershell
git add src\RATools.Application\Publishing\PackageModel\EctdPackageRecords.cs src\RATools.Application\Publishing\PackageModel\EctdPackageModelBuilder.cs
git add -f tests\RATools.Tests\Publishing\PackageModel\EctdPackageModelBuilderTests.cs tests\RATools.Tests\Publishing\Ich\IchIndexXmlWriterTests.cs
git commit -m "feat: add US regional package metadata"
```

## Task 2: Add US Regional Writer Contract

**Files:**
- Create: `src/RATools.Application/Publishing/UsRegional/IUsRegionalXmlWriter.cs`
- Create: `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriteResult.cs`
- Create: `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriterException.cs`
- Create: `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`

- [ ] **Step 1: Write failing contract test**

Create `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`:

```csharp
using System.Xml.Linq;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class UsRegionalXmlWriterTests
{
    [Fact]
    public void WriteResult_ExposesExpectedContract()
    {
        var document = new XDocument(new XElement("root"));

        var result = new UsRegionalXmlWriteResult(
            "us-regional.xml",
            "m1/us/us-regional.xml",
            document,
            "<root />");

        Assert.Equal("us-regional.xml", result.FileName);
        Assert.Equal("m1/us/us-regional.xml", result.RelativePath);
        Assert.Same(document, result.Document);
        Assert.Equal("<root />", result.XmlContent);
    }
}
```

- [ ] **Step 2: Run focused contract test to verify RED**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests.WriteResult_ExposesExpectedContract
```

Expected: FAIL at compile time because `RATools.Application.Publishing.UsRegional` does not exist.

- [ ] **Step 3: Add minimal contract files**

Create `src/RATools.Application/Publishing/UsRegional/IUsRegionalXmlWriter.cs`:

```csharp
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.UsRegional;

public interface IUsRegionalXmlWriter
{
    UsRegionalXmlWriteResult Write(EctdSequencePackage package);
}
```

Create `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriteResult.cs`:

```csharp
using System.Xml.Linq;

namespace RATools.Application.Publishing.UsRegional;

public sealed record UsRegionalXmlWriteResult(
    string FileName,
    string RelativePath,
    XDocument Document,
    string XmlContent);
```

Create `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriterException.cs`:

```csharp
namespace RATools.Application.Publishing.UsRegional;

public abstract class UsRegionalXmlWriterException(string message) : Exception(message);

public sealed class UsRegionalXmlMetadataException(
    Guid applicationId,
    string sequenceNumber,
    string fieldName,
    string reason)
    : UsRegionalXmlWriterException($"Unable to generate US regional XML for sequence {sequenceNumber}: metadata field '{fieldName}' {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public string FieldName { get; } = fieldName;

    public string Reason { get; } = reason;
}

public sealed class UsRegionalXmlSectionMappingException(
    Guid applicationId,
    string sequenceNumber,
    Guid? placementId,
    string? ctdSection,
    string reason)
    : UsRegionalXmlWriterException($"Unable to map Module 1 section '{ctdSection ?? "(none)"}' in sequence {sequenceNumber}: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid? PlacementId { get; } = placementId;

    public string? CtdSection { get; } = ctdSection;

    public string Reason { get; } = reason;
}
```

- [ ] **Step 4: Run focused contract test to verify GREEN**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests.WriteResult_ExposesExpectedContract
```

Expected: PASS.

- [ ] **Step 5: Commit writer contract**

Run:

```powershell
git add src\RATools.Application\Publishing\UsRegional
git add -f tests\RATools.Tests\Publishing\UsRegional\UsRegionalXmlWriterTests.cs
git commit -m "feat: add US regional XML writer contract"
```

## Task 3: Generate Root and Required Admin Metadata

**Files:**
- Modify: `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`
- Create: `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`

- [ ] **Step 1: Add failing root/admin tests and helpers**

Append to `UsRegionalXmlWriterTests`:

```csharp
using RATools.Application.Publishing.PackageModel;

[Fact]
public void Write_GeneratesRootDoctypeNamespacesAndAdmin()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves: []);

    var result = writer.Write(package);

    Assert.Equal("us-regional.xml", result.FileName);
    Assert.Equal("m1/us/us-regional.xml", result.RelativePath);
    Assert.Equal("fda-regional", result.Document.Root?.Name.LocalName);
    Assert.Equal("http://www.ich.org/fda", result.Document.Root?.Name.NamespaceName);
    Assert.Equal("3.3", result.Document.Root?.Attribute("dtd-version")?.Value);
    Assert.Equal("fda-regional:fda-regional", result.Document.DocumentType?.Name);
    Assert.Equal("../../util/dtd/us-regional-v3-3.dtd", result.Document.DocumentType?.SystemId);
    Assert.Contains("xmlns:fda-regional=\"http://www.ich.org/fda\"", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("xmlns:xlink=\"http://www.w3c.org/1999/xlink\"", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<admin><applicant-info>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<id>ANDA123456</id>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<company-name>Acme Pharma</company-name>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<submission-description>Initial sequence</submission-description>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<applicant-contact-name applicant-contact-type=\"regulatory\">Jane Regulatory</applicant-contact-name>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<telephone telephone-number-type=\"office\">301-555-0100</telephone>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<email>jane.regulatory@example.test</email>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<application application-containing-files=\"false\">", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<application-number application-type=\"anda\">ANDA123456</application-number>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<submission-id submission-type=\"original-application\">0001</submission-id>", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("<sequence-number submission-sub-type=\"initial\">0001</sequence-number>", result.XmlContent, StringComparison.Ordinal);
    Assert.DoesNotContain("<m1-regional>", result.XmlContent, StringComparison.Ordinal);
}

[Fact]
public void Write_ThrowsArgumentNullExceptionForNullPackage()
{
    var writer = new UsRegionalXmlWriter();

    void Act() => writer.Write(null!);

    Assert.Throws<ArgumentNullException>(Act);
}

[Fact]
public void Write_ThrowsMetadataExceptionForMissingRequiredRegionalMetadata()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(usRegional: CreateUsRegionalMetadata(applicantContactName: ""));

    var exception = Assert.Throws<UsRegionalXmlMetadataException>(() => writer.Write(package));

    Assert.Equal(package.ApplicationId, exception.ApplicationId);
    Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
    Assert.Equal("ApplicantContactName", exception.FieldName);
    Assert.Equal("is required", exception.Reason);
}

private static EctdSequencePackage CreatePackage(
    EctdUsRegionalMetadata? usRegional = null,
    IReadOnlyCollection<EctdLeaf>? module1Leaves = null,
    IReadOnlyCollection<EctdLeaf>? ichLeaves = null)
{
    return new EctdSequencePackage(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "ANDA123456",
        "0001",
        "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
        "3.2.2",
        "3.3",
        new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-322", "anda"),
        new EctdSequenceMetadata("0001", "original-application", "initial", "Initial sequence", "Acme Pharma", "356h"),
        usRegional ?? CreateUsRegionalMetadata(),
        module1Leaves ?? [],
        ichLeaves ?? [],
        []);
}

private static EctdUsRegionalMetadata CreateUsRegionalMetadata(
    string applicantContactName = "Jane Regulatory")
{
    return new EctdUsRegionalMetadata(
        "ANDA123456",
        "Acme Pharma",
        "Initial sequence",
        applicantContactName,
        "regulatory",
        "301-555-0100",
        "office",
        "jane.regulatory@example.test",
        "anda",
        "original-application",
        "initial",
        "356h");
}
```

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests
```

Expected: FAIL at compile time because `UsRegionalXmlWriter` does not exist.

- [ ] **Step 3: Implement minimal root/admin writer**

Create `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`:

```csharp
using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.UsRegional;

public sealed class UsRegionalXmlWriter : IUsRegionalXmlWriter
{
    private static readonly XNamespace FdaRegionalNamespace = "http://www.ich.org/fda";
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";

    public UsRegionalXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateRequiredMetadata(package);

        var root = new XElement(FdaRegionalNamespace + "fda-regional",
            new XAttribute(XNamespace.Xmlns + "fda-regional", FdaRegionalNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", "3.3"),
            BuildAdminElement(package));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType("fda-regional:fda-regional", null, "../../util/dtd/us-regional-v3-3.dtd", null),
            root);

        return new UsRegionalXmlWriteResult(
            "us-regional.xml",
            "m1/us/us-regional.xml",
            document,
            document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement BuildAdminElement(EctdSequencePackage package)
    {
        var metadata = package.UsRegional;
        var submissionInformationChildren = new List<object>
        {
            new XElement("submission-id",
                new XAttribute("submission-type", metadata.SubmissionType),
                package.SequenceNumber),
            new XElement("sequence-number",
                new XAttribute("submission-sub-type", metadata.SubmissionSubtype),
                package.SequenceNumber)
        };

        if (!string.IsNullOrWhiteSpace(metadata.FormType))
        {
            submissionInformationChildren.Add(new XElement("form",
                new XAttribute("form-type", metadata.FormType)));
        }

        return new XElement("admin",
            new XElement("applicant-info",
                new XElement("id", metadata.ApplicantId),
                new XElement("company-name", metadata.CompanyName),
                new XElement("submission-description", metadata.SubmissionDescription),
                new XElement("applicant-contacts",
                    new XElement("applicant-contact",
                        new XElement("applicant-contact-name",
                            new XAttribute("applicant-contact-type", metadata.ApplicantContactType),
                            metadata.ApplicantContactName),
                        new XElement("telephones",
                            new XElement("telephone",
                                new XAttribute("telephone-number-type", metadata.TelephoneNumberType),
                                metadata.Telephone)),
                        new XElement("emails",
                            new XElement("email", metadata.Email))))),
            new XElement("application-set",
                new XElement("application",
                    new XAttribute("application-containing-files", package.Module1Leaves.Count > 0 ? "true" : "false"),
                    new XElement("application-information",
                        new XElement("application-number",
                            new XAttribute("application-type", metadata.ApplicationType),
                            package.ApplicationNumber)),
                    new XElement("submission-information", submissionInformationChildren))));
    }

    private static void ValidateRequiredMetadata(EctdSequencePackage package)
    {
        var metadata = package.UsRegional;
        Require(package, nameof(metadata.ApplicantId), metadata.ApplicantId);
        Require(package, nameof(metadata.CompanyName), metadata.CompanyName);
        Require(package, nameof(metadata.SubmissionDescription), metadata.SubmissionDescription);
        Require(package, nameof(metadata.ApplicantContactName), metadata.ApplicantContactName);
        Require(package, nameof(metadata.ApplicantContactType), metadata.ApplicantContactType);
        Require(package, nameof(metadata.Telephone), metadata.Telephone);
        Require(package, nameof(metadata.TelephoneNumberType), metadata.TelephoneNumberType);
        Require(package, nameof(metadata.Email), metadata.Email);
        Require(package, nameof(metadata.ApplicationType), metadata.ApplicationType);
        Require(package, nameof(metadata.SubmissionType), metadata.SubmissionType);
        Require(package, nameof(metadata.SubmissionSubtype), metadata.SubmissionSubtype);
    }

    private static void Require(EctdSequencePackage package, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsRegionalXmlMetadataException(package.ApplicationId, package.SequenceNumber, fieldName, "is required");
        }
    }
}
```

- [ ] **Step 4: Run focused tests to verify GREEN**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests
```

Expected: PASS.

- [ ] **Step 5: Commit root/admin writer**

Run:

```powershell
git add src\RATools.Application\Publishing\UsRegional
git add -f tests\RATools.Tests\Publishing\UsRegional\UsRegionalXmlWriterTests.cs
git commit -m "feat: generate US regional XML admin root"
```

## Task 4: Add DTD-Derived M1 Section Map and Guard Tests

**Files:**
- Create: `src/RATools.Application/Publishing/UsRegional/UsRegionalM1V33.cs`
- Create: `tests/RATools.Tests/Publishing/UsRegional/UsRegionalM1V33Tests.cs`

- [ ] **Step 1: Write failing section-map tests**

Create `tests/RATools.Tests/Publishing/UsRegional/UsRegionalM1V33Tests.cs`:

```csharp
using System.Text.RegularExpressions;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class UsRegionalM1V33Tests
{
    [Theory]
    [InlineData("m1.2", "m1-2-cover-letters")]
    [InlineData("m1.14.2.3", "m1-14-2-3-final-labeling-text")]
    [InlineData("m1.16.2.1", "m1-16-2-1-final-rems")]
    public void TryFind_ReturnsKnownDtdElementName(string sectionPath, string expectedElementName)
    {
        var found = UsRegionalM1V33.TryFind(sectionPath, out var node);

        Assert.True(found);
        Assert.NotNull(node);
        Assert.Equal(expectedElementName, node!.ElementName);
    }

    [Fact]
    public void Map_CoversLeafAcceptingM1DtdElementsWithoutRequiredStructuralAttributes()
    {
        var requiredElements = LoadLeafAcceptingM1ElementsFromDtd()
            .Where(x => !IsAttributeHeavyElement(x))
            .ToArray();
        var mappedElements = UsRegionalM1V33.Flatten()
            .Where(x => x.AcceptsLeaves)
            .Select(x => x.ElementName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var element in requiredElements)
        {
            Assert.Contains(element, mappedElements);
        }
    }

    [Fact]
    public void Map_MarksPromotionalMaterialNodesAsRequiringUnsupportedAttributes()
    {
        var found = UsRegionalM1V33.TryFind("m1.15.2.1.1", out var node);

        Assert.True(found);
        Assert.NotNull(node);
        Assert.True(node!.RequiresUnsupportedAttributes);
    }

    private static IEnumerable<string> LoadLeafAcceptingM1ElementsFromDtd()
    {
        var dtdPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "reference", "dtd", "us-regional-v3-3.dtd"));
        var dtd = File.ReadAllText(dtdPath);
        var matches = Regex.Matches(dtd, @"<!ELEMENT\s+(m1-[^\s]+)\s+\(\(leaf\s+\|\s+node-extension\)\*\)>");
        return matches.Select(x => x.Groups[1].Value);
    }

    private static bool IsAttributeHeavyElement(string elementName)
        => elementName.StartsWith("m1-15", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run section-map tests to verify RED**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalM1V33Tests
```

Expected: FAIL at compile time because `UsRegionalM1V33` does not exist.

- [ ] **Step 3: Add static section map type**

Create `src/RATools.Application/Publishing/UsRegional/UsRegionalM1V33.cs`:

```csharp
namespace RATools.Application.Publishing.UsRegional;

public static class UsRegionalM1V33
{
    public static readonly UsRegionalSectionNode Root = Node(
        "m1-regional",
        "m1",
        acceptsLeaves: false,
        requiresUnsupportedAttributes: false,
        children:
        [
            Node("m1-1-forms", "m1.1", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-2-cover-letters", "m1.2", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
            Node(
                "m1-3-administrative-information",
                "m1.3",
                acceptsLeaves: false,
                requiresUnsupportedAttributes: false,
                [
                    Node(
                        "m1-3-1-contact-sponsor-applicant-information",
                        "m1.3.1",
                        acceptsLeaves: false,
                        requiresUnsupportedAttributes: false,
                        [
                            Node("m1-3-1-1-change-of-address-or-corporate-name", "m1.3.1.1", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-3-1-2-change-in-contact-agent", "m1.3.1.2", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-3-1-3-change-in-sponsor", "m1.3.1.3", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-3-1-4-transfer-of-obligation", "m1.3.1.4", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-3-1-5-change-in-ownership-of-an-application-or-reissuance-of-license", "m1.3.1.5", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
                        ]),
                    Node("m1-3-2-field-copy-certification", "m1.3.2", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                    Node("m1-3-3-debarment-certification", "m1.3.3", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                    Node("m1-3-4-financial-certification-and-disclosure", "m1.3.4", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                    Node(
                        "m1-3-5-patent-and-exclusivity",
                        "m1.3.5",
                        acceptsLeaves: false,
                        requiresUnsupportedAttributes: false,
                        [
                            Node("m1-3-5-1-patent-information", "m1.3.5.1", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-3-5-2-patent-certification", "m1.3.5.2", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-3-5-3-exclusivity-claim", "m1.3.5.3", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
                        ]),
                    Node("m1-3-6-tropical-disease-priority-review-voucher", "m1.3.6", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
                ]),
            Node("m1-4-references", "m1.4", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-5-application-status", "m1.5", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-6-meetings", "m1.6", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-7-fast-track", "m1.7", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-8-special-protocol-assessment-request", "m1.8", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-9-pediatric-administrative-information", "m1.9", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-10-dispute-resolution", "m1.10", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-11-information-amendment-information-not-covered-under-modules-2-to-5", "m1.11", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-12-other-correspondence", "m1.12", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node("m1-13-annual-report", "m1.13", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
            Node(
                "m1-14-labeling",
                "m1.14",
                acceptsLeaves: false,
                requiresUnsupportedAttributes: false,
                [
                    Node("m1-14-1-draft-labeling", "m1.14.1", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
                    Node(
                        "m1-14-2-final-labeling",
                        "m1.14.2",
                        acceptsLeaves: false,
                        requiresUnsupportedAttributes: false,
                        [
                            Node("m1-14-2-1-final-carton-or-container-labels", "m1.14.2.1", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-14-2-2-final-package-insert-package-inserts-patient-information-medication-guides", "m1.14.2.2", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                            Node("m1-14-2-3-final-labeling-text", "m1.14.2.3", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
                        ]),
                    Node("m1-14-3-listed-drug-labeling", "m1.14.3", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
                    Node("m1-14-4-investigational-drug-labeling", "m1.14.4", acceptsLeaves: false, requiresUnsupportedAttributes: false, []),
                    Node("m1-14-5-foreign-labeling", "m1.14.5", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                    Node("m1-14-6-product-labeling-for-2253-submissions", "m1.14.6", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
                ]),
            Node(
                "m1-15-promotional-material",
                "m1.15",
                acceptsLeaves: false,
                requiresUnsupportedAttributes: true,
                [
                    Node("m1-15-2-materials", "m1.15.2", acceptsLeaves: false, requiresUnsupportedAttributes: true,
                    [
                        Node("m1-15-2-1-material", "m1.15.2.1", acceptsLeaves: false, requiresUnsupportedAttributes: true,
                        [
                            Node("m1-15-2-1-1-clean-version", "m1.15.2.1.1", acceptsLeaves: true, requiresUnsupportedAttributes: true, [])
                        ])
                    ])
                ]),
            Node(
                "m1-16-risk-management-plan",
                "m1.16",
                acceptsLeaves: false,
                requiresUnsupportedAttributes: false,
                [
                    Node("m1-16-1-risk-management-non-rems", "m1.16.1", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
                    Node(
                        "m1-16-2-risk-evaluation-and-mitigation-strategies-rems",
                        "m1.16.2",
                        acceptsLeaves: false,
                        requiresUnsupportedAttributes: false,
                        [
                            Node("m1-16-2-1-final-rems", "m1.16.2.1", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
                        ])
                ]),
            Node("m1-17-postmarketing-studies", "m1.17", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
            Node("m1-18-proprietary-names", "m1.18", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
            Node("m1-19-pre-eua-and-eua", "m1.19", acceptsLeaves: true, requiresUnsupportedAttributes: false, []),
            Node("m1-20-general-investigational-plan-for-initial-ind", "m1.20", acceptsLeaves: true, requiresUnsupportedAttributes: false, [])
        ]);

    private static readonly IReadOnlyDictionary<string, UsRegionalSectionNode> BySectionPath = Flatten()
        .ToDictionary(x => x.SectionPath, x => x, StringComparer.OrdinalIgnoreCase);

    public static bool TryFind(string sectionPath, out UsRegionalSectionNode? node)
        => BySectionPath.TryGetValue(sectionPath, out node);

    public static IEnumerable<UsRegionalSectionNode> Flatten()
        => Flatten(Root);

    private static IEnumerable<UsRegionalSectionNode> Flatten(UsRegionalSectionNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static UsRegionalSectionNode Node(
        string elementName,
        string sectionPath,
        bool acceptsLeaves,
        bool requiresUnsupportedAttributes,
        IReadOnlyCollection<UsRegionalSectionNode> children)
        => new(elementName, sectionPath, acceptsLeaves, requiresUnsupportedAttributes, children);
}

public sealed record UsRegionalSectionNode(
    string ElementName,
    string SectionPath,
    bool AcceptsLeaves,
    bool RequiresUnsupportedAttributes,
    IReadOnlyCollection<UsRegionalSectionNode> Children);
```

Keep expanding the static map until `Map_CoversLeafAcceptingM1DtdElementsWithoutRequiredStructuralAttributes` passes. The guard test is the acceptance proof for coverage; do not weaken the test to fit an incomplete map.

- [ ] **Step 4: Run section-map tests to verify GREEN**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalM1V33Tests
```

Expected: PASS.

- [ ] **Step 5: Commit section map**

Run:

```powershell
git add src\RATools.Application\Publishing\UsRegional\UsRegionalM1V33.cs
git add -f tests\RATools.Tests\Publishing\UsRegional\UsRegionalM1V33Tests.cs
git commit -m "feat: add US regional M1 section map"
```

## Task 5: Map Module 1 Leaves Into us-regional.xml

**Files:**
- Modify: `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`
- Modify: `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`

- [ ] **Step 1: Add failing M1 leaf mapping tests**

Append to `UsRegionalXmlWriterTests`:

```csharp
[Fact]
public void Write_MapsModule1LeavesToDtdSectionsInOrder()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves:
    [
        CreateLeaf("m1.16.2.1", "leaf-00000000000000000000000000000003", "rems.pdf"),
        CreateLeaf("m1.2", "leaf-00000000000000000000000000000001", "cover-letter.pdf"),
        CreateLeaf("m1.14.2.3", "leaf-00000000000000000000000000000002", "labeling.pdf")
    ]);

    var xml = writer.Write(package).XmlContent;

    Assert.Contains("<m1-regional>", xml, StringComparison.Ordinal);
    Assert.Contains("<m1-2-cover-letters>", xml, StringComparison.Ordinal);
    Assert.Contains("<m1-14-labeling><m1-14-2-final-labeling><m1-14-2-3-final-labeling-text>", xml, StringComparison.Ordinal);
    Assert.Contains("<m1-16-risk-management-plan><m1-16-2-risk-evaluation-and-mitigation-strategies-rems><m1-16-2-1-final-rems>", xml, StringComparison.Ordinal);
    Assert.True(xml.IndexOf("<m1-2-cover", StringComparison.Ordinal) < xml.IndexOf("<m1-14-labeling", StringComparison.Ordinal));
    Assert.True(xml.IndexOf("<m1-14-labeling", StringComparison.Ordinal) < xml.IndexOf("<m1-16-risk", StringComparison.Ordinal));
}

[Fact]
public void Write_IgnoresIchLeaves()
{
    var writer = new UsRegionalXmlWriter();
    var ichLeaf = CreateLeaf("m3.2", "leaf-00000000000000000000000000000004", "quality.pdf");
    var package = CreatePackage(module1Leaves: [], ichLeaves: [ichLeaf]);

    var xml = writer.Write(package).XmlContent;

    Assert.DoesNotContain("m3-quality", xml, StringComparison.Ordinal);
    Assert.DoesNotContain("leaf-00000000000000000000000000000004", xml, StringComparison.Ordinal);
}

[Fact]
public void Write_EmitsLeafAttributesRelativeHrefAndLifecycleModifiedFile()
{
    var writer = new UsRegionalXmlWriter();
    var lifecycle = new EctdLifecycleReference(Guid.NewGuid(), Guid.NewGuid(), "0000", "m1/us/12-cover-letters/old.pdf");
    var package = CreatePackage(module1Leaves:
    [
        CreateLeaf("m1.2", "leaf-11111111111111111111111111111111", "new.pdf", "replace", lifecycle)
    ]);

    var result = writer.Write(package);
    var leaf = result.Document.Descendants("leaf").Single();

    Assert.Equal("leaf-11111111111111111111111111111111", leaf.Attribute("ID")?.Value);
    Assert.Equal("replace", leaf.Attribute("operation")?.Value);
    Assert.Equal("sha-new.pdf", leaf.Attribute("checksum")?.Value);
    Assert.Equal("sha256", leaf.Attribute("checksum-type")?.Value);
    Assert.Equal("simple", leaf.Attribute(XName.Get("type", "http://www.w3c.org/1999/xlink"))?.Value);
    Assert.Equal("12-cover-letters/new.pdf", leaf.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
    Assert.Equal("../../../0000/m1/us/12-cover-letters/old.pdf", leaf.Attribute("modified-file")?.Value);
    Assert.Equal("new", leaf.Element("title")?.Value);
}

[Fact]
public void Write_DoesNotEmitPrototypeOnlyLeafChildren()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves: [CreateLeaf("m1.2", "leaf-22222222222222222222222222222222", "cover.pdf")]);

    var xml = writer.Write(package).XmlContent;

    Assert.DoesNotContain("<fileName>", xml, StringComparison.Ordinal);
    Assert.DoesNotContain("<mimeType>", xml, StringComparison.Ordinal);
}

[Fact]
public void Write_ProducesStableXmlForRepeatedWrites()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves:
    [
        CreateLeaf("m1.2", "leaf-33333333333333333333333333333333", "cover-a.pdf"),
        CreateLeaf("m1.2", "leaf-33333333333333333333333333333334", "cover-b.pdf")
    ]);

    var first = writer.Write(package).XmlContent;
    var second = writer.Write(package).XmlContent;

    Assert.Equal(first, second);
}

private static EctdLeaf CreateLeaf(
    string ctdSection,
    string leafId,
    string fileName,
    string operation = "new",
    EctdLifecycleReference? lifecycle = null)
{
    var href = ctdSection switch
    {
        "m1.2" => $"m1/us/12-cover-letters/{fileName}",
        "m1.14.2.3" => $"m1/us/114-labeling/{fileName}",
        "m1.16.2.1" => $"m1/us/116-risk-management-plan/{fileName}",
        _ => $"{ctdSection.Replace('.', '/')}/{fileName}"
    };

    return new EctdLeaf(
        Guid.Parse($"{leafId[5..13]}-{leafId[13..17]}-{leafId[17..21]}-{leafId[21..25]}-{leafId[25..37]}"),
        Guid.NewGuid(),
        leafId,
        "0001",
        ctdSection,
        ctdSection.Split('.')[0],
        operation,
        Path.GetFileNameWithoutExtension(fileName),
        href,
        fileName,
        "application/pdf",
        $"C:/workspace/0001/{ctdSection}/{fileName}",
        10,
        $"sha-{fileName}",
        lifecycle);
}
```

- [ ] **Step 2: Run writer tests to verify RED**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests
```

Expected: FAIL because the writer currently emits only `admin`.

- [ ] **Step 3: Implement M1 leaf grouping and recursive section output**

Update `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`.

After `BuildAdminElement(package)` add M1 regional output when needed:

```csharp
var m1Regional = BuildM1RegionalElement(package);
if (m1Regional is not null)
{
    root.Add(m1Regional);
}
```

Add:

```csharp
private static XElement? BuildM1RegionalElement(EctdSequencePackage package)
{
    ValidateLeaves(package);

    var leavesBySection = package.Module1Leaves
        .Select((leaf, index) => new IndexedLeaf(leaf, index))
        .GroupBy(x => x.Leaf.CtdSection, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            x => x.Key,
            x => x.OrderBy(leaf => leaf.Index).ThenBy(leaf => leaf.Leaf.LeafId, StringComparer.OrdinalIgnoreCase).Select(leaf => leaf.Leaf).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    var childElements = UsRegionalM1V33.Root.Children
        .Select(child => BuildSectionElement(child, leavesBySection))
        .Where(child => child is not null)
        .Cast<XElement>()
        .ToArray();

    return childElements.Length == 0
        ? null
        : new XElement("m1-regional", childElements);
}

private static XElement? BuildSectionElement(
    UsRegionalSectionNode node,
    IReadOnlyDictionary<string, EctdLeaf[]> leavesBySection)
{
    leavesBySection.TryGetValue(node.SectionPath, out var leaves);
    var childElements = node.Children
        .Select(child => BuildSectionElement(child, leavesBySection))
        .Where(child => child is not null)
        .Cast<XElement>()
        .ToArray();

    if ((leaves is null || leaves.Length == 0) && childElements.Length == 0)
    {
        return null;
    }

    var element = new XElement(node.ElementName);
    if (leaves is not null)
    {
        foreach (var leaf in leaves)
        {
            element.Add(BuildLeafElement(leaf));
        }
    }

    element.Add(childElements);
    return element;
}

private static XElement BuildLeafElement(EctdLeaf leaf)
{
    var attributes = new List<object>
    {
        new XAttribute("ID", leaf.LeafId),
        new XAttribute("operation", leaf.Operation),
        new XAttribute("checksum", leaf.Sha256),
        new XAttribute("checksum-type", "sha256"),
        new XAttribute(XlinkNamespace + "type", "simple"),
        new XAttribute(XlinkNamespace + "href", BuildRegionalHref(leaf.Href))
    };

    if (leaf.Lifecycle is not null)
    {
        attributes.Add(new XAttribute("modified-file", BuildModifiedFileHref(leaf.Lifecycle)));
    }

    return new XElement("leaf",
        attributes,
        new XElement("title", leaf.Title));
}

private static string BuildRegionalHref(string sequenceRootHref)
{
    const string module1Prefix = "m1/us/";
    return sequenceRootHref.StartsWith(module1Prefix, StringComparison.OrdinalIgnoreCase)
        ? sequenceRootHref[module1Prefix.Length..]
        : sequenceRootHref;
}

private static string BuildModifiedFileHref(EctdLifecycleReference lifecycle)
    => $"../../../{lifecycle.TargetSequenceNumber}/{lifecycle.ModifiedFileHref}";

private static void ValidateLeaves(EctdSequencePackage package)
{
    foreach (var leaf in package.Module1Leaves)
    {
        if (!string.Equals(leaf.Module, "m1", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsRegionalXmlSectionMappingException(
                package.ApplicationId,
                package.SequenceNumber,
                leaf.PlacementId,
                leaf.CtdSection,
                "leaf is not a Module 1 leaf");
        }
    }
}

private sealed record IndexedLeaf(EctdLeaf Leaf, int Index);
```

- [ ] **Step 4: Run writer tests to verify GREEN**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests
```

Expected: PASS.

- [ ] **Step 5: Commit M1 leaf mapping**

Run:

```powershell
git add src\RATools.Application\Publishing\UsRegional
git add -f tests\RATools.Tests\Publishing\UsRegional\UsRegionalXmlWriterTests.cs
git commit -m "feat: map Module 1 leaves into US regional XML"
```

## Task 6: Add Mapping Errors, Form Handling, and DI Registration

**Files:**
- Modify: `tests/RATools.Tests/Publishing/UsRegional/UsRegionalXmlWriterTests.cs`
- Modify: `src/RATools.Application/Publishing/UsRegional/UsRegionalXmlWriter.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`

- [ ] **Step 1: Add failing error/form/DI tests**

Append to `UsRegionalXmlWriterTests`:

```csharp
[Fact]
public void Write_ThrowsForUnknownModule1Section()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves: [CreateLeaf("m1.999", "leaf-44444444444444444444444444444444", "bad.pdf")]);

    var exception = Assert.Throws<UsRegionalXmlSectionMappingException>(() => writer.Write(package));

    Assert.Equal(package.ApplicationId, exception.ApplicationId);
    Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
    Assert.Equal("m1.999", exception.CtdSection);
    Assert.Equal("section is not in the supported US Regional M1 profile", exception.Reason);
}

[Fact]
public void Write_ThrowsForUnsupportedAttributeHeavySection()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves: [CreateLeaf("m1.15.2.1.1", "leaf-55555555555555555555555555555555", "promo.pdf")]);

    var exception = Assert.Throws<UsRegionalXmlSectionMappingException>(() => writer.Write(package));

    Assert.Equal("m1.15.2.1.1", exception.CtdSection);
    Assert.Equal("section requires unsupported regional attributes", exception.Reason);
}

[Fact]
public void Write_EmitsM1FormsInsideAdminFormElement()
{
    var writer = new UsRegionalXmlWriter();
    var package = CreatePackage(module1Leaves: [CreateLeaf("m1.1", "leaf-66666666666666666666666666666666", "form-356h.pdf")]);

    var xml = writer.Write(package).XmlContent;

    Assert.Contains("<form form-type=\"356h\"><leaf", xml, StringComparison.Ordinal);
    Assert.DoesNotContain("<m1-1-forms>", xml, StringComparison.Ordinal);
}

[Fact]
public void AddApplication_RegistersUsRegionalXmlWriter()
{
    var services = new ServiceCollection();

    services.AddApplication();

    var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IUsRegionalXmlWriter));
    Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    Assert.Equal(typeof(UsRegionalXmlWriter), descriptor.ImplementationType);
}
```

Add missing usings if needed:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
```

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests
```

Expected: FAIL because section errors, form placement, or DI registration is not complete.

- [ ] **Step 3: Complete section validation**

Update `ValidateLeaves` in `UsRegionalXmlWriter`:

```csharp
private static void ValidateLeaves(EctdSequencePackage package)
{
    foreach (var leaf in package.Module1Leaves)
    {
        if (!string.Equals(leaf.Module, "m1", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsRegionalXmlSectionMappingException(
                package.ApplicationId,
                package.SequenceNumber,
                leaf.PlacementId,
                leaf.CtdSection,
                "leaf is not a Module 1 leaf");
        }

        if (!UsRegionalM1V33.TryFind(leaf.CtdSection, out var node))
        {
            throw new UsRegionalXmlSectionMappingException(
                package.ApplicationId,
                package.SequenceNumber,
                leaf.PlacementId,
                leaf.CtdSection,
                "section is not in the supported US Regional M1 profile");
        }

        if (node.RequiresUnsupportedAttributes)
        {
            throw new UsRegionalXmlSectionMappingException(
                package.ApplicationId,
                package.SequenceNumber,
                leaf.PlacementId,
                leaf.CtdSection,
                "section requires unsupported regional attributes");
        }

        if (!node.AcceptsLeaves && !string.Equals(node.SectionPath, "m1.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsRegionalXmlSectionMappingException(
                package.ApplicationId,
                package.SequenceNumber,
                leaf.PlacementId,
                leaf.CtdSection,
                "section does not directly accept leaves");
        }
    }
}
```

- [ ] **Step 4: Emit m1.1 leaves inside admin form**

In `BuildAdminElement`, add form leaves:

```csharp
var formLeaves = package.Module1Leaves
    .Where(x => string.Equals(x.CtdSection, "m1.1", StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (!string.IsNullOrWhiteSpace(metadata.FormType) || formLeaves.Length > 0)
{
    Require(package, nameof(metadata.FormType), metadata.FormType);
    submissionInformationChildren.Add(new XElement("form",
        new XAttribute("form-type", metadata.FormType!),
        formLeaves.Select(BuildLeafElement)));
}
```

In `BuildM1RegionalElement`, exclude `m1.1` from regional section grouping:

```csharp
var leavesBySection = package.Module1Leaves
    .Where(leaf => !string.Equals(leaf.CtdSection, "m1.1", StringComparison.OrdinalIgnoreCase))
    .Select((leaf, index) => new IndexedLeaf(leaf, index))
    .GroupBy(x => x.Leaf.CtdSection, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(
        x => x.Key,
        x => x.OrderBy(leaf => leaf.Index).ThenBy(leaf => leaf.Leaf.LeafId, StringComparer.OrdinalIgnoreCase).Select(leaf => leaf.Leaf).ToArray(),
        StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 5: Register writer in DI**

Update `src/RATools.Application/DependencyInjection.cs`:

```csharp
using RATools.Application.Publishing.UsRegional;
```

Add near the ICH writer registration:

```csharp
services.AddSingleton<IUsRegionalXmlWriter, UsRegionalXmlWriter>();
```

- [ ] **Step 6: Run focused tests to verify GREEN**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional.UsRegionalXmlWriterTests
```

Expected: PASS.

- [ ] **Step 7: Commit errors/forms/DI**

Run:

```powershell
git add src\RATools.Application\Publishing\UsRegional src\RATools.Application\DependencyInjection.cs
git add -f tests\RATools.Tests\Publishing\UsRegional\UsRegionalXmlWriterTests.cs
git commit -m "feat: complete US regional XML writer behavior"
```

## Task 7: Full Verification

**Files:**
- Inspect all changed files.

- [ ] **Step 1: Run focused US regional tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.UsRegional
```

Expected: PASS.

- [ ] **Step 2: Run package and ICH regression tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter "FullyQualifiedName~RATools.Tests.Publishing.PackageModel|FullyQualifiedName~RATools.Tests.Publishing.Ich"
```

Expected: PASS.

- [ ] **Step 3: Run backend test suite**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj
```

Expected: PASS.

- [ ] **Step 4: Run frontend test suite**

Run from `frontend`:

```powershell
npm.cmd test
```

Expected: PASS. Existing React/AntD deprecation warnings may appear, but there must be zero failing tests.

- [ ] **Step 5: Review final diff and history**

Run:

```powershell
git status --short
git log --oneline -12
git diff --stat HEAD
```

Expected: worktree is clean after commits; recent commits include US regional package metadata, writer contract, admin/root XML, M1 section map, M1 leaf behavior, and this plan/spec.

## Self-Review

- Spec coverage: tasks cover package metadata, writer contract, root shape, DTD system id, namespaces, required `admin`, empty M1 behavior, DTD-derived section map with guard coverage, M1 section ordering, leaf attributes, relative href conversion, lifecycle `modified-file`, `m1.1` form handling, prototype element exclusion, deterministic XML, mapping errors, metadata errors, DI registration, and backend/frontend verification.
- Scope: plan does not replace `BackboneService`, write files, generate `index-md5.txt`, copy standards assets, create zip packages, add frontend metadata editing, or add full DTD validation.
- Type consistency: namespaces, method signatures, result record, exception properties, and test paths match the approved design and current package model style.
- Risk note: current persisted/API metadata does not yet include applicant contact, telephone, or email. The writer fails fast on blank package metadata rather than inventing values. A later metadata/API/UI task is still required before publish orchestration can produce successful real M1 XML from ordinary user-entered application data.
