namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 与 <see cref="FactAttribute"/> 等价，但没有可用 PostgreSQL 时把用例标记为 Skip。
/// Skip 在构造时（即发现阶段）决定。CI 上不会走到 Skip 分支，
/// 且 <see cref="PostgresGateTests"/> 会把"CI 上被跳过"变成硬失败。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (!PostgresTestEnvironment.IsAvailable)
        {
            Skip = PostgresTestEnvironment.SkipReason;
        }
    }
}
