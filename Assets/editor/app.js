/* Personify 2.0 - the NPC editor page.
 *
 * Mounted as the Sideload surface `personify-editor` over the main menu. The host (C#) owns the project files,
 * autosave, undo history, validation, installing and the 3D preview; the page owns what the player is looking at:
 * the screen, the area, the NPC selection, open sections and the map view.
 *
 * Every edit goes to the host at once through `project.set` / `project.unset`, and the page applies the same change
 * to its own copy first, so a field never waits for an answer. The host answers with a Delta (problems, install
 * state, undo availability); undo, redo and anything structural (add, remove, role) answer with the whole project.
 *
 * Written against the Sideload engine, not a browser: every box is a column flexbox, a page can neither read nor set
 * a scroll offset (so long lists scroll through their own window, see vlist), `s1.call` is synchronous, an input that
 * leaves the document leaves a grey bar until a second rebuild (sweepInputs), and only Latin-1 glyphs draw.
 */

// mock.js installs a stand-in when there is no host (browser) or the host does not answer app.init.
const host = (typeof window !== 'undefined' && window.personifyMock) ? window.personifyMock : s1;

/* ---------- small helpers ---------- */

const el = (tag, cls, text) => {
  const node = document.createElement(tag);
  if (cls) node.className = cls;
  if (text !== undefined && text !== null) node.textContent = String(text);
  return node;
};

// The engine hands a control's value on the event; a browser keeps it on the element.
const valueOf = e => (e && e.value !== undefined && e.value !== null ? e.value : e && e.target ? e.target.value : '');

function setCls(node, cls, on) { if (on) node.classList.add(cls); else node.classList.remove(cls); }

const clone = v => (v === undefined ? undefined : JSON.parse(JSON.stringify(v)));
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const round = (v, d) => { const f = Math.pow(10, d === undefined ? 2 : d); return Math.round(v * f) / f; };
const fmtNum = v => (typeof v === 'number' && isFinite(v) ? String(round(v, 2)) : '-');
const pct = v => Math.round(v * 100) + ' %';

function rectOf(node) {
  try { return node && node.rect ? node.rect() : null; }
  catch (e) { console.warn('[personify] rect() failed: ' + e.message); return null; }
}

// Sideload raises `wheel` with wheelDelta in notches, positive downwards; a browser raises deltaY in pixels.
function wheelDir(e) {
  if (typeof e.deltaY === 'number' && e.deltaY !== 0) return e.deltaY > 0 ? 1 : -1;
  if (typeof e.wheelDelta === 'number' && e.wheelDelta !== 0) return e.wheelDelta > 0 ? 1 : -1;
  return 0;
}

function call(name, arg) {
  let raw = '';
  try {
    raw = host.call(name, arg === undefined ? '' : (typeof arg === 'string' ? arg : JSON.stringify(arg)));
  } catch (e) {
    console.error('[personify] call ' + name + ' threw: ' + (e && e.message));
    return null;
  }
  if (!raw) { console.warn('[personify] ' + name + ' answered nothing'); return null; }
  try { return JSON.parse(raw); }
  catch (e) { console.error('[personify] ' + name + ' answered unreadable JSON: ' + e.message); return null; }
}

function parsePayload(p, what) {
  if (p === null || p === undefined || p === '') return null;
  if (typeof p !== 'string') return p;
  try { return JSON.parse(p); }
  catch (e) { console.error('[personify] unreadable ' + what + ' payload: ' + e.message); return null; }
}

/* ---------- texts ---------- */

let STR = {};
// Every visible string goes through t(). T() only marks an English string for the table where it is stored before
// the translation is known (constant lists); it is translated with t() when drawn.
function t(s, vars) {
  let out = (STR && STR[s]) || s;
  if (vars) for (const k in vars) out = out.split('{' + k + '}').join(String(vars[k]));
  return out;
}
const T = s => s;

// Error codes the host answers with (contract, "Nachträge des Hosts").
const ERRORS = {
  no_project: T('No project is open.'),
  name_missing: T('Enter a name.'),
  create_failed: T('The project could not be created.'),
  not_found: T('The project was not found.'),
  import_failed: T('The pack could not be imported.'),
  restore_failed: T('That version could not be restored.'),
  npc_not_found: T('That NPC no longer exists.'),
  role_unknown: T('Unknown role.'),
  portrait_failed: T('The contact picture could not be taken.'),
  no_world: T('Load a save once first.'),
  no_save: T('No save found.'),
  save_failed: T('The project could not be saved.'),
  blocked: T('Fix the errors first.'),
  personnel_missing: T('Personnel is not installed.'),
  marker_failed: T('The test could not be prepared.'),
  restart_failed: T('The game could not restart.'),
  bad_folder: T('That folder name is not allowed.'),
  exists: T('That project already exists.'),
  move_failed: T('The pack could not be moved.'),
  remove_failed: T('The pack could not be removed.'),
};
function errText(code) {
  if (!code) return t('Something went wrong.');
  if (ERRORS[code]) return t(ERRORS[code]);
  console.warn('[personify] untranslated error code ' + code);
  return t('Failed: {code}', { code });
}

/* ---------- state ---------- */

const st = {
  init: null, expert: false, lang: 'en', hasWorld: false,
  projects: [], packs: [], importable: [],
  project: null, problems: [], install: 'not_installed', canUndo: false, canRedo: false,
  selection: [], active: null, area: 'look',
  look: null, world: null, map: null, effects: null,
};
const ui = {
  screen: 'start', search: '', roleFilter: 'all',
  open: {},                 // area -> { sectionId: bool }
  focusField: null,         // field path a problem jump asked to show
  pick: null,               // { what:'spawn'|'step'|'car', index }
  step: -1,                 // selected schedule step
  problemsOpen: false,
  pending: {},              // variant lists with an empty row the player has not written yet
};

const AREAS = [['look', T('Look')], ['role', T('Role')], ['routine', T('Routine')], ['relations', T('Relations')]];
const ROLES = [['civilian', T('Civilian')], ['customer', T('Customer')], ['dealer', T('Dealer')], ['supplier', T('Supplier')]];
const ROLE_SUB = {
  civilian: T('Lives in the town and has no trade.'),
  customer: T('Buys your product.'),
  dealer: T('Sells your product for a cut.'),
  supplier: T('Sells you ingredients and seeds.'),
};

const npcs = () => (st.project && st.project.npcs) || [];
const npcById = id => npcs().find(n => n.id === id) || null;
const roleOf = n => (!n ? 'civilian' : n.dealer ? 'dealer' : n.supplier ? 'supplier' : n.customer ? 'customer' : 'civilian');
const roleLabel = r => t((ROLES.find(x => x[0] === r) || ROLES[0])[1]);
const multi = () => st.selection.length > 1;
const targets = () => (multi() ? st.selection.slice() : st.active ? [st.active] : []);
const worldHas = () => !!(st.world && st.world.hasWorld);

function getPath(o, path) {
  const ks = path.split('.');
  for (const k of ks) {
    if (o === null || o === undefined || typeof o !== 'object') return undefined;
    o = o[k];
  }
  return o;
}
function setPath(o, path, v) {
  const ks = path.split('.');
  for (let i = 0; i < ks.length - 1; i++) {
    if (o[ks[i]] === null || o[ks[i]] === undefined || typeof o[ks[i]] !== 'object') o[ks[i]] = {};
    o = o[ks[i]];
  }
  o[ks[ks.length - 1]] = clone(v);
}
function unsetPath(o, path) {
  const ks = path.split('.');
  for (let i = 0; i < ks.length - 1; i++) {
    if (!o || typeof o !== 'object') return;
    o = o[ks[i]];
  }
  if (o && typeof o === 'object') delete o[ks[ks.length - 1]];
}

// What the selected NPCs hold at a path: the active NPC's value and whether the others differ.
function readField(path, def) {
  const ids = targets();
  const vals = ids.map(id => getPath(npcById(id), path));
  const first = vals[0];
  const mixed = vals.some(v => !same(v, first));
  return { value: first === undefined ? def : first, mixed, unset: vals.every(v => v === undefined) };
}

/* ---------- writing ---------- */

function packTarget(path) {
  const p = st.project;
  if (path.indexOf('editor.') === 0) { if (!p.editor) p.editor = {}; return [p.editor, path.slice(7)]; }
  if (!p.manifest) p.manifest = {};
  return [p.manifest, path];
}

// One edit, applied to the page's copy and sent to the host as one undo step. `changes` is a list of
// { path, value } or { path, unset:true }. `live` marks an intermediate value while a slider or colour is dragged.
function write(changes, opt) {
  opt = opt || {};
  if (!st.project) { console.warn('[personify] write ignored, no project open'); return false; }
  const ids = opt.pack ? [] : (opt.ids || targets());
  if (!opt.pack && ids.length === 0) { console.warn('[personify] write ignored, no NPC selected'); return false; }

  for (const c of changes) {
    if (opt.pack) {
      const [obj, p] = packTarget(c.path);
      if (c.unset) unsetPath(obj, p); else setPath(obj, p, c.value);
      continue;
    }
    for (const id of ids) {
      const n = npcById(id);
      if (!n) { console.warn('[personify] write skipped unknown NPC ' + id); continue; }
      if (c.unset) unsetPath(n, c.path); else setPath(n, c.path, c.value);
    }
  }

  let name = 'project.set';
  let arg;
  if (changes.length === 1) {
    const c = changes[0];
    if (c.unset) { name = 'project.unset'; arg = { ids, path: c.path }; }
    else arg = { ids, path: c.path, value: c.value };
  } else {
    arg = { ids, changes: changes.map(c => (c.unset ? { path: c.path, unset: true } : { path: c.path, value: c.value })) };
  }
  if (opt.live) arg.live = true;

  const d = call(name, arg);
  if (!d) { if (!opt.live) toast(t('The change was not saved.'), 'bad'); return false; }
  applyDelta(d);
  if (!opt.live) afterWrite(changes);
  return d.ok !== false;
}

function afterWrite(changes) {
  const touches = p => changes.some(c => c.path === p || c.path.indexOf(p + '.') === 0);
  if (touches('name')) renderList();
  if (ui.screen === 'editor' && st.area === 'routine' && (touches('schedule') || touches('spawn'))) drawMapOverlay();
}

function applyDelta(d) {
  if (!d) return;
  if (d.ok === false && d.error) toast(errText(d.error), 'bad');
  if (Array.isArray(d.problems)) st.problems = d.problems;
  if (d.install) st.install = d.install;
  if (d.canUndo !== undefined) st.canUndo = !!d.canUndo;
  if (d.canRedo !== undefined) st.canRedo = !!d.canRedo;
  if (ui.screen === 'editor' && E) { renderTop(); renderFoot(); renderList(); }
}

// A whole project from the host: undo, redo, add, remove, role change, a change from the phone app.
function applyState(ps) {
  if (!ps) { toast(t('The editor host did not answer.'), 'bad'); return false; }
  if (ps.ok === false) { toast(errText(ps.error), 'bad'); return false; }
  if (ps.project) st.project = ps.project;
  applyDelta(Object.assign({}, ps, { ok: true }));
  const ids = npcs().map(n => n.id);
  st.selection = st.selection.filter(id => ids.indexOf(id) >= 0);
  if (ps.added && ids.indexOf(ps.added) >= 0) { st.active = ps.added; st.selection = [ps.added]; }
  if (!st.active || ids.indexOf(st.active) < 0) st.active = ids.length ? ids[0] : null;
  if (st.selection.length === 0 && st.active) st.selection = [st.active];
  if (ui.screen === 'editor') renderEditor();
  return true;
}

/* ---------- inputs, dialogs, toasts ---------- */

// Sideload keeps the native text field of an input that left the document until the rebuild after the one that
// dropped it, as an empty grey bar. Anything about to drop inputs asks for that second rebuild here.
let sweepQueued = false;
function sweepInputs(root) {
  if (!root || sweepQueued || root.querySelectorAll('input, textarea').length === 0) return;
  sweepQueued = true;
  setTimeout(() => { sweepQueued = false; document.body.classList.toggle('swept'); }, 100);
}

function btn(label, cls, onClick) {
  const b = el('div', cls ? 'btn ' + cls : 'btn', label);
  b.addEventListener('click', e => {
    if (e.stopPropagation) e.stopPropagation();
    if (b.classList.contains('off')) return;
    onClick(e);
  });
  return b;
}

function textInput(value, placeholder, onCommit, opts) {
  opts = opts || {};
  const n = el(opts.multi ? 'textarea' : 'input', 'inp' + (opts.cls ? ' ' + opts.cls : ''));
  n.value = value === undefined || value === null ? '' : String(value);
  if (placeholder) n.setAttribute('placeholder', placeholder);
  let cur = n.value;
  let sent = n.value;
  const commit = () => { if (cur === sent) return; sent = cur; onCommit(cur); };
  n.addEventListener('input', e => { cur = valueOf(e); if (opts.onInput) opts.onInput(cur); });
  n.addEventListener('change', commit);
  n.addEventListener('keydown', e => { if (e.key === 'Enter' && !opts.multi) commit(); });
  return n;
}

function selectEl(options, value, onChange, opts) {
  opts = opts || {};
  const s = el('select', 'sel' + (opts.cls ? ' ' + opts.cls : ''));
  const add = (v, l) => {
    const o = el('option', null, l);
    o.setAttribute('value', v);
    if (v === value) o.setAttribute('selected', 'selected');
    s.appendChild(o);
  };
  if (opts.mixed) { add('__mixed', t('Mixed')); value = '__mixed'; }
  if (opts.none) add('', opts.noneLabel || t('None'));
  let found = opts.mixed || (opts.none && (value === '' || value === undefined || value === null));
  for (const [v, l] of options) { add(v, l); if (v === value) found = true; }
  // A value the list does not know (hand-written pack, other game version) is kept and shown as itself.
  if (!found && value !== undefined && value !== null && value !== '') add(value, String(value));
  s.value = value === undefined || value === null ? '' : value;
  s.addEventListener('change', e => { const v = valueOf(e); if (v === '__mixed') return; onChange(v); });
  return s;
}

function colorEl(value, onLive, onCommit) {
  const c = el('input', 'col');
  c.setAttribute('type', 'color');
  c.value = hex6(value);
  let last = 0;
  c.addEventListener('input', e => {
    const now = Date.now();
    if (onLive && now - last > 120) { last = now; onLive(valueOf(e).toUpperCase()); }
  });
  c.addEventListener('change', e => onCommit(valueOf(e).toUpperCase()));
  return c;
}

