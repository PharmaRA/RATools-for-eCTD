using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RATools.Tests.Api;

public sealed class DocumentsApiSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DocumentsApiSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [Trait("Category", "PathSecurity")]
    [InlineData("../outside.pdf")]
    [InlineData("C:\\outside.pdf")]
    [InlineData("\\\\server\\share\\outside.pdf")]
    [InlineData("/var/tmp/outside.pdf")]
    public async Task RawCreateEndpoint_CannotRegisterClientSuppliedPathOrHashes(string storagePath)
    {
        using var tempRoot = new TemporaryDirectory();
        using var client = CreateClient(tempRoot.Path);

        var response = await client.PostAsJsonAsync("/api/documents", new
        {
            FileName = "outside.pdf",
            MediaType = "application/pdf",
            FileSize = 1,
            Sha256 = new string('0', 64),
            Md5 = new string('0', 32),
            StoragePath = storagePath
        });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var documents = await client.GetFromJsonAsync<DocumentResponse[]>("/api/documents");
        Assert.NotNull(documents);
        Assert.Empty(documents);
    }

    [Fact]
    public async Task UploadEndpoint_DerivesStorageMetadataFromStoredContent()
    {
        using var tempRoot = new TemporaryDirectory();
        using var client = CreateClient(tempRoot.Path);
        var bytes = Encoding.UTF8.GetBytes("server generated metadata");

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "evidence.pdf");

        var response = await client.PostAsync("/api/documents/upload", form);
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(document);
        Assert.Equal("evidence.pdf", document!.FileName);
        Assert.Equal("application/pdf", document.MediaType);
        Assert.Equal(bytes.LongLength, document.FileSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), document.Sha256);
        Assert.Equal(Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), document.Md5);
        Assert.True(IsInside(document.StoragePath, tempRoot.Path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(document.StoragePath));
    }

    private HttpClient CreateClient(string storageRoot)
    {
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Persistence:Provider", "InMemory");
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.Sources.Clear();
                    configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory",
                        ["Swagger:Enabled"] = "false",
                        ["Security:ApiKey"] = "test-key",
                        ["Security:AllowedWorkspaceRoots:0"] = storageRoot,
                        ["FileStorage:RootPath"] = storageRoot,
                        ["BackboneOutput:RootPath"] = storageRoot
                    });
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
        return client;
    }

    private static bool IsInside(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DocumentResponse(
        Guid Id,
        string FileName,
        string MediaType,
        long FileSize,
        string Sha256,
        string Md5,
        string StoragePath);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-documents-api-{Guid.NewGuid():N}");
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
