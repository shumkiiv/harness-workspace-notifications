using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class TaskIcon
{
    internal static readonly Icon Small = Load(SystemInformation.SmallIconSize);
    internal static readonly Icon Window = Load(SystemInformation.IconSize);

    private static Icon Load(Size size)
    {
        using (Stream stream = typeof(TaskIcon).Assembly.GetManifestResourceStream("HarnessTasks.ico"))
        {
            return new Icon(stream, size);
        }
    }
}

public static class Graphite
{
    public static bool IsDark { get; private set; }
    public static Color Base { get; private set; }
    public static Color Card { get; private set; }
    public static Color Border { get; private set; }
    public static Color Primary { get; private set; }
    public static Color Secondary { get; private set; }
    public static Color QuotaLow { get; private set; }
    public static Color QuotaMedium { get; private set; }
    public static Color QuotaHigh { get; private set; }
    public static readonly Color Running = Color.FromArgb(127, 179, 224);
    public static readonly Color Completed = Color.FromArgb(127, 194, 154);
    public static readonly Color Issue = Color.FromArgb(224, 147, 122);

    static Graphite()
    {
        ApplyTheme(true);
    }

    public static void ApplyTheme(bool dark)
    {
        IsDark = dark;
        Base = dark ? Color.FromArgb(30, 33, 38) : Color.FromArgb(245, 247, 250);
        Card = dark ? Color.FromArgb(38, 42, 49) : Color.FromArgb(255, 255, 255);
        Border = dark ? Color.FromArgb(52, 58, 68) : Color.FromArgb(205, 211, 218);
        Primary = dark ? Color.FromArgb(232, 234, 237) : Color.FromArgb(35, 40, 46);
        Secondary = dark ? Color.FromArgb(169, 177, 187) : Color.FromArgb(92, 101, 112);
        QuotaLow = dark ? Color.FromArgb(170, 108, 102) : Color.FromArgb(181, 111, 101);
        QuotaMedium = dark ? Color.FromArgb(176, 151, 97) : Color.FromArgb(165, 136, 76);
        QuotaHigh = dark ? Color.FromArgb(105, 159, 132) : Color.FromArgb(80, 137, 111);
    }

    public static Color Accent(string status)
    {
        if (String.Equals(status, "running", StringComparison.OrdinalIgnoreCase)) return IsDark ? Running : Color.FromArgb(45, 103, 151);
        if (String.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)) return IsDark ? Completed : Color.FromArgb(45, 115, 75);
        if (String.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
            || String.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase)
            || String.Equals(status, "max-tokens", StringComparison.OrdinalIgnoreCase)
            || String.Equals(status, "aborted", StringComparison.OrdinalIgnoreCase)
            || String.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase)) return IsDark ? Issue : Color.FromArgb(151, 69, 51);
        return Secondary;
    }

    public static Color ChipFill(string status)
    {
        if (String.Equals(status, "running", StringComparison.OrdinalIgnoreCase)) return IsDark ? Color.FromArgb(31, 44, 56) : Color.FromArgb(224, 239, 249);
        if (String.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)) return IsDark ? Color.FromArgb(31, 47, 39) : Color.FromArgb(225, 242, 232);
        if (Accent(status) == Issue) return IsDark ? Color.FromArgb(51, 39, 36) : Color.FromArgb(250, 229, 223);
        return Card;
    }
}

internal sealed class ItemScrollList : Panel
{
    private const int TrackWidth = 14;
    private const int WheelStep = 120;
    private int firstIndex;
    private int lastIndex;
    private int wheelDelta;
    private bool arranging;
    private int? thumbDragOffset;

