using RATools.Domain.PackageValidation;

namespace RATools.Application.PackageValidation;

internal static class PackageFormBindingReader
{
    internal static PackageFormBindings Read(ICapturedPackageInput target, PackageLifecycleInspection lifecycle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inspections = lifecycle.XmlInspections.Where(xml => xml.InputDigest == target.InputDigest &&
            xml.ProfileSnapshotId == lifecycle.ProfileSnapshotId).ToArray();
        if (inspections.Length != 1) return PackageFormBindings.Unknown;
        var xml = inspections[0];
        var documents = xml.Backbones.Where(document => document.Kind == BackboneKind.UsRegional).ToArray();
        var complete = documents.Length == 1 && !xml.Checks.Any(check =>
            (check.RuleId is "XML-OFFLINE" or "XML-LIMITS") && check.Status != CheckStatus.Pass ||
            check.Findings.Any(finding => finding.Code == "INVALID_REGIONAL_BACKBONE_REFERENCE"));
        var groups = new List<PackageFormGroup>();
        long observations = 0;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            complete &= document.ReadComplete && document.DtdStatus == CheckStatus.Pass;
            var elements = document.Elements.ToDictionary(element => element.NodePath, StringComparer.Ordinal);
            var children = document.Elements.Where(element => element.ParentPath is not null)
                .ToLookup(element => element.ParentPath!, StringComparer.Ordinal);
            var applications = new Dictionary<string, PackageFormApplication>(StringComparer.Ordinal);
            foreach (var element in document.Elements.Where(element => element.LocalName == "application"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Chain(element, "application", "application-set", "admin")) { complete = false; continue; }
                var numbers = children[element.NodePath].Where(child => Plain(child, "application-information"))
                    .SelectMany(info => children[info.NodePath].Where(child => Plain(child, "application-number")))
                    .Select(number => new PackageFormApplicationNumber(number.NodePath, number.Text,
                        Attribute(number, "application-type"), number.Location)).ToArray();
                applications.Add(element.NodePath, new(element.NodePath, Attribute(element, "application-containing-files"), numbers));
            }
            var shared = applications.Values.ToArray();
            var leafGroups = new Dictionary<string, List<ParsedPackageLeaf>>(StringComparer.Ordinal);
            foreach (var leaf in document.Leaves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!elements.TryGetValue(leaf.NodePath, out var element)) { complete = false; continue; }
                var parent = Parent(element);
                var depth = 0;
                while (parent is not null && Plain(parent, "node-extension"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++depth > target.Limits.MaxXmlDepth) { complete = false; parent = null; break; }
                    parent = Parent(parent);
                }
                if (parent is null || !Plain(parent, "form")) continue;
                if (!leafGroups.TryGetValue(parent.NodePath, out var list)) leafGroups.Add(parent.NodePath, list = []);
                list.Add(leaf);
            }
            foreach (var form in document.Elements.Where(element => element.LocalName == "form"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string scope;
                PackageFormApplication[] bound;
                if (Chain(form, "form", "submission-information", "application", "application-set", "admin"))
                {
                    scope = "ApplicationAdmin";
                    var applicationPath = Parent(Parent(form)!)!.NodePath;
                    if (!applications.TryGetValue(applicationPath, out var application)) { complete = false; continue; }
                    bound = [application];
                }
                else if (Chain(form, "form", "m1-1-forms", "m1-regional"))
                { scope = "SharedM1"; bound = shared; }
                else { complete = false; continue; }
                var leaves = leafGroups.GetValueOrDefault(form.NodePath) ?? [];
                observations += 1L + bound.Length + leaves.Count + bound.Sum(application => (long)application.Numbers.Count);
                if (observations > target.Limits.MaxXmlNodes) return new(false, groups);
                groups.Add(new(document.LogicalPath, form.NodePath, scope, Attribute(form, "form-type"),
                    document.ReadComplete, document.DtdStatus, bound, leaves));
            }

            ParsedXmlElement? Parent(ParsedXmlElement element) => element.ParentPath is { } path ? elements.GetValueOrDefault(path) : null;

            bool Chain(ParsedXmlElement element, params string[] names)
            {
                ParsedXmlElement? current = element;
                foreach (var name in names)
                {
                    if (current is null || !Plain(current, name)) return false;
                    current = Parent(current);
                }
                return current is { ParentPath: null, LocalName: "fda-regional", NamespaceUri: "http://www.ich.org/fda" };
            }
        }
        return new(complete, groups);
    }

    private static bool Plain(ParsedXmlElement element, string name) => element.LocalName == name && element.NamespaceUri.Length == 0;
    private static ParsedXmlAttributeValue? Attribute(ParsedXmlElement element, string name) =>
        element.Attributes.Values.SingleOrDefault(attribute => attribute.LocalName == name && attribute.NamespaceUri.Length == 0);
}
