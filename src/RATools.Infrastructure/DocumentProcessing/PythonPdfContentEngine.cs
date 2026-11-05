using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;

namespace RATools.Infrastructure.DocumentProcessing;

public sealed class PdfContentEngineOptions
{
    public bool Enabled { get; set; }
    public string PythonExecutablePath { get; set; } = "";
    public string PythonVersion { get; set; } = "3.11.9";
    public string ScriptPath { get; set; } = "";
    public string ScriptSha256 { get; set; } = "";
    public string ScratchDirectory { get; set; } = "";
    public string? TessdataDirectory { get; set; }
    public long MaxInputBytes { get; set; } = 512 * 1024 * 1024;
    public long MaxOutputBytes { get; set; } = 512 * 1024 * 1024;
    public long MaxReportBytes { get; set; } = 1024 * 1024;
    public long MaxPixels { get; set; } = 24_000_000;
    public long MaxMemoryBytes { get; set; } = 1024L * 1024 * 1024;
    public TimeSpan MaxCpuTime { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan InspectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class PythonPdfContentEngine(PdfContentEngineOptions options, QpdfProcessingOptions qpdf) : IPdfContentEngine, IDisposable
{
    private const string Engine = "pymupdf-1.28.2-content-v1";
    private const string ModelHash = "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Unknown = ["OCR_TEXT_ACCURACY", "REGIONAL_CONFORMANCE", "PDF_A_CONFORMANCE", "SIGNATURE_VALIDITY", "UNSUPPORTED_FONT_FAMILIES"];
    private readonly SemaphoreSlim _slot = new(1, 1);

    public async Task<PdfContentCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await CapabilitiesForInspectionAsync(cancellationToken).ConfigureAwait(false); }
        finally { _slot.Release(); }
    }

