// Generates the golden fixtures for Doba.Core from the design prototype's own JavaScript.
//
//   node tests/Doba.Core.Tests/Golden/generate.js
//
// Loads the `Component` class from docs/design/design-reference.dc.html, runs seed(), the screen projections
// (weekData, monthData, yearData, dayData, inboxData, todayData), renderVals() (header, sidebar, dialog labels) and the
// navigation commands, and writes semantic JSON (no colours or CSS) to ./fixtures. The C# golden tests map Doba.Core's
// records to the same shapes and compare. Node is only needed to regenerate the fixtures, never to run the tests.
'use strict';
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const HTML = path.join(ROOT, 'docs', 'design', 'design-reference.dc.html');
const OUT = path.join(__dirname, 'fixtures');

// ---------- load the prototype ----------

const html = fs.readFileSync(HTML, 'utf8');
const scripts = [...html.matchAll(/<script[^>]*data-dc-script[^>]*>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
const src = scripts.find((s) => s.includes('class Component extends DCLogic'));
if (!src) throw new Error('Component script not found');

class DCLogic {
  constructor(props) { this.props = props || {}; }
  setState(p) { Object.assign(this.state, p); }
}
// wheelScroll debounces with setTimeout; run callbacks synchronously so a scroll commits immediately.
const syncTimers = { setTimeout: (fn) => { fn(); return 0; }, clearTimeout: () => {} };
const Component = new Function('DCLogic', 'setTimeout', 'clearTimeout', src + '\nreturn Component;')(DCLogic, syncTimers.setTimeout, syncTimers.clearTimeout);

const ACCENT = '#9FB4C7';
const PR = { low: 1, med: 2, high: 3, asap: 4 };
const TAB_NAMES = { look: 'Appearance', cal: 'Calendar', tasks: 'Tasks', win: 'Windows', data: 'Data', keys: 'Shortcuts' };

function make(today) {
  const c = new Component({});
  if (today) c.TODAY = today; // seed() already ran with the prototype's own TODAY (2026-11-15)
  return c;
}

const idOf = (s) => (typeof s === 'string' && s[0] === 't' ? +s.slice(1) + 1 : null);

// Captures where a `go`/`open` closure would navigate by stubbing zoom().
function target(c, fn) {
  if (!fn) return null;
  let hit = null;
  const z = c.zoom;
  c.zoom = (view, anchor) => { hit = { view: view, anchor: anchor }; };
  try { fn(null); } finally { c.zoom = z; }
  return hit;
}

function row(c, r) {
  const t = c.state.tasks.find((x) => x.id === r.id);
  return {
    id: idOf(r.id), title: r.title, done: !!t.done, prio: PR[t.prio] || 0, tag: r.tagLabel || '', time: r.time || '',
    meta: r.meta || '', multi: !!t.end, dim: r.op === '0.3' || r.op === '0.2', move: !!r.canMove
  };
}

function span(c, s) {
  const m = /grid-column: (\d+) \/ span (\d+); grid-row: (\d+)/.exec(s.pos);
  return { row: row(c, s), col: +m[1] - 1, span: +m[2], lane: +m[3] - 2, clipL: s.ml === '0', clipR: s.mr === '0' };
}

const more = (o) => (o.hasMore ? +o.more.slice(1, o.more.indexOf(' ')) : 0);
const pct = (p) => parseInt(p, 10);
const tone = (lc) => (lc === '#D9A066' ? 'warning' : (lc === ACCENT ? 'accent' : 'muted'));

function group(c, g) {
  return { key: g.key, label: g.label, count: g.count, tone: tone(g.lc), link: g.link ? target(c, g.go) : null, items: g.items.map((r) => row(c, r)) };
}

function blocks(c, list) {
  return list.map((b) => {
    const t = c.state.tasks.find((x) => x.id === b.id);
    return { row: row(c, b), start: c.mins(t.time), dur: t.dur || 30, range: b.range, top: parseInt(b.top, 10), h: parseInt(b.h, 10), compact: b.dir === 'row' };
  });
}

function week(c, wk, ws) {
  return {
    weekStart: ws,
    rows: wk.rowsList.map((r) => ({
      lanes: (r.tpl.match(/36px/g) || []).length,
      cells: r.cells.map((d, i) => ({
        date: d.key.replace('next-', ''), col: i, wd: d.wd, num: +d.num, mon: d.mon, today: d.isToday, preview: d.isNext,
        pct: pct(d.pct), more: more(d), empty: d.empty, tasks: d.tasks.map((t) => row(c, t))
      })),
      spans: r.spans.map((s) => span(c, s))
    }))
  };
}

function month(c, mo, a) {
  return {
    year: +a.slice(0, 4), month: +a.slice(5, 7), wds: mo.wds.map((w) => w.label),
    weeks: mo.weeks.map((w) => ({
      weekStart: w.key, wn: w.num, cur: w.color === ACCENT, zoom: target(c, w.open), h: parseInt(w.h, 10),
      lanes: (w.rows.match(/24px/g) || []).length,
      days: w.days.map((d) => ({ date: d.key, num: +d.num, inMonth: d.cellBg === 'transparent', today: d.isToday, more: more(d), items: d.items.map((t) => row(c, t)) })),
      spans: w.spans.map((s) => span(c, s))
    }))
  };
}

function year(c, yr, a) {
  return {
    year: +a.slice(0, 4),
    months: yr.months.map((m, i) => {
      const mr = m.rows.find((r) => r.isMore);
      return {
        month: i + 1, name: m.name, cur: m.isCur, past: m.nameColor === '#A1A1A6', stat: m.stat, pct: pct(m.pct),
        more: mr ? +mr.label.slice(1, mr.label.indexOf(' ')) : 0, zoom: target(c, m.open),
        rows: m.rows.filter((r) => r.isItem).map((r) => row(c, r))
      };
    })
  };
}

function day(c, dy, d) {
  return {
    date: d, progress: dy.progress, empty: dy.empty, spans: dy.spans.map((r) => row(c, r)), items: dy.items.map((r) => row(c, r)),
    blocks: blocks(c, dy.blocks), now: dy.hasNow ? { top: parseInt(dy.nowTop, 10), label: dy.nowLabel } : null
  };
}

function inbox(c, inb) {
  return { left: inb.left.map((g) => group(c, g)), right: inb.right.map((g) => group(c, g)), schedCount: inb.schedCount };
}

function today(c, td) {
  return {
    summary: td.summary, pct: pct(td.pct), groups: td.groups.map((g) => group(c, g)), blocks: blocks(c, td.blocks),
    now: { top: parseInt(td.nowTop, 10), label: td.nowLabel }
  };
}

function header(c, v) {
  return { crumbs: v.crumbs.map((k) => ({ label: k.label, target: k.go ? target(c, k.go) : null })), title: v.title, subtitle: v.subtitle };
}

function sidebar(c, v) {
  return { inbox: v.inboxCount, today: v.todayCount, tags: v.lists.map((l) => ({ name: l.key, count: l.count, active: l.pressed === 'true' })) };
}

// ---------- seed ----------

const base = make();
const seed = base.state.tasks.map((t) => ({
  id: idOf(t.id), date: t.date, end: t.end, title: t.title, tag: t.tag, done: t.done, time: t.time, dur: t.dur, prio: PR[t.prio] || 0
}));
const idByTitle = (title) => base.state.tasks.findIndex((t) => t.title === title);

// ---------- screens ----------

const screenCases = [
  // [name, view, anchor, todayOverride, filter, settingsTab]
  ['week-demo', 'week', '2026-11-15'],
  ['week-demo-monday-anchor', 'week', '2026-11-09'],
  ['week-next', 'week', '2026-11-16'],
  ['week-prev', 'week', '2026-11-04'],
  ['week-month-boundary', 'week', '2026-12-02'],
  ['week-year-boundary', 'week', '2026-12-31'],
  ['week-holidays', 'week', '2026-12-21'],
  ['week-vacation', 'week', '2026-08-10'],
  ['week-filter-work', 'week', '2026-11-15', null, 'work'],
  ['month-nov', 'month', '2026-11-15'],
  ['month-dec', 'month', '2026-12-01'],
  ['month-jan-2026', 'month', '2026-01-01'],
  ['month-jan-2027', 'month', '2027-01-01'],
  ['month-aug', 'month', '2026-08-01'],
  ['month-feb', 'month', '2026-02-01'],
  ['month-nov-filter-personal', 'month', '2026-11-15', null, 'personal'],
  ['year-2026', 'year', '2026-11-15'],
  ['year-2027', 'year', '2027-11-01'],
  ['year-2025', 'year', '2025-11-01'],
  ['day-2026-11-15', 'day', '2026-11-15'],
  ['day-2026-11-16', 'day', '2026-11-16'],
  ['day-2026-11-18', 'day', '2026-11-18'],
  ['day-2026-11-20', 'day', '2026-11-20'],
  ['day-2026-11-10', 'day', '2026-11-10'],
  ['day-2026-11-13', 'day', '2026-11-13'],
  ['day-2026-07-01', 'day', '2026-07-01'],
  ['inbox', 'inbox', '2026-11-15'],
  ['inbox-filter-personal', 'inbox', '2026-11-15', null, 'personal'],
  ['today', 'today', '2026-11-15'],
  ['today-filter-work', 'today', '2026-11-15', null, 'work'],
  // Midnight: the same data seen one day later.
  ['midnight-today', 'today', '2026-11-16', '2026-11-16'],
  ['midnight-inbox', 'inbox', '2026-11-16', '2026-11-16'],
  ['midnight-week', 'week', '2026-11-16', '2026-11-16'],
  ['midnight-day', 'day', '2026-11-16', '2026-11-16'],
  ['midnight-month', 'month', '2026-11-16', '2026-11-16'],
  ['midnight-year', 'year', '2026-11-16', '2026-11-16'],
  ['late-today', 'today', '2026-12-28', '2026-12-28'],
  ['late-inbox', 'inbox', '2026-12-28', '2026-12-28'],
  ['settings-look', 'settings', '2026-11-15', null, null, 'look'],
  ['settings-cal', 'settings', '2026-11-15', null, null, 'cal'],
  ['settings-tasks', 'settings', '2026-11-15', null, null, 'tasks'],
  ['settings-win', 'settings', '2026-11-15', null, null, 'win'],
  ['settings-data', 'settings', '2026-11-15', null, null, 'data'],
  ['settings-keys', 'settings', '2026-11-15', null, null, 'keys'],
];

const screens = screenCases.map(([name, view, anchor, todayO, filter, tab]) => {
  const c = make(todayO);
  c.state.view = view;
  c.state.anchor = anchor;
  c.state.filter = filter || null;
  if (tab) c.state.cfgTab = tab;
  const v = c.renderVals();
  const out = {
    name: name, view: view, anchor: anchor, today: c.TODAY, now: c.hhmm(c.NOW), filter: filter || null, tab: TAB_NAMES[tab || 'look'],
    header: header(c, v), sidebar: sidebar(c, v)
  };
  if (view === 'week') out.week = week(c, v.wk, c.weekStart(anchor));
  if (view === 'month') out.month = month(c, v.mo, anchor);
  if (view === 'year') out.year = year(c, v.yr, anchor);
  if (view === 'day') out.day = day(c, v.dy, anchor);
  if (view === 'inbox') out.inbox = inbox(c, v.inb);
  if (view === 'today') out.todayView = today(c, v.td);
  return out;
});

// ---------- dialog ----------

const draftOf = (c, m) => ({
  editId: m.editId ? idOf(m.editId) : null, title: m.title, notes: m.notes, date: m.date, days: m.days, timed: !!m.timed,
  time: m.time, dur: m.dur, prio: PR[m.prio] || 0, tag: m.tag || null,
  calMonth: m.calMonth || ((m.date || c.TODAY).slice(0, 7) + '-01')
});

function labels(c) {
  const nt = c.renderVals().nt;
  const selIdx = (list) => list.findIndex((w) => w.sel === 'true');
  const T = c.TODAY;
  return {
    where: nt.where, date: nt.pDate.value, time: nt.pTime.value, prio: nt.pPrio.value, tag: nt.pList.value,
    hasDate: nt.hasDate, showTime: nt.showTime,
    dateOpts: nt.dateOpts.map((o) => ({ label: o.label, hint: o.hint, selected: o.pressed === 'true' })),
    calLabel: nt.calLabel,
    calDays: nt.calDays.map((d) => {
      const band = d.strip !== 'transparent';
      const parts = d.stripR.split(' ');
      const marked = d.dot !== 'transparent';
      const fg = d.fg === '#0E0E0F' ? 'marked' : (d.fg === ACCENT ? 'today' : (d.fg === '#8C8C92' ? 'past' : (d.fg === '#EDEDEF' ? 'normal' : 'out')));
      return {
        date: d.key, num: d.num, selected: d.pressed === 'true', marked: marked, band: band, bandL: band && parts[0] === '8px', bandR: band && parts[1] === '8px',
        today: d.aria.endsWith(', today'), fg: fg, past: d.key < T
      };
    }),
    days: nt.daysLabel, until: nt.until, range: nt.rangeLabel, hour: selIdx(nt.hours), minute: selIdx(nt.minutes),
    heading: nt.heading, cta: nt.cta, hint: nt.hint, canSave: nt.disabled === 'false'
  };
}

const dialogs = [];
function addDialog(name, c, extra) {
  const entry = Object.assign({ name: name, today: c.TODAY, draft: draftOf(c, c.state.modal), labels: labels(c) }, extra || {});
  // Wheel commits for this draft.
  entry.wheel = [['h', 0], ['h', 14], ['h', 23], ['m', 0], ['m', 7], ['m', 11]].map(([k, idx]) => {
    const keep = Object.assign({}, c.state.modal);
    c.wheelScroll(k, { scrollTop: idx * 34 });
    const r = { hour: k === 'h', index: idx, time: c.state.modal.time, timed: !!c.state.modal.timed };
    c.state.modal = keep;
    return r;
  });
  // createTask for this draft (when it has a title).
  if (String(c.state.modal.title || '').trim()) {
    const keep = Object.assign({}, c.state.modal);
    const before = c.state.tasks;
    c.createTask();
    const t = keep.editId ? c.state.tasks.find((x) => x.id === keep.editId) : c.state.tasks[c.state.tasks.length - 1];
    entry.created = { date: t.date, end: t.end, title: t.title, tag: t.tag || null, prio: PR[t.prio] || 0, done: !!t.done, time: t.time, dur: t.dur, notes: t.notes };
    c.state.tasks = before;
    c.state.modal = keep;
  }
  dialogs.push(entry);
}

// openModal from each screen, with and without a default priority.
for (const [view, anchor] of [['inbox', '2026-11-15'], ['today', '2026-11-15'], ['week', '2026-11-04'], ['day', '2026-11-18'], ['month', '2026-12-01'], ['year', '2026-11-15']]) {
  for (const defPrio of [null, 'high']) {
    const c = make();
    c.state.view = view;
    c.state.anchor = anchor;
    c.state.cfg.defPrio = defPrio;
    c.openModal();
    addDialog('open-' + view + (defPrio ? '-' + defPrio : ''), c, { open: { view: view, anchor: anchor, defPrio: PR[defPrio] || 0, date: 'view' } });
  }
}
// "Add task" rows: moreInbox / moreToday / moreDay.
for (const [fn, view, anchor] of [['moreInbox', 'inbox', '2026-11-15'], ['moreToday', 'today', '2026-11-15'], ['moreDay', 'day', '2026-11-20']]) {
  const c = make();
  c.state.view = view;
  c.state.anchor = anchor;
  c[fn]();
  addDialog(fn, c, { open: { view: view, anchor: anchor, defPrio: 0, date: c.state.modal.date } });
}
// openEdit of representative tasks.
for (const title of ['Plan next week and set top 3 priorities', 'Morning run along the river', 'Write a blog post about the migration', 'Renew passport', 'Holidays', 'Laundry', 'Weekly review']) {
  const c = make();
  c.state.view = 'week';
  c.openEdit('t' + idByTitle(title));
  addDialog('edit-' + title, c, { editTask: idByTitle(title) + 1 });
}
// Hand-made drafts (the prototype's startModal and variations).
const demoModal = {
  title: 'Prepare slides for the Friday demo', notes: 'Outline: problem → live demo → next steps. Ask for feedback on the onboarding flow.',
  date: '2026-11-16', days: 1, timed: true, time: '14:00', dur: 60, prio: 'high', tag: 'work', open: 'date', calMonth: null
};
const variants = [
  ['start-modal', {}],
  ['span-3', { days: 3, date: '2026-11-18' }],
  ['span-month-end', { days: 5, date: '2026-11-29' }],
  ['span-week-wrap', { days: 9, date: '2026-11-13', calMonth: '2026-11-01' }],
  ['span-14-other-month', { days: 14, date: '2026-12-24', calMonth: '2026-11-01' }],
  ['undated', { date: null, days: 1 }],
  ['today-untimed', { date: '2026-11-15', timed: false, prio: null, tag: null }],
  ['wheel-rounding', { time: '09:57', dur: 45 }],
  ['wheel-rounding-2', { time: '18:32', dur: 120 }],
  ['wheel-rounding-3', { time: '07:13', dur: 15, timed: false }],
  ['late-evening', { time: '23:30', dur: 60 }],
  ['dur-30', { dur: 30, date: '2026-11-22' }],
  ['cal-dec', { calMonth: '2026-12-01' }],
  ['cal-feb-2027', { calMonth: '2027-02-01', date: '2027-02-14' }],
  ['cal-oct', { calMonth: '2026-10-01', date: '2026-10-05', prio: 'asap', tag: 'personal' }],
  ['empty-title', { title: '   ' }],
  ['low-learning', { prio: 'low', tag: 'learning', date: '2026-11-23' }],
  ['med', { prio: 'med', date: '2026-11-30' }],
];
for (const [name, v] of variants) {
  const c = make();
  c.state.modal = Object.assign({}, demoModal, v);
  addDialog('draft-' + name, c);
}
// Midnight: dialog labels one day later.
{
  const c = make('2026-11-16');
  c.state.modal = Object.assign({}, demoModal, { date: '2026-11-17' });
  addDialog('draft-midnight', c);
}

// ---------- navigation ----------

const VIEWS = ['inbox', 'today', 'year', 'month', 'week', 'day', 'settings'];
function runNav(name, start, steps, todayO) {
  const c = make(todayO);
  c.state.view = start.view;
  c.state.anchor = start.anchor;
  c.state.cfgTab = start.tab || 'look';
  const snap = () => ({ view: c.state.view, anchor: c.state.anchor, tab: TAB_NAMES[c.state.cfgTab || 'look'] });
  const out = [];
  for (const st of steps) {
    c._anim = null;
    const [cmd, ...args] = st;
    if (cmd === 'zoom') c.zoom(args[0], args[1], null);
    else if (cmd === 'shift') c.shift(args[0]);
    else if (cmd === 'goToday') c.goToday();
    else if (cmd === 'openInbox') c.openInbox();
    else if (cmd === 'goTodayDay') c.goTodayDay();
    else if (cmd === 'section') c.navSection(args[0], args[1]);
    else if (cmd === 'goCalendar') c.goCalendar();
    else if (cmd === 'goSettings') c.goSettings();
    else if (cmd === 'backFromSettings') c.backFromSettings();
    else if (cmd === 'setTab') c.setTab(args[0]);
    else throw new Error(cmd);
    const a = c._anim;
    let kind = a ? a.kind : 'none';
    if (cmd === 'goSettings' && kind === 'in') kind = 'settings-in';
    if (cmd === 'backFromSettings' && kind === 'out') kind = 'settings-out';
    out.push({ cmd: cmd, args: args.map((x) => (cmd === 'setTab' ? TAB_NAMES[x] : x)), after: snap(), kind: kind });
  }
  return { name: name, today: c.TODAY, start: { view: start.view, anchor: start.anchor, tab: TAB_NAMES[start.tab || 'look'] }, steps: out };
}
if (VIEWS.length !== 7) throw new Error('views');

const navigation = [
  runNav('calendar-tour', { view: 'week', anchor: '2026-11-15' }, [
    ['zoom', 'day', '2026-11-18'], ['shift', 1], ['shift', -1], ['shift', -1], ['goCalendar'], ['zoom', 'week', '2026-11-25'],
    ['zoom', 'month', '2026-11-25'], ['shift', 1], ['shift', -1], ['shift', -1], ['shift', -1], ['zoom', 'year', '2026-09-01'],
    ['shift', 1], ['shift', -1], ['shift', -1], ['goToday'], ['zoom', 'month', '2026-11-15'], ['zoom', 'week', '2026-11-09'],
    ['shift', 1], ['shift', 1], ['shift', 1], ['shift', 1], ['shift', 1], ['shift', 1], ['goToday'], ['zoom', 'year', '2026-11-15'],
    ['zoom', 'day', '2026-03-03'], ['goToday'], ['shift', 1], ['goToday'], ['goCalendar'], ['goCalendar'],
    ['zoom', 'month', '2026-01-31'], ['shift', 1], ['shift', 1], ['zoom', 'year', '2026-01-31'], ['shift', -1], ['shift', 1]
  ]),
  runNav('sections', { view: 'week', anchor: '2026-11-18' }, [
    ['openInbox'], ['openInbox'], ['goTodayDay'], ['openInbox'], ['goCalendar'], ['goTodayDay'], ['goTodayDay'], ['goCalendar'],
    ['zoom', 'day', '2026-11-20'], ['openInbox'], ['shift', 1], ['goToday'], ['openInbox'], ['zoom', 'day', '2026-11-16'],
    ['goTodayDay'], ['zoom', 'month', '2026-12-05'], ['goTodayDay'], ['goToday'], ['section', 'month', '2026-12-01'], ['section', 'inbox', '2026-12-01']
  ]),
  runNav('settings', { view: 'day', anchor: '2026-11-20' }, [
    ['goSettings'], ['goSettings'], ['setTab', 'cal'], ['setTab', 'keys'], ['setTab', 'keys'], ['setTab', 'look'], ['setTab', 'data'],
    ['shift', 1], ['backFromSettings'], ['openInbox'], ['goSettings'], ['setTab', 'win'], ['backFromSettings'], ['goSettings'],
    ['goToday'], ['goSettings'], ['goCalendar'], ['goTodayDay'], ['goSettings'], ['setTab', 'tasks'], ['backFromSettings'],
    ['zoom', 'year', '2026-11-15'], ['goSettings'], ['backFromSettings']
  ]),
  runNav('midnight', { view: 'month', anchor: '2026-11-15' }, [
    ['shift', 1], ['shift', -1], ['goToday'], ['zoom', 'week', '2026-11-15'], ['goToday'], ['openInbox'], ['goToday'], ['goTodayDay']
  ], '2026-11-16'),
];

// ---------- write ----------

fs.mkdirSync(OUT, { recursive: true });
// One array element per line keeps the files small and the diffs readable.
const write = (file, data) => fs.writeFileSync(path.join(OUT, file), '[\n' + data.map((x) => JSON.stringify(x)).join(',\n') + '\n]\n');
write('seed.json', seed);
write('screens.json', screens);
write('dialog.json', dialogs);
write('navigation.json', navigation);
console.log('seed', seed.length, 'screens', screens.length, 'dialogs', dialogs.length, 'navigation', navigation.length);
