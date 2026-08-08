namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 真实 PostgreSQL 用例的运行条件。数据库由外部提供（CI 是 runner 的 service container，
/// 开发机是 docker-compose 起的那个实例），测试进程只负责连上去——
/// 不让测试进程自己起容器：那要求进程能直连 Docker daemon，socket 文件存在也不代表
/// daemon 可用，且在无 Docker 的开发机上永远无法自验。
/// </summary>
internal static class PostgresTestEnvironment
{
    /// <summary>
    /// 指向一个可用 PostgreSQL 的连接串。目标库会被 <c>Migrate()</c>，请用专用测试库。
    /// </summary>
    public const string ConnectionStringVariable = "RATOOLS_TEST_POSTGRES";

    public static string? ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ConnectionStringVariable);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static bool IsAvailable => ConnectionString is not null;

    /// <summary>
    /// 是否在 CI 上跑。GitHub Actions 会设 <c>CI</c> 与 <c>GITHUB_ACTIONS</c>。
    /// 用于 <see cref="PostgresGateTests"/>：CI 上这些用例被跳过必须是**硬失败**，
    /// 否则就是一道假门禁——绿灯只说明测试没跑。
    /// </summary>
    public static bool IsContinuousIntegration =>
        IsTruthy(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
        || IsTruthy(Environment.GetEnvironmentVariable("CI"));

    public const string SkipReason =
        $"未设置 {ConnectionStringVariable}，无可用 PostgreSQL；"
        + "本机跳过属预期行为（CI 由 service container 提供，必真跑）。";

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "1", StringComparison.Ordinal);
}
