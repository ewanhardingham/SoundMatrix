using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace SoundMatrix;

internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _openItem;

    public TrayIcon(Action open, Action settings, Action exit, bool testMode)
    {
        _openItem = new Forms.ToolStripMenuItem("Open matrix", null, (_, _) => open());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_openItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings", null, (_, _) => settings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => exit()));

        var version = typeof(TrayIcon).Assembly.GetName().Version;
        var text = testMode ? "SoundMatrix (test mode)"
            : version is null ? "SoundMatrix" : $"SoundMatrix {version.ToString(3)}";
        _icon = new Forms.NotifyIcon { Icon = CreateIcon(), Text = text, ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void SetOpenHotkey(Hotkey hotkey) =>
        _openItem.Text = hotkey.IsEmpty ? "Open matrix" : $"Open matrix  ({hotkey.Display})";

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    /// <summary>A 2×2 grid of the first four palette colours.</summary>
    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var cells = new[] { (1, 1), (17, 1), (1, 17), (17, 17) };
            for (int i = 0; i < cells.Length; i++)
            {
                using var brush = new SolidBrush(ColorTranslator.FromHtml(AppSettings.Palette[i]));
                using var path = RoundedRect(new Rectangle(cells[i].Item1, cells[i].Item2, 14, 14), 4);
                g.FillPath(brush, path);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
