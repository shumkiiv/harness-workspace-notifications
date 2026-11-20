using System;
using System.Drawing;
using System.Windows.Forms;

internal static class TelegramSettingsUi
{
    private const string OwnBotHint = "Введён ID бота. Укажите свой ID пользователя Telegram.";

    // This dialog owns no credentials and never talks to the store.  The
    // publisher owns the one durable record and is called only on this UI thread.
    internal static TelegramCredentials Open(IWin32Window owner, bool desktopErrorsEnabled, TelegramPublisher publisher,
        Action<TelegramCredentials> applied = null, float scale = 1f)
    {
        TelegramCredentials saved = publisher == null ? null : publisher.Current;
        bool hasSaved = saved != null;
        bool showReset = hasSaved && saved.Uncertain;
        int optionsHeight = showReset ? 100 : 72;
        int footerTop = 204 + optionsHeight;
        Size logicalSize = new Size(420, footerTop + 83);
        scale = FitScale(owner, scale, logicalSize);
        TelegramCredentials result = null;
        using (Font body = new Font("Segoe UI", 9f * scale))
        using (Font titleFont = new Font("Segoe UI", 11f * scale, FontStyle.Bold))
        using (Form form = new Form())
        {
            Func<int, int> px = value => (int)Math.Round(scale * value);
            Color surface = ModernTheme.Enabled ? ModernTheme.Surface : Graphite.Base;
            Color card = ModernTheme.Enabled ? ModernTheme.Card : Graphite.Card;
            Color primary = ModernTheme.Enabled ? ModernTheme.TextPrimary : Graphite.Primary;
            Color secondary = ModernTheme.Enabled ? ModernTheme.TextSecondary : Graphite.Secondary;
            Color border = ModernTheme.Enabled ? ModernTheme.CardBorder : Graphite.Border;

            form.Text = "Telegram";
            form.AutoScaleDimensions = new SizeF(96, 96);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Font = body;
            form.BackColor = surface;
            form.ForeColor = primary;
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(px(logicalSize.Width), px(logicalSize.Height));
            form.Resize += delegate { ModernTheme.ApplyWindowRegion(form); };
            ModernTheme.ApplyWindowRegion(form);

            Panel header = new Panel { BackColor = card };
            Place(form, header, px, 0, 0, 420, 42);
            Label title = new Label { Text = "Telegram", Font = titleFont, TextAlign = ContentAlignment.MiddleLeft };
            Place(header, title, px, 16, 0, 348, 42);
            MouseEventHandler drag = delegate(object sender, MouseEventArgs args) { DshNotifyGui.DragWindow(form, sender, args); };
            header.MouseDown += drag;
            title.MouseDown += drag;
            Button close = MakeButton("×", card, primary, border);
            close.AccessibleName = "Закрыть настройки Telegram";
            close.TabStop = false;
            close.Click += delegate { form.Close(); };
            Place(header, close, px, 376, 7, 28, 28);
            form.CancelButton = close;

            Panel fields = MakeCard(surface, card);
            Place(form, fields, px, 16, 56, 388, 130);
            TextBox token = new TextBox { UseSystemPasswordChar = true, MaxLength = 256, Text = hasSaved ? "••••••••" : String.Empty };
            AddField(fields, token, "Токен бота", px, 9, card, surface, primary, border);
            TextBox chat = new TextBox { Text = hasSaved ? saved.ChatId.ToString() : String.Empty };
            AddField(fields, chat, "ID личного чата", px, 65, card, surface, primary, border);

            Panel options = MakeCard(surface, card);
            Place(form, options, px, 16, 196, 388, optionsHeight);
            CheckBox enabled = MakeToggle("Включить карточку", hasSaved && saved.Enabled, hasSaved, card, primary);
            Place(options, enabled, px, 12, 8, 364, 28);
            CheckBox errors = MakeToggle("Отдельное сообщение об ошибке", hasSaved && saved.ErrorNotice, desktopErrorsEnabled, card, primary);
            Place(options, errors, px, 12, 36, 364, 28);
            CheckBox reset = MakeToggle("Создать карточку заново", false, true, card, primary);
            if (showReset) Place(options, reset, px, 12, 64, 364, 28);

            Label state = new Label { ForeColor = secondary, TextAlign = ContentAlignment.MiddleLeft,
                Text = desktopErrorsEnabled ? "Сначала проверьте личный чат." : "Сообщения об ошибках зависят от уведомлений Windows." };
            if (hasSaved && TelegramCredentialStore.IsOwnBotChat(saved.BotToken, saved.ChatId)) state.Text = OwnBotHint;
            Place(form, state, px, 16, footerTop, 388, 32);
            Button verify = MakeButton("Проверить", card, primary, border);
            verify.Enabled = publisher != null;
            Place(form, verify, px, 176, footerTop + 35, 108, 34);
            Button save = MakeButton("Сохранить", card, primary, border);
            save.Enabled = hasSaved;
            if (ModernTheme.Enabled)
            {
                Action updateSave = delegate
                {
                    save.BackColor = save.Enabled ? ModernTheme.Accent : card;
                    save.ForeColor = save.Enabled ? (Graphite.IsDark ? surface : Color.White) : secondary;
                    save.FlatAppearance.BorderColor = save.Enabled ? ModernTheme.Accent : border;
                };
                save.EnabledChanged += delegate { updateSave(); };
                updateSave();
            }
            Place(form, save, px, 296, footerTop + 35, 108, 34);
            string verifiedToken = null; long verifiedChat = 0;
            verify.Click += delegate
            {
                long id; string entered = token.Text == "••••••••" && hasSaved ? saved.BotToken : token.Text;
                if (!Int64.TryParse(chat.Text, out id) || id <= 0 || !TelegramCredentialStore.ValidToken(entered)) { state.Text = "Проверьте токен и ID личного чата."; return; }
                if (TelegramCredentialStore.IsOwnBotChat(entered, id)) { state.Text = OwnBotHint; return; }
                TelegramCredentials candidate = new TelegramCredentials { BotToken = entered, ChatId = id, ErrorNotice = errors.Checked };
                verify.Enabled = false; state.Text = "Проверяем личный чат…";
                publisher.VerifyDraft(candidate, delegate(TelegramPublisher.TelegramVerifyResult checkedChat)
                {
                    if (form.IsDisposed) return;
                    verify.Enabled = true;
                    if (!checkedChat.Confirmed) { state.Text = "Личный чат не подтверждён."; return; }
                    verifiedToken = entered; verifiedChat = id; state.Text = "Личный чат подтверждён."; enabled.Enabled = true; save.Enabled = true;
                });
            };
            save.Click += delegate
            {
                long id; string entered = token.Text == "••••••••" && hasSaved ? saved.BotToken : token.Text;
                if (!Int64.TryParse(chat.Text, out id) || publisher == null) return;
                if (enabled.Checked && TelegramCredentialStore.IsOwnBotChat(entered, id)) { state.Text = OwnBotHint; return; }
                bool changed = !hasSaved || id != saved.ChatId || entered != saved.BotToken;
                if (enabled.Checked && (!hasSaved || !saved.Enabled || changed || reset.Checked) && (verifiedToken != entered || verifiedChat != id)) { state.Text = "Изменённые данные нужно проверить."; return; }
                TelegramCredentials value = new TelegramCredentials { BotToken = entered, ChatId = id, Enabled = enabled.Checked, ErrorNotice = errors.Checked };
                if (!publisher.Configure(value, reset.Checked)) { state.Text = "Не удалось сохранить настройку."; return; }
                result = publisher.Current; if (applied != null) applied(result); form.Close();
            };
            form.ShowDialog(owner);
            if (!showReset) reset.Dispose();
        }
        return result;
    }

