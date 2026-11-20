using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using Microsoft.Win32;
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
    public bool HideCompletedWhenHarnessOpen { get; set; }
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
    public string Theme { get; set; }
    public bool ThemeDark { get; set; }
    public string InterfaceStyle { get; set; }
    public string NormalizedInterfaceStyle { get { return InterfaceStyle == "modern" ? "modern" : "classic"; } }
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
        Theme = "system";
        ThemeDark = true;
        InterfaceStyle = "classic";
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
    private const int GeneralRowHeight = 24;
    private const int GeneralValueLeft = 198;
    private const int SettingsContentWidth = 382;
    private readonly Settings value;
    private readonly Action<Settings> changed;
    private readonly Action<TelegramCredentials> telegramChanged;
    private readonly TelegramPublisher telegramPublisher;
    private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();
    private readonly Dictionary<Control, int> pages = new Dictionary<Control, int>();
    private readonly ComboBox soundPreset = new SettingsChoice();
    private readonly ComboBox widgetScale = new SettingsChoice();
    private readonly ComboBox themeMode = new SettingsChoice();
    private readonly ComboBox interfaceStyle = new SettingsChoice();
    private readonly List<Button> tabButtons = new List<Button>();
    private readonly Action<string> quotaKeyChanged;
    private readonly bool allowQuotaVault;
    private readonly Rectangle? layoutWorkingArea;
    private Panel contentPanel;
    private Panel navPanel;
    private Panel titleBar;
    private Label modernPageTitle;
    private Button closeButton;
    private bool syncing;
    private bool fittingContent;
    private int page;
    private int limitProviderPage;
    private Button previousLimitPage;
    private Button nextLimitPage;
    private Label limitPageCaption;
    private readonly Dictionary<string, string> limitProviderCatalog = new Dictionary<string, string>();
    private bool refreshedDuringSave;

    public Button SaveButton { get; private set; }

    internal SettingsForm(Settings current, Action<Settings> onChanged, List<Dictionary<string, object>> providers,
        Action<string> previewSound = null, Action<string> onQuotaKeyChanged = null, bool quotaVaultEnabled = true,
        Rectangle? workingArea = null, Action<TelegramCredentials> onTelegramChanged = null, TelegramPublisher publisher = null)
    {
        value = current;
        changed = onChanged;
        telegramChanged = onTelegramChanged;
        telegramPublisher = publisher;
        quotaKeyChanged = onQuotaKeyChanged;
        allowQuotaVault = quotaVaultEnabled;
        layoutWorkingArea = workingArea;
        Text = "Настройки";
        FormBorderStyle = FormBorderStyle.None;
        SuspendLayout();
        ClientSize = new Size(398, 329);
        ModernTheme.Apply(value.NormalizedInterfaceStyle == "modern", Graphite.IsDark);
        BackColor = ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base;
        ForeColor = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary;
        KeyPreview = true;
        ShowInTaskbar = false;
        KeyDown += delegate(object sender, KeyEventArgs args) { if (args.KeyCode == Keys.Escape) Close(); };
        Shown += delegate { FitToVisibleContent(); };
        titleBar = new Panel();
        titleBar.BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card; titleBar.SetBounds(0, 0, 398, 32);
        titleBar.MouseDown += DragCaption;
        Label title = new Label(); title.Text = "☷  Настройки"; title.ForeColor = Graphite.Primary; title.AutoSize = true; title.Location = new Point(10, 8); title.MouseDown += DragCaption;
        titleBar.Controls.Add(title); Controls.Add(titleBar);
        navPanel = new Panel(); navPanel.BackColor = ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base; navPanel.SetBounds(8, 36, SettingsContentWidth, 28); navPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        string[] tabNames = new string[] { "Общие", "Карточки", "Состояния", "Лимиты" };
        for (int tabIndex = 0; tabIndex < tabNames.Length; tabIndex++)
        {
            Button tab = new Button(); int selected = tabIndex;
            tab.Text = tabNames[tabIndex]; tab.AccessibleName = tabNames[tabIndex]; tab.SetBounds(tabIndex * 96, 1, 92, 25);
            tab.FlatStyle = FlatStyle.Flat;
            tab.Click += delegate { ShowPage(selected); };
            navPanel.Controls.Add(tab);
            tabButtons.Add(tab);
        }
        Controls.Add(navPanel);
        modernPageTitle = new Label { Name = "modern-page-title", Text = "Общие", Visible = false, Font = new Font("Segoe UI", 11F, FontStyle.Bold), AutoEllipsis = true };
        Controls.Add(modernPageTitle);
        int top = 72;
        page = 0;
        AddCaption("Виджет", ref top);
        Add("show-widget", "Показывать виджет", value.ShowWidget, ref top, GeneralRowHeight);
        Add("widget-topmost", "Поверх остальных окон", value.WidgetTopMost, ref top, GeneralRowHeight);
        Add("compact-widget", "Компактный вид", value.CompactWidget, ref top, GeneralRowHeight);
        Add("compact-workspace-list", "Короткий список областей", value.CompactWorkspaceList, ref top, GeneralRowHeight);
        Add("show-limits", "Показывать лимиты ИИ", value.ShowLimits, ref top, GeneralRowHeight);
        widgetScale.Items.AddRange(new object[] { "Маленький (85%)", "Средний (100%)", "Большой (125%)" });
        widgetScale.SelectedIndex = value.WidgetScale == "small" ? 0 : (value.WidgetScale == "large" ? 2 : 1);
        AddGeneralChoice(widgetScale, "widget-scale", "Масштаб виджета", ref top);
        themeMode.Items.AddRange(new object[] { "Как в Windows", "Светлая", "Тёмная" });
        themeMode.SelectedIndex = ThemeIndex(value.Theme);
        AddGeneralChoice(themeMode, "theme-mode", "Тема", ref top);
        interfaceStyle.Items.AddRange(new object[] { "Классика", "Современный" });
        interfaceStyle.SelectedIndex = value.NormalizedInterfaceStyle == "modern" ? 1 : 0;
        AddGeneralChoice(interfaceStyle, "interface-style", "Интерфейс", ref top);
        AddCaption("Уведомления", ref top);
        Add("show-notices", "Показывать уведомления", value.ShowNotifications, ref top, GeneralRowHeight);
        Add("hide-completed-harness-open", "Скрывать завершённые при открытом Harness", value.HideCompletedWhenHarnessOpen, ref top, GeneralRowHeight);
        Add("subagent-notices", "Уведомления субагентов", value.SubagentNotices, ref top, GeneralRowHeight);
        Add("notice-sound", "Звук уведомлений", value.NotificationSound, ref top, GeneralRowHeight);
        soundPreset.DisplayMember = "Value";
        soundPreset.Items.AddRange(NotificationSounds.Presets.Cast<object>().ToArray());
        soundPreset.SelectedIndex = NotificationSounds.IndexOf(value.NotificationSoundPreset);
        Button preview = new Button { Text = "▶", AccessibleName = "Прослушать звук", FlatStyle = FlatStyle.Flat, Enabled = previewSound != null };
        preview.Click += delegate { if (previewSound != null) previewSound(SelectedSound()); };
        AddGeneralChoice(soundPreset, "notification-sound-preset", "Сигнал уведомления", ref top, preview);
        Button telegram = new Button { Text = "Настроить Telegram", AccessibleName = "Настроить Telegram", FlatStyle = FlatStyle.Flat, BackColor = Graphite.Card, ForeColor = Graphite.Primary, Enabled = telegramPublisher != null };
        telegram.SetBounds(RowInset, top, 180, 24);
        telegram.Click += delegate
        {
            TelegramSettingsUi.Open(this, value.ShowNotifications && value.StatusError, telegramPublisher, telegramChanged, WindowScaleFactor());
        };
        Controls.Add(telegram); pages.Add(telegram, page); top += 28;
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
        AddCaption("Ключи только для лимитов", ref top);
        AddQuotaKeyButton("deepseek-official", "DeepSeek API", ref top);
        AddQuotaKeyButton("zai", "Z.ai Coding", ref top);
        AddCaption("Показывать лимиты", ref top);
        foreach (Dictionary<string, object> provider in providers ?? new List<Dictionary<string, object>>())
        {
            string id = Convert.ToString(provider.ContainsKey("id") ? provider["id"] : "");
            if (String.IsNullOrEmpty(id) || checks.ContainsKey("limit-" + id)) continue;
            string label = Convert.ToString(provider.ContainsKey("label") ? provider["label"] : id);
            Add("limit-" + id, label, !value.HiddenLimitProviders.Contains(id), ref top);
        }
        foreach (KeyValuePair<string, string> provider in ProviderCatalog(providers)) limitProviderCatalog[provider.Key] = provider.Value;
        SaveButton = new Button();
        SaveButton.Text = "Готово";
        SaveButton.SetBounds(296, 290, 84, 26);
        SaveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        SaveButton.FlatStyle = FlatStyle.Flat;
        SaveButton.BackColor = Graphite.Card;
        SaveButton.ForeColor = Graphite.Primary;
        SaveButton.Click += delegate { Close(); };
        Controls.Add(SaveButton);
        closeButton = new Button();
        closeButton.Text = "×";
        closeButton.AccessibleName = "Закрыть настройки";
        closeButton.FlatStyle = FlatStyle.Flat;
        closeButton.FlatAppearance.BorderColor = Graphite.Border;
        closeButton.BackColor = Graphite.Card;
        closeButton.ForeColor = Graphite.Primary;
        closeButton.Size = new Size(28, 24);
        closeButton.Left = 362;
        closeButton.Top = 6;
        closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeButton.Click += delegate { Close(); };
        titleBar.Controls.Add(closeButton);
        titleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        MinimumSize = new Size(360, 280);
        AutoScroll = false;
        contentPanel = new Panel();
        contentPanel.SetBounds(8, 64, SettingsContentWidth, 218);
        contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        contentPanel.AutoScroll = false;
        contentPanel.BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card;
        contentPanel.Paint += PaintModernCards;
        foreach (Control control in new List<Control>(pages.Keys))
        {
            control.Top = Math.Max(8, control.Top - 64);
            Controls.Remove(control);
            contentPanel.Controls.Add(control);
        }
        Controls.Add(contentPanel);
        contentPanel.SizeChanged += delegate { FitRows(); };
        AddLimitPageControls();
        FitRows();
        ShowPage(0);
        ResumeLayout(true);
        ApplyOwnPalette(this);
        StyleTabs();
        CaptureScaleBaseline(this);
        Resize += delegate { ModernTheme.ApplyWindowRegion(this); };
        ApplyWindowScale();
        ApplyInterfaceStyleRebuild();
    }

    public CheckBox Check(string key)
    {
        return checks[key];
    }

    private void AddQuotaKeyButton(string provider, string label, ref int top)
    {
        Label caption = new Label { Text = label, ForeColor = Graphite.Primary, BackColor = Graphite.Base };
        caption.SetBounds(RowInset, top + 3, 178, RowHeight);
        Button button = new Button { Text = "Настроить", FlatStyle = FlatStyle.Flat, BackColor = Graphite.Card, ForeColor = Graphite.Primary,
            Enabled = allowQuotaVault, AccessibleName = "Настроить ключ " + label };
        button.FlatAppearance.BorderColor = Graphite.Border;
        button.SetBounds(221, top, 132, 23);
        button.Click += delegate { WidgetQuotaKeys.OpenDialog(provider, allowQuotaVault, quotaKeyChanged); };
        ConfigureQuotaKeyContextMenu(button, provider, label);
        Controls.Add(caption); pages.Add(caption, page);
        Controls.Add(button); pages.Add(button, page);
        top += 26;
    }

    private void ConfigureQuotaKeyContextMenu(Button button, string provider, string label)
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem remove = new ToolStripMenuItem("Удалить ключ");
        remove.Enabled = allowQuotaVault;
        remove.AccessibleName = "Удалить ключ " + label;
        remove.Click += delegate { WidgetQuotaKeys.DeleteStoredKey(provider, quotaKeyChanged); };
        menu.Items.Add(remove);
        button.ContextMenuStrip = menu;
    }

    public void UpdateLimitProviders(List<Dictionary<string, object>> providers)
    {
        Dictionary<string, string> catalog = ProviderCatalog(providers);
        if (CatalogEquals(limitProviderCatalog, catalog)) return;
        int previousPage = page;
        page = 3;
        HashSet<string> wanted = new HashSet<string>(catalog.Keys);
        foreach (string key in checks.Keys.Where(key => key.StartsWith("limit-", StringComparison.Ordinal) && !wanted.Contains(key.Substring(6))).ToList())
        {
            Control control = checks[key]; contentPanel.Controls.Remove(control); pages.Remove(control); checks.Remove(key);
            scaleBounds.Remove(control); scaleFonts.Remove(control); control.Dispose();
        }
        float factor = WindowScaleFactor();
        int top = pages.Where(item => item.Value == 3).Select(item => scaleBounds.ContainsKey(item.Key)
            ? scaleBounds[item.Key].Bottom + 2 : (int)Math.Round(item.Key.Bottom / factor) + 2).DefaultIfEmpty(8).Max();
        foreach (KeyValuePair<string, string> provider in catalog)
        {
            string id = provider.Key;
            string label = provider.Value;
            CheckBox existing; if (checks.TryGetValue("limit-" + id, out existing)) { existing.Text = label; continue; }
            Add("limit-" + id, label, !value.HiddenLimitProviders.Contains(id), ref top);
            CaptureScaleBaseline(checks["limit-" + id]);
        }
        page = previousPage;
        limitProviderCatalog.Clear();
        foreach (KeyValuePair<string, string> provider in catalog) limitProviderCatalog[provider.Key] = provider.Value;
        FitRows();
        ShowPage(previousPage);
        ApplyWindowScale();
    }

    private static Dictionary<string, string> ProviderCatalog(List<Dictionary<string, object>> providers)
    {
        Dictionary<string, string> result = new Dictionary<string, string>();
        foreach (Dictionary<string, object> provider in providers ?? new List<Dictionary<string, object>>())
        {
            string id = Convert.ToString(provider.ContainsKey("id") ? provider["id"] : "");
            if (String.IsNullOrEmpty(id) || result.ContainsKey(id)) continue;
            result.Add(id, Convert.ToString(provider.ContainsKey("label") ? provider["label"] : id));
        }
        return result;
    }

    private static bool CatalogEquals(Dictionary<string, string> left, Dictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;
        foreach (KeyValuePair<string, string> item in left)
        {
            string label;
            if (!right.TryGetValue(item.Key, out label) || label != item.Value) return false;
        }
        return true;
    }

    public void RefreshTheme()
    {
        SyncAppearanceControls();
        refreshedDuringSave = true;
        ApplyInterfaceStyleRebuild();
        ApplyWindowScale();
    }

    internal void SyncAppearanceControls()
    {
        syncing = true;
        themeMode.SelectedIndex = ThemeIndex(value.Theme);
        interfaceStyle.SelectedIndex = value.NormalizedInterfaceStyle == "modern" ? 1 : 0;
        checks["show-limits"].Checked = value.ShowLimits;
        checks["compact-widget"].Checked = value.CompactWidget;
        syncing = false;
    }

    private static void ApplyOwnPalette(Control control)
    {
        Panel panel = control as Panel;
        if (panel != null) panel.BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card;
        Label label = control as Label;
        if (label != null)
        {
            label.BackColor = ModernTheme.Enabled && label.Name == "modern-page-title" ? ModernTheme.Surface : (ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card);
            label.ForeColor = label.Name == "settings-section" ? (ModernTheme.Enabled ? ModernTheme.Accent : Graphite.Accent("running")) : (ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary);
        }
        Button button = control as Button;
        if (button != null) { button.BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card; button.ForeColor = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary; button.FlatAppearance.BorderColor = ModernTheme.Enabled ? ModernTheme.CardBorder : Graphite.Border; }
        CheckBox check = control as CheckBox;
        if (check != null) { check.BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card; check.ForeColor = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary; }
        ComboBox combo = control as ComboBox;
        if (combo != null) { combo.BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card; combo.ForeColor = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary; }
        foreach (Control child in control.Controls) ApplyOwnPalette(child);
    }

    private void DragCaption(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }

    private void AddGeneralChoice(ComboBox combo, string name, string text, ref int top, Button action = null)
    {
        int right = SettingsContentWidth - RowInset;
        int width = right - GeneralValueLeft;
        Label label = new Label { Text = text, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        label.SetBounds(RowInset, top, GeneralValueLeft - RowInset * 2, GeneralRowHeight);
        combo.Name = name;
        combo.AccessibleName = text;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem += PaintChoice;
        combo.SetBounds(GeneralValueLeft, top + 1, action == null ? width : width - 36, GeneralRowHeight - 2);
        combo.SelectedIndexChanged += delegate { Save(); };
        Controls.Add(label); pages.Add(label, page);
        Controls.Add(combo); pages.Add(combo, page);
        if (action != null)
        {
            action.SetBounds(right - 30, top + 1, 30, GeneralRowHeight - 2);
            Controls.Add(action); pages.Add(action, page);
        }
        top += GeneralRowHeight;
    }

    private void Add(string key, string text, bool selected, ref int top, int rowHeight = RowHeight)
    {
        CheckBox check = new ModernToggle();
        check.Text = text;
        check.Checked = selected;
        check.SetBounds(RowInset, top, 1, rowHeight);
        check.TextAlign = ContentAlignment.MiddleLeft;
        check.CheckAlign = ContentAlignment.MiddleRight;
        if (key == "show-widget") check.AccessibleDescription = "Показывает список задач рядом с рабочим столом";
        if (key == "show-notices") check.AccessibleDescription = "Показывает уведомления о задачах";
        if (key == "widget-topmost") check.AccessibleDescription = "Удерживает виджет поверх других окон";
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
        top += rowHeight;
    }

    private void AddCaption(string text, ref int top)
    {
        ModernSectionLabel caption = new ModernSectionLabel();
        caption.Name = "settings-section";
        caption.Text = text;
        caption.HelpText = text == "Виджет" ? "Показывает задачи рядом с рабочим столом"
            : (text == "Уведомления" ? "Сообщает о важных изменениях задач" : "Настройка выбранного раздела");
        caption.TextAlign = ContentAlignment.MiddleLeft;
        caption.ForeColor = Graphite.Accent("running");
        caption.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
        caption.Left = RowInset;
        caption.Top = top;
        caption.Width = 280;
        caption.Height = 20;
        if (contentPanel == null) Controls.Add(caption); else contentPanel.Controls.Add(caption);
        pages[caption] = page;
        top += 22;
    }

    private void AddLimitPageControls()
    {
        previousLimitPage = new Button { Text = "←", AccessibleName = "Предыдущая страница лимитов", FlatStyle = FlatStyle.Flat };
        nextLimitPage = new Button { Text = "→", AccessibleName = "Следующая страница лимитов", FlatStyle = FlatStyle.Flat };
        limitPageCaption = new Label { TextAlign = ContentAlignment.MiddleCenter, ForeColor = Graphite.Secondary, BackColor = Graphite.Card };
        previousLimitPage.Click += delegate { if (limitProviderPage > 0) { limitProviderPage--; ShowPage(page); } };
        nextLimitPage.Click += delegate { limitProviderPage++; ShowPage(page); };
        contentPanel.Controls.Add(previousLimitPage);
        contentPanel.Controls.Add(nextLimitPage);
        contentPanel.Controls.Add(limitPageCaption);
        pages[previousLimitPage] = 3;
        pages[nextLimitPage] = 3;
        pages[limitPageCaption] = 3;
    }

    private int WorkingAreaHeight()
    {
        Rectangle area = WorkingArea();
        return Math.Max(1, area.Height - 16);
    }

    private Rectangle WorkingArea()
    {
        return layoutWorkingArea ?? Screen.GetWorkingArea(new Point(Left, Top));
    }

    private void FitToVisibleContent()
    {
        if (fittingContent || contentPanel == null || scaleBounds.Count == 0) return;
        fittingContent = true;
        try
        {
        float factor = WindowScaleFactor();
        int margin = Math.Max(6, (int)Math.Round(8 * factor));
        int footerGap = margin;
        int bottomMargin = Math.Max(8, (int)Math.Round(14 * factor));
        int maximumHeight = WorkingAreaHeight();
        int availableContentHeight = Math.Max(1, maximumHeight - contentPanel.Top - footerGap - SaveButton.Height - bottomMargin);
        foreach (Control control in new Control[] { previousLimitPage, nextLimitPage, limitPageCaption }) control.Visible = false;
        List<Control> controls = pages.Where(item => item.Value == page && item.Key != previousLimitPage
            && item.Key != nextLimitPage && item.Key != limitPageCaption).Select(item => item.Key).ToList();
        List<List<Control>> rows = new List<List<Control>>();
        int bandBottom = Int32.MinValue;
        foreach (Control control in controls.OrderBy(control => BaseBounds(control, factor).Top))
        {
            Rectangle bounds = BaseBounds(control, factor);
            if (rows.Count == 0 || bounds.Top >= bandBottom)
            {
                rows.Add(new List<Control>());
                bandBottom = bounds.Bottom;
            }
            else
            {
                bandBottom = Math.Max(bandBottom, bounds.Bottom);
            }
            rows[rows.Count - 1].Add(control);
        }
        int navigationHeight = Math.Max(1, (int)Math.Round(23 * factor));
        int usableHeight = Math.Max(1, availableContentHeight - navigationHeight - margin * 2);
        List<List<List<Control>>> contentPages = new List<List<List<Control>>>();
        List<List<Control>> currentPage = new List<List<Control>>();
        int used = 0;
        foreach (List<Control> row in rows)
        {
            int rowHeight = row.Select(control => MeasuredBounds(control, factor).Height).Max();
            int sectionGap = ModernTheme.Enabled && currentPage.Count > 0 && row.Any(control => control.Name == "settings-section")
                ? Math.Max(6, (int)Math.Round(12 * factor)) : 0;
            if (currentPage.Count > 0 && used + sectionGap + rowHeight > usableHeight)
            {
                contentPages.Add(currentPage);
                currentPage = new List<List<Control>>();
                used = 0;
                sectionGap = 0;
            }
            currentPage.Add(row);
            used += sectionGap + rowHeight;
        }
        if (currentPage.Count > 0) contentPages.Add(currentPage);
        if (contentPages.Count == 0) contentPages.Add(new List<List<Control>>());
        limitProviderPage = Math.Max(0, Math.Min(limitProviderPage, contentPages.Count - 1));
        foreach (Control control in controls) control.Visible = false;
        int top = margin;
        foreach (List<Control> row in contentPages[limitProviderPage])
        {
            if (ModernTheme.Enabled && top > margin && row.Any(control => control.Name == "settings-section"))
                top += Math.Max(6, (int)Math.Round(12 * factor));
            int sourceTop = row.Select(control => BaseBounds(control, factor).Top).Min();
            int rowHeight = row.Select(control => MeasuredBounds(control, factor).Height).Max();
            foreach (Control control in row)
            {
                Rectangle source = MeasuredBounds(control, factor);
                Rectangle baseBounds = BaseBounds(control, factor);
                control.SetBounds(source.Left, top + baseBounds.Top - sourceTop, source.Width, source.Height);
                control.Visible = true;
            }
            top += rowHeight + (ModernTheme.Enabled && row.Any(control => control.Name == "settings-section") ? Math.Max(3, (int)Math.Round(4 * factor)) : 0);
        }
        if (contentPages.Count > 1)
        {
            previousLimitPage.Visible = true;
            nextLimitPage.Visible = true;
            limitPageCaption.Visible = true;
            previousLimitPage.SetBounds(RowInset, top, 32, navigationHeight);
            nextLimitPage.SetBounds(Math.Max(RowInset + 36, contentPanel.ClientSize.Width - RowInset - 32), top, 32, navigationHeight);
            limitPageCaption.SetBounds(RowInset + 40, top + 2, Math.Max(1, contentPanel.ClientSize.Width - (RowInset + 40) * 2), navigationHeight - 4);
            previousLimitPage.Enabled = limitProviderPage > 0;
            nextLimitPage.Enabled = limitProviderPage + 1 < contentPages.Count;
            previousLimitPage.AccessibleName = page == 3 ? "Предыдущая страница лимитов" : "Предыдущая страница настроек";
            nextLimitPage.AccessibleName = page == 3 ? "Следующая страница лимитов" : "Следующая страница настроек";
            limitPageCaption.Text = "Страница " + (limitProviderPage + 1) + " из " + contentPages.Count;
            top += navigationHeight;
        }
        int contentHeight = Math.Min(availableContentHeight, Math.Max(1, top + margin));
        Rectangle workingArea = WorkingArea();
        MinimumSize = new Size(Math.Min(Math.Max(1, (int)Math.Round(360 * factor)), workingArea.Width), Math.Min(Math.Max(1, (int)Math.Round(280 * factor)), maximumHeight));
        int logicalWidth = ModernTheme.Enabled ? 550 : scaleBounds[this].Width;
        int width = Math.Min(Math.Max(1, (int)Math.Round(logicalWidth * factor)), workingArea.Width);
        int height = Math.Min(maximumHeight, contentPanel.Top + contentHeight + footerGap + SaveButton.Height + bottomMargin);
        if (ClientSize.Width != width || ClientSize.Height != height) ClientSize = new Size(width, height);
        if (Visible) Location = Geometry.ClampToVisible(Bounds, workingArea).Location;
        contentPanel.Height = contentHeight;
        SaveButton.Top = contentPanel.Bottom + footerGap;
        if (contentPages.Count > 1)
        {
            nextLimitPage.Left = Math.Max(RowInset + 36, contentPanel.ClientSize.Width - RowInset - nextLimitPage.Width);
            limitPageCaption.Width = Math.Max(1, nextLimitPage.Left - limitPageCaption.Left - 4);
        }
        FitRows();
        // The card borders are drawn by the parent, outside its child controls.
        // Invalidating only moved rows can leave old border fragments on screen.
        if (ModernTheme.Enabled) contentPanel.Invalidate();
        }
        finally
        {
            fittingContent = false;
        }
    }

    private void FitRows()
    {
        int inset = (int)Math.Round(RowInset * WindowScaleFactor());
        int right = (int)Math.Round((SettingsContentWidth - RowInset) * WindowScaleFactor());
        int width = Math.Max(1, Math.Min(right, contentPanel.ClientSize.Width - inset) - inset);
        foreach (CheckBox row in checks.Values) row.Width = width;
    }

    private void ShowPage(int selected)
    {
        if (page != selected) limitProviderPage = 0;
        page = selected;
        if (modernPageTitle != null && selected >= 0 && selected < tabButtons.Count) modernPageTitle.Text = tabButtons[selected].Text;
        StyleTabs();
        foreach (KeyValuePair<Control, int> item in pages) item.Key.Visible = item.Value == selected;
        FitToVisibleContent();
        EnsureVisibleControlsFit();
    }

    private void EnsureVisibleControlsFit()
    {
        foreach (Control control in contentPanel.Controls)
        {
            if (!control.Visible) continue;
            int width = Math.Min(control.Width, contentPanel.ClientSize.Width);
            int height = Math.Min(control.Height, contentPanel.ClientSize.Height);
            int left = Math.Max(0, Math.Min(control.Left, Math.Max(0, contentPanel.ClientSize.Width - width)));
            int top = Math.Max(0, Math.Min(control.Top, Math.Max(0, contentPanel.ClientSize.Height - height)));
            if (left != control.Left || top != control.Top || width != control.Width || height != control.Height)
                control.Bounds = new Rectangle(left, top, width, height);
        }
    }

    private void StyleTabs()
    {
        for (int index = 0; index < tabButtons.Count; index++)
        {
            bool selected = index == page;
            Button tab = tabButtons[index];
            tab.BackColor = selected ? (ModernTheme.Enabled ? ModernTheme.AccentSoft : Graphite.ChipFill("running")) : (ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card);
            tab.ForeColor = selected ? (ModernTheme.Enabled ? ModernTheme.Accent : Graphite.Accent("running")) : (ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary);
            tab.FlatAppearance.BorderColor = selected ? (ModernTheme.Enabled ? ModernTheme.Accent : Graphite.Accent("running")) : (ModernTheme.Enabled ? ModernTheme.CardBorder : Graphite.Border);
        }
    }

    private void Save()
    {
        if (syncing) return;
        refreshedDuringSave = false;
        bool wasCompactWorkspaceList = value.CompactWorkspaceList;
        string previousScale = value.WidgetScale;
        string previousStyle = value.NormalizedInterfaceStyle;
        value.ShowWidget = checks["show-widget"].Checked;
        value.ShowNotifications = checks["show-notices"].Checked;
        value.HideCompletedWhenHarnessOpen = checks["hide-completed-harness-open"].Checked;
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
        value.Theme = ThemeMode();
        if (value.Theme == "light") value.ThemeDark = false;
        if (value.Theme == "dark") value.ThemeDark = true;
        value.InterfaceStyle = interfaceStyle.SelectedIndex == 1 ? "modern" : "classic";
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
        bool styleChanged = value.NormalizedInterfaceStyle != previousStyle;
        if (!refreshedDuringSave && (styleChanged || value.WidgetScale != previousScale))
        {
            if (styleChanged) ApplyInterfaceStyleRebuild();
            ApplyWindowScale();
        }
    }

    public void ApplyInterfaceStyleRebuild()
    {
        ModernTheme.Apply(value.NormalizedInterfaceStyle == "modern", Graphite.IsDark);
        ApplyInterfaceStyleGeometry(WindowScaleFactor());
        BackColor = ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base;
        ApplyOwnPalette(this);
        StyleTabs();
        EnsureVisibleControlsFit();
        Invalidate(true);
    }

    private void ApplyInterfaceStyleGeometry(float factor)
    {
        bool modern = ModernTheme.Enabled;
        int shift = modern ? (int)Math.Round(152 * factor) : 0;
        int logicalWidth = (int)Math.Round((modern ? 550 : 398) * factor);
        Rectangle area = WorkingArea();
        ClientSize = new Size(Math.Min(area.Width, Math.Max(1, logicalWidth)), ClientSize.Height);
        titleBar.Width = ClientSize.Width;
        closeButton.Left = Math.Max(0, ClientSize.Width - closeButton.Width - (int)Math.Round(8 * factor));
        SaveButton.Left = Math.Max(0, ClientSize.Width - SaveButton.Width - (int)Math.Round(18 * factor));
        if (modern)
        {
            navPanel.SetBounds((int)Math.Round(12 * factor), (int)Math.Round(52 * factor), (int)Math.Round(142 * factor), Math.Max(1, ClientSize.Height - (int)Math.Round(64 * factor)));
            navPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            for (int index = 0; index < tabButtons.Count; index++)
                tabButtons[index].SetBounds(0, (int)Math.Round(index * 34 * factor), navPanel.Width, Math.Max(22, (int)Math.Round(30 * factor)));
            modernPageTitle.Visible = true;
            modernPageTitle.ForeColor = ModernTheme.TextPrimary;
            modernPageTitle.BackColor = ModernTheme.Surface;
            modernPageTitle.SetBounds(shift + (int)Math.Round(12 * factor), (int)Math.Round(46 * factor), Math.Max(1, ClientSize.Width - shift - (int)Math.Round(24 * factor)), (int)Math.Round(24 * factor));
            contentPanel.Left = shift + (int)Math.Round(8 * factor);
            contentPanel.Top = (int)Math.Round(76 * factor);
            contentPanel.Width = Math.Max(1, ClientSize.Width - contentPanel.Left - (int)Math.Round(8 * factor));
        }
        else
        {
            navPanel.SetBounds((int)Math.Round(8 * factor), (int)Math.Round(36 * factor), Math.Min(ClientSize.Width - (int)Math.Round(16 * factor), (int)Math.Round(SettingsContentWidth * factor)), (int)Math.Round(28 * factor));
            navPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            for (int index = 0; index < tabButtons.Count; index++)
                tabButtons[index].SetBounds((int)Math.Round(index * 96 * factor), (int)Math.Round(factor), (int)Math.Round(92 * factor), Math.Max(1, (int)Math.Round(25 * factor)));
            modernPageTitle.Visible = false;
            contentPanel.Left = (int)Math.Round(8 * factor);
            contentPanel.Top = (int)Math.Round(64 * factor);
            contentPanel.Width = Math.Min(ClientSize.Width - (int)Math.Round(16 * factor), (int)Math.Round(SettingsContentWidth * factor));
        }
        contentPanel.Invalidate();
        if (!fittingContent) FitToVisibleContent();
        ModernTheme.ApplyWindowRegion(this);
    }

    private void PaintModernCards(object sender, PaintEventArgs args)
    {
        if (!ModernTheme.Enabled) return;
        List<Control> visible = pages.Where(item => item.Value == page && item.Key.Visible).Select(item => item.Key).ToList();
        if (visible.Count == 0) return;
        List<Label> sections = visible.OfType<Label>().Where(label => label.Name == "settings-section").OrderBy(label => label.Top).ToList();
        if (sections.Count == 0)
        {
            int top = Math.Max(2, visible.Min(control => control.Top) - 4);
            int bottom = Math.Min(contentPanel.ClientSize.Height - 2, visible.Max(control => control.Bottom) + 4);
            ModernTheme.PaintCard(args.Graphics, new Rectangle(2, top, Math.Max(1, contentPanel.ClientSize.Width - 4), Math.Max(1, bottom - top)));
            return;
        }
        for (int index = 0; index < sections.Count; index++)
        {
            Label section = sections[index];
            int next = index + 1 < sections.Count ? sections[index + 1].Top - 7 : contentPanel.ClientSize.Height - 3;
            List<Control> group = visible.Where(control => control.Top >= section.Top && control.Top < next).ToList();
            int bottom = group.Count == 0 ? section.Bottom + 3 : Math.Min(next - 3, group.Max(control => control.Bottom) + 3);
            ModernTheme.PaintCard(args.Graphics, new Rectangle(2, Math.Max(2, section.Top - 4), Math.Max(1, contentPanel.ClientSize.Width - 4), Math.Max(1, bottom - section.Top + 4)));
        }
    }

    private readonly Dictionary<Control, Rectangle> scaleBounds = new Dictionary<Control, Rectangle>();
    private readonly Dictionary<Control, float> scaleFonts = new Dictionary<Control, float>();
    private void CaptureScaleBaseline(Control control)
    {
        if (!scaleBounds.ContainsKey(control)) scaleBounds.Add(control, control.Bounds);
        if (!scaleFonts.ContainsKey(control)) scaleFonts.Add(control, control.Font.SizeInPoints);
        foreach (Control child in control.Controls) CaptureScaleBaseline(child);
    }

    private float WindowScaleFactor()
    {
        float requested = value.WidgetScale == "small" ? .85F : (value.WidgetScale == "large" ? 1.25F : 1F);
        int width = ModernTheme.Enabled ? 550 : 398;
        return Math.Min(requested, Math.Max(.5F, (float)WorkingArea().Width / width));
    }

    private static Rectangle ScaleRectangle(Rectangle rectangle, float factor)
    {
        int left = (int)Math.Round(rectangle.Left * factor);
        int top = (int)Math.Round(rectangle.Top * factor);
        return new Rectangle(left, top, Math.Max(1, (int)Math.Round(rectangle.Right * factor) - left),
            Math.Max(1, (int)Math.Round(rectangle.Bottom * factor) - top));
    }

    private Rectangle BaseBounds(Control control, float factor)
    {
        return ScaleRectangle(scaleBounds[control], factor);
    }

    private Rectangle MeasuredBounds(Control control, float factor)
    {
        Rectangle bounds = BaseBounds(control, factor);
        if (ModernTheme.Enabled && control.Name == "settings-section")
            bounds.Height = Math.Max(bounds.Height, (int)Math.Round(36 * factor));
        return bounds;
    }

    private void ApplyWindowScale()
    {
        if (scaleBounds.Count == 0) return;
        float factor = WindowScaleFactor();
        foreach (Control disposed in scaleBounds.Keys.Where(control => control.IsDisposed).ToList())
        {
            scaleBounds.Remove(disposed);
            scaleFonts.Remove(disposed);
        }
        Rectangle root = scaleBounds[this];
        MinimumSize = new Size(Math.Max(1, (int)Math.Round(360 * factor)), Math.Max(1, (int)Math.Round(280 * factor)));
        int targetWidth = ModernTheme.Enabled ? (int)Math.Round(550 * factor) : (int)Math.Round(root.Width * factor);
        ClientSize = new Size(Math.Max(1, targetWidth), Math.Max(1, (int)Math.Round(root.Height * factor)));
        foreach (KeyValuePair<Control, Rectangle> item in scaleBounds.Where(item => item.Key != this && !item.Key.IsDisposed)
            .OrderBy(item => ControlDepth(item.Key)).ToList())
        {
            Control control = item.Key;
            Rectangle target = ScaleRectangle(item.Value, factor);
            if (control.Bounds != target) control.Bounds = target;
            float size = scaleFonts[control];
            float targetSize = size * factor;
            if (Math.Abs(control.Font.SizeInPoints - targetSize) > .01F)
                control.Font = new Font(control.Font.FontFamily, targetSize, control.Font.Style);
        }
        FitToVisibleContent();
        ApplyInterfaceStyleGeometry(factor);
    }

    private static int ControlDepth(Control control)
    {
        int depth = 0;
        while ((control = control.Parent) != null) depth++;
        return depth;
    }

    private static int ThemeIndex(string theme)
    {
        return theme == "light" ? 1 : (theme == "dark" ? 2 : 0);
    }

    private string ThemeMode()
    {
        return themeMode.SelectedIndex == 1 ? "light" : (themeMode.SelectedIndex == 2 ? "dark" : "system");
    }

    private string SelectedSound()
    {
        return soundPreset.SelectedIndex < 0 ? NotificationSounds.DefaultId : NotificationSounds.Presets[soundPreset.SelectedIndex].Key;
    }

    private void PaintChoice(object sender, DrawItemEventArgs args)
    {
        bool highlighted = (args.State & (DrawItemState.Selected | DrawItemState.ComboBoxEdit)) == DrawItemState.Selected;
        using (Brush fill = new SolidBrush(highlighted ? Graphite.Border : Graphite.Card))
            args.Graphics.FillRectangle(fill, args.Bounds);
        if (args.Index < 0) return;
        Rectangle text = Rectangle.Inflate(args.Bounds, -4, 0);
        ComboBox combo = (ComboBox)sender;
        TextRenderer.DrawText(args.Graphics, combo.GetItemText(combo.Items[args.Index]), args.Font, text,
            Graphite.Primary, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }

    private sealed class SettingsChoice : ComboBox
    {
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            const int PaintMessage = 0x000F;
            const int PrintMessage = 0x0317;
            const int PrintClientMessage = 0x0318;
            if (message.Msg == PaintMessage && IsHandleCreated)
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle)) PaintFrame(graphics);
            }
            else if ((message.Msg == PrintMessage || message.Msg == PrintClientMessage) && message.WParam != IntPtr.Zero)
            {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) PaintFrame(graphics);
            }
        }

        private void PaintFrame(Graphics graphics)
        {
            Rectangle area = ClientRectangle;
            if (area.Width < 4 || area.Height < 4) return;
            int arrowWidth = Math.Min(area.Width - 3, SystemInformation.VerticalScrollBarWidth);
            var arrow = new Rectangle(area.Right - arrowWidth - 1, 1, arrowWidth, area.Height - 2);
            using (Brush fill = new SolidBrush(Graphite.Base)) graphics.FillRectangle(fill, arrow);
            int x = arrow.Left + arrow.Width / 2;
            int y = arrow.Top + arrow.Height / 2;
            using (Pen mark = new Pen(Graphite.Secondary))
                graphics.DrawLines(mark, new Point[] { new Point(x - 3, y - 1), new Point(x, y + 2), new Point(x + 3, y - 1) });
            using (Pen border = new Pen(Focused ? Graphite.Accent("running") : Graphite.Border))
            {
                graphics.DrawRectangle(border, 0, 0, area.Width - 1, area.Height - 1);
                graphics.DrawRectangle(border, 1, 1, area.Width - 3, area.Height - 3);
                graphics.DrawLine(border, arrow.Left - 1, 1, arrow.Left - 1, area.Height - 2);
            }
        }
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

    protected override void OnResize(EventArgs args)
    {
        base.OnResize(args);
        ModernTheme.ApplyWindowRegion(this);
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
    private const int BadgeSize = 18;
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
    private int PaintedRowHeight { get { return Math.Max(BadgeSize, Px(RowHeight)); } }
    private int ProviderGap { get { return Px(3); } }
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
            lines += HasCurrentData(provider) ? ValueList(provider, "windows").Count + Metrics(provider).Count : 1;
        Height = Math.Max(Px(22), Px(FirstRowTop) + lines * PaintedRowHeight + Math.Max(0, providers.Count - 1) * ProviderGap);
        AccessibleDescription = String.Join(", ", providers.Select(ProviderName).ToArray());
        Invalidate();
    }

    public int DesiredHeight { get { return Height; } }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.Clear(ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base);
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
            int providerLeft = Math.Max(Px(ProviderTextLeft), Px(6) + BadgeSize + 3);
            int top = Px(FirstRowTop);
            int rowHeight = PaintedRowHeight;
            bool firstProvider = true;
            foreach (Dictionary<string, object> provider in providers)
            {
                if (!firstProvider)
                {
                    using (Pen divider = new Pen(Graphite.Border)) graphics.DrawLine(divider, Px(6), top + 1, Px(Math.Max(6, width - 6)), top + 1);
                    top += ProviderGap;
                }
                firstProvider = false;
                Image badge = ProviderBadge(Field(provider, "id"), ProviderName(provider));
                if (badge != null) graphics.DrawImage(badge, new Rectangle(Px(6), top + (rowHeight - BadgeSize) / 2, BadgeSize, BadgeSize));
                TextRenderer.DrawText(graphics, ProviderName(provider), detail,
                    new Rectangle(providerLeft, top, Math.Max(1, Px(dataLeft - 5) - providerLeft), rowHeight),
                    Graphite.Primary, CompactText | TextFormatFlags.VerticalCenter);
                if (!HasCurrentData(provider))
                {
                    TextRenderer.DrawText(graphics, DeepSeekReason(provider), detail, new Rectangle(Px(dataLeft), top, Px(dataWidth), rowHeight),
                        Graphite.Secondary, CompactText | TextFormatFlags.VerticalCenter);
                    top += rowHeight;
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
                        new Rectangle(Px(dataLeft), top, Px(dataWidth), rowHeight - Px(4)), Graphite.Primary, CompactText);
                    DrawTrack(graphics, dataLeft, dataWidth, top + rowHeight, percent);
                    top += rowHeight;
                }
                foreach (LimitMetric metric in Metrics(provider, true))
                {
                    int textHeight = metric.HasTrack ? rowHeight - Px(4) : rowHeight;
                    TextFormatFlags flags = metric.HasTrack ? CompactText : CompactText | TextFormatFlags.VerticalCenter;
                    TextRenderer.DrawText(graphics, metric.Caption, detail, new Rectangle(Px(dataLeft), top, Px(dataWidth), textHeight),
                        Graphite.Primary, flags);
                    if (metric.HasTrack) DrawTrack(graphics, dataLeft, dataWidth, top + rowHeight, metric.Percent);
                    top += rowHeight;
                }
            }
        }
    }

    private void DrawTrack(Graphics graphics, int left, int width, int rowBottom, double? percent)
    {
        Rectangle track = new Rectangle(Px(left), rowBottom - Px(4), Px(Math.Min(70, width)), Px(3));
        if (!percent.HasValue)
        {
            using (Brush hatch = new HatchBrush(HatchStyle.LightDownwardDiagonal, Graphite.Border, Graphite.Base))
                graphics.FillRectangle(hatch, track);
            return;
        }
        using (Brush trackBrush = new SolidBrush(Graphite.Border)) graphics.FillRectangle(trackBrush, track);
        double value = percent.Value;
        Color tier = value < 25 ? Graphite.QuotaLow : (value < 60 ? Graphite.QuotaMedium : Graphite.QuotaHigh);
        using (Brush fill = new SolidBrush(tier)) graphics.FillRectangle(fill, track.X, track.Y, (int)(track.Width * value / 100.0), track.Height);
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
    private sealed class LimitMetric
    {
        public readonly string Caption;
        public readonly bool HasTrack;
        public readonly double? Percent;

        public LimitMetric(string caption, double? percent = null, bool hasTrack = true)
        {
            Caption = caption;
            Percent = percent;
            HasTrack = hasTrack;
        }

        public string Hint
        {
            get
            {
                if (!HasTrack) return Caption;
                return Caption + " · " + (Percent.HasValue ? "осталось " + Math.Round(Percent.Value) + "%" : "Лимит не указан");
            }
        }
    }

    private static List<LimitMetric> Metrics(Dictionary<string, object> provider, bool compact = false)
    {
        List<LimitMetric> values = new List<LimitMetric>();
        foreach (Dictionary<string, object> amount in ValueList(provider, "amounts"))
        {
            string balance = Field(amount, "balance"); string currency = Field(amount, "currency");
            if (balance.Length > 0 && currency.Length > 0)
                values.Add(new LimitMetric((Field(amount, "kind") == "bonus" ? "Бонус " : "Баланс ") + DisplayAmount(balance, compact) + " " + currency));
        }
        string unlimited = NestedField(provider, "credits", "unlimited");
        string credits = NestedField(provider, "credits", "balance");
        if (unlimited == "True") values.Add(new LimitMetric("Кредиты без лимита", hasTrack: false));
        else if (credits.Length > 0) values.Add(new LimitMetric("Кредиты " + DisplayAmount(credits, compact)));
        string remaining = NestedField(provider, "individualLimit", "remaining");
        if (remaining.Length > 0)
            values.Add(new LimitMetric((compact ? "Бюджет " : "Остаток лимита расходов ") + DisplayAmount(remaining, compact),
                SpendingPercent(provider, remaining)));
        return values;
    }

    private static double? SpendingPercent(Dictionary<string, object> provider, string remaining)
    {
        double percent;
        if (Double.TryParse(NestedField(provider, "individualLimit", "remainingPercent"), out percent) && percent >= 0 && percent <= 100)
            return percent;
        decimal current, limit;
        const NumberStyles numberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!Decimal.TryParse(remaining, numberStyle, CultureInfo.InvariantCulture, out current) ||
            !Decimal.TryParse(NestedField(provider, "individualLimit", "limit"), numberStyle, CultureInfo.InvariantCulture, out limit) ||
            limit <= 0) return null;
        return (double)(Math.Min(limit, Math.Max(0, current)) / limit * 100);
    }

    private static bool ProviderAvailable(Dictionary<string, object> provider)
    {
        return String.Equals(Field(provider, "state"), "ok", StringComparison.Ordinal)
            && (ValueList(provider, "windows").Count > 0 || Metrics(provider).Count > 0);
    }
    private bool HasCurrentData(Dictionary<string, object> provider)
    {
        return receivedAt != DateTime.MinValue && (DateTime.UtcNow - receivedAt).TotalMinutes <= 10 && ProviderAvailable(provider);
    }
    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        int y = eventArgs.Y;
        int visualTop = Px(FirstRowTop);
        int rowHeight = PaintedRowHeight;
        bool firstProvider = true;
        foreach (Dictionary<string, object> provider in providers)
        {
            if (!firstProvider && y >= visualTop && y < visualTop + ProviderGap) { hints.SetToolTip(this, String.Empty); return; }
            if (!firstProvider) visualTop += ProviderGap;
            firstProvider = false;
            if (!HasCurrentData(provider))
            {
                if (y >= visualTop && y < visualTop + rowHeight)
                {
                    hints.SetToolTip(this, ProviderName(provider) + " · " + DeepSeekReason(provider));
                    return;
                }
                visualTop += rowHeight;
                continue;
            }
            List<Dictionary<string, object>> windows = ValueList(provider, "windows");
            List<LimitMetric> metrics = Metrics(provider);
            int count = Math.Max(1, windows.Count + metrics.Count);
            if (y >= visualTop && y < visualTop + count * rowHeight)
            {
                int local = (y - visualTop) / rowHeight;
                string detail;
                if (local < windows.Count)
                {
                    Dictionary<string, object> window = windows[local];
                    string reset = HasNumber(window, "resetAt") ? Countdown(ToLong(window, "resetAt")) : "срок неизвестен";
                    detail = Field(window, "label") + " " + Math.Round(ToDouble(window["percent"])) + "% · " + reset;
                }
                else if (local - windows.Count < metrics.Count) detail = metrics[local - windows.Count].Hint;
                else detail = "Данные недоступны";
                hints.SetToolTip(this, ProviderName(provider) + " · " + detail);
                return;
            }
            visualTop += count * rowHeight;
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

internal static class HarnessWindowProbe
{
    private const string HarnessProcessName = "DeepSeek Harness";
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaCloaked = 14;
    private const int RestoreWindow = 9;
    private const uint ProcessSnapshot = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr DefaultHeap;
        public uint ModuleId, Threads, ParentId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Executable;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry entry);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr window, int command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    internal static bool IsHarnessWindowOpen()
    {
        try
        {
            HashSet<uint> candidates = CandidateProcessIds();
            if (candidates.Count == 0) return false;
            bool found = false;
            if (!EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                if (candidates.Contains(processId) && IsTopLevelVisibleWindow(window)) found = true;
                return true;
            }, IntPtr.Zero)) return false;
            return found;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryActivateDesktop()
    {
        uint desktop;
        UInt32.TryParse(Environment.GetEnvironmentVariable("DSH_WN_DESKTOP_PID"), out desktop);
        HashSet<uint> candidates = CandidateProcessIds();
        if (!candidates.Contains(desktop)) desktop = AddLaunchParents(candidates);
        foreach (uint id in candidates)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)id))
                {
                    if (!String.Equals(process.ProcessName, HarnessProcessName, StringComparison.Ordinal)) continue;
                    IntPtr window = process.MainWindowHandle;
                    uint owner;
                    if (window != IntPtr.Zero && GetWindowThreadProcessId(window, out owner) != 0 &&
                        owner == id && IsTopLevelVisibleWindow(window, true))
                    {
                        if (IsIconic(window)) ShowWindowAsync(window, RestoreWindow);
                        if (SetForegroundWindow(window)) return true;
                    }
                    // Let the protocol activate a desktop window hidden to the tray.
                    if (id == desktop) AllowSetForegroundWindow(id);
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return false;
    }

    private static uint AddLaunchParents(HashSet<uint> candidates)
    {
        // An already-running host may still use a launcher that supplied no desktop PID.
        uint helper;
        using (Process current = Process.GetCurrentProcess()) helper = (uint)current.Id;
        Dictionary<uint, uint> parents = new Dictionary<uint, uint>();
        using (var snapshot = CreateToolhelp32Snapshot(ProcessSnapshot, 0))
        {
            if (snapshot.IsInvalid) return 0;
            ProcessEntry entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
            if (!Process32FirstW(snapshot, ref entry)) return 0;
            do
            {
                if (entry.Id == helper || String.Equals(entry.Executable, HarnessProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
                    parents[entry.Id] = entry.ParentId;
            } while (Process32NextW(snapshot, ref entry));
        }
        uint host, desktop;
        if (!parents.TryGetValue(helper, out host) || !parents.TryGetValue(host, out desktop)) return 0;
        candidates.Add(host);
        if (!parents.ContainsKey(desktop)) return host;
        candidates.Add(desktop);
        return desktop;
    }

    private static HashSet<uint> CandidateProcessIds()
    {
        HashSet<uint> result = new HashSet<uint>();
        AddCandidate(result, Environment.GetEnvironmentVariable("DSH_WN_HOST_PID"));
        AddCandidate(result, Environment.GetEnvironmentVariable("DSH_WN_DESKTOP_PID"));
        return result;
    }

    private static void AddCandidate(HashSet<uint> result, string value)
    {
        int processId;
        Process process = null;
        if (!Int32.TryParse(value, out processId) || processId <= 0) return;
        try
        {
            process = Process.GetProcessById(processId);
            if (String.Equals(process.ProcessName, HarnessProcessName, StringComparison.Ordinal)) result.Add((uint)processId);
        }
        catch
        {
        }
        finally
        {
            if (process != null) process.Dispose();
        }
    }

    private static bool IsTopLevelVisibleWindow(IntPtr window, bool includeMinimized = false)
    {
        if (!IsWindowVisible(window) || (!includeMinimized && IsIconic(window)) || GetWindow(window, GwOwner) != IntPtr.Zero) return false;
        if ((GetWindowLong(window, GwlExStyle) & WsExToolWindow) != 0) return false;
        try
        {
            int cloaked;
            if (DwmGetWindowAttribute(window, DwmwaCloaked, out cloaked, Marshal.SizeOf(typeof(int))) == 0 && cloaked != 0) return false;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        return true;
    }
}

public sealed class DshNotifyGui : Form
{
    private const string DshOpenUrl = "dsh://open";
    private const int WidgetWidth = 248;
    private const int WidgetHeaderHeight = 30;
    private const int CompactContentHeight = 85;
    private const int ExpandedHeight = 320;
    private const int ResizeGripHeight = 6;
    private const int DefaultQuotaHeight = 120;
    private const int NoticeStackWidth = 352;
    private const int NoticeStackHeightLimit = 420;
    private const int HarnessProbeIntervalMilliseconds = 500;
    private const int NonClientLeftButtonDown = 0x00A1;
    private readonly Action<string> playNoticeSound;
    private readonly Func<bool> harnessOpenProbe;
    private readonly Timer harnessNoticeTimer = new Timer();
    private const int CaptionHit = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr update, IntPtr region, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ShowWindow(IntPtr window, int command);

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
    private bool resizeDragHasFit;
    private int resizeFitHeight;
    private readonly Button compactToggle = new Button();
    private readonly Dictionary<string, WidgetRow> widgetRows = new Dictionary<string, WidgetRow>();
    private readonly Dictionary<string, Dictionary<string, object>> notices =
        new Dictionary<string, Dictionary<string, object>>();
    private readonly List<string> noticeOrder = new List<string>();
    private readonly List<SettingsForm> settingsForms = new List<SettingsForm>();
    private readonly List<Button> headerButtons = new List<Button>();
    private FlowLayoutPanel headerActions;
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
    private bool completedNoticesHiddenByHarness;
    private bool themeApplied;
    internal int NoticeRenderCountForTest { get; private set; }
    internal bool HarnessNoticeTimerEnabledForTest { get { return harnessNoticeTimer.Enabled; } }
    private bool isShuttingDown;
    private bool lastShowWidget = true;
    private bool lastShowNotifications = true;
    private readonly bool demoMode;
    private List<Dictionary<string, object>> limitProviders = new List<Dictionary<string, object>>();
    private List<Dictionary<string, object>> availableLimitProviders = new List<Dictionary<string, object>>();
    private DateTime limitsReceivedAt = DateTime.MinValue;
    private readonly TelegramPublisher telegramPublisher;
    private TelegramCredentials telegramSettings;
    private string lastTelegramData;
    private DateTime lastTelegramSourceAt = DateTime.MinValue;

    private const int SettingChangeMessage = 0x001A;
    private const int SetRedrawMessage = 0x000B;
    private const int HideWindowCommand = 0;
    private const uint RedrawFrameAndChildren = 0x0400U | 0x0080U | 0x0001U | 0x0004U;

    private float WidgetScaleFactor { get { return Settings != null && Settings.WidgetScale == "small" ? .85F : (Settings != null && Settings.WidgetScale == "large" ? 1.25F : 1F); } }
    private static void NormalizeTheme(Settings settings)
    {
        if (settings.Theme != "system" && settings.Theme != "light" && settings.Theme != "dark")
            settings.Theme = settings.ThemeDark ? "dark" : "light";
        if (settings.Theme == "light") settings.ThemeDark = false;
        if (settings.Theme == "dark") settings.ThemeDark = true;
    }

    private static bool WindowsThemeDark()
    {
        try
        {
            object setting = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return setting is int && (int)setting == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool EffectiveThemeDark(Settings settings)
    {
        return settings.Theme == "system" ? WindowsThemeDark() : settings.ThemeDark;
    }

    private void ApplyEffectiveTheme()
    {
        if (!ThemeVisualsChanged())
        {
            foreach (SettingsForm form in settingsForms) form.SyncAppearanceControls();
            return;
        }
        Graphite.ApplyTheme(EffectiveThemeDark(Settings));
        ModernTheme.Apply(Settings != null && Settings.NormalizedInterfaceStyle == "modern", Graphite.IsDark);
        ApplyTheme();
        themeApplied = true;
    }
    private int Px(int value) { return (int)Math.Round(value * WidgetScaleFactor); }
    private Font WidgetFont(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size * WidgetScaleFactor, style); }
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
        : this(demo, callback, sound, null)
    {
    }

    internal DshNotifyGui(bool demo, Action<string> outputCallback, Action<string> sound, Func<bool> harnessOpenProbe, bool enableTelegram = true)
    {
        demoMode = demo;
        playNoticeSound = sound;
        if (harnessOpenProbe != null) this.harnessOpenProbe = harnessOpenProbe;
        else if (demo) this.harnessOpenProbe = delegate { return false; };
        else this.harnessOpenProbe = HarnessWindowProbe.IsHarnessWindowOpen;
        this.outputCallback = outputCallback;
        if (this.outputCallback == null)
        {
            output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.AutoFlush = true;
        }
        Settings = new Settings();
        telegramPublisher = new TelegramPublisher(new TelegramTransport(),
            delegate(Action action) { System.Threading.ThreadPool.QueueUserWorkItem(delegate { action(); }); },
            delegate(Action action) { try { BeginInvoke(action); } catch { } },
            delegate { return DateTime.UtcNow; }, TelegramCredentialStore.Save);
        if (!demoMode && enableTelegram)
        {
            TelegramCredentialStore.TryRead(out telegramSettings);
            telegramPublisher.Load(telegramSettings);
        }
        harnessNoticeTimer.Interval = HarnessProbeIntervalMilliseconds;
        harnessNoticeTimer.Tick += delegate { RefreshHarnessNoticeSuppressionFromTimer(); };
        Text = (demo ? "[ДЕМО] " : "") + "Задачи";
        Icon = TaskIcon.Window;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ResizeRedraw = true;
        BackColor = Graphite.Base;
        ClientSize = new Size(WidgetWidth, WidgetHeaderHeight + CompactContentHeight);
        Font = new Font("Segoe UI", 9F);
        Panel header = new Panel();
        headerPanel = header;
        header.Height = WidgetHeaderHeight;
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
        headerTitle.SetBounds(30, 0, 1, WidgetHeaderHeight);
        headerTitle.TextAlign = ContentAlignment.MiddleLeft;
        headerTitle.AutoEllipsis = true;
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
        themeButton.Click += delegate
        {
            bool dark = EffectiveThemeDark(Settings);
            Settings.Theme = dark ? "light" : "dark";
            Settings.ThemeDark = !dark;
            UserChanged(Settings);
        };
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
            CaptureResizeFitHeight();
            resizeGrip.Capture = true;
        };
        resizeGrip.MouseMove += delegate(object sender, MouseEventArgs args)
        {
            if (!resizeGrip.Capture || args.Button != MouseButtons.Left) return;
            ResizeFromDrag(resizeStartHeight + Control.MousePosition.Y - resizeStartY);
        };
        resizeGrip.MouseUp += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left) resizeGrip.Capture = false;
        };
        Controls.Add(resizeGrip);
        compactToggle.Text = "›";
        compactToggle.Size = new Size(26, 24);
        StyleHeaderButton(compactToggle);
        headerButtons.Add(compactToggle);
        compactToggle.AccessibleName = "Развернуть список";
        compactToggle.Click += delegate { ToggleCompactMode(); };
        Button hideButton = new Button();
        hideButton.Text = "×";
        hideButton.AccessibleName = "Скрыть виджет";
        hideButton.Size = new Size(26, 24);
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
            headerTitle.Width = Math.Max(1, header.ClientSize.Width - headerActions.Width - headerTitle.Left - 4);
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
        menu.Items.Add("Telegram", null, delegate
        {
            if (!demoMode) TelegramSettingsUi.Open(this, Settings.ShowNotifications && Settings.StatusError, telegramPublisher, TelegramChanged, WidgetScaleFactor);
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

    internal static void DragWindow(Form window, object sender, MouseEventArgs eventArgs)
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
        if (message.Msg == SettingChangeMessage && headerPanel != null && Settings.Theme == "system" &&
            EffectiveThemeDark(Settings) != Graphite.IsDark)
        {
            ApplyEffectiveTheme();
            Render();
        }
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
            harnessNoticeTimer.Stop();
            harnessNoticeTimer.Dispose();
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
        NormalizeTheme(Settings);
        TopMost = Settings.WidgetTopMost;
        compact.WidgetZoom = WidgetScaleFactor;
        limits.WidgetZoom = WidgetScaleFactor;
        resizeGrip.Height = Px(ResizeGripHeight);
        Action apply = delegate
        {
            ApplyEffectiveTheme();
            lastShowWidget = Settings.ShowWidget;
            lastShowNotifications = Settings.ShowNotifications;
            SettingsApplier.ApplyLocation(this, Settings.WidgetX, Settings.WidgetY);
            SettingsApplier.ApplyLocation(stack, Settings.NoticeX, Settings.NoticeY);
            Render();
        };
        if (ThemeVisualsChanged()) RunStyleTransition(apply);
        else apply();
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
        NormalizeTheme(Settings);
        TopMost = Settings.WidgetTopMost;
        compact.WidgetZoom = WidgetScaleFactor;
        limits.WidgetZoom = WidgetScaleFactor;
        resizeGrip.Height = Px(ResizeGripHeight);
        if (ThemeVisualsChanged()) RunStyleTransition(delegate { ApplyEffectiveTheme(); Render(); });
        else { ApplyEffectiveTheme(); Render(); }
        lastShowWidget = Settings.ShowWidget;
        lastShowNotifications = Settings.ShowNotifications;
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

    private void RunStyleTransition(Action action)
    {
        List<KeyValuePair<Form, IntPtr>> visibleForms = new List<KeyValuePair<Form, IntPtr>>();
        foreach (Form form in new Form[] { this, stack }.Concat(settingsForms.Cast<Form>()))
        {
            if (form != null && !form.IsDisposed && form.IsHandleCreated && !form.InvokeRequired && form.Visible)
                visibleForms.Add(new KeyValuePair<Form, IntPtr>(form, form.Handle));
        }
        try
        {
            foreach (KeyValuePair<Form, IntPtr> form in visibleForms) SendMessage(form.Value, SetRedrawMessage, IntPtr.Zero, IntPtr.Zero);
            action();
        }
        finally
        {
            foreach (KeyValuePair<Form, IntPtr> form in visibleForms)
            {
                if (form.Key.IsDisposed || !form.Key.IsHandleCreated || form.Key.Handle != form.Value) continue;
                SendMessage(form.Value, SetRedrawMessage, new IntPtr(1), IntPtr.Zero);
                if (!form.Key.Visible) ShowWindow(form.Value, HideWindowCommand);
                else RedrawWindow(form.Value, IntPtr.Zero, IntPtr.Zero, RedrawFrameAndChildren);
            }
        }
    }

    private bool ThemeVisualsChanged()
    {
        return !themeApplied || ModernTheme.Enabled != (Settings.NormalizedInterfaceStyle == "modern")
            || Graphite.IsDark != EffectiveThemeDark(Settings);
    }

    protected override void OnResize(EventArgs args)
    {
        base.OnResize(args);
        ModernTheme.ApplyWindowRegion(this);
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
            lastTelegramSourceAt = DateTime.UtcNow;
            if (String.Equals(lastWidgetSnapshot, line, StringComparison.Ordinal)) { PublishTelegram(); return; }
            StoreRows(message);
            RenderWidget();
            PublishTelegram();
            lastWidgetSnapshot = line;
        }
        else if (type == "widget.limits")
        {
            StoreLimits(message);
            RenderWidget();
            PublishTelegram();
        }
        else if (type == "quota.fetch")
        {
            string provider = TextOf(message, "provider");
            string requestId = TextOf(message, "requestId");
            WidgetQuotaKeys.FetchAsync(provider, requestId, demoMode, delegate(Dictionary<string, object> result)
            {
                try { BeginInvoke((Action)delegate { SendDictionary(result); }); } catch { }
            });
        }
        else if (type == "notice.show")
        {
            if (String.Equals(StatusOf(message), "error", StringComparison.OrdinalIgnoreCase))
            {
                if (!demoMode && Settings.ShowNotifications && Settings.StatusError) telegramPublisher.NotifyError();
            }
            if (SuppressNewCompletedNotice(message)) return;
            bool added = StoreNotice(message);
            RefreshHarnessNoticeSuppression();
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

    private void PublishTelegram()
    {
        if (demoMode) return;
        if (lastTelegramSourceAt == DateTime.MinValue) return;
        lastTelegramData = TelegramCardPolicy.Build(widgetRows.Values, Settings.ShowLimits ? limitProviders : null, lastTelegramSourceAt);
        telegramPublisher.Tick(lastTelegramData, TelegramPublisher.StableKey(lastTelegramData));
    }

    private void TelegramChanged(TelegramCredentials changed)
    {
        telegramSettings = changed;
        if (!String.IsNullOrEmpty(lastTelegramData)) telegramPublisher.Tick(lastTelegramData, TelegramPublisher.StableKey(lastTelegramData));
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
        Color surface = ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base;
        Color card = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card;
        BackColor = surface;
        rows.BackColor = surface;
        cards.BackColor = surface;
        compact.BackColor = surface;
        limits.BackColor = surface;
        limitsHost.BackColor = surface;
        resizeGrip.BackColor = surface;
        resizeGrip.Invalidate();
        stack.BackColor = ModernTheme.Enabled ? ModernTheme.CardBorder : Graphite.Border;
        headerPanel.BackColor = card;
        headerTitle.ForeColor = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary;
        taskGlyph.BackColor = card;
        noticeHeader.BackColor = surface;
        noticeHeaderTitle.ForeColor = ModernTheme.Enabled ? ModernTheme.TextSecondary : Graphite.Secondary;
        StyleHeaderButton(noticeHeaderClose);
        LayoutNoticeHeaderClose();
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
        ModernTheme.ApplyWindowRegion(this);
        ModernTheme.ApplyWindowRegion(stack);
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
        RefreshHarnessNoticeSuppression();
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
        compactToggle.Text = Settings.CompactWidget ? "›" : "‹";
        compactToggle.AccessibleName = Settings.CompactWidget ? "Развернуть список" : "Свернуть список";
        if (Settings.CompactWidget)
        {
            compact.UpdateRows(visibleRows, Settings);
            UpdateLimitsHost();
            ResizeWidget();
            return;
        }
        int firstItem = rows.FirstIndex;
        rows.SuspendLayout();
        try
        {
            ClearCards(rows);
            int usableWidth = Math.Min(Px(WidgetWidth), Screen.FromPoint(Location).WorkingArea.Width);
            int width = Math.Max(1, usableWidth - rows.Padding.Horizontal);
            foreach (WidgetRow row in visibleRows.OrderBy(row => IsCompleted(row.Status) ? 0 : 1))
            {
                rows.Controls.Add(Settings.CompactWorkspaceList ? MakeCompactWorkspaceRow(row, width) : MakeWidgetCard(row, width));
            }
            UpdateLimitsHost();
            ResizeWidget();
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

    private bool HasRetainedCompletedNotice()
    {
        return noticeOrder.Any(id => notices.ContainsKey(id) && IsCompleted(StatusOf(notices[id])));
    }

    private bool ShouldHideCompletedNotices()
    {
        if (!Settings.HideCompletedWhenHarnessOpen || !Settings.ShowNotifications || !HasRetainedCompletedNotice()) return false;
        try
        {
            return harnessOpenProbe();
        }
        catch
        {
            return false;
        }
    }

    private void UpdateHarnessNoticeTimer()
    {
        if (Settings.HideCompletedWhenHarnessOpen && Settings.ShowNotifications && HasRetainedCompletedNotice())
        {
            if (!harnessNoticeTimer.Enabled) harnessNoticeTimer.Start();
        }
        else
        {
            harnessNoticeTimer.Stop();
        }
    }

    private bool RefreshHarnessNoticeSuppression()
    {
        bool hidden = ShouldHideCompletedNotices();
        bool changed = hidden != completedNoticesHiddenByHarness;
        completedNoticesHiddenByHarness = hidden;
        UpdateHarnessNoticeTimer();
        return changed;
    }

    private void RefreshHarnessNoticeSuppressionFromTimer()
    {
        if (!RefreshHarnessNoticeSuppression()) return;
        RenderNotices();
        if (!completedNoticesHiddenByHarness && !noticesManuallyHidden) ShowNotices();
    }

    private bool SuppressNewCompletedNotice(Dictionary<string, object> message)
    {
        if (!Settings.HideCompletedWhenHarnessOpen || !IsCompleted(StatusOf(message))) return false;
        bool open;
        try
        {
            open = harnessOpenProbe();
        }
        catch
        {
            open = false;
        }
        if (!open) return false;
        string id = TextOf(message, "id");
        if (!String.IsNullOrEmpty(id) && notices.ContainsKey(id)) return false;
        if (!String.IsNullOrEmpty(id)) Send("notice.closed", "id", id);
        return true;
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
        NoticeRenderCountForTest++;
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
                if (IsVisible(StatusOf(notice)) && !(completedNoticesHiddenByHarness && IsCompleted(StatusOf(notice)))) cards.Controls.Add(MakeNoticeCard(id, notice));
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
        hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        hide.Click += delegate { HideNoticeStack(); };
        header.Controls.Add(title);
        header.Controls.Add(hide);
        hide.BringToFront();
        header.SizeChanged += delegate { LayoutNoticeHeaderClose(); };
        LayoutNoticeHeaderClose();
        return header;
    }

    private void LayoutNoticeHeaderClose()
    {
        bool modern = ModernTheme.Enabled;
        int size = modern ? 22 : 28;
        int inset = modern ? 8 : 2;
        noticeHeaderClose.SetBounds(Math.Max(0, noticeHeader.ClientSize.Width - size - inset),
            Math.Max(0, (noticeHeader.ClientSize.Height - size) / 2), size, size);
        if (modern) noticeHeaderClose.BackColor = noticeHeader.BackColor;
    }

    private Panel MakeNoticeCard(string id, Dictionary<string, object> notice)
    {
        Panel card = new Panel
        {
            Width = 338,
            BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card,
            Margin = new Padding(0, 0, 0, 6),
            AccessibleName = "Уведомление о задаче"
        };
        ApplyModernCardPaint(card, true);
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
            Panel chip = MakeStatusIndicator(status, caption, new Rectangle(titleRight - width, 8, width, 20), true, CardDetailFont);
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

    private static Panel MakeStatusIndicator(string status, string text, Rectangle bounds, bool badge, Font captionFont)
    {
        Panel indicator = new Panel
        {
            Bounds = bounds,
            BackColor = badge ? Graphite.ChipFill(status) : Graphite.Card
        };
        int glyphSize = StatusGlyph.GlyphSize;
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

    private static void ApplyModernCardPaint(Panel card, bool clipCorners = false)
    {
        if (clipCorners && ModernTheme.Enabled)
        {
            card.Resize += delegate { ModernTheme.ApplyRegion(card, 8); };
            ModernTheme.ApplyRegion(card, 8);
        }
        card.Paint += delegate(object sender, PaintEventArgs args)
        {
            if (!ModernTheme.Enabled) return;
            Rectangle bounds = card.ClientRectangle;
            if (clipCorners) bounds.Size = new Size(Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1));
            ModernTheme.PaintCard(args.Graphics, bounds);
        };
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
            BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card,
            AccessibleName = "Карточка рабочей области",
            Margin = new Padding(0, 0, 0, 6)
        };
        ApplyModernCardPaint(card);
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
            Panel indicator = MakeStatusIndicator(row.Status, status, new Rectangle(Px(10), top, contentWidth + Px(2), Math.Max(StatusGlyph.GlyphSize, Px(18))), false, WidgetFont(8.25F));
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
        Panel item = new Panel { Width = width, Height = Px(26), BackColor = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card,
            AccessibleName = "Рабочая область: " + (String.IsNullOrEmpty(row.Workspace) ? row.Title : row.Workspace), Margin = new Padding(0, 0, 0, ModernTheme.Enabled ? Px(5) : Px(2)) };
        ApplyModernCardPaint(item);
        string title = String.IsNullOrEmpty(row.Workspace) ? row.Title : row.Workspace;
        int textLeft = Px(8);
        int statusLeft = Math.Max(textLeft, width - Px(118));
        int lineHeight = Math.Max(StatusGlyph.GlyphSize, Px(18));
        int lineTop = (item.Height - lineHeight) / 2;
        int titleWidth = Math.Max(1, statusLeft - textLeft - Px(8));
        Font detail = WidgetFont(8.25F);
        string duration = Settings.InlineTaskTime && !String.IsNullOrWhiteSpace(row.TimeText) && row.TimeText != "Время неизвестно"
            ? FormatNoticeDuration(row.TimeText).Replace(" мин", "м").Replace(" сек", "с").Replace(" ч", "ч")
            : String.Empty;
        if (!String.IsNullOrWhiteSpace(duration))
        {
            int gap = Px(5);
            int durationWidth = Math.Min(Math.Max(1, titleWidth - Px(24) - gap * 2), TextRenderer.MeasureText(duration, detail).Width);
            titleWidth = Math.Max(1, Math.Min(titleWidth - durationWidth - gap, TextRenderer.MeasureText(title, detail).Width));
            item.Controls.Add(new Label { Text = duration, AutoEllipsis = true, Font = detail, ForeColor = Graphite.Secondary,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                AccessibleName = "Длительность выполнения " + row.TimeText,
                Bounds = new Rectangle(textLeft + titleWidth + gap, lineTop, durationWidth, lineHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Right });
        }
        Label name = new Label { Text = title, AutoEllipsis = true, Font = detail, ForeColor = Graphite.Primary,
            TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
            Bounds = new Rectangle(textLeft, lineTop, titleWidth, lineHeight), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        string state = String.IsNullOrEmpty(row.StatusText) ? StatusLabel(row.Status) : row.StatusText;
        Panel indicator = MakeStatusIndicator(row.Status, state, new Rectangle(statusLeft, lineTop, Px(112), lineHeight), false, detail);
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
            if (!demoMode && !HarnessWindowProbe.TryActivateDesktop())
            {
                try { System.Diagnostics.Process.Start(DshOpenUrl); } catch { }
            }
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
        RefreshHarnessNoticeSuppression();
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
        expandedUserHeight = LogicalHeightAtLeast(requested);
        UpdateLimitsHost();
        ResizeWidget();
    }

    internal void BeginResizeForTest()
    {
        resizeStartHeight = Height;
        CaptureResizeFitHeight();
    }

    internal void ResizeFromDragForTest(int height)
    {
        ResizeFromDrag(height);
    }

    internal int ResizeFitHeightForTest { get { return resizeFitHeight; } }

    private void CaptureResizeFitHeight()
    {
        Rectangle area = Screen.FromPoint(Location).WorkingArea;
        resizeFitHeight = Settings.CompactWidget
            ? Math.Max(MeasureExpandedFitHeight(VisibleWidgetRows()), Height + 1)
            : FitHeightForContent(rows.ContentHeight, area, rows.TallestItemHeight);
        resizeDragHasFit = !Settings.CompactWidget && Height >= resizeFitHeight;
    }

    private void ResizeFromDrag(int requestedHeight)
    {
        if (resizeFitHeight <= 0) CaptureResizeFitHeight();
        if (Settings.CompactWidget)
        {
            if (requestedHeight < resizeFitHeight) return;
            expandedUserHeight = LogicalHeightAtLeast(requestedHeight);
            resizeDragHasFit = true;
            Settings.CompactWidget = false;
            UserChanged(Settings);
            return;
        }
        if (requestedHeight < resizeFitHeight && (resizeDragHasFit || requestedHeight < resizeStartHeight))
        {
            Settings.CompactWidget = true;
            UserChanged(Settings);
            return;
        }
        if (requestedHeight >= resizeFitHeight) resizeDragHasFit = true;
        SetUserWidgetHeight(requestedHeight);
    }

    private int LogicalHeightAtLeast(int physicalHeight)
    {
        int logical = (int)Math.Round(physicalHeight / WidgetScaleFactor);
        return Px(logical) < physicalHeight ? logical + 1 : logical;
    }

    private List<WidgetRow> VisibleWidgetRows()
    {
        return widgetRows.Values.Where(row => IsVisible(row.Status)).ToList();
    }

    private int MeasureExpandedFitHeight(IEnumerable<WidgetRow> visibleRows)
    {
        int usableWidth = Math.Min(Px(WidgetWidth), Screen.FromPoint(Location).WorkingArea.Width);
        int width = Math.Max(1, usableWidth - rows.Padding.Horizontal);
        int contentHeight = rows.Padding.Vertical;
        int tallestItemHeight = 0;
        foreach (WidgetRow row in visibleRows.OrderBy(row => IsCompleted(row.Status) ? 0 : 1))
        {
            Control item = Settings.CompactWorkspaceList ? (Control)MakeCompactWorkspaceRow(row, width) : MakeWidgetCard(row, width);
            contentHeight += item.Height + item.Margin.Vertical;
            tallestItemHeight = Math.Max(tallestItemHeight, item.Height + item.Margin.Vertical);
            item.Dispose();
        }
        return FitHeightForContent(contentHeight, Screen.FromPoint(Location).WorkingArea, tallestItemHeight);
    }

    private int FitHeightForContent(int contentHeight, Rectangle area, int tallestItemHeight)
    {
        int headerHeight = headerPanel == null ? WidgetHeaderHeight : headerPanel.Height;
        int taskHeight = Math.Max(contentHeight, rows.Padding.Vertical + Math.Max(Px(24), tallestItemHeight));
        int complete = headerHeight + (Settings.ShowLimits ? limits.DesiredHeight : 0) + resizeGrip.Height + taskHeight;
        return Math.Min(complete, FeasibleHeight(area));
    }

    private void ResizeWidget()
    {
        Point previous = Location;
        Rectangle area = Screen.FromPoint(previous).WorkingArea;
        int headerHeight = headerPanel == null ? WidgetHeaderHeight : headerPanel.Height;
        int height = headerHeight + Px(CompactContentHeight) + limitsHost.Height + resizeGrip.Height;
        if (!Settings.CompactWidget)
        {
            int chrome = headerHeight + limitsHost.Height + resizeGrip.Height;
            int minimum = chrome + rows.Padding.Vertical + Math.Max(Px(24), rows.TallestItemHeight);
            int automatic = Math.Max(minimum, chrome + rows.ContentHeight);
            int automaticMaximum = Math.Max(minimum, HeightLimit(area, headerHeight + Px(ExpandedHeight - WidgetHeaderHeight + ResizeGripHeight)));
            int maximum = Math.Max(minimum, FeasibleHeight(area));
            height = expandedUserHeight > 0 ? Math.Min(maximum, Math.Max(minimum, Px(expandedUserHeight))) : Math.Min(automatic, automaticMaximum);
        }
        resizeGrip.Visible = true;
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

    private int FeasibleHeight(Rectangle area)
    {
        return Math.Max(1, area.Bottom - Math.Max(Top, area.Top));
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
        SettingsForm form = new SettingsForm(Settings, UserChanged, availableLimitProviders, playNoticeSound,
            delegate(string provider) { Send("quota-key.changed", "provider", provider); }, !demoMode, null, TelegramChanged, demoMode ? null : telegramPublisher);
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
        int minimumTaskArea = Settings.CompactWidget ? Px(CompactContentHeight)
            : Math.Max(rows.ContentHeight, rows.Padding.Vertical + Math.Max(Px(24), rows.TallestItemHeight));
        int available = Math.Max(Math.Max(DefaultQuotaHeight, Px(DefaultQuotaHeight)), preferred - headerPanel.Height - resizeGrip.Height - minimumTaskArea);
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
        telegramPublisher.Close();
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

    private void SendDictionary(Dictionary<string, object> message)
    {
        string line = json.Serialize(message);
        if (outputCallback != null) outputCallback(line);
        else output.WriteLine(line);
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
            gui.Send("ready", "quotaKeys", true);
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
        bool checkbox = !settings.FieldTime && WidgetQuotaKeys.SelfTest() && TelegramCardTests.SelfTest();
        bool soundDisabledByDefault = !settings.NotificationSound;
        form.Check("notice-sound").Checked = true;
        bool soundCheckbox = settings.NotificationSound;
        form.Check("hide-completed-harness-open").Checked = true;
        bool harnessCheckbox = settings.HideCompletedWhenHarnessOpen;
        ComboBox soundChoices = (ComboBox)form.Controls.Find("notification-sound-preset", true).Single();
        bool legacySoundPreserved = soundChoices.SelectedIndex == 0 && settings.NotificationSoundPreset == "icq";
        soundChoices.SelectedIndex = NotificationSounds.IndexOf("squeak");
        bool soundSelectionSaved = settings.NotificationSoundPreset == "squeak" && soundChoices.Items.Count == 6;
        ComboBox scaleChoices = (ComboBox)form.Controls.Find("widget-scale", true).Single();
        scaleChoices.SelectedIndex = 2;
        Size settingsLargeSize = form.ClientSize;
        bool settingsScaleLarge = settingsLargeSize.Width == 498 && settingsLargeSize.Height > 0;
        scaleChoices.SelectedIndex = 0;
        Size settingsSmallSize = form.ClientSize;
        bool settingsScaleSmall = settingsSmallSize.Width == 338 && settingsSmallSize.Height < settingsLargeSize.Height;
        scaleChoices.SelectedIndex = 2;
        bool settingsScaleNoDrift = form.ClientSize == settingsLargeSize;
        bool settingsChildrenFit = form.SaveButton.Right <= form.ClientSize.Width && form.SaveButton.Bottom <= form.ClientSize.Height;
        form.UpdateLimitProviders(new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", "after-scale" }, { "label", "Добавлен после масштаба" } }
        });
        int providerTopLarge = form.Check("limit-after-scale").Top;
        scaleChoices.SelectedIndex = 1;
        int providerTopMedium = form.Check("limit-after-scale").Top;
        scaleChoices.SelectedIndex = 2;
        int providerTopLargeAgain = form.Check("limit-after-scale").Top;
        bool providerScaleStable = Math.Abs(providerTopMedium - (int)Math.Round(providerTopLarge / 1.25F)) <= 1
            && providerTopLargeAgain == providerTopLarge;
        ComboBox themes = (ComboBox)form.Controls.Find("theme-mode", true).Single();
        themes.SelectedIndex = 1;
        bool explicitLightTheme = settings.Theme == "light" && !settings.ThemeDark;
        themes.SelectedIndex = 0;
        bool systemThemeDoesNotOverwriteLegacy = settings.Theme == "system" && !settings.ThemeDark;
        IntPtr settingsHandle = form.Handle;
        ComboBox interfaceStyles = (ComboBox)form.Controls.Find("interface-style", true).Single();
        bool interfaceStyleDefaultsClassic = interfaceStyles.SelectedIndex == 0 && settings.NormalizedInterfaceStyle == "classic";
        interfaceStyles.SelectedIndex = 1;
        bool interfaceStyleModern = settings.NormalizedInterfaceStyle == "modern" && ModernTheme.Enabled && form.Handle == settingsHandle;
        bool modernPaintCoversBuffer;
        using (var toggle = new ModernToggle { Text = "Виджет", Size = new Size(280, 30), BackColor = ModernTheme.Card })
        using (var heading = new ModernSectionLabel { Text = "Виджет", HelpText = "Задачи рядом с рабочим столом",
            Size = new Size(280, 36), BackColor = ModernTheme.Card })
            modernPaintCoversBuffer = PaintCoversBuffer(toggle) && PaintCoversBuffer(heading);
        interfaceStyles.SelectedIndex = 0;
        bool interfaceStyleReturnsClassic = settings.NormalizedInterfaceStyle == "classic" && !ModernTheme.Enabled && form.Handle == settingsHandle;
        form.Dispose();
        List<Dictionary<string, object>> longProviders = Enumerable.Range(0, 40).Select(index => new Dictionary<string, object>
        {
            { "id", "long-provider-" + index }, { "label", "Длинный поставщик лимитов " + index }
        }).ToList();
        Settings longSettings = new Settings();
        SettingsForm longForm = new SettingsForm(longSettings, delegate(Settings changed) { }, longProviders,
            null, null, true, new Rectangle(0, 0, 450, 360));
        longForm.Show();
        Application.DoEvents();
        Panel settingsContent = longForm.Controls.Cast<Control>().OfType<Panel>().Single(panel => panel != null && panel.Controls.Count > 20);
        bool defaultTabFits = settingsContent.Controls.Cast<Control>().Where(control => control.Visible)
            .All(control => settingsContent.ClientRectangle.Contains(control.Bounds));
        Dictionary<Control, int> longFormPages = (Dictionary<Control, int>)typeof(SettingsForm).GetField("pages",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(longForm);
        HashSet<Control> visibleGeneralControls = new HashSet<Control>();
        while (true)
        {
            foreach (Control control in longFormPages.Where(item => item.Value == 0).Select(item => item.Key).Where(control => control.Visible)) visibleGeneralControls.Add(control);
            Button nextSettingsPage = settingsContent.Controls.Cast<Control>().OfType<Button>()
                .FirstOrDefault(button => button.AccessibleName == "Следующая страница настроек");
            if (nextSettingsPage == null || !nextSettingsPage.Enabled) break;
            nextSettingsPage.PerformClick();
        }
        bool generalControlsReachable = longFormPages.Where(item => item.Value == 0).Select(item => item.Key)
            .All(control => visibleGeneralControls.Contains(control));
        Button limitsTab = longForm.Controls.Cast<Control>().OfType<Panel>().SelectMany(panel => panel.Controls.Cast<Control>())
            .OfType<Button>().Single(button => button.AccessibleName == "Лимиты");
        limitsTab.PerformClick();
        bool settingsNoScrollbars = !longForm.AutoScroll && !settingsContent.AutoScroll;
        Button previousPage = settingsContent.Controls.Cast<Control>().OfType<Button>().Single(button => button.AccessibleName == "Предыдущая страница лимитов");
        Button nextPage = settingsContent.Controls.Cast<Control>().OfType<Button>().Single(button => button.AccessibleName == "Следующая страница лимитов");
        HashSet<string> visibleProviders = new HashSet<string>();
        do
        {
            foreach (CheckBox row in settingsContent.Controls.OfType<CheckBox>().Where(row => row.Visible && row.Name == "")) visibleProviders.Add(row.Text);
            if (!nextPage.Enabled) break;
            nextPage.PerformClick();
        } while (true);
        bool settingsPagingKeepsLimitRowsReachable = previousPage.Visible && nextPage.Visible && visibleProviders.Count == longProviders.Count
            && settingsContent.Controls.Cast<Control>().Where(control => control.Visible).All(control => settingsContent.ClientRectangle.Contains(control.Bounds));
        Size lightSize = longForm.ClientSize;
        ComboBox longThemes = (ComboBox)longForm.Controls.Find("theme-mode", true).Single();
        longThemes.SelectedIndex = 1;
        Size explicitLightSize = longForm.ClientSize;
        longThemes.SelectedIndex = 2;
        bool settingsThemesKeepLayoutStable = lightSize == explicitLightSize && explicitLightSize == longForm.ClientSize;
        ComboBox longScaleChoices = (ComboBox)longForm.Controls.Find("widget-scale", true).Single();
        bool settingsFitsWorkingAreaAtAllScales = defaultTabFits && generalControlsReachable;
        foreach (int scale in new int[] { 0, 1, 2 })
        {
            longScaleChoices.SelectedIndex = scale;
            settingsFitsWorkingAreaAtAllScales = settingsFitsWorkingAreaAtAllScales && longForm.ClientSize.Width <= 450
                && longForm.ClientSize.Height <= 344 && settingsContent.Controls.Cast<Control>().Where(control => control.Visible)
                .All(control => settingsContent.ClientRectangle.Contains(control.Bounds));
        }
        ComboBox longInterface = (ComboBox)longForm.Controls.Find("interface-style", true).Single();
        longInterface.SelectedIndex = 1;
        Button statesTab = longForm.Controls.Cast<Control>().OfType<Panel>().SelectMany(panel => panel.Controls.Cast<Control>())
            .OfType<Button>().Single(button => button.AccessibleName == "Состояния");
        bool settingsCardsRedraw = true;
        foreach (Button tab in new[] { statesTab, limitsTab })
        {
            bool fullInvalidation = false;
            InvalidateEventHandler checkDamage = delegate(object sender, InvalidateEventArgs args)
            {
                fullInvalidation |= args.InvalidRect.Contains(settingsContent.ClientRectangle);
            };
            settingsContent.Invalidated += checkDamage;
            tab.PerformClick();
            settingsContent.Invalidated -= checkDamage;
            settingsCardsRedraw &= fullInvalidation;
        }
        longInterface.SelectedIndex = 0;
        longForm.Dispose();
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
        Settings defaults = new Settings();
        bool newSettings = serializer.Deserialize<Settings>(serializer.Serialize(defaults)).ThemeDark
            && serializer.Deserialize<Settings>(serializer.Serialize(defaults)).Theme == "system"
            && !serializer.Deserialize<Settings>(serializer.Serialize(defaults)).ShowLimits;
        bool countdown = LimitsPanel.Countdown(0) == "сброс доступен"
            && LimitsPanel.Countdown((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds + 59) == "<1 мин";
        List<string> output = new List<string>();
        int soundCount = 0;
        string playedPreset = null;
        bool harnessOpen = true;
        List<string> harnessOutput = new List<string>();
        DshNotifyGui harnessGui = new DshNotifyGui(false, delegate(string line) { harnessOutput.Add(line); },
            delegate(string preset) { soundCount++; }, delegate { return harnessOpen; }, false);
        Settings harnessSettings = new Settings();
        harnessSettings.HideCompletedWhenHarnessOpen = true;
        harnessGui.ApplyRemote(harnessSettings);
        harnessGui.Process("{\"t\":\"notice.show\",\"id\":\"suppressed\",\"reason\":\"completed\"}");
        bool newCompletedSuppressed = harnessGui.cards.Controls.Count == 0 && harnessOutput.Count(line => line.Contains("notice.closed")) == 1
            && soundCount == 0 && !harnessGui.HarnessNoticeTimerEnabledForTest;
        harnessOpen = false;
        harnessGui.Process("{\"t\":\"notice.show\",\"id\":\"retained\",\"reason\":\"completed\"}");
        int retainedRenderCount = harnessGui.NoticeRenderCountForTest;
        bool retainedShown = harnessGui.cards.Controls.Count == 1 && harnessGui.HarnessNoticeTimerEnabledForTest;
        harnessOpen = true;
        harnessGui.RefreshHarnessNoticeSuppressionFromTimer();
        bool retainedHiddenOnTransition = harnessGui.cards.Controls.Count == 0 && harnessGui.NoticeRenderCountForTest == retainedRenderCount + 1;
        harnessGui.Process("{\"t\":\"notice.show\",\"id\":\"retained\",\"reason\":\"completed\"}");
        bool duplicateRetained = harnessOutput.Count(line => line.Contains("notice.closed")) == 1;
        int hiddenRenderCount = harnessGui.NoticeRenderCountForTest;
        harnessGui.RefreshHarnessNoticeSuppressionFromTimer();
        bool noRepeatedRender = harnessGui.NoticeRenderCountForTest == hiddenRenderCount;
        harnessGui.HideNoticeStack();
        harnessOpen = false;
        harnessGui.RefreshHarnessNoticeSuppressionFromTimer();
        bool retainedRestoredWithoutReplayingSound = harnessGui.cards.Controls.Count == 1 && !harnessGui.stack.Visible && soundCount == 0;
        harnessGui.Dispose();
        bool harnessDefaultOff = !new Settings().HideCompletedWhenHarnessOpen;
        DshNotifyGui gui = new DshNotifyGui(false, delegate(string line)
        {
            output.Add(line);
        }, delegate(string preset) { soundCount++; playedPreset = preset; }, null, false);
        bool iconDrawn;
        using (Bitmap iconBitmap = new Bitmap(gui.taskGlyph.Width, gui.taskGlyph.Height))
        {
            gui.taskGlyph.DrawToBitmap(iconBitmap, gui.taskGlyph.ClientRectangle);
            Color center = iconBitmap.GetPixel(iconBitmap.Width / 2, iconBitmap.Height / 2);
            iconDrawn = center.B > center.R + 30;
        }
        bool taskIconConsistent = gui.Icon == TaskIcon.Window && gui.tray.Icon == TaskIcon.Small
            && HasMatchingShellIcon(gui.Icon);
        bool noTaskbarButtons = !gui.ShowInTaskbar;
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
        int settingsChangedBeforeResize = output.Count(line => line.Contains("settings.changed"));
        gui.BeginResizeForTest();
        int resizeThreshold = gui.ResizeFitHeightForTest;
        Rectangle resizeArea = Screen.FromPoint(gui.Location).WorkingArea;
        int oldAutomaticLimit = HeightLimit(resizeArea, gui.headerPanel.Height + gui.Px(ExpandedHeight - WidgetHeaderHeight + ResizeGripHeight));
        int feasibleManualHeight = Math.Min(resizeArea.Bottom - Math.Max(gui.Top, resizeArea.Top), oldAutomaticLimit + 40);
        gui.SetUserWidgetHeight(feasibleManualHeight);
        bool manualHeightCanExceedAutomaticDefault = gui.Height == feasibleManualHeight;
        gui.SetUserWidgetHeight(resizeThreshold + 20);
        int manualExpandedHeight = gui.Height;
        gui.ToggleCompactMode();
        gui.ToggleCompactMode();
        bool manualTogglePreservesHeight = !gui.Settings.CompactWidget && gui.Height == manualExpandedHeight;
        gui.BeginResizeForTest();
        int capturedThreshold = gui.ResizeFitHeightForTest;
        gui.ResizeFromDragForTest(capturedThreshold - 1);
        int compactHeight = gui.Height;
        bool resizeThresholdCheck = capturedThreshold == resizeThreshold && gui.Settings.CompactWidget
            && gui.resizeGrip.Height == gui.Px(ResizeGripHeight)
            && compactHeight == gui.headerPanel.Height + gui.Px(CompactContentHeight) + gui.limitsHost.Height + gui.resizeGrip.Height;
        gui.BeginResizeForTest();
        gui.ResizeFromDragForTest(capturedThreshold - 1);
        resizeThresholdCheck = resizeThresholdCheck && gui.Settings.CompactWidget && gui.Height == compactHeight;
        gui.BeginResizeForTest();
        int compactGestureThreshold = gui.ResizeFitHeightForTest;
        gui.ResizeFromDragForTest(compactHeight);
        resizeThresholdCheck = resizeThresholdCheck && compactGestureThreshold > compactHeight && gui.Settings.CompactWidget;
        gui.ResizeFromDragForTest(compactGestureThreshold);
        resizeThresholdCheck = resizeThresholdCheck && !gui.Settings.CompactWidget && gui.Height >= capturedThreshold;
        resizeThresholdCheck = resizeThresholdCheck && manualTogglePreservesHeight
            && output.Count(line => line.Contains("settings.changed")) == settingsChangedBeforeResize + 4;
        bool resizeThresholdAllScales = true;
        foreach (string scale in new string[] { "small", "medium", "large" })
        {
            foreach (bool shortList in new bool[] { false, true })
            {
                settings.WidgetScale = scale;
                settings.CompactWorkspaceList = shortList;
                settings.CompactWidget = false;
                gui.UserChanged(settings);
                gui.BeginResizeForTest();
                int threshold = gui.ResizeFitHeightForTest;
                gui.ResizeFromDragForTest(threshold);
                gui.ResizeFromDragForTest(threshold - 1);
                bool collapsed = gui.Settings.CompactWidget;
                gui.BeginResizeForTest();
                gui.ResizeFromDragForTest(gui.ResizeFitHeightForTest);
                resizeThresholdAllScales = resizeThresholdAllScales && threshold > 0 && collapsed && !gui.Settings.CompactWidget;
            }
        }
        settings.CompactWorkspaceList = false;
        settings.WidgetScale = "medium";
        settings.CompactWidget = false;
        gui.UserChanged(settings);
        StringBuilder mixedRows = new StringBuilder("{\"t\":\"widget.render\",\"rows\":[");
        for (int index = 0; index < 5; index++)
        {
            if (index > 0) mixedRows.Append(",");
            mixedRows.Append("{\"id\":\"fit-full-").Append(index).Append("\",\"workspace\":\"Полная карточка\",\"title\":\"Полная карточка\",\"status\":\"running\",\"statusText\":\"Выполняется\",\"taskSummary\":\"Длинная задача для точной высоты строки\",\"path\":\"C:\\\\Work\",\"timeText\":\"10:00\"}");
        }
        mixedRows.Append(",{\"id\":\"fit-short\",\"title\":\"Короткая\",\"status\":\"completed\"}]}");
        gui.Process(mixedRows.ToString());
        int exactMixedFit = gui.headerPanel.Height + gui.limitsHost.Height + gui.resizeGrip.Height + gui.rows.ContentHeight;
        gui.BeginResizeForTest();
        bool resizeThresholdExactRows = gui.ResizeFitHeightForTest == exactMixedFit;
        StringBuilder manyRows = new StringBuilder("{\"t\":\"widget.render\",\"rows\":[");
        for (int index = 0; index < 40; index++)
        {
            if (index > 0) manyRows.Append(",");
            manyRows.Append("{\"id\":\"resize-").Append(index).Append("\",\"title\":\"Длинный список\",\"status\":\"running\"}");
        }
        manyRows.Append("]}");
        settings.CompactWorkspaceList = false;
        settings.WidgetScale = "medium";
        settings.CompactWidget = false;
        gui.UserChanged(settings);
        gui.Process(manyRows.ToString());
        gui.BeginResizeForTest();
        int longListThreshold = gui.ResizeFitHeightForTest;
        bool grewWhileAlreadyScrolling = true;
        if (gui.Height + 1 < longListThreshold)
        {
            int startingHeight = gui.Height;
            gui.ResizeFromDragForTest(startingHeight + 1);
            grewWhileAlreadyScrolling = !gui.Settings.CompactWidget && gui.Height > startingHeight;
        }
        gui.ResizeFromDragForTest(longListThreshold);
        gui.ResizeFromDragForTest(longListThreshold - 1);
        bool resizeThresholdLongList = gui.Settings.CompactWidget && grewWhileAlreadyScrolling;
        gui.BeginResizeForTest();
        gui.ResizeFromDragForTest(gui.ResizeFitHeightForTest);
        resizeThresholdLongList = resizeThresholdLongList && !gui.Settings.CompactWidget
            && gui.rows.Controls.Count == 40 && gui.rows.Controls.Cast<Control>().Any(item => !item.Visible);
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
        bool settingsTogglesKeepLayoutStable = SettingsTogglesKeepLayoutStable();
        bool passed = checkbox && position && clamp && settingsRoundTrip && newSettings && iconDrawn
            && taskIconConsistent && providerBadgeAliases && notificationSound && notificationSoundPresets
            && countdown && taskTitle && moveEmitted && moveStored && noTaskbarButtons
            && interfaceStyleDefaultsClassic && interfaceStyleModern && interfaceStyleReturnsClassic && modernPaintCoversBuffer;
        passed = passed && settingsScaleLarge && settingsScaleSmall && settingsScaleNoDrift && settingsChildrenFit && providerScaleStable
            && explicitLightTheme && systemThemeDoesNotOverwriteLegacy && settingsNoScrollbars && settingsPagingKeepsLimitRowsReachable
            && settingsFitsWorkingAreaAtAllScales && settingsThemesKeepLayoutStable && settingsCardsRedraw
            && settingsTogglesKeepLayoutStable;
        passed = passed && noEcho && allRowsShown && allNoticesShown && interactionIds && registeredIdleFiltered;
        passed = passed && matrix && fieldsHidden && rowsReplaced && compactToggle && noticePresentation
            && completedRowFirst && completedNoticeFirst && noticeHeightAndWidth && fixedWidgetWidth && wholeCardScroll;
        passed = passed && harnessDefaultOff && newCompletedSuppressed && retainedShown && retainedHiddenOnTransition
            && duplicateRetained && noRepeatedRender && retainedRestoredWithoutReplayingSound && harnessCheckbox;
        passed = passed && resizeThresholdCheck && resizeThresholdAllScales && resizeThresholdLongList && resizeThresholdExactRows && manualHeightCanExceedAutomaticDefault;
        Console.WriteLine("{\"t\":\"selftest\",\"pass\":" + (passed ? "true" : "false")
            + ",\"checks\":{\"checkbox\":" + JsonBool(checkbox)
            + ",\"position\":" + JsonBool(position)
            + ",\"clamp\":" + JsonBool(clamp)
            + ",\"settingsRoundTrip\":" + JsonBool(settingsRoundTrip)
            + ",\"newSettings\":" + JsonBool(newSettings)
            + ",\"harnessDefaultOff\":" + JsonBool(harnessDefaultOff)
            + ",\"harnessCheckbox\":" + JsonBool(harnessCheckbox)
            + ",\"newCompletedSuppressed\":" + JsonBool(newCompletedSuppressed)
            + ",\"retainedShown\":" + JsonBool(retainedShown)
            + ",\"retainedHiddenOnTransition\":" + JsonBool(retainedHiddenOnTransition)
            + ",\"duplicateRetained\":" + JsonBool(duplicateRetained)
            + ",\"noRepeatedRender\":" + JsonBool(noRepeatedRender)
            + ",\"retainedRestoredWithoutReplayingSound\":" + JsonBool(retainedRestoredWithoutReplayingSound)
            + ",\"interfaceStyleDefaultsClassic\":" + JsonBool(interfaceStyleDefaultsClassic)
            + ",\"interfaceStyleModern\":" + JsonBool(interfaceStyleModern)
            + ",\"interfaceStyleReturnsClassic\":" + JsonBool(interfaceStyleReturnsClassic)
            + ",\"modernPaintCoversBuffer\":" + JsonBool(modernPaintCoversBuffer)
            + ",\"iconDrawn\":" + JsonBool(iconDrawn)
            + ",\"taskIconConsistent\":" + JsonBool(taskIconConsistent)
            + ",\"providerBadgeAliases\":" + JsonBool(providerBadgeAliases)
            + ",\"notificationSound\":" + JsonBool(notificationSound)
            + ",\"notificationSoundPresets\":" + JsonBool(notificationSoundPresets)
            + ",\"countdown\":" + JsonBool(countdown)
            + ",\"taskTitle\":" + JsonBool(taskTitle)
            + ",\"noTaskbarButtons\":" + JsonBool(noTaskbarButtons)
            + ",\"settingsScaleLarge\":" + JsonBool(settingsScaleLarge)
            + ",\"settingsScaleSmall\":" + JsonBool(settingsScaleSmall)
            + ",\"settingsScaleNoDrift\":" + JsonBool(settingsScaleNoDrift)
            + ",\"settingsChildrenFit\":" + JsonBool(settingsChildrenFit)
            + ",\"providerScaleStable\":" + JsonBool(providerScaleStable)
            + ",\"resizeThreshold\":" + JsonBool(resizeThresholdCheck)
            + ",\"resizeThresholdAllScales\":" + JsonBool(resizeThresholdAllScales)
            + ",\"resizeThresholdLongList\":" + JsonBool(resizeThresholdLongList)
            + ",\"resizeThresholdExactRows\":" + JsonBool(resizeThresholdExactRows)
            + ",\"manualHeightCanExceedAutomaticDefault\":" + JsonBool(manualHeightCanExceedAutomaticDefault)
            + ",\"explicitLightTheme\":" + JsonBool(explicitLightTheme)
            + ",\"systemThemeDoesNotOverwriteLegacy\":" + JsonBool(systemThemeDoesNotOverwriteLegacy)
            + ",\"settingsNoScrollbars\":" + JsonBool(settingsNoScrollbars)
            + ",\"settingsPagingKeepsLimitRowsReachable\":" + JsonBool(settingsPagingKeepsLimitRowsReachable)
            + ",\"settingsFitsWorkingAreaAtAllScales\":" + JsonBool(settingsFitsWorkingAreaAtAllScales)
            + ",\"settingsThemesKeepLayoutStable\":" + JsonBool(settingsThemesKeepLayoutStable)
            + ",\"settingsCardsRedraw\":" + JsonBool(settingsCardsRedraw)
            + ",\"settingsTogglesKeepLayoutStable\":" + JsonBool(settingsTogglesKeepLayoutStable)
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

    private static bool SettingsTogglesKeepLayoutStable()
    {
        foreach (string style in new[] { "classic", "modern" })
        {
            int saved = 0;
            using (var gui = new DshNotifyGui(true, delegate(string message)
            {
                if (message.Contains("settings.changed")) saved++;
            }, delegate { }))
            {
                var settings = new Settings { Theme = "light", InterfaceStyle = style, CompactWidget = false };
                gui.ApplyRemote(settings);
                gui.Show();
                gui.ShowSettings();
                SettingsForm form = gui.settingsForms.Single();
                Application.DoEvents();
                Rectangle bounds = form.Bounds;
                bool layoutChanged = false;
                form.Layout += delegate { layoutChanged = true; };
                form.SizeChanged += delegate { layoutChanged = true; };
                form.Check("notice-sound").Checked = true;
                form.Check("hide-completed-harness-open").Checked = true;
                form.Check("compact-widget").Checked = true;
                Application.DoEvents();
                if (layoutChanged || form.Bounds != bounds || !form.Visible || saved != 3
                    || !settings.NotificationSound || !settings.HideCompletedWhenHarnessOpen || !settings.CompactWidget)
                    return false;
            }
        }
        return true;
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

    private static bool PaintCoversBuffer(Control control)
    {
        using (var image = new Bitmap(control.Width + 28, control.Height + 28))
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(16, 16);
            var clip = new Rectangle(0, 0, control.Width - 8, control.Height - 3);
            graphics.SetClip(clip);
            using (var args = new PaintEventArgs(graphics, clip))
                control.GetType().GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(control, new object[] { args });
            return image.GetPixel(16 + control.Width - 75, 20).ToArgb() == control.BackColor.ToArgb()
                && image.GetPixel(4, 4).ToArgb() == Color.Magenta.ToArgb()
                && image.GetPixel(16 + control.Width - 1, 20).ToArgb() == Color.Magenta.ToArgb();
        }
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
