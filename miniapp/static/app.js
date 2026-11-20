(() => {
  const q = selector => document.querySelector(selector);
  const text = value => document.createTextNode(String(value));
  const status = {
    running: ['●', 'В работе'], completed: ['✓', 'Готово'], error: ['!', 'Ошибка'],
    blocked: ['◌', 'Заблокировано'], aborted: ['■', 'Отменено'], interrupted: ['■', 'Прервано'],
    disposed: ['·', 'Сессия закрыта'], 'max-tokens': ['!', 'Прервано: лимит токенов'],
    idle: ['·', 'Без дела'], unknown: ['?', 'Неизвестно']
  };
  const demo = { snapshot: Date.now() / 1000 | 0, rows: [{ name: 'Демонстрация', status: 'running', seconds: 75 }], limits: [{ label: 'Codex · 5 ч', remaining: 76.5, total: 100, reset: 1795000000 }, { label: 'Кредиты', amount: 12.5, currency: 'USD' }] };

  const duration = seconds => {
    const value = Math.max(0, Math.floor(Number(seconds) || 0));
    if (value < 60) return value + ' с';
    if (value < 3600) {
      const minutes = Math.floor(value / 60), remainder = value % 60;
      return minutes + ' мин' + (remainder ? ' ' + remainder + ' с' : '');
    }
    const hours = Math.floor(value / 3600), minutes = Math.floor(value % 3600 / 60);
    return hours + ' ч' + (minutes ? ' ' + minutes + ' мин' : '');
  };
  const reset = value => {
    if (!value) return '';
    const seconds = Math.max(0, value - Math.floor(Date.now() / 1000));
    return seconds ? 'сброс через ' + duration(seconds) : 'сброс скоро';
  };
  function row(left, right, className) {
    const item = document.createElement('div'), head = document.createElement('div');
    const label = document.createElement('span'), value = document.createElement('span');
    item.className = 'item'; head.className = 'head'; label.append(text(left)); value.className = className || ''; value.append(text(right)); head.append(label, value); item.append(head);
    return item;
  }
  function limit(value) {
    const denominator = Number.isFinite(value.total) && value.total > 0 ? value.total : 100;
    const percent = Number.isFinite(value.remaining) ? Math.max(0, Math.min(100, value.remaining / denominator * 100)) : null;
    const amount = value.amount != null ? String(value.amount) + (value.currency ? ' ' + value.currency : '') : '';
    const summary = percent == null ? amount : Math.round(percent) + '%' + (amount ? ' · ' + amount : '');
    const item = row(value.label, summary, '');
    if (percent != null) {
      const bar = document.createElement('div'), fill = document.createElement('i'), meta = document.createElement('div');
      bar.className = 'bar ' + (percent < 20 ? 'danger' : percent < 50 ? 'warn' : ''); bar.setAttribute('role', 'progressbar'); bar.setAttribute('aria-label', value.label); bar.setAttribute('aria-valuemin', '0'); bar.setAttribute('aria-valuemax', String(denominator)); bar.setAttribute('aria-valuenow', String(value.remaining)); fill.style.width = percent + '%'; bar.append(fill); meta.className = 'meta'; meta.append(text(value.remaining + '/' + denominator + ' · ' + reset(value.reset))); item.append(bar, meta);
    }
    return item;
  }
  function task(value) { const item = status[value.status] || ['?', 'Неизвестно']; return row(item[0] + ' ' + value.name, item[1] + ' · ' + duration(value.seconds), 'state'); }
  function render(node, values, make, empty) { node.replaceChildren(); values.length ? values.forEach(value => node.append(make(value))) : node.append(text(empty)); }
  function showIssue(message) { q('#fresh').className = 'stale'; q('#fresh').textContent = message; }
  async function load() {
    try {
      let data;
      if (location.hash === '#demo') data = { snapshot: demo, stale: false };
      else {
        const webApp = window.Telegram?.WebApp;
        if (!webApp?.initData) { showIssue('Откройте из Telegram'); return; }
        const initData = webApp.initData;
        const response = await fetch('/harness/api/state', { headers: { 'X-Telegram-Init-Data': initData }, cache: 'no-store' });
        if (response.status === 401) { showIssue('Сеанс истёк — откройте заново'); return; }
        if (!response.ok) throw Error();
        data = await response.json();
      }
      const snapshot = data.snapshot;
      q('#fresh').className = data.stale ? 'stale' : '';
      q('#fresh').textContent = data.stale ? 'Данные устарели' : 'Онлайн';
      q('#stamp').textContent = snapshot ? 'Обновлено ' + new Date(snapshot.snapshot * 1000).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : 'Нет данных';
      render(q('#limits'), snapshot?.limits || [], limit, 'Нет данных'); render(q('#rows'), snapshot?.rows || [], task, 'Нет активных задач');
    } catch { showIssue('Нет связи — повторим'); }
  }
  load(); setInterval(load, 30000); window.Telegram?.WebApp?.ready();
})();
