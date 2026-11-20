using System;
using System.Globalization;
using System.Text.RegularExpressions;

// All mutable state belongs to the WinForms owner. `run` gets immutable work;
// `post` returns completion to that owner. Workers never save or change state.
internal sealed class TelegramPublisher
{
    private const int MinimumUpdateSeconds = 15, HeartbeatSeconds = 120, ErrorSeconds = 300, RetrySeconds = 60, VerifyRetrySeconds = 300;
    private readonly ITelegramTransport transport; private readonly string html;
    private readonly Action<Action> run, post; private readonly Func<DateTime> now; private readonly Func<TelegramCredentials, bool> save;
    private TelegramCredentials current; private string latestData, latestKey, sentKey, verifiedAddress;
    private DateTime sentAt = DateTime.MinValue, retryAt = DateTime.MinValue, errorAt = DateTime.MinValue;
    private Flight active; private int version; private bool errorPending, closed;
    private enum FlightKind { Verify, Edit, Create, Error, DraftVerify }
    private sealed class Flight
    {
        internal readonly FlightKind Kind; internal readonly TelegramCredentials Credentials; internal readonly int Version; internal readonly string Data, Key; internal readonly Action<TelegramVerifyResult> Callback;
        internal Flight(FlightKind kind, TelegramCredentials credentials, int version, string data, string key, Action<TelegramVerifyResult> callback)
        { Kind = kind; Credentials = credentials; Version = version; Data = data; Key = key; Callback = callback; }
    }
    internal sealed class TelegramVerifyResult { internal readonly bool Confirmed; internal TelegramVerifyResult(bool confirmed) { Confirmed = confirmed; } }

    internal TelegramPublisher(ITelegramTransport source, string widgetHtml, Action<Action> background, Action<Action> ownerPost, Func<DateTime> clock, Func<TelegramCredentials, bool> saveCurrent)
    { transport = source; html = widgetHtml ?? String.Empty; run = background; post = ownerPost; now = clock; save = saveCurrent; }
    internal TelegramCredentials Current { get { return Copy(current); } }

    // The UI calls this after construction. Demo never does, so a demo never reads CredMan.
    internal void Load(TelegramCredentials stored) { if (!closed) { current = Copy(stored); verifiedAddress = null; version++; } }

