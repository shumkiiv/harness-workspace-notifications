using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class NotificationSounds
{
    internal const string DefaultId = "icq";
    private const uint AsyncSoundFile = 0x00020000 | 0x0001 | 0x0002;
    private static readonly Dictionary<string, SoundPlayer> players = new Dictionary<string, SoundPlayer>();
    internal static readonly KeyValuePair<string, string>[] Presets =
    {
        new KeyValuePair<string, string>(DefaultId, "ICQ «Uh-oh!»"),
        new KeyValuePair<string, string>("boing", "Пружинка"),
        new KeyValuePair<string, string>("squeak", "Резиновая игрушка"),
        new KeyValuePair<string, string>("whistle", "Мультяшный свисток"),
        new KeyValuePair<string, string>("pop", "Хлопок"),
        new KeyValuePair<string, string>("scratch", "Скрежет пластинки")
    };

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(string path, IntPtr module, uint flags);

    internal static int IndexOf(string id)
    {
        int index = Array.FindIndex(Presets, item => item.Key == id);
        return index < 0 ? 0 : index;
    }

    private static SoundPlayer GetPlayer(string id)
    {
        SoundPlayer player;
        if (!players.TryGetValue(id, out player))
        {
            Stream wave = typeof(NotificationSounds).Assembly.GetManifestResourceStream("Sounds." + id + ".wav");
            if (wave == null) return null;
            player = new SoundPlayer(wave);
            players.Add(id, player);
        }
        return player;
    }

    internal static bool LoadPackagedPresets()
    {
        foreach (var preset in Presets.Skip(1))
        {
            SoundPlayer player = GetPlayer(preset.Key);
            if (player == null) return false;
            player.Load();
        }
        return true;
    }

    internal static void Play(string id)
    {
        int preset = IndexOf(id);
        if (preset == 0)
        {
            string dataDir = Environment.GetEnvironmentVariable("DSH_WN_DATA_DIR");
            if (String.IsNullOrEmpty(dataDir))
                dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "workspace-notifications");
            string file = Path.Combine(dataDir, "icq-message.wav");
            if (File.Exists(file) && PlaySoundW(file, IntPtr.Zero, AsyncSoundFile)) return;
        }
        else
        {
            SoundPlayer player = GetPlayer(Presets[preset].Key);
            if (player != null)
            {
                player.Play();
                return;
            }
        }
        SystemSounds.Asterisk.Play();
    }
}

public class Settings
{
    public bool ShowWidget { get; set; }
    public bool ShowNotifications { get; set; }
    public bool NotificationSound { get; set; }
    public string NotificationSoundPreset { get; set; }
    public bool SubagentNotices { get; set; }
    public bool FieldWorkspace { get; set; }
    public bool FieldTask { get; set; }
    public bool FieldStatus { get; set; }
    public bool FieldTime { get; set; }
    public bool FieldPath { get; set; }
    public bool WidgetCounts { get; set; }
    public bool StatusRunning { get; set; }
    public bool StatusCompleted { get; set; }
    public bool StatusError { get; set; }
    public bool StatusMaxTokens { get; set; }
    public bool StatusBlocked { get; set; }
    public bool StatusAborted { get; set; }
    public bool StatusInterrupted { get; set; }
    public bool StatusIdle { get; set; }
    public bool StatusUnknown { get; set; }
    public bool StatusDisposed { get; set; }
    public bool CompactWidget { get; set; }
    public string WidgetScale { get; set; }
    public bool WidgetTopMost { get; set; }
    public bool CompactWorkspaceList { get; set; }
    public bool InlineTaskTime { get; set; }
    public bool ThemeDark { get; set; }
    public bool ShowLimits { get; set; }
    public List<string> HiddenLimitProviders { get; set; }
    public int WidgetX { get; set; }
    public int WidgetY { get; set; }
    public int NoticeX { get; set; }
    public int NoticeY { get; set; }

    public Settings()
    {
        ShowWidget = true;
        ShowNotifications = true;
        NotificationSoundPreset = NotificationSounds.DefaultId;
        FieldWorkspace = true;
        FieldTask = true;
        FieldStatus = true;
        FieldTime = true;
        FieldPath = true;
        WidgetCounts = true;
        StatusRunning = true;
        StatusCompleted = true;
        StatusError = true;
        StatusMaxTokens = true;
        StatusBlocked = true;
        StatusAborted = true;
        StatusInterrupted = true;
        StatusIdle = false;
        StatusUnknown = true;
        StatusDisposed = true;
        CompactWidget = true;
        WidgetScale = "medium";
        WidgetTopMost = false;
        CompactWorkspaceList = false;
        InlineTaskTime = false;
        ThemeDark = true;
        ShowLimits = false;
        HiddenLimitProviders = new List<string>();
    }
}

public sealed class WidgetRow
{
    public string Id;
    public string Workspace;
    public string Title;
    public string Path;
    public string Status;
    public string StatusText;
    public string TaskSummary;
    public string TimeText;
    public int RunningCount;
    public int CompletedCount;
    public bool Registered;
    public string SessionId;
}

public static class Geometry
{
    public static Rectangle ClampToVisible(Rectangle rectangle, Rectangle available)
    {
        int width = Math.Min(rectangle.Width, available.Width);
        int height = Math.Min(rectangle.Height, available.Height);
        int left = Math.Max(available.Left, Math.Min(rectangle.Left, available.Right - width));
        int top = Math.Max(available.Top, Math.Min(rectangle.Top, available.Bottom - height));
        return new Rectangle(left, top, width, height);
    }

    public static Rectangle ClampToVisible(Rectangle rectangle)
    {
        return ClampToVisible(rectangle, Screen.FromRectangle(rectangle).WorkingArea);
    }
}

public static class SettingsApplier
{
    public static void ApplyLocation(Form form, int x, int y)
    {
        Rectangle rectangle = Geometry.ClampToVisible(new Rectangle(x, y, form.Width, form.Height));
        form.StartPosition = FormStartPosition.Manual;
        form.Location = rectangle.Location;
        form.Size = rectangle.Size;
    }
}

