using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

// Synthetic-only smoke tests used by the native safety harness.  They have no
// CredMan, network, timers, or threads: injected delegates run inline.
internal static class TelegramCardTests
{
    private sealed class Fake : ITelegramTransport
    {
        internal int Gets, Creates, TextEdits, WidgetEdits; internal bool MissingEdit, TextNoOp; internal string CreatedText, EditedText;
        internal TelegramReply Create = new TelegramReply { Ok = true, MessageId = 17 };
        public TelegramReply GetChat(string token, long chat) { Gets++; return new TelegramReply { Ok = true, ChatId = chat, ChatType = "private" }; }
        public TelegramReply SendSummary(string token, long chat, string text) { Creates++; CreatedText = text; return Create; }
        public TelegramReply EditText(string token, long chat, int message, string text) { TextEdits++; EditedText = text; return TextNoOp ? new TelegramReply { NotModified = true } : new TelegramReply { Ok = true }; }
        public TelegramReply EditMessageWidgetData(string token, long chat, int message, string data) { WidgetEdits++; return MissingEdit ? new TelegramReply { MissingMessage = true } : new TelegramReply { Ok = true }; }
        public TelegramReply SendText(string token, long chat, string text) { return new TelegramReply { Ok = true }; }
    }
    private sealed class Store
    {
        internal TelegramCredentials Value; internal bool Fail; internal int Saves;
        internal bool Save(TelegramCredentials value) { Saves++; if (Fail) return false; Value = Copy(value); return true; }
    }
    internal static bool SelfTest()
    {
        if (!ParserContract()) return false;
        if (!NoOpDoesNotCreate()) return false;
        if (!PlainTextContract()) return false;
        if (!MetricAndStatusContract()) return false;
        if (!InvariantProjection()) return false;
        if (!MiniAppSignatureContract()) return false;
        if (!RejectsOwnBot()) return false;
        DateTime clock = new DateTime(2026, 1, 1); Fake fake = new Fake(); Store store = new Store { Fail = false };
        Action<Action> inline = delegate(Action action) { action(); };
        TelegramPublisher publisher = new TelegramPublisher(fake, inline, inline, delegate { return clock; }, store.Save);
        TelegramCredentials settings = new TelegramCredentials { Enabled = true, ErrorNotice = true, BotToken = "123456:abcdefghijklmnopqrstuvwxyz_123", ChatId = 123 };
        if (!publisher.Configure(settings, false)) return false;
        publisher.Tick("{\"snapshot\":1,\"rows\":[]}", TelegramPublisher.StableKey("{\"snapshot\":1,\"rows\":[]}"));
        if (fake.Gets != 1 || fake.Creates != 1 || publisher.Current.MessageId != 17 || publisher.Current.Uncertain || !publisher.Current.IsTextMessage || String.IsNullOrEmpty(fake.CreatedText)) return false;
        clock = clock.AddSeconds(15); publisher.Tick("{\"snapshot\":2,\"rows\":[1]}", TelegramPublisher.StableKey("{\"snapshot\":2,\"rows\":[1]}"));
        if (fake.TextEdits != 1 || fake.WidgetEdits != 0 || TelegramPublisher.StableKey("{\"snapshot\":1}") != TelegramPublisher.StableKey("{\"snapshot\":2}")) return false;
        fake.TextNoOp = true; clock = clock.AddSeconds(15); publisher.Tick("{\"snapshot\":3,\"rows\":[2]}", TelegramPublisher.StableKey("{\"snapshot\":3,\"rows\":[2]}"));
        if (fake.TextEdits != 2) return false;
        clock = clock.AddSeconds(15); publisher.Tick("{\"snapshot\":4,\"rows\":[2]}", TelegramPublisher.StableKey("{\"snapshot\":4,\"rows\":[2]}"));
        if (fake.TextEdits != 2) return false;
        fake.Create = new TelegramReply { Ok = true }; settings.ChatId = 124;
        if (!publisher.Configure(settings, true)) return false;
        publisher.Tick("{\"snapshot\":3}", TelegramPublisher.StableKey("{\"snapshot\":3}"));
        if (!publisher.Current.Uncertain || fake.Creates != 2 || !publisher.Current.IsTextMessage) return false;
        clock = clock.AddMinutes(10); publisher.Tick("{\"snapshot\":4}", TelegramPublisher.StableKey("{\"snapshot\":4}"));
        if (fake.Creates != 2 || !TelegramCredentialStore.ValidToken(settings.BotToken)) return false;
        return LegacyAndRestartContract(inline, clock);
    }
    private static bool RejectsOwnBot()
    {
        Fake fake = new Fake();
        Store store = new Store();
        Action<Action> inline = delegate(Action action) { action(); };
        TelegramPublisher publisher = new TelegramPublisher(fake, inline, inline,
            delegate { return new DateTime(2026, 1, 1); }, store.Save);
        TelegramCredentials wrong = new TelegramCredentials
        {
            Enabled = true, BotToken = "123456:abcdefghijklmnopqrstuvwxyz_123", ChatId = 123456
        };
        publisher.Load(wrong);
        publisher.Tick("{\"rows\":[]}", "test");
        bool rejected = false;
        publisher.VerifyDraft(wrong, delegate(TelegramPublisher.TelegramVerifyResult result) { rejected = !result.Confirmed; });
        if (!rejected || publisher.Configure(wrong, false) || fake.Gets != 0 || fake.Creates != 0 || store.Saves != 0) return false;
        wrong.Enabled = false;
        return publisher.Configure(wrong, false) && store.Saves == 1 && fake.Gets == 0;
    }
    private static bool ParserContract()
    {
        TelegramReply missingOk = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"error_code\":400,\"description\":\"Bad Request: message to edit not found\"}");
        TelegramReply contradictory = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":true,\"error_code\":400}");
        TelegramReply stringCode = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":\"400\"}");
        TelegramReply definite = TelegramTransport.ParseBody((HttpStatusCode)429, "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":31}}");
        TelegramReply mismatch = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":31}}");
        TelegramReply unchanged = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: message is not modified\"}");
        TelegramReply unchangedLong = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message\"}");
        TelegramReply changedText = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: message is not modified now\"}");
        return missingOk.Uncertain && !missingOk.MissingMessage && missingOk.RetryAfterSeconds == 0
            && contradictory.Uncertain && !contradictory.MissingMessage && contradictory.RetryAfterSeconds == 0
            && stringCode.Uncertain && !stringCode.MissingMessage && stringCode.RetryAfterSeconds == 0
            && !definite.Uncertain && definite.RetryAfterSeconds == 31
            && mismatch.Uncertain && !mismatch.MissingMessage && mismatch.RetryAfterSeconds == 0
            && !unchanged.Uncertain && unchanged.NotModified && !unchangedLong.Uncertain && unchangedLong.NotModified
            && !changedText.Uncertain && !changedText.NotModified;
    }
    private static bool NoOpDoesNotCreate()
    {
        Fake fake = new Fake { Create = new TelegramReply { NotModified = true } }; Store store = new Store();
        Action<Action> inline = delegate(Action action) { action(); };
        TelegramPublisher publisher = new TelegramPublisher(fake, inline, inline, delegate { return new DateTime(2026, 1, 1); }, store.Save);
        if (!publisher.Configure(new TelegramCredentials { Enabled = true, BotToken = "123456:abcdefghijklmnopqrstuvwxyz_123", ChatId = 789 }, false)) return false;
        publisher.Tick("{\"snapshot\":1}", "one");
        return fake.Creates == 1 && publisher.Current.MessageId == 0 && !publisher.Current.Uncertain;
    }
    private static bool PlainTextContract()
    {
        string data = "{\"snapshot\":1767225600,\"rows\":[{\"name\":\"Тест\",\"status\":\"running\",\"seconds\":75}],\"limits\":[{\"label\":\"Лимит\",\"remaining\":83.5,\"reset\":1767229200},{\"label\":\"Баланс\",\"amount\":12.5,\"currency\":\"USD\"}]}";
        string text = TelegramTextFormatter.Format(data);
        string unsafeData = "{\"snapshot\":1,\"rows\":[{\"name\":\"C:\\\\private\",\"status\":\"running\",\"seconds\":0}],\"limits\":[]}";
        string localTimestamp = TimeZoneInfo.ConvertTime(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Local).ToString("dd.MM.yyyy HH:mm zzz", CultureInfo.InvariantCulture);
        return text.IndexOf("🟢") >= 0 && text.IndexOf("1 мин 15 с") >= 0 && text.IndexOf("[████████░░]") >= 0
            && text.IndexOf("зелёный") >= 0 && text.IndexOf("12.5 USD") >= 0 && text.Length <= 4096
            && text.IndexOf(localTimestamp) >= 0
            && TelegramTextFormatter.Format(unsafeData).IndexOf("C:\\private") < 0;
    }
    private static bool MetricAndStatusContract()
    {
        Dictionary<string, object> individual = new Dictionary<string, object> { { "remainingPercent", 50 }, { "remaining", 125 } };
        Dictionary<string, object> provider = new Dictionary<string, object> { { "label", "Тест" }, { "individualLimit", individual } };
        string projected = TelegramCardPolicy.Build(null, new[] { provider }, new DateTime(2026, 1, 1));
        string fromProjection = TelegramTextFormatter.Format(projected);
        string mixed = TelegramTextFormatter.Format("{\"snapshot\":1767225600,\"rows\":[],\"limits\":[{\"label\":\"Метрика\",\"remaining\":50,\"amount\":125,\"currency\":\"USD\",\"reset\":1767229200}]}");
        string statuses = TelegramTextFormatter.Format("{\"rows\":[{\"name\":\"A\",\"status\":\"aborted\",\"seconds\":0},{\"name\":\"I\",\"status\":\"interrupted\",\"seconds\":0},{\"name\":\"D\",\"status\":\"disposed\",\"seconds\":0},{\"name\":\"U\",\"status\":\"unknown\",\"seconds\":0},{\"name\":\"L\",\"status\":\"max-tokens\",\"seconds\":0}],\"limits\":[]}");
        return fromProjection.IndexOf("50%") >= 0 && fromProjection.IndexOf("125") >= 0
            && mixed.IndexOf("50%") >= 0 && mixed.IndexOf("125 USD") >= 0 && mixed.IndexOf("до сброса 1 ч 0 мин") >= 0
            && statuses.IndexOf("Отменено") >= 0 && statuses.IndexOf("Прервано") >= 0 && statuses.IndexOf("Сессия закрыта") >= 0
            && statuses.IndexOf("Состояние неизвестно") >= 0 && statuses.IndexOf("лимит токенов") >= 0;
    }
    private static bool LegacyAndRestartContract(Action<Action> inline, DateTime clock)
    {
        Store store = new Store(); Fake fake = new Fake();
        TelegramCredentials legacy = new TelegramCredentials { Enabled = true, BotToken = "123456:abcdefghijklmnopqrstuvwxyz_123", ChatId = 456, MessageId = 31 };
        TelegramPublisher publisher = new TelegramPublisher(fake, inline, inline, delegate { return clock; }, store.Save);
        publisher.Load(legacy); publisher.Tick("{\"snapshot\":1}", "one");
        if (fake.WidgetEdits != 1 || fake.TextEdits != 0 || publisher.Current.IsTextMessage) return false;
        TelegramPublisher restarted = new TelegramPublisher(fake, inline, inline, delegate { return clock; }, store.Save);
        restarted.Load(publisher.Current); clock = clock.AddMinutes(3); restarted.Tick("{\"snapshot\":2}", "two");
        if (fake.WidgetEdits != 2 || restarted.Current.MessageId != 31) return false;
        fake.Create = new TelegramReply { Ok = true, MessageId = 32 };
        fake.WidgetEdits = 0;
        fake.MissingEdit = true;
        clock = clock.AddMinutes(3); restarted.Tick("{\"snapshot\":3}", "three");
        return fake.WidgetEdits == 1 && fake.Creates == 1 && restarted.Current.MessageId == 32 && restarted.Current.IsTextMessage && !restarted.Current.Uncertain;
    }
    private static bool InvariantProjection()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Dictionary<string, object> window = new Dictionary<string, object> { { "label", "Окно" }, { "percent", 83.5 }, { "resetAt", 1L } };
            Dictionary<string, object> amount = new Dictionary<string, object> { { "currency", "USD" }, { "balance", 12.5 } };
            Dictionary<string, object> provider = new Dictionary<string, object> { { "label", "Тест" }, { "windows", new object[] { window } }, { "amounts", new object[] { amount } } };
            Dictionary<string, object> root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(TelegramCardPolicy.Build(null, new[] { provider }, new DateTime(2026, 1, 1)));
            object raw = root["limits"]; IList limits = raw as IList;
            if (limits == null || limits.Count != 2) return false;
            Dictionary<string, object> quota = limits[0] as Dictionary<string, object>; Dictionary<string, object> balance = limits[1] as Dictionary<string, object>;
            return Math.Abs(Convert.ToDouble(quota["remaining"], CultureInfo.InvariantCulture) - 83.5) < .001
                && Math.Abs(Convert.ToDouble(balance["amount"], CultureInfo.InvariantCulture) - 12.5) < .001;
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
    private static bool MiniAppSignatureContract()
    {
        string actual = HarnessMiniAppFeed.Signature("999999:abcdefghijklmnopqrstuvwxyz_123456", "42", 1, Encoding.UTF8.GetBytes("{}"));
        return actual == "7b6877cc7c13d49e5eac59604fe7f496bbbcd0701f90ae9f8494d0130261af8f";
    }
    private static TelegramCredentials Copy(TelegramCredentials value) { return new TelegramCredentials { Enabled = value.Enabled, ErrorNotice = value.ErrorNotice, BotToken = value.BotToken, ChatId = value.ChatId, MessageId = value.MessageId, Uncertain = value.Uncertain, IsTextMessage = value.IsTextMessage }; }
}