const hex6 = v => {
  const s = String(v || '#FFFFFF');
  if (/^#[0-9a-fA-F]{6}/.test(s)) return s.slice(0, 7).toLowerCase();
  if (/^#[0-9a-fA-F]{3}$/.test(s)) return ('#' + s[1] + s[1] + s[2] + s[2] + s[3] + s[3]).toLowerCase();
  return '#ffffff';
};

function sw(on, onChange, mixed) {
  const n = el('div', 'sw' + (on ? ' on' : '') + (mixed ? ' mx' : ''));
  n.appendChild(el('div', 'kn'));
  n.addEventListener('click', e => {
    if (e.stopPropagation) e.stopPropagation();
    const next = mixed ? true : !n.classList.contains('on');
    mixed = false;
    n.classList.remove('mx');
    setCls(n, 'on', next);
    onChange(next);
  });
  return n;
}

function segEl(options, value, onPick, cls) {
  const box = el('div', 'seg' + (cls ? ' ' + cls : ''));
  const nodes = [];
  for (const [v, l] of options) {
    const o = el('div', v === value ? 'o on' : 'o', l);
    o.addEventListener('click', () => {
      if (o.classList.contains('on')) return;
      for (const x of nodes) x.classList.remove('on');
      o.classList.add('on');
      onPick(v);
    });
    nodes.push(o);
    box.appendChild(o);
  }
  return box;
}

let toastNode = null;
function toast(text, tone) {
  if (toastNode) toastNode.remove();
  toastNode = el('div', 'toast ' + (tone || 'info'), text);
  document.body.appendChild(toastNode);
  const mine = toastNode;
  setTimeout(() => { if (toastNode === mine) { mine.remove(); toastNode = null; } }, 4200);
}

// A hover hint: mouseenter says when, rect() says where.
function tip(anchor, text) {
  let node = null;
  const hide = () => { if (node) { node.remove(); node = null; } };
  anchor.addEventListener('mouseenter', () => {
    if (node || !text) return;
    const r = rectOf(anchor);
    if (!r || !r.height) return;
    node = el('div', 'tip', text);
    const w = Math.min(320, 20 + text.length * 7);
    const vw = innerWidth || 1920;
    let left = Math.max(6, Math.min(vw - w - 6, r.x + r.width / 2 - w / 2));
    node.style.left = Math.round(left) + 'px';
    node.style.top = Math.round(r.y > 40 ? r.y - 34 : r.y + r.height + 6) + 'px';
    node.style.width = Math.round(w) + 'px';
    document.body.appendChild(node);
  });
  anchor.addEventListener('mouseleave', hide);
  anchor.addEventListener('click', hide);
}

let dialog = null;
function openDialog(cls) {
  closeDialog();
  const scrim = el('div', 'scrim');
  const box = el('div', 'dialog' + (cls ? ' ' + cls : ''));
  scrim.appendChild(box);
  scrim.addEventListener('click', e => { if (e.target === scrim) closeDialog(); });
  document.body.appendChild(scrim);
  dialog = scrim;
  return box;
}
function closeDialog() {
  if (!dialog) return false;
  sweepInputs(dialog);
  dialog.remove();
  dialog = null;
  return true;
}
function dialogHead(box, title) {
  const h = el('div', 'dh');
  h.appendChild(el('div', 'dt', title));
  h.appendChild(el('div', 'sp'));
  h.appendChild(btn(t('Close'), 'sm', closeDialog));
  box.appendChild(h);
}

function confirmDialog(title, consequence, okLabel, onYes, danger) {
  const box = openDialog('small');
  dialogHead(box, title);
  if (consequence) box.appendChild(el('div', 'dsub', consequence));
  const acts = el('div', 'dacts');
  acts.appendChild(el('div', 'sp'));
  acts.appendChild(btn(t('Cancel'), null, closeDialog));
  acts.appendChild(btn(okLabel, danger ? 'bad' : 'go', () => { closeDialog(); onYes(); }));
  box.appendChild(acts);
}

let menuNode = null;
function openMenu(anchor, items) {
  closeMenu();
  const r = rectOf(anchor) || { x: 16, y: 16, height: 40 };
  const scrim = el('div', 'mscrim');
  scrim.addEventListener('click', e => { if (e.target === scrim) closeMenu(); });
  const m = el('div', 'menu');
  m.style.left = Math.round(r.x) + 'px';
  m.style.top = Math.round(r.y + r.height + 6) + 'px';
  for (const it of items) {
    if (!it) { m.appendChild(el('div', 'msep')); continue; }
    const row = el('div', 'mi' + (it.danger ? ' bad' : '') + (it.off ? ' off' : ''), it.label);
    row.addEventListener('click', () => { if (it.off) return; closeMenu(); it.run(); });
    m.appendChild(row);
  }
  scrim.appendChild(m);
  document.body.appendChild(scrim);
  menuNode = scrim;
}
function closeMenu() { if (!menuNode) return false; menuNode.remove(); menuNode = null; return true; }

/* ---------- windowed list ---------- */

// Sideload does not virtualise lists and a page cannot read a scroll offset, so a long list scrolls through its own
// window: fixed row height, the rows that fit are built, the wheel and a drawn scrollbar move the window.
const lists = new Set();
function vlist(o) {
  const box = el('div', 'vl' + (o.cls ? ' ' + o.cls : ''));
  const rows = el('div', 'vlr');
  const bar = el('div', 'vlb');
  const thumb = el('div', 'vlt');
  bar.appendChild(thumb);
  box.appendChild(rows);
  box.appendChild(bar);
  const v = { box, items: [], off: 0, h: 0 };
  const fit = () => Math.max(1, Math.floor((v.h || o.guess || 480) / o.rowH));
  v.clamp = () => {
    const max = Math.max(0, v.items.length - fit());
    if (v.off > max) v.off = max;
    if (v.off < 0) v.off = 0;
  };
  v.draw = () => {
    v.clamp();
    rows.replaceChildren();
    const n = fit();
    if (v.items.length === 0 && o.empty) rows.appendChild(el('div', 'vle', o.empty()));
    const end = Math.min(v.items.length, v.off + n);
    for (let i = v.off; i < end; i++) {
      const r = o.build(v.items[i], i);
      r.style.height = o.rowH + 'px';
      rows.appendChild(r);
    }
    const over = v.items.length > n;
    bar.style.display = over ? 'flex' : 'none';
    if (over) {
      thumb.style.height = Math.max(6, n / v.items.length * 100) + '%';
      thumb.style.top = (v.off / v.items.length * 100) + '%';
    }
  };
  v.set = (items, keep) => { v.items = items; if (!keep) v.off = 0; v.draw(); };
  v.scrollBy = d => { const before = v.off; v.off += d; v.clamp(); if (v.off !== before) v.draw(); };
  v.reveal = i => {
    const n = fit();
    if (i < 0) return;
    if (i < v.off) v.off = i; else if (i >= v.off + n) v.off = i - n + 1;
    v.draw();
  };
  v.check = () => {
    const r = rectOf(box);
    if (r && r.height && Math.abs(r.height - v.h) > 1) { v.h = r.height; v.draw(); }
  };
  box.addEventListener('wheel', e => {
    const d = wheelDir(e);
    if (d) v.scrollBy(d * 3);
    if (e.preventDefault) e.preventDefault();
  });
  let drag = null;
  thumb.addEventListener('pointerdown', e => {
    if (e.stopPropagation) e.stopPropagation();
    drag = { y: e.clientY, off: v.off };
    try { thumb.setPointerCapture(e.pointerId); } catch (err) { console.warn('[personify] scrollbar capture refused: ' + err.message); }
  });
  thumb.addEventListener('pointermove', e => {
    if (!drag || !(e.buttons & 1) || !v.items.length) return;
    const per = (v.h || o.guess || 480) / v.items.length;
    const next = drag.off + Math.round((e.clientY - drag.y) / per);
    if (next !== v.off) { v.off = next; v.draw(); }
  });
  thumb.addEventListener('pointerup', () => { drag = null; });
  thumb.addEventListener('lostpointercapture', () => { drag = null; });
  bar.addEventListener('click', e => {
    if (e.target === thumb) return;
    const r = rectOf(thumb);
    v.scrollBy((r && e.clientY < r.y ? -1 : 1) * fit());
  });
  lists.add(v);
  return v;
}

/* ---------- picker dialog ---------- */

// One searchable list for every "choose one of many" field: hair, clothing, items, effects, connections.
// items: [{ value, label, group?, sub? }]
function pickDialog(title, items, opts, onPick) {
  opts = opts || {};
  const box = openDialog('tall');
  dialogHead(box, title);
  let q = '';
  const search = textInput('', t('Search'), () => {}, { cls: 'search', onInput: v => { q = v; refill(); } });
  box.appendChild(search);
  const list = vlist({
    rowH: 38, cls: 'pick', guess: 520,
    empty: () => t('Nothing matches.'),
    build: it => {
      if (it.header) return el('div', 'pgh', it.label);
      const r = el('div', 'pr' + (it.value === opts.current ? ' on' : ''));
      r.appendChild(el('div', 'pn', it.label));
      if (it.sub) r.appendChild(el('div', 'ps', it.sub));
      r.addEventListener('click', () => { closeDialog(); onPick(it.value); });
      return r;
    },
  });
  box.appendChild(list.box);
  function refill() {
    const s = q.trim().toLowerCase();
    const hits = items.filter(it => !s || (it.label + ' ' + (it.group || '') + ' ' + it.value).toLowerCase().indexOf(s) >= 0);
    const out = [];
    let g = null;
    for (const it of hits) {
      if (!s && it.group && it.group !== g) { g = it.group; out.push({ header: true, label: it.group }); }
      out.push(it);
    }
    list.set(out);
  }
  refill();
  const cur = items.findIndex(it => it.value === opts.current);
  if (cur > 0) list.reveal(cur);
  const acts = el('div', 'dacts');
  if (opts.allowNone) acts.appendChild(btn(opts.noneLabel || t('None'), null, () => { closeDialog(); onPick(''); }));
  if (opts.extra) acts.appendChild(opts.extra);
  acts.appendChild(el('div', 'sp'));
  acts.appendChild(btn(t('Cancel'), null, closeDialog));
  box.appendChild(acts);
  search.focus();
}

/* ---------- boot ---------- */

function boot() {
  const init = call('app.init');
  if (!init) {
    document.getElementById('app').appendChild(el('div', 'fatal', t('The editor host did not answer.')));
    return;
  }
  st.init = init;
  STR = init.strings || {};
  st.expert = !!init.expert;
  st.lang = init.lang || 'en';
  st.hasWorld = !!init.hasWorld;
  console.log('[personify] editor page up, lang ' + st.lang + ', personnel ' + init.personnelVersion + ', sideload ' + init.sideloadVersion);
  goStart();
}

/* ---------- start screen ---------- */

function goStart() {
  ui.screen = 'start';
  ui.pick = null;
  E = null;
  setStageMode('none');
  st.projects = call('projects.list') || [];
  st.packs = call('packs.list') || [];
  renderStart();
}

const INSTALL_TEXT = {
  not_installed: T('Not in your game'),
  installed: T('In your game'),
  changed: T('Changed since install'),
};
const installTone = s => (s === 'installed' ? 'ok' : s === 'changed' ? 'warn' : 'mu');

function renderStart() {
  const root = document.getElementById('app');
  sweepInputs(root);
  root.replaceChildren();
  const page = el('div', 'start');

  const head = el('div', 'shead');
  const ttl = el('div', 'stl');
  ttl.appendChild(el('div', 'brand', 'Personify'));
  ttl.appendChild(el('div', 'bsub', t('Build NPCs for Schedule I.')));
  head.appendChild(ttl);
  head.appendChild(el('div', 'sp'));
  head.appendChild(btn(t('Back to Side Hustle'), null, () => { if (!call('editor.exit')) toast(t('The editor host did not answer.'), 'bad'); }));
  page.appendChild(head);

  const cols = el('div', 'scols');

  // Projects
  const pc = el('div', 'card grow');
  const ph = el('div', 'ch');
  ph.appendChild(el('div', 'h', t('Projects')));
  ph.appendChild(el('div', 'sp'));
  ph.appendChild(btn(t('Import pack'), 'sm', importDialog));
  ph.appendChild(btn(t('New project'), 'sm go', newProjectDialog));
  pc.appendChild(ph);
  const plist = el('div', 'scroll');
  if (st.projects.length === 0) {
    const empty = el('div', 'empty');
    empty.appendChild(el('div', 'emsg', t('No projects yet.')));
    const acts = el('div', 'row gap');
    acts.appendChild(btn(t('New project'), 'go', newProjectDialog));
    acts.appendChild(btn(t('Import pack'), null, importDialog));
    empty.appendChild(acts);
    plist.appendChild(empty);
  }
  for (const p of st.projects) {
    const r = el('div', 'prow');
    r.addEventListener('click', () => openProject(p.folder));
    const tx = el('div', 'pt');
    tx.appendChild(el('div', 'pnm', p.name || p.folder));
    tx.appendChild(el('div', 'psub', p.npcs === 1 ? t('1 NPC') : t('{n} NPCs', { n: p.npcs || 0 })));
    r.appendChild(tx);
    r.appendChild(el('div', 'pst ' + installTone(p.install), t(INSTALL_TEXT[p.install] || INSTALL_TEXT.not_installed)));
    r.appendChild(btn(t('Delete'), 'sm', () => confirmDialog(t('Delete {name}?', { name: p.name || p.folder }),
      t('The project folder is removed; an installed pack stays in your game.'), t('Delete'), () => {
        const res = call('projects.delete', p.folder);
        if (!res || res.ok === false) toast(errText(res && res.error), 'bad');
        else console.log('[personify] project ' + p.folder + ' deleted');
        goStart();
      }, true)));
    r.appendChild(btn(t('Open'), 'sm go', () => openProject(p.folder)));
    plist.appendChild(r);
  }
  pc.appendChild(plist);
  cols.appendChild(pc);

  // Installed packs
  const kc = el('div', 'card packs');
  const kh = el('div', 'ch');
  kh.appendChild(el('div', 'h', t('Installed packs')));
  kc.appendChild(kh);
  const klist = el('div', 'scroll');
  if (st.packs.length === 0) klist.appendChild(el('div', 'quiet', t('No NPC packs installed.')));
  for (const k of st.packs) {
    const r = el('div', 'krow');
    const tx = el('div', 'pt');
    tx.appendChild(el('div', 'pnm', k.name || k.folder));
    const bits = [];
    if (k.version) bits.push('v' + k.version);
    if (k.author) bits.push(t('by {author}', { author: k.author }));
    bits.push(k.npcs === 1 ? t('1 NPC') : t('{n} NPCs', { n: k.npcs || 0 }));
    tx.appendChild(el('div', 'psub', bits.join(' · ')));
    if (k.ownProject) tx.appendChild(el('div', 'ptag', t('From your project')));
    r.appendChild(tx);
    r.appendChild(sw(!!k.enabled, on => {
      const res = call('packs.toggle', { folder: k.folder, enabled: on });
      if (!res || res.ok === false) { toast(errText(res && res.error), 'bad'); goStart(); return; }
      console.log('[personify] pack ' + k.folder + (on ? ' enabled' : ' disabled'));
      // Personnel reads packs when a save loads, so the switch takes effect there.
      toast(on ? t('Pack on from the next save load.') : t('Pack off from the next save load.'), 'info');
      k.enabled = on;
    }));
    r.appendChild(btn(t('Remove'), 'sm', () => confirmDialog(t('Remove {name}?', { name: k.name || k.folder }),
      t('Saves that contain these NPCs lose them the next time they load.'), t('Remove'), () => {
        const res = call('packs.remove', k.folder);
        if (!res || res.ok === false) toast(errText(res && res.error), 'bad');
        else { console.log('[personify] pack ' + k.folder + ' removed'); toast(t('Pack removed.'), 'ok'); }
        goStart();
      }, true)));
    klist.appendChild(r);
  }
  kc.appendChild(klist);
  cols.appendChild(kc);

  page.appendChild(cols);
  root.appendChild(page);
}

function newProjectDialog() {
  const box = openDialog('small');
  dialogHead(box, t('New project'));
  let name = '';
  let author = '';
  const err = el('div', 'ferr');
  err.style.display = 'none';
  const f1 = el('div', 'df');
  f1.appendChild(el('div', 'dl', t('Name')));
  const n = textInput('', t('My NPC pack'), v => { name = v; }, { onInput: v => { name = v; } });
  n.addEventListener('keydown', e => { if (e.key === 'Enter') create(); });
  f1.appendChild(n);
  box.appendChild(f1);
  const f2 = el('div', 'df');
  f2.appendChild(el('div', 'dl', t('Author')));
  f2.appendChild(textInput('', t('Your name'), v => { author = v; }, { onInput: v => { author = v; } }));
  box.appendChild(f2);
  box.appendChild(err);
  const acts = el('div', 'dacts');
  acts.appendChild(el('div', 'sp'));
  acts.appendChild(btn(t('Cancel'), null, closeDialog));
  acts.appendChild(btn(t('Create'), 'go', create));
  box.appendChild(acts);
  n.focus();
  function create() {
    if (!name.trim()) { err.textContent = t('Enter a name.'); err.style.display = 'flex'; return; }
    const r = call('projects.create', { name: name.trim(), author: author.trim() });
    if (!r || r.ok === false) { err.textContent = errText(r && r.error); err.style.display = 'flex'; return; }
    console.log('[personify] project created in ' + r.folder);
    closeDialog();
    openProject(r.folder);
  }
}

function importDialog() {
  st.importable = call('projects.importable') || [];
  const box = openDialog('tall');
  dialogHead(box, t('Import pack'));
  box.appendChild(el('div', 'dsub', t('Installed Personnel packs and zips from UserData/Personify/Import.')));
  const list = el('div', 'scroll');
  if (st.importable.length === 0) list.appendChild(el('div', 'quiet', t('Nothing to import.')));
  for (const it of st.importable) {
    const r = el('div', 'prow');
    const tx = el('div', 'pt');
    tx.appendChild(el('div', 'pnm', it.name));
    tx.appendChild(el('div', 'psub', (it.kind === 'zip' ? t('Zip file') : t('Installed pack')) + ' · ' +
      (it.npcs === 1 ? t('1 NPC') : t('{n} NPCs', { n: it.npcs || 0 }))));
    r.appendChild(tx);
    r.appendChild(btn(t('Import'), 'sm go', () => {
      const res = call('projects.import', it.path);
      if (!res || res.ok === false) { toast(errText(res && res.error), 'bad'); return; }
      const folders = res.folders || [];
      console.log('[personify] imported ' + it.path + ' into ' + folders.join(', '));
      closeDialog();
      if (folders.length === 1) { openProject(folders[0]); return; }
      toast(t('{n} projects imported.', { n: folders.length }), 'ok');
      goStart();
    }));
    list.appendChild(r);
  }
  box.appendChild(list);
}

/* ---------- editor ---------- */

function openProject(folder) {
  const ps = call('projects.open', folder);
  if (!ps || ps.ok === false) { toast(errText(ps && ps.error), 'bad'); return; }
  closeDialog();
  ui.screen = 'editor';
  ui.step = -1;
  ui.pick = null;
  ui.problemsOpen = false;
  st.selection = [];
  st.active = null;
  // Catalogs are asked once per open: a save loaded in between can have filled the world catalog.
  st.look = call('catalog.look') || {};
  st.world = call('catalog.world') || { hasWorld: false, buildings: [], regions: [], npcs: [], items: [] };
  st.effects = call('catalog.effects') || [];
  st.map = null;
  buildEditor();
  applyState(ps);
  console.log('[personify] opened ' + folder + ' with ' + npcs().length + ' NPCs');
}

let E = null;
let npcVl = null;

function buildEditor() {
  const root = document.getElementById('app');
  sweepInputs(root);
  root.replaceChildren();
  lists.clear();
  E = {};
  const ed = el('div', 'ed');
  E.top = el('div', 'topbar');
  ed.appendChild(E.top);

  const mid = el('div', 'mid');
  E.side = el('div', 'side');
  E.stage = el('div', 'stage');
  E.stage.id = 'stage';
  E.fields = el('div', 'fields');
  mid.appendChild(E.side);
  mid.appendChild(E.stage);
  mid.appendChild(E.fields);
  ed.appendChild(mid);

  E.foot = el('div', 'foot');
  ed.appendChild(E.foot);
  root.appendChild(ed);

  buildSide();
  wireStage();
  E.fhead = el('div', 'fhead');
  E.fscroll = el('div', 'fscroll');
  E.fbody = el('div', 'fbody');
  E.fscroll.appendChild(E.fbody);
  E.fields.appendChild(E.fhead);
  E.fields.appendChild(E.fscroll);
}

function renderEditor() {
  if (!E) return;
  renderTop();
  renderList();
  renderFields();
  renderStage();
  renderFoot();
}

/* --- top bar --- */

function renderTop() {
  if (!E) return;
  const top = E.top;
  top.replaceChildren();
  const pm = el('div', 'pmenu');
  pm.appendChild(el('div', 'pmn', (st.project && st.project.name) || t('Project')));
  pm.appendChild(el('div', 'pmc', t('Menu')));
  pm.addEventListener('click', () => openMenu(pm, [
    { label: t('Pack settings'), run: packDialog },
    { label: t('History'), run: historyDialog },
    { label: t('Publish'), run: publish },
    st.install !== 'not_installed' ? { label: t('Remove from my game'), run: uninstall, danger: true } : null,
    null,
    { label: t('Back to projects'), run: goStart },
  ].filter((x, i, a) => x !== null || (i > 0 && a[i - 1] !== null))));
  top.appendChild(pm);

  const tabs = el('div', 'tabs');
  for (const [id, label] of AREAS) {
    const tb = el('div', st.area === id ? 'tab on' : 'tab', t(label));
    tb.addEventListener('click', () => setArea(id));
    tabs.appendChild(tb);
  }
  top.appendChild(tabs);
  top.appendChild(el('div', 'sp'));
  top.appendChild(btn(t('Undo'), 'sm' + (st.canUndo ? '' : ' off'), undo));
  top.appendChild(btn(t('Redo'), 'sm' + (st.canRedo ? '' : ' off'), redo));
  const ex = el('div', 'extog');
  ex.appendChild(el('div', 'exl', t('Expert')));
  ex.appendChild(sw(st.expert, on => setExpert(on)));
  top.appendChild(ex);
}

function setArea(id) {
  if (st.area === id) return;
  st.area = id;
  ui.pick = null;
  ui.focusField = null;
  renderTop();
  renderFields();
  renderStage();
}

function setExpert(on) {
  st.expert = on;
  const r = call('app.setExpert', on ? '1' : '0');
  if (!r || r.ok === false) console.warn('[personify] app.setExpert was not stored');
  if (ui.screen === 'editor') { renderTop(); renderFields(); }
}

function undo() {
  if (!st.canUndo) return;
  const ps = call('project.undo');
  if (applyState(ps)) console.log('[personify] undo');
}
function redo() {
  if (!st.canRedo) return;
  const ps = call('project.redo');
  if (applyState(ps)) console.log('[personify] redo');
}

function packDialog() {
  const box = openDialog('mid');
  dialogHead(box, t('Pack settings'));
  const m = (st.project && st.project.manifest) || {};
  const ed = (st.project && st.project.editor) || {};
  const row = (label, path, value, ph, opts) => {
    const f = el('div', 'df');
    f.setAttribute('data-path', path);
    if (ui.focusField && ui.focusField.indexOf(path) === 0) f.classList.add('hl');
    f.appendChild(el('div', 'dl', label));
    f.appendChild(textInput(value, ph, v => write([{ path, value: v }], { pack: true }), opts));
    box.appendChild(f);
  };
  row(t('Name'), 'name', m.name || st.project.name, t('My NPC pack'));
  row(t('Author'), 'author', m.author, t('Your name'));
  row(t('Version'), 'editor.modVersion', ed.modVersion, '1.0.0');
  row(t('Description'), 'editor.description', ed.description, t('One line for the mod page.'), { multi: true });
  row(t('Website'), 'editor.websiteUrl', ed.websiteUrl, 'https://');
  if (st.expert) {
    row(t('Pack id'), 'packId', m.packId, t('Folder name'));
    const f = el('div', 'df row');
    f.appendChild(el('div', 'dl grow', t('Register every NPC as a world NPC')));
    f.appendChild(sw(m.autoRegister !== false, on => write([{ path: 'autoRegister', value: on }], { pack: true })));
    box.appendChild(f);
  }
  ui.focusField = null;
}

function historyDialog() {
  const list = call('project.history') || [];
  const box = openDialog('tall');
  dialogHead(box, t('History'));
  const sc = el('div', 'scroll');
  if (list.length === 0) sc.appendChild(el('div', 'quiet', t('No saved versions yet.')));
  for (const h of list) {
    const r = el('div', 'prow');
    const tx = el('div', 'pt');
    tx.appendChild(el('div', 'pnm', fmtDate(h.utc)));
    tx.appendChild(el('div', 'psub', h.npcs === 1 ? t('1 NPC') : t('{n} NPCs', { n: h.npcs || 0 })));
    r.appendChild(tx);
    r.appendChild(btn(t('Restore'), 'sm', () => confirmDialog(t('Restore this version?'),
      t('Undo brings the current state back.'), t('Restore'), () => {
        if (applyState(call('project.restore', String(h.id)))) { console.log('[personify] restored version ' + h.id); toast(t('Version restored.'), 'ok'); }
      })));
    sc.appendChild(r);
  }
  box.appendChild(sc);
}

function fmtDate(utc) {
  const d = new Date(utc);
  if (isNaN(d.getTime())) return String(utc);
  const p = n => String(n).padStart(2, '0');
  return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
}

function publish() {
  const r = call('publish');
  if (!r) { toast(t('The editor host did not answer.'), 'bad'); return; }
  if (r.ok === false) { toast(r.text || errText(r.error || r.code), 'bad'); return; }
  console.log('[personify] published to ' + r.path);
  const box = openDialog('small');
  dialogHead(box, t('Pack published'));
  if (r.text) box.appendChild(el('div', 'dsub', r.text));
  if (r.path) box.appendChild(el('div', 'path', r.path));
  const acts = el('div', 'dacts');
  acts.appendChild(el('div', 'sp'));
  acts.appendChild(btn(t('Close'), 'go', closeDialog));
  box.appendChild(acts);
}

function uninstall() {
  confirmDialog(t('Remove the pack from your game?'), t('Saves that contain these NPCs lose them the next time they load.'),
    t('Remove'), () => {
      const r = call('uninstall');
      if (!r || r.ok === false) { toast(errText(r && r.error), 'bad'); return; }
      applyDelta(r);
      console.log('[personify] pack uninstalled');
      toast(t('Pack removed from your game.'), 'ok');
    }, true);
}

/* --- NPC list --- */

function buildSide() {
  const side = E.side;
  const search = textInput(ui.search, t('Search NPCs'), () => {}, {
    cls: 'search', onInput: v => { ui.search = v; renderList(true); },
  });
  side.appendChild(search);
  const filt = selectEl([['all', t('All roles')]].concat(ROLES.map(([v, l]) => [v, t(l)])), ui.roleFilter,
    v => { ui.roleFilter = v; renderList(true); }, { cls: 'rolef' });
  side.appendChild(filt);
  E.selbar = el('div', 'selbar');
  side.appendChild(E.selbar);
  npcVl = vlist({ rowH: 48, cls: 'npcs', guess: 560, build: npcRow, empty: () => t('No NPC matches.') });
  side.appendChild(npcVl.box);
  E.sideActs = el('div', 'sacts');
  side.appendChild(E.sideActs);
}

function filteredNpcs() {
  const q = ui.search.trim().toLowerCase();
  return npcs().filter(n => {
    if (ui.roleFilter !== 'all' && roleOf(n) !== ui.roleFilter) return false;
    if (q && ((n.name || '') + ' ' + (n.id || '')).toLowerCase().indexOf(q) < 0) return false;
    return true;
  });
}

function problemsOf(n) {
  const i = npcs().indexOf(n);
  let e = 0, w = 0;
  for (const p of st.problems) if (p.npc === i) { if (p.severity === 'error') e++; else w++; }
  return { e, w };
}

function npcRow(n) {
  const on = n.id === st.active;
  const checked = multi() && st.selection.indexOf(n.id) >= 0;
  const r = el('div', 'nr' + (on ? ' on' : '') + (checked ? ' chk' : ''));
  r.addEventListener('click', () => setActive(n.id));
  const cb = el('div', 'cb' + (checked ? ' on' : ''));
  cb.appendChild(el('div', 'cbi'));
  cb.addEventListener('click', e => { if (e.stopPropagation) e.stopPropagation(); toggleCheck(n.id); });
  r.appendChild(cb);
  const tx = el('div', 'nt');
  tx.appendChild(el('div', 'nn', n.name || n.id || t('Unnamed')));
  tx.appendChild(el('div', 'ns', roleLabel(roleOf(n))));
  r.appendChild(tx);
  const pr = problemsOf(n);
  if (pr.e) r.appendChild(el('div', 'nb bad', String(pr.e)));
  else if (pr.w) r.appendChild(el('div', 'nb warn', String(pr.w)));
  return r;
}

function renderList(reset) {
  if (!E || !npcVl) return;
  const list = filteredNpcs();
  npcVl.set(list, !reset);
  E.selbar.replaceChildren();
  if (multi()) {
    E.selbar.appendChild(el('div', 'selt', t('{n} selected', { n: st.selection.length })));
    E.selbar.appendChild(el('div', 'sp'));
    E.selbar.appendChild(btn(t('Clear'), 'xs', () => { st.selection = st.active ? [st.active] : []; selectionChanged(); }));
  } else if (list.length > 1) {
    E.selbar.appendChild(el('div', 'sp'));
    E.selbar.appendChild(btn(t('Select all shown'), 'xs', () => {
      st.selection = list.map(n => n.id);
      if (st.selection.indexOf(st.active) < 0) st.active = st.selection[0];
      selectionChanged();
    }));
  }
  const acts = E.sideActs;
  acts.replaceChildren();
  acts.appendChild(btn(t('+ New NPC'), 'go', wizard));
  const row = el('div', 'row gap');
  if (multi()) {
    row.appendChild(btn(t('Remove {n}', { n: st.selection.length }), 'sm grow', () => removeNpcs(st.selection.slice())));
  } else if (st.active) {
    row.appendChild(btn(t('Duplicate'), 'sm grow', () => {
      const ps = call('npc.duplicate', { id: st.active });
      if (applyState(ps)) console.log('[personify] duplicated ' + st.active);
    }));
    row.appendChild(btn(t('Remove'), 'sm grow', () => removeNpcs([st.active])));
  }
  acts.appendChild(row);
}

function removeNpcs(ids) {
  const one = ids.length === 1 ? npcById(ids[0]) : null;
  confirmDialog(one ? t('Remove {name}?', { name: one.name || one.id }) : t('Remove {n} NPCs?', { n: ids.length }),
    t('Undo brings them back.'), t('Remove'), () => {
      if (applyState(call('npc.remove', { ids }))) console.log('[personify] removed ' + ids.join(', '));
    }, true);
}

function setActive(id) {
  st.active = id;
  st.selection = [id];
  ui.step = -1;
  ui.pick = null;
  selectionChanged();
}

function toggleCheck(id) {
  if (!multi()) {
    if (id === st.active) return;
    st.selection = st.active ? [st.active, id] : [id];
  } else {
    const i = st.selection.indexOf(id);
    if (i >= 0) st.selection.splice(i, 1); else st.selection.push(id);
    if (st.selection.length === 0) st.selection = [id];
    if (st.selection.indexOf(st.active) < 0) st.active = st.selection[0];
  }
  selectionChanged();
}

function selectionChanged() {
  ui.focusField = null;
  renderList();
  renderFields();
  renderStage();
}

/* --- footer and problems --- */

function counts() {
  let e = 0, w = 0;
  for (const p of st.problems) { if (p.severity === 'error') e++; else w++; }
  return { e, w };
}

function renderFoot() {
  if (!E) return;
  const f = E.foot;
  f.replaceChildren();
  const c = counts();
  const pb = el('div', 'probs' + (c.e ? ' bad' : c.w ? ' warn' : ' ok') + (ui.problemsOpen ? ' open' : ''));
  const parts = [];
  if (c.e) parts.push(c.e === 1 ? t('1 error') : t('{n} errors', { n: c.e }));
  if (c.w) parts.push(c.w === 1 ? t('1 warning') : t('{n} warnings', { n: c.w }));
  pb.appendChild(el('div', 'pbt', parts.length ? parts.join(' · ') : t('No problems')));
  if (parts.length) pb.appendChild(el('div', 'pbc', ui.problemsOpen ? t('Hide') : t('Show')));
  pb.addEventListener('click', () => { if (!st.problems.length) return; ui.problemsOpen = !ui.problemsOpen; renderFoot(); });
  f.appendChild(pb);
  f.appendChild(el('div', 'sp'));
  f.appendChild(el('div', 'ist ' + installTone(st.install), t(INSTALL_TEXT[st.install] || INSTALL_TEXT.not_installed)));
  const tb = btn(t('Test'), 'big', testDialog);
  if (!st.active) tb.classList.add('off');
  f.appendChild(tb);
  const label = st.install === 'changed' ? t('Update') : st.install === 'installed' ? t('Up to date') : t('Add to my game');
  const ib = btn(label, 'big go' + (c.e ? ' off' : st.install === 'installed' ? ' quietgo' : ''), doInstall);
  if (c.e) tip(ib, t('Fix the errors first.'));
  f.appendChild(ib);
  renderProblems();
}

let probNode = null;
function renderProblems() {
  if (probNode) { probNode.remove(); probNode = null; }
  if (!ui.problemsOpen || !st.problems.length) return;
  const box = el('div', 'plist');
  const sorted = st.problems.slice().sort((a, b) => (a.severity === b.severity ? 0 : a.severity === 'error' ? -1 : 1));
  for (const p of sorted) {
    const r = el('div', 'pli ' + (p.severity === 'error' ? 'bad' : 'warn'));
    r.appendChild(el('div', 'plsev', p.severity === 'error' ? t('Error') : t('Warning')));
    const n = typeof p.npc === 'number' && p.npc >= 0 ? npcs()[p.npc] : null;
    const tx = el('div', 'plt');
    tx.appendChild(el('div', 'pln', n ? (n.name || n.id) : t('Pack')));
    tx.appendChild(el('div', 'plm', p.text ? t(p.text) : p.code));
    r.appendChild(tx);
    r.addEventListener('click', () => jumpTo(p));
    box.appendChild(r);
  }
  document.body.appendChild(box);
  probNode = box;
}

function jumpTo(p) {
  ui.problemsOpen = false;
  ui.focusField = p.field || null;
  if (p.area === 'pack' || typeof p.npc !== 'number' || p.npc < 0) { renderFoot(); packDialog(); return; }
  const n = npcs()[p.npc];
  if (!n) { console.warn('[personify] problem points at NPC index ' + p.npc + ' that does not exist'); renderFoot(); return; }
  st.active = n.id;
  st.selection = [n.id];
  if (p.area && p.area !== st.area) { st.area = p.area; ui.pick = null; }
  const idx = filteredNpcs().indexOf(n);
  if (idx < 0) { ui.search = ''; ui.roleFilter = 'all'; }
  renderEditor();
  npcVl.reveal(filteredNpcs().indexOf(n));
}

function doInstall() {
  if (counts().e) { ui.problemsOpen = true; renderFoot(); return; }
  const r = call('install');
  if (!r) { toast(t('The editor host did not answer.'), 'bad'); return; }
  applyDelta(r);
  if (r.ok === false) { console.error('[personify] install failed: ' + (r.code || '') + ' ' + (r.text || '')); toast(r.text || errText(r.code), 'bad'); return; }
  console.log('[personify] installed, code ' + r.code);
  toast(r.text || t('The pack is in your game.'), 'ok');
}

function testDialog() {
  if (!st.active) return;
  const saves = call('test.saves') || [];
  const box = openDialog('mid');
  dialogHead(box, t('Test {name}', { name: (npcById(st.active) || {}).name || st.active }));
  box.appendChild(el('div', 'dsub', t('The game restarts, loads the save and puts you next to the NPC.')));
  let pick = (saves.find(s => s.newest) || saves[0] || {}).slot;
  const list = el('div', 'saves');
  const rows = [];
  if (saves.length === 0) list.appendChild(el('div', 'quiet', t('No save found.')));
  for (const s of saves) {
    const r = el('div', 'sv' + (s.slot === pick ? ' on' : ''));
    const tx = el('div', 'pt');
    tx.appendChild(el('div', 'pnm', s.name || t('Save {n}', { n: s.slot })));
    const sub = [];
    if (s.org) sub.push(s.org);
    if (s.lastPlayed) sub.push(t('Last played {when}', { when: fmtDate(s.lastPlayed) }));
    tx.appendChild(el('div', 'psub', sub.join(' · ')));
    r.appendChild(tx);
    if (s.newest) r.appendChild(el('div', 'ptag', t('Newest')));
    r.addEventListener('click', () => { pick = s.slot; for (const x of rows) x.classList.remove('on'); r.classList.add('on'); });
    rows.push(r);
    list.appendChild(r);
  }
  box.appendChild(list);
  const err = el('div', 'ferr');
  err.style.display = 'none';
  box.appendChild(err);
  const acts = el('div', 'dacts');
  acts.appendChild(el('div', 'sp'));
  acts.appendChild(btn(t('Cancel'), null, closeDialog));
  acts.appendChild(btn(t('Start test'), 'go' + (saves.length ? '' : ' off'), () => {
    if (counts().e) { err.textContent = t('Fix the errors first.'); err.style.display = 'flex'; return; }
    const r = call('test.start', { slot: pick, npcId: st.active });
    if (!r || r.ok === false) { err.textContent = errText(r && r.error); err.style.display = 'flex'; console.error('[personify] test.start failed: ' + (r && r.error)); return; }
    console.log('[personify] test started in save ' + pick + ' for ' + st.active);
    closeDialog();
    toast(t('The game restarts with the test.'), 'ok');
  }));
  box.appendChild(acts);
}

/* --- stage: 3D preview hole or the page-drawn map --- */

let stageMode = null;
let previewOf = null;
let stageRectSent = '';

function setStageMode(m) {
  if (stageMode === m) return;
  stageMode = m;
  const r = call('stage.mode', m);
  if (!r) console.warn('[personify] stage.mode ' + m + ' not taken');
}

function renderStage() {
  if (!E) return;
  const routine = st.area === 'routine';
  setStageMode(routine ? 'none' : 'preview');
  setCls(E.stage, 'map', routine);
  if (!routine && st.active && previewOf !== st.active) {
    previewOf = st.active;
    const r = call('preview.show', st.active);
    if (!r) console.warn('[personify] preview.show ' + st.active + ' not taken');
  }
  if (routine) buildMap();
  else { E.stage.replaceChildren(); mapNodes = null; }
}

function reportStage() {
  if (!E || ui.screen !== 'editor') return;
  const r = rectOf(E.stage);
  if (!r || !r.width) return;
  const rect = { x: Math.round(r.x), y: Math.round(r.y), width: Math.round(r.width), height: Math.round(r.height) };
  const key = JSON.stringify(rect);
  if (key === stageRectSent) return;
  stageRectSent = key;
  if (!call('stage.rect', rect)) console.warn('[personify] stage.rect not taken');
  if (st.area === 'routine' && mapNodes) { mapNodes.fitted = false; layoutMap(); }
}

// Drag on the stage turns the preview (or pans the map), the wheel zooms.
const ptr = { down: false, x: 0, y: 0, sx: 0, sy: 0, moved: false, lx: -1, ly: -1 };
function wireStage() {
  const s = E.stage;
  s.addEventListener('pointerdown', e => {
    ptr.down = true; ptr.moved = false;
    ptr.x = ptr.sx = e.clientX; ptr.y = ptr.sy = e.clientY;
    try { s.setPointerCapture(e.pointerId); } catch (err) { console.warn('[personify] stage capture refused: ' + err.message); }
  });
  s.addEventListener('pointermove', e => {
    ptr.lx = e.clientX; ptr.ly = e.clientY;
    if (st.area === 'routine' && mapNodes) hoverMap(e.clientX, e.clientY);
    if (!ptr.down || !(e.buttons & 1)) return;
    const dx = e.clientX - ptr.x;
    const dy = e.clientY - ptr.y;
    ptr.x = e.clientX; ptr.y = e.clientY;
    if (Math.abs(e.clientX - ptr.sx) + Math.abs(e.clientY - ptr.sy) > 4) ptr.moved = true;
    if (!ptr.moved) return;
    if (st.area === 'routine') panMap(dx, dy);
    else if (dx) call('preview.rotate', round(dx * 0.6, 1));
  });
  s.addEventListener('pointerup', e => {
    const wasDown = ptr.down;
    ptr.down = false;
    if (wasDown && !ptr.moved && st.area === 'routine') mapClick(e.clientX, e.clientY);
  });
  s.addEventListener('lostpointercapture', () => { ptr.down = false; });
  s.addEventListener('wheel', e => {
    const d = wheelDir(e);
    if (e.preventDefault) e.preventDefault();
    if (!d) return;
    if (st.area === 'routine') zoomMap(d);
    else call('preview.zoom', d);
  });
}

/* --- map (Routine) --- */

let mapNodes = null;
const mv = { z: 1, px: 0, py: 0, W: 0, H: 0, sw: 0, sh: 0 };

function loadMap() {
  if (st.map) return st.map;
  st.map = call('world.map') || { hasMap: false };
  if (!st.map.places) st.map.places = [];
  if (!st.map.vehicles) st.map.vehicles = [];
  return st.map;
}

function buildMap() {
  const m = loadMap();
  E.stage.replaceChildren();
  mapNodes = null;
  if (!m.hasMap || !(m.maxX > m.minX) || !(m.maxZ > m.minZ)) {
    const box = el('div', 'nomap');
    box.appendChild(el('div', 'emsg', t('Load a save once to get the map.')));
    E.stage.appendChild(box);
    return;
  }
  const wrap = el('div', 'mapwrap');
  let img;
  if (m.image) { img = el('img', 'mapimg'); img.setAttribute('src', m.image); }
  else img = el('div', 'mapimg mapph');
  wrap.appendChild(img);
  const ov = el('div', 'mapov');
  wrap.appendChild(ov);
  const bar = el('div', 'mapbar');
  wrap.appendChild(bar);
  E.stage.appendChild(wrap);
  mapNodes = { wrap, img, ov, bar, fitted: false };
  layoutMap();
}

function layoutMap() {
  if (!mapNodes) return;
  const m = st.map;
  const r = rectOf(E.stage);
  const sw = r && r.width ? r.width : 900;
  const sh = r && r.height ? r.height : 700;
  const dx = m.maxX - m.minX;
  const dz = m.maxZ - m.minZ;
  const s = Math.min(sw / dx, sh / dz);
  if (!mapNodes.fitted || mv.sw !== sw || mv.sh !== sh) {
    mv.W = dx * s; mv.H = dz * s; mv.sw = sw; mv.sh = sh;
    mv.z = 1; mv.px = (sw - mv.W) / 2; mv.py = (sh - mv.H) / 2;
    mapNodes.fitted = !!(r && r.width);
  }
  mapNodes.img.style.width = Math.round(mv.W) + 'px';
  mapNodes.img.style.height = Math.round(mv.H) + 'px';
  applyMapTransform();
  drawMapOverlay();
}

function applyMapTransform() {
  if (!mapNodes) return;
  mapNodes.img.style.transform = 'translate(' + round(mv.px, 1) + 'px, ' + round(mv.py, 1) + 'px) scale(' + round(mv.z, 3) + ')';
}

function w2s(x, z) {
  const m = st.map;
  const u = (x - m.minX) / (m.maxX - m.minX);
  const v = (z - m.minZ) / (m.maxZ - m.minZ);
  return { x: mv.px + u * mv.W * mv.z, y: mv.py + (1 - v) * mv.H * mv.z };
}
function s2w(sx, sy) {
  const m = st.map;
  const u = (sx - mv.px) / (mv.W * mv.z);
  const v = 1 - (sy - mv.py) / (mv.H * mv.z);
  return { x: m.minX + u * (m.maxX - m.minX), z: m.minZ + v * (m.maxZ - m.minZ), inside: u >= 0 && u <= 1 && v >= 0 && v <= 1 };
}

function panMap(dx, dy) {
  if (!mapNodes) return;
  mv.px += dx; mv.py += dy;
  applyMapTransform();
  drawMapOverlay();
}

function zoomMap(d) {
  if (!mapNodes) return;
  const r = rectOf(E.stage) || { x: 0, y: 0, width: mv.sw, height: mv.sh };
  const inside = ptr.lx >= r.x && ptr.lx <= r.x + r.width && ptr.ly >= r.y && ptr.ly <= r.y + r.height;
  const cx = inside ? ptr.lx - r.x : r.width / 2;
  const cy = inside ? ptr.ly - r.y : r.height / 2;
  const nz = Math.max(1, Math.min(10, mv.z * (d < 0 ? 1.25 : 0.8)));
  if (nz === mv.z) return;
  mv.px = cx - (cx - mv.px) * nz / mv.z;
  mv.py = cy - (cy - mv.py) * nz / mv.z;
  mv.z = nz;
  applyMapTransform();
  drawMapOverlay();
}

// The kinds of place a pick snaps to, by what is being placed. Empty = any kind.
function pickKinds(pick) {
  if (!pick || pick.what !== 'step') return [];
  const s = scheduleOf()[pick.index] || {};
  if (s.type === 'stayInBuilding') return ['building'];
  if (s.type === 'driveToCarPark') return ['carpark'];
  if (s.type === 'sit') return ['seat'];
  return [];
}
const pickNeedsPlace = pick => pickKinds(pick).length > 0;

function placeNear(x, z, kinds, radius) {
  let best = null;
  let bd = radius * radius;
  for (const p of st.map.places) {
    if (kinds.length && kinds.indexOf(p.kind) < 0) continue;
    const d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
    if (d <= bd) { bd = d; best = p; }
  }
  return best;
}

function placeByName(kind, name) {
  if (!name || !st.map) return null;
  return st.map.places.find(p => p.kind === kind && p.name === name) || null;
}

// World point of a schedule step, for its pin: its own position, or the named place it goes to.
function stepPoint(s) {
  if (Array.isArray(s.position) && s.position.length >= 3) return { x: s.position[0], z: s.position[2] };
  const p = s.type === 'stayInBuilding' ? placeByName('building', s.building)
    : s.type === 'driveToCarPark' ? placeByName('carpark', s.parkingLot)
      : s.type === 'sit' ? (placeByName('seat', s.seatSetPath) || placeByName('seat', s.seatSet)) : null;
  return p ? { x: p.x, z: p.z } : null;
}

function drawMapOverlay() {
  if (!mapNodes || !st.map) return;
  const ov = mapNodes.ov;
  ov.replaceChildren();
  const n = npcById(st.active);
  if (!n) return;
  const pts = [];
  const sp = n.spawn;
  if (sp && typeof sp.x === 'number' && typeof sp.z === 'number') pts.push({ p: w2s(sp.x, sp.z), label: t('Start'), cls: 'spawn', idx: -2 });
  scheduleOf().forEach((s, i) => {
    const w = stepPoint(s);
    if (w) pts.push({ p: w2s(w.x, w.z), label: String(i + 1), cls: i === ui.step ? 'on' : '', idx: i });
  });

  // Route: lines between consecutive points in time order, starting at the spawn.
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1].p;
    const b = pts[i].p;
    const len = Math.sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
    if (len < 2) continue;
    const ln = el('div', 'mline');
    ln.style.left = Math.round(a.x) + 'px';
    ln.style.top = Math.round(a.y - 1) + 'px';
    ln.style.width = Math.round(len) + 'px';
    ln.style.transform = 'rotate(' + round(Math.atan2(b.y - a.y, b.x - a.x) * 180 / Math.PI, 1) + 'deg)';
    ov.appendChild(ln);
  }

  // While picking, the places this pick can snap to.
  if (ui.pick) {
    const kinds = pickKinds(ui.pick);
    let shown = 0;
    for (const p of st.map.places) {
      if (kinds.length && kinds.indexOf(p.kind) < 0) continue;
      const q = w2s(p.x, p.z);
      if (q.x < 0 || q.y < 0 || q.x > mv.sw || q.y > mv.sh) continue;
      const d = el('div', 'mplace ' + p.kind);
      d.style.left = Math.round(q.x - 3) + 'px';
      d.style.top = Math.round(q.y - 3) + 'px';
      ov.appendChild(d);
      if (++shown >= 300) break;
    }
  }

  for (const it of pts) {
    const pin = el('div', 'mpin ' + it.cls, it.label);
    const w = it.idx === -2 ? 44 : 24;
    pin.style.left = Math.round(it.p.x - w / 2) + 'px';
    pin.style.top = Math.round(it.p.y - 12) + 'px';
    ov.appendChild(pin);
  }
  mapNodes.pins = pts;
  drawMapBar();
}