    public async Task<PdfContentInspection> InspectAsync(DocumentVersion source, CancellationToken cancellationToken = default)
    {
        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capabilities = await CapabilitiesForInspectionAsync(cancellationToken).ConfigureAwait(false);
            if (!capabilities.Enabled)
                return new(source.Id, source.Sha256, 0, "Unknown", [], [], [], capabilities, capabilities.BlockReasons);
            if (await HashFileAsync(source.StoragePath, options.MaxInputBytes, cancellationToken).ConfigureAwait(false) != source.Sha256)
                throw new InvalidDataException("The captured PDF content changed before inspection.");
            var prefix = NewPrefix();
            _ = await RunWorkerAsync(new { mode = "inspect", sourcePath = source.StoragePath, receiptPath = prefix + ".receipt.json",
                maxInputBytes = options.MaxInputBytes, maxPixels = options.MaxPixels }, prefix, null, options.InspectionTimeout, cancellationToken).ConfigureAwait(false);
            var data = await ReadReportAsync<InspectionData>(prefix + ".receipt.json", cancellationToken).ConfigureAwait(false);
            if (data.EngineVersion != Engine || data.SourceSha256 != source.Sha256 || data.PageCount is < 0 or > 500 ||
                data.SearchablePages is null || data.ScannedPages is null || data.MissingFonts is null || data.BlockReasons is null)
                throw new InvalidDataException("The content inspection does not match its captured source.");
            if (data.BlockReasons.Count == 0 && (!data.SearchablePages.Concat(data.ScannedPages).Order().SequenceEqual(Enumerable.Range(1, data.PageCount)) || data.PageCount == 0))
                throw new InvalidDataException("The content inspection page inventory is incomplete.");
            await VerifyBindingAsync(capabilities.EngineBinding!, cancellationToken).ConfigureAwait(false);
            return new(source.Id, source.Sha256, data.PageCount, data.SourceVersion ?? "Unknown", data.SearchablePages,
                data.ScannedPages, data.MissingFonts, capabilities, data.BlockReasons);
        }
        finally { _slot.Release(); }
    }

    public async Task<PdfContentEvidence> ExecuteAsync(DocumentVersion source, string outputPath, PdfContentParameters parameters,
        PdfContentInspection inspection, TimeSpan timeout, long maxOutputBytes, CancellationToken cancellationToken = default)
    {
        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            parameters = parameters.Normalize();
            await VerifyBindingAsync(inspection.Capabilities.EngineBinding!, cancellationToken).ConfigureAwait(false);
            parameters.ValidateAgainst(inspection);
            if (inspection.SourceVersionId != source.Id || inspection.Sha256 != source.Sha256 ||
                await HashFileAsync(source.StoragePath, options.MaxInputBytes, cancellationToken).ConfigureAwait(false) != source.Sha256)
                throw new InvalidDataException("Content processing source no longer matches its preview.");
            if (File.Exists(outputPath) || File.Exists(outputPath + ".receipt.json")) throw new IOException("Content result or receipt already exists.");
            var maximum = Math.Min(maxOutputBytes, options.MaxOutputBytes);
            var run = await RunWorkerAsync(new { sourcePath = source.StoragePath, outputPath, receiptPath = outputPath + ".receipt.json", parameters,
                tessdata = options.TessdataDirectory, qpdf = qpdf.ExecutablePath, maxInputBytes = options.MaxInputBytes,
                maxOutputBytes = maximum, maxPixels = options.MaxPixels }, outputPath, outputPath, timeout, cancellationToken).ConfigureAwait(false);
            var receipt = await ReadReportAsync<PdfContentReceipt>(outputPath + ".receipt.json", cancellationToken).ConfigureAwait(false);
            var evidence = new PdfContentEvidence(inspection.Capabilities.EngineBinding!,
                await HashFileAsync(outputPath + ".receipt.json", options.MaxReportBytes, cancellationToken).ConfigureAwait(false), receipt,
                new(run.PeakMemoryBytes, run.CpuMilliseconds, run.WallMilliseconds, options.MaxMemoryBytes,
                    (long)options.MaxCpuTime.TotalMilliseconds, options.MaxInputBytes, maximum, options.MaxReportBytes, options.MaxPixels));
            await VerifyEvidenceCoreAsync(source, outputPath, parameters, inspection, evidence, cancellationToken).ConfigureAwait(false);
            return evidence;
        }
        finally { _slot.Release(); }
    }

    public async Task VerifyEvidenceAsync(DocumentVersion source, string outputPath, PdfContentParameters parameters,
        PdfContentInspection inspection, PdfContentEvidence evidence, CancellationToken cancellationToken = default)
    {
        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await VerifyEvidenceCoreAsync(source, outputPath, parameters, inspection, evidence, cancellationToken).ConfigureAwait(false); }
        finally { _slot.Release(); }
    }

    private async Task VerifyEvidenceCoreAsync(DocumentVersion source, string output, PdfContentParameters parameters,
        PdfContentInspection inspection, PdfContentEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.Receipt is null || evidence.Resources is null || evidence.EngineBinding != inspection.Capabilities.EngineBinding ||
            inspection.SourceVersionId != source.Id || inspection.Sha256 != source.Sha256)
            throw new InvalidDataException("The content result is bound to another source or engine.");
        await VerifyBindingAsync(evidence.EngineBinding, cancellationToken).ConfigureAwait(false);
        if (await HashFileAsync(output + ".receipt.json", options.MaxReportBytes, cancellationToken).ConfigureAwait(false) != evidence.ReceiptSha256)
            throw new InvalidDataException("The content receipt changed after processing.");
        var receipt = await ReadReportAsync<PdfContentReceipt>(output + ".receipt.json", cancellationToken).ConfigureAwait(false);
        if (JsonSerializer.Serialize(receipt, Json) != JsonSerializer.Serialize(evidence.Receipt, Json) ||
            receipt.EngineVersion != Engine || receipt.SourceSha256 != source.Sha256 ||
            await HashFileAsync(source.StoragePath, options.MaxInputBytes, cancellationToken).ConfigureAwait(false) != source.Sha256 ||
            receipt.OutputSha256 != await HashFileAsync(output, options.MaxOutputBytes, cancellationToken).ConfigureAwait(false) ||
            receipt.OutputBytes != new FileInfo(output).Length || receipt.OutputBytes > evidence.Resources.MaxOutputBytes ||
            JsonSerializer.Serialize(receipt.Parameters, Json) != JsonSerializer.Serialize(parameters.Normalize(), Json) ||
            receipt.RegulatoryConformance != "NotEvaluated" || receipt.ElapsedMilliseconds < 0 ||
            (parameters.TargetVersion is not null && receipt.OutputVersion != parameters.TargetVersion))
            throw new InvalidDataException("The content receipt does not match its source, output and accepted parameters.");
        if (receipt.Pages is null || !receipt.Pages.Select(page => page.Page).SequenceEqual(Enumerable.Range(1, inspection.PageCount)) ||
            receipt.Pages.Any(page => page.RenderSha256 is null || page.RenderSha256.Count != 2 ||
                !IsDigest(page.RenderSha256.GetValueOrDefault("72")) || !IsDigest(page.RenderSha256.GetValueOrDefault("150")) || !IsDigest(page.TextSha256)))
            throw new InvalidDataException("The content receipt lacks complete page fidelity evidence.");
        if (parameters.Ocr is { } ocr)
        {
            var proof = receipt.Ocr ?? throw new InvalidDataException("OCR evidence is missing.");
            if (proof.ProcessedPages is null || proof.AlreadySearchablePages is null || proof.Language != "eng" || proof.Dpi != 150 || proof.ModelSha256 != ModelHash || !proof.RequiresTextReview ||
                !proof.ProcessedPages.SequenceEqual(ocr.Pages.Where(inspection.ScannedPages.Contains)) ||
                !proof.AlreadySearchablePages.SequenceEqual(ocr.Pages.Where(inspection.SearchablePages.Contains)))
                throw new InvalidDataException("OCR page/model evidence does not match the explicit plan.");
        }
        else if (receipt.Ocr is not null) throw new InvalidDataException("Unrequested OCR was reported.");
        if (parameters.EmbedStandardFonts)
        {
            var proof = receipt.Fonts ?? throw new InvalidDataException("Font embedding evidence is missing.");
            if (proof.Embedded is null || proof.RegulatoryConformance != "NotEvaluated" ||
                !proof.Embedded.Select(font => font.Name).Distinct().Order(StringComparer.Ordinal).SequenceEqual(inspection.MissingFonts.Select(font => font.Name).Order(StringComparer.Ordinal)) ||
                proof.Embedded.Any(font => font.ProgramBytes < 1 || !IsDigest(font.ProgramSha256)))
                throw new InvalidDataException("Font evidence does not cover the inspected missing font set.");
        }
        else if (receipt.Fonts is not null) throw new InvalidDataException("Unrequested font processing was reported.");
        // PeakJobMemoryUsed may include rejected allocation attempts. Preserve the
        // raw observed peak; enforcement comes from assigning the suspended worker
        // to the configured job before it can execute or create descendants.
        if (evidence.Resources.PeakMemoryBytes < 1 || evidence.Resources.MemoryLimitBytes != options.MaxMemoryBytes ||
            evidence.Resources.CpuLimitMilliseconds != (long)options.MaxCpuTime.TotalMilliseconds ||
            evidence.Resources.MaxInputBytes != options.MaxInputBytes || evidence.Resources.MaxOutputBytes < 1 ||
            evidence.Resources.MaxOutputBytes > options.MaxOutputBytes || evidence.Resources.MaxReportBytes != options.MaxReportBytes ||
            evidence.Resources.MaxPixels != options.MaxPixels || evidence.Resources.CpuMilliseconds < 0 ||
            evidence.Resources.CpuMilliseconds > evidence.Resources.CpuLimitMilliseconds || evidence.Resources.WallMilliseconds < 0)
            throw new InvalidDataException("The content process exceeded its recorded resource limits.");
    }

    private async Task<PdfContentCapabilities> CapabilitiesCoreAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var scriptHash = await HashFileAsync(options.ScriptPath, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(scriptHash, options.ScriptSha256, StringComparison.OrdinalIgnoreCase))
            throw new PdfProcessingRejectedException("The qualified content script fingerprint changed.");
        var pythonHash = await HashFileAsync(options.PythonExecutablePath, 128 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        var qpdfHash = await HashFileAsync(qpdf.ExecutablePath, 256 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        var prefix = NewPrefix();
        var qpdfProbe = await PdfContentProcess.RunAsync(qpdf.ExecutablePath, ["--version"], prefix + ".qpdf", options.MaxMemoryBytes,
            options.MaxCpuTime, options.ProbeTimeout, 65536, options.MaxOutputBytes, options.MaxReportBytes, null, cancellationToken).ConfigureAwait(false);
        if (qpdfProbe.ExitCode != 0 || qpdfProbe.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() != "qpdf version 12.4.1")
            throw new PdfProcessingRejectedException("The content version tool is not qpdf 12.4.1.");
        _ = await RunWorkerAsync(new { mode = "probe", receiptPath = prefix + ".receipt.json" }, prefix, null, options.ProbeTimeout, cancellationToken).ConfigureAwait(false);
        var probe = await ReadReportAsync<ProbeData>(prefix + ".receipt.json", cancellationToken).ConfigureAwait(false);
        if (probe.EngineVersion != Engine || probe.PythonVersion != options.PythonVersion || probe.PyMuPdfVersion != "1.28.2" || !IsDigest(probe.RuntimeFingerprint))
            throw new PdfProcessingRejectedException("The content runtime version is not qualified.");
        var ocr = false; string? modelHash = null;
        if (!string.IsNullOrWhiteSpace(options.TessdataDirectory))
        {
            var model = Path.Combine(options.TessdataDirectory, "eng.traineddata");
            if (File.Exists(model)) { modelHash = await HashFileAsync(model, 32 * 1024 * 1024, cancellationToken).ConfigureAwait(false); ocr = modelHash == ModelHash; }
        }
        var binding = Hash(JsonSerializer.Serialize(new { Schema = "pdf-content-host-v1", scriptHash, pythonHash, qpdfHash,
            probe, modelHash, options.MaxInputBytes, options.MaxOutputBytes, options.MaxReportBytes, options.MaxPixels,
            options.MaxMemoryBytes, CpuTicks = options.MaxCpuTime.Ticks }, Json));
        return new(true, Engine + ":" + binding, ocr, true, true, "eng", 150, 500, options.MaxPixels,
            ocr ? [] : ["OCR_MODEL_NOT_CONFIGURED_OR_NOT_QUALIFIED"], Unknown);
    }

    private async Task<PdfContentCapabilities> CapabilitiesForInspectionAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled) return Disabled("CONTENT_NOT_CONFIGURED");
        if (!OperatingSystem.IsWindows()) return Disabled("CONTENT_HOST_RESOURCE_ENFORCEMENT_NOT_QUALIFIED");
        try { return await CapabilitiesCoreAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or
            NotSupportedException or System.ComponentModel.Win32Exception or JsonException or TimeoutException)
        { return Disabled("CONTENT_DEPENDENCY_OR_CONFIGURATION_NOT_QUALIFIED"); }
    }

    private async Task VerifyBindingAsync(string expected, CancellationToken cancellationToken)
    {
        var current = await CapabilitiesCoreAsync(cancellationToken).ConfigureAwait(false);
        if (current.EngineBinding != expected) throw new PdfProcessingRejectedException("Content dependencies changed; create a new inspected plan.");
    }
    private void ValidateConfiguration()
    {
        if (!options.Enabled || !OperatingSystem.IsWindows()) throw new PdfProcessingRejectedException("Content processing is not enabled on a qualified host.");
        foreach (var path in new[] { options.PythonExecutablePath, options.ScriptPath, options.ScratchDirectory, qpdf.ExecutablePath })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Content dependencies and scratch storage require explicit absolute paths.");
        if (!IsDigest(options.ScriptSha256) || qpdf.ExecutableArguments.Count > 0 ||
            options.MaxInputBytes is < 1 or > 536870912 || options.MaxOutputBytes is < 1 or > 536870912 ||
            options.MaxReportBytes is < 1024 or > 16777216 || options.MaxPixels is < 1 or > 24000000 ||
            options.MaxMemoryBytes is < 67108864 or > 4294967296 || options.MaxCpuTime <= TimeSpan.Zero ||
            options.ProbeTimeout <= TimeSpan.Zero || options.InspectionTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Content resource limits or script qualification are invalid.");
    }
    private string NewPrefix()
    {
        var directory = Path.Combine(options.ScratchDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return Path.Combine(directory, "inspection");
    }
    private async Task<ContentProcessResult> RunWorkerAsync(object request, string prefix, string? output, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var requestPath = prefix + ".request.json";
        var json = JsonSerializer.Serialize(request, Json);
        if (Encoding.UTF8.GetByteCount(json) > 65536) throw new ArgumentException("The content request exceeds its limit.");
        await PdfContentProcess.WriteNewAsync(requestPath, json, cancellationToken).ConfigureAwait(false);
        var result = await PdfContentProcess.RunAsync(options.PythonExecutablePath, ["-I", "-B", options.ScriptPath, "--request", requestPath],
            prefix, options.MaxMemoryBytes, options.MaxCpuTime, timeout, 65536, options.MaxOutputBytes, options.MaxReportBytes, output, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new PdfProcessingRejectedException("The qualified PDF content process rejected the input or exceeded a host resource limit.");
        return result;
    }
    private async Task<T> ReadReportAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (stream.Length > options.MaxReportBytes) throw new InvalidDataException("The content report exceeded its limit.");
        var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("The content report is missing.");
    }
    private static async Task<string> HashFileAsync(string path, long limit, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length > limit) throw new InvalidDataException("A content dependency or PDF exceeds its limit.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; long length = 0; int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        { length += read; if (length > limit) throw new InvalidDataException("Content file limit exceeded."); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private PdfContentCapabilities Disabled(string reason) => new(false, null, false, false, false, "eng", 150, 500, options.MaxPixels, [reason], Unknown);
    public void Dispose() => _slot.Dispose();
    private sealed record ProbeData(string EngineVersion, string PythonVersion, string PyMuPdfVersion, string RuntimeFingerprint);
    private sealed record InspectionData(string EngineVersion, string SourceSha256, int PageCount, string? SourceVersion,
        IReadOnlyList<int> SearchablePages, IReadOnlyList<int> ScannedPages, IReadOnlyList<PdfContentFontFact> MissingFonts, IReadOnlyList<string> BlockReasons);
}
