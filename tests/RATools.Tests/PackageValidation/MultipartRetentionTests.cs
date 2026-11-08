using System.Text;
using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Api.Filters;
using RATools.Application.Abstractions.Security;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.PackageValidation;

public sealed class MultipartRetentionTests
{
    private readonly string _root = Path.Combine(RetainedPackageTestFiles.ScratchParent, "ratools-multipart-retention-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StorageIoFailure_Returns422InsteadOfUnhandledError()
    {
        Directory.CreateDirectory(_root);
        var obstruction = Path.Combine(_root, "form-uploads");
        await File.WriteAllTextAsync(obstruction, "retain obstruction");
        using var body = new MemoryStream(Body());
        var context = Context(body);
        await Filter().OnResourceExecutionAsync(context, UnexpectedNext);
        Assert.Equal(422, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Equal("retain obstruction", await File.ReadAllTextAsync(obstruction));
    }

    [Fact]
    public async Task UnauthorizedRead_Returns403InsteadOfUnhandledError()
    {
        using var body = new FaultingStream(new UnauthorizedAccessException("Controlled access failure"));
        var context = Context(body);
        await Filter().OnResourceExecutionAsync(context, UnexpectedNext);
        Assert.Equal(403, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task DownstreamException_RetainsBothCleanupAndLoggingDiagnostics()
    {
        Directory.CreateDirectory(Path.Combine(_root, "form-uploads"));
        await File.WriteAllTextAsync(Path.Combine(_root, "form-uploads", "_recycle"), "retain obstruction");
        var original = new InvalidOperationException("Controlled downstream failure");
        var logger = new FaultingLogger();
        using var body = new MemoryStream(Body());
        var context = Context(body);
        var downstream = new ResourceExecutedContext(context, []) { Exception = original };
        await Filter(logger).OnResourceExecutionAsync(context, () => Task.FromResult(downstream));
        Assert.Same(original, downstream.Exception);
        Assert.IsType<IOException>(original.Data["MultipartRetentionFailure"]);
        Assert.Same(logger.Failure, original.Data["MultipartRetentionLoggingFailure"]);
        var owned = Assert.Single(Directory.GetDirectories(Path.Combine(_root, "form-uploads")));
        Assert.Equal(Payload(), await File.ReadAllBytesAsync(Path.Combine(owned, "package.zip")));
    }

    [Fact]
    public async Task CancelledRead_RetainsPrefixAndOriginalCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        using var body = new InterruptedBody(Body(), cancellation, failure);
        var context = Context(body);
        context.HttpContext.RequestAborted = cancellation.Token;
        var actual = await Record.ExceptionAsync(() => Filter().OnResourceExecutionAsync(context, UnexpectedNext));
        Assert.Same(failure, actual);
        var retained = Assert.Single(Directory.GetDirectories(Path.Combine(_root, "form-uploads", "_recycle")));
        var prefix = await File.ReadAllBytesAsync(Path.Combine(retained, "package.zip"));
        Assert.InRange(prefix.Length, 1, Payload().Length - 1);
        Assert.Equal(Payload()[..prefix.Length], prefix);
    }

    [Fact]
    public async Task SuccessfulRegistration_WithFailedRecycleAndLogger_KeepsCreatedResponseAndRegisteredBytes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "form-uploads"));
        await File.WriteAllTextAsync(Path.Combine(_root, "form-uploads", "_recycle"), "retained obstruction");
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("0001/index.xml").Open());
            await writer.WriteAsync("<ectd/>");
        }
        var bytes = zip.ToArray();
        var store = new InMemoryPackageValidationStore();
        var registry = new PackageValidationInputRegistry(store, new PackageInputReader(Path.Combine(_root, "captures")),
            new UploadOnlyPolicy(), Options.Create(new PackageValidationOptions { StorageRoot = _root }));
        using var body = new MemoryStream(Body(bytes));
        var context = Context(body);
        PackageInputDto? input = null;
        var created = new CreatedResult("/controlled-input", null);
        var executed = new ResourceExecutedContext(context, []) { Result = created };
        await Filter(new FaultingLogger()).OnResourceExecutionAsync(context, async () =>
        {
            await using var uploaded = context.HttpContext.Request.Form.Files.GetFile("File")!.OpenReadStream();
            input = await registry.UploadAsync(Guid.NewGuid(), uploaded, "0001", "0001");
            created.Value = input;
            return executed;
        });
        Assert.Null(executed.Exception);
        Assert.Same(created, executed.Result);
        Assert.Equal(201, created.StatusCode);
        var result = Assert.IsType<PackageInputDto>(created.Value);
        Assert.Equal(input, await registry.GetAsync(result.Id));
        var registered = Assert.IsType<RegisteredPackageInput>(await store.GetInputAsync(result.Id));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(registered.SourcePath));
        var stage = Assert.Single(Directory.GetDirectories(Path.Combine(_root, "form-uploads")));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(stage, "package.zip")));
    }

    [Fact]
    public async Task ConcurrentUploads_HoldSeparateLiveStreamsAndRetainDistinctBytes()
    {
        var arrived = 0;
        var bothBound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Payload();
        var second = Enumerable.Repeat((byte)43, 131073).ToArray();
        async Task Execute(byte[] expected)
        {
            using var body = new MemoryStream(Body(expected));
            var context = Context(body);
            await Filter().OnResourceExecutionAsync(context, async () =>
            {
                if (Interlocked.Increment(ref arrived) == 2) bothBound.SetResult();
                await bothBound.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await using var stream = context.HttpContext.Request.Form.Files.GetFile("File")!.OpenReadStream();
                using var received = new MemoryStream();
                await stream.CopyToAsync(received);
                Assert.Equal(expected, received.ToArray());
                return new ResourceExecutedContext(context, []) { Result = new OkResult() };
            });
        }
        await Task.WhenAll(Execute(first), Execute(second));
        var directories = Directory.GetDirectories(Path.Combine(_root, "form-uploads", "_recycle"));
        Assert.Equal(2, directories.Length);
        var retained = await Task.WhenAll(directories.Select(directory => File.ReadAllBytesAsync(Path.Combine(directory, "package.zip"))));
        Assert.Single(retained, bytes => bytes.SequenceEqual(first));
        Assert.Single(retained, bytes => bytes.SequenceEqual(second));
        Assert.Equal("_recycle", Path.GetFileName(Assert.Single(Directory.GetDirectories(Path.Combine(_root, "form-uploads")))));
    }

    private RetainedPackageUploadFilter Filter(ILogger<RetainedPackageUploadFilter>? logger = null) =>
        new(Options.Create(new PackageValidationOptions { StorageRoot = _root }), Options.Create(new FormOptions()),
            logger ?? NullLogger<RetainedPackageUploadFilter>.Instance);

    private static ResourceExecutingContext Context(Stream body)
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "multipart/form-data; boundary=retention-fixture";
        http.Request.Body = body;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(action, [], new List<IValueProviderFactory>());
    }

    private static Task<ResourceExecutedContext> UnexpectedNext() => throw new InvalidOperationException("Invalid input must not execute the action.");
    private static byte[] Payload() => Enumerable.Repeat((byte)42, 131072).ToArray();
    private static byte[] Body(byte[]? payload = null) => [.. Encoding.UTF8.GetBytes("--retention-fixture\r\nContent-Disposition: form-data; name=\"File\"; filename=\"ignored.zip\"\r\n\r\n"),
        .. payload ?? Payload(), .. Encoding.UTF8.GetBytes("\r\n--retention-fixture--\r\n")];

    private sealed class UploadOnlyPolicy : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [];
        public string EnsureAllowed(string path) => throw new InvalidOperationException("Owned upload must not require caller source authorization.");
    }

    private sealed class FaultingStream(Exception failure) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(failure);
    }

    private sealed class InterruptedBody(byte[] bytes, CancellationTokenSource cancellation, Exception failure) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < 65536) return base.ReadAsync(buffer, cancellationToken);
            cancellation.Cancel();
            return ValueTask.FromException<int>(failure);
        }
    }

    private sealed class FaultingLogger : ILogger<RetainedPackageUploadFilter>
    {
        public InvalidOperationException Failure { get; } = new("Controlled logger failure");
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => throw Failure;
    }
}
