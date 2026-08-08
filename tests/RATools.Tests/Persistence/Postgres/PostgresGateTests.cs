namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 防假门禁：真实 PostgreSQL 用例靠 <see cref="RequiresPostgresFactAttribute"/> 在缺少数据库时
/// Skip，而 Skip 不会让测试运行变红——一旦 CI 上的 service container 或连接串配置被改坏、删掉，
/// 那 6 条约束用例会集体静默跳过，CI 照样全绿，等于门禁形同虚设。
/// 这条守卫把"CI 上跳过"变成硬失败。
/// </summary>
public sealed class PostgresGateTests
{
    [Fact]
    public void PostgresBackedTests_MustActuallyRunOnCi()
    {
        if (!PostgresTestEnvironment.IsContinuousIntegration)
        {
            // 开发机没有测试库是正常的，跳过是预期行为，这里不设要求。
            return;
        }

        Assert.True(
            PostgresTestEnvironment.IsAvailable,
            $"CI 上必须提供真实 PostgreSQL：未读到 {PostgresTestEnvironment.ConnectionStringVariable}。"
            + " 检查 .github/workflows/ci.yml 里 backend job 的 postgres service 与 Test step 的该环境变量"
            + "——缺了它约束用例会全部静默跳过，CI 仍然显示绿色。");
    }
}
