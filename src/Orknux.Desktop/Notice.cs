namespace Orknux.Desktop;

/// <summary>
/// A strip at the top of the screen saying an agent is acting, and how to stop
/// it. On a desktop the agent works on the person's own screen rather than in a
/// window of its own, so this is the equivalent of Chrome's "is being debugged"
/// bar: never in the way of a click, never taking the focus, always on top.
/// It stays a moment after the last action so a run of them reads as one.
/// </summary>
internal sealed class Notice : Form
{
    private readonly System.Windows.Forms.Timer linger = new() { Interval = 2500 };

    public Notice()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x5b, 0x76, 0xd0);
        Size = new Size(460, 30);
        Controls.Add(new Label
        {
            Text = "Orknux is using this computer  ·  Ctrl+Alt+Q stops it",
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 9.5f),
        });
        linger.Tick += (_, _) => { linger.Stop(); Hide(); };
    }

    /// <summary>Never activated, and clicks pass through it to what is underneath.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8;
            var create = base.CreateParams;
            create.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOPMOST;
            return create;
        }
    }

    public void ShowActing()
    {
        linger.Stop();
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top);
        if (!Visible)
        {
            Show();
            Opacity = 0.92;
        }
    }

    public void Done()
    {
        if (Visible) linger.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) linger.Dispose();
        base.Dispose(disposing);
    }
}