    // reset is permitted only after the UI has explicitly verified the address.
    internal bool Configure(TelegramCredentials draft, bool reset)
    {
        if (closed || draft == null || (draft.Enabled && TelegramCredentialStore.IsOwnBotChat(draft.BotToken, draft.ChatId))) return false;
        bool freshAddress = !SameAddress(current, draft) || reset;
        TelegramCredentials next = Copy(draft);
        if (SameAddress(current, next) && !reset) { next.MessageId = current.MessageId; next.Uncertain = current.Uncertain; }
        else { next.MessageId = 0; next.Uncertain = false; }
        if (!save(next)) return false;
        current = next; version++; verifiedAddress = null;
        if (freshAddress) { sentKey = null; sentAt = DateTime.MinValue; retryAt = DateTime.MinValue; errorAt = DateTime.MinValue; errorPending = false; }
        Drive(); return true;
    }
    internal void Tick(string data, string stableKey) { if (!closed && !String.IsNullOrEmpty(data)) { latestData = data; latestKey = stableKey ?? data; Drive(); } }
    internal void NotifyError() { if (!closed) { errorPending = true; Drive(); } }
    internal void Close() { closed = true; latestData = null; errorPending = false; }
    internal void VerifyDraft(TelegramCredentials draft, Action<TelegramVerifyResult> callback)
    {
        if (callback == null) return;
        if (draft == null || !TokenLooksValid(draft.BotToken) || draft.ChatId <= 0 ||
            TelegramCredentialStore.IsOwnBotChat(draft.BotToken, draft.ChatId))
        {
            callback(new TelegramVerifyResult(false));
            return;
        }
        // A configuration check does not jump ahead of a real publication.
        // The UI can ask again; this preserves the sole in-flight request.
        if (active != null) { callback(new TelegramVerifyResult(false)); return; }
        Dispatch(new Flight(FlightKind.DraftVerify, Copy(draft), version, null, null, callback));
    }
    private void Drive()
    {
        if (closed || active != null || current == null || !current.Enabled ||
            TelegramCredentialStore.IsOwnBotChat(current.BotToken, current.ChatId)) return;
        DateTime time = now(); if (time < retryAt) return;
        if (verifiedAddress != Address(current)) { Dispatch(new Flight(FlightKind.Verify, Copy(current), version, null, null, null)); return; }
        if (current.Uncertain) return; // no timed resend after an ambiguous create or restart
        if (!String.IsNullOrEmpty(latestData) && Due(time))
        {
            if (current.MessageId > 0) Dispatch(new Flight(FlightKind.Edit, Copy(current), version, latestData, latestKey, null));
            else { TelegramCredentials pending = Copy(current); pending.Uncertain = true; if (!save(pending)) return; current = pending; Dispatch(new Flight(FlightKind.Create, Copy(current), version, latestData, latestKey, null)); }
            return;
        }
        if (errorPending && current.ErrorNotice && (time - errorAt).TotalSeconds >= ErrorSeconds) { errorPending = false; errorAt = time; Dispatch(new Flight(FlightKind.Error, Copy(current), version, null, null, null)); }
    }
    private bool Due(DateTime time) { if (sentKey == null) return true; double age = (time - sentAt).TotalSeconds; return sentKey == latestKey ? age >= HeartbeatSeconds : age >= MinimumUpdateSeconds; }
    private void Dispatch(Flight flight)
    {
        active = flight;
        run(delegate
        {
            TelegramReply reply = null;
            try
            {
                if (flight.Kind == FlightKind.Verify || flight.Kind == FlightKind.DraftVerify) reply = transport.GetChat(flight.Credentials.BotToken, flight.Credentials.ChatId);
                else if (flight.Kind == FlightKind.Edit) reply = transport.EditMessageWidgetData(flight.Credentials.BotToken, flight.Credentials.ChatId, flight.Credentials.MessageId, flight.Data);
                else if (flight.Kind == FlightKind.Create) reply = transport.SendWidget(flight.Credentials.BotToken, flight.Credentials.ChatId, html, flight.Data);
                else reply = transport.SendText(flight.Credentials.BotToken, flight.Credentials.ChatId, "Harness: требуется внимание.");
            }
            catch { reply = new TelegramReply { Uncertain = true }; }
            post(delegate { Complete(flight, reply); });
        });
    }
    private void Complete(Flight flight, TelegramReply reply)
    {
        if (!ReferenceEquals(active, flight)) return;
        active = null; // always clear busy, including a stale configuration result
        if (flight.Kind == FlightKind.DraftVerify)
        {
            if (flight.Callback != null) flight.Callback(new TelegramVerifyResult(IsPrivate(reply, flight.Credentials.ChatId)));
            Drive(); return;
        }
        if (closed || flight.Version != version || !SameAddress(current, flight.Credentials)) { Drive(); return; }
        DateTime time = now();
        if (flight.Kind == FlightKind.Verify) { if (IsPrivate(reply, current.ChatId)) verifiedAddress = Address(current); else retryAt = time.AddSeconds(VerifyRetrySeconds); }
        else if (flight.Kind == FlightKind.Edit)
        {
            if (reply != null && reply.Ok) MarkSent(flight, time);
            else if (IsAmbiguous(reply)) Delay(reply, time); // edit is idempotent: retry the same saved message
            else if (reply != null && reply.MissingMessage) { TelegramCredentials missing = Copy(current); missing.MessageId = 0; missing.Uncertain = false; if (save(missing)) current = missing; else Delay(null, time); }
            else Delay(reply, time);
        }
        else if (flight.Kind == FlightKind.Create)
        {
            if (reply != null && reply.Ok && reply.MessageId > 0) { TelegramCredentials created = Copy(current); created.MessageId = reply.MessageId; created.Uncertain = false; if (save(created)) { current = created; MarkSent(flight, time); } }
            else if (IsAmbiguous(reply)) Pause();
            else { TelegramCredentials rejected = Copy(current); rejected.Uncertain = false; if (save(rejected)) current = rejected; Delay(reply, time); }
        }
        else if (flight.Kind == FlightKind.Error) Delay(reply, time);
        Drive();
    }
    private void Pause() { TelegramCredentials paused = Copy(current); paused.Uncertain = true; if (save(paused)) current = paused; }
    private void Delay(TelegramReply reply, DateTime time) { int seconds = reply == null ? 0 : reply.RetryAfterSeconds; retryAt = time.AddSeconds(seconds > 0 ? seconds : RetrySeconds); }
    private void MarkSent(Flight flight, DateTime time) { sentKey = flight.Key; sentAt = time; }
    private static bool IsPrivate(TelegramReply reply, long id) { return reply != null && reply.Ok && reply.ChatId == id && reply.ChatType == "private"; }
    private static bool TokenLooksValid(string token)
    {
        if (String.IsNullOrEmpty(token) || token.Length > 256) return false;
        int split = token.IndexOf(':'); if (split < 6 || split > 16 || split != token.LastIndexOf(':') || token.Length - split - 1 < 20) return false;
        for (int i = 0; i < split; i++) if (token[i] < '0' || token[i] > '9') return false;
        for (int i = split + 1; i < token.Length; i++) { char c = token[i]; if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false; }
        return true;
    }
    private static bool IsAmbiguous(TelegramReply reply) { return reply == null || reply.Uncertain || (reply.Ok && reply.MessageId <= 0); }
    internal static string StableKey(string value) { return String.IsNullOrEmpty(value) ? String.Empty : Snapshot.Replace(value, "\"snapshot\":0"); }
    private static readonly Regex Snapshot = new Regex("\\\"snapshot\\\"\\s*:\\s*-?\\d+", RegexOptions.Compiled);
    private static string Address(TelegramCredentials value) { return value == null ? String.Empty : value.BotToken + "|" + value.ChatId.ToString(CultureInfo.InvariantCulture); }
    private static bool SameAddress(TelegramCredentials a, TelegramCredentials b) { return a != null && b != null && a.BotToken == b.BotToken && a.ChatId == b.ChatId; }
    private static TelegramCredentials Copy(TelegramCredentials value) { return value == null ? null : new TelegramCredentials { Enabled = value.Enabled, ErrorNotice = value.ErrorNotice, BotToken = value.BotToken, ChatId = value.ChatId, MessageId = value.MessageId, Uncertain = value.Uncertain }; }
}
