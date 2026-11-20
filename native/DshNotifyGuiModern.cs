using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Shared Fluent-like colours and small drawing primitives.  The controls remain
// the real WinForms controls; this class only changes their presentation.
internal static class ModernTheme
{
    internal static bool Enabled { get; private set; }
    internal static Color Surface { get; private set; }
    internal static Color Card { get; private set; }
    internal static Color CardBorder { get; private set; }
    internal static Color Accent { get; private set; }
    internal static Color AccentSoft { get; private set; }
    internal static Color TextPrimary { get; private set; }
    internal static Color TextSecondary { get; private set; }
    internal static Color StatusOk { get; private set; }
    internal static Color StatusWarn { get; private set; }

    internal static void Apply(bool enabled, bool dark)
    {
        Enabled = enabled;
        Surface = dark ? Color.FromArgb(22, 31, 48) : Color.FromArgb(239, 247, 255);
        Card = dark ? Color.FromArgb(31, 45, 68) : Color.FromArgb(250, 253, 255);
        CardBorder = dark ? Color.FromArgb(61, 84, 119) : Color.FromArgb(196, 218, 240);
        Accent = dark ? Color.FromArgb(112, 183, 255) : Color.FromArgb(34, 116, 214);
        AccentSoft = dark ? Color.FromArgb(39, 73, 112) : Color.FromArgb(215, 235, 255);
        TextPrimary = dark ? Color.FromArgb(235, 244, 255) : Color.FromArgb(24, 57, 91);
        TextSecondary = dark ? Color.FromArgb(174, 199, 230) : Color.FromArgb(73, 108, 142);
        StatusOk = dark ? Color.FromArgb(107, 207, 161) : Color.FromArgb(28, 139, 91);
        StatusWarn = dark ? Color.FromArgb(255, 183, 102) : Color.FromArgb(177, 105, 25);
    }

    internal static void PaintCard(Graphics graphics, Rectangle bounds)
    {
        using (GraphicsPath path = Rounded(bounds, 8))
        using (Brush fill = new SolidBrush(Card))
        using (Pen edge = new Pen(CardBorder))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(edge, path);
        }
    }

    internal static void PaintStatus(Graphics graphics, Point center, bool ok)
    {
        using (Brush brush = new SolidBrush(ok ? StatusOk : StatusWarn))
            graphics.FillEllipse(brush, center.X - 4, center.Y - 4, 8, 8);
    }

    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernToggle : CheckBox
{
    public ModernToggle()
    {
        AutoSize = false;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        if (!ModernTheme.Enabled) { base.OnPaint(args); return; }
        Rectangle track = new Rectangle(Math.Max(0, Width - 42), Math.Max(1, (Height - 20) / 2), 38, 20);
        using (GraphicsPath path = ModernTheme.Rounded(track, 10))
        using (Brush fill = new SolidBrush(Checked ? ModernTheme.Accent : ModernTheme.CardBorder))
        using (Brush thumb = new SolidBrush(Color.White))
        {
            args.Graphics.FillPath(fill, path);
            int left = Checked ? track.Right - 18 : track.Left + 2;
            args.Graphics.FillEllipse(thumb, left, track.Top + 2, 16, 16);
        }
        TextRenderer.DrawText(args.Graphics, Text, Font, new Rectangle(0, 0, Math.Max(1, track.Left - 5), Height),
            ModernTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(args.Graphics, ClientRectangle, ModernTheme.TextPrimary, BackColor);
    }
}

internal sealed class ModernSectionLabel : Label
{
    internal string HelpText { get; set; }

    public ModernSectionLabel()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.TopLeft;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        if (!ModernTheme.Enabled) { base.OnPaint(args); return; }
        TextRenderer.DrawText(args.Graphics, Text, Font, new Rectangle(0, 0, Width, Font.Height + 2), ModernTheme.Accent,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(args.Graphics, HelpText ?? String.Empty, Font,
            new Rectangle(0, Font.Height + 1, Width, Math.Max(1, Height - Font.Height - 1)), ModernTheme.TextSecondary,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
