// Upload dialog — server URL, DM password, title, live-map toggle.
// Exports the current render and pushes it through DrBridge; the
// status label shows each step of the flow.
using System;
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

    public static BridgeDialog Create(Func<byte[]> pngProvider)
    {
        var d = new BridgeDialog
        {
            Title = "Odeslat do DMmap",
            _pngProvider = pngProvider,
        };
        d.Build();
        return d;
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
                s => _status.Text = s
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
