# ICH index.xml Writer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a tested `IIchIndexXmlWriter` that converts `EctdSequencePackage.IchBackboneLeaves` into deterministic ICH eCTD v3.2.2 `index.xml` content.

**Architecture:** Add a focused `RATools.Application.Publishing.Ich` module that consumes the package model and static FDA section profile data. The writer produces an `XDocument` and serialized XML string only; it does not replace `BackboneService`, write files, create MD5 manifests, or generate US Regional Module 1 XML.

**Tech Stack:** .NET 8, LINQ to XML, xUnit, existing `RATools.Application.Publishing.PackageModel`, existing `RATools.Application.Validation.Profiles.FdaEctd322`.

---

## File Structure

- Create `src/RATools.Application/Publishing/Ich/IIchIndexXmlWriter.cs`
  - Writer service contract.
- Create `src/RATools.Application/Publishing/Ich/IchIndexXmlWriteResult.cs`
  - Result record holding `index.xml`, `XDocument`, and serialized XML.
- Create `src/RATools.Application/Publishing/Ich/IchIndexXmlWriterException.cs`
  - Base writer exception and section mapping exception.
- Create `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`
  - XML writer implementation.
- Modify `src/RATools.Application/DependencyInjection.cs`
  - Register `IIchIndexXmlWriter` as singleton.
- Create `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
  - Unit tests for root shape, section mapping, leaf attributes, lifecycle, M1 exclusion, deterministic output, and DI.

## Task 1: Add Writer Contract

**Files:**
- Create: `src/RATools.Application/Publishing/Ich/IIchIndexXmlWriter.cs`
- Create: `src/RATools.Application/Publishing/Ich/IchIndexXmlWriteResult.cs`
- Create: `src/RATools.Application/Publishing/Ich/IchIndexXmlWriterException.cs`
- Test: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`

- [ ] **Step 1: Write the failing contract test**

Create `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`:

```csharp
using System.Xml.Linq;
using RATools.Application.Publishing.Ich;

namespace RATools.Tests.Publishing.Ich;

public sealed class IchIndexXmlWriterTests
{
    [Fact]
    public void WriteResult_ExposesExpectedContract()
    {
        var document = new XDocument(new XElement("root"));

        var result = new IchIndexXmlWriteResult("index.xml", document, "<root />");

        Assert.Equal("index.xml", result.FileName);
        Assert.Same(document, result.Document);
        Assert.Equal("<root />", result.XmlContent);
    }
}
```

- [ ] **Step 2: Run focused test to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests.WriteResult_ExposesExpectedContract
```

Expected: FAIL at compile time because `RATools.Application.Publishing.Ich` does not exist.

- [ ] **Step 3: Add minimal contract files**

Create `src/RATools.Application/Publishing/Ich/IIchIndexXmlWriter.cs`:

```csharp
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.Ich;

public interface IIchIndexXmlWriter
{
    IchIndexXmlWriteResult Write(EctdSequencePackage package);
}
```

Create `src/RATools.Application/Publishing/Ich/IchIndexXmlWriteResult.cs`:

```csharp
using System.Xml.Linq;

namespace RATools.Application.Publishing.Ich;

public sealed record IchIndexXmlWriteResult(
    string FileName,
    XDocument Document,
    string XmlContent);
```

Create `src/RATools.Application/Publishing/Ich/IchIndexXmlWriterException.cs`:

```csharp
namespace RATools.Application.Publishing.Ich;

public abstract class IchIndexXmlWriterException(string message) : Exception(message);

public sealed class IchIndexXmlSectionMappingException(
    Guid applicationId,
    string sequenceNumber,
    Guid? placementId,
    string? ctdSection,
    string reason)
    : IchIndexXmlWriterException($"Unable to map CTD section '{ctdSection ?? "(none)"}' in sequence {sequenceNumber}: {reason}.")
{
    public Guid ApplicationId { get; } = applicationId;

    public string SequenceNumber { get; } = sequenceNumber;

    public Guid? PlacementId { get; } = placementId;

    public string? CtdSection { get; } = ctdSection;

    public string Reason { get; } = reason;
}
```

- [ ] **Step 4: Run focused test to verify it passes**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests.WriteResult_ExposesExpectedContract
```

Expected: PASS.

- [ ] **Step 5: Commit contract**

Run:

```powershell
git add src\RATools.Application\Publishing\Ich
git add -f tests\RATools.Tests\Publishing\Ich\IchIndexXmlWriterTests.cs
git commit -m "feat: add ICH index XML writer contract"
```

