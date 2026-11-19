using RATools.Application.PackageValidation;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace RATools.Infrastructure.PackageValidation;

// Uses the navigation reader's resolver and budget. No JavaScript, appearance synthesis,
// default-value substitution or XFA template/script interpretation is performed.
internal sealed class PdfPigPackageFormReader(Func<IToken?, IToken?> tokenResolver, Action<int> count, PackageReadLimits limits)
{
    private readonly List<PackagePdfFormField> _fields = [];
    private readonly List<string> _reasons = [];
    private readonly HashSet<DictionaryToken> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<IndirectReference, IToken?> _resolved = [];

    private IToken? Resolve(IToken? token)
    {
        if (token is not IndirectReferenceToken reference) return token;
        count(0);
        if (_resolved.TryGetValue(reference.Data, out var cached)) return cached;
        IToken? resolved;
        try { resolved = tokenResolver(token); }
        catch (Exception exception) when (exception is not (OperationCanceledException or PdfInspectionLimitException))
        { Unknown("Form object could not be resolved."); resolved = null; }
        if (resolved is null) Unknown("Unresolved form object.");
        _resolved.Add(reference.Data, resolved);
        return resolved;
    }

    public PackagePdfFormFacts Read(DictionaryToken catalog)
    {
        count(0);
        var raw = Entry(catalog, "AcroForm");
        if (_reasons.Count > 0) return new(null, false, false, [], _reasons);
        if (raw is null or NullToken) return new(false, false, true, []);
        if (Resolve(raw) is not DictionaryToken form)
            return new(true, false, false, [], ["Invalid AcroForm dictionary."]);
        var xfaToken = Entry(form, "XFA");
        var hasXfa = xfaToken is not null;
        if (hasXfa) Unknown("XFA template/script semantics are not qualified; AcroForm values alone are not authoritative.");
        if (Entry(form, "Fields") is not ArrayToken roots) Unknown("Invalid or missing AcroForm Fields array.");
        else foreach (var root in roots.Data) ReadField(root, null, "", new(null, null, null, null), 0);
        var xfa = xfaToken is null ? PackageXfaFacts.Unknown : new PdfPigPackageXfaReader(Resolve, count, limits).Read(xfaToken);
        return new(true, hasXfa, _reasons.Count == 0, _fields, _reasons, xfa);
    }

    private sealed record Inherited(IToken? Type, IToken? Flags, IToken? Value, IToken? Default);

    private void ReadField(IToken raw, DictionaryToken? parent, string prefix, Inherited inherited, int depth)
    {
        count(depth);
        if (Resolve(raw) is not DictionaryToken field || !_visited.Add(field))
        { Unknown("Unresolved, cyclic or repeated form field."); return; }
        if (Entry(field, "Parent") is { } parentToken)
        {
            if (parent is null || !ReferenceEquals(Resolve(parentToken), parent)) Unknown("Form field Parent does not match its tree owner.");
        }
        else if (parent is not null) Unknown("Non-root form field has no Parent.");

        var nameToken = Entry(field, "T");
        var partial = nameToken is null ? null : Text(Resolve(nameToken));
        if (nameToken is not null && (partial is null || partial.Length == 0 || partial.Contains('.', StringComparison.Ordinal)))
            Unknown("Invalid partial form-field name.");
        var name = partial is null ? prefix : prefix.Length == 0 ? partial : prefix + "." + partial;
        var values = new Inherited(Effective(field, "FT", inherited.Type), Effective(field, "Ff", inherited.Flags),
            Effective(field, "V", inherited.Value), Effective(field, "DV", inherited.Default));

        var childFields = new List<IToken>();
        var widgets = new List<IToken>();
        if (Entry(field, "Kids") is { } kidsToken)
        {
            if (Resolve(kidsToken) is not ArrayToken kids) Unknown("Invalid field Kids array.");
            else foreach (var child in kids.Data)
            {
                count(depth + 1);
                if (Resolve(child) is not DictionaryToken childDictionary)
                { Unknown("Unresolved form child."); continue; }
                // Widget-only children are visual instances of this field, not separate values.
                if ((Entry(childDictionary, "Subtype") as NameToken)?.Data == "Widget" &&
                    Entry(childDictionary, "T") is null && Entry(childDictionary, "FT") is null && Entry(childDictionary, "Kids") is null)
                    widgets.Add(child);
                else childFields.Add(child);
            }
        }
        foreach (var widget in widgets) CheckWidget(widget, field, values);
        var type = (Resolve(values.Type) as NameToken)?.Data;
        if (values.Type is not null && type is not ("Tx" or "Btn" or "Ch" or "Sig")) Unknown("Unknown form field type.");
        var flags = Resolve(values.Flags);
        long flagBits = 0;
        if (flags is not null and not NullToken)
        {
            if (flags is NumericToken number && !number.HasDecimalPlaces && number.Long is >= 0 and <= uint.MaxValue) flagBits = number.Long;
            else Unknown("Invalid form-field flags.");
        }
        // Validate this node before child overrides can conceal malformed declarations.
        var current = Values(values.Value, type);
        var defaults = Values(values.Default, type);
        if (childFields.Count > 0)
        {
            if (widgets.Count > 0) Unknown("Field mixes child fields and widget-only children.");
            foreach (var child in childFields) ReadField(child, field, name, values, depth + 1);
            return;
        }

        if (type is not ("Tx" or "Btn" or "Ch" or "Sig")) Unknown("Unknown or missing terminal field type.");
        if (name.Length == 0) Unknown("Terminal form field has no qualified name.");
        if (!_names.Add(name)) Unknown("Duplicate qualified form-field name.");
        _fields.Add(new(name, type ?? "", current, defaults,
            Resolve(values.Value) is not (null or NullToken), (flagBits & 1) != 0));
    }

