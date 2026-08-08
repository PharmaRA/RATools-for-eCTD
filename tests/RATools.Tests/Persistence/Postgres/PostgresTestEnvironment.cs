namespace RATools.Tests.Persistence.Postgres;

/// <summary>
/// 判定本机能否跑 Testcontainers。开发机通常没有 Docker（本仓库的 Windows 开发机就没有），
/// 这时相关用例 Skip 属于**预期行为**；CI 的 ubuntu runner 自带 Docker，必须真跑。
/// 探测必须快且绝不抛异常——它在 xunit 的**发现阶段**执行，抛异常会让整个测试程序集无法枚举。
/// </summary>
internal static class PostgresTestEnvironment
{
    private const string WindowsDockerPipe = "docker_engine";
    private const string UnixDockerSocket = "/var/run/docker.sock";

    /// <summary>
    /// 设了这个环境变量就直接连指定的 PostgreSQL，不起容器——
    /// 给"有本机 PG 但没装 Docker"的开发机一条自证路径（库会被迁移，请用专用测试库）。
    /// </summary>
    public const string ExternalConnectionStringVariable = "RATOOLS_TEST_POSTGRES";

    private static readonly Lazy<bool> DockerAvailable = new(Detect, isThreadSafe: true);

    public static string? ExternalConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ExternalConnectionStringVariable);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static bool IsDockerAvailable => DockerAvailable.Value;

    /// <summary>
    /// 能跑真实 PostgreSQL 用例：显式连接串优先，否则要求本机有 Docker。
    /// </summary>
    public static bool IsAvailable => ExternalConnectionString is not null || IsDockerAvailable;

    /// <summary>
    /// 供 <see cref="RequiresDockerFactAttribute"/> 填进 Skip 的原因文案。
    /// </summary>
    public const string SkipReason =
        $"需要 Docker 起真实 PostgreSQL 容器，或用 {ExternalConnectionStringVariable} 指定现成实例；"
        + "本机两者皆无，跳过属预期行为（CI 上会真跑）。";

    private static bool Detect()
    {
        try
        {
            // 显式配置优先：CI 或远端 Docker 都通过 DOCKER_HOST 指定，此时不去猜默认端点。
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            {
                return true;
            }

            return OperatingSystem.IsWindows() ? WindowsDockerPipeExists() : File.Exists(UnixDockerSocket);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 探测不到就当没有：宁可 Skip，也不要让发现阶段失败。
            return false;
        }
    }

    /// <summary>
    /// Windows 上 Docker 引擎是命名管道而非文件，File.Exists 对管道路径不可靠，
    /// 因此枚举 <c>\\.\pipe\</c> 下的管道名来判断。
    /// </summary>
    private static bool WindowsDockerPipeExists()
    {
        foreach (var pipe in Directory.EnumerateFiles(@"\\.\pipe\"))
        {
            if (Path.GetFileName(pipe).Equals(WindowsDockerPipe, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
