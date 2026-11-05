using System.Text.Json;
using System.Text.RegularExpressions;
namespace FiveHundredArena;
internal static class ConfigurationStructure
{
    public static void Validate(JsonElement root)
    {
        using var schema = JsonDocument.Parse(typeof(ConfigurationStructure).Assembly.GetManifestResourceStream("rules.schema.json")!);
        Check(root, schema.RootElement, "$");
    }
    private static void Check(JsonElement v, JsonElement s, string path)
    {
        void Bad() => throw new ArgumentException("Invalid configuration at " + path);
        bool Equal(JsonElement a, JsonElement b) => System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(a.GetRawText()), System.Text.Json.Nodes.JsonNode.Parse(b.GetRawText()));
        if (s.TryGetProperty("anyOf", out var alternatives))
        {
            foreach (var option in alternatives.EnumerateArray())
            {
                try
                {
                    Check(v, option, path);
                    return;
                }
                catch (ArgumentException) { }
            }
            Bad();
        }
        if (s.TryGetProperty("const", out var constant) && !Equal(v, constant))
            Bad();
        if (s.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(x => Equal(v, x)))
            Bad();
        if (s.TryGetProperty("type", out var type))
        {
            bool match = type.GetString() switch
            {
                "object" => v.ValueKind == JsonValueKind.Object,
                "array" => v.ValueKind == JsonValueKind.Array,
                "string" => v.ValueKind == JsonValueKind.String,
                "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out _),
                "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => v.ValueKind == JsonValueKind.Null,
                _ => false
            };
            if (!match)
                Bad();
        }
        if (v.ValueKind == JsonValueKind.Object && s.TryGetProperty("properties", out var properties))
        {
            if (s.TryGetProperty("required", out var required))
                foreach (var p in required.EnumerateArray())
                    if (!v.TryGetProperty(p.GetString()!, out _))
                        Bad();
            var names = new HashSet<string>();
            foreach (var p in v.EnumerateObject())
            {
                if (!names.Add(p.Name) || !properties.TryGetProperty(p.Name, out var definition))
                    Bad();
                else
                    Check(p.Value, definition, path + "." + p.Name);
            }
        }
        if (v.ValueKind == JsonValueKind.Array && s.TryGetProperty("items", out var item))
        {
            int n = v.GetArrayLength();
            if (s.TryGetProperty("minItems", out var min) && n < min.GetInt32() || s.TryGetProperty("maxItems", out var max) && n > max.GetInt32())
                Bad();
            if (s.TryGetProperty("uniqueItems", out var unique) && unique.GetBoolean() && v.EnumerateArray().Select(x => x.GetRawText()).Distinct().Count() != n)
                Bad();
            int i = 0;
            foreach (var x in v.EnumerateArray())
                Check(x, item, path + "[" + i++ + "]");
        }
        if (v.ValueKind == JsonValueKind.Number)
        {
            int n = v.GetInt32();
            if (s.TryGetProperty("minimum", out var min) && n < min.GetInt32() || s.TryGetProperty("maximum", out var max) && n > max.GetInt32())
                Bad();
        }
        if (v.ValueKind == JsonValueKind.String)
        {
            var text = v.GetString()!;
            if (s.TryGetProperty("minLength", out var min) && text.Length < min.GetInt32() || s.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!))
                Bad();
        }
    }
}
