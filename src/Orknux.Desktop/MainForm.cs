using System.Runtime.InteropServices;
using Orknux.Desktop.Core;

namespace Orknux.Desktop;

/// <summary>
/// The window a person connects from, the log of what their agents did, and the
/// tray icon it lives in afterwards.
///
/// The desktop is lent exactly while this is connected. Disconnect, quitting
/// from the tray, or the stop hotkey - Ctrl+Alt+Q, which works whatever has the
/// focus - takes it back, and <see cref="Notice"/> says on screen whenever an
/// agent is acting.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(0x12, 0x12, 0x14);
    private static readonly Color Panel = Color.FromArgb(0x1b, 0x1b, 0x1f);
    private static readonly Color Line = Color.FromArgb(0x2c, 0x2c, 0x33);
    private static readonly Color Ink = Color.FromArgb(0xe7, 0xe7, 0xea);
    private static readonly Color Muted = Color.FromArgb(0x9a, 0x9a, 0xa3);
    private static readonly Color Accent = Color.FromArgb(0x5b, 0x76, 0xd0);
    private static readonly Color Good = Color.FromArgb(0x4c, 0xc3, 0x8a);
    private static readonly Color Bad = Color.FromArgb(0xe5, 0x48, 0x4d);

    private const int StopHotkey = 0x4F4B;
    private const int WM_HOTKEY = 0x0312;

    private readonly TextBox server = new();
    private readonly TextBox token = new() { UseSystemPasswordChar = true };
    private readonly Button connect = new() { Text = "Connect" };
    private readonly Button disconnect = new() { Text = "Disconnect", Visible = false };
    private readonly Label state = new() { Text = "Not connected", AutoSize = true };
    private readonly Label message = new() { AutoSize = false, Height = 40, Visible = false };
    private readonly ListBox log = new() { IntegralHeight = false, BorderStyle = BorderStyle.None };
    private readonly NotifyIcon tray = new();
    private readonly Notice notice = new();
    private readonly Settings settings = Settings.Load();
    private readonly string version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private UiaRunner? runner;
    private Connection? connection;
    private CancellationTokenSource? running;
    private bool quitting;

    public MainForm()
    {
        Text = "Orknux for Windows";
        ClientSize = new Size(460, 560);
        MinimumSize = new Size(380, 420);
        BackColor = Background;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        Icon = SystemIcons.Application;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 9, BackColor = Background };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(Caption("Server"));
        Field(server, "https://orknux.example.com");
        layout.Controls.Add(server);
        layout.Controls.Add(Caption("Pairing code or access token"));
        Field(token, "K7P-4QX or orkx_…");
        layout.Controls.Add(token);
        layout.Controls.Add(new Label
        {
            Text = "In Orknux, open Preferences › Connected clients and press Connect a client, then type the code here. A read and write access token works too. Your agents act on this computer as you while it is connected. Ctrl+Alt+Q stops them at any time.",
            ForeColor = Muted, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 4, 0, 10),
        });

        var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Background, Margin = new Padding(0) };
        Style(connect, primary: true);
        Style(disconnect, primary: false);
        buttons.Controls.Add(connect);
        buttons.Controls.Add(disconnect);
        state.ForeColor = Muted;
        state.Margin = new Padding(12, 8, 0, 0);
        buttons.Controls.Add(state);
        layout.Controls.Add(buttons);

        message.ForeColor = Bad;
        message.Dock = DockStyle.Fill;
        layout.Controls.Add(message);
        layout.Controls.Add(new Label { Text = "WHAT YOUR AGENTS DID", ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 12, 0, 6), Font = new Font(Font.FontFamily, 8f) });
        log.BackColor = Panel;
        log.ForeColor = Ink;
        log.Dock = DockStyle.Fill;
        log.DrawMode = DrawMode.OwnerDrawFixed;
        log.ItemHeight = 22;
        log.DrawItem += DrawLogLine;
        layout.Controls.Add(log);
        Controls.Add(layout);

        server.Text = settings.Server;
        token.Text = settings.Token;
        connect.Click += async (_, _) => await Connect();
        disconnect.Click += (_, _) => Stop("Disconnected.");
        AcceptButton = connect;

        tray.Icon = SystemIcons.Application;
        tray.Text = "Orknux for Windows";
        tray.Visible = true;
        tray.DoubleClick += (_, _) => Reveal();
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => Reveal());
        tray.ContextMenuStrip.Items.Add("Disconnect", null, (_, _) => Stop("Disconnected."));
        tray.ContextMenuStrip.Items.Add("Quit", null, (_, _) => { quitting = true; Close(); });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Ctrl+Alt+Q, wherever the focus is: the one way to stop an agent mid-action without finding this window.
        RegisterHotKey(Handle, StopHotkey, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.Q);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == StopHotkey && running is not null)
        {
            Stop("Stopped with Ctrl+Alt+Q.");
            Reveal();
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window keeps the connection in the tray; Quit from the tray ends it.
        if (!quitting && e.CloseReason == CloseReason.UserClosing && running is not null)
        {
            e.Cancel = true;
            Hide();
            tray.ShowBalloonTip(3000, "Orknux for Windows", "Still connected. Ctrl+Alt+Q stops your agents; Quit is in the tray menu.", ToolTipIcon.Info);
            return;
        }
        Stop(null);
        UnregisterHotKey(Handle, StopHotkey);
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    private async Task Connect()
    {
        Say(null);
        if (!Uri.TryCreate(server.Text.Trim(), UriKind.Absolute, out var address) || (address.Scheme != "https" && address.Scheme != "http"))
        {
            Say("That is not a server address. It starts with https://.");
            return;
        }
        if (token.Text.Trim().Length == 0)
        {
            Say("Type the pairing code from Orknux, or paste an access token.");
            return;
        }
        if (Pairing.IsCode(token.Text))
        {
            // A code works once and for minutes: traded now, and the credential is what is kept.
            connect.Enabled = false;
            SetState("Pairing…", Muted);
            try
            {
                token.Text = await Pairing.TradeAsync(address, token.Text, $"Windows on {Environment.MachineName}", CancellationToken.None);
            }
            catch (ConnectionRefusedException refused)
            {
                SetState("Not connected", Muted);
                Say(refused.Message);
                return;
            }
            finally
            {
                connect.Enabled = true;
            }
        }
        settings.Server = address.ToString().TrimEnd('/');
        settings.Token = token.Text.Trim();
        settings.Save();

        runner ??= new UiaRunner();
        connection?.Dispose();
        connection = new Connection(address, token.Text, version, runner);
        connection.Logged += (line, failed) => BeginInvoke(() => Log(line, failed));
        connection.StateChanged += said => BeginInvoke(() => SetState(said, said.StartsWith("Connected") ? Good : Muted));
        connection.Busy += on => BeginInvoke(() => { if (on) notice.ShowActing(); else notice.Done(); });

        SetState("Connecting…", Muted);
        connect.Enabled = false;
        running = new CancellationTokenSource();
        try
        {
            var user = await connection.HelloAsync(running.Token);
            Showing(connected: true);
            SetState($"Connected as {user}", Good);
            Log($"Connected to {address.Host} as {user}", false);
            var loop = running;
            _ = Task.Run(async () =>
            {
                await connection.RunAsync(loop.Token);
                if (!loop.IsCancellationRequested) BeginInvoke(() => Stop("The server stopped accepting this computer."));
            });
        }
        catch (ConnectionRefusedException refused)
        {
            running = null;
            SetState("Not connected", Muted);
            Say(refused.Message);
        }
        finally
        {
            connect.Enabled = true;
        }
    }

    private void Stop(string? reason)
    {
        if (running is null) return;
        running.Cancel();
        running = null;
        notice.Done();
        Showing(connected: false);
        SetState("Not connected", Muted);
        if (reason is not null)
        {
            Say(reason);
            Log(reason, false);
        }
    }

    private void Showing(bool connected)
    {
        connect.Visible = !connected;
        disconnect.Visible = connected;
        server.Enabled = !connected;
        token.Enabled = !connected;
        tray.Text = connected ? "Orknux for Windows - connected" : "Orknux for Windows";
    }

    private void Reveal()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void SetState(string text, Color colour)
    {
        state.Text = text;
        state.ForeColor = colour;
    }

    private void Say(string? text)
    {
        message.Visible = text is not null;
        message.Text = text ?? "";
    }

    private void Log(string line, bool failed)
    {
        log.Items.Insert(0, (DateTime.Now.ToString("HH:mm:ss") + "  " + line, failed));
        while (log.Items.Count > 200) log.Items.RemoveAt(log.Items.Count - 1);
    }

    private void DrawLogLine(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var (line, failed) = ((string, bool))log.Items[e.Index];
        using var back = new SolidBrush(Panel);
        e.Graphics.FillRectangle(back, e.Bounds);
        TextRenderer.DrawText(e.Graphics, line, Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            failed ? Bad : Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var rule = new Pen(Line);
        e.Graphics.DrawLine(rule, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private static Label Caption(string text) =>
        new() { Text = text, ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };

    private static void Field(TextBox box, string placeholder)
    {
        box.PlaceholderText = placeholder;
        box.BackColor = Panel;
        box.ForeColor = Ink;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Dock = DockStyle.Fill;
    }

    private static void Style(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = primary ? Accent : Line;
        button.BackColor = primary ? Accent : Background;
        button.ForeColor = Color.White;
        button.AutoSize = true;
        button.Padding = new Padding(10, 3, 10, 3);
    }

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            connection?.Dispose();
            runner?.Dispose();
            notice.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
