global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
using System.Net;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.Security;

internal static class LinuxPlatform
{
    private static readonly List<object> Results = [];
    private static int _failed;
    private static async Task<int> Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "Npgsql"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Npgsql.dll")) : null;
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Actual Linux required");
        await Check("budget concurrency bound and cancelled waiter recovery", BudgetAsync);
        await Check("content host refuses unqualified Linux before tool execution", ContentRefusalAsync);
        await Check("HTTP port-only refused", () => RefuseAsync(new() { ["HTTP_PORTS"] = "8080" }));
        await Check("HTTPS multi-port-only refused", () => RefuseAsync(new() { ["HTTPS_PORTS"] = "8443;9443" }));
        await Check("both protocol port sets refused", () => RefuseAsync(new() { ["HTTP_PORTS"] = "8080;8081", ["HTTPS_PORTS"] = "8443" }));
        await Check("preferHostingUrls cannot bypass loopback endpoint", () => RefuseAsync(new() { ["HTTP_PORTS"] = "8080", ["preferHostingUrls"] = "true", ["Kestrel:Endpoints:Local:Url"] = "http://127.0.0.1:0" }));
        await Check("explicit loopback URL overrides port settings", () => HostAsync(new() { ["urls"] = "http://127.0.0.1:0", ["HTTP_PORTS"] = "8080", ["HTTPS_PORTS"] = "8443" }));
        await Check("explicit endpoint owns loopback listener", () => HostAsync(new() { ["Kestrel:Endpoints:Local:Url"] = "http://127.0.0.1:0", ["HTTP_PORTS"] = "8080" }));
        await Check("wildcard requires container option", () => RefuseAsync(new() { ["urls"] = "http://0.0.0.0:8080" }));
        await Check("container configuration exception remains explicit", () => {
            using var host = Builder(new() { ["HTTP_PORTS"] = "8080" }).Build();
            Validate(host, true); return Task.CompletedTask; // Validate only: never open a public listener.
        });
        await File.WriteAllTextAsync(Path.Combine(args[0], "linux-tests.json"), JsonSerializer.Serialize(new {
            passed = Results.Count - _failed, failed = _failed, skipped = 0, cases = Results,
            unqualified = new[] { "Linux qpdf hard memory/CPU process-tree enforcement", "Linux Python content capability", "real backend Formal capture/delivery" }
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"TOTAL passed={Results.Count - _failed} failed={_failed} skipped=0");
        return _failed == 0 ? 0 : 1;
    }
    private static async Task Check(string name, Func<Task> action)
    {
        try { await action(); Results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Results.Add(new { name, passed = false, error = error.Message }); Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task BudgetAsync()
    {
        using var budget = new PdfToolProcessBudget(2);
        var first = await budget.AcquireAsync(); var second = await budget.AcquireAsync();
        try
        {
            using var cancel = new CancellationTokenSource();
            var blocked = budget.AcquireAsync(cancel.Token).AsTask();
            Require(!blocked.IsCompleted, "Third process was admitted");
            await cancel.CancelAsync();
            try { await blocked; throw new InvalidOperationException("Cancelled waiter succeeded"); }
            catch (OperationCanceledException) { }
            var next = budget.AcquireAsync().AsTask();
            Require(!next.IsCompleted, "Cancelled wait leaked a slot");
            await first.DisposeAsync();
            await using var admitted = await next.WaitAsync(TimeSpan.FromSeconds(2));
            await first.DisposeAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try { await budget.AcquireAsync(timeout.Token); throw new InvalidOperationException("Lease released twice"); }
            catch (OperationCanceledException) { }
        }
        finally { await first.DisposeAsync(); await second.DisposeAsync(); }
        await using var recovered = await budget.AcquireAsync();
    }
    private static async Task ContentRefusalAsync()
    {
        try
        {
            await PdfContentProcess.RunAsync("/path-that-must-never-execute", [], "unused",
                67108864, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 4096, 4096, 4096, null, CancellationToken.None);
            throw new InvalidOperationException("Unqualified content execution succeeded");
        }
        catch (PlatformNotSupportedException) { }
    }
    private static WebApplicationBuilder Builder(Dictionary<string, string?> config)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development, Args = [] });
        builder.Configuration.Sources.Clear();
        config["Persistence:Provider"] = "InMemory";
        builder.Configuration.AddInMemoryCollection(config);
        builder.Logging.ClearProviders();
        return builder;
    }
    private static void Validate(WebApplication host, bool container = false) => new LocalOnlyDeploymentValidator(
        host.Configuration, host.Environment, Options.Create(new DeploymentOptions { Mode = "LocalOnly", Containerized = container })).Validate();
    private static async Task RefuseAsync(Dictionary<string, string?> config)
    {
        await using var host = Builder(config).Build();
        try { Validate(host); throw new InvalidDataException("Unsafe listener configuration was accepted"); }
        catch (InvalidOperationException) { }
    }
    private static async Task HostAsync(Dictionary<string, string?> config)
    {
        await using var host = Builder(config).Build();
        Validate(host);
        host.MapGet("/probe", () => "linux-loopback-proof");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(deadline.Token);
        try
        {
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            Require(addresses.Count == 1, "Expected one listener");
            var uri = new Uri(addresses.Single());
            Require(IPAddress.IsLoopback(IPAddress.Parse(uri.Host)), "Actual listener was not loopback");
            using var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { UseProxy = false });
            Require(await client.GetStringAsync(new Uri(uri, "/probe"), deadline.Token) == "linux-loopback-proof", "Probe bytes differ");
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}
