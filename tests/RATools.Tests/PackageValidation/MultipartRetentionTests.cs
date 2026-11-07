using System.Text;
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
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class MultipartRetentionTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ratools-multipart-retention-" + Guid.NewGuid().ToString("N"));

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
    private static byte[] Body() => [.. Encoding.UTF8.GetBytes("--retention-fixture\r\nContent-Disposition: form-data; name=\"File\"; filename=\"ignored.zip\"\r\n\r\n"),
        .. Payload(), .. Encoding.UTF8.GetBytes("\r\n--retention-fixture--\r\n")];

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
