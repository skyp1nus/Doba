// Generates the golden fixtures for Doba.Core from the design prototype's own JavaScript.
//
//   node tests/Doba.Core.Tests/Golden/generate.js
//
// Loads the `Component` class from docs/design/design-reference.dc.html, runs seed(), the screen projections
// (weekData, monthData, yearData, dayData, inboxData, todayData), renderVals() (header, sidebar, dialog labels, the
// schedule column), the navigation commands and the schedule's layoutDay / sbCompute / sbKey / sbAuto / durLabel / sbVis,
// and writes semantic JSON (no colours or CSS) to ./fixtures. The C# golden tests map Doba.Core's
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
// sbAuto asks for animation frames; the schedule cases below run one frame by hand.
const frames = { next: null };
const raf = { request: (fn) => { frames.next = fn; return 1; }, cancel: () => { frames.next = null; } };
const Component = new Function('DCLogic', 'setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame', src + '\nreturn Component;')(
  DCLogic, syncTimers.setTimeout, syncTimers.clearTimeout, raf.request, raf.cancel);

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

// ---------- schedule ----------
// The schedule column: the overlap layout (layoutDay), the rendered column (renderVals().sc = schedData), the drag math
// (sbCompute), the arrow keys (sbKey), the autoscroll speed (sbAuto), durLabel and the content tiers (sbVis).

const schedule = [];
const px = (v) => parseFloat(v);
const num = (s) => +String(s).slice(1); // synthetic layout ids 't1'..'t9' → 1..9 (string and numeric order agree)

function layoutCase(name, items) {
  const c = make();
  const L = c.layoutDay(items.map(([id, start, dur]) => ({ id: id, start: start, end: start + Math.max(dur, 30) })), 3);
  schedule.push({
    name: 'layout-' + name, kind: 'layout',
    items: items.map(([id, start, dur]) => ({ id: num(id), start: start, dur: dur })),
    pos: items.map(([id]) => {
      const p = L.pos[id];
      return p.hidden ? { id: num(id), hidden: true } : { id: num(id), col: p.col, span: p.span, cols: p.cols };
    }),
    overflows: L.overflows.map((o) => ({ col: o.col, cols: o.cols, start: o.start, end: o.end, ids: o.ids.map(num) }))
  });
}
const at = (n, start, dur) => Array.from({ length: n }, (_, i) => ['t' + (i + 1), start, dur]);
layoutCase('empty', []);
layoutCase('single', [['t1', 570, 60]]);
layoutCase('two-at-once', at(2, 600, 60));
layoutCase('three-at-once', at(3, 600, 60));
layoutCase('four-at-once', at(4, 600, 60));
layoutCase('five-at-once', at(5, 600, 60));
layoutCase('six-at-once', at(6, 600, 60));
layoutCase('partial', [['t1', 600, 60], ['t2', 630, 60]]);
layoutCase('chain', [['t1', 600, 60], ['t2', 645, 75], ['t3', 700, 60]]);
layoutCase('short-inside-long', [['t1', 540, 180], ['t2', 600, 15]]);
layoutCase('touching', [['t1', 600, 60], ['t2', 660, 60]]);
layoutCase('done-next-to-open', [['t1', 840, 90], ['t2', 870, 45], ['t3', 900, 60]]); // 15 Nov: 14:00, 14:30 (done), 15:00
layoutCase('15-next-to-30', [['t1', 600, 15], ['t2', 615, 30]]);
layoutCase('15-then-30-touching', [['t1', 600, 15], ['t2', 630, 30]]);
layoutCase('unspecified-duration', [['t1', 600, 0], ['t2', 620, 0]]);
layoutCase('span-widening', [['t1', 600, 120], ['t2', 600, 30], ['t3', 600, 30], ['t4', 630, 30]]);
layoutCase('overflow-occupies', [['t1', 600, 120], ['t2', 600, 30], ['t3', 600, 30], ['t4', 600, 30], ['t5', 630, 60]]);
layoutCase('equal-starts', [['t3', 600, 60], ['t1', 600, 60], ['t2', 600, 30]]);
layoutCase('two-clusters-with-overflow', [...at(4, 600, 60), ['t5', 780, 30], ['t6', 780, 30], ['t7', 790, 30], ['t8', 795, 45], ['t9', 800, 20]]);
layoutCase('demo-16-nov', [['t1', 540, 180], ['t2', 600, 30], ['t3', 615, 30], ['t4', 620, 40]]);
layoutCase('sixteen-hours', [['t1', 420, 960], ['t2', 1320, 60]]);

