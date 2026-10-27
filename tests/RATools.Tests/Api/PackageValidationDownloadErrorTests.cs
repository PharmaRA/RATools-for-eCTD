using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using RATools.Api.Controllers;
using RATools.Application.PackageValidation;

namespace RATools.Tests.Api;

public sealed class PackageValidationDownloadErrorTests
{
    [Theory]
    [InlineData(false, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(true, StatusCodes.Status409Conflict)]
    public async Task UnstartedDownloadErrorsDiscardBinaryHeaders(bool changedInput, int status)
    {
        var controller = new Probe(new DefaultHttpContext());
        Exception failure = changedInput ? new PackageInputChangedException("memory") : new IOException("First read failed.");
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.FailDownloadAsync(failure));
        Assert.Equal(status, result.StatusCode);
        Assert.Null(controller.Response.ContentType);
        Assert.Null(controller.Response.ContentLength);
        Assert.False(controller.Response.Headers.ContainsKey("Content-Disposition"));
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task StartedDownloadErrorsAreRethrownWithoutChangingHeaders()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        var controller = new Probe(context);
        var failure = new IOException("Partial download failed.");
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => controller.FailDownloadAsync(failure)));
        Assert.Equal("application/octet-stream", controller.Response.ContentType);
        Assert.Equal(4096, controller.Response.ContentLength);
    }

    private sealed class Probe : PackageValidationControllerBase
    {
        public Probe(HttpContext context) => ControllerContext = new ControllerContext { HttpContext = context };

        public Task<IActionResult> FailDownloadAsync(Exception failure) => RespondAsync(() =>
        {
            Response.ContentType = "application/octet-stream";
            Response.ContentLength = 4096;
            Response.Headers.ContentDisposition = "attachment; filename=source.xml";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers.CacheControl = "no-store";
            return Task.FromException<IActionResult>(failure);
        });
    }

    private sealed class StartedResponse : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
