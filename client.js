(function () {
  var name = '@local/dsh-workspace-notifications', bridge = '/plugins/@local/dsh-workspace-notifications/quota/bridge', status = '/plugins/dsh-openai-codex/auth/status', wait = '/plugins/@local/dsh-workspace-notifications/session/wait';
  window.__ModuleLoader__.load({ id: name, factory: function () { return { name: name, inject: [], apply: function (ctx) {
    function mountQuota() { var stopped = false, busy = false, timer, controller;
      function tick() { if (stopped || busy) return; busy = true; controller = new AbortController(); var deadline = setTimeout(function () { if (controller) controller.abort(); }, 15000);
        fetch(bridge, { credentials: 'same-origin', signal: controller.signal }).then(function (r) { return r.ok ? r.json() : null }).then(function (gate) { if (stopped || !gate || !gate.enabled) return null; return fetch(status, { credentials: 'same-origin', signal: controller.signal }).then(function (r) { return r.ok ? r.json() : null }); }).then(function (data) {
          if (stopped || !data || data.status !== 'signed-in') return null; var windows = []; (data.usage && data.usage.rateLimits || []).forEach(function (rate) { (rate.windows || []).forEach(function (w) { if (typeof w.remainingPercent === 'number' && Number.isFinite(w.remainingPercent) && Number.isInteger(w.windowSeconds) && w.windowSeconds > 0 && (w.resetAt == null || Number.isInteger(w.resetAt))) windows.push({ percent: w.remainingPercent, windowSeconds: w.windowSeconds, resetAt: w.resetAt == null ? null : w.resetAt }); }); });
          if (!stopped && (windows.length || data.usage && (data.usage.credits || data.usage.individualLimit))) { var usage = data.usage || {}; var payload = { provider: 'openai-codex', windows: windows.slice(0, 8) }; if (usage.credits && (usage.credits.unlimited === true || typeof usage.credits.balance === 'string')) payload.credits = { unlimited: usage.credits.unlimited === true, balance: typeof usage.credits.balance === 'string' ? usage.credits.balance : undefined }; if (usage.individualLimit && (typeof usage.individualLimit.remaining === 'string' || typeof usage.individualLimit.remainingPercent === 'number')) payload.individualLimit = { limit: usage.individualLimit.limit, used: usage.individualLimit.used, remaining: usage.individualLimit.remaining, remainingPercent: usage.individualLimit.remainingPercent }; return fetch(bridge, { method: 'POST', credentials: 'same-origin', signal: controller.signal, headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) }); }
        }).catch(function () {}).then(function () { clearTimeout(deadline); busy = false; controller = null; }); }
      tick(); timer = setInterval(tick, 60000); return function () { stopped = true; clearInterval(timer); if (controller) controller.abort(); };
    }
    function mountSessionOpen(scope) {
      if (location.protocol !== 'dsh-app:' || location.hostname !== 'app' || !scope.uiWorkspace || typeof scope.uiWorkspace.openSession !== 'function') return function () {};
      var stopped = false, controller, timer, delay = 1000;
      function again(waitFor) { if (!stopped) timer = setTimeout(poll, waitFor); }
      function poll() {
        if (stopped) return;
        controller = new AbortController();
        fetch(wait, { method: 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal })
          .then(function (response) {
            if (response.status === 204) { delay = 1000; return null; }
            if (response.status === 200) { delay = 1000; return response.json(); }
            throw new Error('wait failed');
          }).then(function (value) {
            if (!stopped && value && typeof value.sessionId === 'string') {
              var sessionId = value.sessionId;
              scope.uiWorkspace.openSession(sessionId);
            }
            if (!stopped) again(0);
          }).catch(function () {
            if (!stopped) { var next = delay; delay = Math.min(8000, delay * 2); again(next); }
          });
      }
      poll();
      return function () { stopped = true; clearTimeout(timer); if (controller) controller.abort(); };
    }
    var stopQuota = ctx.effect ? ctx.effect(mountQuota) : mountQuota();
    var stopOpen = typeof ctx.inject === 'function' ? ctx.inject(['uiWorkspace'], function (scope) { return scope.effect ? scope.effect(function () { return mountSessionOpen(scope); }) : mountSessionOpen(scope); }) : mountSessionOpen(ctx);
    return function () { if (typeof stopQuota === 'function') stopQuota(); if (typeof stopOpen === 'function') stopOpen(); };
  } }; } });
}());