    public ItemScrollList()
    {
        SetStyle(ControlStyles.Selectable, true);
        DoubleBuffered = true;
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    internal int FirstIndex
    {
        get { return firstIndex; }
        set
        {
            firstIndex = Math.Max(0, value);
            PerformLayout();
        }
    }

    protected override void OnLayout(LayoutEventArgs args)
    {
        if (arranging) return;
        arranging = true;
        try
        {
            base.OnLayout(args);
            int availableHeight = Math.Max(0, ClientSize.Height - Padding.Vertical);
            int remaining = availableHeight;
            lastIndex = Math.Max(0, Controls.Count - 1);
            for (int index = Controls.Count - 1; index >= 0; index--)
            {
                int height = ItemHeight(Controls[index]);
                if (height > remaining) break;
                remaining -= height;
                lastIndex = index;
            }
            firstIndex = Math.Min(firstIndex, lastIndex);
            int width = Math.Max(1, ClientSize.Width - Padding.Horizontal - (lastIndex > 0 ? TrackWidth : 0));
            int top = Padding.Top;
            bool fits = availableHeight > 0;
            for (int index = 0; index < Controls.Count; index++)
            {
                Control item = Controls[index];
                if (index < firstIndex)
                {
                    item.Hide();
                    continue;
                }
                fits = fits && top + ItemHeight(item) <= ClientSize.Height - Padding.Bottom;
                if (!fits)
                {
                    item.Hide();
                    continue;
                }
                item.Bounds = new Rectangle(Padding.Left, top + item.Margin.Top, width, item.Height);
                item.Show();
                top += ItemHeight(item);
            }
        }
        finally
        {
            arranging = false;
        }
        Invalidate();
    }

    private static int ItemHeight(Control item)
    {
        return item.Height + item.Margin.Vertical;
    }

    internal int ContentHeight { get { return Padding.Vertical + Controls.Cast<Control>().Sum(item => ItemHeight(item)); } }

    internal int TallestItemHeight
    {
        get { return Controls.Count == 0 ? 0 : Controls.Cast<Control>().Max(item => ItemHeight(item)); }
    }

    internal void ScrollWheel(int delta)
    {
        wheelDelta += delta;
        int steps = wheelDelta / WheelStep;
        wheelDelta %= WheelStep;
        if (steps != 0) FirstIndex = firstIndex - steps;
    }

    protected override void OnMouseWheel(MouseEventArgs args)
    {
        if (lastIndex > 0)
        {
            ScrollWheel(args.Delta);
            var handled = args as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }
        base.OnMouseWheel(args);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        return key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (args.KeyCode == Keys.Up || args.KeyCode == Keys.Down)
        {
            FirstIndex = firstIndex + (args.KeyCode == Keys.Up ? -1 : 1);
            args.Handled = true;
        }
        base.OnKeyDown(args);
    }

    private Rectangle Track()
    {
        return new Rectangle(ClientSize.Width - Padding.Right - TrackWidth + 3,
            Padding.Top, TrackWidth - 4, Math.Max(0, ClientSize.Height - Padding.Vertical));
    }

    private Rectangle Thumb(Rectangle track)
    {
        int height = Math.Min(track.Height, Math.Max(18,
            track.Height * (Controls.Count - lastIndex) / Math.Max(1, Controls.Count)));
        int y = track.Y + (track.Height - height) * firstIndex / Math.Max(1, lastIndex);
        return new Rectangle(track.X + 2, y, track.Width - 4, height);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        if (lastIndex == 0) return;
        Rectangle track = Track();
        bool dark = BackColor.GetBrightness() < 0.5f;
        using (Brush groove = new SolidBrush(dark ? Color.FromArgb(16, 18, 20) : SystemColors.ControlLight))
        using (Brush slider = new SolidBrush(dark ? Color.FromArgb(67, 73, 82) : SystemColors.ControlDark))
        {
            args.Graphics.FillRectangle(groove, track);
            args.Graphics.FillRectangle(slider, Thumb(track));
        }
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (lastIndex == 0 || args.Button != MouseButtons.Left || !Track().Contains(args.Location)) return;
        Rectangle thumb = Thumb(Track());
        if (args.Y >= thumb.Top && args.Y < thumb.Bottom)
        {
            thumbDragOffset = args.Y - thumb.Top;
            Capture = true;
        }
        else
        {
            FirstIndex = firstIndex + (args.Y < thumb.Top ? -1 : 1);
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (!thumbDragOffset.HasValue || lastIndex == 0) return;
        Rectangle track = Track();
        int travel = track.Height - Thumb(track).Height;
        if (travel <= 0) return;
        int offset = Math.Max(0, Math.Min(travel, args.Y - track.Y - thumbDragOffset.Value));
        FirstIndex = (int)Math.Round((double)offset * lastIndex / travel);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button == MouseButtons.Left && thumbDragOffset.HasValue) Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        thumbDragOffset = null;
        base.OnMouseCaptureChanged(args);
    }
}

internal sealed class StatusGlyph : Control
{
    internal const int GlyphSize = 16;
    private readonly string status;