// renderVals().sc. `tasks` replaces the seed (ids 't0'..'t9' → 1..10); `ovf` opens the "+N" starting at that minute.
function schedCase(name, view, anchor, o) {
  o = o || {};
  const c = make(o.today);
  if (o.tasks) {
    c.state.tasks = o.tasks.map(([date, title, time, dur, done, prio, tag], i) => ({
      id: 't' + i, date: date, end: null, title: title, tag: tag || null, done: !!done, time: time, dur: dur, prio: prio || null, notes: ''
    }));
  }
  c.state.view = view;
  c.state.anchor = anchor;
  c.state.filter = o.filter || null;
  const d = view === 'today' ? c.TODAY : anchor;
  if (o.ovf != null) c.state.ovf = d + '@' + o.ovf;
  const sc = c.renderVals().sc;
  const day = c.state.tasks.filter((t) => t.date === d && !t.end && t.time && c.mins(t.time) >= 7 * 60);
  const L = c.layoutDay(day.map((t) => { const st = c.mins(t.time); return { id: t.id, start: st, end: st + Math.max(t.dur || 30, 30) }; }), 3);
  const shown = new Set(sc.blocks.map((b) => b.id));
  const byId = (a, b) => a.id - b.id;
  schedule.push({
    name: 'sched-' + name, kind: 'sched', view: view, anchor: anchor, today: c.TODAY, filter: o.filter || null, ovf: o.ovf == null ? null : o.ovf,
    tasks: o.tasks ? c.state.tasks.map((t) => ({ id: idOf(t.id), date: t.date, title: t.title, tag: t.tag, done: t.done, time: t.time, dur: t.dur, prio: PR[t.prio] || 0 })) : null,
    blocks: sc.blocks.map((b) => {
      const p = L.pos[b.id];
      return {
        id: idOf(b.id), col: p.col, span: p.span, cols: p.cols, left: px(b.left), w: px(b.w), top: px(b.top), h: px(b.h),
        meta: b.hasMeta ? b.meta : '', tall: b.dir === 'column', chkTop: px(b.chkTop), gripH: px(b.gripH),
        label: b.label, tip: b.tip, chkLabel: b.chkLabel, dim: b.op !== '1'
      };
    }).sort(byId),
    hidden: day.filter((t) => !shown.has(t.id)).map((t) => idOf(t.id)).sort((a, b) => a - b),
    overflows: sc.ovfs.map((v) => ({
      start: +v.key.slice(v.key.indexOf('@') + 1), left: px(v.left), top: px(v.top), w: px(v.w), h: px(v.h), tall: v.align === 'flex-start',
      text: v.text, label: v.label
    })),
    pop: sc.hasPop ? {
      left: px(sc.pop.left), top: px(sc.pop.top), below: sc.pop.origin === '100% 0%', head: sc.pop.head, label: sc.pop.label,
      rows: sc.pop.items.map((x) => ({ id: idOf(x.key.slice(1)), title: x.title, range: x.range, chkLabel: x.chkLabel, done: x.donePressed === 'true' }))
    } : null
  });
}
schedCase('today', 'today', '2026-11-15');
schedCase('today-filter-work', 'today', '2026-11-15', { filter: 'work' });
schedCase('day-2026-11-15', 'day', '2026-11-15');
schedCase('day-2026-11-16', 'day', '2026-11-16');
schedCase('day-2026-11-16-open', 'day', '2026-11-16', { ovf: 615 });
schedCase('day-2026-11-18', 'day', '2026-11-18');
schedCase('midnight-today', 'today', '2026-11-16', { today: '2026-11-16', ovf: 615 });
schedCase('midnight-day', 'day', '2026-11-16', { today: '2026-11-16' });
const D20 = '2026-11-20';
schedCase('late-overflow-opens-above', 'day', D20, {
  ovf: 1320,
  tasks: [[D20, 'Late one', '22:00', 60], [D20, 'Late two', '22:00', 60], [D20, 'Late three', '22:00', 30], [D20, 'Late four', '22:00', 30, true]]
});
schedCase('mixed', 'day', D20, {
  ovf: 600,
  tasks: [
    [D20, 'Alpha', '10:00', 60], [D20, 'Bravo', '10:00', 60, false, 'low', 'work'], [D20, 'Charlie', '10:00', 30, false, 'high'],
    [D20, 'Delta', '10:00', 45, true], [D20, 'Echo', '10:00', 30, false, 'asap'], [D20, 'Foxtrot', '10:00', 15],
    [D20, 'Golf — done next to open', '12:00', 60, true], [D20, 'Hotel', '12:30', 0],
    [D20, 'India', '14:00', 30], [D20, 'Juliet', '14:00', 30]
  ]
});
schedCase('mixed-filter', 'day', D20, {
  filter: 'work',
  tasks: [[D20, 'Kilo', '09:00', 120, false, null, 'work'], [D20, 'Lima', '09:30', 30], [D20, 'Mike', '06:30', 60], [D20, 'November', '22:30', 30, true]]
});

