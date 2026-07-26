namespace RATools.Infrastructure.Publishing;

public sealed class BackboneOutputOptions
{
    public const string SectionName = "BackboneOutput";

    public string RootPath { get; set; } = "App_Data/publish";

    /// <summary>
    /// 每个 application/sequence 保留的 _jobs 交付副本份数。每次发布都会在
    /// _jobs/{jobId} 下产生一份完整包副本且从不清理，磁盘会线性耗尽；
    /// 只清理 _jobs（工作副本），_artifacts 与 _packages 是交付物不动。
    /// 小于等于 0 表示不清理。
    /// </summary>
    public int RetainJobRuns { get; set; } = 5;
}