public sealed class SettingsForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    private const int RowInset = 10;
    private const int RowHeight = 20;
    private readonly Settings value;
    private readonly Action<Settings> changed;
    private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
    private readonly Dictionary<Control, int> pages = new Dictionary<Control, int>();
    private readonly ComboBox soundPreset = new ComboBox();
    private readonly ComboBox widgetScale = new ComboBox();
    private Panel contentPanel;
    private bool syncing;
    private int page;

    public Button SaveButton { get; private set; }

    public SettingsForm(Settings current, Action<Settings> onChanged, List<Dictionary<string, object>> providers,
        Action<string> previewSound = null)
    {
        value = current;
        changed = onChanged;
        Text = "Настройки";
        FormBorderStyle = FormBorderStyle.None;
        SuspendLayout();
        ClientSize = new Size(398, 329);
        BackColor = Graphite.Base;
        ForeColor = Graphite.Primary;
        KeyPreview = true;
        KeyDown += delegate(object sender, KeyEventArgs args) { if (args.KeyCode == Keys.Escape) Close(); };
        Panel titleBar = new Panel();
        titleBar.BackColor = Graphite.Card; titleBar.SetBounds(0, 0, 398, 32);
        titleBar.MouseDown += DragCaption;
        Label title = new Label(); title.Text = "☷  Настройки"; title.ForeColor = Graphite.Primary; title.AutoSize = true; title.Location = new Point(10, 8); title.MouseDown += DragCaption;
        titleBar.Controls.Add(title); Controls.Add(titleBar);
        Panel tabs = new Panel(); tabs.BackColor = Graphite.Base; tabs.SetBounds(8, 36, 374, 28); tabs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        string[] tabNames = new string[] { "Общие", "Карточки", "Состояния", "Лимиты" };
        for (int tabIndex = 0; tabIndex < tabNames.Length; tabIndex++)
        {
            Button tab = new Button(); int selected = tabIndex;
            tab.Text = tabNames[tabIndex]; tab.AccessibleName = tabNames[tabIndex]; tab.SetBounds(tabIndex * 92, 1, 88, 25);
            tab.FlatStyle = FlatStyle.Flat; tab.FlatAppearance.BorderColor = Graphite.Border; tab.BackColor = tabIndex == 0 ? Graphite.Card : Graphite.Base; tab.ForeColor = Graphite.Primary;
            tab.Click += delegate { ShowPage(selected); };
            tabs.Controls.Add(tab);
        }
        Controls.Add(tabs);
        tabs.SizeChanged += delegate
        {
            int tabWidth = Math.Max(1, tabs.ClientSize.Width / Math.Max(1, tabs.Controls.Count));
            for (int index = 0; index < tabs.Controls.Count; index++) tabs.Controls[index].SetBounds(index * tabWidth, 1, tabWidth, 25);
        };
        int top = 72;
        page = 0;
        Add("show-widget", "Показывать виджет", value.ShowWidget, ref top);
        Add("show-notices", "Показывать уведомления", value.ShowNotifications, ref top);
        Add("notice-sound", "Звук уведомлений", value.NotificationSound, ref top);
        soundPreset.Name = "notification-sound-preset";
        soundPreset.AccessibleName = "Сигнал уведомления";
        soundPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        soundPreset.FlatStyle = FlatStyle.Flat;
        soundPreset.BackColor = Graphite.Card;
        soundPreset.ForeColor = Graphite.Primary;
        soundPreset.DrawMode = DrawMode.OwnerDrawFixed;
        soundPreset.DrawItem += PaintSoundPreset;
        soundPreset.DisplayMember = "Value";
        soundPreset.Items.AddRange(NotificationSounds.Presets.Cast<object>().ToArray());
        soundPreset.SelectedIndex = NotificationSounds.IndexOf(value.NotificationSoundPreset);
        soundPreset.SetBounds(RowInset, top, 199, 23);
        soundPreset.SelectedIndexChanged += delegate { Save(); };
        Controls.Add(soundPreset);
        pages.Add(soundPreset, page);
        Button preview = new Button { Text = "Прослушать звук", FlatStyle = FlatStyle.Flat, Enabled = previewSound != null,
            ForeColor = Graphite.Primary, BackColor = Graphite.Card };
        preview.FlatAppearance.BorderColor = Graphite.Border;
        preview.SetBounds(221, top, 132, 23);
        preview.Click += delegate { if (previewSound != null) previewSound(SelectedSound()); };
        Controls.Add(preview);
        pages.Add(preview, page);
        top += 26;
        widgetScale.Name = "widget-scale";
        widgetScale.AccessibleName = "Масштаб виджета";
        widgetScale.DropDownStyle = ComboBoxStyle.DropDownList;
        widgetScale.FlatStyle = FlatStyle.Flat;
        widgetScale.BackColor = Graphite.Card;
        widgetScale.ForeColor = Graphite.Primary;
        widgetScale.Items.AddRange(new object[] { "Маленький (85%)", "Средний (100%)", "Большой (125%)" });
        widgetScale.SelectedIndex = value.WidgetScale == "small" ? 0 : (value.WidgetScale == "large" ? 2 : 1);
        widgetScale.SetBounds(RowInset, top, 199, 23);
        widgetScale.SelectedIndexChanged += delegate { Save(); };
        Label scaleLabel = new Label { Text = "Масштаб виджета", ForeColor = Graphite.Secondary, BackColor = Graphite.Base };
        scaleLabel.SetBounds(218, top + 3, 130, 18);
        Controls.Add(scaleLabel); pages.Add(scaleLabel, page);
        Controls.Add(widgetScale); pages.Add(widgetScale, page); top += 26;
        Add("subagent-notices", "Уведомления субагентов", value.SubagentNotices, ref top);
        Add("compact-widget", "Компактный вид", value.CompactWidget, ref top);
        Add("widget-topmost", "Поверх остальных окон", value.WidgetTopMost, ref top);
        Add("compact-workspace-list", "Короткий список областей: имя и состояние", value.CompactWorkspaceList, ref top);
        Add("theme-dark", "Тёмная тема", value.ThemeDark, ref top);
        Add("show-limits", "Лимиты ИИ в окне „Задачи“", value.ShowLimits, ref top);
        page = 1; top = 72;
        Add("field-workspace", "Рабочая область", value.FieldWorkspace, ref top);
        Add("field-task", "Задача", value.FieldTask, ref top);
        Add("field-status", "Результат", value.FieldStatus, ref top);
        Add("field-time", "Время", value.FieldTime, ref top);
        Add("field-path", "Путь", value.FieldPath, ref top);
        Add("widget-counts", "Счётчики", value.WidgetCounts, ref top);
        Add("inline-task-time", "Время рядом с задачей", value.InlineTaskTime, ref top);
        page = 2; top = 72;
        Add("status-running", "Выполняется", value.StatusRunning, ref top);
        Add("status-completed", "Завершено", value.StatusCompleted, ref top);
        Add("status-error", "Ошибка", value.StatusError, ref top);
        Add("status-max-tokens", "Прервано: лимит токенов", value.StatusMaxTokens, ref top);
        Add("status-blocked", "Заблокировано", value.StatusBlocked, ref top);
        Add("status-aborted", "Отменено", value.StatusAborted, ref top);
        Add("status-interrupted", "Прервано", value.StatusInterrupted, ref top);
        Add("status-idle", "Без задач", value.StatusIdle, ref top);
        Add("status-unknown", "Состояние неизвестно", value.StatusUnknown, ref top);
        Add("status-disposed", "Сессия закрыта", value.StatusDisposed, ref top);
        page = 3; top = 72;
        AddCaption("Показывать лимиты", ref top);
        foreach (Dictionary<string, object> provider in providers ?? new List<Dictionary<string, object>>())
        {
            string id = Convert.ToString(provider.ContainsKey("id") ? provider["id"] : "");
            if (String.IsNullOrEmpty(id) || checks.ContainsKey("limit-" + id)) continue;
            string label = Convert.ToString(provider.ContainsKey("label") ? provider["label"] : id);
            Add("limit-" + id, label, !value.HiddenLimitProviders.Contains(id), ref top);
        }
        SaveButton = new Button();
        SaveButton.Text = "Готово";
        SaveButton.Top = 290;
        SaveButton.Left = 292;
        SaveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        SaveButton.FlatStyle = FlatStyle.Flat;
        SaveButton.BackColor = Graphite.Card;
        SaveButton.ForeColor = Graphite.Primary;
        SaveButton.Click += delegate { Close(); };
        Controls.Add(SaveButton);
        Button close = new Button();
        close.Text = "×";
        close.AccessibleName = "Закрыть настройки";
        close.FlatStyle = FlatStyle.Flat;
        close.FlatAppearance.BorderColor = Graphite.Border;
        close.BackColor = Graphite.Card;
        close.ForeColor = Graphite.Primary;
        close.Size = new Size(28, 24);
        close.Left = 362;
        close.Top = 6;
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Click += delegate { Close(); };
        titleBar.Controls.Add(close);
        titleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        MinimumSize = new Size(360, 280);
        AutoScroll = false;
        contentPanel = new Panel();
        contentPanel.SetBounds(8, 64, 382, 218);
        contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        contentPanel.AutoScroll = true;
        contentPanel.BackColor = Graphite.Card;
        foreach (Control control in new List<Control>(pages.Keys))
        {
            control.Top = Math.Max(8, control.Top - 64);
            Controls.Remove(control);
            contentPanel.Controls.Add(control);
        }
        Controls.Add(contentPanel);
        contentPanel.SizeChanged += delegate { FitRows(); };
        FitRows();
        ShowPage(0);
        ResumeLayout(true);
        ApplyOwnPalette(this);
    }

    public CheckBox Check(string key)
    {
        return checks[key];
    }

    public void UpdateLimitProviders(List<Dictionary<string, object>> providers)
    {
        int previousPage = page;
        Point scroll = contentPanel.AutoScrollPosition;
        page = 3;
        HashSet<string> wanted = new HashSet<string>((providers ?? new List<Dictionary<string, object>>()).Select(provider => Convert.ToString(provider.ContainsKey("id") ? provider["id"] : "")).Where(id => !String.IsNullOrEmpty(id)));
        foreach (string key in checks.Keys.Where(key => key.StartsWith("limit-", StringComparison.Ordinal) && !wanted.Contains(key.Substring(6))).ToList())
        {
            Control control = checks[key]; contentPanel.Controls.Remove(control); pages.Remove(control); checks.Remove(key); control.Dispose();
        }
        int top = pages.Where(item => item.Value == 3).Select(item => item.Key.Bottom + 2).DefaultIfEmpty(8).Max();
        foreach (Dictionary<string, object> provider in providers ?? new List<Dictionary<string, object>>())
        {
            string id = Convert.ToString(provider.ContainsKey("id") ? provider["id"] : "");
            if (String.IsNullOrEmpty(id)) continue;
            string label = Convert.ToString(provider.ContainsKey("label") ? provider["label"] : id);
            CheckBox existing; if (checks.TryGetValue("limit-" + id, out existing)) { existing.Text = label; continue; }
            Add("limit-" + id, label, !value.HiddenLimitProviders.Contains(id), ref top);
        }
        page = previousPage;
        FitRows();
        ShowPage(previousPage);
        contentPanel.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
    }

    public void RefreshTheme()
    {
        syncing = true;
        checks["theme-dark"].Checked = value.ThemeDark;
        checks["show-limits"].Checked = value.ShowLimits;
        checks["compact-widget"].Checked = value.CompactWidget;
        syncing = false;
        ApplyOwnPalette(this);
    }

    private static void ApplyOwnPalette(Control control)
    {
        Panel panel = control as Panel;
        if (panel != null) panel.BackColor = Graphite.Card;
        Label label = control as Label;
        if (label != null) label.ForeColor = Graphite.Primary;
        Button button = control as Button;
        if (button != null) { button.BackColor = Graphite.Card; button.ForeColor = Graphite.Primary; button.FlatAppearance.BorderColor = Graphite.Border; }
        CheckBox check = control as CheckBox;
        if (check != null) { check.BackColor = Graphite.Card; check.ForeColor = Graphite.Primary; }
        ComboBox combo = control as ComboBox;
        if (combo != null) { combo.BackColor = Graphite.Card; combo.ForeColor = Graphite.Primary; }
        foreach (Control child in control.Controls) ApplyOwnPalette(child);
    }

    private void DragCaption(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }

    private void Add(string key, string text, bool selected, ref int top)
    {
        CheckBox check = new CheckBox();
        check.Text = text;
        check.Checked = selected;
        check.SetBounds(RowInset, top, 1, RowHeight);
        check.TextAlign = ContentAlignment.MiddleLeft;
        check.CheckAlign = ContentAlignment.MiddleRight;
        check.FlatStyle = FlatStyle.Flat;
        check.ForeColor = Graphite.Primary;
        check.BackColor = Graphite.Base;
        check.CheckedChanged += delegate
        {
            Save();
        };
        checks.Add(key, check);
        pages[check] = page;
        if (contentPanel == null) Controls.Add(check); else contentPanel.Controls.Add(check);
        top += RowHeight;
    }

    private void AddCaption(string text, ref int top)
    {
        Label caption = new Label();
        caption.Text = text;
        caption.ForeColor = Graphite.Running;
        caption.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
        caption.Left = 10;
        caption.Top = top;
        caption.Width = 280;
        caption.Height = 20;
        if (contentPanel == null) Controls.Add(caption); else contentPanel.Controls.Add(caption);
        pages[caption] = page;
        top += 22;
    }

    private void FitRows()
    {
        // Reserve the vertical scrollbar gutter before layout can introduce a horizontal scrollbar.
        int width = Math.Max(1, contentPanel.Width - RowInset * 2 - SystemInformation.VerticalScrollBarWidth);
        foreach (CheckBox row in checks.Values) row.Width = width;
    }

    private void ShowPage(int selected)
    {
        page = selected;
        contentPanel.AutoScrollPosition = Point.Empty;
        foreach (KeyValuePair<Control, int> item in pages) item.Key.Visible = item.Value == selected;
    }

    private void Save()
    {
        if (syncing) return;
        bool wasCompactWorkspaceList = value.CompactWorkspaceList;
        value.ShowWidget = checks["show-widget"].Checked;
        value.ShowNotifications = checks["show-notices"].Checked;
        value.NotificationSound = checks["notice-sound"].Checked;
        value.NotificationSoundPreset = SelectedSound();
        value.WidgetScale = widgetScale.SelectedIndex == 0 ? "small" : (widgetScale.SelectedIndex == 2 ? "large" : "medium");
        value.SubagentNotices = checks["subagent-notices"].Checked;
        value.FieldWorkspace = checks["field-workspace"].Checked;
        value.FieldTask = checks["field-task"].Checked;
        value.FieldStatus = checks["field-status"].Checked;
        value.FieldTime = checks["field-time"].Checked;
        value.FieldPath = checks["field-path"].Checked;
        value.WidgetCounts = checks["widget-counts"].Checked;
        value.CompactWidget = checks["compact-widget"].Checked;
        value.WidgetTopMost = checks["widget-topmost"].Checked;
        value.CompactWorkspaceList = checks["compact-workspace-list"].Checked;
        if (!wasCompactWorkspaceList && value.CompactWorkspaceList)
        {
            value.CompactWidget = false;
            syncing = true; checks["compact-widget"].Checked = false; syncing = false;
        }
        value.InlineTaskTime = checks["inline-task-time"].Checked;
        value.ThemeDark = checks["theme-dark"].Checked;
        value.ShowLimits = checks["show-limits"].Checked;
        value.HiddenLimitProviders = value.HiddenLimitProviders.Where(id => !checks.ContainsKey("limit-" + id))
            .Concat(checks.Where(item => item.Key.StartsWith("limit-", StringComparison.Ordinal) && !item.Value.Checked)
            .Select(item => item.Key.Substring("limit-".Length))).Distinct().Take(32).ToList();
        value.StatusRunning = checks["status-running"].Checked;
        value.StatusCompleted = checks["status-completed"].Checked;
        value.StatusError = checks["status-error"].Checked;
        value.StatusMaxTokens = checks["status-max-tokens"].Checked;
        value.StatusBlocked = checks["status-blocked"].Checked;
        value.StatusAborted = checks["status-aborted"].Checked;
        value.StatusInterrupted = checks["status-interrupted"].Checked;
        value.StatusIdle = checks["status-idle"].Checked;
        value.StatusUnknown = checks["status-unknown"].Checked;
        value.StatusDisposed = checks["status-disposed"].Checked;
        changed(value);
    }

    private string SelectedSound()
    {
        return soundPreset.SelectedIndex < 0 ? NotificationSounds.DefaultId : NotificationSounds.Presets[soundPreset.SelectedIndex].Key;
    }

    private void PaintSoundPreset(object sender, DrawItemEventArgs args)
    {
        bool highlighted = (args.State & (DrawItemState.Selected | DrawItemState.ComboBoxEdit)) == DrawItemState.Selected;
        using (Brush fill = new SolidBrush(highlighted ? Graphite.Border : Graphite.Card))
            args.Graphics.FillRectangle(fill, args.Bounds);
        if (args.Index < 0) return;
        Rectangle text = Rectangle.Inflate(args.Bounds, -4, 0);
        TextRenderer.DrawText(args.Graphics, NotificationSounds.Presets[args.Index].Value, args.Font, text,
            Graphite.Primary, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }
}

public sealed class NotificationWindow : Form
{
    private bool userMoving;
    public Action<int, int> SavePosition;

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0231)
        {
            userMoving = true;
        }
        if (message.Msg == 0x0232 && userMoving)
        {
            userMoving = false;
            if (SavePosition != null)
            {
                SavePosition(Left, Top);
            }
        }
        base.WndProc(ref message);
    }
}

public sealed class TaskGlyph : Control
{
    public TaskGlyph()
    {
        DoubleBuffered = true;
        BackColor = Graphite.Card;
    }
    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Icon icon = TaskIcon.Small;
        int size = Math.Min(Math.Min(Width, Height), icon.Width);
        if (size == 0) return;
        eventArgs.Graphics.DrawIcon(icon, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
    }
}

