using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using RATools.Application.DocumentProcessing;

namespace RATools.Infrastructure.DocumentProcessing;

public sealed class QpdfProcessingOptions
{
    public string ExecutablePath { get; set; } = "qpdf";
    public IReadOnlyList<string> ExecutableArguments { get; set; } = [];
    public Func<string, Stream>? OutputStreamFactory { get; set; }
    public Action<int>? ProcessStartedCallback { get; set; }
    public string SupportedVersion { get; set; } = "12.4.1";
    public long MaxJsonBytes { get; set; } = 64 * 1024 * 1024;
    public long MaxOutputBytes { get; set; } = 512 * 1024 * 1024;
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(SupportedVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxJsonBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxOutputBytes);
        if (ExecutionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ExecutionTimeout));
    }
}

public sealed class QpdfProcessingEngine(QpdfProcessingOptions options) : IPdfProcessingEngine
{
    private static readonly string[] FileSpecificationKeys = ["/F", "/UF"];
    public string EngineVersion => $"qpdf-{options.SupportedVersion}";

    public async Task<PdfNavigationInspection> InspectAsync(string sourcePath, long maxJsonBytes, CancellationToken cancellationToken = default)
    {
        options.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The PDF source file was not found.", sourcePath);
        if (maxJsonBytes is < 1 or > 512L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxJsonBytes));
        var version = await RunAsync(["--version"], options.ExecutionTimeout, maxJsonBytes, cancellationToken);
        if (version.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (version.TimedOut) throw new TimeoutException("qpdf version check exceeded its execution timeout.");
        if (version.ExitCode != 0 || !IsSupportedVersion(version.Stdout, options.SupportedVersion))
            throw new InvalidOperationException("The configured qpdf executable does not report the supported exact version.");
        ProcessResult inspected;
        try
        {
            inspected = await RunAsync(["--json", "--json-stream-data=inline", ToolPath(sourcePath), "-"], options.ExecutionTimeout,
                Math.Min(maxJsonBytes, options.MaxJsonBytes), cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("The configured qpdf executable is not available.", exception);
        }
        if (inspected.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (inspected.TimedOut) throw new TimeoutException("qpdf navigation inspection exceeded its execution timeout.");
        if (inspected.OutputLimitExceeded) throw new InvalidOperationException("qpdf navigation JSON exceeded the configured size limit.");
        if (inspected.ExitCode != 0)
        {
            if (TryReadInspection(inspected.Stdout) is { } failedInspection) return failedInspection;
            throw new InvalidOperationException("qpdf could not inspect the PDF source.");
        }
        var root = JsonNode.Parse(inspected.Stdout) as JsonObject
            ?? throw new InvalidOperationException("qpdf returned a JSON document with no root object.");
        return ReadInspection(root);
    }

    private PdfNavigationInspection ReadInspection(JsonObject root)
    {
        try { return ReadInspectionCore(root.DeepClone().AsObject()); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return new(EngineVersion, null, null, [], [], [], false, false, false, false, false,
                ["PDF navigation contains unresolved, cyclic or unsupported objects; inspection is incomplete."]);
        }
    }

    private PdfNavigationInspection ReadInspectionCore(JsonObject root)
    {
        ExpandNavigationObjects(root);
        var objectValues = BuildObjectMap(root);
        var catalog = objectValues.Values.FirstOrDefault(value => IsType(value, "/Catalog"));
        if (catalog is null) throw new InvalidOperationException("PDF catalog was not present in qpdf JSON.");
        var pageRefs = root["pages"]?.AsArray().Select(page => page?["object"]?.GetValue<string>())
            .Where(reference => reference is not null).Cast<string>().ToArray() ?? [];
        var pageNumbers = pageRefs.Select((reference, index) => (reference, number: index + 1))
            .ToDictionary(item => item.reference, item => item.number, StringComparer.Ordinal);
        var reasons = new List<string>();
        var bookmarks = new List<PdfBookmarkInspection>();
        if (root["outlines"] is JsonArray outlines)
        {
            for (var index = 0; index < outlines.Count; index++)
                ReadBookmarks(outlines[index]?.AsObject(), $"outline[{index + 1}]", 0, pageNumbers, objectValues, bookmarks, reasons);
        }
        var links = new List<PdfLinkInspection>();
        for (var pageIndex = 0; pageIndex < pageRefs.Length; pageIndex++)
        {
            if (!objectValues.TryGetValue(pageRefs[pageIndex], out var page))
            {
                reasons.Add($"Page object {pageRefs[pageIndex]} was not present in qpdf JSON.");
                continue;
            }
            var annotations = page["/Annots"]?.AsArray() ?? [];
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                var annotation = annotations[annotationIndex] as JsonObject;
                if (annotation is null)
                {
                    reasons.Add($"page[{pageIndex + 1}]/annotation[{annotationIndex + 1}] could not be resolved.");
                    continue;
                }
                if (!string.Equals(annotation["/Subtype"]?.GetValue<string>(), "/Link", StringComparison.Ordinal)) continue;
                var path = $"page[{pageIndex + 1}]/annotation[{annotationIndex + 1}]";
                var link = ReadLink(annotation, path, pageIndex + 1, pageNumbers, reasons);
                links.Add(link);
                if (annotation.ContainsKey("/AA") || annotation["/A"]?.AsObject()?["/Next"] is not null)
                    reasons.Add($"{path} contains an additional or chained action.");
            }
        }
        var namedDestinations = ReadNamedDestinations(catalog, objectValues, pageNumbers, reasons);
        var namedNames = namedDestinations.Select(destination => destination.Name).ToHashSet(StringComparer.Ordinal);
        var namedPages = namedDestinations.Where(destination => destination.IsValid && destination.PageNumber is not null)
            .ToDictionary(destination => destination.Name, destination => destination.PageNumber, StringComparer.Ordinal);
        bookmarks = bookmarks.Select(bookmark => bookmark with
        {
            PageNumber = bookmark.PageNumber ?? (bookmark.NamedDestination is { } name && namedPages.TryGetValue(name, out var page) ? page : null),
            IsValid = bookmark.IsValid && (bookmark.NamedDestination is null || namedNames.Contains(bookmark.NamedDestination))
        }).ToList();
        links = links.Select(link => link with
        {
            TargetPageNumber = link.TargetPageNumber ?? (link.Kind != PdfLinkTargetKind.RelativeRemotePdf && link.NamedDestination is { } name && namedPages.TryGetValue(name, out var page) ? page : null),
            IsValid = link.IsValid && (link.Kind != PdfLinkTargetKind.NamedDestination || link.NamedDestination is { } namedLink && namedNames.Contains(namedLink))
        }).ToList();
        if (bookmarks.Any(bookmark => !bookmark.IsValid) || links.Any(link => !link.IsValid))
            reasons.Add("One or more navigation targets could not be resolved.");
        var encrypted = root["encrypt"]?.AsObject()?["encrypted"]?.GetValue<bool>() == true;
        var signature = objectValues.Select(item => item.Value).Any(ContainsSignatureMarker);
        if (signature) reasons.Add("The PDF contains a signature dictionary; signature validity is not asserted.");
        var pageMode = catalog["/PageMode"]?.GetValue<string>()?.TrimStart('/');
        var navigationComplete = reasons.Count == 0 || reasons.All(reason => reason.Contains("signature", StringComparison.OrdinalIgnoreCase));
        return new(EngineVersion, pageRefs.Length, pageMode, bookmarks, links, namedDestinations, encrypted, signature,
            SignatureValidityKnown: !signature, navigationComplete, DestinationsComplete: !reasons.Any(reason => reason.Contains("destination", StringComparison.OrdinalIgnoreCase)),
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private PdfNavigationInspection? TryReadInspection(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject root ? ReadInspection(root) : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static void ReadBookmarks(JsonObject? node, string path, int level, IReadOnlyDictionary<string, int> pageNumbers,
        IReadOnlyDictionary<string, JsonObject> objectValues, List<PdfBookmarkInspection> output, List<string> reasons)
    {
        if (node is null) { reasons.Add($"{path} is not a valid outline node."); return; }
        var title = node["title"]?.GetValue<string>() ?? string.Empty;
        var destinationToken = node["dest"];
        if (destinationToken is null && node["object"]?.GetValue<string>() is { } reference && objectValues.TryGetValue(reference, out var outlineObject))
            destinationToken = outlineObject["/Dest"];
        var destination = ReadDestination(destinationToken, pageNumbers);
        output.Add(new(path, title, level, destination.PageNumber, destination.NamedDestination, destination.IsValid));
        if (!destination.IsValid) reasons.Add($"{path} has an invalid destination.");
        if (node["kids"] is JsonArray kids)
            for (var index = 0; index < kids.Count; index++)
                ReadBookmarks(kids[index]?.AsObject(), $"{path}/child[{index + 1}]", level + 1, pageNumbers, objectValues, output, reasons);
    }

    private static PdfLinkInspection ReadLink(JsonObject annotation, string path, int pageNumber,
        IReadOnlyDictionary<string, int> pageNumbers, List<string> reasons)
    {
        var action = annotation["/A"]?.AsObject();
        var actionKind = action?["/S"]?.GetValue<string>();
        var destination = string.Equals(actionKind, "/GoToR", StringComparison.Ordinal)
            ? ReadRemoteDestination(action?["/D"])
            : ReadDestination(action?["/D"] ?? annotation["/Dest"], pageNumbers);
        if (string.Equals(actionKind, "/URI", StringComparison.Ordinal))
        {
            var uri = DecodeQpdfString(action?["/URI"]?.GetValue<string>());
            var valid = !string.IsNullOrWhiteSpace(uri) && Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out _);
            if (!valid) reasons.Add($"{path} contains an invalid URI action.");
            return new(path, pageNumber, PdfLinkTargetKind.ExternalUri, uri ?? string.Empty, null, null, null, valid);
        }
        if (string.Equals(actionKind, "/GoToR", StringComparison.Ordinal))
        {
            var file = DecodeQpdfString(action?["/F"]?.GetValue<string>());
            var target = destination.NamedDestination ?? (destination.PageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            var valid = !string.IsNullOrWhiteSpace(file) && destination.IsValid;
            if (!valid) reasons.Add($"{path} contains an invalid remote destination.");
            return new(path, pageNumber, PdfLinkTargetKind.RelativeRemotePdf, target, destination.PageNumber, destination.NamedDestination, file, valid);
        }
        var inDocumentTarget = destination.NamedDestination ?? (destination.PageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        if (!destination.IsValid) reasons.Add($"{path} contains an invalid internal destination.");
        return new(path, pageNumber, destination.PageNumber is not null ? PdfLinkTargetKind.Page : PdfLinkTargetKind.NamedDestination, inDocumentTarget, destination.PageNumber,
            destination.NamedDestination, null, destination.IsValid);
    }

    private static List<PdfNamedDestinationInspection> ReadNamedDestinations(JsonObject catalog,
        IReadOnlyDictionary<string, JsonObject> objectValues, IReadOnlyDictionary<string, int> pageNumbers, List<string> reasons)
    {
        var output = new List<PdfNamedDestinationInspection>();
        if (catalog["/Dests"] is JsonObject direct)
        {
            foreach (var item in direct)
            {
                var destination = ReadDestination(item.Value, pageNumbers);
                output.Add(new(item.Key.TrimStart('/'), destination.PageNumber, destination.IsValid));
                if (!destination.IsValid) reasons.Add($"Named destination '{item.Key.TrimStart('/')}' is invalid.");
            }
        }
        if (catalog["/Names"]?.AsObject()?["/Dests"] is { } nameTree)
            ReadNameTree(nameTree, objectValues, pageNumbers, output, reasons, new HashSet<string>(StringComparer.Ordinal));
        return output;
    }

    private static void ReadNameTree(JsonNode node, IReadOnlyDictionary<string, JsonObject> objectValues,
        IReadOnlyDictionary<string, int> pageNumbers, List<PdfNamedDestinationInspection> output,
        List<string> reasons, ISet<string> visited)
    {
        var resolved = node.GetValueKind() == JsonValueKind.String && objectValues.TryGetValue(node.GetValue<string>(), out var value) ? value : node.AsObject();
        var identity = resolved.ToJsonString();
        if (!visited.Add(identity)) { reasons.Add("Destination name tree contains a cycle."); return; }
        if (resolved["/Names"] is JsonArray names)
        {
            for (var index = 0; index + 1 < names.Count; index += 2)
            {
                var name = DecodeQpdfString(names[index]?.GetValue<string>());
                var destination = ReadDestination(names[index + 1], pageNumbers);
                if (name is null) reasons.Add("Destination name tree contains a non-string name.");
                else
                {
                    output.Add(new(name, destination.PageNumber, destination.IsValid));
                    if (!destination.IsValid) reasons.Add($"Named destination '{name}' is invalid.");
                }
            }
        }
        if (resolved["/Kids"] is JsonArray kids)
            foreach (var child in kids) if (child is not null) ReadNameTree(child, objectValues, pageNumbers, output, reasons, visited);
    }

    private static (int? PageNumber, string? NamedDestination, bool IsValid) ReadDestination(JsonNode? token,
        IReadOnlyDictionary<string, int> pageNumbers)
    {
        if (token is null) return (null, null, false);
        if (token.GetValueKind() == JsonValueKind.String)
        {
            var name = DecodeQpdfString(token.GetValue<string>());
            return (null, name, !string.IsNullOrWhiteSpace(name));
        }
        if (token is not JsonArray array || array.Count < 2) return (null, null, false);
        var reference = array[0]?.GetValue<string>();
        var page = reference is not null && pageNumbers.TryGetValue(reference, out var pageNumber) ? pageNumber : (int?)null;
        return (page, null, page is not null);
    }

    private static (int? PageNumber, string? NamedDestination, bool IsValid) ReadRemoteDestination(JsonNode? token)
    {
        if (token is null) return (null, null, false);
        if (token.GetValueKind() == JsonValueKind.Number)
        {
            var zeroBased = token.GetValue<int>();
            return (zeroBased >= 0 ? zeroBased + 1 : null, null, zeroBased >= 0);
        }
        if (token.GetValueKind() == JsonValueKind.String)
        {
            var name = DecodeQpdfString(token.GetValue<string>());
            return (null, name, !string.IsNullOrWhiteSpace(name));
        }
        if (token is JsonArray array && array.Count > 0 && array[0]?.GetValueKind() == JsonValueKind.Number)
        {
            var zeroBased = array[0]!.GetValue<int>();
            return (zeroBased >= 0 ? zeroBased + 1 : null, null, zeroBased >= 0);
        }
        return (null, null, false);
    }

    private static string? DecodeQpdfString(string? value)
        => string.IsNullOrWhiteSpace(value) ? value : value.StartsWith("u:", StringComparison.Ordinal) ? value[2..] : value;

    private static bool ContainsSignatureMarker(JsonObject value)
        => value.Any(property => property.Key == "/ByteRange" ||
            property.Key == "/Type" && property.Value?.GetValue<string>() == "/Sig" ||
            property.Key == "/FT" && property.Value?.GetValue<string>() == "/Sig") ||
            value.Select(property => property.Value).Any(node => node is JsonObject child && ContainsSignatureMarker(child));

    public async Task<PdfEngineResult> ExecuteAsync(PdfEngineRequest request, CancellationToken cancellationToken = default)
    {
        options.Validate();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!File.Exists(request.SourcePath)) return Failure(request.OutputPath, "SOURCE_NOT_FOUND", TimeSpan.Zero);
        var sourcePath = Path.GetFullPath(request.SourcePath);
        var outputPath = Path.GetFullPath(request.OutputPath);
        if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
            return Failure(outputPath, "SOURCE_OUTPUT_COLLISION", TimeSpan.Zero);
        if (File.Exists(outputPath)) return Failure(outputPath, "OUTPUT_EXISTS", TimeSpan.Zero);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var started = Stopwatch.GetTimestamp();
        var timeout = request.Timeout > TimeSpan.Zero ? request.Timeout : options.ExecutionTimeout;
        var maxJson = request.MaxJsonBytes > 0 ? Math.Min(request.MaxJsonBytes, options.MaxJsonBytes) : options.MaxJsonBytes;
        var maxOutput = request.MaxOutputBytes > 0 ? Math.Min(request.MaxOutputBytes, options.MaxOutputBytes) : options.MaxOutputBytes;
        try
        {
            if (Path.IsPathRooted(options.ExecutablePath) && !File.Exists(options.ExecutablePath))
                return Failure(outputPath, "ENGINE_NOT_FOUND", Elapsed(started));

            ProcessResult version;
            try { version = await RunAsync(["--version"], timeout, maxJson, cancellationToken); }
            catch (System.ComponentModel.Win32Exception) { return Failure(outputPath, "ENGINE_NOT_FOUND", Elapsed(started)); }
            if (version.Cancelled) throw new OperationCanceledException(cancellationToken);
            if (version.TimedOut) return Failure(outputPath, "ENGINE_TIMEOUT", Elapsed(started));
            if (version.ExitCode != 0 || !IsSupportedVersion(version.Stdout, options.SupportedVersion))
                return Failure(outputPath, "ENGINE_VERSION_UNSUPPORTED", Elapsed(started));

            var inspected = await RunAsync(["--json", "--json-stream-data=inline", ToolPath(sourcePath), "-"], timeout, maxJson, cancellationToken);
            if (inspected.Cancelled) throw new OperationCanceledException(cancellationToken);
            if (inspected.TimedOut) return Failure(outputPath, "ENGINE_TIMEOUT", Elapsed(started));
            if (inspected.OutputLimitExceeded) return Failure(outputPath, "JSON_OUTPUT_LIMIT", Elapsed(started));
            if (inspected.ExitCode != 0)
            {
                var failedInspection = TryReadInspection(inspected.Stdout);
                if (failedInspection?.IsEncrypted == true) return Failure(outputPath, "ENCRYPTED_PDF", Elapsed(started));
                if (failedInspection?.HasSignature == true) return Failure(outputPath, "SIGNED_PDF", Elapsed(started));
                return Failure(outputPath, "SOURCE_INVALID", Elapsed(started));
            }

            var json = JsonNode.Parse(inspected.Stdout) as JsonObject
                ?? throw new InvalidOperationException("qpdf returned a JSON document with no root object.");
            var sourceFacts = ReadInspection(json);
            if (sourceFacts.IsEncrypted) return Failure(outputPath, "ENCRYPTED_PDF", Elapsed(started));
            if (sourceFacts.HasSignature) return Failure(outputPath, "SIGNED_PDF", Elapsed(started));
            if (!sourceFacts.NavigationComplete || !sourceFacts.DestinationsComplete)
                return Failure(outputPath, "SOURCE_NAVIGATION_INCOMPLETE", Elapsed(started));
            var sourceJson = json.DeepClone().AsObject();
            ApplyParameters(json, request.Parameters.Normalize());
            var taskJsonPath = outputPath + ".qpdf.json";
            // qpdf reads inline stream base64 without unescaping JSON unicode escapes.
            // This file is consumed only by qpdf; keep '+' literal in its stream data.
            await WriteExclusiveAsync(taskJsonPath, json.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), cancellationToken);

            var stagingPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".qpdf.tmp";
            var updated = await RunToFileAsync(["--update-from-json=" + ToolPath(taskJsonPath), ToolPath(sourcePath), "-"], stagingPath, timeout, maxOutput, cancellationToken);
            if (updated.Cancelled) throw new OperationCanceledException(cancellationToken);
            if (updated.TimedOut) return Failure(outputPath, "ENGINE_TIMEOUT", Elapsed(started));
            if (updated.OutputLimitExceeded) return Failure(outputPath, "OUTPUT_SIZE_LIMIT", Elapsed(started));
            if (updated.ExitCode != 0 || !File.Exists(stagingPath)) return Failure(outputPath, "ENGINE_UPDATE_FAILED", Elapsed(started));
            var stagedSize = new FileInfo(stagingPath).Length;
            if (stagedSize > maxOutput) return Failure(outputPath, "OUTPUT_SIZE_LIMIT", Elapsed(started));
            var reopened = await RunAsync(["--json", "--json-stream-data=inline", ToolPath(stagingPath), "-"], timeout, maxJson, cancellationToken);
            if (reopened.Cancelled) throw new OperationCanceledException(cancellationToken);
            if (reopened.TimedOut) return Failure(outputPath, "RESULT_REOPEN_TIMEOUT", Elapsed(started));
            if (reopened.OutputLimitExceeded) return Failure(outputPath, "RESULT_JSON_SIZE_LIMIT", Elapsed(started));
            if (reopened.ExitCode != 0) return Failure(outputPath, "RESULT_REOPEN_FAILED", Elapsed(started));
            var resultJson = JsonNode.Parse(reopened.Stdout) as JsonObject
                ?? throw new InvalidOperationException("qpdf returned no JSON for the generated result.");
            ValidateResult(sourceJson, resultJson, request.Parameters.Normalize());
            if (File.Exists(outputPath)) return Failure(outputPath, "OUTPUT_EXISTS", Elapsed(started));
            File.Move(stagingPath, outputPath);
            var size = new FileInfo(outputPath).Length;
            return new(true, outputPath, EngineVersion, null, Elapsed(started), size);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Failure(outputPath, "ENGINE_ERROR: " + exception.Message, Elapsed(started));
        }
    }

    private static string ToolPath(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows() || absolute.Length < 248) return absolute;
        // The pinned Windows qpdf build rejects extended-length path syntax.
        // Read an existing DOS alias; this never creates or changes a file/link.
        var extended = absolute.StartsWith(@"\\?\", StringComparison.Ordinal) ? absolute :
            absolute.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + absolute[2..] : @"\\?\" + absolute;
        var buffer = new char[32768];
        var length = GetShortPathName(extended, buffer, (uint)buffer.Length);
        if (length is > 0 && length < buffer.Length)
        {
            var shortened = new string(buffer, 0, (int)length);
            shortened = shortened.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + shortened[8..] :
                shortened.StartsWith(@"\\?\", StringComparison.Ordinal) ? shortened[4..] : shortened;
            if (shortened.Length < 248) return shortened;
        }
        throw new InvalidOperationException("The native PDF engine cannot open this Windows path. Use a shorter workspace path or a volume with existing short file names.");
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, uint bufferLength);

    private void ApplyParameters(JsonObject root, PdfProcessingParameters parameters)
    {
        var inspection = ReadInspection(root);
        if (!inspection.NavigationComplete || !inspection.DestinationsComplete)
            throw new InvalidOperationException("Navigation must be completely inspected before editing.");
        var objectValues = BuildObjectMap(root);
        var qpdfObjects = root["qpdf"]?.AsArray().ElementAtOrDefault(1)?.AsObject()
            ?? throw new InvalidOperationException("qpdf object table was not present in JSON.");
        var catalog = objectValues.Values.FirstOrDefault(value => IsType(value, "/Catalog"))
            ?? throw new InvalidOperationException("PDF catalog was not present in qpdf JSON.");
        if (parameters.PageMode is not null) catalog["/PageMode"] = "/" + parameters.PageMode;

            var pageRefs = root["pages"]?.AsArray().Select(page => page?[
            "object"]?.GetValue<string>()).Where(reference => reference is not null).Cast<string>().ToArray() ?? [];
        if (parameters.Bookmarks is not null)
        {
            var bookmarkDestinations = inspection.NamedDestinations.Where(item => item.IsValid).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
            ReplaceOutlines(root, catalog, qpdfObjects, objectValues, parameters.Bookmarks, pageRefs, bookmarkDestinations);
        }

        var linkDestinations = inspection.NamedDestinations.Where(item => item.IsValid).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var edit in parameters.Links ?? [])
        {
            if (edit.SourcePageNumber > pageRefs.Length) throw new InvalidOperationException("A link source page is outside the PDF page range.");
            if (!objectValues.TryGetValue(pageRefs[edit.SourcePageNumber - 1], out var page))
                throw new InvalidOperationException("The source page object was not present in qpdf JSON.");
            var annotationRefs = ResolveOriginalNode(root, page["/Annots"]) as JsonArray ?? [];
            var (pathPage, pathAnnotation) = ParseObjectPath(edit.ObjectPath!);
            if (pathPage != edit.SourcePageNumber || pathAnnotation < 1 || pathAnnotation > annotationRefs.Count)
                throw new InvalidOperationException("The link object path no longer matches the source page annotation list.");
            var annotation = ResolveOriginalNode(root, annotationRefs[pathAnnotation - 1]) as JsonObject;
            if (!string.Equals(annotation?["/Subtype"]?.GetValue<string>(), "/Link", StringComparison.Ordinal))
                annotation = null;
            if (annotation is null) throw new InvalidOperationException("The requested source page has no link annotation.");
            if (!string.IsNullOrWhiteSpace(edit.OriginalTarget) &&
                inspection.Links.Single(link => link.ObjectPath == edit.ObjectPath).Target != edit.OriginalTarget)
                throw new InvalidOperationException("The selected link changed after the navigation preview was generated.");
            annotation["/A"] = ActionFor(edit, pageRefs, linkDestinations);
            annotation.Remove("/Dest");
        }
        ApplyReferenceEdits(root, parameters.ReferenceEdits ?? []);
    }

    private static void ApplyReferenceEdits(JsonObject root, IReadOnlyList<PdfReferenceEdit> edits)
    {
        var objects = BuildObjectMap(root);
        var pages = root["pages"]?.AsArray() ?? [];
        foreach (var edit in edits)
        {
            var (pageNumber, annotationNumber) = ParseObjectPath(edit.ObjectPath);
            if (pageNumber != edit.SourcePageNumber || pageNumber < 1 || pageNumber > pages.Count)
                throw new InvalidOperationException("The reference selector does not identify a source page.");
            var page = objects[pages[pageNumber - 1]!["object"]!.GetValue<string>()];
            var annotations = ResolveOriginalNode(root, page["/Annots"]) as JsonArray ?? [];
            if (annotationNumber < 1 || annotationNumber > annotations.Count)
                throw new InvalidOperationException("The selected reference annotation disappeared.");
            var annotation = ResolveOriginalNode(root, annotations[annotationNumber - 1]) as JsonObject;
            var action = ResolveOriginalNode(root, annotation?["/A"]) as JsonObject;
            var expectedKind = edit.IsUri ? "/URI" : "/GoToR";
            if (annotation?["/Subtype"]?.GetValue<string>() != "/Link" || action?["/S"]?.GetValue<string>() != expectedKind)
                throw new InvalidOperationException("The selected reference action type changed.");
            var key = edit.IsUri ? "/URI" : "/F";
            var file = ResolveOriginalNode(root, action[key]);
            if (file is JsonObject specification)
            {
                var present = FileSpecificationKeys.Where(specification.ContainsKey).ToArray();
                if (present.Length == 0 || present.Any(name => DecodeQpdfString(specification[name]?.GetValue<string>()) != edit.OldReference))
                    throw new InvalidOperationException("The file specification no longer matches the reviewed reference.");
                foreach (var name in present) specification[name] = "u:" + edit.NewReference;
            }
            else
            {
                if (DecodeQpdfString(file?.GetValue<string>()) != edit.OldReference)
                    throw new InvalidOperationException("The reference no longer matches the reviewed target.");
                action[key] = "u:" + edit.NewReference;
            }
        }
    }

    private void ValidateResult(JsonObject source, JsonObject result, PdfProcessingParameters parameters)
    {
        source = source.DeepClone().AsObject();
        result = result.DeepClone().AsObject();
        ExpandNavigationObjects(source);
        ExpandNavigationObjects(result);
        // Compare the reopened file with the original graph after only the exact
        // approved file/URI fields changed. Destination arrays and action options
        // remain covered by the ordinary page/annotation fidelity comparison.
        ApplyReferenceEdits(source, parameters.ReferenceEdits ?? []);
        var sourceObjects = BuildObjectMap(source);
        var resultObjects = BuildObjectMap(result);
        var sourcePages = source["pages"]?.AsArray() ?? [];
        var resultPages = result["pages"]?.AsArray() ?? [];
        var sourcePageNumbers = sourcePages.Select((page, index) => (Reference: page!["object"]!.GetValue<string>(), Number: index + 1))
            .ToDictionary(item => item.Reference, item => item.Number, StringComparer.Ordinal);
        var resultPageNumbers = resultPages.Select((page, index) => (Reference: page!["object"]!.GetValue<string>(), Number: index + 1))
            .ToDictionary(item => item.Reference, item => item.Number, StringComparer.Ordinal);
        if (sourcePages.Count != resultPages.Count) throw new InvalidOperationException("Result page count changed during PDF navigation processing.");
        for (var index = 0; index < sourcePages.Count; index++)
        {
            var sourcePage = sourcePages[index]?.AsObject() ?? throw new InvalidOperationException("Source page JSON was invalid.");
            var resultPage = resultPages[index]?.AsObject() ?? throw new InvalidOperationException("Result page JSON was invalid.");
            var sourcePageObject = ResolvePageObject(sourcePage, sourceObjects);
            var resultPageObject = ResolvePageObject(resultPage, resultObjects);
            var editedAnnotations = (parameters.Links ?? []).Where(edit => edit.SourcePageNumber == index + 1)
                .Select(edit => ParseObjectPath(edit.ObjectPath!).Annotation).ToHashSet();
            if (!string.Equals(CanonicalVisualPage(sourcePageObject, sourcePage, sourceObjects, editedAnnotations, sourcePageNumbers),
                CanonicalVisualPage(resultPageObject, resultPage, resultObjects, editedAnnotations, resultPageNumbers), StringComparison.Ordinal))
                throw new InvalidOperationException($"Result page {index + 1} geometry, resources, images or decoded content changed.");
        }

        var navigation = ReadInspection(result);
        var original = ReadInspection(source);
        if (!navigation.NavigationComplete || !navigation.DestinationsComplete || !original.NavigationComplete || !original.DestinationsComplete)
            throw new InvalidOperationException("Source and result navigation must be completely inspected.");
        if (navigation.IsEncrypted || navigation.HasSignature)
            throw new InvalidOperationException("Generated PDF is encrypted or contains a signature and cannot be adopted.");
        if (parameters.PageMode is not null && !string.Equals(navigation.PageMode, parameters.PageMode, StringComparison.Ordinal))
            throw new InvalidOperationException("Generated PDF initial view did not match the requested PageMode.");
        if (parameters.PageMode is null && navigation.PageMode != original.PageMode)
            throw new InvalidOperationException("Unselected initial view changed.");
        if (parameters.Bookmarks is null && !navigation.Bookmarks.SequenceEqual(original.Bookmarks))
            throw new InvalidOperationException("Unselected bookmarks changed.");
        if (!navigation.NamedDestinations.OrderBy(item => item.Name, StringComparer.Ordinal)
            .SequenceEqual(original.NamedDestinations.OrderBy(item => item.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Named destinations changed.");
        var selected = (parameters.Links ?? []).Select(edit => edit.ObjectPath).ToHashSet(StringComparer.Ordinal);
        if (navigation.Links.Count != original.Links.Count || !navigation.Links.Where(link => !selected.Contains(link.ObjectPath))
            .SequenceEqual(original.Links.Where(link => !selected.Contains(link.ObjectPath))))
            throw new InvalidOperationException("Unselected PDF links changed or disappeared.");
        if (parameters.Bookmarks is not null)
        {
            if (navigation.Bookmarks.Count != parameters.Bookmarks.Count)
                throw new InvalidOperationException("Generated PDF bookmark count did not match the requested replacement tree.");
            for (var index = 0; index < parameters.Bookmarks.Count; index++)
            {
                var expected = parameters.Bookmarks[index];
                var actual = navigation.Bookmarks[index];
                var destinationMatches = expected.NamedDestination is { } named
                    ? string.Equals(actual.NamedDestination, named.TrimStart('#'), StringComparison.Ordinal)
                    : actual.PageNumber == expected.PageNumber && actual.NamedDestination is null;
                if (!actual.IsValid || actual.Level != expected.Level || !string.Equals(actual.Title, expected.Title, StringComparison.Ordinal) ||
                    !destinationMatches)
                    throw new InvalidOperationException($"Generated PDF bookmark {index + 1} did not match the requested edit.");
            }
        }
        foreach (var edit in parameters.Links ?? [])
        {
            var actual = navigation.Links.SingleOrDefault(link => link.ObjectPath == edit.ObjectPath)
                ?? throw new InvalidOperationException($"Generated PDF link selector '{edit.ObjectPath}' was not found.");
            if (!actual.IsValid || actual.Kind != edit.TargetKind) throw new InvalidOperationException("Generated PDF link kind or validity did not match the requested edit.");
            ValidateLinkTarget(actual, edit);
        }
    }

    private static void ValidateLinkTarget(PdfLinkInspection actual, PdfLinkEdit edit)
    {
        var target = edit.Target.Trim();
        switch (edit.TargetKind)
        {
            case PdfLinkTargetKind.Page when !int.TryParse(target, out var page) || actual.TargetPageNumber != page:
                throw new InvalidOperationException("Generated PDF page link target did not match the requested edit.");
            case PdfLinkTargetKind.NamedDestination when !string.Equals(actual.NamedDestination, target.TrimStart('#'), StringComparison.Ordinal):
                throw new InvalidOperationException("Generated PDF named link target did not match the requested edit.");
            case PdfLinkTargetKind.RelativeRemotePdf:
                var remote = ParseRemoteTarget(target);
                if (!string.Equals(actual.FileSpecification, remote.File, StringComparison.Ordinal) ||
                    (remote.PageNumber is not null && actual.TargetPageNumber != remote.PageNumber) ||
                    (remote.NamedDestination is not null && !string.Equals(actual.NamedDestination, remote.NamedDestination, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Generated PDF remote link target did not match the requested edit.");
                break;
            case PdfLinkTargetKind.ExternalUri when !string.Equals(actual.Target, target, StringComparison.Ordinal):
                throw new InvalidOperationException("Generated PDF URI link target did not match the requested edit.");
        }
    }

    private static JsonObject ResolvePageObject(JsonObject pageSummary, IReadOnlyDictionary<string, JsonObject> objects)
    {
        var reference = pageSummary["object"]?.GetValue<string>()
            ?? throw new InvalidOperationException("PDF page summary did not identify its page object.");
        return objects.GetValueOrDefault(reference)
            ?? throw new InvalidOperationException($"PDF page object '{reference}' was not present in qpdf JSON.");
    }

    private static string CanonicalVisualPage(JsonObject page, JsonObject pageSummary, IReadOnlyDictionary<string, JsonObject> objects,
        HashSet<int> editedAnnotations, Dictionary<string, int> pageNumbers)
    {
        var visual = new JsonObject();
        foreach (var key in new[] { "/MediaBox", "/CropBox", "/TrimBox", "/BleedBox", "/ArtBox", "/Rotate", "/UserUnit", "/Contents", "/Resources" })
            if (ResolveInheritedPageValue(page, key, objects) is { } value) visual[key] = NormalizePdfNode(value, objects, new HashSet<string>(StringComparer.Ordinal));
        if (pageSummary["images"] is { } images) visual["images"] = NormalizePdfNode(images, objects, new HashSet<string>(StringComparer.Ordinal));
        if (page["/Annots"] is JsonArray annotations)
        {
            var appearances = new JsonArray();
            for (var index = 0; index < annotations.Count; index++)
            {
                var annotation = annotations[index]?.DeepClone() as JsonObject ?? throw new InvalidOperationException("Unresolved result annotation.");
                annotation.Remove("/P"); // The containing page array determines page ownership.
                if (editedAnnotations.Contains(index + 1)) { annotation.Remove("/A"); annotation.Remove("/Dest"); }
                appearances.Add(NormalizePdfNode(annotation, objects, new HashSet<string>(StringComparer.Ordinal), pageNumbers));
            }
            visual["annotations"] = appearances;
        }
        return visual.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonNode? ResolveInheritedPageValue(JsonObject page, string key, IReadOnlyDictionary<string, JsonObject> objects)
    {
        var current = page;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (current[key] is { } value) return value;
            var parent = current["/Parent"]?.GetValue<string>();
            if (parent is null || !visited.Add(parent) || !objects.TryGetValue(parent, out current!)) return null;
        }
    }

    private static JsonNode? NormalizePdfNode(JsonNode? node, IReadOnlyDictionary<string, JsonObject> objects, ISet<string> visited,
        Dictionary<string, int>? pageNumbers = null)
    {
        if (node is null) return null;
        if (node.GetValueKind() == JsonValueKind.String)
        {
            var value = node.GetValue<string>();
            if (pageNumbers is not null && pageNumbers.TryGetValue(value, out var pageNumber))
                return JsonValue.Create("page-reference:" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (objects.TryGetValue(value, out var resolved))
            {
                if (!visited.Add(value)) return JsonValue.Create("cyclic-reference");
                try { return NormalizePdfNode(resolved, objects, visited, pageNumbers); }
                finally { visited.Remove(value); }
            }
            return node.DeepClone();
        }
        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array) result.Add(NormalizePdfNode(item, objects, visited, pageNumbers));
            return result;
        }
        if (node is JsonObject objectNode)
        {
            var result = new JsonObject();
            foreach (var property in objectNode.OrderBy(property => property.Key, StringComparer.Ordinal))
                result[property.Key] = NormalizePdfNode(property.Value, objects, visited, pageNumbers);
            return result;
        }
        return node.DeepClone();
    }

    private static void ReplaceOutlines(JsonObject root, JsonObject catalog, JsonObject qpdfObjects,
        Dictionary<string, JsonObject> objectValues, IReadOnlyList<PdfBookmarkEdit> edits, IReadOnlyList<string> pageRefs,
        IReadOnlySet<string> namedDestinations)
    {
        if (edits.Count == 0)
        {
            root["outlines"] = new JsonArray();
            catalog.Remove("/Outlines");
            return;
        }

        var maxObjectId = root["qpdf"]!.AsArray()[0]! ["maxobjectid"]!.GetValue<int>();
        var rootReference = catalog["/Outlines"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rootReference))
        {
            rootReference = $"{++maxObjectId} 0 R";
            catalog["/Outlines"] = rootReference;
            qpdfObjects["obj:" + rootReference] = new JsonObject { ["value"] = new JsonObject { ["/Type"] = "/Outlines" } };
            objectValues[rootReference] = qpdfObjects["obj:" + rootReference]!["value"]!.AsObject();
        }

        var nodes = edits.Select(edit => new OutlineBuildNode(edit, $"{++maxObjectId} 0 R")).ToArray();
        var roots = new List<OutlineBuildNode>();
        var stack = new List<OutlineBuildNode>();
        foreach (var node in nodes)
        {
            while (stack.Count > node.Edit.Level) stack.RemoveAt(stack.Count - 1);
            if (node.Edit.Level > stack.Count) throw new InvalidOperationException("Bookmark levels must increase by at most one level.");
            if (stack.Count == 0) roots.Add(node);
            else stack[^1].Children.Add(node);
            if (stack.Count == node.Edit.Level) stack.Add(node);
            else stack[node.Edit.Level] = node;
        }

        var rootObject = objectValues[rootReference];
        rootObject.Remove("/First");
        rootObject.Remove("/Last");
        rootObject.Remove("/Count");
        rootObject["/Type"] = "/Outlines";
        rootObject["/First"] = roots[0].Reference;
        rootObject["/Last"] = roots[^1].Reference;
        rootObject["/Count"] = CountDescendants(roots);
        var topLevel = new JsonArray();
        for (var index = 0; index < roots.Count; index++)
        {
            WriteOutlineNode(roots[index], rootReference, qpdfObjects, pageRefs, namedDestinations, topLevel, index == 0 ? null : roots[index - 1].Reference,
                index == roots.Count - 1 ? null : roots[index + 1].Reference);
        }
        root["outlines"] = topLevel;
        root["qpdf"]!.AsArray()[0]! ["maxobjectid"] = maxObjectId;
    }

    private static void WriteOutlineNode(OutlineBuildNode node, string parentReference, JsonObject qpdfObjects,
        IReadOnlyList<string> pageRefs, IReadOnlySet<string> namedDestinations, JsonArray? topLevel, string? previous, string? next)
    {
        var destination = Destination(node.Edit.PageNumber, node.Edit.NamedDestination, pageRefs, namedDestinations);
        var value = new JsonObject
        {
            ["/Title"] = "u:" + node.Edit.Title,
            ["/Parent"] = parentReference,
            ["/Dest"] = destination.DeepClone()
        };
        if (previous is not null) value["/Prev"] = previous;
        if (next is not null) value["/Next"] = next;
        if (node.Children.Count > 0)
        {
            value["/First"] = node.Children[0].Reference;
            value["/Last"] = node.Children[^1].Reference;
            value["/Count"] = CountDescendants(node.Children);
        }
        qpdfObjects["obj:" + node.Reference] = new JsonObject { ["value"] = value };
        var display = new JsonObject
        {
            ["object"] = node.Reference,
            ["title"] = node.Edit.Title,
            ["dest"] = destination.DeepClone(),
            ["kids"] = new JsonArray(),
            ["open"] = true
        };
        if (topLevel is not null) topLevel.Add(display);
        var childTop = display["kids"]!.AsArray();
        for (var index = 0; index < node.Children.Count; index++)
            WriteOutlineNode(node.Children[index], node.Reference, qpdfObjects, pageRefs, namedDestinations, childTop,
                index == 0 ? null : node.Children[index - 1].Reference,
                index == node.Children.Count - 1 ? null : node.Children[index + 1].Reference);
    }

    private static int CountDescendants(IReadOnlyList<OutlineBuildNode> nodes)
        => nodes.Sum(node => 1 + CountDescendants(node.Children));

    private sealed record OutlineBuildNode(PdfBookmarkEdit Edit, string Reference)
    {
        public List<OutlineBuildNode> Children { get; } = [];
    }

    private static (int Page, int Annotation) ParseObjectPath(string objectPath)
    {
        var pageStart = objectPath.IndexOf("page[", StringComparison.Ordinal);
        var pageEnd = objectPath.IndexOf(']', pageStart + 5);
        var annotationStart = objectPath.IndexOf("/annotation[", StringComparison.Ordinal);
        var annotationEnd = objectPath.IndexOf(']', annotationStart + 12);
        if (pageStart < 0 || pageEnd < 0 || annotationStart < 0 || annotationEnd < 0 ||
            !int.TryParse(objectPath[(pageStart + 5)..pageEnd], out var page) ||
            !int.TryParse(objectPath[(annotationStart + 12)..annotationEnd], out var annotation))
            throw new InvalidOperationException("The link object path is malformed.");
        return (page, annotation);
    }

    private static bool AnnotationTargetMatches(JsonObject annotation, string expected, IReadOnlyDictionary<string, int> pageNumbers)
    {
        var action = annotation["/A"]?.AsObject();
        var actionKind = action?["/S"]?.GetValue<string>();
        var current = actionKind == "/URI"
            ? DecodeQpdfString(action?["/URI"]?.GetValue<string>())
            : (actionKind == "/GoToR"
                ? DestinationText(ReadRemoteDestination(action?["/D"]))
                : DestinationText(ReadDestination(action?["/D"] ?? annotation["/Dest"], pageNumbers)));
        var normalizedCurrent = DecodeQpdfString(current)?.TrimStart('#');
        var normalizedExpected = DecodeQpdfString(expected)?.TrimStart('#');
        return string.Equals(normalizedCurrent, normalizedExpected, StringComparison.Ordinal);
    }

    private static string? DestinationText((int? PageNumber, string? NamedDestination, bool IsValid) destination)
        => destination.NamedDestination ?? destination.PageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static HashSet<string> CollectNamedDestinationNames(JsonObject catalog, IReadOnlyDictionary<string, JsonObject> objectValues)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (catalog["/Dests"] is JsonObject direct)
            foreach (var item in direct) names.Add(item.Key.TrimStart('/'));
        if (catalog["/Names"]?.AsObject()?["/Dests"] is { } nameTree)
            CollectNameTreeNames(nameTree, objectValues, names, new HashSet<string>(StringComparer.Ordinal));
        return names;
    }

    private static void CollectNameTreeNames(JsonNode node, IReadOnlyDictionary<string, JsonObject> objectValues,
        ISet<string> names, ISet<string> visited)
    {
        var resolved = node.GetValueKind() == JsonValueKind.String && objectValues.TryGetValue(node.GetValue<string>(), out var value)
            ? value : node.AsObject();
        if (!visited.Add(resolved.ToJsonString())) return;
        if (resolved["/Names"] is JsonArray pairs)
            for (var index = 0; index + 1 < pairs.Count; index += 2)
                if (pairs[index] is JsonValue key) names.Add(DecodeQpdfString(key.GetValue<string>()) ?? string.Empty);
        if (resolved["/Kids"] is JsonArray kids)
            foreach (var child in kids) if (child is not null) CollectNameTreeNames(child, objectValues, names, visited);
    }

    private static JsonNode Destination(int? pageNumber, string? namedDestination, IReadOnlyList<string> pageRefs,
        IReadOnlySet<string> namedDestinations)
    {
        if (pageNumber is { } page && page >= 1 && page <= pageRefs.Count)
            return new JsonArray(pageRefs[page - 1], "/Fit");
        if (!string.IsNullOrWhiteSpace(namedDestination))
        {
            var normalized = namedDestination.TrimStart('#');
            if (!namedDestinations.Contains(normalized)) throw new InvalidOperationException($"Named destination '{normalized}' does not exist.");
            return JsonValue.Create("u:" + normalized)!;
        }
        throw new InvalidOperationException("A PDF destination requires a valid page or named destination.");
    }

    private static JsonObject ActionFor(PdfLinkEdit edit, IReadOnlyList<string> pageRefs, HashSet<string> namedDestinations)
    {
        var target = edit.Target.Trim();
        return edit.TargetKind switch
        {
            PdfLinkTargetKind.NamedDestination when namedDestinations.Contains(target.TrimStart('#'))
                => new JsonObject { ["/S"] = "/GoTo", ["/D"] = "u:" + target.TrimStart('#') },
            PdfLinkTargetKind.Page when int.TryParse(target, out var page) && page >= 1 && page <= pageRefs.Count
                => new JsonObject { ["/S"] = "/GoTo", ["/D"] = new JsonArray(pageRefs[page - 1], "/Fit") },
            PdfLinkTargetKind.RelativeRemotePdf => RemoteAction(target),
            PdfLinkTargetKind.ExternalUri => new JsonObject { ["/S"] = "/URI", ["/URI"] = "u:" + target },
            _ => throw new InvalidOperationException("The requested PDF link target is invalid.")
        };
    }

    private static JsonObject RemoteAction(string target)
    {
        var remote = ParseRemoteTarget(target);
        var action = new JsonObject { ["/S"] = "/GoToR", ["/F"] = "u:" + remote.File };
        action["/D"] = remote.PageNumber is { } page
            ? new JsonArray(page - 1, "/Fit")
            : "u:" + (remote.NamedDestination ?? "overview");
        return action;
    }

    private static (string File, int? PageNumber, string? NamedDestination) ParseRemoteTarget(string target)
    {
        var parsed = PdfRemoteTarget.Parse(target);
        return (parsed.File, parsed.PageNumber, parsed.NamedDestination);
    }

    private static void ExpandNavigationObjects(JsonObject root)
    {
        var table = root["qpdf"]?[1] as JsonObject ?? throw new InvalidDataException("Missing PDF objects.");
        JsonNode? Resolve(JsonNode? token)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (token is JsonValue scalar && scalar.TryGetValue<string>(out var reference) && table.TryGetPropertyValue("obj:" + reference, out var record))
            {
                if (visited.Count >= 64 || !visited.Add(reference)) throw new InvalidDataException("Cyclic PDF object reference.");
                token = record?["value"] ?? throw new InvalidDataException("Navigation cannot refer to a stream.");
            }
            return token;
        }
        JsonNode? DestinationValue(JsonNode? node)
        {
            var resolved = Resolve(node);
            if (resolved is JsonObject dictionary) resolved = Resolve(dictionary["/D"]);
            return resolved?.DeepClone();
        }
        void Action(JsonObject node)
        {
            if (node["/A"] is { } raw)
            {
                var action = Resolve(raw)?.DeepClone() as JsonObject ?? throw new InvalidDataException("Invalid PDF action.");
                if (action["/D"] is { } destination) action["/D"] = DestinationValue(destination);
                if (action["/F"] is { } file)
                {
                    var value = Resolve(file);
                    if (value is JsonObject spec) value = Resolve(spec["/UF"] ?? spec["/F"]);
                    action["/F"] = value?.DeepClone();
                }
                node["/A"] = action;
            }
            if (node["/Dest"] is { } direct) node["/Dest"] = DestinationValue(direct);
        }
        JsonObject NameTree(JsonNode raw, ISet<JsonNode> visited, int depth)
        {
            var node = Resolve(raw) as JsonObject ?? throw new InvalidDataException("Invalid destination name tree.");
            if (depth > 64 || !visited.Add(node)) throw new InvalidDataException("Cyclic destination name tree.");
            var copy = node.DeepClone().AsObject();
            if (Resolve(node["/Names"]) is JsonArray pairs)
            {
                if (pairs.Count % 2 != 0) throw new InvalidDataException("Incomplete destination pairs.");
                var values = new JsonArray();
                for (var index = 0; index < pairs.Count; index += 2)
                { values.Add(Resolve(pairs[index])?.DeepClone()); values.Add(DestinationValue(pairs[index + 1])); }
                copy["/Names"] = values;
            }
            if (Resolve(node["/Kids"]) is JsonArray kids)
            {
                var children = new JsonArray();
                foreach (var child in kids) children.Add(NameTree(child!, visited, depth + 1));
                copy["/Kids"] = children;
            }
            visited.Remove(node);
            return copy;
        }
        var objects = BuildObjectMap(root);
        foreach (var item in objects.Values)
        {
            if (IsType(item, "/Page") && item["/Annots"] is { } raw)
            {
                var annotations = Resolve(raw) as JsonArray ?? throw new InvalidDataException("Invalid PDF annotation array.");
                var expanded = new JsonArray();
                foreach (var token in annotations)
                {
                    var annotation = Resolve(token)?.DeepClone() as JsonObject ?? throw new InvalidDataException("Invalid PDF annotation.");
                    Action(annotation); expanded.Add(annotation);
                }
                item["/Annots"] = expanded;
            }
            if (item.ContainsKey("/Title")) Action(item);
            if (!IsType(item, "/Catalog")) continue;
            if (item["/Dests"] is { } dests)
            {
                var dictionary = Resolve(dests) as JsonObject ?? throw new InvalidDataException("Invalid PDF destinations.");
                var values = new JsonObject();
                foreach (var pair in dictionary) values[pair.Key] = DestinationValue(pair.Value);
                item["/Dests"] = values;
            }
            if (item["/Names"] is { } names)
            {
                var dictionary = Resolve(names)?.DeepClone() as JsonObject ?? throw new InvalidDataException("Invalid PDF names dictionary.");
                if (dictionary["/Dests"] is { } tree) dictionary["/Dests"] = NameTree(tree, new HashSet<JsonNode>(ReferenceEqualityComparer.Instance), 0);
                item["/Names"] = dictionary;
            }
        }
    }

    private static JsonNode? ResolveOriginalNode(JsonObject root, JsonNode? node)
    {
        var table = root["qpdf"]?[1] as JsonObject ?? throw new InvalidDataException("Missing PDF objects.");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (node is JsonValue scalar && scalar.TryGetValue<string>(out var reference) && table.TryGetPropertyValue("obj:" + reference, out var record))
        {
            if (visited.Count >= 64 || !visited.Add(reference)) throw new InvalidDataException("Cyclic PDF object reference.");
            node = record?["value"] ?? throw new InvalidDataException("Navigation cannot refer to a stream.");
        }
        return node;
    }

    private static Dictionary<string, JsonObject> BuildObjectMap(JsonObject root)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (root["qpdf"] is not JsonArray qpdf || qpdf.Count < 2 || qpdf[1] is not JsonObject objects) return map;
        foreach (var property in objects)
            if (property.Value is JsonObject item)
            {
                if (item["value"] is JsonObject value) map[property.Key[4..]] = value;
                else if (item["stream"] is JsonObject) map[property.Key[4..]] = item;
            }
        return map;
    }

    private static bool IsSupportedVersion(string stdout, string supportedVersion)
    {
        var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith("qpdf version ", StringComparison.Ordinal));
        return line is not null && string.Equals(line["qpdf version ".Length..].Trim(), supportedVersion, StringComparison.Ordinal);
    }

    private static bool IsType(JsonObject value, string expected) => string.Equals(value["/Type"]?.GetValue<string>(), expected, StringComparison.Ordinal);

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, long maxOutputBytes, CancellationToken cancellationToken)
    {
        using var process = CreateProcess(arguments);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        options.ProcessStartedCallback?.Invoke(process.Id);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            var stdout = ReadLimitedAsync(process.StandardOutput.BaseStream, maxOutputBytes, linked.Token);
            var stderr = ReadLimitedAsync(process.StandardError.BaseStream, Math.Min(maxOutputBytes, 256 * 1024), linked.Token);
            var wait = process.WaitForExitAsync(linked.Token);
            await ObserveReadersAsync(process, wait, stdout, stderr);
            return new(process.ExitCode, await stdout, await stderr, false, false, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(-1, string.Empty, string.Empty, true, false, false);
        }
        catch (OutputLimitException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(-1, string.Empty, string.Empty, false, true, false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(-1, string.Empty, string.Empty, false, false, true);
        }
        catch
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProcessResult> RunToFileAsync(IReadOnlyList<string> arguments, string outputPath, TimeSpan timeout,
        long maxOutputBytes, CancellationToken cancellationToken)
    {
        using var process = CreateProcess(arguments);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        options.ProcessStartedCallback?.Invoke(process.Id);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        Stream? output = null;
        try
        {
            output = options.OutputStreamFactory?.Invoke(outputPath)
                ?? new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
            var stdout = CopyLimitedAsync(process.StandardOutput.BaseStream, output, maxOutputBytes, linked.Token);
            var stderr = ReadLimitedAsync(process.StandardError.BaseStream, Math.Min(maxOutputBytes, 256 * 1024), linked.Token);
            var wait = process.WaitForExitAsync(linked.Token);
            await ObserveReadersAsync(process, wait, stdout, stderr);
            await output.FlushAsync(cancellationToken);
            return new(process.ExitCode, string.Empty, await stderr, false, false, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(-1, string.Empty, string.Empty, true, false, false);
        }
        catch (OutputLimitException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(-1, string.Empty, string.Empty, false, true, false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(-1, string.Empty, string.Empty, false, false, true);
        }
        catch
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (output is not null) await output.DisposeAsync();
        }
    }

    private Process CreateProcess(IReadOnlyList<string> arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in options.ExecutableArguments) process.StartInfo.ArgumentList.Add(argument);
        return process;
    }

    private static async Task ObserveReadersAsync(Process process, Task wait, Task stdout, Task stderr)
    {
        var stdoutDone = false;
        var stderrDone = false;
        while (!wait.IsCompleted)
        {
            var pending = new List<Task> { wait };
            if (!stdoutDone) pending.Add(stdout);
            if (!stderrDone) pending.Add(stderr);
            var completed = await Task.WhenAny(pending);
            if (completed == stdout) { stdoutDone = true; await stdout; }
            if (completed == stderr) { stderrDone = true; await stderr; }
        }
        await wait;
        if (!stdoutDone) await stdout;
        if (!stderrDone) await stderr;
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        using var output = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maxBytes) throw new OutputLimitException();
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return;
            var remaining = maxBytes - destination.Position;
            if (remaining <= 0) throw new OutputLimitException();
            if (read > remaining)
            {
                await destination.WriteAsync(buffer.AsMemory(0, (int)remaining), cancellationToken);
                throw new OutputLimitException();
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task WriteExclusiveAsync(string path, string contents, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static PdfEngineResult Failure(string path, string code, TimeSpan duration)
        => new(false, path, string.Empty, code, duration);

    private static TimeSpan Elapsed(long timestamp) => Stopwatch.GetElapsedTime(timestamp);

    private sealed class OutputLimitException : Exception;
    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut, bool OutputLimitExceeded, bool Cancelled);
}
