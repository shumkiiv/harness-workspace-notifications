using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

// Formats only TelegramCardPolicy's bounded projection. It never consumes
// Harness source records, paths, transcripts, session ids, or credentials.
internal static class TelegramTextFormatter
{
    internal const int MaximumLength = 4096;
    internal static string Format(string projectedData)
    {
        Dictionary<string, object> root = Parse(projectedData);
        long snapshot = Number(root, "snapshot");
        StringBuilder output = new StringBuilder("Harness");
        AppendRows(output, root.ContainsKey("rows") ? root["rows"] as IEnumerable : null);
        AppendLimits(output, root.ContainsKey("limits") ? root["limits"] as IEnumerable : null, snapshot);
        if (snapshot > 0) Append(output, "Обновлено " + Local(snapshot));
        return output.ToString();
    }

    private static Dictionary<string, object> Parse(string value)
    {
        try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(value) ?? new Dictionary<string, object>(); }
        catch { return new Dictionary<string, object>(); }
    }
    private static void AppendRows(StringBuilder output, IEnumerable rows)
    {
        bool any = false;
        foreach (object raw in rows ?? new object[0])
        {
            Dictionary<string, object> row = raw as Dictionary<string, object>; if (row == null) continue;
            string status = Text(row, "status"), icon; if (!Status(status, out icon)) continue;
            Append(output, icon + " " + StatusName(status) + " · " + Safe(Text(row, "name")) + " · " + Duration(Number(row, "seconds")));
            any = true;
        }
        if (!any) Append(output, "⚪ Нет активных задач");
    }
    private static void AppendLimits(StringBuilder output, IEnumerable limits, long snapshot)
    {
        foreach (object raw in limits ?? new object[0])
        {
            Dictionary<string, object> limit = raw as Dictionary<string, object>; if (limit == null) continue;
            string label = Safe(Text(limit, "label"));
            if (limit.ContainsKey("remaining"))
            {
                int remaining = Math.Max(0, Math.Min(100, (int)Math.Round(Decimal(limit, "remaining"))));
                string line = label + ": [" + Bar(remaining) + "] " + remaining.ToString(CultureInfo.InvariantCulture) + "% · " + Color(remaining);
                if (limit.ContainsKey("amount")) line += " · " + Amount(limit);
                long reset = Number(limit, "reset"); if (reset > snapshot && snapshot > 0) line += " · до сброса " + Duration(reset - snapshot);
                Append(output, line);
            }
            else if (limit.ContainsKey("amount"))
            {
                Append(output, label + ": " + Amount(limit));
            }
        }
    }
    private static void Append(StringBuilder output, string line)
    {
        if (String.IsNullOrEmpty(line) || output.Length + 1 + line.Length > MaximumLength) return;
        output.Append('\n').Append(line);
    }
    private static long Number(Dictionary<string, object> value, string key)
    {
        long result; return value != null && value.ContainsKey(key) && Int64.TryParse(Convert.ToString(value[key], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? Math.Max(0, result) : 0;
    }
    private static double Decimal(Dictionary<string, object> value, string key)
    {
        double result; return value != null && value.ContainsKey(key) && Double.TryParse(Convert.ToString(value[key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && !Double.IsNaN(result) && !Double.IsInfinity(result) ? Math.Max(0, result) : 0;
    }
    private static string Text(Dictionary<string, object> value, string key) { return value != null && value.ContainsKey(key) ? Convert.ToString(value[key], CultureInfo.InvariantCulture) ?? String.Empty : String.Empty; }
    private static string Safe(string value) { return TelegramCardPolicy.SafeName(value); }
    private static string SafeCurrency(string value)
    {
        if (String.IsNullOrEmpty(value) || value.Length > 8) return String.Empty;
        foreach (char c in value) if (!(c >= 'A' && c <= 'Z')) return String.Empty;
        return value;
    }
    private static string Amount(Dictionary<string, object> limit)
    {
        string currency = SafeCurrency(Text(limit, "currency"));
        return Decimal(limit, "amount").ToString("0.##", CultureInfo.InvariantCulture) + (currency.Length == 0 ? String.Empty : " " + currency);
    }
    private static string Bar(int percent) { return new String('█', percent / 10) + new String('░', 10 - percent / 10); }
    private static string Color(int percent) { return percent < 20 ? "красный" : percent < 50 ? "жёлтый" : "зелёный"; }
    private static string Duration(long seconds)
    {
        seconds = Math.Max(0, seconds); if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture) + " с";
        if (seconds < 3600) return (seconds / 60).ToString(CultureInfo.InvariantCulture) + " мин " + (seconds % 60).ToString(CultureInfo.InvariantCulture) + " с";
        return (seconds / 3600).ToString(CultureInfo.InvariantCulture) + " ч " + ((seconds % 3600) / 60).ToString(CultureInfo.InvariantCulture) + " мин";
    }
    private static string Local(long seconds)
    {
        try
        {
            DateTimeOffset utc = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);
            return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).ToString("dd.MM.yyyy HH:mm zzz", CultureInfo.InvariantCulture);
        }
        catch { return String.Empty; }
    }
    private static bool Status(string value, out string icon)
    {
        icon = value == "running" ? "🟢" : value == "completed" ? "✅" : value == "error" ? "🔴" : value == "blocked" ? "🟠" : value == "idle" ? "⚪" : "⚫";
        return value == "running" || value == "completed" || value == "error" || value == "blocked" || value == "aborted" || value == "interrupted" || value == "idle" || value == "disposed" || value == "max-tokens" || value == "unknown";
    }
    private static string StatusName(string value)
    {
        if (value == "running") return "Выполняется"; if (value == "completed") return "Завершено"; if (value == "error") return "Ошибка"; if (value == "blocked") return "Заблокировано"; if (value == "idle") return "Без задач"; if (value == "aborted") return "Отменено"; if (value == "interrupted") return "Прервано"; if (value == "disposed") return "Сессия закрыта"; if (value == "unknown") return "Состояние неизвестно"; if (value == "max-tokens") return "Прервано: лимит токенов"; return "Остановлено";
    }
}
