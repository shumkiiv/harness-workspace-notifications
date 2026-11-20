using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

public sealed class TelegramCredentials
{
    public bool Enabled { get; set; }
    public bool ErrorNotice { get; set; }
    public string BotToken { get; set; }
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public bool Uncertain { get; set; }
    // Absent in old durable records; false preserves widget edits.
    public bool IsTextMessage { get; set; }
}

internal static class TelegramCredentialStore
{
    private const uint Type = 1, Persist = 2;
    private const string Target = "DshNotifyGui/TelegramWidget/v1";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Credential { public uint Flags, Type; public string TargetName, Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint BlobSize; public IntPtr Blob; public uint Persist, AttributeCount; public IntPtr Attributes; public string TargetAlias, UserName; }
    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("Advapi32.dll")] private static extern void CredFree(IntPtr credential);
    internal static bool TryRead(out TelegramCredentials value)
    {
        value = null; IntPtr raw;
        if (!CredRead(Target, Type, 0, out raw)) return false;
        try { Credential stored = (Credential)Marshal.PtrToStructure(raw, typeof(Credential)); byte[] bytes = stored.BlobSize == 0 ? null : Copy(stored.Blob, stored.BlobSize); string text = bytes == null ? null : Encoding.UTF8.GetString(bytes); if (bytes != null) Array.Clear(bytes, 0, bytes.Length); var root = new JavaScriptSerializer().Deserialize<TelegramCredentials>(text); if (root == null || !ValidToken(root.BotToken) || root.ChatId <= 0) return false; value = root; return true; }
        catch { return false; } finally { CredFree(raw); }
    }
    internal static bool Save(TelegramCredentials value)
    {
        if (value == null || !ValidToken(value.BotToken) || value.ChatId <= 0) return false;
        byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)); IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
        try { Marshal.Copy(bytes, 0, blob, bytes.Length); Credential c = new Credential { Type = Type, TargetName = Target, Persist = Persist, BlobSize = (uint)bytes.Length, Blob = blob }; return CredWrite(ref c, 0); }
        finally { Array.Clear(bytes, 0, bytes.Length); if (blob != IntPtr.Zero) { byte[] zero = new byte[bytes.Length]; Marshal.Copy(zero, 0, blob, zero.Length); Marshal.FreeCoTaskMem(blob); } }
    }
    private static byte[] Copy(IntPtr source, uint length) { byte[] bytes = new byte[length]; Marshal.Copy(source, bytes, 0, bytes.Length); return bytes; }
    internal static bool ValidToken(string token)
    {
        if (String.IsNullOrEmpty(token) || token.Length > 256) return false; int split = token.IndexOf(':'); if (split < 6 || split > 16 || split != token.LastIndexOf(':')) return false;
        for (int i = 0; i < split; i++) if (token[i] < '0' || token[i] > '9') return false;
        for (int i = split + 1; i < token.Length; i++) { char c = token[i]; if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false; }
        return token.Length - split - 1 >= 20;
    }

    internal static bool IsOwnBotChat(string token, long chatId)
    {
        if (String.IsNullOrEmpty(token) || chatId <= 0) return false;
        int separator = token.IndexOf(':');
        long botId;
        return separator > 0 && Int64.TryParse(token.Substring(0, separator), NumberStyles.None,
            CultureInfo.InvariantCulture, out botId) && botId == chatId;
    }
}
