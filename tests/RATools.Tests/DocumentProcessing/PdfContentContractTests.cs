using System.Text.Json;
using RATools.Application.DocumentProcessing;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfContentContractTests
{
    private static readonly int[] ExpectedValues1 = new[] { 1, 3 };

    [Fact]
    public void ContentParametersSurviveTheDurableParameterContract()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var parameters = JsonSerializer.Deserialize<PdfProcessingParameters>("""
            {"pageMode":null,"bookmarks":null,"links":null,"content":{"ocr":{"language":"eng","pages":[1,3],"dpi":150},"embedStandardFonts":false,"targetVersion":"1.7"}}
            """, options)!;
        using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(parameters.Normalize(), options));
        Assert.True(roundTrip.RootElement.TryGetProperty("content", out var content));
        Assert.Equal("eng", content.GetProperty("ocr").GetProperty("language").GetString());
        Assert.Equal(ExpectedValues1, content.GetProperty("ocr").GetProperty("pages").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal("1.7", content.GetProperty("targetVersion").GetString());
    }
}
