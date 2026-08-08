namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 与 <see cref="FactAttribute"/> 等价，但在没有 Docker 的机器上把用例标记为 Skip。
/// Skip 在构造时（即发现阶段）决定，因此无 Docker 时容器根本不会被启动。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!PostgresTestEnvironment.IsAvailable)
        {
            Skip = PostgresTestEnvironment.SkipReason;
        }
    }
}