// sbCompute: drag pointer travel (px) → slot.
const dragRows = [];
const dys = [0, 3, 5.9, 6, -6, 6.1, -6.1, 12, -12, 18, 100, -100, 1000, -1000];
for (const [mode, pairs] of [
  ['move', [[570, 60], [577, 30], [420, 60], [1320, 60], [1350, 30], [420, 960], [420, 1020], [600, 15]]],
  ['resize', [[570, 60], [570, 30], [1370, 30], [1365, 15], [420, 960], [577, 30]]]
]) {
  for (const [s0, d0] of pairs) {
    for (const dy of dys) {
      for (const alt of [false, true]) {
        const c = make();
        c.schedScrollEl = null;
        c.state.drag = { id: 't0', mode: mode, start: s0, dur: d0, s0: s0, d0: d0 };
        c._dp = { id: 't0', active: true, y0: 100, cy: 100 + dy, scale: 1, alt: alt, mode: mode, s0: s0, d0: d0, cur: { start: s0, dur: d0 } };
        c.sbCompute();
        dragRows.push({ mode: mode, s0: s0, d0: d0, dy: dy, alt: alt, start: c.state.drag.start, dur: c.state.drag.dur });
      }
    }
  }
}
schedule.push({ name: 'drag', kind: 'drag', rows: dragRows });

// sbKey: arrow keys on a focused block (dur 0 = unspecified, drawn as 30).
const keyRows = [];
for (const start of [577, 570, 420, 425, 600, 1320, 1350, 1365, 1370]) {
  for (const dur of [30, 60, 15, 0, 960]) {
    for (const key of ['ArrowUp', 'ArrowDown']) {
      for (const shift of [false, true]) {
        for (const alt of [false, true]) {
          const c = make();
          c.state.tasks = [{ id: 't0', date: '2026-11-15', end: null, title: 'Write the weekly update', time: c.hhmm(start), dur: dur, done: false }];
          const e = { key: key, shiftKey: shift, altKey: alt, target: { getAttribute: (k) => (k === 'data-sbm' ? '1' : null) }, preventDefault() {} };
          c.sbKey(e, 't0');
          const t = c.state.tasks[0];
          const changed = c.mins(t.time) !== start || t.dur !== dur;
          keyRows.push({ start: start, dur: dur, down: key === 'ArrowDown', shift: shift, alt: alt, changed: changed, ns: changed ? c.mins(t.time) : null, nd: changed ? t.dur : null, chip: changed ? c.state.kb.m : null });
        }
      }
    }
  }
}
schedule.push({ name: 'keys', kind: 'keys', rows: keyRows });

// sbAuto: one frame of autoscroll for a pointer at y over a scroller [top, bottom].
const autoRows = [];
for (const scale of [1, 1.5]) {
  const top = 100, bottom = 600, z = 40 * scale;
  for (const y of [top - 50, top - 0.5, top, top + 0.5, top + 20, top + z - 0.1, top + z, top + z + 1, 350, bottom - z - 1, bottom - z, bottom - z + 0.1, bottom - 20, bottom, bottom + 0.5, bottom + 50]) {
    const c = make();
    const sc = { scrollHeight: 3000, clientHeight: 500, scrollTop: 1000, getBoundingClientRect: () => ({ top: top, bottom: bottom }) };
    c.schedScrollEl = sc;
    c.sbCompute = () => {};
    c._dp = { active: true, cy: y, scale: scale };
    c._raf = null;
    frames.next = null;
    c.sbAuto();
    const tick = frames.next;
    frames.next = null;
    tick();
    autoRows.push({ y: y, top: top, bottom: bottom, scale: scale, v: sc.scrollTop - 1000 });
  }
}
schedule.push({ name: 'autoscroll', kind: 'autoscroll', rows: autoRows });

// durLabel and the content tiers of sbVis.
schedule.push({ name: 'durations', kind: 'durations', rows: [5, 15, 30, 45, 60, 75, 105, 120, 180, 960, 1005].map((m) => ({ m: m, label: base.durLabel(m) })) });
const tierRows = [];
for (const w of [98, 119, 119.5, 120, 139, 140, 149, 202, 219, 220, 302]) {
  for (const h of [24, 33, 43, 44, 45, 69]) {
    for (const active of [false, true]) {
      const v = base.sbVis({ title: 'x', done: false }, w, h, 570, 105, ACCENT, active);
      tierRows.push({ w: w, h: h, active: active, meta: v.hasMeta ? v.meta : '', tall: v.dir === 'column' });
    }
  }
}
schedule.push({ name: 'tiers', kind: 'tiers', rows: tierRows });

// ---------- write ----------

fs.mkdirSync(OUT, { recursive: true });
// One array element per line keeps the files small and the diffs readable.
const write = (file, data) => fs.writeFileSync(path.join(OUT, file), '[\n' + data.map((x) => JSON.stringify(x)).join(',\n') + '\n]\n');
write('seed.json', seed);
write('screens.json', screens);
write('dialog.json', dialogs);
write('navigation.json', navigation);
write('schedule.json', schedule);
console.log('seed', seed.length, 'screens', screens.length, 'dialogs', dialogs.length, 'navigation', navigation.length, 'schedule', schedule.length);