function drawMapBar(hoverText) {
  if (!mapNodes) return;
  const bar = mapNodes.bar;
  bar.replaceChildren();
  if (ui.pick) {
    const what = ui.pick.what === 'spawn' ? t('Click the map to set the start point.')
      : pickNeedsPlace(ui.pick) ? t('Click a marked place.') : t('Click the map to set the point.');
    bar.appendChild(el('div', 'mbt', what));
    if (hoverText) bar.appendChild(el('div', 'mbh', hoverText));
    bar.appendChild(el('div', 'sp'));
    bar.appendChild(btn(t('Cancel'), 'xs', () => { ui.pick = null; drawMapOverlay(); renderFields(); }));
  } else {
    bar.appendChild(el('div', 'mbt mu', t('Drag to move, wheel to zoom.')));
    bar.appendChild(el('div', 'sp'));
    bar.appendChild(btn(t('Fit'), 'xs', () => { mapNodes.fitted = false; layoutMap(); }));
  }
}

let hoverKey = '';
function hoverMap(cx, cy) {
  if (!ui.pick || !mapNodes) return;
  const r = rectOf(E.stage);
  if (!r) return;
  const w = s2w(cx - r.x, cy - r.y);
  const kinds = pickKinds(ui.pick);
  const p = w.inside ? placeNear(w.x, w.z, kinds, kinds.length ? 12 : 4) : null;
  const key = p ? p.kind + ':' + p.name : '';
  if (key === hoverKey) return;
  hoverKey = key;
  drawMapBar(p ? t('Snaps to {place}', { place: p.label || p.name }) : '');
}

