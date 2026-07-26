using System.Net;
using System.Net.Http.Json;
using System.ComponentModel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class SecurityBoundaryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityBoundaryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");

        var response = await client.GetAsync("/api/filesystem/directories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsUnauthorized_WhenApiKeyIsWrong()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "wrong-key");

        var response = await client.GetAsync("/api/filesystem/directories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FilesystemDirectories_ReachesController_WhenApiKeyIsCorrect()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync("/api/filesystem/directories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsBadRequest_WhenAllowedRootsAreEmpty()
    {
        var client = CreateClientWithoutAllowedRoots("local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync("/api/filesystem/directories");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsBadRequest_WhenPathIsOutsideAllowedRoot()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        var client = CreateClient(allowedRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync($"/api/filesystem/directories?path={Uri.EscapeDataString(outsideRoot.Path)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsAllowedRoots_WhenPathIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync("/api/filesystem/directories");
        var result = await response.Content.ReadFromJsonAsync<DirectoryBrowseResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Directories);
        Assert.Equal(Path.GetFileName(Path.TrimEndingDirectorySeparator(tempRoot.Path)), entry.Name);
        Assert.Equal(Path.GetFullPath(tempRoot.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), entry.FullPath);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsNullParentPath_WhenBrowsingAllowedRootWithDisallowedParent()
    {
        using var parentRoot = new TemporaryDirectory();
        var allowedRoot = Path.Combine(parentRoot.Path, "allowed");
        Directory.CreateDirectory(allowedRoot);
        var client = CreateClient(allowedRoot, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync($"/api/filesystem/directories?path={Uri.EscapeDataString(allowedRoot)}");
        var result = await response.Content.ReadFromJsonAsync<DirectoryBrowseResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Null(result.ParentPath);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsReparsePointChildAsInaccessible_WhenBrowsingAllowedRoot()
    {
        using var allowedRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(outsideRoot.Path, "outside-child"));
        var linkPath = Path.Combine(allowedRoot.Path, "link");
        if (!TryCreateDirectorySymlink(linkPath, outsideRoot.Path))
        {
            return;
        }

        var client = CreateClient(allowedRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync($"/api/filesystem/directories?path={Uri.EscapeDataString(allowedRoot.Path)}");
        var result = await response.Content.ReadFromJsonAsync<DirectoryBrowseResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Directories);
        Assert.Equal("link", entry.Name);
        Assert.Equal(Path.GetFullPath(linkPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), entry.FullPath);
        Assert.False(entry.CanBrowse);
        Assert.False(entry.HasChildren);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsConfiguredReparsePointRootAsInaccessible_WhenBrowsingRoots()
    {
        using var tempRoot = new TemporaryDirectory();
        using var outsideRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(outsideRoot.Path, "outside-child"));
        var linkRoot = Path.Combine(tempRoot.Path, "link-root");
        if (!TryCreateDirectorySymlink(linkRoot, outsideRoot.Path))
        {
            return;
        }

        var client = CreateClient(linkRoot, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync("/api/filesystem/directories");
        var result = await response.Content.ReadFromJsonAsync<DirectoryBrowseResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Directories);
        Assert.Equal("link-root", entry.Name);
        Assert.Equal(Path.GetFullPath(linkRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), entry.FullPath);
        Assert.False(entry.CanBrowse);
        Assert.False(entry.HasChildren);
    }

    [Fact]
    public async Task FilesystemResolveDirectory_PreservesLeadingSpaceInAllowedPath()
    {
        using var tempRoot = new TemporaryDirectory();
        var spacedRootName = " root";
        var spacedRootPath = Path.Combine(tempRoot.Path, spacedRootName);
        Directory.CreateDirectory(spacedRootPath);

        var client = CreateClient(spacedRootPath, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.PostAsJsonAsync("/api/filesystem/resolve-directory", new { Path = spacedRootPath });
        var result = await response.Content.ReadFromJsonAsync<DirectoryResolutionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(spacedRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), result.FullPath);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsMissingAllowedRootAsInaccessibleEntry_WhenPathIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var missingRoot = Path.Combine(tempRoot.Path, "missing-root");
        var client = CreateClient(missingRoot, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");

        var response = await client.GetAsync("/api/filesystem/directories");
        var result = await response.Content.ReadFromJsonAsync<DirectoryBrowseResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        var entry = Assert.Single(result.Directories);
        var normalizedMissingRoot = Path.GetFullPath(missingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.Equal("missing-root", entry.Name);
        Assert.Equal(normalizedMissingRoot, entry.FullPath);
        Assert.False(entry.CanBrowse);
        Assert.False(entry.HasChildren);
    }

    [Fact]
    public async Task FilesystemDirectories_ReturnsUnauthorized_WhenApiKeyHasDuplicateValues()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", ["local-dev-key", "wrong-key"]);

        var response = await client.GetAsync("/api/filesystem/directories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ImportApplication_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");

        var response = await client.PostAsJsonAsync("/api/applications/import", new
        {
            WorkingDirectoryPath = tempRoot.Path,
            EctdTemplateKey = "us-fda-ectd-32",
            SponsorName = "Sponsor"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateApplication_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");

        var response = await client.PostAsJsonAsync("/api/applications", new
        {
            ApplicationNumber = "app-001",
            EctdTemplateKey = "us-fda-ectd-32",
            SponsorName = "Sponsor",
            WorkingDirectoryParentPath = tempRoot.Path
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteApplication_ReturnsUnauthorized_WhenPurgeWorkspaceApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}?deleteMode=PurgeWorkspace");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSequence_ReturnsUnauthorized_WhenPurgeWorkspaceApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}/sequences/0001?deleteMode=PurgeWorkspace");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteApplication_ReturnsUnauthorized_WhenPurgeIsRequestedWithValidKeyButDestructiveOperationsDisabled()
    {
        // 默认 Security:AllowDestructiveOperations=false：即使 API Key 正确，
        // purge 也必须被拒——这是 purge 与普通读写的唯一权限差别。
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}?deleteMode=PurgeWorkspace");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteApplication_ReachesService_WhenPurgeIsRequestedAndDestructiveOperationsEnabled()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key", allowDestructiveOperations: true);
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}?deleteMode=PurgeWorkspace");

        // 授权通过后到达服务层；随机 id 不存在 → 404（而非 401）。
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteApplication_DatabaseOnlyStillWorks_WhenDestructiveOperationsDisabled()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}?deleteMode=DatabaseOnly");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteApplication_ReturnsUnauthorized_WhenDatabaseOnlyApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}?deleteMode=DatabaseOnly");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSequence_ReturnsUnauthorized_WhenDatabaseOnlyApiKeyIsMissing()
    {
        using var tempRoot = new TemporaryDirectory();
        var client = CreateClient(tempRoot.Path, "local-dev-key");
        var id = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/applications/{id}/sequences/0001?deleteMode=DatabaseOnly");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient CreateClient(string allowedRoot, string apiKey, bool allowDestructiveOperations = false)
    {
        return _factory.WithWebHostBuilder(builder =>
            {
                // AllowDestructiveOperations 与 Persistence:Provider 都在服务注册阶段被读取，
                // ConfigureAppConfiguration 的覆盖那时尚未合并，必须走 UseSetting。
                // （Provider 不走 UseSetting 时会注册 Npgsql，开发机上恰好有本地 PG 会假绿，CI 上 500。）
                builder.UseSetting("Security:AllowDestructiveOperations", allowDestructiveOperations ? "true" : "false");
                builder.UseSetting("Persistence:Provider", "InMemory");
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.Sources.Clear();
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = apiKey,
                        ["Security:AllowedWorkspaceRoots:0"] = allowedRoot,
                        ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-uploads"),
                        ["BackboneOutput:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-publish"),
                        ["ValidationProfile:Name"] = "fda-ectd-3.2-manual",
                        ["ValidationProfile:Mode"] = "relaxed"
                    });
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
    }

    private HttpClient CreateClientWithoutAllowedRoots(string apiKey)
    {
        return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.Sources.Clear();
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = apiKey,
                        ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-uploads"),
                        ["BackboneOutput:RootPath"] = Path.Combine(Path.GetTempPath(), "ratools-test-publish"),
                        ["ValidationProfile:Name"] = "fda-ectd-3.2-manual",
                        ["ValidationProfile:Mode"] = "relaxed"
                    });
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
    }

    private sealed record DirectoryBrowseResponse(string? ParentPath, DirectoryBrowseEntryResponse[] Directories);

    private sealed record DirectoryBrowseEntryResponse(string Name, string FullPath, bool CanBrowse, bool HasChildren);

    private sealed record DirectoryResolutionResponse(string FullPath, bool IsAccessible);

    private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or Win32Exception)
        {
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-security-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
