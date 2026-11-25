#nullable enable
// Bridge to the dungeon-revealer / DMmap backend — uploads the
// rendered map PNG and creates a map entry, optionally setting it as
// the live map. The backend's GraphQL runs over socket.io events; we
// speak EIO4 long-polling directly over HTTP so no websocket client
// is needed (the session is short-lived anyway).
//
// Protocol (server-build/server/socket-authentication.js +
// @n1ru4l/socket-io-graphql-*):
//   GET  /api/socket.io/?EIO=4&transport=polling        → "0{json sid}"
//   POST ?sid=X  body "40"                              → namespace connect
//   POST ?sid=X  body 42["authenticate",{password,desiredRole:"admin"}]
//   GET  ?sid=X                                         → 42["authenticated"]
//   POST ?sid=X  body 42["@graphql/execute",{id,operation,variables}]
//   GET  ?sid=X                                         → 42["@graphql/result",{...}]
//   PUT  {uploadUrl} (absolute URL from the mutation)   → raw PNG bytes
//   POST /api/map/{id}/send  Authorization: Bearer pw   → live map
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DmMap;

public static class DrBridge
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public class Result
    {
        public bool Ok;
        public string Message = "";
        public string MapId = "";
    }

    /// <summary>Full upload flow. `status` gets human-readable Czech
    /// progress lines as the flow advances.</summary>
    public static async Task<Result> UploadMap(
        string baseUrl,
        string password,
        string title,
        byte[] png,
        bool setLive,
        Action<string>? status = null
    )
    {
        var res = new Result();
        void Say(string s) => status?.Invoke(s);
        baseUrl = baseUrl.TrimEnd('/');

        // ── 1. socket.io EIO4 handshake ──
        Say("Připojuji…");
        var sioUrl = $"{baseUrl}/api/socket.io/?EIO=4&transport=polling";
        string sid;
        try
        {
            var open = await Http.GetStringAsync(sioUrl);
            var json = open[open.IndexOf('{')..];
            using var doc = JsonDocument.Parse(json);
            sid = doc.RootElement.GetProperty("sid").GetString()!;
        }
        catch (Exception ex)
        {
            res.Message = $"Server nedostupný: {ex.Message}";
            return res;
        }
        var sidUrl = $"{sioUrl}&sid={sid}";

        // ── 2. namespace connect + authenticate ──
        try
        {
            await Post(sidUrl, "40");
            var authPayload = JsonSerializer.Serialize(
                new object[]
                {
                    "authenticate",
                    new Dictionary<string, object>
                    {
                        ["password"] = password,
                        ["desiredRole"] = "admin",
                    },
                }
            );
            await Post(sidUrl, "42" + authPayload);
        }
        catch (Exception ex)
        {
            res.Message = $"Připojení selhalo: {ex.Message}";
            return res;
        }

        Say("Autentizace…");
        var authed = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!authed && DateTime.UtcNow < deadline)
        {
            foreach (var (evt, arg) in await Poll(sidUrl))
                switch (evt)
                {
                    case "authenticated":
                        authed = true;
                        break;
                    case "authentication_error":
                        res.Message = "Špatné heslo (DM_PASSWORD).";
                        return res;
                }
        }
        if (!authed)
        {
            res.Message = "Autentizace vypršela.";
            return res;
        }

        // ── 3. mapImageRequestUpload ──
        Say("Žádám upload…");
        var sha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        var upload = await Graphql(
            sidUrl,
            1,
            "mutation($input: MapImageRequestUploadInput!) { mapImageRequestUpload(input: $input) { id uploadUrl } }",
            new Dictionary<string, object>
            {
                ["input"] = new Dictionary<string, object>
                {
                    ["sha256"] = sha,
                    ["extension"] = "png",
                },
            }
        );
        if (upload == null)
        {
            res.Message = "mapImageRequestUpload: žádná odpověď.";
            return res;
        }
        var uploadData = upload.Value;
        if (uploadData.TryGetProperty("errors", out var errs))
        {
            res.Message = "GraphQL chyba: " + errs.GetRawText();
            return res;
        }
        var req = uploadData.GetProperty("data").GetProperty("mapImageRequestUpload");
        var uploadUrl = req.GetProperty("uploadUrl").GetString()!;
        var uploadId = req.GetProperty("id").GetString()!;
        // dev server returns a relative path when publicUrl is unset
        if (uploadUrl.StartsWith('/'))
            uploadUrl = baseUrl + uploadUrl;

        // ── 4. PUT the PNG ──
        Say("Nahrávám obrázek…");
        try
        {
            using var content = new ByteArrayContent(png);
            content.Headers.ContentType = new("image/png");
            var put = await Http.PutAsync(uploadUrl, content);
            if (!put.IsSuccessStatusCode)
            {
                res.Message = $"Upload selhal: HTTP {(int)put.StatusCode}";
                return res;
            }
        }
        catch (Exception ex)
        {
            res.Message = $"Upload selhal: {ex.Message}";
            return res;
        }

        // ── 5. mapCreate ──
        Say("Vytvářím mapu…");
        var created = await Graphql(
            sidUrl,
            2,
            "mutation($input: MapCreateInput!) { mapCreate(input: $input) { __typename ... on MapCreateSuccess { createdMap { id } } ... on MapCreateError { reason } } }",
            new Dictionary<string, object>
            {
                ["input"] = new Dictionary<string, object>
                {
                    ["title"] = title,
                    ["mapImageUploadId"] = uploadId,
                },
            }
        );
        if (created == null)
        {
            res.Message = "mapCreate: žádná odpověď.";
            return res;
        }
        var mc = created.Value.GetProperty("data").GetProperty("mapCreate");
        var type = mc.GetProperty("__typename").GetString();
        if (type != "MapCreateSuccess")
        {
            res.Message =
                "mapCreate: "
                + (mc.TryGetProperty("reason", out var r) ? r.GetString() : mc.GetRawText());
            return res;
        }
        var mapId = mc.GetProperty("createdMap").GetProperty("id").GetString()!;
        res.MapId = mapId;

        // ── 6. optional: set as live map ──
        if (setLive)
        {
            Say("Nastavuji živou mapu…");
            try
            {
                // /send expects a multipart "image" field — the map
                // shown to players live (same PNG we uploaded).
                using var form = new MultipartFormDataContent();
                var file = new ByteArrayContent(png);
                file.Headers.ContentType = new("image/png");
                form.Add(file, "image", "map.png");
                using var reqMsg = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{baseUrl}/api/map/{mapId}/send"
                )
                {
                    Content = form,
                };
                reqMsg.Headers.Authorization = new("Bearer", password);
                var send = await Http.SendAsync(reqMsg);
                if (!send.IsSuccessStatusCode)
                    Say($"Poznámka: /send vrátil HTTP {(int)send.StatusCode}");
            }
            catch (Exception ex)
            {
                Say($"Poznámka: /send selhal: {ex.Message}");
            }
        }

        // best-effort disconnect
        try
        {
            await Post(sidUrl, "41");
        }
        catch { }

        res.Ok = true;
        res.Message = $"Hotovo — mapa „{title}“ (id {mapId}).";
        return res;
    }

    /// <summary>One GraphQL operation over the socket — emit
    /// `@graphql/execute`, poll until the matching `@graphql/result`.</summary>
    static async Task<JsonElement?> Graphql(
        string sidUrl,
        int id,
        string operation,
        Dictionary<string, object> variables
    )
    {
        var payload = JsonSerializer.Serialize(
            new object[]
            {
                "@graphql/execute",
                new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["operation"] = operation,
                    ["variables"] = variables,
                },
            }
        );
        await Post(sidUrl, "42" + payload);
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
            foreach (var (evt, arg) in await Poll(sidUrl))
                if (evt == "@graphql/result" && arg.GetProperty("id").GetInt32() == id)
                    return arg;
        return null;
    }

    /// <summary>One long-poll GET → decoded socket.io events as
    /// (name, payload) pairs. Also answers engine ping (2) with pong.</summary>
    static async Task<List<(string Evt, JsonElement Arg)>> Poll(string sidUrl)
    {
        var events = new List<(string, JsonElement)>();
        try
        {
            var body = await Http.GetStringAsync(sidUrl);
            foreach (var packet in body.Split('\x1e'))
            {
                if (packet.Length == 0)
                    continue;
                if (packet[0] == '2') // EIO ping → pong
                {
                    _ = Post(sidUrl, "3");
                    continue;
                }
                // socket.io event packet: "42[...]"
                if (packet.StartsWith("42"))
                {
                    using var doc = JsonDocument.Parse(packet[2..]);
                    var arr = doc.RootElement;
                    if (arr.GetArrayLength() >= 1)
                    {
                        var name = arr[0].GetString()!;
                        var arg =
                            arr.GetArrayLength() > 1
                                ? arr[1].Clone()
                                : JsonDocument.Parse("{}").RootElement.Clone();
                        events.Add((name, arg));
                    }
                }
            }
        }
        catch
        {
            // transient poll failure — the caller retries until its deadline
        }
        return events;
    }

    static async Task Post(string url, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "text/plain");
        var res = await Http.PostAsync(url, content);
        res.EnsureSuccessStatusCode();
    }
}
