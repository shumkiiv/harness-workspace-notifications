using System;
using System.Windows.Forms;

internal static class TelegramSettingsUi
{
    // This dialog owns no credentials and never talks to the store.  The
    // publisher owns the one durable record and is called only on this UI thread.
    internal static TelegramCredentials Open(IWin32Window owner, bool desktopErrorsEnabled, TelegramPublisher publisher, Action<TelegramCredentials> applied = null)
    {
        TelegramCredentials saved = publisher == null ? null : publisher.Current;
        bool hasSaved = saved != null;
        TelegramCredentials result = null;
        using (Form form = new Form { Text = "Telegram", Width = 410, Height = 245, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
        {
            Label tokenLabel = new Label { Text = "Токен бота", Left = 12, Top = 18, Width = 120 };
            TextBox token = new TextBox { Left = 12, Top = 38, Width = 368, UseSystemPasswordChar = true, MaxLength = 256, Text = hasSaved ? "••••••••" : String.Empty };
            Label idLabel = new Label { Text = "ID личного чата", Left = 12, Top = 68, Width = 180 };
            TextBox chat = new TextBox { Left = 12, Top = 88, Width = 200, Text = hasSaved ? saved.ChatId.ToString() : String.Empty };
            CheckBox enabled = new CheckBox { Text = "Включить карточку", Left = 12, Top = 120, Enabled = hasSaved, Checked = hasSaved && saved.Enabled };
            CheckBox errors = new CheckBox { Text = "Отдельное сообщение об ошибке", Left = 12, Top = 144, Checked = hasSaved && saved.ErrorNotice, Enabled = desktopErrorsEnabled };
            CheckBox reset = new CheckBox { Text = "Создать карточку заново", Left = 12, Top = 168, Checked = false, Visible = hasSaved && saved.Uncertain };
            Label state = new Label { Left = 12, Top = 193, Width = 365, Height = 22, Text = desktopErrorsEnabled ? "Сначала проверьте личный чат." : "Сообщения об ошибках зависят от уведомлений Windows." };
            Button verify = new Button { Text = "Проверить", Left = 220, Top = 86, Width = 78, Enabled = publisher != null };
            Button save = new Button { Text = "Сохранить", Left = 302, Top = 86, Width = 78, Enabled = hasSaved };
            string verifiedToken = null; long verifiedChat = 0;
            verify.Click += delegate
            {
                long id; string entered = token.Text == "••••••••" && hasSaved ? saved.BotToken : token.Text;
                if (!Int64.TryParse(chat.Text, out id) || id <= 0 || !TelegramCredentialStore.ValidToken(entered)) { state.Text = "Проверьте токен и ID личного чата."; return; }
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
                bool changed = !hasSaved || id != saved.ChatId || entered != saved.BotToken;
                if (enabled.Checked && (!hasSaved || !saved.Enabled || changed || reset.Checked) && (verifiedToken != entered || verifiedChat != id)) { state.Text = "Изменённые данные нужно проверить."; return; }
                TelegramCredentials value = new TelegramCredentials { BotToken = entered, ChatId = id, Enabled = enabled.Checked, ErrorNotice = errors.Checked };
                if (!publisher.Configure(value, reset.Checked)) { state.Text = "Не удалось сохранить настройку."; return; }
                result = publisher.Current; if (applied != null) applied(result); form.Close();
            };
            form.Controls.AddRange(new Control[] { tokenLabel, token, idLabel, chat, enabled, errors, reset, state, verify, save }); form.ShowDialog(owner);
        }
        return result;
    }
}
