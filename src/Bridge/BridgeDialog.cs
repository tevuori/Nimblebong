#nullable enable
// Upload dialog — server URL, DM password, title, live-map toggle.
// Exports the current render and pushes it through DrBridge; the
// status label shows each step of the flow.
using System;
using System.Collections.Generic;
using Godot;

namespace DmMap;

public partial class BridgeDialog : AcceptDialog
{
    LineEdit _server = null!;
    LineEdit _password = null!;
    LineEdit _title = null!;
    CheckBox _live = null!;
    Label _status = null!;
    Button _send = null!;
    Func<byte[]> _pngProvider = () => [];
    Func<Scene> _sceneProvider = () => new Scene();

    public static BridgeDialog Create(Func<byte[]> pngProvider, Func<Scene>? sceneProvider = null)
    {
        var d = new BridgeDialog
        {
            Title = "Odeslat do DMmap",
            _pngProvider = pngProvider,
        };
        if (sceneProvider != null)
            d._sceneProvider = sceneProvider;
        d.Build();
        return d;
    }

    /// <summary>Marker props → DR tokens. Marker Scale maps to token
    /// radius; no label/text in our model yet — leave label empty.</summary>
    List<Dictionary<string, object>> Tokens()
    {
        var tokens = new List<Dictionary<string, object>>();
        foreach (var p in _sceneProvider().Props)
        {
            if (p.Kind != "marker")
                continue;
            tokens.Add(
                new Dictionary<string, object>
                {
                    ["x"] = p.X,
                    ["y"] = p.Y,
                    ["color"] = "#e5484d",
                    ["label"] = "",
                    ["radius"] = Math.Max(8, p.Scale * 0.5),
                    ["isVisibleForPlayers"] = true,
                }
            );
        }
        return tokens;
    }

    void Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        AddChild(box);

        _server = Row(box, "Server", "http://localhost:3000");
        _password = Row(box, "Heslo (DM)", "");
        _password.Secret = true;
        _title = Row(box, "Název mapy", "DMmap mapa");
        _live = new CheckBox { Text = "Nastavit jako živou mapu" };
        box.AddChild(_live);
        _status = new Label { Text = "" };
        box.AddChild(_status);
        _send = new Button { Text = "Odeslat" };
        box.AddChild(_send);
        _send.Pressed += OnSend;
    }

    static LineEdit Row(VBoxContainer box, string label, string value)
    {
        box.AddChild(new Label { Text = label });
        var e = new LineEdit { Text = value };
        box.AddChild(e);
        return e;
    }

    async void OnSend()
    {
        _send.Disabled = true;
        _status.Text = "Exportuji…";
        try
        {
            var png = _pngProvider();
            var res = await DrBridge.UploadMap(
                _server.Text,
                _password.Text,
                _title.Text,
                png,
                _live.ButtonPressed,
                s => _status.Text = s,
                Tokens()
            );
            _status.Text = res.Message;
        }
        catch (Exception ex)
        {
            _status.Text = "Chyba: " + ex.Message;
        }
        finally
        {
            _send.Disabled = false;
        }
    }
}