function mapClick(cx, cy) {
  if (!mapNodes) return;
  const r = rectOf(E.stage);
  if (!r) return;
  const sx = cx - r.x;
  const sy = cy - r.y;
  if (!ui.pick) {
    // Without a pick a click on a pin selects its step.
    let best = null;
    let bd = 16 * 16;
    for (const it of mapNodes.pins || []) {
      const d = (it.p.x - sx) * (it.p.x - sx) + (it.p.y - sy) * (it.p.y - sy);
      if (d <= bd) { bd = d; best = it; }
    }
    if (best && best.idx >= 0) { ui.step = best.idx; drawMapOverlay(); renderFields(); }
    return;
  }
  const w = s2w(sx, sy);
  if (!w.inside) return;
  const kinds = pickKinds(ui.pick);
  const place = placeNear(w.x, w.z, kinds, kinds.length ? 12 : 4);
  if (kinds.length && !place) { toast(t('No matching place there.'), 'warn'); return; }
  let x = w.x, z = w.z, y = 0, region = null;
  if (place) { x = place.x; z = place.z; y = place.y; }
  const g = call('world.ground', { x: round(x), z: round(z) });
  if (g && g.ok !== false) {
    // The host pulls x/z onto the nearest walkable point; a snapped place keeps its own position.
    if (!place) { y = g.y; if (typeof g.x === 'number') x = g.x; if (typeof g.z === 'number') z = g.z; }
    region = g.region || null;
    if (g.walkable === false && !place) toast(t('That spot is not walkable.'), 'warn');
  } else console.warn('[personify] world.ground gave no height for ' + round(x) + ', ' + round(z));
  applyPick(ui.pick, { x: round(x), y: round(y), z: round(z), region, place });
  ui.pick = null;
  hoverKey = '';
  drawMapOverlay();
  renderFields();
}

function applyPick(pick, at) {
  if (pick.what === 'spawn') {
    const ch = [{ path: 'spawn.x', value: at.x }, { path: 'spawn.y', value: at.y }, { path: 'spawn.z', value: at.z }];
    if (at.region) ch.push({ path: 'spawn.region', value: at.region });
    write(ch);
    console.log('[personify] spawn of ' + st.active + ' set to ' + at.x + ', ' + at.y + ', ' + at.z);
    return;
  }
  const steps = clone(scheduleOf());
  const s = steps[pick.index];
  if (!s) { console.warn('[personify] pick for missing step ' + pick.index); return; }
  if (pick.what === 'car') {
    s.createVehicle = Object.assign({ code: (s.createVehicle && s.createVehicle.code) || '' }, s.createVehicle || {}, { position: [at.x, at.y, at.z] });
  } else if (s.type === 'stayInBuilding' && at.place) s.building = at.place.name;
  else if (s.type === 'driveToCarPark' && at.place) s.parkingLot = at.place.name;
  else if (s.type === 'sit' && at.place) { s.seatSetPath = at.place.name; delete s.seatSet; }
  else s.position = [at.x, at.y, at.z];
  writeSchedule(steps, pick.index);
}

/* ---------- fields panel ---------- */

function renderFields() {
  if (!E) return;
  sweepInputs(E.fields);
  E.fhead.replaceChildren();
  E.fbody.replaceChildren();
  const n = npcById(st.active);
  if (!n) {
    E.fbody.appendChild(el('div', 'quiet pad', t('No NPC yet.')));
    E.fbody.appendChild(btn(t('+ New NPC'), 'go', wizard));
    return;
  }
  if (multi()) {
    E.fhead.appendChild(el('div', 'fht', t('{n} NPCs selected', { n: st.selection.length })));
    E.fhead.appendChild(el('div', 'fhs', t('Changes apply to all selected NPCs.')));
  } else {
    E.fhead.appendChild(textInput(n.name || '', t('Name'), v => {
      const name = v.trim();
      if (!name) { toast(t('Enter a name.'), 'bad'); renderFields(); return; }
      write([{ path: 'name', value: name }]);
    }, { cls: 'nameinp' }));
    const sub = el('div', 'fhs');
    sub.textContent = roleLabel(roleOf(n)) + (st.expert ? ' · ' + n.id : '');
    E.fhead.appendChild(sub);
  }
  const box = E.fbody;
  if (st.area === 'look') buildLook(box, n);
  else if (st.area === 'role') buildRole(box, n);
  else if (st.area === 'routine') buildRoutine(box, n);
  else buildRelations(box, n);
  showFocus();
}

// A problem jump opens the section holding the field and marks the row.
function showFocus() {
  const f = ui.focusField;
  if (!f) return;
  ui.focusField = null;
  const rows = E.fbody.querySelectorAll('[data-path]');
  let hit = null;
  for (const r of rows) {
    const p = r.getAttribute('data-path');
    if (p === f || f.indexOf(p + '.') === 0 || p.indexOf(f + '.') === 0) { hit = r; break; }
  }
  if (!hit) { console.warn('[personify] problem field ' + f + ' has no row in ' + st.area); return; }
  hit.classList.add('hl');
  const sec = hit.closest ? hit.closest('.sec') : null;
  if (sec) {
    for (const s of E.fbody.querySelectorAll('.sec')) setSection(s, s === sec);
  }
}

