using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

internal interface ITelegramTransport
{
    TelegramReply GetChat(string token, long chatId);
    TelegramReply SendWidget(string token, long chatId, string html, string chatData);
    TelegramReply EditMessageWidgetData(string token, long chatId, int messageId, string chatData);
    TelegramReply SendText(string token, long chatId, string text);
}

internal sealed class TelegramReply
{
    internal bool Ok;
    internal bool MissingMessage;
    internal bool Uncertain;
    internal int RetryAfterSeconds;
    internal long ChatId;
    internal string ChatType;
    internal int MessageId;
}

// The only production transport.  It deliberately returns classifications, never server text.
internal sealed class TelegramTransport : ITelegramTransport
{
    private const int TimeoutMilliseconds = 10000;
    private const int MaximumResponseBytes = 65536;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes };

    static TelegramTransport()
    {
        // This helper targets the legacy .NET Framework. Use the same secure
        // protocol as the quota transport even when no quota reader has run.
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    }

    public TelegramReply GetChat(string token, long chatId) { return Call(token, "getChat", new Dictionary<string, object> { { "chat_id", chatId } }); }
    public TelegramReply SendWidget(string token, long chatId, string html, string chatData)
    {
        return Call(token, "sendWidget", new Dictionary<string, object> { { "chat_id", chatId }, { "html", html }, { "chat_data", chatData }, { "disable_notification", true }, { "protect_content", true } });
    }
    public TelegramReply EditMessageWidgetData(string token, long chatId, int messageId, string chatData)
    {
        return Call(token, "editMessageWidgetData", new Dictionary<string, object> { { "chat_id", chatId }, { "message_id", messageId }, { "chat_data", chatData } });
    }
    public TelegramReply SendText(string token, long chatId, string text)
    {
        return Call(token, "sendMessage", new Dictionary<string, object> { { "chat_id", chatId }, { "text", text }, { "disable_notification", true } });
    }

    private TelegramReply Call(string token, string method, Dictionary<string, object> body)
    {
        TelegramReply reply = new TelegramReply { Uncertain = true };
        if (!TelegramCredentialStore.ValidToken(token) || String.IsNullOrEmpty(method)) return reply;
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.telegram.org/bot" + token + "/" + method);
            request.Method = "POST"; request.ContentType = "application/json"; request.AllowAutoRedirect = false;
            request.Timeout = TimeoutMilliseconds; request.ReadWriteTimeout = TimeoutMilliseconds;
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(body));
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) return Parse(response);
        }
        catch (WebException error)
        {
            HttpWebResponse response = error.Response as HttpWebResponse;
            if (response == null) { reply.Uncertain = true; return reply; }
            try { return Parse(response); } finally { response.Dispose(); }
        }
        catch { reply.Uncertain = true; return reply; }
    }

    private TelegramReply Parse(HttpWebResponse response)
    {
        // A transport response is ambiguous until a real Telegram rejection is
        // fully parsed and its error_code agrees with the HTTP status.  In
        // particular, malformed bodies and 2xx create replies without an id
        // must never make a later create safe.
        TelegramReply reply = new TelegramReply { Uncertain = true };
        try
        {
            string text;
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) { if (output.Length + count > MaximumResponseBytes) return reply; output.Write(buffer, 0, count); }
                text = Encoding.UTF8.GetString(output.ToArray());
            }
            return ParseBody(response.StatusCode, text);
        }
        catch { return reply; }
    }

    // Kept pure so classification can be checked without an HTTP response or
    // a network request.  Only an actual JSON boolean `ok:false` plus a JSON
    // integer error code matching the 4xx status is a definitive rejection.
    internal static TelegramReply ParseBody(HttpStatusCode status, string text)
    {
        TelegramReply reply = new TelegramReply { Uncertain = true };
        try
        {
            Dictionary<string, object> root = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes }.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null) return reply;
            object rawOk; bool hasOk = root.TryGetValue("ok", out rawOk) && rawOk is bool;
            reply.Ok = status == HttpStatusCode.OK && hasOk && (bool)rawOk;
            int statusCode = (int)status; object rawCode;
            bool rejected = hasOk && !(bool)rawOk && statusCode >= 400 && statusCode < 500
                && root.TryGetValue("error_code", out rawCode) && rawCode is int && (int)rawCode == statusCode;
            if (rejected) reply.Uncertain = false;
            object parameters; Dictionary<string, object> values;
            if (rejected && statusCode == 429 && root.TryGetValue("parameters", out parameters)
                && (values = parameters as Dictionary<string, object>) != null && values.ContainsKey("retry_after") && values["retry_after"] is int)
                reply.RetryAfterSeconds = Math.Max(0, (int)values["retry_after"]);
            string description = root.ContainsKey("description") ? root["description"] as string : null;
            reply.MissingMessage = rejected && statusCode == 400 && String.Equals(description, "Bad Request: message to edit not found", StringComparison.Ordinal);
            object result;
            if (reply.Ok && root.TryGetValue("result", out result) && (values = result as Dictionary<string, object>) != null)
            {
                if (values.ContainsKey("id")) Int64.TryParse(Convert.ToString(values["id"], System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out reply.ChatId);
                if (values.ContainsKey("type")) reply.ChatType = values["type"] as string;
                if (values.ContainsKey("message_id")) Int32.TryParse(Convert.ToString(values["message_id"], System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out reply.MessageId);
            }
            return reply;
        }
        catch { return reply; }
    }
}