    public StatusGlyph(string value)
    {
        status = value ?? String.Empty;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color color = Graphite.Accent(status);
        using (Pen pen = new Pen(color, 1.6F))
        using (Brush brush = new SolidBrush(color))
        {
            pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            if (status == "running")
                graphics.FillPolygon(brush, new[] { new Point(4, 2), new Point(13, 8), new Point(4, 13) });
            else if (status == "completed")
                graphics.DrawLines(pen, new[] { new Point(3, 8), new Point(6, 11), new Point(13, 4) });
            else if (status == "error" || status == "blocked" || status == "max-tokens")
            {
                graphics.DrawEllipse(pen, 2, 2, 11, 11);
                graphics.DrawLine(pen, 7.5F, 5, 7.5F, 8);
                graphics.FillEllipse(brush, 6.75F, 9.5F, 1.5F, 1.5F);
            }
            else if (status == "aborted" || status == "interrupted")
            {
                graphics.DrawLine(pen, 4, 4, 11, 11);
                graphics.DrawLine(pen, 11, 4, 4, 11);
            }
            else if (status == "disposed")
                graphics.DrawLine(pen, 4, 8, 11, 8);
            else
                graphics.DrawEllipse(pen, 4, 4, 7, 7);
        }
    }
}

public sealed class CompactWidgetControl : Control
{
    private const int NamesHeight = 16;
    private IList<WidgetRow> rows = new List<WidgetRow>();
    private Settings settings;
    private float scale = 1F;
    private int logicalWidth;
    private int logicalHeight;
    public float WidgetZoom { get { return scale; } set { scale = value < .9F ? .85F : (value > 1.1F ? 1.25F : 1F); MinimumSize = new Size((int)Math.Round(230 * scale), (int)Math.Round(85 * scale)); Invalidate(); } }
    public CompactWidgetControl()
    {
        DoubleBuffered = true;
        BackColor = ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base;
        ForeColor = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary;
        MinimumSize = new Size(230, 85);
    }