function setSection(sec, open) {
  setCls(sec, 'open', open);
  const body = sec.querySelector('.secb');
  if (body) body.style.display = open ? 'flex' : 'none';
  const mark = sec.querySelector('.secm');
  if (mark) mark.textContent = open ? '-' : '+';
  const id = sec.getAttribute('data-sec');
  if (!ui.open[st.area]) ui.open[st.area] = {};
  ui.open[st.area][id] = open;
}

function section(box, id, title, build, defOpen) {
  const area = ui.open[st.area] || (ui.open[st.area] = {});
  const open = area[id] !== undefined ? area[id] : !!defOpen;
  const sec = el('div', 'sec' + (open ? ' open' : ''));
  sec.setAttribute('data-sec', id);
  const h = el('div', 'sech');
  h.appendChild(el('div', 'secn', t(title)));
  h.appendChild(el('div', 'sp'));
  h.appendChild(el('div', 'secm', open ? '-' : '+'));
  h.addEventListener('click', () => setSection(sec, !sec.classList.contains('open')));
  sec.appendChild(h);
  const body = el('div', 'secb');
  if (!open) body.style.display = 'none';
  build(body);
  sec.appendChild(body);
  box.appendChild(sec);
  return sec;
}

function frow(box, label, path) {
  const r = el('div', 'fr');
  if (path) r.setAttribute('data-path', path);
  r.appendChild(el('div', 'fl', t(label)));
  box.appendChild(r);
  return r;
}

function line(box, text, cls) { const n = el('div', 'note' + (cls ? ' ' + cls : ''), text); box.appendChild(n); return n; }
function mixedTag(row) { row.appendChild(el('div', 'mxt', t('Mixed'))); }
const live = () => st.area === 'look';

// A numeric slider. o: { min, max, step, def, xmin, xmax (expert range), whole, fmt, parse, store, pair, extra }.
// `pair` is [otherPath, otherDef]: the other end of a min/max range is written with it while it is still unset,
// because Personnel fills a missing end with the other one.
function slider(box, label, path, o) {
  const paths = Array.isArray(path) ? path : [path];
  const f = readField(paths[0], o.def);
  const parse = o.parse || (v => v);
  let v = parse(f.value);
  if (typeof v !== 'number' || !isFinite(v)) v = typeof o.def === 'number' ? o.def : o.min;
  let min = st.expert && o.xmin !== undefined ? o.xmin : o.min;
  let max = st.expert && o.xmax !== undefined ? o.xmax : o.max;
  // A stored value beyond the rails widens them instead of snapping back.
  if (v < min) min = v;
  if (v > max) max = v;
  const step = o.step || (o.whole ? 1 : 0.01);
  const fmt = o.fmt || fmtNum;
  const row = frow(box, label, paths[0]);
  const rng = el('input', 'rng');
  rng.setAttribute('type', 'range');
  rng.setAttribute('min', String(min));
  rng.setAttribute('max', String(max));
  rng.setAttribute('step', String(step));
  rng.value = String(v);
  row.appendChild(rng);
  const out = x => { const y = o.whole ? Math.round(x) : round(x, o.dec === undefined ? 2 : o.dec); return o.store ? o.store(y) : y; };
  const changes = x => {
    const val = out(x);
    const ch = paths.map(p => ({ path: p, value: val }));
    if (o.pair && readField(o.pair[0]).unset) ch.push({ path: o.pair[0], value: o.pair[1] });
    if (o.extra) for (const e of o.extra(val)) ch.push(e);
    return ch;
  };
  let shown;
  if (st.expert && !o.noType) {
    shown = textInput(f.mixed ? '' : String(round(v, 3)), f.mixed ? t('Mixed') : '', s => {
      const x = Number(String(s).replace(',', '.'));
      if (!isFinite(x)) { toast(t('Enter a number.'), 'bad'); return; }
      rng.value = String(x);
      write(changes(x));
    }, { cls: 'num' });
  } else {
    shown = el('div', 'fv' + (f.unset ? ' def' : ''), f.mixed ? t('Mixed') : fmt(v));
  }
  row.appendChild(shown);
  let lastLive = 0;
  const show = x => { if (shown.tagName === 'INPUT') shown.value = String(round(x, 3)); else { shown.textContent = fmt(x); shown.classList.remove('def'); } };
  rng.addEventListener('input', e => {
    const x = Number(valueOf(e));
    show(x);
    const now = Date.now();
    if (live() && now - lastLive > 120) { lastLive = now; write(changes(x), { live: true }); }
  });
  rng.addEventListener('change', e => {
    const x = Number(valueOf(e));
    show(x);
    write(changes(x));
    if (o.after) o.after();
  });
  return row;
}

function toggle(box, label, path, def, after) {
  const f = readField(path, def);
  const row = frow(box, label, path);
  row.appendChild(el('div', 'sp'));
  if (f.mixed) mixedTag(row);
  row.appendChild(sw(!!f.value, on => { write([{ path, value: on }]); if (after) after(); }, f.mixed));
  return row;
}

function segRow(box, label, path, options, def, after) {
  const f = readField(path, def);
  const row = frow(box, label, path);
  row.classList.add('wide');
  row.appendChild(segEl(options.map(([v, l]) => [v, t(l)]), f.mixed ? '__mixed' : f.value, v => { write([{ path, value: v }]); if (after) after(); }));
  return row;
}

function selectRow(box, label, path, options, def, opts) {
  opts = opts || {};
  const f = readField(path, def);
  const row = frow(box, label, path);
  row.appendChild(selectEl(options, f.value, v => {
    if (v === '' && opts.none) write([{ path, unset: true }]);
    else write([{ path, value: v }]);
    if (opts.after) opts.after();
  }, { mixed: f.mixed, none: opts.none, noneLabel: opts.noneLabel }));
  return row;
}

function textRow(box, label, path, ph, opts) {
  opts = opts || {};
  const f = readField(path, '');
  const row = frow(box, label, path);
  if (opts.multi) row.classList.add('stack');
  row.appendChild(textInput(f.mixed ? '' : (f.value || ''), f.mixed ? t('Mixed') : ph, v => {
    if (!v.trim() && !opts.keepEmpty) write([{ path, unset: true }]);
    else write([{ path, value: v }]);
  }, { multi: opts.multi }));
  return row;
}

function colorRow(box, label, path, def, extra) {
  const f = readField(path, def);
  const row = frow(box, label, path);
  row.appendChild(el('div', 'sp'));
  if (f.mixed) mixedTag(row);
  const ch = v => [{ path, value: v }].concat(extra ? extra(v) : []);
  row.appendChild(colorEl(f.value, v => write(ch(v), { live: true }), v => write(ch(v))));
  return row;
}

// A JSON block as raw text, for experts. An unreadable text is refused and the field keeps what was stored.
function jsonRow(box, label, path) {
  const f = readField(path);
  const row = frow(box, label, path);
  row.classList.add('stack');
  row.appendChild(textInput(f.mixed ? '' : (f.value === undefined ? '' : JSON.stringify(f.value, null, 2)), f.mixed ? t('Mixed') : '{ }', v => {
    if (!v.trim()) { write([{ path, unset: true }]); return; }
    let parsed;
    try { parsed = JSON.parse(v); }
    catch (e) { console.warn('[personify] ' + path + ' JSON refused: ' + e.message); toast(t('This is not valid JSON.'), 'bad'); return; }
    write([{ path, value: parsed }]);
  }, { multi: true, cls: 'code' }));
  return row;
}

function lockLine(box, text) {
  const n = el('div', 'lock');
  n.appendChild(el('div', 'lockt', text));
  box.appendChild(n);
}

/* ---------- Look ---------- */

const catalog = key => (st.look && st.look[key]) || [];
const pathSet = list => { const s = new Set(); for (const o of list) s.add(o.path); return s; };
const nameOfPath = p => {
  if (!p) return t('None');
  const keys = ['hair', 'tops', 'bottoms', 'shoes', 'headwear', 'eyewear', 'mouths', 'facialHair', 'facialDetails', 'tattoos', 'faceLayers', 'bodyLayers', 'accessories'];
  for (const k of keys) { const o = catalog(k).find(x => x.path === p); if (o) return o.name; }
  const i = p.lastIndexOf('/');
  return (i >= 0 ? p.slice(i + 1) : p).replace(/_/g, ' ');
};
const asItems = list => list.map(o => ({ value: o.path, label: o.name || nameOfPath(o.path), group: o.group }));
const tintOf = l => (l && (l.tint || l.color)) || '#FFFFFF';

const BONES = [
  ['HeadBone', T('Head'), true], ['HipBone', T('Hips'), false], ['LeftFootBone', T('Left foot'), true],
  ['RightFootBone', T('Right foot'), true], ['LeftShoulder', T('Left shoulder'), true], ['RightShoulder', T('Right shoulder'), true],
  ['MiddleSpine', T('Mid spine'), true], ['LowerSpine', T('Lower spine'), true], ['LowestSpine', T('Lowest spine'), true],
  ['FaceMesh', T('Face mesh'), true],
];

function buildLook(box, n) {
  if (multi()) { lockLine(box, t('The look is edited one NPC at a time.')); return; }
  const acts = el('div', 'acts');
  acts.appendChild(btn(t('Random look'), 'sm', () => {
    if (applyState(call('npc.randomLook', { id: n.id }))) { previewOf = null; renderStage(); console.log('[personify] random look for ' + n.id); }
  }));
  acts.appendChild(btn(n.portrait ? t('New contact picture') : t('Contact picture'), 'sm', () => {
    const d = call('portrait.capture', n.id);
    if (!d || d.ok === false) { toast(errText(d && d.error), 'bad'); return; }
    applyDelta(d);
    // The host writes the field; the Delta carries its value for the page's copy.
    const fresh = npcById(n.id);
    if (fresh && d.portrait) fresh.portrait = d.portrait;
    else if (!d.portrait) console.warn('[personify] portrait.capture answered without a portrait path');
    console.log('[personify] contact picture taken for ' + n.id);
    toast(t('Contact picture saved.'), 'ok');
    renderFields();
  }));
  box.appendChild(acts);

  const skinExtra = v => (n.personify && n.personify.eyelidsCustom ? [] :
    [{ path: 'appearance.leftEyeLidColor', value: v }, { path: 'appearance.rightEyeLidColor', value: v }]);

  section(box, 'body', T('Body'), b => {
    slider(b, T('Gender'), 'appearance.gender', { min: 0, max: 1, def: 0, xmin: -3, xmax: 4, fmt: v => (v <= 0.1 ? t('Male') : v >= 0.9 ? t('Female') : fmtNum(v)) });
    slider(b, T('Height'), 'appearance.height', { min: 0.5, max: 1.5, def: 0.98, xmin: 0.05, xmax: 4 });
    slider(b, T('Weight'), 'appearance.weight', { min: 0, max: 1, def: 0.4, xmin: -3, xmax: 4 });
    colorRow(b, T('Skin'), 'appearance.skinColor', '#96785F', skinExtra);
  }, true);

  section(box, 'hair', T('Hair'), b => {
    pathRow(b, T('Style'), 'appearance.hairPath', catalog('hair'));
    colorRow(b, T('Colour'), 'appearance.hairColor', '#101014');
  });

  section(box, 'face', T('Face'), b => {
    slotRow(b, T('Mouth'), 'faceLayers', catalog('mouths'), false);
    slotRow(b, T('Facial hair'), 'faceLayers', catalog('facialHair'), false);
    slotRow(b, T('Eye details'), 'faceLayers', catalog('facialDetails'), false);
  });

  section(box, 'eyes', T('Eyes'), b => {
    colorRow(b, T('Eye colour'), 'appearance.eyeBallTint', '#FFFFFF');
    slider(b, T('Pupil'), 'appearance.pupilDilation', { min: 0, max: 1, def: 1, xmax: 5 });
    slider(b, T('Upper lid'), ['appearance.leftEye.top', 'appearance.rightEye.top'], { min: 0, max: 1, def: 0.5, xmin: -2, xmax: 3 });
    slider(b, T('Lower lid'), ['appearance.leftEye.bottom', 'appearance.rightEye.bottom'], { min: 0, max: 1, def: 0.5, xmin: -2, xmax: 3 });
    if (st.expert) {
      slider(b, T('Right upper lid'), 'appearance.rightEye.top', { min: 0, max: 1, def: 0.5, xmin: -2, xmax: 3 });
      slider(b, T('Right lower lid'), 'appearance.rightEye.bottom', { min: 0, max: 1, def: 0.5, xmin: -2, xmax: 3 });
      const lidMark = [{ path: 'personify.eyelidsCustom', value: true }];
      colorRow(b, T('Left eyelid'), 'appearance.leftEyeLidColor', '#96785F', () => lidMark);
      colorRow(b, T('Right eyelid'), 'appearance.rightEyeLidColor', '#96785F', () => lidMark);
      const mats = (st.look && st.look.eyeballMaterials) || [];
      if (mats.length) selectRow(b, T('Eyeball material'), 'appearance.eyeballMaterial', mats.map(m => [m, m]), 'Default');
    }
  });

  section(box, 'brows', T('Eyebrows'), b => {
    slider(b, T('Size'), 'appearance.eyebrowScale', { min: 0, max: 2, def: 1, xmin: -5, xmax: 10 });
    slider(b, T('Thickness'), 'appearance.eyebrowThickness', { min: 0, max: 3, def: 1, xmin: -5, xmax: 10 });
    slider(b, T('Brow height'), 'appearance.eyebrowRestingHeight', { min: -1, max: 1, def: 0, xmin: -10, xmax: 10 });
    slider(b, T('Angle'), 'appearance.eyebrowRestingAngle', { min: -4, max: 11, def: 0, xmin: -180, xmax: 180 });
  });

  section(box, 'clothes', T('Clothing'), b => {
    slotRow(b, T('Top'), 'bodyLayers', catalog('tops'), true);
    slotRow(b, T('Bottom'), 'bodyLayers', catalog('bottoms'), true);
    slotRow(b, T('Shoes'), 'accessories', catalog('shoes'), true);
    slotRow(b, T('Headwear'), 'accessories', catalog('headwear'), true);
    slotRow(b, T('Eyewear'), 'accessories', catalog('eyewear'), true);
  });

  section(box, 'acc', T('Accessories'), b => accessoryRows(b, n));
  section(box, 'tattoos', T('Tattoos'), b => tattooRows(b, n));

  if (st.expert) {
    section(box, 'xfaceLayers', T('Face layers'), b => layerStack(b, n, 'faceLayers'));
    section(box, 'xbodyLayers', T('Body layers'), b => layerStack(b, n, 'bodyLayers'));
    section(box, 'xaccessories', T('Accessory layers'), b => layerStack(b, n, 'accessories'));
    section(box, 'xdistort', T('Distortion'), b => {
      line(b, t('Unclamped bone scale for body horror; not a vanilla look.'), 'mu');
      for (const [key, label, canHide] of BONES) {
        const base = 'appearance.distortion.' + key;
        const hidden = canHide && readField(base + '.hide', false).value === true;
        const head = el('div', 'subh', t(label));
        b.appendChild(head);
        if (canHide) toggle(b, T('Hide'), base + '.hide', false, renderFields);
        if (hidden || key === 'FaceMesh') continue;
        slider(b, T('Scale X'), base + '.scaleX', { min: 0, max: 8, def: 1 });
        slider(b, T('Scale Y'), base + '.scaleY', { min: 0, max: 8, def: 1 });
        slider(b, T('Scale Z'), base + '.scaleZ', { min: 0, max: 8, def: 1 });
      }
    });
  }
}

// A path field picked from a catalog: the hair style.
function pathRow(box, label, path, options) {
  const f = readField(path, '');
  const row = frow(box, label, path);
  const b = btn(f.mixed ? t('Mixed') : nameOfPath(f.value), 'pickb', () => pickDialog(t(label), asItems(options), { current: f.value, allowNone: true }, v => {
    write([v ? { path, value: v } : { path, unset: true }]);
    renderFields();
  }));
  row.appendChild(b);
  return row;
}

// A vanilla-creator slot: owns the one layer of `list` whose path is in `options`.
function slotRow(box, label, list, options, withTint) {
  const n = npcById(st.active);
  const arr = getPath(n, 'appearance.' + list) || [];
  const set = pathSet(options);
  const idx = arr.findIndex(l => l && set.has(l.path));
  const cur = idx >= 0 ? arr[idx] : null;
  const row = frow(box, label, 'appearance.' + list);
  row.appendChild(btn(cur ? nameOfPath(cur.path) : t('None'), 'pickb', () =>
    pickDialog(t(label), asItems(options), { current: cur && cur.path, allowNone: true }, path => {
      const next = clone(arr);
      if (!path) { if (idx >= 0) next.splice(idx, 1); }
      else {
        const o = options.find(x => x.path === path) || {};
        const entry = { path, tint: cur ? tintOf(cur) : (o.tint || '#FFFFFF') };
        if (idx >= 0) next[idx] = entry; else next.push(entry);
      }
      write([{ path: 'appearance.' + list, value: next }]);
      renderFields();
    })));
  if (withTint && cur) {
    const tintAt = v => { const next = clone(arr); next[idx].tint = v; delete next[idx].color; return [{ path: 'appearance.' + list, value: next }]; };
    row.appendChild(colorEl(tintOf(cur), v => write(tintAt(v), { live: true }), v => write(tintAt(v))));
  }
  return row;
}

