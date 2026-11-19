// Each job: wrapper attrs passed to <dc-import name="Main" ...> plus optional real-UI clicks.
const view = (v) => ({ name: v, attrs: { 'start-view': v } });

const settingsTab = (name, idx) => ({
  name, attrs: { 'start-view': 'settings' },
  // nav[aria-label=Settings] buttons: 0 = Back to app, 1..6 = look, cal, tasks, win, data, keys
  act: async (p) => { if (idx > 1) await p.locator('nav[aria-label="Settings"] button').nth(idx).click(); },
});

// Pills in the New task dialog, in order: date, time, priority, tag.
const dialog = (name, idx) => ({
  name, attrs: { 'start-view': 'week', 'start-modal': 'true' },
  act: async (p) => {
    const pills = p.locator('[role="dialog"][aria-modal="true"] button[aria-haspopup]');
    if (idx > 0) {
      await pills.nth(idx).click();
      await p.waitForTimeout(400);
    }
  },
});

module.exports = [
  view('inbox'), view('today'), view('year'), view('month'), view('week'), view('day'),
  settingsTab('settings-appearance', 1), settingsTab('settings-calendar', 2), settingsTab('settings-tasks', 3),
  settingsTab('settings-windows', 4), settingsTab('settings-data', 5), settingsTab('settings-shortcuts', 6),
  dialog('dialog-date', 0), dialog('dialog-time', 1), dialog('dialog-priority', 2), dialog('dialog-tag', 3),
  { name: 'sidebar-collapsed', attrs: { 'start-view': 'week' },
    act: async (p) => { await p.locator('nav[aria-label="Main"] button[aria-label="Collapse sidebar"]').click(); await p.waitForTimeout(600); } },
  { name: 'density-compact', attrs: { 'start-view': 'week' },
    act: async (p) => {
      await p.locator('nav[aria-label="Main"] button', { hasText: 'Settings' }).click();
      await p.waitForTimeout(900);
      await p.locator('[role="group"][aria-label="Density"] button', { hasText: 'Compact' }).click();
      await p.waitForTimeout(400);
      await p.locator('nav[aria-label="Settings"] button', { hasText: 'Back to app' }).click();
      await p.waitForTimeout(900);
    } },
];
