(function () {
  var name = '@local/dsh-workspace-notifications';
  var bridge = '/plugins/@local/dsh-workspace-notifications/quota/bridge';
  var status = '/plugins/dsh-openai-codex/auth/status';
  window.__ModuleLoader__.load({ id: name, factory: function () { return { name: name, inject: [], apply: function (ctx) {
    function mount() {
    var stopped = false, busy = false, timer, controller;
    function tick() { if (stopped || busy) return; busy = true;
      controller = new AbortController(); var deadline = setTimeout(function () { if (controller) controller.abort(); }, 15000);
      fetch(bridge, { credentials: 'same-origin', signal: controller.signal }).then(function (r) { return r.ok ? r.json() : null }).then(function (gate) {
        if (stopped || !gate || !gate.enabled) return null;
        return fetch(status, { credentials: 'same-origin', signal: controller.signal }).then(function (r) { return r.ok ? r.json() : null });
      }).then(function (data) {
        if (stopped || !data || data.status !== 'signed-in') return null;
        var windows = []; (data.usage && data.usage.rateLimits || []).forEach(function (rate) { (rate.windows || []).forEach(function (w) {
          if (typeof w.remainingPercent === 'number' && Number.isFinite(w.remainingPercent) && Number.isInteger(w.windowSeconds) && w.windowSeconds > 0 && (w.resetAt == null || Number.isInteger(w.resetAt))) windows.push({ percent: w.remainingPercent, windowSeconds: w.windowSeconds, resetAt: w.resetAt == null ? null : w.resetAt });
        }); });
        if (!stopped && windows.length) return fetch(bridge, { method: 'POST', credentials: 'same-origin', signal: controller.signal, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ provider: 'openai-codex', windows: windows.slice(0, 8) }) });
      }).catch(function () {}).then(function () { clearTimeout(deadline); busy = false; controller = null; });
    }
    tick(); timer = setInterval(tick, 60000); return function () { stopped = true; clearInterval(timer); if (controller) controller.abort(); };
    }
    return ctx.effect ? ctx.effect(mount) : mount();
  } }; } });
}());