function accessoryRows(box, n) {
  const arr = getPath(n, 'appearance.accessories') || [];
  const slots = new Set([...pathSet(catalog('shoes')), ...pathSet(catalog('headwear')), ...pathSet(catalog('eyewear'))]);
  const others = [];
  arr.forEach((l, i) => { if (l && !slots.has(l.path)) others.push(i); });
  if (!others.length) line(box, t('No other accessories.'), 'mu');
  for (const i of others) layerRow(box, n, 'accessories', arr, i, { noOrder: true });
  const options = catalog('accessories').filter(o => !slots.has(o.path));
  box.appendChild(btn(t('+ Add accessory'), 'sm', () => pickDialog(t('Accessories'), asItems(options), {}, path => {
    if (!path) return;
    const o = options.find(x => x.path === path) || {};
    write([{ path: 'appearance.accessories', value: arr.concat([{ path, tint: o.tint || '#FFFFFF' }]) }]);
    renderFields();
  })));
}

function tattooRows(box, n) {
  const tats = catalog('tattoos');
  const set = pathSet(tats);
  let any = false;
  for (const list of ['faceLayers', 'bodyLayers']) {
    const arr = getPath(n, 'appearance.' + list) || [];
    arr.forEach((l, i) => { if (l && set.has(l.path)) { any = true; layerRow(box, n, list, arr, i, { noOrder: true }); } });
  }
  if (!any) line(box, t('No tattoos.'), 'mu');
  box.appendChild(btn(t('+ Add tattoo'), 'sm', () => pickDialog(t('Tattoos'), asItems(tats), {}, path => {
    if (!path) return;
    const o = tats.find(x => x.path === path) || {};
    const list = String(o.group || '').toLowerCase() === 'face' || path.indexOf('/Face/') >= 0 ? 'faceLayers' : 'bodyLayers';
    const arr = getPath(npcById(st.active), 'appearance.' + list) || [];
    write([{ path: 'appearance.' + list, value: arr.concat([{ path, tint: o.tint || '#FFFFFF' }]) }]);
    renderFields();
  })));
}

// One entry of a layer list: name, tint, visible switch, order, remove. A hidden layer moves into
// personify.hiddenLayers, so it stays in the project and out of the installed pack.
function layerRow(box, n, list, arr, i, opts) {
  opts = opts || {};
  const l = arr[i];
  const r = el('div', 'lr');
  r.setAttribute('data-path', 'appearance.' + list);
  r.appendChild(el('div', 'lrn', l.file ? l.file : nameOfPath(l.path)));
  const tintAt = v => { const next = clone(arr); next[i].tint = v; delete next[i].color; return [{ path: 'appearance.' + list, value: next }]; };
  r.appendChild(colorEl(tintOf(l), v => write(tintAt(v), { live: true }), v => write(tintAt(v))));
  if (opts.visible !== false) {
    r.appendChild(sw(true, () => {
      const next = clone(arr);
      const [gone] = next.splice(i, 1);
      const hidden = clone(getPath(n, 'personify.hiddenLayers') || []);
      hidden.push(Object.assign({ list, index: i }, gone));
      write([{ path: 'appearance.' + list, value: next }, { path: 'personify.hiddenLayers', value: hidden }]);
      renderFields();
    }));
  }
  if (!opts.noOrder) {
    r.appendChild(btn(t('Up'), 'xs' + (i === 0 ? ' off' : ''), () => moveLayer(list, arr, i, -1)));
    r.appendChild(btn(t('Down'), 'xs' + (i === arr.length - 1 ? ' off' : ''), () => moveLayer(list, arr, i, 1)));
  }
  r.appendChild(btn(t('Remove'), 'xs', () => {
    const next = clone(arr);
    next.splice(i, 1);
    write([{ path: 'appearance.' + list, value: next }]);
    renderFields();
  }));
  box.appendChild(r);
}

function moveLayer(list, arr, i, d) {
  const next = clone(arr);
  const j = i + d;
  if (j < 0 || j >= next.length) return;
  const x = next[i]; next[i] = next[j]; next[j] = x;
  write([{ path: 'appearance.' + list, value: next }]);
  renderFields();
}

function layerStack(box, n, list) {
  const arr = getPath(n, 'appearance.' + list) || [];
  if (!arr.length) line(box, t('No layers.'), 'mu');
  arr.forEach((l, i) => { if (l) layerRow(box, n, list, arr, i); });
  const hidden = getPath(n, 'personify.hiddenLayers') || [];
  hidden.forEach((h, hi) => {
    if (h.list !== list) return;
    const r = el('div', 'lr off');
    r.appendChild(el('div', 'lrn', (h.file || nameOfPath(h.path)) + ' (' + t('hidden') + ')'));
    r.appendChild(sw(false, () => {
      const back = clone(hidden);
      const [entry] = back.splice(hi, 1);
      const next = clone(arr);
      const at = Math.max(0, Math.min(next.length, entry.index === undefined ? next.length : entry.index));
      delete entry.list; delete entry.index;
      next.splice(at, 0, entry);
      write([{ path: 'appearance.' + list, value: next }, back.length ? { path: 'personify.hiddenLayers', value: back } : { path: 'personify.hiddenLayers', unset: true }]);
      renderFields();
    }));
    box.appendChild(r);
  });
  const options = catalog(list);
  box.appendChild(btn(t('+ Add layer'), 'sm', () => pickDialog(t('Add layer'), asItems(options), {}, path => {
    if (!path) return;
    const o = options.find(x => x.path === path) || {};
    write([{ path: 'appearance.' + list, value: arr.concat([{ path, tint: o.tint || '#FFFFFF' }]) }]);
    renderFields();
  })));
}

/* ---------- Role ---------- */

const DAYS = [['Monday', T('Mo')], ['Tuesday', T('Tu')], ['Wednesday', T('We')], ['Thursday', T('Th')], ['Friday', T('Fr')], ['Saturday', T('Sa')], ['Sunday', T('Su')]];
const STANDARDS = [['VeryLow', T('Very low')], ['Low', T('Low')], ['Moderate', T('Moderate')], ['High', T('High')], ['VeryHigh', T('Very high')]];
const DRUGS = [['marijuana', T('Weed')], ['methamphetamine', T('Meth')], ['cocaine', T('Cocaine')], ['mdma', T('MDMA')], ['shrooms', T('Shrooms')], ['heroin', T('Heroin')]];
const CONVERSATIONS = [['none', T('Plain')], ['customer', T('Customer')], ['dealer', T('Dealer')]];

// Customer profiles: one click sets spending, orders per week and standards together.
const PROFILES = [
  ['casual', T('Casual'), { spending: { min: 100, max: 300 }, ordersPerWeek: { min: 1, max: 2 }, standards: 'Low' }],
  ['regular', T('Regular'), { spending: { min: 200, max: 500 }, ordersPerWeek: { min: 2, max: 4 }, standards: 'Moderate' }],
  ['big', T('Big spender'), { spending: { min: 800, max: 2000 }, ordersPerWeek: { min: 3, max: 5 }, standards: 'High' }],
  ['picky', T('Picky'), { spending: { min: 400, max: 900 }, ordersPerWeek: { min: 1, max: 2 }, standards: 'VeryHigh' }],
];

// Phone texts per role with the placeholders the game fills in.
const CONTRACT_PH = '<PRODUCT> <PRICE> <QUALITY> <LOCATION> <WINDOW_START> <WINDOW_END>';
const PHONE_KEYS = {
  customer: [
    ['contract_request', T('Asks for a deal'), CONTRACT_PH], ['contract_accepted', T('Deal accepted'), CONTRACT_PH],
    ['contract_rejected', T('Deal rejected'), CONTRACT_PH], ['offer_expired', T('Offer expired'), CONTRACT_PH],
    ['counteroffer_accepted', T('Counteroffer accepted'), CONTRACT_PH], ['counteroffer_rejected', T('Counteroffer rejected'), CONTRACT_PH],
    ['awaiting_deal', T('Waiting at the meeting'), ''], ['late_deal', T('You are late'), ''],
    ['deal_completed', T('Deal done'), ''], ['customer_rejected_deal', T('Turns the deal down'), ''],
    ['offer_reject', T('Refuses an offer'), ''], ['post_deal_recommend', T('Recommends someone'), '<NAME>'],
    ['sample_sufficient', T('Likes the sample'), ''], ['sample_insufficient', T('Dislikes the sample'), ''],
  ],
  dealer: [
    ['dealer_rob_defended', T('Fought off a robbery'), ''], ['dealer_rob_partially_defended', T('Lost part in a robbery'), ''],
    ['dealer_rob_loss', T('Was robbed'), ''], ['inventory_depleted', T('Out of product'), ''],
  ],
  supplier: [
    ['deaddrop_requested', T('Order received'), '<TIME>'], ['deaddrop_ready', T('Order at the dead drop'), '<LOCATION>'],
    ['supplier_request_repayment', T('Asks for the debt'), '<DEBT>'], ['supplier_unlocked', T('Unlocked'), ''],
    ['supplier_meet_confirm', T('Confirms a meeting'), '<LOCATION>'],
  ],
  civilian: [],
};
const REACTIONS = [['annoyed', T('Annoyed')], ['hurt', T('Hurt')], ['panic_start', T('Panics')], ['noticed_pickpocket', T('Notices a pickpocket')]];

function buildRole(box, n) {
  const roles = targets().map(id => roleOf(npcById(id)));
  const role = roles.every(r => r === roles[0]) ? roles[0] : null;

  const pick = el('div', 'roles');
  for (const [r, label] of ROLES) {
    const c = el('div', 'rc' + (r === role ? ' on' : ''));
    c.appendChild(el('div', 'rcn', t(label)));
    c.appendChild(el('div', 'rcs', t(ROLE_SUB[r])));
    c.addEventListener('click', () => {
      if (r === role) return;
      const ps = call('npc.setRole', { ids: targets(), role: r });
      if (applyState(ps)) console.log('[personify] role of ' + targets().join(', ') + ' set to ' + r);
    });
    pick.appendChild(c);
  }
  box.appendChild(pick);
  if (!role) line(box, t('The selected NPCs have different roles.'), 'warn');

  if (st.expert) section(box, 'ids', T('Ids and extensions'), b => {
    if (!multi()) {
      textRow(b, T('Id'), 'id', t('Derived from the name'));
      textRow(b, T('Save id'), 'saveId', t('Same as the id'));
    }
    const pack = getPath(st.project, 'manifest.autoRegister');
    toggle(b, T('World NPC'), 'spawn.auto', pack !== false);
    jsonRow(b, T('Extensions'), 'extensions');
  });

  if (role === 'customer') section(box, 'customer', T('Customer'), b => customerFields(b), true);
  if (role === 'customer') section(box, 'taste', T('Taste'), b => tasteFields(b));
  if (role === 'dealer') section(box, 'dealer', T('Dealer'), b => dealerFields(b), true);
  if (role === 'supplier') section(box, 'supplier', T('Supplier'), b => supplierFields(b), true);

  section(box, 'character', T('Character'), b => {
    slider(b, T('Health'), 'behavior.maxHealth', { min: 1, max: 1000, def: 100, whole: true });
    slider(b, T('Aggression'), 'behavior.aggression', { min: 0, max: 1, def: 0, fmt: pct });
    slider(b, T('Size'), 'behavior.scale', { min: 0.25, max: 4, def: 1, xmin: 0.05, xmax: 10 });
    selectRow(b, T('Conversation'), 'behavior.conversation', CONVERSATIONS.map(([v, l]) => [v, t(l)]), 'none');
  }, role !== 'customer' && role !== 'dealer' && role !== 'supplier');

  section(box, 'inventory', T('Inventory'), b => {
    slider(b, T('Cash from'), 'inventory.cash.min', { min: 0, max: 5000, step: 5, def: 0, whole: true, pair: ['inventory.cash.max', 0], fmt: v => '$' + Math.round(v) });
    slider(b, T('Cash to'), 'inventory.cash.max', { min: 0, max: 5000, step: 5, def: 0, whole: true, pair: ['inventory.cash.min', 0], fmt: v => '$' + Math.round(v) });
    itemList(b, T('Items'), 'inventory.items', true);
    toggle(b, T('Empty each night'), 'inventory.clearEachNight', false);
  });

  section(box, 'lines', T('Greeting'), b => variantList(b, 'lines.greeting', '', false));
  if (role && PHONE_KEYS[role].length) section(box, 'phone', T('Phone texts'), b => {
    for (const [key, label, ph] of PHONE_KEYS[role]) {
      b.appendChild(el('div', 'subh', t(label)));
      if (ph) b.appendChild(el('div', 'ph', t('Placeholders: {list}', { list: ph })));
      variantList(b, 'lines.phone.' + key, '', true);
    }
  });
  section(box, 'reactions', T('Reactions'), b => {
    for (const [key, label] of REACTIONS) {
      b.appendChild(el('div', 'subh', t(label)));
      variantList(b, 'lines.reactions.' + key, '', false);
    }
  });
  if (st.expert) section(box, 'linesraw', T('All texts as JSON'), b => jsonRow(b, T('Texts'), 'lines'));
}

function profileOf(c) {
  if (!c) return null;
  for (const [id, , v] of PROFILES) {
    if (same(c.spending, v.spending) && same(c.ordersPerWeek, v.ordersPerWeek) && c.standards === v.standards) return id;
  }
  return null;
}

function customerFields(b) {
  const profs = targets().map(id => profileOf(npcById(id).customer));
  const cur = profs.every(p => p === profs[0]) ? profs[0] : null;
  const row = frow(b, T('Profile'), 'customer.spending');
  row.classList.add('wide');
  const chips = el('div', 'chips');
  for (const [id, label, v] of PROFILES) {
    const c = el('div', 'chip' + (cur === id ? ' on' : ''), t(label));
    c.addEventListener('click', () => {
      write([{ path: 'customer.spending', value: v.spending }, { path: 'customer.ordersPerWeek', value: v.ordersPerWeek }, { path: 'customer.standards', value: v.standards }]);
      renderFields();
    });
    chips.appendChild(c);
  }
  row.appendChild(chips);
  if (!cur) line(b, profs.some(p => p !== profs[0]) ? t('Mixed') : t('Own values'), 'mu');

  if (st.expert) {
    slider(b, T('Spends from'), 'customer.spending.min', { min: 0, max: 5000, step: 10, def: 200, whole: true, pair: ['customer.spending.max', 500], fmt: v => '$' + Math.round(v), after: renderFields });
    slider(b, T('Spends to'), 'customer.spending.max', { min: 0, max: 5000, step: 10, def: 500, whole: true, pair: ['customer.spending.min', 200], fmt: v => '$' + Math.round(v), after: renderFields });
    slider(b, T('Orders a week from'), 'customer.ordersPerWeek.min', { min: 0, max: 7, def: 1, whole: true, pair: ['customer.ordersPerWeek.max', 5], after: renderFields });
    slider(b, T('Orders a week to'), 'customer.ordersPerWeek.max', { min: 0, max: 7, def: 5, whole: true, pair: ['customer.ordersPerWeek.min', 1], after: renderFields });
  }
  selectRow(b, T('Standards'), 'customer.standards', STANDARDS.map(([v, l]) => [v, t(l)]), 'Moderate', { after: renderFields });
  segRow(b, T('Order day'), 'customer.preferredOrderDay', DAYS, undefined);
  slider(b, T('Order time'), 'customer.orderTime', {
    min: 0, max: 95, def: 48, whole: true,
    parse: v => { const m = minutesOf(v); return m === null ? 48 : Math.round(m / 15); },
    store: q => hhmm(q * 15), fmt: q => hhmm(q * 15), noType: true,
  });
  b.appendChild(el('div', 'subh', t('Samples')));
  line(b, t('Chance to take a sample, by the average relation to this customer\'s contacts.'), 'mu');
  slider(b, T('0 % at'), 'customer.mutualRelationRequirement.min', { min: 0, max: 5, step: 0.1, def: 3, pair: ['customer.mutualRelationRequirement.max', 5] });
  slider(b, T('100 % at'), 'customer.mutualRelationRequirement.max', { min: 0, max: 5, step: 0.1, def: 5, pair: ['customer.mutualRelationRequirement.min', 3] });
  toggle(b, T('First sample always works'), 'customer.guaranteeFirstSample', false);
  toggle(b, T('Can be approached directly'), 'customer.allowDirectApproach', true);
  slider(b, T('Calls the police'), 'customer.callPoliceChance', { min: 0, max: 1, def: 0.5, fmt: pct });
  slider(b, T('Addiction'), 'customer.dependence.base', { min: 0, max: 1, def: 0, fmt: pct, pair: ['customer.dependence.multiplier', 1] });
  slider(b, T('Addiction rate'), 'customer.dependence.multiplier', { min: 0, max: 2, def: 1, pair: ['customer.dependence.base', 0] });
}

function tasteFields(b) {
  line(b, t('-1 hates it, 1 loves it.'), 'mu');
  for (const [k, label] of DRUGS) slider(b, label, 'customer.affinities.' + k, { min: -1, max: 1, def: 0 });
  b.appendChild(el('div', 'subh', t('Favourite effects')));
  const f = readField('customer.preferredProperties', []);
  const ids = Array.isArray(f.value) ? f.value : [];
  if (f.mixed) line(b, t('Mixed'), 'mu');
  const names = {};
  for (const e of st.effects || []) names[e.id] = e.name;
  const chips = el('div', 'chips');
  chips.setAttribute('data-path', 'customer.preferredProperties');
  for (const id of ids) {
    const c = el('div', 'chip on', (names[id] || id) + '  x');
    c.addEventListener('click', () => {
      const next = ids.filter(x => x !== id);
      write([next.length ? { path: 'customer.preferredProperties', value: next } : { path: 'customer.preferredProperties', unset: true }]);
      renderFields();
    });
    chips.appendChild(c);
  }
  b.appendChild(chips);
  const free = (st.effects || []).filter(e => ids.indexOf(e.id) < 0);
  if (!(st.effects || []).length) line(b, t('No effects known yet.'), 'mu');
  else b.appendChild(btn(t('+ Add effect'), 'sm', () => pickDialog(t('Favourite effects'),
    free.map(e => ({ value: e.id, label: e.name, group: e.tier ? t('Tier {n}', { n: e.tier }) : '' })), {}, id => {
      if (!id) return;
      write([{ path: 'customer.preferredProperties', value: ids.concat([id]) }]);
      renderFields();
    })));
}

