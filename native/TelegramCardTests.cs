using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Web.Script.Serialization;

// Synthetic-only smoke tests used by the native safety harness.  They have no
// CredMan, network, timers, or threads: injected delegates run inline.
internal static class TelegramCardTests
{
    private sealed class Fake : ITelegramTransport
    {
        internal int Gets, Creates, Edits; internal TelegramReply Create = new TelegramReply { Ok = true, MessageId = 17 };
        public TelegramReply GetChat(string token, long chat) { Gets++; return new TelegramReply { Ok = true, ChatId = chat, ChatType = "private" }; }
        public TelegramReply SendWidget(string token, long chat, string html, string data) { Creates++; return Create; }
        public TelegramReply EditMessageWidgetData(string token, long chat, int message, string data) { Edits++; return new TelegramReply { Ok = true }; }
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
        if (!InvariantProjection()) return false;
        DateTime clock = new DateTime(2026, 1, 1); Fake fake = new Fake(); Store store = new Store { Fail = false };
        Action<Action> inline = delegate(Action action) { action(); };
        TelegramPublisher publisher = new TelegramPublisher(fake, "x", inline, inline, delegate { return clock; }, store.Save);
        TelegramCredentials settings = new TelegramCredentials { Enabled = true, ErrorNotice = true, BotToken = "123456:abcdefghijklmnopqrstuvwxyz_123", ChatId = 123 };
        if (!publisher.Configure(settings, false)) return false;
        publisher.Tick("{\"snapshot\":1,\"rows\":[]}", TelegramPublisher.StableKey("{\"snapshot\":1,\"rows\":[]}"));
        if (fake.Gets != 1 || fake.Creates != 1 || publisher.Current.MessageId != 17 || publisher.Current.Uncertain) return false;
        clock = clock.AddSeconds(15); publisher.Tick("{\"snapshot\":2,\"rows\":[1]}", TelegramPublisher.StableKey("{\"snapshot\":2,\"rows\":[1]}"));
        if (fake.Edits != 1 || TelegramPublisher.StableKey("{\"snapshot\":1}") != TelegramPublisher.StableKey("{\"snapshot\":2}")) return false;
        fake.Create = new TelegramReply { Ok = true }; settings.ChatId = 124;
        if (!publisher.Configure(settings, true)) return false;
        publisher.Tick("{\"snapshot\":3}", TelegramPublisher.StableKey("{\"snapshot\":3}"));
        if (!publisher.Current.Uncertain || fake.Creates != 2) return false;
        clock = clock.AddMinutes(10); publisher.Tick("{\"snapshot\":4}", TelegramPublisher.StableKey("{\"snapshot\":4}"));
        return fake.Creates == 2 && TelegramCredentialStore.ValidToken(settings.BotToken);
    }
    private static bool ParserContract()
    {
        TelegramReply missingOk = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"error_code\":400,\"description\":\"Bad Request: message to edit not found\"}");
        TelegramReply contradictory = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":true,\"error_code\":400}");
        TelegramReply stringCode = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":\"400\"}");
        TelegramReply definite = TelegramTransport.ParseBody((HttpStatusCode)429, "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":31}}");
        TelegramReply mismatch = TelegramTransport.ParseBody(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":31}}");
        return missingOk.Uncertain && !missingOk.MissingMessage && missingOk.RetryAfterSeconds == 0
            && contradictory.Uncertain && !contradictory.MissingMessage && contradictory.RetryAfterSeconds == 0
            && stringCode.Uncertain && !stringCode.MissingMessage && stringCode.RetryAfterSeconds == 0
            && !definite.Uncertain && definite.RetryAfterSeconds == 31
            && mismatch.Uncertain && !mismatch.MissingMessage && mismatch.RetryAfterSeconds == 0;
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
    private static TelegramCredentials Copy(TelegramCredentials value) { return new TelegramCredentials { Enabled = value.Enabled, ErrorNotice = value.ErrorNotice, BotToken = value.BotToken, ChatId = value.ChatId, MessageId = value.MessageId, Uncertain = value.Uncertain }; }
}
