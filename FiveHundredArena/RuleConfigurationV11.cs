using System.Text.Json.Nodes;
namespace FiveHundredArena;

public sealed record RuleVariants
{
    public bool AceNoFace { get; init; }
    public string AllPass { get; init; } = "redeal";
    public bool RotateDealerOnRedeal { get; init; } = true;
    public string OpenTiming { get; init; } = "after_first_trick";
    public string JokerFollow { get; init; } = "only_when_void";
    public string? JokerLead { get; init; }
    public bool? JokerNomination { get; init; }
    public bool JokerLowInMisere { get; init; }
    public bool MisereWithoutPickup { get; init; }
    public bool GrandNullo { get; init; }
    public bool GrandSlam { get; init; }
    public string? MisereRanking { get; init; }
    public string OpenMisereRanking { get; init; } = "between_10D_10H";
}

// Each call resolves a fresh complete document, so disabling a feature restores the selected preset.
public static class RuleConfigurationV11
{
    public static ConfiguredRules Create(string preset = "australian", RuleVariants? options = null)
    {
        options ??= new();
        string resource = preset switch { "australian" => "au-default-v1.1.json", "american" => "us-default-v1.1.json", _ => throw new ArgumentException("Unknown v1.1 preset") };
        using var stream = typeof(RuleConfigurationV11).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var root = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        var contracts = root["contracts"]!.AsObject();
        root["deal"]!["redealRequest"]!["mode"] = options.AceNoFace ? "ace_no_face" : "none";
        root["deal"]!["rotation"] = options.RotateDealerOnRedeal ? "every_deal" : "after_played_deal";
        root["auction"]!["allPass"]!["action"] = options.AllPass;
        root["auction"]!["allPass"]!["dealerOnRedeal"] = options.RotateDealerOnRedeal ? "advance" : "same";
        contracts["open_misere"]!["exposure"]!["timing"] = options.OpenTiming;
        foreach (var entry in contracts)
        {
            var joker = entry.Value!["joker"]!;
            joker["follow"] = options.JokerFollow;
            if (options.JokerLead != null) joker["leadSuits"] = options.JokerLead;
            if (options.JokerNomination.HasValue) joker["predeclare"] = options.JokerNomination.Value ? "holder_optional" : "none";
            if (entry.Key != "normal" && entry.Key != "grand_slam") joker["unassignedRank"] = options.JokerLowInMisere ? "lowest" : "highest";
        }
        var order = root["auction"]!["orderLowToHigh"]!.AsArray().Select(x => x!.GetValue<string>()).Where(x => x is not ("misere" or "open_misere")).ToList();
        void Insert(string id, string ranking)
        {
            if (!contracts[id]!["enabled"]!.GetValue<bool>()) return;
            string before = ranking switch
            {
                "between_7NT_8S" => "8S", "between_8S_8C" => "8C", "between_9NT_10S" => "10S", "between_10D_10H" => "10H",
                "by_points" or "by_points_higher" or "by_points_lower" or "by_points_first" => "", _ => throw new ArgumentException("Unknown ranking option")
            };
            int index;
            if (before.Length > 0) index = order.IndexOf(before);
            else
            {
                int points = contracts[id]!["points"]!.GetValue<int>();
                int Value(string key) => key is "misere" or "open_misere" ? contracts[key]!["points"]!.GetValue<int>() : NormalValue(root, key);
                index = order.FindIndex(key => ranking == "by_points_lower" ? Value(key) >= points : Value(key) > points);
                if (index < 0) index = order.Count;
            }
            if (index < 0) throw new ArgumentException("Ranking anchor is disabled");
            order.Insert(index, id);
        }
        Insert("misere", options.MisereRanking ?? (preset == "american" ? "between_8S_8C" : "between_7NT_8S"));
        Insert("open_misere", options.OpenMisereRanking);
        root["extensions"]!["openMisereRanking"] = options.OpenMisereRanking;
        if (options.MisereWithoutPickup)
        {
            if (options.MisereRanking != null || options.OpenMisereRanking != "between_10D_10H") throw new ArgumentException("No Pickup bundle fixes the Misere/Open Misere rankings; choose it without competing ranking overrides");
            contracts["misere_no_pickup"]!["enabled"] = true;contracts["misere"]!["points"] = 150;contracts["open_misere"]!["points"] = 250;
            order.Remove("misere");order.Remove("open_misere");
            order.Insert(order.IndexOf("7C"), "misere");order.Insert(order.IndexOf("8C"), "open_misere");order.Insert(order.IndexOf("9C"), "misere_no_pickup");
            root["extensions"]!["openMisereRanking"] = "no_pickup_bundle";
        }
        contracts["grand_nullo"]!["enabled"] = options.GrandNullo;
        if (options.GrandNullo) order.Insert(order.IndexOf("open_misere") + 1, "grand_nullo");
        contracts["grand_slam"]!["enabled"] = options.GrandSlam;root["extensions"]!["grandSlamEnabled"] = options.GrandSlam;
        root["auction"]!["orderLowToHigh"] = new JsonArray(order.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        return (ConfiguredRules)RuleConfiguration.Parse(root.ToJsonString());
    }
    private static int NormalValue(JsonObject root, string id)
    {
        int suffix = id.EndsWith("NT") ? 2 : 1;
        return root["scoring"]!["normalTable"]![id[..^suffix]]![id[^suffix..]]!.GetValue<int>();
    }
}