function buildingRow(b, label, path) {
  const list = (st.world && st.world.buildings) || [];
  if (!list.length) return textRow(b, label, path, t('Building name'));
  return selectRow(b, label, path, list.map(x => [x, x]), '', { none: true, noneLabel: t('No home') });
}

function dealerFields(b) {
  segRow(b, T('Works for'), 'dealer.type', [['player', T('You')], ['cartel', T('The cartel')]], 'player');
  slider(b, T('Cut'), 'dealer.cut', { min: 0, max: 1, def: 0.2, fmt: pct });
  slider(b, T('Signing fee'), 'dealer.signingFee', { min: 0, max: 10000, step: 50, def: 0, whole: true, fmt: v => '$' + Math.round(v) });
  buildingRow(b, T('Home'), 'dealer.home');
  toggle(b, T('Accepts lower quality'), 'dealer.allowInsufficientQuality', false);
  toggle(b, T('Accepts higher quality'), 'dealer.allowExcessQuality', true);
  if (st.expert) textRow(b, T('Deals counter'), 'dealer.completedDealsVariable', t('Variable name'));
}

function supplierFields(b) {
  itemList(b, T('Sells'), 'supplier.items', false);
  slider(b, T('Debt limit from'), 'supplier.minOrderLimit', { min: 0, max: 10000, step: 10, def: 50, whole: true, fmt: v => '$' + Math.round(v) });
  slider(b, T('Debt limit to'), 'supplier.maxOrderLimit', { min: 0, max: 10000, step: 10, def: 500, whole: true, fmt: v => '$' + Math.round(v) });
  textRow(b, T('Recommendation'), 'supplier.recommendMessage', t('What a customer says about them'), { multi: true });
  textRow(b, T('Locked hint'), 'supplier.unlockHint', t('Shown on the locked contact'));
  if (st.expert) textRow(b, T('Stash dead drop'), 'supplier.stashDeadDrop', t('Dead drop GUID'));
}

// A list of item ids from catalog.world.items. With quantities, an entry is { id, quantity } above 1.
function itemList(b, label, path, withQty) {
  const f = readField(path, []);
  const arr = Array.isArray(f.value) ? f.value : [];
  const items = (st.world && st.world.items) || [];
  const nameOf = id => { const it = items.find(x => x.id === id); return it ? it.name : id; };
  const head = frow(b, label, path);
  head.appendChild(el('div', 'sp'));
  if (f.mixed) mixedTag(head);
  const save = next => { write([next.length ? { path, value: next } : { path, unset: true }]); renderFields(); };
  arr.forEach((e, i) => {
    const id = typeof e === 'string' ? e : e && e.id;
    const qty = typeof e === 'object' && e && e.quantity ? e.quantity : 1;
    const r = el('div', 'lr');
    r.appendChild(el('div', 'lrn', nameOf(id)));
    if (withQty) {
      const setQ = q => { const next = clone(arr); next[i] = q > 1 ? { id, quantity: q } : id; save(next); };
      r.appendChild(btn('-', 'xs' + (qty <= 1 ? ' off' : ''), () => setQ(qty - 1)));
      r.appendChild(el('div', 'qty', String(qty)));
      r.appendChild(btn('+', 'xs', () => setQ(qty + 1)));
    }
    r.appendChild(btn(t('Remove'), 'xs', () => { const next = clone(arr); next.splice(i, 1); save(next); }));
    b.appendChild(r);
  });
  if (!items.length) {
    line(b, t('Load a save once to list the items.'), 'mu');
    const add = textInput('', t('Item id'), v => { if (v.trim()) save(arr.concat([v.trim()])); });
    b.appendChild(add);
    return;
  }
  b.appendChild(btn(t('+ Add item'), 'sm', () => pickDialog(t(label),
    items.map(it => ({ value: it.id, label: it.name, group: it.category, sub: st.expert ? it.id : '' })), {}, id => {
      if (id) save(arr.concat([id]));
    })));
}

// Text variants; the game picks one at random. A phone variant with several lines is sent as several messages.
function variantList(b, path, ph, chain) {
  const f = readField(path, []);
  const arr = Array.isArray(f.value) ? f.value : [];
  if (f.mixed) line(b, t('Mixed'), 'mu');
  const save = next => { write([next.length ? { path, value: next } : { path, unset: true }]); renderFields(); };
  const toText = v => (Array.isArray(v) ? v.join('\n') : String(v || ''));
  const fromText = s => {
    const lines = s.split('\n').map(x => x.trim()).filter(x => x);
    return chain && lines.length > 1 ? lines : lines.join(' ');
  };
  const wrap = el('div', 'vars');
  wrap.setAttribute('data-path', path);
  arr.forEach((v, i) => {
    const r = el('div', 'var');
    r.appendChild(textInput(toText(v), ph, s => {
      const next = clone(arr);
      if (!s.trim()) next.splice(i, 1); else next[i] = fromText(s);
      save(next);
    }, { multi: true }));
    r.appendChild(btn(t('Remove'), 'xs', () => { const next = clone(arr); next.splice(i, 1); save(next); }));
    wrap.appendChild(r);
  });
  if (ui.pending[path]) {
    const r = el('div', 'var');
    const inp = textInput('', chain ? t('One message per line') : t('New text'), s => {
      delete ui.pending[path];
      if (s.trim()) save(arr.concat([fromText(s)])); else renderFields();
    }, { multi: true });
    r.appendChild(inp);
    wrap.appendChild(r);
    inp.focus();
  }
  if (!arr.length && !ui.pending[path]) wrap.appendChild(el('div', 'note mu', t('Game text')));
  b.appendChild(wrap);
  if (!ui.pending[path]) b.appendChild(btn(t('+ Add text'), 'xs', () => { ui.pending[path] = true; renderFields(); }));
}

/* ---------- Routine ---------- */

const STEP_TYPES = [
  ['walkTo', T('Walk to')], ['stayInBuilding', T('Go inside')], ['sit', T('Sit down')],
  ['locationAction', T('Hang out')], ['locationDialogue', T('Wait to talk')], ['useVendingMachine', T('Use a vending machine')],
  ['useAtm', T('Use an ATM')], ['useSlotMachine', T('Play a slot machine')], ['driveToCarPark', T('Drive to')],
  ['dealSignal', T('Ready for deals')], ['handleDeal', T('Handle a deal')],
];
const HANGOUT = [['none', T('Stand around')], ['smokeBreak', T('Smoke')], ['drinking', T('Drink')], ['graffiti', T('Spray graffiti')], ['holdItem', T('Hold an item')]];
const SLOT_MODES = [['single', T('One spin')], ['spinCount', T('Number of spins')], ['untilTime', T('Until a time')], ['untilBroke', T('Until broke')], ['untilTimeOrBroke', T('Until a time or broke')]];
const POINT_TYPES = ['walkTo', 'useSlotMachine', 'locationDialogue', 'locationAction'];
const DURATION_TYPES = ['stayInBuilding', 'sit', 'locationAction'];
const REGIONS = ['Northtown', 'Westville', 'Downtown', 'Docks', 'Suburbia', 'Uptown'];

