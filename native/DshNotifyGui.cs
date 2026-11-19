using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Settings
{
    public bool ShowWidget { get; set; }
    public bool ShowNotifications { get; set; }
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
    public int WidgetX { get; set; }
    public int WidgetY { get; set; }
    public int NoticeX { get; set; }
    public int NoticeY { get; set; }

    public Settings()
    {
        ShowWidget = true;
        ShowNotifications = true;
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
    private readonly Settings value;
    private readonly Action<Settings> changed;
    private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();

    public Button SaveButton { get; private set; }

    public SettingsForm(Settings current, Action<Settings> onChanged)
    {
        value = current;
        changed = onChanged;
        Text = "Настройки";
        BackColor = Color.FromArgb(30, 33, 38);
        ForeColor = Color.FromArgb(232, 234, 237);
        int top = 10;
        AddCaption("Окна", ref top);
        Add("show-widget", "Показывать виджет", value.ShowWidget, ref top);
        Add("show-notices", "Показывать уведомления", value.ShowNotifications, ref top);
        Add("subagent-notices", "Уведомления субагентов", value.SubagentNotices, ref top);
        AddCaption("Поля", ref top);
        Add("field-workspace", "Рабочая область", value.FieldWorkspace, ref top);
        Add("field-task", "Задача", value.FieldTask, ref top);
        Add("field-status", "Результат", value.FieldStatus, ref top);
        Add("field-time", "Время", value.FieldTime, ref top);
        Add("field-path", "Путь", value.FieldPath, ref top);
        Add("widget-counts", "Счётчики", value.WidgetCounts, ref top);
        Add("compact-widget", "Компактный вид", value.CompactWidget, ref top);
        AddCaption("Состояния", ref top);
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
        SaveButton = new Button();
        SaveButton.Text = "Готово";
        SaveButton.Top = top;
        SaveButton.Left = 10;
        SaveButton.BackColor = Color.FromArgb(46, 51, 59);
        SaveButton.ForeColor = Color.FromArgb(232, 234, 237);
        SaveButton.Click += delegate { Close(); };
        Controls.Add(SaveButton);
        Width = 320;
        Height = top + 70;
    }

    public CheckBox Check(string key)
    {
        return checks[key];
    }

    private void Add(string key, string text, bool selected, ref int top)
    {
        CheckBox check = new CheckBox();
        check.Text = text;
        check.Checked = selected;
        check.Top = top;
        check.Left = 10;
        check.Width = 290;
        check.ForeColor = Color.FromArgb(232, 234, 237);
        check.BackColor = Color.FromArgb(30, 33, 38);
        check.CheckedChanged += delegate
        {
            Save();
        };
        checks.Add(key, check);
        Controls.Add(check);
        top += 22;
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
        Controls.Add(caption);
        top += 22;
    }

    private void Save()
    {
        value.ShowWidget = checks["show-widget"].Checked;
        value.ShowNotifications = checks["show-notices"].Checked;
        value.SubagentNotices = checks["subagent-notices"].Checked;
        value.FieldWorkspace = checks["field-workspace"].Checked;
        value.FieldTask = checks["field-task"].Checked;
        value.FieldStatus = checks["field-status"].Checked;
        value.FieldTime = checks["field-time"].Checked;
        value.FieldPath = checks["field-path"].Checked;
        value.WidgetCounts = checks["widget-counts"].Checked;
        value.CompactWidget = checks["compact-widget"].Checked;
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

public sealed class DshNotifyGui : Form
{
    private const int WidgetWidth = 248;
    private const int CompactHeight = 115;
    private const int ExpandedHeight = 320;
    private const int NoticeStackWidth = 352;
    private const int NoticeStackHeightLimit = 420;
    private const int NonClientLeftButtonDown = 0x00A1;
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
    private readonly Button compactToggle = new Button();
    private readonly Dictionary<string, WidgetRow> widgetRows = new Dictionary<string, WidgetRow>();
    private readonly Dictionary<string, Dictionary<string, object>> notices =
        new Dictionary<string, Dictionary<string, object>>();
    private readonly List<string> noticeOrder = new List<string>();
    private readonly List<Form> settingsForms = new List<Form>();
    private string lastWidgetSnapshot;
    private static readonly Font CardTitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
    private static readonly Font CardDetailFont = new Font("Segoe UI", 8.25F, FontStyle.Regular);
    private bool widgetUserMoving;
    private bool noticesManuallyHidden;
    private bool isShuttingDown;
    private bool lastShowWidget = true;
    private bool lastShowNotifications = true;

    public Settings Settings { get; private set; }

    public DshNotifyGui(bool demo)
        : this(demo, null)
    {
    }

    public DshNotifyGui(bool demo, Action<string> callback)
    {
        outputCallback = callback;
        if (outputCallback == null)
        {
            output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.AutoFlush = true;
        }
        Settings = new Settings();
        Text = (demo ? "[ДЕМО] " : "") + "Рабочие области";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Graphite.Base;
        ClientSize = new Size(WidgetWidth, CompactHeight);
        Font = new Font("Segoe UI", 9F);
        Panel header = new Panel();
        header.Height = 30;
        header.Dock = DockStyle.Top;
        header.BackColor = Color.FromArgb(38, 42, 49);
        header.MouseDown += DragWidget;
        FlowLayoutPanel headerActions = new FlowLayoutPanel();
        headerActions.Dock = DockStyle.Right;
        headerActions.Width = 86;
        headerActions.Padding = new Padding(2, 3, 2, 3);
        headerActions.WrapContents = false;
        Label headerTitle = new Label();
        headerTitle.Text = demo ? "Области · демо" : "Рабочие области";
        headerTitle.ForeColor = Color.FromArgb(232, 234, 237);
        headerTitle.Location = new Point(10, 7);
        headerTitle.AutoSize = true;
        headerTitle.MouseDown += DragWidget;
        Button settingsButton = new Button();
        settingsButton.Text = "⚙";
        settingsButton.AccessibleName = "Настройки";
        settingsButton.Size = new Size(26, 24);
        StyleHeaderButton(settingsButton);
        settingsButton.Click += delegate
        {
            ShowSettings();
        };
        rows.Dock = DockStyle.Fill;
        rows.Padding = new Padding(4);
        rows.BackColor = Graphite.Base;
        rows.AccessibleName = "Список рабочих областей";
        Controls.Add(rows);
        compact = new CompactWidgetControl();
        compact.Dock = DockStyle.Fill;
        Controls.Add(compact);
        compactToggle.Text = "›";
        compactToggle.Size = new Size(25, 24);
        StyleHeaderButton(compactToggle);
        compactToggle.AccessibleName = "Развернуть список";
        compactToggle.Click += delegate { ToggleCompactMode(); };
        Button hideButton = new Button();
        hideButton.Text = "×";
        hideButton.AccessibleName = "Скрыть виджет";
        hideButton.Size = new Size(25, 24);
        StyleHeaderButton(hideButton);
        hideButton.Click += delegate { Hide(); };
        header.Controls.Add(headerTitle);
        headerActions.Controls.Add(settingsButton);
        headerActions.Controls.Add(compactToggle);
        headerActions.Controls.Add(hideButton);
        header.Controls.Add(headerActions);
        Controls.Add(header);
        cards.Dock = DockStyle.Fill;
        cards.Padding = new Padding(6);
        cards.BackColor = Graphite.Base;
        cards.AccessibleName = "Список уведомлений";
        stack.FormBorderStyle = FormBorderStyle.None;
        stack.BackColor = Graphite.Border;
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
        tray.Icon = SystemIcons.Information;
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
        else if (type == "notice.show")
        {
            StoreNotice(message);
            RenderNotices();
            if (Settings.ShowNotifications)
            {
                if (!noticesManuallyHidden)
                {
                    ShowNotices();
                }
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
            if (!String.IsNullOrEmpty(row.Id))
            {
                widgetRows[row.Id] = row;
            }
        }
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

    private void StoreNotice(Dictionary<string, object> message)
    {
        string id = TextOf(message, "id");
        if (String.IsNullOrEmpty(id))
        {
            return;
        }
        if (!notices.ContainsKey(id))
        {
            noticeOrder.Add(id);
        }
        notices[id] = new Dictionary<string, object>(message);
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
        compactToggle.Text = Settings.CompactWidget ? "›" : "‹";
        compactToggle.AccessibleName = Settings.CompactWidget ? "Развернуть список" : "Свернуть список";
        if (Settings.CompactWidget)
        {
            compact.UpdateRows(visibleRows, Settings);
            ResizeWidget(CompactHeight);
            return;
        }
        ResizeWidget(ExpandedHeight);
        int firstItem = rows.FirstIndex;
        rows.SuspendLayout();
        try
        {
            ClearCards(rows);
            int width = Math.Max(1, ClientSize.Width - rows.Padding.Horizontal);
            foreach (WidgetRow row in visibleRows.OrderBy(row => IsCompleted(row.Status) ? 0 : 1))
            {
                rows.Controls.Add(MakeWidgetCard(row, width));
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
        while (container.Controls.Count > 0) container.Controls[0].Dispose();
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
        Label title = new Label
        {
            Dock = DockStyle.Fill,
            Text = demo ? "Уведомления · демо" : "Уведомления",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = CardTitleFont,
            ForeColor = Graphite.Secondary
        };
        MouseEventHandler drag = delegate(object sender, MouseEventArgs args)
        {
            DragWindow(stack, sender, args);
        };
        header.MouseDown += drag;
        title.MouseDown += drag;
        Button hide = NoticeCloseButton("Скрыть уведомления");
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
            int width = Math.Min(161, TextRenderer.MeasureText(caption, CardDetailFont).Width + 22);
            Label chip = new Label
            {
                Text = "• " + caption,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Font = CardDetailFont,
                ForeColor = Graphite.Accent(status),
                BackColor = Graphite.ChipFill(status),
                Bounds = new Rectangle(titleRight - width, 8, width, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
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
        if (Settings.FieldTask && !String.IsNullOrWhiteSpace(task) && task != "Задача не указана")
        {
            Size measured = TextRenderer.MeasureText(task, Font, new Size(card.Width - 24, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int height = Math.Min(Font.Height * 2 + 3, Math.Max(Font.Height + 1, measured.Height));
            AddNoticeLabel(card, task, Font, Graphite.Primary, top, card.Width - 24, height);
            top += height + 5;
        }

        List<string> metadata = new List<string>();
        if (Settings.FieldTime)
        {
            string duration = FormatNoticeDuration(TextOf(notice, "duration"));
            if (duration != "Время неизвестно") AddLine(metadata, duration);
        }
        if (Settings.FieldPath) AddLine(metadata, TextOf(notice, "path"));
        if (metadata.Count > 0)
        {
            AddNoticeLabel(card, String.Join("  ·  ", metadata.ToArray()), CardDetailFont, Graphite.Secondary,
                top, card.Width - 24, 16);
            top += 17;
        }
        card.Height = top + 8;
        return card;
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
        int contentWidth = Math.Max(1, width - 24);
        string title = String.IsNullOrEmpty(row.Workspace) ? row.Title : row.Workspace;
        int top = 8;
        if (Settings.FieldWorkspace && !String.IsNullOrWhiteSpace(title))
        {
            AddNoticeLabel(card, title, CardTitleFont, Graphite.Primary, top, contentWidth, 19);
            top += 21;
        }
        if (Settings.FieldStatus)
        {
            string status = String.IsNullOrEmpty(row.StatusText) ? StatusLabel(row.Status) : row.StatusText;
            AddNoticeLabel(card, "• " + status, CardDetailFont, Graphite.Accent(row.Status), top, contentWidth, 17);
            top += 20;
        }
        foreach (string detail in WidgetDetails(row))
        {
            AddNoticeLabel(card, detail, CardDetailFont, Graphite.Secondary, top, contentWidth, 17);
            top += 18;
        }
        card.Height = top + 6;
        return card;
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

    private List<string> WidgetDetails(WidgetRow row)
    {
        List<string> lines = new List<string>();
        if (Settings.FieldTask && row.TaskSummary != "Задача не указана")
        {
            AddLine(lines, row.TaskSummary);
        }
        if (Settings.FieldTime)
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
        button.FlatAppearance.BorderColor = Color.FromArgb(52, 58, 68);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 51, 59);
        button.BackColor = Color.FromArgb(38, 42, 49);
        button.ForeColor = Color.FromArgb(169, 177, 187);
        button.TabStop = true;
    }

    private void ToggleCompactMode()
    {
        Settings.CompactWidget = !Settings.CompactWidget;
        UserChanged(Settings);
    }

    private void ResizeWidget(int height)
    {
        Point previous = Location;
        Rectangle area = Screen.FromPoint(previous).WorkingArea;
        Rectangle next = Geometry.ClampToVisible(new Rectangle(previous,
            new Size(WidgetWidth, Math.Min(height, HeightLimit(area, ExpandedHeight)))), area);
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
        SettingsForm form = new SettingsForm(Settings, UserChanged);
        settingsForms.Add(form);
        form.FormClosed += delegate
        {
            settingsForms.Remove(form);
        };
        form.Show();
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
        SettingsForm form = new SettingsForm(settings, delegate(Settings changed) { });
        form.Check("field-time").Checked = false;
        bool checkbox = !settings.FieldTime;
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
        List<string> output = new List<string>();
        DshNotifyGui gui = new DshNotifyGui(false, delegate(string line)
        {
            output.Add(line);
        });
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
            + "{\"id\":\"running\",\"title\":\"Выполняется\",\"status\":\"running\","
            + "\"path\":\"C:\\\\Path\",\"timeText\":\"10:00\"},"
            + "{\"id\":\"completed\",\"title\":\"Завершено\",\"status\":\"completed\"},"
            + "{\"id\":\"error\",\"title\":\"Ошибка\",\"status\":\"error\"},"
            + "{\"id\":\"max\",\"title\":\"Лимит\",\"status\":\"max-tokens\"},"
            + "{\"id\":\"blocked\",\"title\":\"Блок\",\"status\":\"blocked\"},"
            + "{\"id\":\"aborted\",\"title\":\"Отмена\",\"status\":\"aborted\"},"
            + "{\"id\":\"interrupted\",\"title\":\"Прервано\",\"status\":\"interrupted\"},"
            + "{\"id\":\"idle\",\"title\":\"Без задач\",\"status\":\"idle\"},"
            + "{\"id\":\"unknown\",\"title\":\"Неизвестно\",\"status\":\"unknown\"},"
            + "{\"id\":\"disposed\",\"title\":\"Закрыто\",\"status\":\"disposed\"}]}");
        gui.Process("{\"t\":\"notice.show\",\"id\":\"completed\",\"reason\":\"completed\","
            + "\"workspace\":\"Alpha\",\"task\":\"Задача\",\"statusText\":\"Готово\","
            + "\"path\":\"C:\\\\Notice\",\"duration\":\"1 с\"}");
        bool allRowsShown = gui.rows.Controls.Count == 10;
        bool allNoticesShown = gui.cards.Controls.Count == 1;
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
        gui.Dispose();
        bool passed = checkbox && position && clamp && settingsRoundTrip && moveEmitted && moveStored;
        passed = passed && noEcho && allRowsShown && allNoticesShown;
        passed = passed && matrix && fieldsHidden && rowsReplaced && compactToggle && noticePresentation
            && completedRowFirst && completedNoticeFirst && noticeHeightAndWidth && fixedWidgetWidth && wholeCardScroll;
        Console.WriteLine("{\"t\":\"selftest\",\"pass\":" + (passed ? "true" : "false")
            + ",\"checks\":{\"checkbox\":" + JsonBool(checkbox)
            + ",\"position\":" + JsonBool(position)
            + ",\"clamp\":" + JsonBool(clamp)
            + ",\"settingsRoundTrip\":" + JsonBool(settingsRoundTrip)
            + ",\"moveEmitted\":" + JsonBool(moveEmitted)
            + ",\"moveStored\":" + JsonBool(moveStored)
            + ",\"noEcho\":" + JsonBool(noEcho)
            + ",\"allRowsShown\":" + JsonBool(allRowsShown)
            + ",\"allNoticesShown\":" + JsonBool(allNoticesShown)
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
