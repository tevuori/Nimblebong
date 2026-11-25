#nullable enable
// Placement rules — res://assets/config/placement.json. Protection
// zones: protected kinds get a keep-out circle of radius
// (scale/2)·factor + padding; other kinds are nudged out of it, and
// placing a protected kind clears kept-out props from its zone
// (clearOnPlace). Kinds in "except" may stand anywhere. Prop
// definitions override per object ("protect", "keepOut") and otherwise
// inherit the rules of their base kind.
//
// Override: "enabled": false in the JSON, or Placement.IgnoreProtection
// at runtime (settings toggle), switches the whole system off — props
// land exactly where placed and nothing is cleared.
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DmMap;

public class PlacementConfig
{
    public bool Enabled = true;
    public double Padding = 6;
    public bool ClearOnPlace = true;
    public Dictionary<string, double> Kinds = new();
    public HashSet<string> Except = [];
}

public static class Placement
{
    public const string Path = "res://assets/config/placement.json";

    static PlacementConfig? _cfg;

    public static PlacementConfig Config => _cfg ??= Load();

    /// <summary>Runtime override (settings): true = ignore protection
    /// zones regardless of the JSON.</summary>
    public static bool IgnoreProtection { get; set; }

    /// <summary>Protection active = JSON enabled and not overridden.</summary>
    public static bool ProtectionActive => Config.Enabled && !IgnoreProtection;

    public static void Reload() => _cfg = null;

    /// <summary>Zone factor of a scene kind, or null when unprotected:
    /// the definition's "protect", else its base kind's factor.</summary>
    public static double? Factor(string kind)
    {
        if (PropDefs.TryGet(kind, out var def) && def.Protect is { } f)
            return f > 0 ? f : null;
        return Config.Kinds.TryGetValue(PropDefs.BaseKind(kind), out var k) ? k : null;
    }

    /// <summary>Whether a kind must stay outside zones: the definition's
    /// "keepOut", else unprotected and not in "except" (via base kind).</summary>
    public static bool KeptOut(string kind)
    {
        if (PropDefs.TryGet(kind, out var def) && def.KeepOut is { } k)
            return k;
        return Factor(kind) == null && !Config.Except.Contains(PropDefs.BaseKind(kind));
    }

    static PlacementConfig Load()
    {
        var cfg = new PlacementConfig();
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"placement: cannot open {Path} — protection disabled");
            cfg.Enabled = false;
            return cfg;
        }
        var json = Json.ParseString(file.GetAsText());
        if (json.VariantType != Variant.Type.Dictionary)
            throw new ConfigException($"placement: {Path} is not a JSON object");
        var root = json.AsGodotDictionary();
        if (!root.TryGetValue("protection", out var pv) || pv.VariantType != Variant.Type.Dictionary)
            throw new ConfigException("placement.protection: expected object");
        var p = pv.AsGodotDictionary();
        string[] known = ["enabled", "padding", "clearOnPlace", "kinds", "except"];
        foreach (var k in p.Keys)
            if (!known.Contains(k.AsString()))
                GD.PushWarning($"placement.protection: unknown key \"{k.AsString()}\"");

        var err = new List<string>();
        bool IsNum(Variant v) => v.VariantType is Variant.Type.Int or Variant.Type.Float;
        if (p.TryGetValue("enabled", out var en))
        {
            if (en.VariantType == Variant.Type.Bool) cfg.Enabled = en.AsBool();
            else err.Add("placement.protection.enabled: expected bool");
        }
        if (p.TryGetValue("clearOnPlace", out var co))
        {
            if (co.VariantType == Variant.Type.Bool) cfg.ClearOnPlace = co.AsBool();
            else err.Add("placement.protection.clearOnPlace: expected bool");
        }
        if (p.TryGetValue("padding", out var pad))
        {
            if (IsNum(pad) && pad.AsDouble() >= 0) cfg.Padding = pad.AsDouble();
            else err.Add("placement.protection.padding: expected number ≥ 0");
        }
        if (p.TryGetValue("kinds", out var kv))
        {
            if (kv.VariantType != Variant.Type.Dictionary)
                err.Add("placement.protection.kinds: expected object {kind: factor}");
            else
                foreach (var (k, v) in kv.AsGodotDictionary())
                {
                    if (IsNum(v) && v.AsDouble() > 0) cfg.Kinds[k.AsString()] = v.AsDouble();
                    else err.Add($"placement.protection.kinds.{k.AsString()}: expected number > 0");
                }
        }
        if (p.TryGetValue("except", out var ev))
        {
            if (ev.VariantType != Variant.Type.Array)
                err.Add("placement.protection.except: expected array of kinds");
            else
                foreach (var v in ev.AsGodotArray())
                {
                    if (v.VariantType == Variant.Type.String) cfg.Except.Add(v.AsString());
                    else err.Add("placement.protection.except[]: expected string");
                }
        }
        if (err.Count > 0)
            throw new ConfigException(string.Join("\n", err));
        return cfg;
    }
}