public sealed class LimitsPanel : Control
{
    private const int FirstRowTop = 1;
    private const int RowHeight = 18;
    private const int ProviderTextLeft = 27;
    private static readonly CultureInfo AmountCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Image[] DeepSeekBadges = LoadProviderBadges("deepseek");
    private static readonly Image[] CodexBadges = LoadProviderBadges("codex");
    private static readonly Image[] ZaiBadges = LoadProviderBadges("zai");
    private static readonly Image[] GenericBadges = LoadProviderBadges("generic");
    private const TextFormatFlags CompactText = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    private List<Dictionary<string, object>> providers = new List<Dictionary<string, object>>();
    private DateTime receivedAt = DateTime.MinValue;
    private float zoom = 1F;
    private int Px(int value) { return (int)Math.Round(value * zoom); }
    public float WidgetZoom { get { return zoom; } set { zoom = value < .9F ? .85F : (value > 1.1F ? 1.25F : 1F); Invalidate(); } }
    private readonly ToolTip hints = new ToolTip();

    public LimitsPanel()
    {
        DoubleBuffered = true;
        Height = 42;
        BackColor = Graphite.Base;
        AccessibleName = "Лимиты ИИ";
    }

    public void UpdateLimits(List<Dictionary<string, object>> value, DateTime received)
    {
        providers = (value ?? new List<Dictionary<string, object>>()).OrderByDescending(ProviderAvailable).ToList();
        receivedAt = received;
        int lines = 0;
        foreach (Dictionary<string, object> provider in providers)
            lines += HasCurrentData(provider) ? ValueList(provider, "windows").Count + MetricText(provider).Count : 1;
        Height = (int)Math.Round(Math.Max(22, FirstRowTop + lines * RowHeight + Math.Max(0, providers.Count - 1) * 3) * zoom);
        AccessibleDescription = String.Join(", ", providers.Select(ProviderName).ToArray());
        Invalidate();
    }

    public int DesiredHeight { get { return Height; } }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.Clear(Graphite.Base);
        using (Font detail = new Font("Segoe UI", 7.5F * zoom, FontStyle.Regular))
        {
            if (providers.Count == 0)
            {
                TextRenderer.DrawText(graphics, "Нет выбранных служб", detail, new Point(Px(8), Px(6)), Graphite.Secondary);
                return;
            }
            int width = (int)Math.Round((Parent == null ? ClientSize.Width : Math.Min(ClientSize.Width, Parent.ClientSize.Width)) / zoom);
            int dataLeft = Math.Min(117, Math.Max(ProviderTextLeft + 21, width / 2));
            int dataWidth = Math.Max(1, width - dataLeft - 6);
            int top = FirstRowTop;
            bool firstProvider = true;
            foreach (Dictionary<string, object> provider in providers)
            {
                if (!firstProvider)
                {
                    using (Pen divider = new Pen(Graphite.Border)) graphics.DrawLine(divider, Px(6), Px(top + 1), Px(Math.Max(6, width - 6)), Px(top + 1));
                    top += 3;
                }
                firstProvider = false;
                Image badge = ProviderBadge(Field(provider, "id"), ProviderName(provider));
                if (badge != null) graphics.DrawImage(badge, new Rectangle(Px(6), Px(top), Px(18), Px(18)));
                TextRenderer.DrawText(graphics, ProviderName(provider), detail,
                    new Rectangle(Px(ProviderTextLeft), Px(top), Px(dataLeft - ProviderTextLeft - 5), Px(RowHeight)),
                    Graphite.Primary, CompactText | TextFormatFlags.VerticalCenter);
                if (!HasCurrentData(provider))
                {
                    TextRenderer.DrawText(graphics, DeepSeekReason(provider), detail, new Rectangle(Px(dataLeft), Px(top), Px(dataWidth), Px(RowHeight)),
                        Graphite.Secondary, CompactText | TextFormatFlags.VerticalCenter);
                    top += RowHeight;
                    continue;
                }
                foreach (Dictionary<string, object> window in ValueList(provider, "windows"))
                {
                    double percent = ToDouble(window["percent"]);
                    string period = Field(window, "label");
                    if (period == "7 дней") period = "7 дн.";
                    string reset = HasNumber(window, "resetAt") ? Countdown(ToLong(window, "resetAt"), true) : "";
                    string caption = period + " · " + Math.Round(percent) + "%" + (reset.Length == 0 ? "" : " · " + reset);
                    TextRenderer.DrawText(graphics, caption, detail,
                        new Rectangle(Px(dataLeft), Px(top), Px(dataWidth), Px(RowHeight - 4)), Graphite.Primary, CompactText);
                    Rectangle track = new Rectangle(Px(dataLeft), Px(top + RowHeight - 4), Px(Math.Min(70, dataWidth)), Px(3));
                    using (Brush trackBrush = new SolidBrush(Graphite.Border)) graphics.FillRectangle(trackBrush, track);
                    Color tier = percent < 25 ? Graphite.QuotaLow : (percent < 60 ? Graphite.QuotaMedium : Graphite.QuotaHigh);
                    using (Brush fill = new SolidBrush(tier)) graphics.FillRectangle(fill, track.X, track.Y, (int)(track.Width * percent / 100.0), track.Height);
                    top += RowHeight;
                }
                foreach (string metric in MetricText(provider, true))
                {
                    TextRenderer.DrawText(graphics, metric, detail, new Rectangle(Px(dataLeft), Px(top), Px(dataWidth), Px(RowHeight)),
                        Graphite.Primary, CompactText | TextFormatFlags.VerticalCenter);
                    top += RowHeight;
                }
            }
        }
    }

    internal static string Countdown(long resetAt, bool compact = false)
    {
        long seconds = resetAt - (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        if (seconds <= 0) return compact ? "сброс" : "сброс доступен";
        if (seconds < 60) return "<1 мин";
        if (seconds < 3600) return (seconds / 60) + " мин";
        if (compact && seconds >= 86400) return (seconds / 86400) + " д " + ((seconds % 86400) / 3600) + " ч";
        return (seconds / 3600) + " ч " + ((seconds % 3600) / 60) + " мин";
    }

    private static string Field(Dictionary<string, object> value, string key)
    {
        object item; return value.TryGetValue(key, out item) ? Convert.ToString(item) : String.Empty;
    }
    private static string NestedField(Dictionary<string, object> value, string key, string nestedKey)
    {
        object item; Dictionary<string, object> nested;
        return value.TryGetValue(key, out item) && (nested = item as Dictionary<string, object>) != null ? Field(nested, nestedKey) : String.Empty;
    }
    private static string ProviderName(Dictionary<string, object> provider)
    {
        string label = Field(provider, "label");
        if (label == "OpenAI Codex") return "Codex";
        return label.Length == 0 ? Field(provider, "id") : label;
    }
    private static string DeepSeekReason(Dictionary<string, object> provider)
    {
        string reason = Field(provider, "unavailableReason");
        if (reason == "MISSING_CONFIG") return "Не настроено";
        if (reason == "MISSING_KEY") return "Нет ключа";
        if (reason == "UNRECOGNIZED_ORIGIN") return "Неверный адрес";
        if (reason == "CREDENTIALS_REJECTED") return "Ключ отклонён";
        if (reason == "NO_RESPONSE") return "Нет ответа";
        return "Нет данных";
    }
    private static Image[] LoadProviderBadges(string brand)
    {
        return new[] { LoadProviderBadge(brand + "-light.png"), LoadProviderBadge(brand + "-dark.png") };
    }
    private static Image LoadProviderBadge(string filename)
    {
        using (Stream stream = typeof(LimitsPanel).Assembly.GetManifestResourceStream("Providers." + filename))
        {
            if (stream == null) return null;
            using (Image source = Image.FromStream(stream)) return new Bitmap(source);
        }
    }
    internal static Image ProviderBadge(string id, string label)
    {
        string route = id.Trim().ToLowerInvariant();
        string name = label.Trim().ToLowerInvariant();
        int theme = Graphite.IsDark ? 1 : 0;
        if (route == "deepseek" || route.StartsWith("deepseek-", StringComparison.Ordinal)
            || name == "deepseek" || name == "deepseek account") return DeepSeekBadges[theme];
        if (route == "openai-codex" || name == "openai codex" || name == "codex") return CodexBadges[theme];
        if (route == "zai" || route.StartsWith("zai-", StringComparison.Ordinal)
            || name == "z.ai" || name == "z.ai coding") return ZaiBadges[theme];
        return GenericBadges[theme];
    }
    private static string DisplayAmount(string balance, bool compact)
    {
        decimal number;
        if (!compact || !Decimal.TryParse(balance, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out number)) return balance;
        string format = number != 0 && Math.Abs(number) < 0.01m ? "0.############" : "#,0.##";
        return number.ToString(format, AmountCulture);
    }
    private static List<string> MetricText(Dictionary<string, object> provider, bool compact = false)
    {
        List<string> values = new List<string>();
        foreach (Dictionary<string, object> amount in ValueList(provider, "amounts"))
        {
            string balance = Field(amount, "balance"); string currency = Field(amount, "currency");
            if (balance.Length > 0 && currency.Length > 0)
                values.Add((Field(amount, "kind") == "bonus" ? "Бонус " : "Баланс ") + DisplayAmount(balance, compact) + " " + currency);
        }
        string unlimited = NestedField(provider, "credits", "unlimited");
        string credits = NestedField(provider, "credits", "balance");
        if (unlimited == "True") values.Add("Кредиты без лимита");
        else if (credits.Length > 0) values.Add("Кредиты " + DisplayAmount(credits, compact));
        string remaining = NestedField(provider, "individualLimit", "remaining");
        if (remaining.Length > 0) values.Add((compact ? "Бюджет " : "Остаток лимита расходов ") + DisplayAmount(remaining, compact));
        return values;
    }
    private static bool ProviderAvailable(Dictionary<string, object> provider)
    {
        return String.Equals(Field(provider, "state"), "ok", StringComparison.Ordinal)
            && (ValueList(provider, "windows").Count > 0 || MetricText(provider).Count > 0);
    }
    private bool HasCurrentData(Dictionary<string, object> provider)
    {
        return receivedAt != DateTime.MinValue && (DateTime.UtcNow - receivedAt).TotalMinutes <= 10 && ProviderAvailable(provider);
    }
    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        int y = (int)Math.Floor(eventArgs.Y / zoom);
        int visualTop = FirstRowTop; bool firstProvider = true;
        foreach (Dictionary<string, object> provider in providers)
        {
            if (!firstProvider && y >= visualTop && y < visualTop + 3) { hints.SetToolTip(this, String.Empty); return; }
            if (!firstProvider) visualTop += 3;
            firstProvider = false;
            if (!HasCurrentData(provider))
            {
                if (y >= visualTop && y < visualTop + RowHeight)
                {
                    hints.SetToolTip(this, ProviderName(provider) + " · " + DeepSeekReason(provider));
                    return;
                }
                visualTop += RowHeight;
                continue;
            }
            List<Dictionary<string, object>> windows = ValueList(provider, "windows");
            List<string> metrics = MetricText(provider);
            int count = Math.Max(1, windows.Count + metrics.Count);
            if (y >= visualTop && y < visualTop + count * RowHeight)
            {
                int local = (y - visualTop) / RowHeight;
                string detail;
                if (local < windows.Count)
                {
                    Dictionary<string, object> window = windows[local];
                    string reset = HasNumber(window, "resetAt") ? Countdown(ToLong(window, "resetAt")) : "срок неизвестен";
                    detail = Field(window, "label") + " " + Math.Round(ToDouble(window["percent"])) + "% · " + reset;
                }
                else if (local - windows.Count < metrics.Count) detail = metrics[local - windows.Count];
                else detail = "Данные недоступны";
                hints.SetToolTip(this, ProviderName(provider) + " · " + detail);
                return;
            }
            visualTop += count * RowHeight;
        }
        hints.SetToolTip(this, String.Empty);
    }
    private static long ToLong(Dictionary<string, object> value, string key)
    {
        object item; long result; return value.TryGetValue(key, out item) && Int64.TryParse(Convert.ToString(item), out result) ? result : 0;
    }
    private static double ToDouble(object value)
    {
        double result; return Double.TryParse(Convert.ToString(value), out result) && !Double.IsNaN(result) && !Double.IsInfinity(result) ? result : 0;
    }
    private static bool HasNumber(Dictionary<string, object> value, string key)
    {
        object item; long result; return value.TryGetValue(key, out item) && Int64.TryParse(Convert.ToString(item), out result);
    }
    private static List<Dictionary<string, object>> ValueList(Dictionary<string, object> value, string key)
    {
        object item; IEnumerable source; List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
        if (!value.TryGetValue(key, out item) || (source = item as IEnumerable) == null) return result;
        foreach (object entry in source) { Dictionary<string, object> dictionary = entry as Dictionary<string, object>; if (dictionary != null) result.Add(dictionary); }
        return result;
    }
}