    private static float FitScale(IWin32Window owner, float requested, Size logicalSize)
    {
        IntPtr handle = owner == null ? IntPtr.Zero : owner.Handle;
        Rectangle area = handle == IntPtr.Zero ? Screen.FromPoint(Control.MousePosition).WorkingArea : Screen.FromHandle(handle).WorkingArea;
        using (Graphics graphics = Graphics.FromHwnd(handle))
        {
            float width = area.Width * 96f / (logicalSize.Width * graphics.DpiX);
            float height = area.Height * 96f / (logicalSize.Height * graphics.DpiY);
            return Math.Min(requested, Math.Min(width, height));
        }
    }

    private static Panel MakeCard(Color surface, Color card)
    {
        Panel panel = new Panel { BackColor = ModernTheme.Enabled ? surface : card };
        if (ModernTheme.Enabled)
            panel.Paint += delegate(object sender, PaintEventArgs args)
            {
                ModernTheme.PaintCard(args.Graphics, Rectangle.Inflate(panel.ClientRectangle, -1, -1));
            };
        return panel;
    }

    private static Button MakeButton(string text, Color background, Color textColor, Color border)
    {
        Button button = new Button { Text = text, BackColor = background, ForeColor = textColor,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = border;
        return button;
    }

    private static CheckBox MakeToggle(string text, bool selected, bool enabled, Color card, Color primary)
    {
        return new ModernToggle { Text = text, CheckAlign = ContentAlignment.MiddleRight,
            TextAlign = ContentAlignment.MiddleLeft, Checked = selected, Enabled = enabled,
            BackColor = card, ForeColor = primary };
    }

    private static void AddField(Panel fields, TextBox input, string caption, Func<int, int> px, int top,
        Color card, Color surface, Color primary, Color border)
    {
        Label label = new Label { Text = caption, BackColor = card, TextAlign = ContentAlignment.MiddleLeft };
        Place(fields, label, px, 12, top, 364, 19);
        Panel frame = new Panel { BackColor = surface };
        Place(fields, frame, px, 12, top + 22, 364, 28);
        frame.Paint += delegate(object sender, PaintEventArgs args)
        {
            ControlPaint.DrawBorder(args.Graphics, frame.ClientRectangle, border, ButtonBorderStyle.Solid);
        };
        input.AccessibleName = caption;
        input.BorderStyle = BorderStyle.None;
        input.BackColor = surface;
        input.ForeColor = primary;
        frame.Controls.Add(input);
        int inset = px(8);
        EventHandler centerInput = delegate
        {
            int height = input.PreferredHeight;
            input.SetBounds(inset, Math.Max(1, (frame.Height - height) / 2), Math.Max(1, frame.Width - inset * 2), height);
        };
        frame.SizeChanged += centerInput;
        centerInput(frame, EventArgs.Empty);
        frame.Click += delegate { input.Focus(); };
    }

    private static void Place(Control parent, Control child, Func<int, int> px, int left, int top, int width, int height)
    {
        child.SetBounds(px(left), px(top), px(width), px(height));
        parent.Controls.Add(child);
    }
}