    public void UpdateRows(IList<WidgetRow> value, Settings current)
    {
        rows = value ?? new List<WidgetRow>();
        settings = current;
        AccessibleName = "Сводка рабочих областей: " + rows.Count;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.ScaleTransform(scale, scale);
        graphics.Clear(BackColor);
        logicalWidth = Math.Max(1, (int)Math.Round(ClientSize.Width / scale));
        logicalHeight = Math.Max(1, (int)Math.Round(ClientSize.Height / scale));
        Rectangle bounds = new Rectangle(0, 0, logicalWidth, logicalHeight);
        if (ModernTheme.Enabled)
        {
            ModernTheme.PaintCard(graphics, bounds);
        }
        else
        {
            using (Brush surface = new SolidBrush(Graphite.Card))
            using (Pen border = new Pen(Graphite.Border))
            {
                graphics.FillRectangle(surface, bounds);
                graphics.DrawRectangle(border, 0, 0, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
            }
        }
        if (rows.Count == 0)
        {
            DrawEmpty(graphics);
            return;
        }
        if (settings != null && settings.WidgetCounts)
        {
            DrawMetrics(graphics);
        }
        DrawRegisteredNames(graphics);
    }

    private void DrawEmpty(Graphics graphics)
    {
        DrawText(graphics, "Нет активности", new Rectangle(12, 28, logicalWidth - 24, 24), 10F, Graphite.Primary);
        DrawText(graphics, "Новые задачи появятся здесь", new Rectangle(12, 56, logicalWidth - 24, 20), 8F, Graphite.Secondary);
    }

    private void DrawRegisteredNames(Graphics graphics)
    {
        if (settings == null || !settings.FieldWorkspace) return;
        List<string> names = rows.Where(row => row.Registered && !String.IsNullOrEmpty(row.Workspace)).Select(row => row.Workspace).Take(2).ToList();
        if (names.Count == 0) return;
        int extra = rows.Count(row => row.Registered) - names.Count;
        string text = String.Join(" · ", names.ToArray()) + (extra > 0 ? " +" + extra : "");
        int namesTop = settings.WidgetCounts ? 62 : logicalHeight - 22;
        DrawText(graphics, text, new Rectangle(12, namesTop, logicalWidth - 24, NamesHeight), 8F, Graphite.Secondary);
    }

    private void DrawMetrics(Graphics graphics)
    {
        int running = Count("running");
        int completed = Count("completed");
        int issues = rows.Count - running - completed;
        int width = Math.Max(58, (logicalWidth - 40) / 3);
        DrawMetric(graphics, new Rectangle(12, 14, width, 42), 0, running, "активных", Color.FromArgb(127, 179, 224));
        DrawMetric(graphics, new Rectangle(20 + width, 14, width, 42), 1, completed, "готово", Color.FromArgb(127, 194, 154));
        DrawMetric(graphics, new Rectangle(28 + width * 2, 14, width, 42), 2, issues, "прочее", Graphite.Secondary);
    }

    private void DrawMetric(Graphics graphics, Rectangle rectangle, int kind, int value, string label, Color accent)
    {
        using (Pen border = new Pen(Graphite.Border))
        {
            graphics.DrawRectangle(border, rectangle);
        }
        var saved = graphics.Save();
        graphics.ScaleTransform(1F / scale, 1F / scale);
        DrawIcon(graphics, new Rectangle((int)Math.Round((rectangle.Left + 5) * scale),
            (int)Math.Round((rectangle.Top + 6) * scale), 12, 12), kind, accent);
        graphics.Restore(saved);
        DrawText(graphics, Convert.ToString(value), new Rectangle(rectangle.Left + 21, rectangle.Top + 4, rectangle.Width - 24, 20), 14F, Graphite.Primary);
        DrawText(graphics, label, new Rectangle(rectangle.Left + 5, rectangle.Top + 25, rectangle.Width - 8, 14), 7F, Graphite.Secondary);
    }

    private static void DrawIcon(Graphics graphics, Rectangle rectangle, int kind, Color color)
    {
        using (Pen pen = new Pen(color, 1.5F))
        using (Brush brush = new SolidBrush(color))
        {
            if (kind == 0)
            {
                graphics.FillPolygon(brush, new Point[] { new Point(rectangle.Left + 2, rectangle.Top), new Point(rectangle.Right, rectangle.Top + 6), new Point(rectangle.Left + 2, rectangle.Bottom) });
            }
            else if (kind == 1)
            {
                graphics.DrawEllipse(pen, rectangle);
                graphics.DrawLine(pen, rectangle.Left + 3, rectangle.Top + 6, rectangle.Left + 5, rectangle.Bottom - 3);
                graphics.DrawLine(pen, rectangle.Left + 5, rectangle.Bottom - 3, rectangle.Right - 2, rectangle.Top + 3);
            }
            else
            {
                graphics.DrawRectangle(pen, rectangle);
                graphics.DrawLine(pen, rectangle.Left + 6, rectangle.Top + 2, rectangle.Left + 6, rectangle.Top + 8);
                graphics.DrawLine(pen, rectangle.Left + 6, rectangle.Bottom - 3, rectangle.Left + 6, rectangle.Bottom - 2);
            }
        }
    }

    private int Count(string status)
    {
        int count = 0;
        foreach (WidgetRow row in rows)
        {
            if (String.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }

    private static void DrawText(Graphics graphics, string text, Rectangle rectangle, float size, Color color)
    {
        using (Font font = new Font("Segoe UI", size, FontStyle.Regular))
        using (Brush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        {
            graphics.DrawString(text, font, brush, rectangle, format);
        }
    }
}