public sealed class DshNotifyGui : Form
{
    private const string DshOpenUrl = "dsh://open";
    private const int WidgetWidth = 248;
    private const int CompactHeight = 115;
    private const int ExpandedHeight = 320;
    private const int ResizeGripHeight = 6;
    private const int DefaultQuotaHeight = 120;
    private const int NoticeStackWidth = 352;
    private const int NoticeStackHeightLimit = 420;
    private const int NonClientLeftButtonDown = 0x00A1;
    private readonly Action<string> playNoticeSound;
    private const int CaptionHit = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private readonly StreamWriter output;
    private readonly Action<string> outputCallback;
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly NotificationWindow stack = new NotificationWindow();
    private readonly ItemScrollList cards = new ItemScrollList();
    private readonly ItemScrollList rows = new ItemScrollList();
    private readonly CompactWidgetControl compact;
    private readonly LimitsPanel limits = new LimitsPanel();
    private readonly Panel limitsHost = new Panel();
    private readonly Panel resizeGrip = new Panel();
    private int expandedUserHeight;
    private int resizeStartHeight;
    private int resizeStartY;
    private bool resizeStartedCompact;
    private readonly Button compactToggle = new Button();
    private readonly Dictionary<string, WidgetRow> widgetRows = new Dictionary<string, WidgetRow>();
    private readonly Dictionary<string, Dictionary<string, object>> notices =
        new Dictionary<string, Dictionary<string, object>>();
    private readonly List<string> noticeOrder = new List<string>();
    private readonly List<SettingsForm> settingsForms = new List<SettingsForm>();
    private readonly List<Button> headerButtons = new List<Button>();
    private FlowLayoutPanel headerActions;
    private float appliedWidgetScale = 1F;
    private Panel headerPanel;
    private Label headerTitle;
    private TaskGlyph taskGlyph;
    private Panel noticeHeader;
    private Label noticeHeaderTitle;
    private Button noticeHeaderClose;
    private string lastWidgetSnapshot;
    private static readonly Font CardTitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
    private static readonly Font CardDetailFont = new Font("Segoe UI", 8.25F, FontStyle.Regular);
    private static readonly Font NoticeTaskFont = new Font("Segoe UI", 9F, FontStyle.Regular);
    private bool widgetUserMoving;
    private bool noticesManuallyHidden;
    private bool isShuttingDown;
    private bool lastShowWidget = true;
    private bool lastShowNotifications = true;
    private readonly bool demoMode;
    private List<Dictionary<string, object>> limitProviders = new List<Dictionary<string, object>>();
    private List<Dictionary<string, object>> availableLimitProviders = new List<Dictionary<string, object>>();
    private DateTime limitsReceivedAt = DateTime.MinValue;

