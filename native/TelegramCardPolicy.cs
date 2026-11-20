using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal static class TelegramCardPolicy
{
    internal const int MaximumChatDataBytes = 1024;
    private static readonly HashSet<string> States = new HashSet<string>(new string[] { "running", "completed", "error", "blocked", "aborted", "interrupted", "idle", "unknown", "disposed", "max-tokens" });
    internal static string Build(IEnumerable<WidgetRow> rows, IEnumerable<Dictionary<string, object>> limits, DateTime snapshot)
    {
        List<Dictionary<string, object>> safeRows = new List<Dictionary<string, object>>();
        foreach (WidgetRow row in (rows ?? new List<WidgetRow>()).Where(row => row != null && row.Registered).OrderByDescending(row => row.Status == "running" || row.Status == "error").ThenBy(row => row.Id).Take(6))
        {
            string name = SafeName(row.Workspace); if (!States.Contains(row.Status ?? String.Empty)) continue;
            safeRows.Add(new Dictionary<string, object> { { "name", name }, { "status", row.Status }, { "seconds", Seconds(row.TimeText) } });
        }
        Dictionary<string, object> root = new Dictionary<string, object> { { "v", 1 }, { "snapshot", UnixTime(snapshot) }, { "rows", safeRows }, { "limits", SafeLimits(limits) } };
        JavaScriptSerializer json = new JavaScriptSerializer();
        string result = json.Serialize(root);
        while (Encoding.UTF8.GetByteCount(result) > MaximumChatDataBytes && safeRows.Count > 0) { safeRows.RemoveAt(safeRows.Count - 1); result = json.Serialize(root); }
        return result;
    }
    internal static string SafeName(string text)
    {
        if (String.IsNullOrWhiteSpace(text) || text.IndexOfAny(new char[] { '\\', '/', ':' }) >= 0 || text.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0) return "Область";
        StringBuilder safe = new StringBuilder(); foreach (char c in text.Trim()) if (!Char.IsControl(c)) safe.Append(c); return safe.Length == 0 ? "Область" : safe.ToString(0, Math.Min(24, safe.Length));
    }
    private static int Seconds(string time) { int value; if (String.IsNullOrEmpty(time)) return 0; StringBuilder digits = new StringBuilder(); foreach (char c in time) { if (c >= '0' && c <= '9') digits.Append(c); else if (digits.Length > 0) break; } return Int32.TryParse(digits.ToString(), out value) ? Math.Max(0, value) : 0; }
    private static List<Dictionary<string, object>> SafeLimits(IEnumerable<Dictionary<string, object>> source)
    {
        List<Dictionary<string, object>> output = new List<Dictionary<string, object>>();
        foreach (Dictionary<string, object> item in source ?? new List<Dictionary<string, object>>())
        {
            string provider = item.ContainsKey("label") ? Convert.ToString(item["label"]) : String.Empty; IEnumerable windows = item.ContainsKey("windows") ? item["windows"] as IEnumerable : null;
            if (String.IsNullOrEmpty(provider) || provider.Length > 20) continue;
            foreach (object raw in windows ?? new object[0])
            {
                Dictionary<string, object> window = raw as Dictionary<string, object>; if (window == null) continue; string label = window.ContainsKey("label") ? Convert.ToString(window["label"]) : provider;
                output.Add(new Dictionary<string, object> { { "label", SafeName(provider + " · " + label) }, { "remaining", Math.Max(0, Math.Min(100, Number(window, "percent"))) }, { "total", 100 }, { "reset", Unix(window, "resetAt") } });
                if (output.Count == 4) return output;
            }
            IEnumerable amounts = item.ContainsKey("amounts") ? item["amounts"] as IEnumerable : null;
            if (amounts != null)
            {
                foreach (object raw in amounts)
                {
                    Dictionary<string, object> amount = raw as Dictionary<string, object>;
                    if (amount == null) continue;
                    string currency = amount.ContainsKey("currency") ? Convert.ToString(amount["currency"]) : String.Empty;
                    if (currency.Length > 8) currency = String.Empty;
                    output.Add(new Dictionary<string, object> { { "label", SafeName(provider) }, { "amount", Number(amount, "balance") }, { "currency", currency } });
                    if (output.Count == 4) return output;
                }
            }
            Dictionary<string, object> credits = item.ContainsKey("credits") ? item["credits"] as Dictionary<string, object> : null;
            if (credits != null && !BooleanOf(credits, "unlimited"))
            {
                output.Add(new Dictionary<string, object> { { "label", SafeName(provider + " · Кредиты") }, { "amount", Number(credits, "balance") } });
                if (output.Count == 4) return output;
            }
            Dictionary<string, object> individual = item.ContainsKey("individualLimit") ? item["individualLimit"] as Dictionary<string, object> : null;
            if (individual != null)
            {
                output.Add(new Dictionary<string, object> { { "label", SafeName(provider + " · Расход") }, { "remaining", Math.Max(0, Math.Min(100, Number(individual, "remainingPercent"))) }, { "amount", Number(individual, "remaining") } });
                if (output.Count == 4) return output;
            }
        }
        return output;
    }
    private static double Number(Dictionary<string, object> value, string key) { double result; return value.ContainsKey(key) && Double.TryParse(Convert.ToString(value[key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !Double.IsInfinity(result) && !Double.IsNaN(result) ? Math.Max(0, Math.Min(result, 999999999)) : 0; }
    private static long Unix(Dictionary<string, object> value, string key) { long result; return value.ContainsKey(key) && Int64.TryParse(Convert.ToString(value[key]), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0 ? result : 0; }
    private static long UnixTime(DateTime value) { return (long)(value.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalSeconds; }
    private static bool BooleanOf(Dictionary<string, object> value, string key) { object result; return value.TryGetValue(key, out result) && result is bool && (bool)result; }
}