## Task 2: Generate Empty ICH Root Document

**Files:**
- Modify: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
- Create: `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`

- [ ] **Step 1: Add failing root XML tests**

Append these tests and helper methods to `IchIndexXmlWriterTests`:

```csharp
using RATools.Application.Publishing.PackageModel;

[Fact]
public void Write_GeneratesEmptyIchRootWithDoctypeAndNamespaces()
{
    var writer = new IchIndexXmlWriter();
    var package = CreatePackage(ichLeaves: []);

    var result = writer.Write(package);

    Assert.Equal("index.xml", result.FileName);
    Assert.Equal("ectd", result.Document.Root?.Name.LocalName);
    Assert.Equal("http://www.ich.org/ectd", result.Document.Root?.Name.NamespaceName);
    Assert.Equal("3.2", result.Document.Root?.Attribute("dtd-version")?.Value);
    Assert.Equal("ectd:ectd", result.Document.DocumentType?.Name);
    Assert.Equal("util/dtd/ich-ectd-3-2.dtd", result.Document.DocumentType?.SystemId);
    Assert.Contains("""xmlns:ectd="http://www.ich.org/ectd"""", result.XmlContent, StringComparison.Ordinal);
    Assert.Contains("""xmlns:xlink="http://www.w3c.org/1999/xlink"""", result.XmlContent, StringComparison.Ordinal);
}

[Fact]
public void Write_ThrowsArgumentNullExceptionForNullPackage()
{
    var writer = new IchIndexXmlWriter();

    Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
}

private static EctdSequencePackage CreatePackage(
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
        new EctdSequenceMetadata("0001", "original-application", null, "Initial sequence", "Acme Pharma", "356h"),
        module1Leaves ?? [],
        ichLeaves ?? [],
        []);
}
```

- [ ] **Step 2: Run focused tests to verify they fail**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: FAIL at compile time because `IchIndexXmlWriter` does not exist.

- [ ] **Step 3: Implement minimal root writer**

Create `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`:

```csharp
using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;

namespace RATools.Application.Publishing.Ich;

public sealed class IchIndexXmlWriter : IIchIndexXmlWriter
{
    private static readonly XNamespace EctdNamespace = "http://www.ich.org/ectd";
    private static readonly XNamespace XlinkNamespace = "http://www.w3c.org/1999/xlink";

    public IchIndexXmlWriteResult Write(EctdSequencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var root = new XElement(EctdNamespace + "ectd",
            new XAttribute(XNamespace.Xmlns + "ectd", EctdNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", XlinkNamespace.NamespaceName),
            new XAttribute("dtd-version", "3.2"));
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XDocumentType("ectd:ectd", null, "util/dtd/ich-ectd-3-2.dtd", null),
            root);

        return new IchIndexXmlWriteResult("index.xml", document, document.ToString(SaveOptions.DisableFormatting));
    }
}
```

- [ ] **Step 4: Run focused tests to verify they pass**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: PASS.

- [ ] **Step 5: Commit root writer**

Run:

```powershell
git add src\RATools.Application\Publishing\Ich
git add -f tests\RATools.Tests\Publishing\Ich\IchIndexXmlWriterTests.cs
git commit -m "feat: generate ICH index XML root"
```

## Task 3: Map ICH Leaves Into Profile-Derived Sections

**Files:**
- Modify: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
- Modify: `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`

- [ ] **Step 1: Add failing section mapping tests**

Append these tests and helper methods:

