#nullable enable
// Scene serialization — the `parseScene` port. Same contract: strict
// document envelope (version/size caps reject outright), forgiving
// per-entry validation (malformed entries drop with a warning, numeric
// fields clamp into the TS ranges). Serialization is canonical so a
// parse→serialize round-trip is byte-stable.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace DmMap;

public class SceneParseException : Exception
{
    public SceneParseException(string message)
        : base(message) { }
}

public static class SceneJson
{
    // TS prototype limits.
    const int MaxRawLength = 2_000_000;
    const int MaxElements = 3000;
    const int MaxTotalPoints = 10_000;
    const int MaxRoadRiverPoints = 2000;
    const int MaxShapePoints = 5000;
    const double MinSize = 4;
    const double MaxSize = 160;

    /// <summary>Canonical JSON — fixed key order, defaults omitted.</summary>
    public static string Serialize(Scene scene)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", scene.Version);
            w.WriteString("name", scene.Name);
            w.WriteNumber("width", scene.Width);
            w.WriteNumber("height", scene.Height);
            if (scene.Biome != null)
                w.WriteString("biome", scene.Biome);
            if (scene.Mood != null)
                w.WriteString("mood", scene.Mood);
            w.WriteBoolean("castShadows", scene.CastShadows);
            w.WriteNumber("nextId", scene.NextId);
            w.WritePropertyName("shapes");
            w.WriteStartArray();
            foreach (var s in scene.Shapes)
            {
                w.WriteStartObject();
                w.WriteNumber("id", s.Id);
                w.WriteString("kind", s.Kind);
                w.WritePropertyName("points");
                WritePoints(w, s.Points);
                w.WriteNumber("size", s.Size);
                w.WriteNumber("height", s.Height);
                w.WriteNumber("roughness", s.Roughness);
                w.WriteNumber("model", s.Model);
                if (s.Angle != 0)
                    w.WriteNumber("angle", s.Angle);
                if (s.Color != null)
                    w.WriteString("color", s.Color);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("strokes");
            w.WriteStartArray();
            foreach (var s in scene.Strokes)
            {
                w.WriteStartObject();
                w.WriteNumber("id", s.Id);
                w.WriteString("kind", s.Kind);
                w.WritePropertyName("points");
                WritePoints(w, s.Points);
                w.WriteNumber("width", s.Width);
                if (s.Model != 0)
                    w.WriteNumber("model", s.Model);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (scene.ArtPack != "classic")
                w.WriteString("artPack", scene.ArtPack);
            if (scene.Season != null)
                w.WriteString("season", scene.Season);
            if (scene.FogCoverAll)
                w.WriteBoolean("fogCoverAll", true);
            if (!scene.FogRooms)
                w.WriteBoolean("fogRooms", false);
            w.WritePropertyName("fog");
            w.WriteStartArray();
            foreach (var s in scene.Fog)
            {
                w.WriteStartObject();
                w.WriteNumber("id", s.Id);
                w.WriteString("kind", s.Kind);
                w.WritePropertyName("points");
                WritePoints(w, s.Points);
                w.WriteNumber("width", s.Width);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("props");
            w.WriteStartArray();
            foreach (var p in scene.Props)
            {
                w.WriteStartObject();
                w.WriteNumber("id", p.Id);
                w.WriteString("kind", p.Kind);
                w.WriteNumber("x", p.X);
                w.WriteNumber("y", p.Y);
                w.WriteNumber("scale", p.Scale);
                w.WriteNumber("rotation", p.Rotation);
                w.WriteNumber("seed", p.Seed);
                if (p.Model >= 0)
                    w.WriteNumber("model", p.Model);
                if (p.Species != null)
                    w.WriteString("species", p.Species);
                if (p.Text != null)
                    w.WriteString("text", p.Text);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WritePoints(Utf8JsonWriter w, IReadOnlyList<Vector2> points)
    {
        w.WriteStartArray();
        foreach (var p in points)
        {
            w.WriteStartArray();
            // Float overload — shortest round-trip representation, so a
            // parse→serialize cycle is byte-stable.
            w.WriteNumberValue(p.X);
            w.WriteNumberValue(p.Y);
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }

    /// <summary>Parse a scene document — throws SceneParseException on a
    /// fatally malformed envelope, appends per-entry issues to `warnings`.</summary>
    public static Scene Parse(string raw, List<string>? warnings = null)
    {
        var warn = warnings ?? new List<string>();
        if (raw.Length > MaxRawLength)
            throw new SceneParseException("Scene file is too large.");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException e)
        {
            throw new SceneParseException($"Not a JSON document: {e.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new SceneParseException("Scene document is not an object.");
            var scene = new Scene();
            if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 1)
                throw new SceneParseException("Invalid or unsupported scene document.");
            scene.Width = Number(root, "width", 1600);
            scene.Height = Number(root, "height", 1000);
            if (scene.Width is < 100 or > 4096 || scene.Height is < 100 or > 4096)
                throw new SceneParseException("Invalid or unsupported scene document.");
            if (root.TryGetProperty("name", out var name))
            {
                if (name.ValueKind == JsonValueKind.String)
                    scene.Name = name.GetString() ?? "";
                else
                    warn.Add("name: not a string — ignored");
            }
            scene.Biome = IdField(root, "biome", Kinds.Biomes, warn);
            scene.Mood = IdField(root, "mood", Kinds.Moods, warn);
            if (root.TryGetProperty("castShadows", out var shadows))
            {
                if (shadows.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    scene.CastShadows = shadows.GetBoolean();
                else
                    warn.Add("castShadows: not a boolean — ignored");
            }
            var totalPoints = 0;
            var roadRiverPoints = 0;
            var elements = 0;
            bool Fits(int points)
            {
                if (++elements > MaxElements || totalPoints + points > MaxTotalPoints)
                {
                    elements--;
                    return false;
                }
                totalPoints += points;
                return true;
            }
            foreach (var el in Members(root, "shapes", warn))
            {
                var shape = ReadShape(el, scene, warn);
                if (shape == null)
                    continue;
                if (!Fits(shape.Points.Count))
                {
                    warn.Add($"{shape.Kind}: dropped — scene geometry limit reached");
                    continue;
                }
                scene.Shapes.Add(shape);
            }
            foreach (var el in Members(root, "strokes", warn))
            {
                var stroke = ReadStroke(el, scene, warn);
                if (stroke == null)
                    continue;
                var riverRoad = stroke.Kind is Kinds.Road or Kinds.River;
                if (riverRoad && roadRiverPoints + stroke.Points.Count > MaxRoadRiverPoints)
                {
                    warn.Add($"{stroke.Kind}: dropped — road/river point limit reached");
                    continue;
                }
                if (!Fits(stroke.Points.Count))
                {
                    warn.Add($"{stroke.Kind}: dropped — scene geometry limit reached");
                    continue;
                }
                if (riverRoad)
                    roadRiverPoints += stroke.Points.Count;
                scene.Strokes.Add(stroke);
            }
            if (root.TryGetProperty("artPack", out var ap) && ap.ValueKind == JsonValueKind.String)
                scene.ArtPack = ap.GetString();
            if (root.TryGetProperty("season", out var se) && se.ValueKind == JsonValueKind.String)
                scene.Season = se.GetString();
            if (root.TryGetProperty("fogCoverAll", out var fca) && fca.ValueKind == JsonValueKind.True)
                scene.FogCoverAll = true;
            if (root.TryGetProperty("fogRooms", out var frs) && frs.ValueKind == JsonValueKind.False)
                scene.FogRooms = false;
            foreach (var el in Members(root, "fog", warn))
            {
                var fog = ReadFog(el, scene, warn);
                if (fog != null)
                    scene.Fog.Add(fog);
            }
            foreach (var el in Members(root, "props", warn))
            {
                var prop = ReadProp(el, scene, warn);
                if (prop == null)
                    continue;
                if (!Fits(1))
                {
                    warn.Add($"{prop.Kind}: dropped — scene element limit reached");
                    continue;
                }
                // A stored "camp" marker never persists — it unfolds into a
                // deterministic prop cluster like the web commit does.
                if (prop.Kind == Kinds.Camp)
                    foreach (var member in Interactions.ExpandCamp(prop, scene))
                        scene.Props.Add(member);
                else
                    scene.Props.Add(prop);
            }
            if (root.TryGetProperty("nextId", out var next) && next.ValueKind == JsonValueKind.Number)
                scene.NextId = next.GetInt32();
            scene.NextId = Math.Max(scene.NextId, MaxUsedId(scene) + 1);
            EnsureUniqueIds(scene, warn);
            return scene;
        }
    }

    /// <summary>Forgiving parse — false + warnings on fatal errors.</summary>
    public static bool TryParse(string raw, out Scene? scene, List<string>? warnings = null)
    {
        var warn = warnings ?? new List<string>();
        try
        {
            scene = Parse(raw, warn);
            return true;
        }
        catch (SceneParseException e)
        {
            warn.Add(e.Message);
            scene = null;
            return false;
        }
        catch (JsonException e)
        {
            warn.Add($"Not a JSON document: {e.Message}");
            scene = null;
            return false;
        }
    }

    static int MaxUsedId(Scene scene)
    {
        var max = 0;
        foreach (var s in scene.Shapes)
            max = Math.Max(max, s.Id);
        foreach (var s in scene.Strokes)
            max = Math.Max(max, s.Id);
        foreach (var p in scene.Props)
            max = Math.Max(max, p.Id);
        return max;
    }

    static void EnsureUniqueIds(Scene scene, List<string> warn)
    {
        var seen = new HashSet<int>();
        int Claim(int id)
        {
            if (id > 0 && seen.Add(id))
                return id;
            var next = scene.AllocId();
            while (!seen.Add(next))
                next = scene.AllocId();
            warn.Add($"id {id}: duplicate or missing — reassigned {next}");
            return next;
        }
        foreach (var s in scene.Shapes)
            s.Id = Claim(s.Id);
        foreach (var s in scene.Strokes)
            s.Id = Claim(s.Id);
        foreach (var p in scene.Props)
            p.Id = Claim(p.Id);
    }

    // -- per-entry readers ------------------------------------------------

    static IEnumerable<JsonElement> Members(JsonElement root, string name, List<string> warn)
    {
        if (!root.TryGetProperty(name, out var list))
            yield break;
        if (list.ValueKind != JsonValueKind.Array)
        {
            warn.Add($"{name}: not an array — ignored");
            yield break;
        }
        foreach (var el in list.EnumerateArray())
            yield return el;
    }

    static double Number(JsonElement obj, string name, double fallback)
    {
        if (
            obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && double.IsFinite(v.GetDouble())
        )
            return v.GetDouble();
        return fallback;
    }

    static string? IdField(JsonElement root, string name, string[] ids, List<string> warn)
    {
        if (!root.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind == JsonValueKind.String && Array.IndexOf(ids, v.GetString()) >= 0)
            return v.GetString();
        warn.Add($"{name}: unknown id — ignored");
        return null;
    }

    /// <summary>Clamped number field: missing/non-numeric → fallback,
    /// finite → clamped into [min,max] (TS range).</summary>
    static double Clamped(JsonElement obj, string name, double min, double max, double fallback)
    {
        if (
            !obj.TryGetProperty(name, out var v)
            || v.ValueKind != JsonValueKind.Number
            || !double.IsFinite(v.GetDouble())
        )
            return fallback;
        return Math.Clamp(v.GetDouble(), min, max);
    }

    static string? ColorField(JsonElement obj, List<string> warn)
    {
        if (!obj.TryGetProperty("color", out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind == JsonValueKind.String && IsHexColor(v.GetString()))
            return v.GetString();
        warn.Add("color: not #rrggbb — ignored");
        return null;
    }

    static bool IsHexColor(string? s) =>
        s != null
        && s.Length == 7
        && s[0] == '#'
        && Uri.IsHexDigit(s[1])
        && Uri.IsHexDigit(s[2])
        && Uri.IsHexDigit(s[3])
        && Uri.IsHexDigit(s[4])
        && Uri.IsHexDigit(s[5])
        && Uri.IsHexDigit(s[6]);

    /// <summary>Read a points array; invalid pairs drop (warning once),
    /// coordinates clamp into the map bounds like parseScene requires.</summary>
    static List<Vector2>? ReadPoints(
        JsonElement obj,
        double width,
        double height,
        string context,
        List<string> warn
    )
    {
        if (!obj.TryGetProperty("points", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            warn.Add($"{context}: missing points — dropped");
            return null;
        }
        var points = new List<Vector2>();
        var bad = 0;
        foreach (var p in arr.EnumerateArray())
        {
            if (
                p.ValueKind != JsonValueKind.Array
                || p.GetArrayLength() != 2
                || p[0].ValueKind != JsonValueKind.Number
                || p[1].ValueKind != JsonValueKind.Number
                || !double.IsFinite(p[0].GetDouble())
                || !double.IsFinite(p[1].GetDouble())
            )
            {
                bad++;
                continue;
            }
            var x = Math.Clamp(p[0].GetDouble(), 0, width);
            var y = Math.Clamp(p[1].GetDouble(), 0, height);
            points.Add(new Vector2((float)x, (float)y));
        }
        if (bad > 0)
            warn.Add($"{context}: dropped {bad} malformed point(s)");
        if (points.Count == 0)
        {
            warn.Add($"{context}: no valid points — dropped");
            return null;
        }
        if (points.Count > MaxShapePoints)
        {
            warn.Add($"{context}: over {MaxShapePoints} points — truncated");
            points = points.GetRange(0, MaxShapePoints);
        }
        return points;
    }

    static int ReadId(JsonElement obj) =>
        obj.TryGetProperty("id", out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var id)
            ? id
            : 0;

    static string? ReadKind(JsonElement obj, string context, List<string> warn)
    {
        if (!obj.TryGetProperty("kind", out var v) || v.ValueKind != JsonValueKind.String)
        {
            warn.Add($"{context}: missing kind — dropped");
            return null;
        }
        var kind = v.GetString()!;
        // Legacy kinds vanish silently, exactly like parseScene.
        if (Array.IndexOf(Kinds.Legacy, kind) >= 0)
            return null;
        return kind;
    }

    static Shape? ReadShape(JsonElement el, Scene scene, List<string> warn)
    {
        const string ctx = "shape";
        if (el.ValueKind != JsonValueKind.Object)
        {
            warn.Add($"{ctx}: not an object — dropped");
            return null;
        }
        var kind = ReadKind(el, ctx, warn);
        if (kind == null)
            return null;
        if (!Kinds.IsArea(kind))
        {
            warn.Add($"{ctx} {kind}: not an area kind — dropped");
            return null;
        }
        var points = ReadPoints(el, scene.Width, scene.Height, kind, warn);
        if (points == null)
            return null;
        var model = (int)Clamped(el, "model", 0, Kinds.VariantCount(kind) - 1, 0);
        return new Shape
        {
            Id = ReadId(el),
            Kind = kind,
            Points = points,
            Size = Clamped(el, "size", MinSize, MaxSize, 24),
            Height = Clamped(el, "height", 0, 100, 0),
            Roughness = Clamped(el, "roughness", 0, 100, 0),
            Model = model,
            Angle = Clamped(el, "angle", -Math.PI * 2, Math.PI * 2, 0),
            Color = ColorField(el, warn),
        };
    }

    static Stroke? ReadStroke(JsonElement el, Scene scene, List<string> warn)
    {
        const string ctx = "stroke";
        if (el.ValueKind != JsonValueKind.Object)
        {
            warn.Add($"{ctx}: not an object — dropped");
            return null;
        }
        var kind = ReadKind(el, ctx, warn);
        if (kind == null)
            return null;
        if (!Kinds.IsLine(kind))
        {
            warn.Add($"{ctx} {kind}: not a line kind — dropped");
            return null;
        }
        var points = ReadPoints(el, scene.Width, scene.Height, kind, warn);
        if (points == null)
            return null;
        return new Stroke
        {
            Id = ReadId(el),
            Kind = kind,
            Points = points,
            Width = Clamped(el, "width", MinSize, MaxSize, 24),
            Model = (int)Clamped(el, "model", 0, 4, 0),
        };
    }

    /// <summary>Fog strokes accept only clear/shroud kinds; they live
    /// outside Kinds.IsLine so the line validator can't be reused.</summary>
    static Stroke? ReadFog(JsonElement el, Scene scene, List<string> warn)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;
        var kind = el.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (kind is not ("clear" or "shroud"))
            return null;
        var points = ReadPoints(el, scene.Width, scene.Height, "fog", warn);
        if (points == null)
            return null;
        return new Stroke
        {
            Id = ReadId(el),
            Kind = kind,
            Points = points,
            Width = Clamped(el, "width", 4, MaxSize, 80),
        };
    }

    static Prop? ReadProp(JsonElement el, Scene scene, List<string> warn)
    {
        const string ctx = "prop";
        if (el.ValueKind != JsonValueKind.Object)
        {
            warn.Add($"{ctx}: not an object — dropped");
            return null;
        }
        var kind = ReadKind(el, ctx, warn);
        if (kind == null)
            return null;
        if (!Kinds.IsProp(kind))
        {
            warn.Add($"{ctx} {kind}: not a prop kind — dropped");
            return null;
        }
        var x = Clamped(el, "x", 0, scene.Width, double.NaN);
        var y = Clamped(el, "y", 0, scene.Height, double.NaN);
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            warn.Add($"{ctx} {kind}: missing x/y — dropped");
            return null;
        }
        var seed = (int)Clamped(el, "seed", 0, int.MaxValue, -1);
        if (seed < 0)
            // Deterministic fallback seeded by position — same file, same
            // seed, and two props at different spots still differ.
            seed = (int)(Rng.Noise(x * 0.37 + y * 0.91) * int.MaxValue);
        return new Prop
        {
            Id = ReadId(el),
            Kind = kind,
            X = x,
            Y = y,
            Scale = Clamped(el, "scale", MinSize, MaxSize, 24),
            Rotation = Clamped(el, "rotation", -Math.PI * 2, Math.PI * 2, 0),
            Seed = seed,
            Model = el.TryGetProperty("model", out var m) ? (int)Clamped(el, "model", 0, 8, -1) : -1,
            Text = el.TryGetProperty("text", out var t) ? t.GetString() : null,
            Species = el.TryGetProperty("species", out var sp)
                ? sp.GetString()
                : null,
        };
    }
}