function minutesOf(v) {
  if (typeof v === 'number') { const h = Math.floor(v / 100), m = v % 100; return h < 24 && m < 60 ? h * 60 + m : null; }
  const s = String(v || '').trim();
  const mm = /^(\d{1,2}):(\d{2})$/.exec(s);
  if (mm) { const h = +mm[1], m = +mm[2]; return h < 24 && m < 60 ? h * 60 + m : null; }
  if (/^\d{3,4}$/.test(s)) return minutesOf(+s);
  return null;
}
const hhmm = m => { m = ((m % 1440) + 1440) % 1440; return String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0'); };
const fmtDur = m => (m < 60 ? t('{n} min', { n: m }) : m % 60 === 0 ? t('{h} h', { h: m / 60 }) : t('{h} h {n} min', { h: Math.floor(m / 60), n: m % 60 }));
const scheduleOf = () => { const n = npcById(st.active); return n && Array.isArray(n.schedule) ? n.schedule : []; };
const stepLabel = type => { const x = STEP_TYPES.find(s => s[0] === type); return x ? t(x[1]) : t('Custom step'); };

// The schedule is written whole and kept in time order; the selected step follows its own move.
function writeSchedule(steps, keepIndex) {
  const keep = keepIndex >= 0 ? steps[keepIndex] : null;
  const keyed = steps.map((s, i) => ({ s, i, m: minutesOf(s.time) }));
  keyed.sort((a, b) => (a.m === null ? 1e9 : a.m) - (b.m === null ? 1e9 : b.m) || a.i - b.i);
  const sorted = keyed.map(k => k.s);
  if (keep) ui.step = sorted.indexOf(keep);
  write([sorted.length ? { path: 'schedule', value: sorted } : { path: 'schedule', unset: true }]);
  renderFields();
  drawMapOverlay();
}

function buildRoutine(box, n) {
  if (multi()) { lockLine(box, t('The routine is edited one NPC at a time.')); return; }
  if (!worldHas()) line(box, t('Load a save once to get the map and places.'), 'warn');
  const steps = scheduleOf();

  section(box, 'presence', T('Presence'), b => {
    toggle(b, T('Walks the world'), 'spawn.physical', steps.length > 0);
    toggle(b, T('Contact visible'), 'contact.visible', true);
    toggle(b, T('Map marker'), 'contact.mapMarker', true);
  }, true);

  section(box, 'spawn', T('Start point'), b => {
    const sp = n.spawn || {};
    const row = frow(b, T('Position'), 'spawn');
    row.appendChild(el('div', 'fv grow', typeof sp.x === 'number' ? fmtNum(sp.x) + ', ' + fmtNum(sp.y) + ', ' + fmtNum(sp.z) : t('Not set')));
    const picking = ui.pick && ui.pick.what === 'spawn';
    row.appendChild(btn(picking ? t('Click the map') : t('Pick on map'), 'xs' + (picking ? ' now' : '') + (st.map && st.map.hasMap ? '' : ' off'), () => startPick({ what: 'spawn' })));
    const regions = (st.world && st.world.regions && st.world.regions.length) ? st.world.regions : REGIONS;
    selectRow(b, T('Region'), 'spawn.region', regions.map(r => [r, r]), '', { none: true, noneLabel: t('Not set') });
    slider(b, T('Facing'), 'spawn.rotationY', { min: 0, max: 359, def: 0, whole: true, fmt: v => Math.round(v) + ' deg' });
  }, true);

  section(box, 'schedule', T('Daily schedule'), b => scheduleFields(b, steps), true);
}

function startPick(pick) {
  if (!st.map || !st.map.hasMap) { toast(t('Load a save once to get the map.'), 'warn'); return; }
  ui.pick = pick;
  hoverKey = '';
  drawMapOverlay();
  renderFields();
}

function scheduleFields(b, steps) {
  // 24 hour bar: each step lasts until the next one starts.
  const bar = el('div', 'day');
  bar.setAttribute('data-path', 'schedule');
  const times = steps.map(s => minutesOf(s.time));
  steps.forEach((s, i) => {
    const start = times[i];
    if (start === null) return;
    let end = 1440;
    for (let j = i + 1; j < steps.length; j++) if (times[j] !== null) { end = times[j]; break; }
    const blk = el('div', 'blk ' + s.type + (i === ui.step ? ' on' : ''));
    blk.style.left = (start / 1440 * 100) + '%';
    blk.style.width = Math.max(0.6, (end - start) / 1440 * 100) + '%';
    blk.addEventListener('click', e => { if (e.stopPropagation) e.stopPropagation(); ui.step = i; renderFields(); drawMapOverlay(); });
    tip(blk, hhmm(start) + ' ' + stepLabel(s.type));
    bar.appendChild(blk);
  });
  b.appendChild(bar);
  const ticks = el('div', 'ticks');
  for (const h of ['00:00', '06:00', '12:00', '18:00', '24:00']) ticks.appendChild(el('div', 'tick', h));
  b.appendChild(ticks);

  if (!steps.length) line(b, t('No steps yet. The NPC stays at its start point.'), 'mu');
  steps.forEach((s, i) => b.appendChild(stepCard(s, i, steps)));

  let addType = 'walkTo';
  const add = el('div', 'addstep');
  add.appendChild(selectEl(STEP_TYPES.map(([v, l]) => [v, t(l)]), addType, v => { addType = v; }));
  add.appendChild(btn(t('+ Add step'), 'sm go', () => {
    const last = times.filter(x => x !== null);
    const at = last.length ? Math.min(23 * 60 + 45, Math.max(...last) + 60) : 8 * 60;
    const s = { type: addType, time: hhmm(at) };
    if (DURATION_TYPES.indexOf(addType) >= 0) s.duration = 60;
    const next = clone(steps).concat([s]);
    writeSchedule(next, next.length - 1);
    console.log('[personify] step ' + addType + ' added at ' + s.time + ' for ' + st.active);
  }));
  b.appendChild(add);
}

function stepCard(s, i, steps) {
  const open = i === ui.step;
  const card = el('div', 'step' + (open ? ' on' : ''));
  card.setAttribute('data-path', 'schedule');
  const head = el('div', 'stph');
  head.appendChild(el('div', 'stpn', String(i + 1)));
  head.appendChild(el('div', 'stpt', (minutesOf(s.time) === null ? '--:--' : hhmm(minutesOf(s.time))) + '  ' + stepLabel(s.type)));
  head.appendChild(el('div', 'sp'));
  head.appendChild(el('div', 'stpd', describeStep(s)));
  head.addEventListener('click', () => { ui.step = open ? -1 : i; renderFields(); drawMapOverlay(); });
  card.appendChild(head);
  if (!open) return card;

  const body = el('div', 'stpb');
  const edit = (fn, keep) => { const next = clone(steps); fn(next[i]); writeSchedule(next, keep === false ? -1 : i); };
  const field = (label) => { const r = el('div', 'fr'); r.appendChild(el('div', 'fl', t(label))); body.appendChild(r); return r; };

  const known = STEP_TYPES.some(x => x[0] === s.type);
  const tr = field(T('Type'));
  tr.appendChild(selectEl(STEP_TYPES.map(([v, l]) => [v, t(l)]), s.type, v => edit(x => {
    // Fields only the old type understood are dropped; time and a map point survive.
    const keepPos = POINT_TYPES.indexOf(v) >= 0 ? x.position : undefined;
    for (const k of Object.keys(x)) if (k !== 'time') delete x[k];
    x.type = v;
    if (keepPos) x.position = keepPos;
    if (DURATION_TYPES.indexOf(v) >= 0) x.duration = 60;
  })));
  if (!known) body.appendChild(el('div', 'note warn', t('This step type is kept as written.')));

  const m = minutesOf(s.time);
  const timeRow = field(T('Time'));
  const rng = el('input', 'rng');
  rng.setAttribute('type', 'range'); rng.setAttribute('min', '0'); rng.setAttribute('max', '95'); rng.setAttribute('step', '1');
  rng.value = String(m === null ? 32 : Math.round(m / 15));
  const tv = el('div', 'fv', m === null ? '--:--' : hhmm(m));
  rng.addEventListener('input', e => { tv.textContent = hhmm(Number(valueOf(e)) * 15); });
  rng.addEventListener('change', e => edit(x => { x.time = hhmm(Number(valueOf(e)) * 15); }));
  timeRow.appendChild(rng);
  if (st.expert) timeRow.appendChild(textInput(s.time || '', 'HH:MM', v => {
    if (minutesOf(v) === null) { toast(t('Enter a time like 07:30.'), 'bad'); return; }
    edit(x => { x.time = hhmm(minutesOf(v)); });
  }, { cls: 'num' }));
  else timeRow.appendChild(tv);

  if (DURATION_TYPES.indexOf(s.type) >= 0) {
    const d = typeof s.duration === 'number' ? s.duration : 60;
    const dr = field(T('Duration'));
    const r2 = el('input', 'rng');
    r2.setAttribute('type', 'range'); r2.setAttribute('min', '5'); r2.setAttribute('max', '720'); r2.setAttribute('step', '5');
    r2.value = String(d);
    const dv = el('div', 'fv', fmtDur(d));
    r2.addEventListener('input', e => { dv.textContent = fmtDur(Number(valueOf(e))); });
    r2.addEventListener('change', e => edit(x => { x.duration = Number(valueOf(e)); }));
    dr.appendChild(r2);
    dr.appendChild(dv);
  }

  const pickBtn = (what, label) => {
    const picking = ui.pick && ui.pick.what === what && ui.pick.index === i;
    return btn(picking ? t('Click the map') : t(label), 'xs' + (picking ? ' now' : '') + (st.map && st.map.hasMap ? '' : ' off'), () => startPick({ what, index: i }));
  };

  if (POINT_TYPES.indexOf(s.type) >= 0) {
    const pr = field(T('Place'));
    const p = s.position;
    pr.appendChild(el('div', 'fv grow', Array.isArray(p) && p.length >= 3 ? fmtNum(p[0]) + ', ' + fmtNum(p[1]) + ', ' + fmtNum(p[2]) : t('Not set')));
    pr.appendChild(pickBtn('step', T('Pick on map')));
  }

  if (s.type === 'walkTo') {
    stepToggle(body, T('Face the destination'), s, 'faceDestination', false, edit);
    stepToggle(body, T('Jump there when skipped'), s, 'warpIfSkipped', false, edit);
    if (st.expert) stepNumber(body, T('Close enough (m)'), s, 'within', edit);
  }
  if (s.type === 'stayInBuilding') {
    const r = field(T('Building'));
    const list = (st.world && st.world.buildings) || [];
    if (list.length) r.appendChild(selectEl(list.map(x => [x, x]), s.building || '', v => edit(x => { if (v) x.building = v; else delete x.building; }), { none: true, noneLabel: t('Pick a building') }));
    else r.appendChild(textInput(s.building || '', t('Building name'), v => edit(x => { if (v.trim()) x.building = v.trim(); else delete x.building; })));
    r.appendChild(pickBtn('step', T('Pick on map')));
    if (st.expert) stepNumber(body, T('Door'), s, 'doorIndex', edit, true);
  }
  if (s.type === 'sit') {
    const r = field(T('Seat'));
    const seats = st.map ? st.map.places.filter(p => p.kind === 'seat') : [];
    // world.map names seat sets by their scene path, which is unique; seatSet (a plain name) stays an expert field.
    if (seats.length) r.appendChild(selectEl(seats.map(p => [p.name, p.label || p.name]), s.seatSetPath || '', v => edit(x => { if (v) { x.seatSetPath = v; delete x.seatSet; } else delete x.seatSetPath; }), { none: true, noneLabel: t('Pick a seat') }));
    else r.appendChild(textInput(s.seatSetPath || '', t('Seat path'), v => edit(x => { if (v.trim()) x.seatSetPath = v.trim(); else delete x.seatSetPath; })));
    r.appendChild(pickBtn('step', T('Pick on map')));
    stepToggle(body, T('Jump there when skipped'), s, 'warpIfSkipped', false, edit);
    if (st.expert) stepText(body, T('Seat set name'), s, 'seatSet', edit);
  }
  if (s.type === 'locationAction') {
    const r = field(T('Does'));
    r.appendChild(selectEl(HANGOUT.map(([v, l]) => [v, t(l)]), s.action || 'none', v => edit(x => { if (v === 'none') delete x.action; else x.action = v; })));
    if (s.action === 'graffiti') stepText(body, T('Graffiti region'), s, 'graffitiRegion', edit);
    if (s.action === 'holdItem' || st.expert) stepText(body, T('Held item path'), s, 'equippablePath', edit);
  }
  if (s.type === 'locationDialogue' && st.expert) {
    stepNumber(body, T('Greeting'), s, 'greetingOverride', edit, true);
    stepNumber(body, T('Choice'), s, 'choice', edit, true);
  }
  if (s.type === 'useSlotMachine') {
    stepNumber(body, T('Bet'), s, 'bet', edit, true);
    const r = field(T('Plays'));
    r.appendChild(selectEl(SLOT_MODES.map(([v, l]) => [v, t(l)]), s.mode || 'single', v => edit(x => { x.mode = v; })));
    if (s.mode === 'spinCount') stepNumber(body, T('Spins'), s, 'spins', edit, true);
    if (s.mode === 'untilTime' || s.mode === 'untilTimeOrBroke') stepText(body, T('Until'), s, 'endTime', edit);
    if (st.expert) stepNumber(body, T('Seconds between spins'), s, 'timeBetweenSpins', edit);
  }
  if (s.type === 'useVendingMachine' && st.expert) stepText(body, T('Machine GUID'), s, 'machineGuid', edit);
  if (s.type === 'useAtm' && st.expert) stepText(body, T('ATM GUID'), s, 'atmGuid', edit);
  if (s.type === 'driveToCarPark') {
    const r = field(T('Car park'));
    const lots = st.map ? st.map.places.filter(p => p.kind === 'carpark') : [];
    if (lots.length) r.appendChild(selectEl(lots.map(p => [p.name, p.label || p.name]), s.parkingLot || '', v => edit(x => { if (v) x.parkingLot = v; else delete x.parkingLot; }), { none: true, noneLabel: t('Pick a car park') }));
    else r.appendChild(textInput(s.parkingLot || '', t('Car park name'), v => edit(x => { if (v.trim()) x.parkingLot = v.trim(); else delete x.parkingLot; })));
    r.appendChild(pickBtn('step', T('Pick on map')));
    const vr = field(T('Car'));
    const vehicles = (st.map && st.map.vehicles) || [];
    const code = s.createVehicle && s.createVehicle.code;
    vr.appendChild(selectEl(vehicles.map(v => [v.code, v.label || v.code]), code || '', v => edit(x => {
      if (!v) delete x.createVehicle;
      else { x.createVehicle = Object.assign({}, x.createVehicle || {}, { code: v }); delete x.vehicle; }
    }), { none: true, noneLabel: t('An existing car') }));
    if (code) {
      const cp = field(T('Car appears at'));
      const p = s.createVehicle.position;
      cp.appendChild(el('div', 'fv grow', Array.isArray(p) ? fmtNum(p[0]) + ', ' + fmtNum(p[2]) : t('Next to the NPC')));
      cp.appendChild(pickBtn('car', T('Pick on map')));
    } else stepText(body, T('Car name'), s, 'vehicle', edit);
  }

  const acts = el('div', 'acts');
  acts.appendChild(el('div', 'sp'));
  acts.appendChild(btn(t('Remove step'), 'xs', () => {
    const next = clone(steps);
    next.splice(i, 1);
    ui.step = -1;
    writeSchedule(next, -1);
  }));
  body.appendChild(acts);
  card.appendChild(body);
  return card;
}

function describeStep(s) {
  switch (s.type) {
    case 'stayInBuilding': return s.building || t('No building');
    case 'sit': {
      const p = placeByName('seat', s.seatSetPath);
      return p ? (p.label || p.name) : s.seatSet || s.seatSetPath || t('No seat');
    }
    case 'driveToCarPark': return s.parkingLot || t('No car park');
    case 'locationAction': return t((HANGOUT.find(h => h[0] === (s.action || 'none')) || HANGOUT[0])[1]);
    default:
      if (POINT_TYPES.indexOf(s.type) >= 0) return Array.isArray(s.position) ? '' : t('No place');
      return '';
  }
}

function stepToggle(body, label, s, key, def, edit) {
  const r = el('div', 'fr');
  r.appendChild(el('div', 'fl', t(label)));
  r.appendChild(el('div', 'sp'));
  r.appendChild(sw(s[key] === undefined ? def : !!s[key], on => edit(x => { x[key] = on; })));
  body.appendChild(r);
}
function stepText(body, label, s, key, edit) {
  const r = el('div', 'fr');
  r.appendChild(el('div', 'fl', t(label)));
  r.appendChild(textInput(s[key] === undefined ? '' : String(s[key]), '', v => edit(x => { if (v.trim()) x[key] = v.trim(); else delete x[key]; })));
  body.appendChild(r);
}
function stepNumber(body, label, s, key, edit, whole) {
  const r = el('div', 'fr');
  r.appendChild(el('div', 'fl', t(label)));
  r.appendChild(textInput(s[key] === undefined ? '' : String(s[key]), '', v => {
    if (!v.trim()) { edit(x => { delete x[key]; }); return; }
    const x = Number(v.replace(',', '.'));
    if (!isFinite(x)) { toast(t('Enter a number.'), 'bad'); return; }
    edit(y => { y[key] = whole ? Math.round(x) : x; });
  }, { cls: 'num' }));
  body.appendChild(r);
}

/* ---------- Relations ---------- */

function buildRelations(box, n) {
  section(box, 'rel', T('Relationship'), b => {
    slider(b, T('Start relation'), 'relationships.delta', { min: 0, max: 5, step: 0.1, def: 0 });
    toggle(b, T('Unlocked from the start'), 'relationships.unlocked', false);
    segRow(b, T('Unlocked by'), 'relationships.unlockType', [['Recommendation', T('Recommendation')], ['DirectApproach', T('Direct approach')]], 'Recommendation');
  }, true);
  section(box, 'conn', T('Connections'), b => connectionFields(b, n), true);
}

// Everyone this NPC can be connected to: the game's NPCs (vanilla and other mods) and this project's NPCs.
function connectionChoices() {
  const out = [];
  const own = new Set(npcs().map(x => x.id));
  for (const x of npcs()) out.push({ value: x.id, label: x.name || x.id, group: t('This project'), sub: roleLabel(roleOf(x)) });
  const world = (st.world && st.world.npcs) || [];
  const rest = world.filter(x => !own.has(x.id)).slice().sort((a, b) => {
    const sa = a.source === 'vanilla' ? '' : String(a.source || '');
    const sb = b.source === 'vanilla' ? '' : String(b.source || '');
    return sa.localeCompare(sb) || String(a.name).localeCompare(String(b.name));
  });
  for (const x of rest) {
    out.push({ value: x.id, label: x.name || x.id, group: x.source === 'vanilla' || !x.source ? t('Schedule I') : x.source, sub: x.region || '' });
  }
  return out;
}

function connectionFields(b, n) {
  const hasNpcs = !!(st.world && st.world.hasNpcs);
  if (!hasNpcs) line(b, t('Load a save once to list the game\'s NPCs.'), 'warn');
  const f = readField('relationships.connections', []);
  const arr = Array.isArray(f.value) ? f.value : [];
  if (f.mixed) line(b, t('Mixed'), 'mu');
  const choices = connectionChoices();
  const byId = {};
  for (const c of choices) byId[c.value] = c;
  const save = next => { write([next.length ? { path: 'relationships.connections', value: next } : { path: 'relationships.connections', unset: true }]); renderFields(); };
  const wrap = el('div', 'conns');
  wrap.setAttribute('data-path', 'relationships.connections');
  if (!arr.length) wrap.appendChild(el('div', 'note mu', t('No connections.')));
  arr.forEach((id, i) => {
    const c = byId[id];
    const r = el('div', 'lr');
    const tx = el('div', 'lrt');
    tx.appendChild(el('div', 'lrn' + (c ? '' : ' mu'), c ? c.label : id));
    tx.appendChild(el('div', 'lrs', c ? c.group : t('Unknown id')));
    r.appendChild(tx);
    r.appendChild(btn(t('Remove'), 'xs', () => { const next = arr.slice(); next.splice(i, 1); save(next); }));
    wrap.appendChild(r);
  });
  b.appendChild(wrap);
  const self = new Set(targets());
  const free = choices.filter(c => arr.indexOf(c.value) < 0 && !self.has(c.value));
  const typed = textInput('', t('Or type an NPC id'), v => {
    const id = v.trim().toLowerCase();
    if (!id) return;
    if (arr.indexOf(id) >= 0) { toast(t('Already connected.'), 'info'); return; }
    save(arr.concat([id]));
  }, { cls: 'grow' });
  b.appendChild(btn(t('+ Add connection'), 'sm', () => pickDialog(t('Connections'), free, {}, id => { if (id) save(arr.concat([id])); })));
  if (st.expert || !hasNpcs) b.appendChild(typed);
}

/* ---------- new NPC wizard ---------- */

function wizard() {
  const w = { step: 0, name: '', role: 'customer', look: 'random', copy: st.active || '' };
  const steps = [T('Name'), T('Role'), T('Look')];
  draw();
  function draw() {
    const box = openDialog('mid');
    dialogHead(box, t('New NPC'));
    const prog = el('div', 'wprog');
    steps.forEach((s, i) => prog.appendChild(el('div', 'wp' + (i === w.step ? ' on' : i < w.step ? ' done' : ''), t(s))));
    box.appendChild(prog);
    const err = el('div', 'ferr');
    err.style.display = 'none';
    if (w.step === 0) {
      const f = el('div', 'df');
      f.appendChild(el('div', 'dl', t('Name')));
      const inp = textInput(w.name, t('First and last name'), v => { w.name = v; }, { onInput: v => { w.name = v; } });
      inp.addEventListener('keydown', e => { if (e.key === 'Enter') next(); });
      f.appendChild(inp);
      box.appendChild(f);
      inp.focus();
    } else if (w.step === 1) {
      const roles = el('div', 'roles');
      for (const [r, label] of ROLES) {
        const c = el('div', 'rc' + (w.role === r ? ' on' : ''));
        c.appendChild(el('div', 'rcn', t(label)));
        c.appendChild(el('div', 'rcs', t(ROLE_SUB[r])));
        c.addEventListener('click', () => { w.role = r; draw(); });
        roles.appendChild(c);
      }
      box.appendChild(roles);
    } else {
      const looks = el('div', 'roles');
      const opt = (id, label, sub) => {
        const c = el('div', 'rc' + (w.look === id ? ' on' : ''));
        c.appendChild(el('div', 'rcn', label));
        c.appendChild(el('div', 'rcs', sub));
        c.addEventListener('click', () => { w.look = id; draw(); });
        looks.appendChild(c);
      };
      opt('random', t('Random'), t('A random look from the game\'s parts.'));
      // Templates come from catalog.look; `menu` is the character standing in the main menu.
      const templates = (st.look && st.look.templates) || [];
      for (const tp of templates) {
        opt('template:' + tp.id, tp.id === 'menu' ? t('Menu character') : tp.name,
          tp.id === 'menu' ? t('The character standing in the main menu.') : t('A ready-made look.'));
      }
      if (npcs().length) opt('copy', t('Copy of an NPC'), t('Same look as an NPC of this project.'));
      box.appendChild(looks);
      if (w.look === 'copy' && npcs().length) {
        const f = el('div', 'df');
        f.appendChild(el('div', 'dl', t('Copy from')));
        f.appendChild(selectEl(npcs().map(x => [x.id, x.name || x.id]), w.copy || npcs()[0].id, v => { w.copy = v; }));
        box.appendChild(f);
      }
    }
    box.appendChild(err);
    const acts = el('div', 'dacts');
    if (w.step > 0) acts.appendChild(btn(t('Back'), null, () => { w.step--; draw(); }));
    acts.appendChild(el('div', 'sp'));
    acts.appendChild(btn(t('Cancel'), null, closeDialog));
    acts.appendChild(btn(w.step < 2 ? t('Next') : t('Create NPC'), 'go', next));
    box.appendChild(acts);
    function next() {
      if (w.step === 0 && !w.name.trim()) { err.textContent = t('Enter a name.'); err.style.display = 'flex'; return; }
      if (w.step < 2) { w.step++; draw(); return; }
      const look = w.look === 'copy' ? 'copy:' + (w.copy || npcs()[0].id) : w.look;
      const ps = call('npc.add', { name: w.name.trim(), role: w.role, look });
      if (!ps || ps.ok === false) { err.textContent = errText(ps && ps.error); err.style.display = 'flex'; return; }
      closeDialog();
      ui.search = '';
      ui.roleFilter = 'all';
      st.area = 'look';
      applyState(ps);
      if (npcVl) npcVl.reveal(filteredNpcs().findIndex(x => x.id === st.active));
      console.log('[personify] NPC ' + ps.added + ' added as ' + w.role + ' with look ' + look);
    }
  }
}

/* ---------- keys, back, events, ticker ---------- */

// Right-click / Escape: close the innermost thing, then step back to the start screen.
function back() {
  if (closeMenu()) return true;
  if (closeDialog()) return true;
  if (ui.problemsOpen) { ui.problemsOpen = false; renderFoot(); return true; }
  if (ui.pick) { ui.pick = null; drawMapOverlay(); renderFields(); return true; }
  if (ui.screen === 'editor') { goStart(); return true; }
  return false;
}

document.addEventListener('back', e => { if (back() && e.preventDefault) e.preventDefault(); });

// Ctrl+Z / Ctrl+Y reach the page as keydown from the keyboard home field (#keys, data-keys) or, in a browser, from
// anywhere outside a text field.
document.addEventListener('keydown', e => {
  if (!e.ctrlKey || ui.screen !== 'editor') return;
  const tag = e.target && e.target.tagName;
  if ((tag === 'INPUT' || tag === 'TEXTAREA') && e.target.id !== 'keys') return;
  const k = String(e.key || '').toLowerCase();
  if (k === 'z' && !e.shiftKey) { undo(); if (e.preventDefault) e.preventDefault(); }
  else if (k === 'y' || (k === 'z' && e.shiftKey)) { redo(); if (e.preventDefault) e.preventDefault(); }
});

(function wireKeyHome() {
  const k = document.getElementById('keys');
  if (!k) { console.warn('[personify] keyboard home field #keys missing, Ctrl+Z needs the host'); return; }
  // Whatever lands in the field is not meant as text; it only exists to receive the declared keys.
  k.addEventListener('input', e => { if (valueOf(e)) k.value = ''; });
})();

host.on('project.changed', p => {
  const ps = parsePayload(p, 'project.changed');
  if (!ps || ui.screen !== 'editor') return;
  console.log('[personify] project changed outside the editor');
  applyState(ps);
});
host.on('toast', p => {
  const x = parsePayload(p, 'toast');
  if (!x) return;
  toast(x.text, x.severity === 'error' ? 'bad' : x.severity === 'warning' ? 'warn' : x.severity === 'success' ? 'ok' : 'info');
});
host.on('editor.back', () => back());
host.on('editor.key', p => {
  const x = parsePayload(p, 'editor.key');
  if (!x || ui.screen !== 'editor') return;
  if (x.key === 'undo') undo(); else if (x.key === 'redo') redo();
});

// The stage rect and the list heights are only known after a render; a light poll catches every size change.
setInterval(() => {
  reportStage();
  for (const v of lists) {
    if (!document.body.contains(v.box)) { lists.delete(v); continue; }
    v.check();
  }
}, 250);

boot();
