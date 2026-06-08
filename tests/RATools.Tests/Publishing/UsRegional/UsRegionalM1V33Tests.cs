using System.Text.RegularExpressions;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class UsRegionalM1V33Tests
{
    [Theory]
    [InlineData("m1.2", "m1-2-cover-letters")]
    [InlineData("m1.14.2.3", "m1-14-2-3-final-labeling-text")]
    [InlineData("m1.16.2.1", "m1-16-2-1-final-rems")]
    [InlineData("m1.17.1", "m1-17-1-correspondence-regarding-postmarketing-commitments")]
    public void TryFind_ReturnsKnownDtdElementName(string sectionPath, string expectedElementName)
    {
        var found = UsRegionalM1V33.TryFind(sectionPath, out var node);

        Assert.True(found);
        Assert.NotNull(node);
        Assert.Equal(expectedElementName, node!.ElementName);
    }

    [Fact]
    public void TryFind_DoesNotMarkM117ParentAsLeafAccepting()
    {
        var found = UsRegionalM1V33.TryFind("m1.17", out var node);

        Assert.True(found);
        Assert.NotNull(node);
        Assert.False(node!.AcceptsLeaves);
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