    private float WidgetScaleFactor { get { return Settings != null && Settings.WidgetScale == "small" ? .85F : (Settings != null && Settings.WidgetScale == "large" ? 1.25F : 1F); } }
    private int Px(int value) { return (int)Math.Round(value * WidgetScaleFactor); }
    private Font WidgetFont(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size * WidgetScaleFactor, style); }
    private void ScaleWidgetFonts(Control control)
    {
        float baseSize;
        if (control.Tag is float) baseSize = (float)control.Tag;
        else
        {
            baseSize = Math.Abs(control.Font.SizeInPoints - Font.SizeInPoints) < .01F ? control.Font.SizeInPoints / appliedWidgetScale : control.Font.SizeInPoints;
            control.Tag = baseSize;
        }
        control.Font = new Font(control.Font.FontFamily, baseSize * WidgetScaleFactor, control.Font.Style);
        foreach (Control child in control.Controls) ScaleWidgetFonts(child);
    }
    private void ApplyWidgetChrome()
    {
        headerPanel.Height = Px(30);
        taskGlyph.Location = new Point(Px(8), Px(6)); taskGlyph.Size = new Size(Px(18), Px(18));
        headerTitle.Location = new Point(Px(30), Px(7));
        headerActions.Padding = new Padding(Px(5), Px(3), Px(5), Px(3));
        foreach (Control child in headerActions.Controls) { Button button = child as Button; if (button != null) button.Size = new Size(Px(26), Px(24)); }
        headerActions.Width = headerActions.Padding.Horizontal + headerActions.Controls.Cast<Control>().Sum(button => button.Width + button.Margin.Horizontal);
        resizeGrip.Height = Px(ResizeGripHeight);
        ScaleWidgetFonts(this); appliedWidgetScale = WidgetScaleFactor;
    }

    public Settings Settings { get; private set; }
    internal bool DemoSettingsAfterApply { get; set; }

    public DshNotifyGui(bool demo)
        : this(demo, null)
    {
    }

    public DshNotifyGui(bool demo, Action<string> callback)
        : this(demo, callback, NotificationSounds.Play)
    {
    }

    internal DshNotifyGui(bool demo, Action<string> callback, Action<string> sound)
    {
        demoMode = demo;
        playNoticeSound = sound;
        outputCallback = callback;
        if (outputCallback == null)
        {
            output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.AutoFlush = true;
        }
        Settings = new Settings();
        Text = (demo ? "[ДЕМО] " : "") + "Задачи";
        Icon = TaskIcon.Window;
        FormBorderStyle = FormBorderStyle.None;
        ResizeRedraw = true;
        BackColor = Graphite.Base;
        ClientSize = new Size(WidgetWidth, CompactHeight);
        Font = new Font("Segoe UI", 9F);
        Panel header = new Panel();
        headerPanel = header;
        header.Height = 30;
        header.Dock = DockStyle.Top;
        header.BackColor = Graphite.Card;
        header.MouseDown += DragWidget;
        headerActions = new FlowLayoutPanel();
        headerActions.Dock = DockStyle.Right;
        headerActions.Padding = new Padding(5, 3, 5, 3);
        headerActions.WrapContents = false;
        TaskGlyph glyph = new TaskGlyph();
        taskGlyph = glyph;
        glyph.Location = new Point(8, 6);
        glyph.Size = new Size(18, 18);
        glyph.MouseDown += DragWidget;
        headerTitle = new Label();
        headerTitle.Text = demo ? "Задачи · демо" : "Задачи";
        headerTitle.ForeColor = Graphite.Primary;
        headerTitle.Location = new Point(30, 7);
        headerTitle.AutoSize = true;
        headerTitle.MouseDown += DragWidget;
        Button settingsButton = new Button();
        settingsButton.Text = "⚙";
        settingsButton.AccessibleName = "Настройки";
        settingsButton.Size = new Size(26, 24);
        StyleHeaderButton(settingsButton);
        headerButtons.Add(settingsButton);
        settingsButton.Click += delegate
        {
            ShowSettings();
        };
        Button themeButton = new Button();
        themeButton.Text = "☾";
        themeButton.AccessibleName = "Сменить тему";
        themeButton.Size = new Size(26, 24);
        StyleHeaderButton(themeButton);
        headerButtons.Add(themeButton);
        themeButton.Click += delegate { Settings.ThemeDark = !Settings.ThemeDark; UserChanged(Settings); };
        rows.Dock = DockStyle.Fill;
        rows.Padding = new Padding(4);
        rows.BackColor = Graphite.Base;
        rows.AccessibleName = "Список рабочих областей";
        Controls.Add(rows);
        limitsHost.Dock = DockStyle.Top;
        limitsHost.AutoScroll = true;
        limitsHost.Visible = false;
        limitsHost.BackColor = Graphite.Base;
        limits.Location = new Point(0, 0);
        limits.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        limitsHost.Controls.Add(limits);
        compact = new CompactWidgetControl();
        compact.Dock = DockStyle.Fill;
        Controls.Add(compact);
        Controls.Add(limitsHost);
        resizeGrip.Dock = DockStyle.Bottom;
        resizeGrip.Height = ResizeGripHeight;
        resizeGrip.Cursor = Cursors.SizeNS;
        resizeGrip.AccessibleName = "Изменить высоту окна";
        resizeGrip.BackColor = Graphite.Base;
        resizeGrip.Paint += delegate(object sender, PaintEventArgs args)
        {
            int center = resizeGrip.Width / 2;
            using (Pen pen = new Pen(Graphite.Secondary))
                args.Graphics.DrawLine(pen, center - 12, 2, center + 12, 2);
        };
        resizeGrip.MouseDown += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            resizeStartHeight = Height;
            resizeStartY = Control.MousePosition.Y;
            resizeStartedCompact = Settings.CompactWidget;
            resizeGrip.Capture = true;
        };
        resizeGrip.MouseMove += delegate(object sender, MouseEventArgs args)
        {
            if (!resizeGrip.Capture || args.Button != MouseButtons.Left || resizeStartedCompact != Settings.CompactWidget) return;
            SetUserWidgetHeight(resizeStartHeight + Control.MousePosition.Y - resizeStartY);
        };
        resizeGrip.MouseUp += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left) resizeGrip.Capture = false;
        };
        Controls.Add(resizeGrip);
        compactToggle.Text = "›";
        compactToggle.Size = new Size(25, 24);
        StyleHeaderButton(compactToggle);
        headerButtons.Add(compactToggle);
        compactToggle.AccessibleName = "Развернуть список";
        compactToggle.Click += delegate { ToggleCompactMode(); };
        Button hideButton = new Button();
        hideButton.Text = "×";
        hideButton.AccessibleName = "Скрыть виджет";
        hideButton.Size = new Size(25, 24);
        StyleHeaderButton(hideButton);
        headerButtons.Add(hideButton);
        hideButton.Click += delegate { Hide(); };
        header.Controls.Add(glyph);
        header.Controls.Add(headerTitle);
        headerActions.Controls.Add(themeButton);
        headerActions.Controls.Add(settingsButton);
        headerActions.Controls.Add(compactToggle);
        headerActions.Controls.Add(hideButton);
        headerActions.Width = headerActions.Padding.Horizontal + headerActions.Controls.Cast<Control>()
            .Sum(button => button.Width + button.Margin.Horizontal);
        header.SizeChanged += delegate
        {
            headerTitle.AutoSize = false;
            headerTitle.Width = Math.Max(1, header.ClientSize.Width - headerActions.Width - headerTitle.Left - 4);
            headerTitle.AutoEllipsis = true;
            headerTitle.Height = 18;
        };
        header.Controls.Add(headerActions);
        Controls.Add(header);
        cards.Dock = DockStyle.Fill;
        cards.Padding = new Padding(6);
        cards.BackColor = Graphite.Base;
        cards.AccessibleName = "Список уведомлений";
        stack.FormBorderStyle = FormBorderStyle.None;
        stack.BackColor = Graphite.Border;
        if (headerPanel != null) headerPanel.BackColor = Graphite.Card;
        if (headerTitle != null) headerTitle.ForeColor = Graphite.Primary;
        foreach (Button button in headerButtons) StyleHeaderButton(button);
        stack.Padding = new Padding(1);
        stack.Controls.Add(cards);
        stack.Controls.Add(MakeNoticeHeader(demo));
        stack.ClientSize = new Size(NoticeStackWidth, 34);
        stack.Text = (demo ? "[ДЕМО] " : "") + "Уведомления";
        stack.Font = Font;
        stack.ShowInTaskbar = false;
        stack.TopMost = true;
        stack.SavePosition = delegate(int x, int y)
        {
            Settings.NoticeX = x;
            Settings.NoticeY = y;
            Send("notices.moved", "x", x, "y", y);
        };
        stack.FormClosing += StackFormClosing;
        tray.Icon = TaskIcon.Small;
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Показать виджет", null, delegate
        {
            ShowWidget();
        });
        menu.Items.Add("Показать уведомления", null, delegate
        {
            ShowNotices();
        });
        menu.Items.Add("Настройки", null, delegate
        {
            ShowSettings();
        });
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    private void DragWidget(object sender, MouseEventArgs eventArgs)
    {
        DragWindow(this, sender, eventArgs);
    }

    private static void DragWindow(Form window, object sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        Point pointer = ((Control)sender).PointToScreen(eventArgs.Location);
        int packedPoint = unchecked((pointer.X & 0xffff) | (pointer.Y << 16));
        ReleaseCapture();
        SendMessage(window.Handle, NonClientLeftButtonDown, new IntPtr(CaptionHit), new IntPtr(packedPoint));
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0231)
        {
            widgetUserMoving = true;
        }
        if (message.Msg == 0x0232 && widgetUserMoving)
        {
            widgetUserMoving = false;
            SaveWidgetPosition();
        }
        base.WndProc(ref message);
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (!isShuttingDown && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(eventArgs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray.Visible = false;
            tray.Dispose();
            if (!stack.IsDisposed)
            {
                stack.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    public void ApplySettings(Settings settings)
    {
        ApplyRemote(settings);
    }

    public void ApplyRemote(Settings settings)
    {
        Settings = settings ?? new Settings();
        if (Settings.WidgetScale != "small" && Settings.WidgetScale != "large") Settings.WidgetScale = "medium";
        TopMost = Settings.WidgetTopMost;
        compact.WidgetZoom = WidgetScaleFactor;
        limits.WidgetZoom = WidgetScaleFactor;
        ApplyWidgetChrome();
        Graphite.ApplyTheme(Settings.ThemeDark);
        ApplyTheme();
        lastShowWidget = Settings.ShowWidget;
        lastShowNotifications = Settings.ShowNotifications;
        SettingsApplier.ApplyLocation(this, Settings.WidgetX, Settings.WidgetY);
        SettingsApplier.ApplyLocation(stack, Settings.NoticeX, Settings.NoticeY);
        Render();
        if (!Settings.ShowWidget)
        {
            Hide();
        }
        if (!Settings.ShowNotifications)
        {
            stack.Hide();
        }
        if (DemoSettingsAfterApply)
        {
            DemoSettingsAfterApply = false;
            ShowSettings();
        }
    }

    internal void SimulateMoveEnd(int x, int y)
    {
        SettingsApplier.ApplyLocation(this, x, y);
        SaveWidgetPosition();
    }

    public void UserChanged(Settings settings)
    {
        bool widgetWasShown = lastShowWidget;
        bool noticesWereShown = lastShowNotifications;
        Settings = settings ?? new Settings();
        if (Settings.WidgetScale != "small" && Settings.WidgetScale != "large") Settings.WidgetScale = "medium";
        TopMost = Settings.WidgetTopMost;
        compact.WidgetZoom = WidgetScaleFactor;
        limits.WidgetZoom = WidgetScaleFactor;
        ApplyWidgetChrome();
        Graphite.ApplyTheme(Settings.ThemeDark);
        ApplyTheme();
        lastShowWidget = Settings.ShowWidget;
        lastShowNotifications = Settings.ShowNotifications;
        Render();
        if (!Settings.ShowWidget)
        {
            Hide();
        }
        else if (!widgetWasShown)
        {
            ShowWidget();
        }
        if (!Settings.ShowNotifications)
        {
            stack.Hide();
        }
        else if (!noticesWereShown || (!noticesManuallyHidden && !stack.Visible))
        {
            ShowNotices();
        }
        Send("settings.changed", "settings", Settings);
    }

    public void Process(string line)
    {
        Dictionary<string, object> message = json.Deserialize<Dictionary<string, object>>(line);
        string type = TextOf(message, "t");
        if (type == "settings.apply")
        {
            object received;
            if (message.TryGetValue("settings", out received))
            {
                ApplyRemote(json.ConvertToType<Settings>(received));
            }
        }
        else if (type == "widget.render")
        {
            if (String.Equals(lastWidgetSnapshot, line, StringComparison.Ordinal)) return;
            StoreRows(message);
            RenderWidget();
            lastWidgetSnapshot = line;
        }
        else if (type == "widget.limits")
        {
            StoreLimits(message);
            RenderWidget();
        }
        else if (type == "notice.show")
        {
            bool added = StoreNotice(message);
            RenderNotices();
            if (Settings.ShowNotifications && !noticesManuallyHidden)
            {
                if (added && Settings.NotificationSound && IsVisible(StatusOf(message)))
                    playNoticeSound(Settings.NotificationSoundPreset);
                ShowNotices();
            }
        }
        else if (type == "shutdown")
        {
            Shutdown();
        }
    }

    private void StoreRows(Dictionary<string, object> message)
    {
        widgetRows.Clear();
        object received;
        if (!message.TryGetValue("rows", out received))
        {
            return;
        }
        IEnumerable list = received as IEnumerable;
        if (list == null)
        {
            return;
        }
        foreach (object item in list)
        {
            Dictionary<string, object> source = item as Dictionary<string, object>;
            if (source == null)
            {
                continue;
            }
            WidgetRow row = new WidgetRow();
            row.Id = TextOf(source, "id");
            row.Workspace = TextOf(source, "workspace");
            row.Title = TextOf(source, "title");
            row.Path = TextOf(source, "path");
            row.Status = TextOf(source, "status");
            row.StatusText = TextOf(source, "statusText");
            row.TaskSummary = TextOf(source, "taskSummary");
            row.TimeText = TextOf(source, "timeText");
            row.RunningCount = NumberOf(source, "runningCount", "active");
            row.CompletedCount = NumberOf(source, "completedCount", "completed");
            row.Registered = String.Equals(TextOf(source, "registered"), "true", StringComparison.OrdinalIgnoreCase);
            row.SessionId = TextOf(source, "sessionId");
            if (!String.IsNullOrEmpty(row.Id))
            {
                widgetRows[row.Id] = row;
            }
        }
    }

    private void StoreLimits(Dictionary<string, object> message)
    {
        limitProviders.Clear();
        availableLimitProviders.Clear();
        object value;
        IEnumerable source;
        if (message.TryGetValue("providers", out value) && (source = value as IEnumerable) != null)
        {
            foreach (object item in source)
            {
                Dictionary<string, object> provider = item as Dictionary<string, object>;
                if (provider != null) limitProviders.Add(provider);
            }
        }
        if (message.TryGetValue("availableProviders", out value) && (source = value as IEnumerable) != null)
        {
            foreach (object item in source)
            {
                Dictionary<string, object> provider = item as Dictionary<string, object>;
                if (provider != null) availableLimitProviders.Add(provider);
            }
        }
        limitsReceivedAt = DateTime.UtcNow;
        foreach (SettingsForm form in settingsForms.ToArray()) form.UpdateLimitProviders(availableLimitProviders);
    }

    private void ApplyTheme()
    {
        BackColor = Graphite.Base;
        rows.BackColor = Graphite.Base;
        cards.BackColor = Graphite.Base;
        compact.BackColor = Graphite.Base;
        limits.BackColor = Graphite.Base;
        limitsHost.BackColor = Graphite.Base;
        resizeGrip.BackColor = Graphite.Base;
        resizeGrip.Invalidate();
        stack.BackColor = Graphite.Border;
        headerPanel.BackColor = Graphite.Card;
        headerTitle.ForeColor = Graphite.Primary;
        taskGlyph.BackColor = Graphite.Card;
        noticeHeader.BackColor = Graphite.Base;
        noticeHeaderTitle.ForeColor = Graphite.Secondary;
        StyleHeaderButton(noticeHeaderClose);
        foreach (Button button in headerButtons) StyleHeaderButton(button);
        foreach (Form form in settingsForms)
        {
            form.BackColor = Graphite.Base;
            form.ForeColor = Graphite.Primary;
            foreach (Control child in form.Controls)
            {
                child.BackColor = Graphite.Card;
                child.ForeColor = Graphite.Primary;
            }
            ((SettingsForm)form).RefreshTheme();
        }
        Invalidate(true);
        stack.Invalidate(true);
    }

    private static int NumberOf(Dictionary<string, object> source, string directKey, string nestedKey)
    {
        object value;
        if (source.TryGetValue(directKey, out value))
        {
            return ToInt(value);
        }
        object counts;
        Dictionary<string, object> nested;
        if (source.TryGetValue("counts", out counts) && (nested = counts as Dictionary<string, object>) != null)
        {
            if (nested.TryGetValue(nestedKey, out value))
            {
                return ToInt(value);
            }
        }
        return 0;
    }

    private static int ToInt(object value)
    {
        int result;
        return Int32.TryParse(Convert.ToString(value), out result) ? result : 0;
    }

    private bool StoreNotice(Dictionary<string, object> message)
    {
        string id = TextOf(message, "id");
        if (String.IsNullOrEmpty(id))
        {
            return false;
        }
        bool added = !notices.ContainsKey(id);
        if (added)
        {
            noticeOrder.Add(id);
        }
        notices[id] = new Dictionary<string, object>(message);
        return added;
    }

    private void Render()
    {
        RenderWidget();
        RenderNotices();
    }

    private void RenderWidget()
    {
        List<WidgetRow> visibleRows = new List<WidgetRow>();
        foreach (WidgetRow row in widgetRows.Values)
        {
            if (IsVisible(row.Status))
            {
                visibleRows.Add(row);
            }
        }
        compact.Visible = Settings.CompactWidget;
        rows.Visible = !Settings.CompactWidget;
        limits.UpdateLimits(limitProviders, limitsReceivedAt);
        UpdateLimitsHost();
        compactToggle.Text = Settings.CompactWidget ? "›" : "‹";
        compactToggle.AccessibleName = Settings.CompactWidget ? "Развернуть список" : "Свернуть список";
        if (Settings.CompactWidget)
        {
            compact.UpdateRows(visibleRows, Settings);
            ResizeWidget();
            return;
        }
        ResizeWidget();
        int firstItem = rows.FirstIndex;
        rows.SuspendLayout();
        try
        {
            ClearCards(rows);
            int width = Math.Max(1, ClientSize.Width - rows.Padding.Horizontal);
            foreach (WidgetRow row in visibleRows.OrderBy(row => IsCompleted(row.Status) ? 0 : 1))
            {
                rows.Controls.Add(Settings.CompactWorkspaceList ? MakeCompactWorkspaceRow(row, width) : MakeWidgetCard(row, width));
            }
        }
        finally
        {
            rows.ResumeLayout(true);
        }
        rows.FirstIndex = firstItem;
    }

    private static bool IsCompleted(string status)
    {
        return String.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearCards(Control container)
    {
        while (container.Controls.Count > 0)
        {
            Control child = container.Controls[0];
            container.Controls.RemoveAt(0);
            child.Dispose();
        }
    }

    private void RenderNotices()
    {
        int firstItem = cards.FirstIndex;
        cards.SuspendLayout();
        try
        {
            ClearCards(cards);
            IEnumerable<string> completed = noticeOrder.AsEnumerable().Reverse().Where(id => IsCompleted(StatusOf(notices[id])));
            IEnumerable<string> other = noticeOrder.Where(id => !IsCompleted(StatusOf(notices[id])));
            foreach (string id in completed.Concat(other))
            {
                Dictionary<string, object> notice = notices[id];
                if (IsVisible(StatusOf(notice))) cards.Controls.Add(MakeNoticeCard(id, notice));
            }
        }
        finally
        {
            cards.ResumeLayout(true);
        }
        if (cards.Controls.Count == 0)
        {
            stack.Hide();
        }
        else if (stack.Visible)
        {
            ResizeNoticeStack();
        }
        cards.FirstIndex = firstItem;
    }

    private Control MakeNoticeHeader(bool demo)
    {
        Panel header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Graphite.Base
        };
        noticeHeader = header;
        Label title = new Label
        {
            Dock = DockStyle.Fill,
            Text = demo ? "Уведомления · демо" : "Уведомления",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = CardTitleFont,
            ForeColor = Graphite.Secondary
        };
        noticeHeaderTitle = title;
        MouseEventHandler drag = delegate(object sender, MouseEventArgs args)
        {
            DragWindow(stack, sender, args);
        };
        header.MouseDown += drag;
        title.MouseDown += drag;
        Button hide = NoticeCloseButton("Скрыть уведомления");
        noticeHeaderClose = hide;
        hide.Dock = DockStyle.Right;
        hide.Click += delegate { HideNoticeStack(); };
        header.Controls.Add(title);
        header.Controls.Add(hide);
        return header;
    }

    private Panel MakeNoticeCard(string id, Dictionary<string, object> notice)
    {
        Panel card = new Panel
        {
            Width = 338,
            BackColor = Graphite.Card,
            Margin = new Padding(0, 0, 0, 6),
            AccessibleName = "Уведомление о задаче"
        };
        Button close = NoticeCloseButton("Закрыть уведомление");
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(card.Width - close.Width - 4, 3);
        close.Click += delegate { CloseNotice(id); };
        card.Controls.Add(close);

        string status = StatusOf(notice);
        int titleRight = close.Left - 4;
        if (Settings.FieldStatus)
        {
            string caption = TextOf(notice, "statusText");
            if (String.IsNullOrEmpty(caption)) caption = StatusLabel(status);
            int width = Math.Min(161, TextRenderer.MeasureText(caption, CardDetailFont).Width + 28);
            Panel chip = MakeStatusIndicator(status, caption, new Rectangle(titleRight - width, 8, width, 20), true, 16, CardDetailFont);
            chip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(chip);
            titleRight = chip.Left - 8;
        }
        if (Settings.FieldWorkspace)
        {
            AddNoticeLabel(card, TextOf(notice, "workspace"), CardTitleFont, Graphite.Primary, 7,
                Math.Max(20, titleRight - 12), 22);
        }
        int top = 34;
        string task = TextOf(notice, "task");
        string normalizedDuration = FormatNoticeDuration(TextOf(notice, "duration"));
        bool validDuration = normalizedDuration != "Время неизвестно" && !String.IsNullOrEmpty(normalizedDuration);
        string inlineDuration = Settings.InlineTaskTime && validDuration ? normalizedDuration : String.Empty;
        if (Settings.FieldTask) top = AddTaskText(card, task, top, card.Width - 24, inlineDuration, NoticeTaskFont, CardDetailFont);

        List<string> metadata = new List<string>();
        bool inlineRendered = Settings.FieldTask && !String.IsNullOrWhiteSpace(task) && task != "Задача не указана" && !String.IsNullOrEmpty(inlineDuration);
        if (Settings.FieldTime && validDuration && !inlineRendered)
        {
            AddLine(metadata, normalizedDuration);
        }
        if (Settings.FieldPath) AddLine(metadata, TextOf(notice, "path"));
        if (metadata.Count > 0)
        {
            top = AddMetadataRule(card, top, card.Width - 24);
            AddNoticeLabel(card, String.Join("  ·  ", metadata.ToArray()), CardDetailFont, Graphite.Secondary,
                top, card.Width - 24, 16);
            top += 17;
        }
        card.Height = top + 8;
        BindSessionOpen(card, TextOf(notice, "sessionId"), close);
        return card;
    }

    private static Panel MakeStatusIndicator(string status, string text, Rectangle bounds, bool badge, int glyphSize, Font captionFont)
    {
        Panel indicator = new Panel
        {
            Bounds = bounds,
            BackColor = badge ? Graphite.ChipFill(status) : Graphite.Card
        };
        var icon = new StatusGlyph(status) { Bounds = new Rectangle(2, (bounds.Height - glyphSize) / 2, glyphSize, glyphSize), BackColor = indicator.BackColor };
        var caption = new Label
        {
            Text = text, UseMnemonic = false, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, Font = captionFont,
            ForeColor = Graphite.Accent(status), BackColor = indicator.BackColor,
            Bounds = new Rectangle(21, 0, Math.Max(1, bounds.Width - 23), bounds.Height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        indicator.Controls.Add(icon);
        indicator.Controls.Add(caption);
        return indicator;
    }

    private int AddTaskText(Panel card, string task, int top, int width, string duration, Font taskFont, Font detailFont, int timeGap = 12)
    {
        if (String.IsNullOrWhiteSpace(task) || task == "Задача не указана") return top;
        int timeWidth = String.IsNullOrEmpty(duration) ? 0 : TextRenderer.MeasureText(duration, detailFont).Width + 8;
        int taskWidth = Math.Max(1, width - timeWidth);
        Size measured = TextRenderer.MeasureText(task, taskFont, new Size(taskWidth, Int32.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        int height = Math.Min(taskFont.Height * 2 + 3, Math.Max(taskFont.Height + 1, measured.Height));
        AddNoticeLabel(card, task, taskFont, Graphite.Primary, top, taskWidth, height);
        if (!String.IsNullOrEmpty(duration))
        {
            Label time = new Label { Text = duration, Font = detailFont, ForeColor = Graphite.Secondary,
                TextAlign = ContentAlignment.TopRight, Bounds = new Rectangle(taskWidth + timeGap, top, timeWidth, detailFont.Height + 2),
                Anchor = AnchorStyles.Top | AnchorStyles.Right };
            card.Controls.Add(time);
        }
        return top + height + 5;
    }

    private static int AddMetadataRule(Panel card, int top, int width)
    {
        card.Controls.Add(new Panel
        {
            BackColor = Graphite.Border,
            Bounds = new Rectangle(12, top, width, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        return top + 6;
    }

    private static Button NoticeCloseButton(string accessibleName)
    {
        Button button = new Button { Text = "×", AccessibleName = accessibleName, Size = new Size(28, 28) };
        StyleHeaderButton(button);
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static void AddNoticeLabel(Panel card, string text, Font font, Color color, int top,
        int width, int height)
    {
        if (String.IsNullOrWhiteSpace(text)) return;
        card.Controls.Add(new Label
        {
            Text = text,
            Font = font,
            ForeColor = color,
            AutoEllipsis = true,
            UseMnemonic = false,
            Bounds = new Rectangle(12, top, width, height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
    }

    private static string FormatNoticeDuration(string duration)
    {
        int seconds;
        if (!duration.EndsWith(" с", StringComparison.Ordinal)
            || !Int32.TryParse(duration.Substring(0, duration.Length - 2), out seconds) || seconds < 0)
        {
            return duration;
        }
        if (seconds < 60) return seconds + " сек";
        if (seconds < 3600) return seconds / 60 + " мин" + (seconds % 60 == 0 ? "" : " " + seconds % 60 + " сек");
        int minutes = seconds % 3600 / 60;
        return seconds / 3600 + " ч" + (minutes == 0 ? "" : " " + minutes + " мин");
    }

    private Panel MakeWidgetCard(WidgetRow row, int width)
    {
        Panel card = new Panel
        {
            Width = width,
            BackColor = Graphite.Card,
            AccessibleName = "Карточка рабочей области",
            Margin = new Padding(0, 0, 0, 6)
        };
        int contentWidth = Math.Max(1, width - Px(24));
        string title = String.IsNullOrEmpty(row.Workspace) ? row.Title : row.Workspace;
        int top = Px(8);
        if (Settings.FieldWorkspace && !String.IsNullOrWhiteSpace(title))
        {
            AddNoticeLabel(card, title, WidgetFont(9F, FontStyle.Bold), Graphite.Primary, top, contentWidth, Px(19));
            top += Px(21);
        }
        if (Settings.FieldStatus)
        {
            string status = String.IsNullOrEmpty(row.StatusText) ? StatusLabel(row.Status) : row.StatusText;
            Panel indicator = MakeStatusIndicator(row.Status, status, new Rectangle(Px(10), top, contentWidth + Px(2), Px(18)), false, Px(16), WidgetFont(8.25F));
            indicator.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(indicator);
            top += Px(21);
        }
        if (Settings.FieldTask) top = AddTaskText(card, row.TaskSummary, top, contentWidth,
            Settings.InlineTaskTime ? row.TimeText : String.Empty, WidgetFont(9F, FontStyle.Regular), WidgetFont(8.25F), Px(12));
        List<string> metadata = WidgetMetadata(row);
        if (metadata.Count > 0 && top > 8) top = AddMetadataRule(card, top, contentWidth);
        foreach (string detail in metadata)
        {
            AddNoticeLabel(card, detail, WidgetFont(8.25F), Graphite.Secondary, top, contentWidth, Px(17));
            top += Px(18);
        }
        card.Height = top + Px(6);
        if (!String.Equals(row.Status, "idle", StringComparison.OrdinalIgnoreCase)) BindSessionOpen(card, row.SessionId, null);
        return card;
    }

    private Panel MakeCompactWorkspaceRow(WidgetRow row, int width)
    {
        Panel item = new Panel { Width = width, Height = Px(26), BackColor = Graphite.Card,
            AccessibleName = "Рабочая область: " + (String.IsNullOrEmpty(row.Workspace) ? row.Title : row.Workspace), Margin = new Padding(0, 0, 0, Px(2)) };
        string title = String.IsNullOrEmpty(row.Workspace) ? row.Title : row.Workspace;
        Label name = new Label { Text = title, AutoEllipsis = true, Font = WidgetFont(8.25F), ForeColor = Graphite.Primary,
            Bounds = new Rectangle(Px(8), Px(4), Math.Max(1, width - Px(126)), Px(18)), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        string state = String.IsNullOrEmpty(row.StatusText) ? StatusLabel(row.Status) : row.StatusText;
        Panel indicator = MakeStatusIndicator(row.Status, state, new Rectangle(Math.Max(Px(8), width - Px(118)), Px(4), Px(112), Px(18)), false, Px(16), WidgetFont(8.25F));
        indicator.AccessibleDescription = state;
        indicator.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        item.Controls.Add(name); item.Controls.Add(indicator);
        if (!String.Equals(row.Status, "idle", StringComparison.OrdinalIgnoreCase)) BindSessionOpen(item, row.SessionId, null);
        return item;
    }

    private void BindSessionOpen(Control control, string sessionId, Control excluded)
    {
        if (String.IsNullOrEmpty(sessionId)) return;
        MouseEventHandler click = delegate(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            Send("session.open", "sessionId", sessionId);
            if (!demoMode) { try { System.Diagnostics.Process.Start(DshOpenUrl); } catch { } }
        };
        foreach (Control child in control.Controls) if (child != excluded) BindSessionOpen(child, sessionId, excluded);
        if (control != excluded) control.MouseClick += click;
    }

    private static string StatusLabel(string status)
    {
        if (status == "running") return "Выполняется";
        if (status == "completed") return "Завершено";
        if (status == "error") return "Ошибка";
        if (status == "disposed") return "Сессия закрыта";
        if (status == "idle") return "Без задач";
        return "Неизвестно";
    }

    private static string ShortStatus(string status, string fallback)
    {
        if (status == "running") return "В работе";
        if (status == "completed") return "Готово";
        if (status == "interrupted" || status == "aborted") return "Стоп";
        return fallback;
    }

    private List<string> WidgetMetadata(WidgetRow row)
    {
        List<string> lines = new List<string>();
        bool inlineRendered = Settings.InlineTaskTime && Settings.FieldTask && !String.IsNullOrWhiteSpace(row.TaskSummary)
            && row.TaskSummary != "Задача не указана" && !String.IsNullOrEmpty(row.TimeText);
        if (Settings.FieldTime && !String.IsNullOrEmpty(row.TimeText) && !inlineRendered)
        {
            AddLine(lines, row.TimeText);
        }
        if (Settings.FieldPath)
        {
            AddLine(lines, row.Path);
        }
        if (Settings.WidgetCounts)
        {
            lines.Add("Активных: " + row.RunningCount + " · Завершено: " + row.CompletedCount);
        }
        return lines;
    }

    private static void AddLine(List<string> lines, params string[] alternatives)
    {
        foreach (string value in alternatives)
        {
            if (!String.IsNullOrEmpty(value))
            {
                lines.Add(value);
                return;
            }
        }
    }

    private static string TextOf(Dictionary<string, object> data, string key)
    {
        object value;
        return data.TryGetValue(key, out value) ? Convert.ToString(value) : String.Empty;
    }

    private static string StatusOf(Dictionary<string, object> notice)
    {
        string reason = TextOf(notice, "reason");
        return String.IsNullOrEmpty(reason) ? TextOf(notice, "status") : reason;
    }

    private bool IsVisible(string status)
    {
        switch ((status ?? String.Empty).ToLowerInvariant())
        {
            case "running": return Settings.StatusRunning;
            case "completed": return Settings.StatusCompleted;
            case "error": return Settings.StatusError;
            case "max-tokens": return Settings.StatusMaxTokens;
            case "blocked": return Settings.StatusBlocked;
            case "aborted": return Settings.StatusAborted;
            case "interrupted": return Settings.StatusInterrupted;
            case "idle": return Settings.StatusIdle;
            case "unknown": return Settings.StatusUnknown;
            case "disposed": return Settings.StatusDisposed;
            default: return Settings.StatusUnknown;
        }
    }

    private void CloseNotice(string id)
    {
        notices.Remove(id);
        noticeOrder.Remove(id);
        RenderNotices();
        Send("notice.closed", "id", id);
    }

    private void ShowWidget()
    {
        SettingsApplier.ApplyLocation(this, Settings.WidgetX, Settings.WidgetY);
        Show();
    }

    private static void StyleHeaderButton(Button button)
    {
        button.Margin = new Padding(2, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Graphite.Border;
        button.FlatAppearance.MouseOverBackColor = Graphite.Base;
        button.BackColor = Graphite.Card;
        button.ForeColor = Graphite.Secondary;
        button.TabStop = true;
    }

    private void ToggleCompactMode()
    {
        Settings.CompactWidget = !Settings.CompactWidget;
        UserChanged(Settings);
    }

    private int UserWidgetHeight
    {
        get { return expandedUserHeight; }
    }

    internal void SetUserWidgetHeight(int height)
    {
        if (Settings.CompactWidget) return;
        Rectangle area = Screen.FromPoint(Location).WorkingArea;
        int requested = Math.Max(1, Math.Min(height, area.Bottom - Math.Max(Top, area.Top)));
        expandedUserHeight = (int)Math.Round(requested / WidgetScaleFactor);
        UpdateLimitsHost();
        ResizeWidget();
    }

    private void ResizeWidget()
    {
        Point previous = Location;
        Rectangle area = Screen.FromPoint(previous).WorkingArea;
        int automatic = Settings.CompactWidget
            ? Px(CompactHeight) + limitsHost.Height
            : Px(ResizeGripHeight + ExpandedHeight);
        int preferred = Settings.CompactWidget ? 0 : Px(expandedUserHeight);
        int height = preferred > 0 ? Math.Max(automatic, preferred) : Math.Min(automatic, HeightLimit(area, Px(ExpandedHeight + ResizeGripHeight)));
        resizeGrip.Visible = !Settings.CompactWidget;
        Rectangle next = Geometry.ClampToVisible(new Rectangle(previous, new Size(Px(WidgetWidth), height)), area);
        if (Bounds == next)
        {
            return;
        }
        Bounds = next;
        if (Location != previous)
        {
            Settings.WidgetX = Left;
            Settings.WidgetY = Top;
            Send("widget.moved", "x", Left, "y", Top);
        }
    }

    private static int HeightLimit(Rectangle area, int maximum)
    {
        return Math.Max(1, Math.Min(maximum, (int)(area.Height * 0.55)));
    }

    private void ShowNotices()
    {
        noticesManuallyHidden = false;
        if (cards.Controls.Count == 0)
        {
            return;
        }
        ResizeNoticeStack();
        stack.Show();
    }

    private void ShowSettings()
    {
        SettingsForm form = new SettingsForm(Settings, UserChanged, availableLimitProviders, playNoticeSound);
        settingsForms.Add(form);
        form.FormClosed += delegate
        {
            settingsForms.Remove(form);
        };
        AddOwnedForm(form);
        form.Show(this);
        form.Activate();
    }

    private void ResizeNoticeStack()
    {
        int desiredHeight = 32 + cards.Padding.Vertical;
        foreach (Control card in cards.Controls)
        {
            desiredHeight += card.Height + card.Margin.Vertical;
        }
        Point anchor = new Point(Settings.NoticeX, Settings.NoticeY);
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        int height = Math.Min(desiredHeight, HeightLimit(area, NoticeStackHeightLimit));
        stack.Bounds = Geometry.ClampToVisible(new Rectangle(anchor, new Size(NoticeStackWidth, height)), area);
        cards.PerformLayout();
    }

    private void UpdateLimitsHost()
    {
        limitsHost.Visible = Settings.ShowLimits;
        int preferred = Math.Min(Px(UserWidgetHeight), Screen.FromPoint(Location).WorkingArea.Height);
        int minimumTaskArea = Settings.CompactWidget ? Px(CompactHeight) - headerPanel.Height : Px(160);
        int available = Math.Max(Px(DefaultQuotaHeight), preferred - headerPanel.Height - resizeGrip.Height - minimumTaskArea);
        limitsHost.Height = Settings.ShowLimits ? Math.Min(available, limits.DesiredHeight) : 0;
        limits.Width = Math.Max(1, ClientSize.Width - limitsHost.Padding.Horizontal);
    }

    private void SaveWidgetPosition()
    {
        Settings.WidgetX = Left;
        Settings.WidgetY = Top;
        Send("widget.moved", "x", Left, "y", Top);
    }

    private void StackFormClosing(object sender, FormClosingEventArgs eventArgs)
    {
        if (!isShuttingDown && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            HideNoticeStack();
        }
    }

    private void HideNoticeStack()
    {
        noticesManuallyHidden = true;
        stack.Hide();
    }

    private void Shutdown()
    {
        if (isShuttingDown)
        {
            return;
        }
        isShuttingDown = true;
        foreach (Form form in new List<Form>(settingsForms))
        {
            form.Close();
        }
        stack.Close();
        tray.Visible = false;
        tray.Dispose();
        Close();
        Application.Exit();
    }

    private void Send(string type, params object[] fields)
    {
        Dictionary<string, object> message = new Dictionary<string, object>();
        message["t"] = type;
        for (int index = 0; index < fields.Length; index += 2)
        {
            message[Convert.ToString(fields[index])] = fields[index + 1];
        }
        string line = json.Serialize(message);
        if (outputCallback != null)
        {
            outputCallback(line);
        }
        else
        {
            output.WriteLine(line);
        }
    }

#if !NO_MAIN
    [STAThread]
    public static int Main(string[] arguments)
    {
        if (Array.IndexOf(arguments, "--self-test") >= 0)
        {
            return SelfTest();
        }
        DshNotifyGui gui = new DshNotifyGui(Array.IndexOf(arguments, "--demo") >= 0);
        gui.DemoSettingsAfterApply = Array.IndexOf(arguments, "--demo-settings") >= 0;
        gui.HandleCreated += delegate
        {
            gui.Send("ready");
            new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                StreamReader input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    string received = line;
                    try
                    {
                        gui.BeginInvoke((Action)delegate
                        {
                            gui.Process(received);
                        });
                    }
                    catch
                    {
                        break;
                    }
                }
                try
                {
                    gui.BeginInvoke((Action)delegate
                    {
                        gui.Shutdown();
                    });
                }
                catch
                {
                }
            })) { IsBackground = true }.Start();
        };
        Application.Run(gui);
        return 0;
    }
#endif

    private static int SelfTest()
    {
        Settings settings = new Settings();
        SettingsForm form = new SettingsForm(settings, delegate(Settings changed) { }, new List<Dictionary<string, object>>());
        form.Check("field-time").Checked = false;
        bool checkbox = !settings.FieldTime;
        bool soundDisabledByDefault = !settings.NotificationSound;
        form.Check("notice-sound").Checked = true;
        bool soundCheckbox = settings.NotificationSound;
        ComboBox soundChoices = (ComboBox)form.Controls.Find("notification-sound-preset", true).Single();
        bool legacySoundPreserved = soundChoices.SelectedIndex == 0 && settings.NotificationSoundPreset == "icq";
        soundChoices.SelectedIndex = NotificationSounds.IndexOf("squeak");
        bool soundSelectionSaved = settings.NotificationSoundPreset == "squeak" && soundChoices.Items.Count == 6;
        form.Dispose();
        Form location = new Form();
        location.Width = 100;
        location.Height = 100;
        SettingsApplier.ApplyLocation(location, 120, 140);
        bool position = location.Left == 120 && location.Top == 140;
        location.Dispose();
        Rectangle offscreen = new Rectangle(-4, -4, 10, 10);
        Rectangle available = new Rectangle(0, 0, 100, 100);
        bool clamp = Geometry.ClampToVisible(offscreen, available).Left == 0;
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        bool settingsRoundTrip = serializer.Deserialize<Settings>(serializer.Serialize(settings)).ShowWidget;
        bool newSettings = serializer.Deserialize<Settings>(serializer.Serialize(settings)).ThemeDark
            && !serializer.Deserialize<Settings>(serializer.Serialize(settings)).ShowLimits;
        bool countdown = LimitsPanel.Countdown(0) == "сброс доступен"
            && LimitsPanel.Countdown((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds + 59) == "<1 мин";
        List<string> output = new List<string>();
        int soundCount = 0;
        string playedPreset = null;
        DshNotifyGui gui = new DshNotifyGui(false, delegate(string line)
        {
            output.Add(line);
        }, delegate(string preset) { soundCount++; playedPreset = preset; });
        bool iconDrawn;
        using (Bitmap iconBitmap = new Bitmap(gui.taskGlyph.Width, gui.taskGlyph.Height))
        {
            gui.taskGlyph.DrawToBitmap(iconBitmap, gui.taskGlyph.ClientRectangle);
            Color center = iconBitmap.GetPixel(iconBitmap.Width / 2, iconBitmap.Height / 2);
            iconDrawn = center.B > center.R + 30;
        }
        bool taskIconConsistent = gui.Icon == TaskIcon.Window && gui.tray.Icon == TaskIcon.Small
            && HasMatchingShellIcon(gui.Icon);
        bool taskTitle = gui.Text == "Задачи";
        gui.SimulateMoveEnd(-400, -400);
        bool moveEmitted = output.Count == 1 && output[0].Contains("widget.moved");
        bool moveStored = gui.Settings.WidgetX == gui.Left && gui.Settings.WidgetY == gui.Top;
        output.Clear();
        settings = new Settings();
        settings.StatusIdle = true;
        settings.CompactWidget = false;
        gui.Process("{\"t\":\"settings.apply\",\"settings\":{\"WidgetX\":120,\"WidgetY\":140}}");
        bool noEcho = output.Count == 0;
        gui.ApplyRemote(settings);
        gui.Process("{\"t\":\"widget.render\",\"rows\":["
            + "{\"id\":\"running\",\"title\":\"Выполняется\",\"status\":\"running\",\"sessionId\":\"self-card\","
            + "\"path\":\"C:\\\\Path\",\"timeText\":\"10:00\"},"
            + "{\"id\":\"completed\",\"title\":\"Завершено\",\"status\":\"completed\"},"
            + "{\"id\":\"error\",\"title\":\"Ошибка\",\"status\":\"error\"},"
            + "{\"id\":\"max\",\"title\":\"Лимит\",\"status\":\"max-tokens\"},"
            + "{\"id\":\"blocked\",\"title\":\"Блок\",\"status\":\"blocked\"},"
            + "{\"id\":\"aborted\",\"title\":\"Отмена\",\"status\":\"aborted\"},"
            + "{\"id\":\"interrupted\",\"title\":\"Прервано\",\"status\":\"interrupted\"},"
            + "{\"id\":\"idle\",\"title\":\"Без задач\",\"status\":\"idle\",\"registered\":true},"
            + "{\"id\":\"unknown\",\"title\":\"Неизвестно\",\"status\":\"unknown\"},"
            + "{\"id\":\"disposed\",\"title\":\"Закрыто\",\"status\":\"disposed\"}]}");
        gui.Process("{\"t\":\"notice.show\",\"id\":\"completed\",\"reason\":\"completed\","
            + "\"workspace\":\"Alpha\",\"task\":\"Задача\",\"statusText\":\"Готово\","
            + "\"path\":\"C:\\\\Notice\",\"duration\":\"1 с\",\"sessionId\":\"self-notice\"}");
        bool allRowsShown = gui.rows.Controls.Count == 10;
        bool allNoticesShown = gui.cards.Controls.Count == 1;
        InvokeLeftClick(gui.rows.Controls[1]);
        InvokeLeftClick(gui.cards.Controls[0]);
        bool interactionIds = output.Any(line => line.Contains("self-card"))
            && output.Any(line => line.Contains("self-notice"));
        settings.StatusIdle = false;
        gui.UserChanged(settings);
        bool registeredIdleFiltered = gui.rows.Controls.Count == 9;
        settings.StatusIdle = true;
        gui.UserChanged(settings);
        bool completedRowFirst = HasText(gui.rows.Controls[0], "Завершено")
            && !HasText(gui.rows.Controls[0], "Выполняется");
        int expandedWidth = gui.Width;
        gui.Process("{\"t\":\"notice.show\",\"id\":\"failed\",\"reason\":\"error\",\"workspace\":\"Ошибка\"}");
        gui.Process("{\"t\":\"notice.show\",\"id\":\"recent\",\"reason\":\"completed\",\"workspace\":\"Последняя\"}");
        bool completedNoticeFirst = HasText(gui.cards.Controls[0], "Последняя");
        int noticeWidth = gui.stack.Width;
        for (int index = 0; index < 12; index++)
        {
            gui.Process("{\"t\":\"notice.show\",\"id\":\"test-" + index
                + "\",\"reason\":\"error\",\"workspace\":\"Проверка\"}");
        }
        bool noticeHeightAndWidth = gui.stack.Width == noticeWidth && gui.stack.Height <= 420
            && gui.stack.Height < Screen.FromControl(gui.stack).WorkingArea.Height;
        gui.cards.FirstIndex = 0;
        gui.cards.ScrollWheel(-60);
        bool wholeCardScroll = gui.cards.FirstIndex == 0;
        gui.cards.ScrollWheel(-60);
        wholeCardScroll = wholeCardScroll && gui.cards.FirstIndex == 1
            && !gui.cards.Controls[0].Visible && gui.cards.Controls[1].Visible
            && gui.cards.Controls.Cast<Control>().Where(card => card.Visible).All(
                card => gui.cards.ClientRectangle.Contains(card.Bounds));
        gui.cards.ScrollWheel(120);
        wholeCardScroll = wholeCardScroll && gui.cards.FirstIndex == 0 && gui.cards.Controls[0].Visible;
        foreach (string id in new string[] { "failed", "recent" }) gui.CloseNotice(id);
        for (int index = 0; index < 12; index++) gui.CloseNotice("test-" + index);
        gui.Process("{\"t\":\"notice.show\",\"id\":\"completed\",\"reason\":\"completed\","
            + "\"workspace\":\"Alpha\",\"task\":\"Задача не указана\",\"duration\":\"113 с\"}");
        bool noticePresentation = gui.stack.FormBorderStyle == FormBorderStyle.None
            && !HasText(gui.cards, "Задача не указана")
            && HasText(gui.cards, "1 мин 53 сек") && gui.stack.Height <= 115;
        gui.Process("{\"t\":\"notice.show\",\"id\":\"completed\",\"reason\":\"completed\","
            + "\"workspace\":\"Alpha\",\"task\":\"Задача\",\"statusText\":\"Готово\","
            + "\"path\":\"C:\\\\Notice\",\"duration\":\"1 с\"}");
        gui.ToggleCompactMode();
        bool compactToggle = gui.Settings.CompactWidget;
        bool fixedWidgetWidth = gui.Width == expandedWidth;
        gui.ToggleCompactMode();
        string[] statusProperties = new string[]
        {
            "StatusRunning", "StatusCompleted", "StatusError", "StatusMaxTokens",
            "StatusBlocked", "StatusAborted", "StatusInterrupted", "StatusIdle",
            "StatusUnknown", "StatusDisposed"
        };
        bool matrix = true;
        foreach (string property in statusProperties)
        {
            typeof(Settings).GetProperty(property).SetValue(settings, false, null);
            gui.UserChanged(settings);
            matrix = matrix && gui.rows.Controls.Count == 9;
            if (property == "StatusCompleted")
            {
                matrix = matrix && gui.cards.Controls.Count == 0;
            }
            typeof(Settings).GetProperty(property).SetValue(settings, true, null);
            gui.UserChanged(settings);
        }
        settings.FieldPath = false;
        settings.FieldTime = false;
        gui.UserChanged(settings);
        bool fieldsHidden = !HasText(gui.rows, "C:\\Path") && !HasText(gui.rows, "10:00");
        fieldsHidden = fieldsHidden && !HasText(gui.cards, "C:\\Notice") && !HasText(gui.cards, "1 с");
        gui.Process("{\"t\":\"widget.render\",\"rows\":["
            + "{\"id\":\"running\",\"title\":\"Alpha 2\",\"status\":\"running\"}]}");
        bool rowsReplaced = gui.rows.Controls.Count == 1 && HasText(gui.rows, "Alpha 2");
        bool silentUntilEnabled = soundCount == 0;
        settings.NotificationSound = true;
        settings.NotificationSoundPreset = "whistle";
        gui.ApplyRemote(settings);
        string newNotice = "{\"t\":\"notice.show\",\"id\":\"test-audio\",\"reason\":\"completed\"}";
        gui.Process(newNotice);
        gui.Process(newNotice);
        gui.Render();
        gui.HideNoticeStack();
        gui.Process("{\"t\":\"notice.show\",\"id\":\"test-audio-hidden\",\"reason\":\"completed\"}");
        gui.ShowNotices();
        settings.StatusError = false;
        gui.ApplyRemote(settings);
        gui.Process("{\"t\":\"notice.show\",\"id\":\"test-audio-filtered\",\"reason\":\"error\"}");
        settings.ShowNotifications = false;
        gui.ApplyRemote(settings);
        gui.Process("{\"t\":\"notice.show\",\"id\":\"test-audio-muted\",\"reason\":\"completed\"}");
        bool notificationSound = soundDisabledByDefault && soundCheckbox && silentUntilEnabled && soundCount == 1;
        bool notificationSoundPresets = legacySoundPreserved && soundSelectionSaved && playedPreset == "whistle"
            && NotificationSounds.LoadPackagedPresets();
        gui.Dispose();
        Image deepSeekBadge = LimitsPanel.ProviderBadge("deepseek-account", "DeepSeek Account");
        Image codexBadge = LimitsPanel.ProviderBadge("openai-codex", "Codex");
        Image zaiBadge = LimitsPanel.ProviderBadge("local-z-route", "Z.ai Coding");
        Image genericBadge = LimitsPanel.ProviderBadge("other", "Другой ИИ");
        bool providerBadgeAliases = deepSeekBadge != null && codexBadge != null && zaiBadge != null && genericBadge != null
            && deepSeekBadge == LimitsPanel.ProviderBadge("configured-api-route", "DeepSeek")
            && new[] { deepSeekBadge, codexBadge, zaiBadge, genericBadge }.Distinct().Count() == 4;
        bool passed = checkbox && position && clamp && settingsRoundTrip && newSettings && iconDrawn
            && taskIconConsistent && providerBadgeAliases && notificationSound && notificationSoundPresets
            && countdown && taskTitle && moveEmitted && moveStored;
        passed = passed && noEcho && allRowsShown && allNoticesShown && interactionIds && registeredIdleFiltered;
        passed = passed && matrix && fieldsHidden && rowsReplaced && compactToggle && noticePresentation
            && completedRowFirst && completedNoticeFirst && noticeHeightAndWidth && fixedWidgetWidth && wholeCardScroll;
        Console.WriteLine("{\"t\":\"selftest\",\"pass\":" + (passed ? "true" : "false")
            + ",\"checks\":{\"checkbox\":" + JsonBool(checkbox)
            + ",\"position\":" + JsonBool(position)
            + ",\"clamp\":" + JsonBool(clamp)
            + ",\"settingsRoundTrip\":" + JsonBool(settingsRoundTrip)
            + ",\"newSettings\":" + JsonBool(newSettings)
            + ",\"iconDrawn\":" + JsonBool(iconDrawn)
            + ",\"taskIconConsistent\":" + JsonBool(taskIconConsistent)
            + ",\"providerBadgeAliases\":" + JsonBool(providerBadgeAliases)
            + ",\"notificationSound\":" + JsonBool(notificationSound)
            + ",\"notificationSoundPresets\":" + JsonBool(notificationSoundPresets)
            + ",\"countdown\":" + JsonBool(countdown)
            + ",\"taskTitle\":" + JsonBool(taskTitle)
            + ",\"moveEmitted\":" + JsonBool(moveEmitted)
            + ",\"moveStored\":" + JsonBool(moveStored)
            + ",\"noEcho\":" + JsonBool(noEcho)
            + ",\"allRowsShown\":" + JsonBool(allRowsShown)
            + ",\"allNoticesShown\":" + JsonBool(allNoticesShown)
            + ",\"interactionIds\":" + JsonBool(interactionIds)
            + ",\"registeredIdleFiltered\":" + JsonBool(registeredIdleFiltered)
            + ",\"statusMatrix\":" + JsonBool(matrix)
            + ",\"fieldsHidden\":" + JsonBool(fieldsHidden)
            + ",\"noticePresentation\":" + JsonBool(noticePresentation)
            + ",\"completedRowFirst\":" + JsonBool(completedRowFirst)
            + ",\"completedNoticeFirst\":" + JsonBool(completedNoticeFirst)
            + ",\"noticeHeightAndWidth\":" + JsonBool(noticeHeightAndWidth)
            + ",\"fixedWidgetWidth\":" + JsonBool(fixedWidgetWidth)
            + ",\"wholeCardScroll\":" + JsonBool(wholeCardScroll)
            + ",\"rowsReplaced\":" + JsonBool(rowsReplaced)
            + ",\"compactToggle\":" + JsonBool(compactToggle) + "}}");
        return passed ? 0 : 1;
    }

    private static bool HasMatchingShellIcon(Icon expected)
    {
        using (Icon shellIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
        {
            if (shellIcon == null || shellIcon.Size != expected.Size) return false;
            using (Bitmap shellBitmap = shellIcon.ToBitmap())
            using (Bitmap expectedBitmap = expected.ToBitmap())
            {
                for (int y = 0; y < expectedBitmap.Height; y++)
                {
                    for (int x = 0; x < expectedBitmap.Width; x++)
                    {
                        if (shellBitmap.GetPixel(x, y) != expectedBitmap.GetPixel(x, y)) return false;
                    }
                }
            }
        }
        return true;
    }

    private static void InvokeLeftClick(Control control)
    {
        typeof(Control).GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 4, 4, 0) });
    }

    private static string JsonBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static bool HasText(Control control, string expected)
    {
        if ((control.Text ?? String.Empty).Contains(expected))
        {
            return true;
        }
        foreach (Control child in control.Controls)
        {
            if (HasText(child, expected))
            {
                return true;
            }
        }
        return false;
    }
}
