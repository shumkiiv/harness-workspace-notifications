using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class WidgetQuotaKeys
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int TimeoutMilliseconds = 15000;
    private const int MaximumResponseBytes = 65536;
    private const int MaximumItems = 32;
    private const string TargetPrefix = "HarnessWidget/quota/";

    static WidgetQuotaKeys()
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    public static bool IsProvider(string provider)
    {
        return String.Equals(provider, "deepseek-official", StringComparison.Ordinal)
            || String.Equals(provider, "zai", StringComparison.Ordinal);
    }

    public static void OpenDialog(string provider, bool allowVault, Action<string> changed)
    {
        if (!IsProvider(provider)) return;
        string title = String.Equals(provider, "zai", StringComparison.Ordinal) ? "Z.ai Coding" : "DeepSeek API";
        using (Form form = new Form())
        {
            form.Text = "Ключ для лимитов";
            form.Icon = TaskIcon.Window;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new System.Drawing.Size(390, 178);
            form.BackColor = Graphite.Base;
            form.ForeColor = Graphite.Primary;
            Label name = new Label { Text = title, AutoSize = true, Left = 14, Top = 14, Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = Graphite.Primary, BackColor = Graphite.Base };
            Label help = new Label { Text = "Ключ используется только для лимитов и хранится в Windows.", Left = 14, Top = 40, Width = 362, Height = 30, ForeColor = Graphite.Secondary, BackColor = Graphite.Base };
            Label status = new Label { Left = 14, Top = 74, Width = 362, Height = 18, ForeColor = Graphite.Secondary, BackColor = Graphite.Base };
            TextBox input = new TextBox { Left = 14, Top = 98, Width = 362, UseSystemPasswordChar = true, MaxLength = 512, AccessibleName = "Ключ " + title, BackColor = Graphite.Card, ForeColor = Graphite.Primary };
            Button save = Button("Сохранить", 14, 108, "Сохранить ключ " + title);
            Button remove = Button("Удалить", 130, 92, "Удалить ключ " + title);
            Button close = Button("Готово", 284, 92, "Закрыть ввод ключа");
            close.DialogResult = DialogResult.Cancel;
            bool stored = false;
            int error = 0;
            if (allowVault) stored = HasKey(provider, out error);
            status.Text = allowVault ? (error == 0 ? (stored ? "Состояние: сохранён" : "Состояние: не задан") : "Состояние: хранилище недоступно") : "В демонстрации ключи не используются";
            save.Enabled = allowVault;
            remove.Enabled = allowVault && stored;
            save.Click += delegate
            {
                string value = input.Text;
                input.Clear();
                if (!ValidKey(value)) { status.Text = "Ключ не сохранён: неверный формат"; return; }
                if (!WriteKey(provider, value, out error)) { status.Text = "Ключ не сохранён: ошибка хранилища"; return; }
                value = String.Empty;
                stored = true;
                remove.Enabled = true;
                status.Text = "Состояние: сохранён";
                if (changed != null) changed(provider);
            };
            remove.Click += delegate
            {
                if (!DeleteKey(provider, out error)) { status.Text = "Ключ не удалён: ошибка хранилища"; return; }
                input.Clear();
                stored = false;
                remove.Enabled = false;
                status.Text = "Состояние: не задан";
                if (changed != null) changed(provider);
            };
            form.Controls.Add(name); form.Controls.Add(help); form.Controls.Add(status); form.Controls.Add(input); form.Controls.Add(save); form.Controls.Add(remove); form.Controls.Add(close);
            form.FormClosed += delegate { input.Clear(); };
            form.ShowDialog();
        }
    }

    private static Button Button(string text, int left, int width, string accessibleName)
    {
        Button result = new Button { Text = text, Left = left, Top = 136, Width = width, AccessibleName = accessibleName,
            FlatStyle = FlatStyle.Flat, BackColor = Graphite.Card, ForeColor = Graphite.Primary };
        result.FlatAppearance.BorderColor = Graphite.Border;
        return result;
    }

    public static void FetchAsync(string provider, string requestId, bool demo, Action<Dictionary<string, object>> completed)
    {
        if (!IsProvider(provider) || !ValidRequestId(requestId) || completed == null) return;
        Thread worker = new Thread(new ThreadStart(delegate
        {
            Dictionary<string, object> result = Fetch(provider, requestId, demo);
            completed(result);
        }));
        worker.IsBackground = true;
        worker.Start();
    }

    private static Dictionary<string, object> Fetch(string provider, string requestId, bool demo)
    {
        Dictionary<string, object> result = Result(provider, requestId, demo ? "error" : "absent");
        if (demo) return result;
        string key;
        int error;
        if (!TryReadKey(provider, out key, out error)) return error == 0 ? result : Result(provider, requestId, "error");
        try
        {
            string url = String.Equals(provider, "zai", StringComparison.Ordinal)
                ? "https://api.z.ai/api/monitor/usage/quota/limit"
                : "https://api.deepseek.com/user/balance";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/json";
            request.AllowAutoRedirect = false;
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = String.Equals(provider, "zai", StringComparison.Ordinal) ? key : "Bearer " + key;
            System.Threading.Timer abort = new System.Threading.Timer(delegate(object ignored) { try { request.Abort(); } catch { } }, null, TimeoutMilliseconds, Timeout.Infinite);
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream body = response.GetResponseStream())
                {
                    if (response.StatusCode != HttpStatusCode.OK) return Result(provider, requestId, "error");
                    Dictionary<string, object> payload = Project(provider, ReadBody(body), key);
                    if (payload == null) return Result(provider, requestId, "error");
                    result = Result(provider, requestId, "ok");
                    result["payload"] = payload;
                    return result;
                }
            }
            finally { abort.Dispose(); }
        }
        catch (WebException exception)
        {
            HttpWebResponse response = exception.Response as HttpWebResponse;
            try
            {
                return response != null && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    ? Result(provider, requestId, "rejected") : Result(provider, requestId, "error");
            }
            finally { if (response != null) response.Dispose(); }
        }
        catch
        {
            return Result(provider, requestId, "error");
        }
        finally
        {
            key = String.Empty;
        }
    }

    private static Dictionary<string, object> Result(string provider, string requestId, string status)
    {
        return new Dictionary<string, object> { { "t", "quota.result" }, { "provider", provider }, { "requestId", requestId }, { "status", status } };
    }

    private static string ReadBody(Stream stream)
    {
        if (stream == null) return null;
        byte[] buffer = new byte[4096];
        using (MemoryStream output = new MemoryStream())
        {
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + count > MaximumResponseBytes) return null;
                output.Write(buffer, 0, count);
            }
            byte[] data = output.ToArray();
            try { return Encoding.UTF8.GetString(data); }
            finally { Array.Clear(data, 0, data.Length); Array.Clear(buffer, 0, buffer.Length); }
        }
    }

    private static Dictionary<string, object> Project(string provider, string body, string key)
    {
        if (String.IsNullOrEmpty(body) || !ValidKey(key)) return null;
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes };
            object raw = serializer.DeserializeObject(body);
            Dictionary<string, object> root = raw as Dictionary<string, object>;
            Dictionary<string, object> projected = String.Equals(provider, "zai", StringComparison.Ordinal) ? ProjectZai(root) : ProjectDeepSeek(root);
            return projected != null && serializer.Serialize(projected).IndexOf(key, StringComparison.Ordinal) < 0 ? projected : null;
        }
        catch { return null; }
    }

    private static Dictionary<string, object> ProjectDeepSeek(Dictionary<string, object> root)
    {
        object raw; IEnumerable list;
        if (root == null || !root.TryGetValue("balance_infos", out raw) || (list = raw as IEnumerable) == null) return null;
        List<Dictionary<string, object>> balances = new List<Dictionary<string, object>>();
        foreach (object entry in list)
        {
            Dictionary<string, object> item = entry as Dictionary<string, object>;
            string currency; string balance;
            if (item == null || !Text(item, "currency", out currency) || !Text(item, "total_balance", out balance)) continue;
            if ((currency != "USD" && currency != "CNY") || !DecimalText(balance)) continue;
            balances.Add(new Dictionary<string, object> { { "currency", currency }, { "total_balance", balance } });
            if (balances.Count > MaximumItems) return null;
        }
        return balances.Count == 0 ? null : new Dictionary<string, object> { { "balance_infos", balances } };
    }

    private static Dictionary<string, object> ProjectZai(Dictionary<string, object> root)
    {
        object raw; Dictionary<string, object> data; IEnumerable list;
        if (root == null || !Equals(root.ContainsKey("success") ? root["success"] : null, true) || Number(root, "code") != 200
            || !root.TryGetValue("data", out raw) || (data = raw as Dictionary<string, object>) == null
            || !data.TryGetValue("limits", out raw) || (list = raw as IEnumerable) == null) return null;
        List<Dictionary<string, object>> limits = new List<Dictionary<string, object>>();
        foreach (object entry in list)
        {
            Dictionary<string, object> item = entry as Dictionary<string, object>;
            string type;
            int unit; int number;
            if (item == null || !Text(item, "type", out type) || (type != "TOKENS_LIMIT" && type != "CREDIT_LIMIT")
                || !Integer(item, "unit", out unit) || (unit != 1 && unit != 3 && unit != 6)
                || !Integer(item, "number", out number) || number < 1 || number > 1000) continue;
            Dictionary<string, object> safe = new Dictionary<string, object> { { "type", type }, { "unit", unit }, { "number", number } };
            CopyNumber(item, safe, "remaining", false); CopyNumber(item, safe, "totalValue", false); CopyNumber(item, safe, "usage", false);
            CopyNumber(item, safe, "currentValue", false); CopyNumber(item, safe, "percentage", false); CopyNumber(item, safe, "nextResetTime", true);
            limits.Add(safe);
            if (limits.Count > MaximumItems) return null;
        }
        return limits.Count == 0 ? null : new Dictionary<string, object> { { "success", true }, { "code", 200 }, { "data", new Dictionary<string, object> { { "limits", limits } } } };
    }

    private static void CopyNumber(Dictionary<string, object> input, Dictionary<string, object> output, string name, bool integer)
    {
        object value; double number;
        if (!input.TryGetValue(name, out value) || !Numeric(value, out number)
            || (integer && (number != Math.Floor(number) || number < 946684800000D || number > 4102444800000D))) return;
        output[name] = integer ? Convert.ToInt64(number) : number;
    }

    private static int Number(Dictionary<string, object> value, string name)
    {
        int result;
        return Integer(value, name, out result) ? result : Int32.MinValue;
    }

    private static bool Integer(Dictionary<string, object> value, string name, out int result)
    {
        object raw; double number; result = 0;
        if (!value.TryGetValue(name, out raw) || !Numeric(raw, out number) || number != Math.Floor(number) || number < Int32.MinValue || number > Int32.MaxValue) return false;
        result = Convert.ToInt32(number);
        return true;
    }

    private static bool Numeric(object value, out double result)
    {
        result = 0;
        if (!(value is int) && !(value is long) && !(value is double) && !(value is decimal) && !(value is float)) return false;
        result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return !Double.IsNaN(result) && !Double.IsInfinity(result);
    }

    private static bool Text(Dictionary<string, object> value, string name, out string result)
    {
        object raw; result = null;
        if (!value.TryGetValue(name, out raw)) return false;
        result = raw as string;
        return result != null;
    }

    private static bool DecimalText(string value)
    {
        if (String.IsNullOrEmpty(value) || value.Length > 31) return false;
        int index = value[0] == '-' ? 1 : 0; int digits = 0; int fraction = 0; bool dot = false;
        for (; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '.' && !dot) { dot = true; continue; }
            if (current < '0' || current > '9') return false;
            digits++; if (dot) fraction++;
        }
        return digits > 0 && digits <= 30 && fraction <= 12;
    }

    private static string Target(string provider) { return TargetPrefix + provider; }

    private static bool HasKey(string provider, out int error)
    {
        string key;
        bool result = TryReadKey(provider, out key, out error);
        key = String.Empty;
        return result;
    }

    private static bool TryReadKey(string provider, out string key, out int error)
    {
        key = null; error = 0;
        if (!IsProvider(provider)) { error = 87; return false; }
        IntPtr pointer;
        if (!CredRead(Target(provider), CredentialTypeGeneric, 0, out pointer))
        {
            error = Marshal.GetLastWin32Error();
            if (error == 1168) error = 0;
            return false;
        }
        try
        {
            NativeCredential credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > 1024) { error = 13; return false; }
            byte[] bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try { key = Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
            if (!ValidKey(key)) { key = null; error = 13; return false; }
            return true;
        }
        finally { CredFree(pointer); }
    }

    private static bool WriteKey(string provider, string key, out int error)
    {
        error = 0;
        if (!IsProvider(provider) || !ValidKey(key)) { error = 87; return false; }
        byte[] bytes = Encoding.UTF8.GetBytes(key);
        IntPtr blob = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            NativeCredential credential = new NativeCredential
            {
                Type = CredentialTypeGeneric, TargetName = Target(provider), CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length, Persist = CredentialPersistLocalMachine, UserName = "Harness widget quota"
            };
            if (CredWrite(ref credential, 0)) return true;
            error = Marshal.GetLastWin32Error(); return false;
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
            if (blob != IntPtr.Zero) { byte[] zeros = new byte[bytes.Length]; Marshal.Copy(zeros, 0, blob, zeros.Length); Marshal.FreeHGlobal(blob); }
        }
    }

    private static bool DeleteKey(string provider, out int error)
    {
        error = 0;
        if (!IsProvider(provider)) { error = 87; return false; }
        if (CredDelete(Target(provider), CredentialTypeGeneric, 0)) return true;
        error = Marshal.GetLastWin32Error();
        return error == 1168;
    }

    private static bool ValidKey(string key)
    {
        if (String.IsNullOrEmpty(key) || key.Length < 8 || key.Length > 512) return false;
        foreach (char current in key) if (current < '!' || current > '~') return false;
        return true;
    }

    private static bool ValidRequestId(string requestId)
    {
        if (String.IsNullOrEmpty(requestId) || requestId.Length > 64) return false;
        foreach (char current in requestId) if (!Char.IsLetterOrDigit(current) && current != '-' && current != '_') return false;
        return true;
    }

    public static bool SelfTest()
    {
        Dictionary<string, object> root = new Dictionary<string, object> { { "balance_infos", new object[] {
            new Dictionary<string, object> { { "currency", "USD" }, { "total_balance", "3.5" }, { "echo", "synthetic-secret" } },
            new Dictionary<string, object> { { "currency", "CNY" }, { "total_balance", "synthetic-key-123" } }
        } } };
        Dictionary<string, object> projected = ProjectDeepSeek(root);
        Dictionary<string, object> zai = new Dictionary<string, object>
        {
            { "success", true }, { "code", 200 }, { "data", new Dictionary<string, object> { { "limits", new object[] {
                new Dictionary<string, object> { { "type", "CREDIT_LIMIT" }, { "unit", 3 }, { "number", 1 }, { "percentage", 25.0 }, { "remaining", "synthetic-key-123" } }
            } } } }
        };
        Dictionary<string, object> zaiProjected = ProjectZai(zai);
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<string, object> normal = Project("deepseek-official", "{\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"3.5\"}]}", "12345678");
        Dictionary<string, object> echoedDeepSeek = Project("deepseek-official", "{\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"\\u0031\\u0032\\u0033\\u0034\\u0035\\u0036\\u0037\\u0038\"}]}", "12345678");
        Dictionary<string, object> echoedZai = Project("zai", "{\"success\":true,\"code\":200,\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"unit\":3,\"number\":1,\"remaining\":12345678,\"totalValue\":20000000}]}}", "12345678");
        return IsProvider("zai") && !IsProvider("other") && ValidKey("synthetic-key-123") && !ValidKey("small")
            && projected != null && !serializer.Serialize(projected).Contains("synthetic-secret")
            && !serializer.Serialize(projected).Contains("synthetic-key-123")
            && zaiProjected != null && !serializer.Serialize(zaiProjected).Contains("synthetic-key-123")
            && normal != null && echoedDeepSeek == null && echoedZai == null;
    }
}
