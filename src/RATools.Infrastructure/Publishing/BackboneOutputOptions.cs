namespace RATools.Infrastructure.Publishing;

public sealed class BackboneOutputOptions
{
    public const string SectionName = "BackboneOutput";

    public string RootPath { get; set; } = "App_Data/publish";
}