    private void CheckWidget(IToken token, DictionaryToken parent, Inherited inherited)
    {
        if (Resolve(token) is not DictionaryToken widget || !_visited.Add(widget))
        { Unknown("Repeated or unresolved field widget."); return; }
        if (!ReferenceEquals(Entry(widget, "Parent"), parent)) Unknown("Widget Parent does not match its field.");
        CheckDuplicate("V", inherited.Value);
        CheckDuplicate("DV", inherited.Default);
        CheckDuplicate("Ff", inherited.Flags);

        void CheckDuplicate(string key, IToken? original)
        {
            if (Entry(widget, key) is not { } extra) return;
            var value = Resolve(extra);
            var expected = Resolve(original);
            var same = (value, expected) switch
            {
                (NumericToken left, NumericToken right) => left.Data == right.Data,
                (NameToken left, NameToken right) => left.Data == right.Data,
                _ => Text(value) is { } text && text == Text(expected)
            };
            if (!same) Unknown("Widget-only child carries conflicting field properties.");
        }
    }

    private IToken? Effective(DictionaryToken field, string key, IToken? inherited)
    {
        return Entry(field, key) ?? inherited;
    }

    private string[] Values(IToken? token, string? type)
    {
        var value = Resolve(token);
        if (value is null or NullToken) return [];
        if (type == "Sig" && value is DictionaryToken) return []; // Not a textual identity field.
        if (type == "Btn" && value is NameToken name) return [name.Data];
        if (type is "Tx" or "Ch" && Text(value) is { } text) return [text];
        if (type == "Ch" && value is ArrayToken array)
        {
            var result = new List<string>();
            foreach (var item in array.Data)
            {
                count(0);
                if (Text(Resolve(item)) is { } entry) result.Add(entry);
                else Unknown("Unsupported choice-field value.");
            }
            return result.ToArray();
        }
        Unknown("Unsupported form-field value for its type.");
        return [];
    }

    private void Unknown(string reason)
    {
        if (!_reasons.Contains(reason, StringComparer.Ordinal)) _reasons.Add(reason);
    }

    private static string? Text(IToken? token) => token switch { StringToken text => text.Data, HexToken hex => hex.Data, _ => null };
    private IToken? Entry(DictionaryToken dictionary, string name)
    {
        var value = Resolve(Get(dictionary, name));
        return value is NullToken ? null : value;
    }
    private static IToken? Get(DictionaryToken dictionary, string name) => dictionary.TryGet(NameToken.Create(name), out var value) ? value : null;
}
