using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Optional, independent relay. It never changes Telegram text publication.
internal sealed class HarnessMiniAppFeed
{
    private const int MaximumBytes = TelegramCardPolicy.MaximumChatDataBytes;
    private readonly Action<Action> run, post; private readonly Func<DateTime> now; private readonly Func<string> dataDirectory;
    private bool busy, closed; private string latest, latestKey, sentKey; private TelegramCredentials latestCredentials; private DateTime lastSent = DateTime.MinValue, retryAt = DateTime.MinValue;
    internal HarnessMiniAppFeed(Action<Action> background, Action<Action> ownerPost, Func<DateTime> clock, Func<string> directory)
    { run = background; post = ownerPost; now = clock; dataDirectory = directory; }
    internal void Tick(string data, TelegramCredentials credentials)
    {
        if (closed) return;
        if (!Eligible(data, credentials)) { latest = latestKey = null; latestCredentials = null; return; }
        latest = data; latestKey = TelegramPublisher.StableKey(data); latestCredentials = Copy(credentials); Drive();
    }
    internal void Close() { closed = true; latest = null; latestCredentials = null; }
    private void Drive()
    {
        if (busy || closed || latest == null || now() < retryAt) return;
        DateTime time = now();
        double age = (time - lastSent).TotalSeconds;
        if (sentKey != null && ((sentKey == latestKey && age < 60) || (sentKey != latestKey && age < 30))) return;
        string data = latest, key = latestKey; TelegramCredentials credentials = latestCredentials; latest = latestKey = null; latestCredentials = null; busy = true;
        run(delegate { bool sent = Send(data, credentials); post(delegate { Complete(key, sent); }); });
    }
    private void Complete(string key, bool sent) { busy = false; if (closed) return; if (sent) { sentKey = key; lastSent = now(); } else retryAt = now().AddSeconds(30); Drive(); }
    // Kept internal and deterministic so the native safety harness can inject a transport-free configuration.
    internal bool Send(string data, TelegramCredentials credentials)
    {
        Uri endpoint; if (!Eligible(data, credentials) || !TryEndpoint(out endpoint)) return false;
        try
        {
            byte[] body = Encoding.UTF8.GetBytes(data); if (body.Length > MaximumBytes) return false;
            long timestamp = (long)(now().ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds;
            string owner = credentials.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST"; request.ContentType = "application/json"; request.AllowAutoRedirect = false; request.Timeout = 10000; request.ReadWriteTimeout = 10000; request.ContentLength = body.Length;
            request.Headers["X-Harness-Owner"] = owner; request.Headers["X-Harness-Timestamp"] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Headers["X-Harness-Signature"] = Signature(credentials.BotToken, owner, timestamp, body);
            using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch { return false; }
    }
    private bool TryEndpoint(out Uri endpoint)
    {
        endpoint = null;
        try
        {
            string root = dataDirectory(); string path = Path.Combine(root, "mini-app.json"); if (!File.Exists(path)) return false;
            Dictionary<string, object> value = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            string raw = value == null || !value.ContainsKey("endpoint") ? null : value["endpoint"] as string;
            Uri parsed; if (!Uri.TryCreate(raw, UriKind.Absolute, out parsed) || parsed.Scheme != Uri.UriSchemeHttps || !String.IsNullOrEmpty(parsed.UserInfo) || !String.IsNullOrEmpty(parsed.Query) || !String.IsNullOrEmpty(parsed.Fragment) || parsed.AbsolutePath != "/harness/api/snapshot") return false;
            endpoint = parsed; return true;
        }
        catch { return false; }
    }
    internal static string DefaultDirectory()
    {
        string configured = Environment.GetEnvironmentVariable("DSH_WN_DATA_DIR");
        return !String.IsNullOrWhiteSpace(configured) ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "workspace-notifications");
    }
    internal static string Signature(string token, string owner, long timestamp, byte[] body)
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(token); byte[] key;
        using (HMACSHA256 derive = new HMACSHA256(tokenBytes)) key = derive.ComputeHash(Encoding.UTF8.GetBytes("harness-key-v1"));
        string digest; using (SHA256 hash = SHA256.Create()) digest = Hex(hash.ComputeHash(body));
        byte[] message = Encoding.UTF8.GetBytes("harness-v1|" + owner + "|" + timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + digest);
        using (HMACSHA256 signer = new HMACSHA256(key)) return Hex(signer.ComputeHash(message));
    }
    private static string Hex(byte[] bytes) { StringBuilder output = new StringBuilder(bytes.Length * 2); foreach (byte value in bytes) output.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)); return output.ToString(); }
    private static bool Eligible(string data, TelegramCredentials value) { return value != null && value.Enabled && !String.IsNullOrEmpty(value.BotToken) && value.ChatId > 0 && !TelegramCredentialStore.IsOwnBotChat(value.BotToken, value.ChatId) && data != null && Encoding.UTF8.GetByteCount(data) <= MaximumBytes; }
    private static TelegramCredentials Copy(TelegramCredentials value) { return new TelegramCredentials { Enabled = value.Enabled, BotToken = value.BotToken, ChatId = value.ChatId }; }
}