```csharp
[Fact]
public void Write_MapsIchLeavesToDtdSectionElements()
{
    var writer = new IchIndexXmlWriter();
    var package = CreatePackage(ichLeaves:
    [
        CreateLeaf("m5.3.5.1", "leaf-00000000000000000000000000000005", "clinical.pdf"),
        CreateLeaf("m3.2", "leaf-00000000000000000000000000000003", "quality.pdf"),
        CreateLeaf("m2", "leaf-00000000000000000000000000000002", "summary.pdf"),
        CreateLeaf("m4.2", "leaf-00000000000000000000000000000004", "nonclinical.pdf")
    ]);

    var result = writer.Write(package);
    var xml = result.XmlContent;

    Assert.Contains("<m2-common-technical-document-summaries>", xml, StringComparison.Ordinal);
    Assert.Contains("<m3-quality><m3-2-body-of-data>", xml, StringComparison.Ordinal);
    Assert.Contains("<m4-nonclinical-study-reports><m4-2-study-reports>", xml, StringComparison.Ordinal);
    Assert.Contains("<m5-clinical-study-reports><m5-3-clinical-study-reports><m5-3-5-reports-of-efficacy-and-safety-studies><m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication>", xml, StringComparison.Ordinal);
    Assert.True(xml.IndexOf("<m2-common", StringComparison.Ordinal) < xml.IndexOf("<m3-quality", StringComparison.Ordinal));
    Assert.True(xml.IndexOf("<m3-quality", StringComparison.Ordinal) < xml.IndexOf("<m4-nonclinical", StringComparison.Ordinal));
    Assert.True(xml.IndexOf("<m4-nonclinical", StringComparison.Ordinal) < xml.IndexOf("<m5-clinical", StringComparison.Ordinal));
}

[Fact]
public void Write_IgnoresModule1Leaves()
{
    var writer = new IchIndexXmlWriter();
    var module1Leaf = CreateLeaf("m1.1", "leaf-00000000000000000000000000000001", "m1.pdf");
    var package = CreatePackage(module1Leaves: [module1Leaf], ichLeaves: []);

    var result = writer.Write(package);

    Assert.DoesNotContain("m1-administrative-information-and-prescribing-information", result.XmlContent, StringComparison.Ordinal);
    Assert.DoesNotContain("leaf-00000000000000000000000000000001", result.XmlContent, StringComparison.Ordinal);
}

private static EctdLeaf CreateLeaf(string ctdSection, string leafId, string fileName, string operation = "new", EctdLifecycleReference? lifecycle = null)
{
    return new EctdLeaf(
        Guid.Parse($"{leafId[5..13]}-{leafId[13..17]}-{leafId[17..21]}-{leafId[21..25]}-{leafId[25..37]}"),
        Guid.NewGuid(),
        leafId,
        "0001",
        ctdSection,
        ctdSection.Split('.')[0],
        operation,
        Path.GetFileNameWithoutExtension(fileName),
        $"{ctdSection.Replace('.', '/')}/{fileName}",
        fileName,
        "application/pdf",
        $"C:/workspace/0001/{ctdSection}/{fileName}",
        10,
        $"sha-{fileName}",
        lifecycle);
}
```

- [ ] **Step 2: Run focused tests to verify they fail**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: FAIL because writer returns only the empty root.

- [ ] **Step 3: Implement section tree mapping**

Update `IchIndexXmlWriter` to:

- build a lookup from `FdaEctd322.Root` section paths to nodes and ancestor chains;
- filter `package.IchBackboneLeaves`;
- reject non M2-M5 leaves that reach the ICH list;
- create profile-ordered section elements only where leaves exist.

Implementation sketch:

```csharp
using RATools.Application.Validation.Profiles;

private static readonly SectionPathNode[] IchTopLevelNodes = FdaEctd322.Root.Children
    .Where(x => x.SectionPath is "m2" or "m3" or "m4" or "m5")
    .Select(x => BuildSectionPathNode(x, []))
    .ToArray();

private static readonly IReadOnlyDictionary<string, SectionPathNode> SectionByPath =
    IchTopLevelNodes.SelectMany(Flatten).ToDictionary(x => x.SectionPath, StringComparer.OrdinalIgnoreCase);
```

Use a private record:

```csharp
private sealed record SectionPathNode(
    string ElementName,
    string SectionPath,
    IReadOnlyCollection<SectionPathNode> Children);
```

Add methods:

```csharp
private static SectionPathNode BuildSectionPathNode(SectionDictionaryManualNode node, IReadOnlyList<SectionDictionaryManualNode> ancestors)
{
    return new SectionPathNode(
        node.ElementName,
        node.SectionPath,
        node.Children.Select(child => BuildSectionPathNode(child, ancestors.Concat([node]).ToArray())).ToArray());
}

private static IEnumerable<SectionPathNode> Flatten(SectionPathNode node)
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
```

Build XML elements recursively:

```csharp
private static XElement? BuildSectionElement(SectionPathNode node, IReadOnlyDictionary<string, EctdLeaf[]> leavesBySection)
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
```

For this task, `BuildLeafElement` may emit minimal leafs:

```csharp
private static XElement BuildLeafElement(EctdLeaf leaf)
{
    return new XElement("leaf",
        new XAttribute("ID", leaf.LeafId),
        new XAttribute("operation", leaf.Operation),
        new XAttribute("checksum", leaf.Sha256),
        new XAttribute("checksum-type", "sha256"),
        new XAttribute(XlinkNamespace + "type", "simple"),
        new XAttribute(XlinkNamespace + "href", leaf.Href),
        new XElement("title", leaf.Title));
}
```

- [ ] **Step 4: Run focused tests to verify they pass**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: PASS.

- [ ] **Step 5: Commit section mapping**

Run:

```powershell
git add src\RATools.Application\Publishing\Ich
git add -f tests\RATools.Tests\Publishing\Ich\IchIndexXmlWriterTests.cs
git commit -m "feat: map ICH leaves into index XML sections"
```

## Task 4: Emit Complete Leaf Attributes and Errors

**Files:**
- Modify: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
- Modify: `src/RATools.Application/Publishing/Ich/IchIndexXmlWriter.cs`

- [ ] **Step 1: Add failing lifecycle/error/determinism tests**

Append these tests:

```csharp
[Fact]
public void Write_EmitsLeafAttributesAndLifecycleModifiedFile()
{
    var writer = new IchIndexXmlWriter();
    var lifecycle = new EctdLifecycleReference(Guid.NewGuid(), Guid.NewGuid(), "0000", "m3/32-body-of-data/old.pdf");
    var package = CreatePackage(ichLeaves:
    [
        CreateLeaf("m3.2", "leaf-11111111111111111111111111111111", "new.pdf", "replace", lifecycle)
    ]);

    var result = writer.Write(package);
    var leaf = result.Document.Descendants("leaf").Single();

    Assert.Equal("leaf-11111111111111111111111111111111", leaf.Attribute("ID")?.Value);
    Assert.Equal("replace", leaf.Attribute("operation")?.Value);
    Assert.Equal("sha-new.pdf", leaf.Attribute("checksum")?.Value);
    Assert.Equal("sha256", leaf.Attribute("checksum-type")?.Value);
    Assert.Equal("simple", leaf.Attribute(XName.Get("type", "http://www.w3c.org/1999/xlink"))?.Value);
    Assert.Equal("m3/2/new.pdf", leaf.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
    Assert.Equal("m3/32-body-of-data/old.pdf", leaf.Attribute("modified-file")?.Value);
    Assert.Equal("new", leaf.Element("title")?.Value);
}

[Fact]
public void Write_DoesNotEmitPrototypeOnlyLeafChildren()
{
    var writer = new IchIndexXmlWriter();
    var package = CreatePackage(ichLeaves: [CreateLeaf("m3.2", "leaf-22222222222222222222222222222222", "quality.pdf")]);

    var result = writer.Write(package);

    Assert.DoesNotContain("<fileName>", result.XmlContent, StringComparison.Ordinal);
    Assert.DoesNotContain("<mimeType>", result.XmlContent, StringComparison.Ordinal);
}

[Fact]
public void Write_ProducesStableXmlForRepeatedWrites()
{
    var writer = new IchIndexXmlWriter();
    var package = CreatePackage(ichLeaves:
    [
        CreateLeaf("m3.2", "leaf-33333333333333333333333333333333", "quality-a.pdf"),
        CreateLeaf("m3.2", "leaf-33333333333333333333333333333334", "quality-b.pdf")
    ]);

    var first = writer.Write(package).XmlContent;
    var second = writer.Write(package).XmlContent;

    Assert.Equal(first, second);
}

[Fact]
public void Write_ThrowsForUnknownIchSection()
{
    var writer = new IchIndexXmlWriter();
    var package = CreatePackage(ichLeaves: [CreateLeaf("m3.999", "leaf-44444444444444444444444444444444", "bad.pdf")]);

    var exception = Assert.Throws<IchIndexXmlSectionMappingException>(() => writer.Write(package));

    Assert.Equal(package.ApplicationId, exception.ApplicationId);
    Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
    Assert.Equal("m3.999", exception.CtdSection);
    Assert.Equal("section is not in the supported ICH profile", exception.Reason);
}
```

- [ ] **Step 2: Run focused tests to verify they fail**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: FAIL for unknown section if current implementation does not throw; lifecycle may already pass if Task 3 emitted `modified-file`.

- [ ] **Step 3: Complete leaf/error handling**

Update `Write()` before grouping leaves:

```csharp
foreach (var leaf in package.IchBackboneLeaves)
{
    if (leaf.Module is not ("m2" or "m3" or "m4" or "m5"))
    {
        throw new IchIndexXmlSectionMappingException(package.ApplicationId, package.SequenceNumber, leaf.PlacementId, leaf.CtdSection, "leaf is not an ICH M2-M5 leaf");
    }

    if (!SectionByPath.ContainsKey(leaf.CtdSection))
    {
        throw new IchIndexXmlSectionMappingException(package.ApplicationId, package.SequenceNumber, leaf.PlacementId, leaf.CtdSection, "section is not in the supported ICH profile");
    }
}
```

Update leaf grouping:

```csharp
var leavesBySection = package.IchBackboneLeaves
    .GroupBy(x => x.CtdSection, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(
        x => x.Key,
        x => x.OrderBy(leaf => leaf.LeafId, StringComparer.OrdinalIgnoreCase).ToArray(),
        StringComparer.OrdinalIgnoreCase);
```

Update `BuildLeafElement` to add lifecycle:

```csharp
if (leaf.Lifecycle is not null)
{
    attributes.Add(new XAttribute("modified-file", leaf.Lifecycle.ModifiedFileHref));
}
```

- [ ] **Step 4: Run focused tests to verify they pass**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: PASS.

- [ ] **Step 5: Commit leaf completion**

Run:

```powershell
git add src\RATools.Application\Publishing\Ich
git add -f tests\RATools.Tests\Publishing\Ich\IchIndexXmlWriterTests.cs
git commit -m "feat: emit ICH index XML leaf details"
```

## Task 5: Register Writer in Dependency Injection

**Files:**
- Modify: `tests/RATools.Tests/Publishing/Ich/IchIndexXmlWriterTests.cs`
- Modify: `src/RATools.Application/DependencyInjection.cs`

- [ ] **Step 1: Add failing DI test**

Append this test:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;

[Fact]
public void AddApplication_RegistersIchIndexXmlWriter()
{
    var services = new ServiceCollection();

    services.AddApplication();

    var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IIchIndexXmlWriter));
    Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    Assert.Equal(typeof(IchIndexXmlWriter), descriptor.ImplementationType);
}
```

- [ ] **Step 2: Run focused DI test to verify it fails**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests.AddApplication_RegistersIchIndexXmlWriter
```

Expected: FAIL because `IIchIndexXmlWriter` is not registered.

- [ ] **Step 3: Register writer**

Modify `src/RATools.Application/DependencyInjection.cs`:

```csharp
using RATools.Application.Publishing.Ich;
```

Add in `AddApplication()` near publishing registrations:

```csharp
services.AddSingleton<IIchIndexXmlWriter, IchIndexXmlWriter>();
```

- [ ] **Step 4: Run focused DI test to verify it passes**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests.AddApplication_RegistersIchIndexXmlWriter
```

Expected: PASS.

- [ ] **Step 5: Commit DI registration**

Run:

```powershell
git add src\RATools.Application\DependencyInjection.cs
git add -f tests\RATools.Tests\Publishing\Ich\IchIndexXmlWriterTests.cs
git commit -m "feat: register ICH index XML writer"
```

## Task 6: Full Verification

**Files:**
- Inspect all changed files.

- [ ] **Step 1: Run focused writer tests**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj --filter FullyQualifiedName~RATools.Tests.Publishing.Ich.IchIndexXmlWriterTests
```

Expected: PASS.

- [ ] **Step 2: Run backend test suite**

Run:

```powershell
dotnet test tests\RATools.Tests\RATools.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Run frontend test suite**

Run from `frontend`:

```powershell
npm test
```

Expected: PASS. Existing React/AntD deprecation warnings may appear, but there must be zero failing tests.

- [ ] **Step 4: Review final diff**

Run:

```powershell
git status --short
git diff --stat HEAD
git log --oneline -8
```

Expected: implementation commits include only ICH writer, DI registration, writer tests, and the plan/spec docs for this feature.

## Self-Review

- Spec coverage: tasks cover writer contract, root shape, DOCTYPE, namespaces, M2-M5 section mapping, M1 exclusion, leaf attributes, lifecycle `modified-file`, prototype element exclusion, deterministic output, section mapping errors, DI registration, and backend/frontend verification.
- Scope: plan does not replace `BackboneService`, write files, generate `us-regional.xml`, generate `index-md5.txt`, copy standards assets, create zip packages, or add full DTD validation.
- Type consistency: namespaces, method signatures, result record, exception properties, and test paths match the approved design and current package model types.
