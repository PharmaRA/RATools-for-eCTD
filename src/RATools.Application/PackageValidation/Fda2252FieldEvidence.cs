namespace RATools.Application.PackageValidation;

public enum PackageFormRawFieldState { Unknown, Missing, Empty, Text }
public sealed record PackageFormRawField(string NodePath, PackageFormRawFieldState State, string? RawText);

// Raw datasets evidence only: not a rendered value, application identity, main-form
// classification, script evaluation or FDA5 compliance result.
public sealed record PackageFormFieldEvidence(PackageFormTemplateMatch Template, string DatasetsSha256,
    PackageFormRawField ApplicationType, PackageFormRawField ApplicationNumber,
    PackageFormRawField ContinuationNumber, string AlgorithmVersion);

internal static class Fda2252FieldEvidence
{
    internal const string AlgorithmVersion = "fda2252-raw-datasets-v1";
    private const string SourceSha256 = "921983dcb9ac14308617684c52e516f283cdf343adb7e0f3ab52fef09b1b1e7e";
    private const string TemplateSha256 = "c761fd02da088844dea36e67570344ffbc13213e00b11849b6bdfe43fab506fc";
    private const string DataRoot = "/{http%3A%2F%2Fwww.xfa.org%2Fschema%2Fxfa-data%2F1.0%2F}datasets[1]/{http%3A%2F%2Fwww.xfa.org%2Fschema%2Fxfa-data%2F1.0%2F}data[1]";
    private const string FormRoot = DataRoot + "/{}form_2252[1]";
    private const string PageRoot = FormRoot + "/{}Page1[1]";
    private const string ApplicationRoot = PageRoot + "/{}application_info[1]";

    internal static PackageFormFieldEvidence? Read(PackagePdfDocument document, PackageFormTemplateMatch match,
        PackageReadLimits limits, ref int work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (match.SourceFormId != "fda-form-2252" || match.SourcePdfSha256 != SourceSha256 || match.TemplateSha256 != TemplateSha256 ||
            match.AlgorithmVersion != FdaXfaTemplateEvidence.AlgorithmVersion || match.LogicalPath != document.LogicalPath ||
            match.CapturedSha256 != document.CapturedSha256 || document.Facts?.Form.Xfa is not { ReadComplete: true } xfa ||
            xfa.IncompleteReasons.Count != 0) return null;
        PackageXfaPacket? datasets = null;
        foreach (var packet in xfa.Packets)
        {
            token.ThrowIfCancellationRequested(); if (work-- <= 0) return null;
            if (packet.Name != "datasets") continue;
            if (datasets is not null) return null;
            datasets = packet;
        }
        if (datasets is null) return null;
        var type = new Field("db_appl_type"); var number = new Field("db_aplcnt_nmbr"); var continuation = new Field("db_aplcnt_nmbr_2");
        var form = new Ancestor("form_2252", FormRoot); var page = new Ancestor("Page1", PageRoot); var application = new Ancestor("application_info", ApplicationRoot);
        var paths = new HashSet<string>(StringComparer.Ordinal); var consistent = true; var nodes = 0; long characters = 0;
        foreach (var node in xfa.Data)
        {
            token.ThrowIfCancellationRequested(); if (work-- <= 0 || ++nodes > limits.MaxXmlNodes) return null;
            var size = (long)node.NodePath.Length + node.LocalName.Length + node.NamespaceUri.Length + node.DirectText.Length;
            if (size > limits.MaxXmlCharacters - characters) return null;
            characters += size;
            if (string.IsNullOrWhiteSpace(node.NodePath) || !paths.Add(node.NodePath)) consistent = false;
            form.Observe(node); page.Observe(node); application.Observe(node);
            type.Observe(node); number.Observe(node); continuation.Observe(node);
        }
        token.ThrowIfCancellationRequested();
        var qualifiedAncestors = consistent && form.Qualified && page.Qualified && application.Qualified;
        return new(match, datasets.Sha256, type.Finish(qualifiedAncestors), number.Finish(qualifiedAncestors),
            continuation.Finish(qualifiedAncestors), AlgorithmVersion);
    }

    private sealed class Ancestor(string name, string path)
    {
        private int _count;
        private bool _valid;
        internal bool Qualified => _count == 1 && _valid;
        internal void Observe(PackageXfaDataNode node)
        {
            if (node.LocalName != name && node.NodePath != path) return;
            _count++;
            _valid = node.NodePath == path && node.LocalName == name && node.NamespaceUri.Length == 0 &&
                node.HasChildren && string.IsNullOrWhiteSpace(node.DirectText);
        }
    }

    private sealed class Field(string name)
    {
        private readonly string _path = ApplicationRoot + "/{}" + name + "[1]";
        private int _count;
        private bool _ambiguous;
        private string? _value;
        internal void Observe(PackageXfaDataNode node)
        {
            if (node.NodePath.StartsWith(_path + "/", StringComparison.Ordinal)) _ambiguous = true;
            if (node.LocalName != name && node.NodePath != _path) return;
            _count++;
            if (node.NodePath != _path || node.LocalName != name || node.NamespaceUri.Length != 0 || node.HasChildren) _ambiguous = true;
            else _value = node.DirectText;
        }
        internal PackageFormRawField Finish(bool ancestors) => !ancestors || _ambiguous || _count > 1
            ? new(_path, PackageFormRawFieldState.Unknown, null)
            : _count == 0 ? new(_path, PackageFormRawFieldState.Missing, null)
            : new(_path, _value!.Length == 0 ? PackageFormRawFieldState.Empty : PackageFormRawFieldState.Text, _value);
    }
}
