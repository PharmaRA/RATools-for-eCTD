using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RATools.Application.PackageValidation;
using RATools.Tests.PackageValidation;

namespace RATools.Tests.Api;

public sealed class PackageInputPreflightApiTests
{
    [Fact]
    public async Task PreflightIsAuthenticatedExplicitAndReadableWithoutSuccessfulRegistration()
    {
        var root = RetainedPackageTestFiles.Create();
        try
        {
            var source = PackageInputPreflightTests.CreateInput(root, "empty-directory");
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Persistence:Provider", "InMemory");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.Sources.Clear();
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Persistence:Provider"] = "InMemory", ["Deployment:Mode"] = "LocalOnly", ["Security:ApiKey"] = "test-key",
                        ["Security:AllowedWorkspaceRoots:0"] = source, ["FileStorage:RootPath"] = Path.Combine(root, "uploads"),
                        ["BackboneOutput:RootPath"] = Path.Combine(root, "publish"), ["PackageValidation:StorageRoot"] = Path.Combine(root, "validation"),
                        ["PackageValidation:Enabled"] = "false"
                    });
                });
            });
            using var client = factory.CreateClient();
            var request = new { applicationId = Guid.NewGuid(), path = source, role = "Directory", profileSnapshotId = PackageValidationCatalog.UsProfile };
            using var unauthenticated = await client.PostAsJsonAsync("/api/package-validation-inputs/preflight", request);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            client.DefaultRequestHeaders.Add("X-RA-Tools-Api-Key", "test-key");
            using var missingRole = await client.PostAsJsonAsync("/api/package-validation-inputs/preflight", new { request.applicationId, request.path, request.profileSnapshotId });
            Assert.Equal(HttpStatusCode.BadRequest, missingRole.StatusCode);
            using var extra = await client.PostAsJsonAsync("/api/package-validation-inputs/preflight", new { request.applicationId, request.path, request.role, request.profileSnapshotId, rootRelativePath = "nested" });
            Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
            using var invalid = await client.PostAsJsonAsync("/api/package-validation-inputs/preflight", new { request.applicationId, request.path, role = 99, request.profileSnapshotId });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var response = await client.PostAsJsonAsync("/api/package-validation-inputs/preflight", request);
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var id = created.RootElement.GetProperty("id").GetString();
            using var read = await client.GetAsync("/api/package-validation-inputs/preflight/" + id);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.True(read.Headers.CacheControl!.NoStore);
            using var reread = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            Assert.Equal(created.RootElement.GetProperty("assessment").GetRawText(), reread.RootElement.GetProperty("assessment").GetRawText());
            Assert.False(reread.RootElement.GetProperty("assessment").GetProperty("isReadyForFinalization").GetBoolean());
        }
        finally { RetainedPackageTestFiles.RetainDirectory(root); }
    }
}
