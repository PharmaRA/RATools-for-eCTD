using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Api.Filters;

// Populate the existing form contract before MVC binding. Framework file buffering
// uses DeleteOnClose; this endpoint instead retains its task-owned staging bytes.
public sealed class RetainedPackageUploadFilter(IOptions<PackageValidationOptions> validationOptions,
    IOptions<FormOptions> formOptions, ILogger<RetainedPackageUploadFilter> logger) : IAsyncResourceFilter
{
    private static readonly Action<ILogger, string, Exception?> LogRetentionFailure = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(1, "MultipartRetentionFailure"), "Multipart staging retained for inspection at {UploadDirectory}");

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var options = formOptions.Value;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status415UnsupportedMediaType);
            return;
        }
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > options.MultipartBoundaryLengthLimit)
        {
            context.Result = new BadRequestObjectResult(new { message = "A bounded multipart boundary is required." });
            return;
        }
        var staging = new RetainedUpload(validationOptions.Value.StorageRoot);
        Exception? failure = null;
        try
        {
            try
            {
                request.Form = await ReadFormAsync(request.Body, boundary, staging, options,
                    Math.Min(validationOptions.Value.ReadLimits.MaxArchiveBytes, options.MultipartBodyLengthLimit), request.HttpContext.RequestAborted).ConfigureAwait(false);
            }
            catch (UploadLimitException exception)
            {
                failure = exception;
                context.Result = new ObjectResult(new { code = "UPLOAD_LIMIT", message = exception.Message }) { StatusCode = StatusCodes.Status413PayloadTooLarge };
                return;
            }
            catch (InvalidDataException exception)
            {
                failure = exception;
                context.Result = new BadRequestObjectResult(new { message = exception.Message });
                return;
            }
            catch (BadHttpRequestException exception)
            {
                failure = exception;
                context.Result = new ObjectResult(new { message = "The upload request was rejected by the HTTP server." }) { StatusCode = exception.StatusCode };
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                failure = exception;
                context.Result = new ObjectResult(new { message = "Access to upload staging is denied." }) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
            catch (IOException exception)
            {
                failure = exception;
                context.Result = new ObjectResult(new { message = "Upload staging is unavailable." }) { StatusCode = StatusCodes.Status422UnprocessableEntity };
                return;
            }
            var executed = await next().ConfigureAwait(false);
            failure = executed.Exception;
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            try { await staging.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Registration may already have committed; cleanup must not change its response.
                if (failure is not null) failure.Data["MultipartRetentionFailure"] = exception;
                try { LogRetentionFailure(logger, staging.DirectoryPath, exception); }
                catch (Exception loggingException)
                {
                    if (failure is not null) failure.Data["MultipartRetentionLoggingFailure"] = loggingException;
                }
            }
        }
    }

    private static async Task<IFormCollection> ReadFormAsync(Stream body, string boundary, RetainedUpload staging,
        FormOptions options, long maximumFileBytes, CancellationToken cancellationToken)
    {
        var reader = new MultipartReader(boundary, body)
        {
            HeadersCountLimit = options.MultipartHeadersCountLimit,
            HeadersLengthLimit = options.MultipartHeadersLengthLimit
        };
        var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var files = new FormFileCollection();
        var sections = 0;
        while (await ReadSectionAsync(reader, cancellationToken).ConfigureAwait(false) is { } section)
        {
            if (++sections > Math.Min(4, options.ValueCountLimit)) throw new InvalidDataException("Upload contains too many form sections.");
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Each upload section must have a form-data disposition.");
            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            if (disposition.FileName.HasValue || disposition.FileNameStar.HasValue)
            {
                if (!string.Equals(name, "File", StringComparison.OrdinalIgnoreCase) || files.Count != 0)
                    throw new InvalidDataException("Upload requires one File section.");
                var stream = staging.CreateFile();
                await CopyBoundedAsync(section.Body, stream, maximumFileBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                var length = stream.Length;
                stream.Position = 0;
                files.Add(new FormFile(stream, 0, length, "File", HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "package.zip")
                { Headers = new HeaderDictionary(section.Headers!) });
            }
            else
            {
                if (name is null || !(name.Equals("ApplicationId", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("SequenceNumber", StringComparison.OrdinalIgnoreCase) || name.Equals("RootRelativePath", StringComparison.OrdinalIgnoreCase)) || fields.ContainsKey(name))
                    throw new InvalidDataException("Upload contains an unknown or duplicate form field.");
                using var value = new MemoryStream();
                await CopyBoundedAsync(section.Body, value, Math.Min(4096, options.ValueLengthLimit), cancellationToken).ConfigureAwait(false);
                fields.Add(name, Encoding.UTF8.GetString(value.GetBuffer(), 0, checked((int)value.Length)));
            }
        }
        return new FormCollection(fields, files);
    }

    private static async Task<MultipartSection?> ReadSectionAsync(MultipartReader reader, CancellationToken cancellationToken)
    {
        try { return await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false); }
        catch (IOException exception) when (exception is not BadHttpRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException("The multipart body is incomplete or unreadable.", exception);
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        long length = 0;
        while (true)
        {
            int read;
            try { read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); }
            catch (IOException exception) when (exception is not BadHttpRequestException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidDataException("The multipart body is incomplete or unreadable.", exception);
            }
            if (read == 0) break;
            length = checked(length + read);
            if (length > maximumBytes) throw new UploadLimitException("The uploaded section exceeds its configured byte limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class UploadLimitException(string message) : IOException(message);

    private sealed class RetainedUpload(string storageRoot) : IAsyncDisposable
    {
        private readonly string _root = Path.GetFullPath(Path.Combine(storageRoot, "form-uploads"));
        private readonly string _id = Guid.NewGuid().ToString("N");
        private FileStream? _stream;
        public string DirectoryPath => Path.Combine(_root, _id);

        public FileStream CreateFile()
        {
            EnsureNoLinks(DirectoryPath);
            Directory.CreateDirectory(DirectoryPath);
            EnsureNoLinks(DirectoryPath);
            _stream = new FileStream(Path.Combine(DirectoryPath, "package.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return _stream;
        }

        public async ValueTask DisposeAsync()
        {
            if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false);
            if (!Directory.Exists(DirectoryPath)) return;
            var owned = Path.GetFullPath(DirectoryPath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(owned), _root, comparison) || Path.GetFileName(owned) != _id)
                throw new InvalidOperationException("Invalid multipart staging ownership.");
            EnsureNoLinks(owned);
            foreach (var entry in Directory.EnumerateFileSystemEntries(owned))
            {
                EnsureNoLinks(entry);
                if (Directory.Exists(entry) || Path.GetFileName(entry) != "package.zip")
                    throw new IOException("Unexpected multipart staging entry requires inspection.");
            }
            var recycle = Path.Combine(_root, "_recycle");
            EnsureNoLinks(recycle);
            Directory.CreateDirectory(recycle);
            EnsureNoLinks(recycle);
            var destination = Path.GetFullPath(Path.Combine(recycle, _id + "-" + Guid.NewGuid().ToString("N")));
            if (!string.Equals(Path.GetDirectoryName(destination), recycle, comparison)) throw new InvalidOperationException("Invalid multipart recycle destination.");
            EnsureNoLinks(owned);
            Directory.Move(owned, destination);
        }

        private static void EnsureNoLinks(string path)
        {
            for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
                if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Multipart storage must not traverse links or reparse points.");
        }
    }
}
