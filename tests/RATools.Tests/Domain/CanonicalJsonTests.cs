using System.Text;
using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Tests.Domain;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void CanonicalBytesAndDigestsMatchTheIndependentSharedContractVectors()
    {
        using var examples = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "contract-examples.json")));
        foreach (var vector in examples.RootElement.GetProperty("canonicalVectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            Assert.Equal(vector.GetProperty("canonical").GetString(), Encoding.UTF8.GetString(CanonicalJson.Encode(input)));
            Assert.Equal(vector.GetProperty("sha256").GetString(), CanonicalJson.Digest(input));
        }
        foreach (var package in examples.RootElement.GetProperty("packageInputs").EnumerateArray())
            Assert.Equal(package.GetProperty("digest").GetString(), CanonicalJson.Digest(package.GetProperty("content")));
        var baseline = examples.RootElement.GetProperty("historyBaseline");
        Assert.Equal(baseline.GetProperty("digest").GetString(), CanonicalJson.Digest(baseline.GetProperty("content")));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("9007199254740992")]
    [InlineData("-9007199254740992")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public void UnsupportedNumbersAndDuplicateMembersAreRejected(string input)
    {
        using var json = JsonDocument.Parse(input);
        Assert.Throws<ArgumentException>(() => CanonicalJson.Encode(json.RootElement));
    }

    [Fact]
    public void InvalidUnicodeIsNeverSilentlyReplacedInADigest()
    {
        using var json = JsonDocument.Parse("\"\\ud800\"");
        Assert.Throws<InvalidOperationException>(() => CanonicalJson.Encode(json.RootElement));
    }

    [Fact]
    public void FieldOrderDoesNotMatterButArrayOrderAndExactStringsDo()
    {
        using var first = JsonDocument.Parse("{\"z\":\"Alpha\",\"a\":[1,2]}");
        using var reordered = JsonDocument.Parse("{\"a\":[1,2],\"z\":\"Alpha\"}");
        using var arrayChanged = JsonDocument.Parse("{\"a\":[2,1],\"z\":\"Alpha\"}");
        using var caseChanged = JsonDocument.Parse("{\"a\":[1,2],\"z\":\"alpha\"}");
        Assert.Equal(CanonicalJson.Digest(first.RootElement), CanonicalJson.Digest(reordered.RootElement));
        Assert.NotEqual(CanonicalJson.Digest(first.RootElement), CanonicalJson.Digest(arrayChanged.RootElement));
        Assert.NotEqual(CanonicalJson.Digest(first.RootElement), CanonicalJson.Digest(caseChanged.RootElement));
    }
}
