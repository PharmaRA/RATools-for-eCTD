using System.Text.Json;
using System.Text.Json.Nodes;

namespace RATools.Api.Controllers;

// The archived projection is stored whole; the API returns one explicit view and
// paginates leaves so a large dossier does not have to be materialized in the UI.
internal static class PackageProjectionView
{
    private static readonly string[] Views = ["history", "current", "sequence"];

    public static string Render(string json, string view, string? sequenceNumber, int offset, int limit)
    {
        if (!Views.Contains(view, StringComparer.Ordinal))
            throw new ArgumentException("Use view=history, current or sequence.");
        if (view == "sequence" && (sequenceNumber is null || sequenceNumber.Length != 4 || sequenceNumber.Any(character => character is < '0' or > '9')))
            throw new ArgumentException("The sequence view requires a four-digit sequenceNumber.");
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("The archived projection is not a JSON object.");
        var leaves = root["leaves"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var selected = view switch
        {
            "current" => leaves.Where(leaf => leaf["status"]!.GetValue<string>() is "Current" or "AppendMember"),
            "sequence" => leaves.Where(leaf => leaf["address"]!["sequenceNumber"]!.GetValue<string>() == sequenceNumber),
            _ => leaves
        };
        var ordered = selected.OrderBy(leaf => leaf["address"]!["sequenceNumber"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(leaf => leaf["address"]!["backboneRelativePath"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(leaf => leaf["address"]!["leafId"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        var page = new JsonObject
        {
            ["schemaVersion"] = root["schemaVersion"]!.DeepClone(),
            ["view"] = view,
            ["sequenceNumber"] = sequenceNumber,
            ["applicationId"] = root["applicationId"]!.DeepClone(),
            ["targetSequence"] = root["targetSequence"]!.DeepClone(),
            ["profileSnapshotId"] = root["profileSnapshotId"]!.DeepClone(),
            ["inputDigest"] = root["inputDigest"]!.DeepClone(),
            ["baselineDigest"] = root["baselineDigest"]!.DeepClone(),
            ["engineVersion"] = root["engineVersion"]!.DeepClone(),
            ["isComplete"] = root["isComplete"]!.DeepClone(),
            ["incompleteReasons"] = root["incompleteReasons"]!.DeepClone(),
            ["appliedSequences"] = root["appliedSequences"]!.DeepClone(),
            ["baseline"] = root["baseline"]!.DeepClone(),
            ["totalCount"] = ordered.Length,
            ["offset"] = offset,
            ["limit"] = limit,
            ["leaves"] = new JsonArray(ordered.Skip(offset).Take(limit).Select(leaf => (JsonNode)leaf.DeepClone()).ToArray()),
            // Findings and event chains stay complete: they are the explanation of the
            // view and are bounded by the inspection's finding limit.
            ["events"] = root["events"]!.DeepClone(),
            ["findings"] = root["findings"]!.DeepClone()
        };
        return page.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
