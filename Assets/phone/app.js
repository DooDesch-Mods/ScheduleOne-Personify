// Personify phone app: project list -> NPC list -> NPC actions, as push navigation in both orientations.
// Every call answers JSON from App/PhoneApp.cs. An action answer carries { ok, msg, npc } and the NPC screen is
// drawn again from that npc block, so the page never keeps its own copy of the project.

const $ = (id) => document.getElementById(id);

let strings = {};

// English source text in, current language out. {0}, {1} ... are filled from the arguments.
function t(en, ...args) {
  let s = strings[en] ?? en;
  args.forEach((a, i) => { s = s.split('{' + i + '}').join(String(a)); });
  return s;
}

function call(name, arg = '') {
  const raw = s1.call(name, arg);
  if (!raw) {
    console.error(name + ' answered nothing');
    return { ok: false, msg: t('Something went wrong. See the log.') };
  }
  try { return JSON.parse(raw); }
  catch (e) {
    console.error(name + ' answered something that is not JSON: ' + e.message);
    return { ok: false, msg: t('Something went wrong. See the log.') };
  }
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text !== undefined) n.textContent = text;
  return n;
}

function icon(name) {
  const i = el('img', 'ic');
  i.setAttribute('src', name + '.png');
  return i;
}

// ---- state -------------------------------------------------------------------------------------------------

const nav = { view: 'projects', folder: null, projectName: '', npcId: null };
let projects = [];
let npcs = [];
let npc = null;
let status = { text: '', kind: '' };

function setStatus(answer) {
  if (!answer || !answer.msg) return;
  status = { text: answer.msg, kind: answer.ok ? 'ok' : 'bad' };
}

function key() { return nav.folder + '\n' + nav.npcId; }

// ---- navigation --------------------------------------------------------------------------------------------

function showProjects() {
  const a = call('phone.projects');
  if (!a.ok) setStatus(a);
  projects = a.projects ?? [];
  nav.view = 'projects';
  nav.folder = null;
  nav.npcId = null;
  render();
}

// quiet: the start-up attempt to reopen the last project falls back to the list without a message.
function showNpcs(folder, quiet = false) {
  const a = call('phone.npcs', folder);
  if (!a.ok) {
    if (quiet) console.warn('last project ' + folder + ' did not open: ' + a.msg);
    else setStatus(a);
    showProjects();
    return false;
  }
  nav.view = 'npcs';
  nav.folder = a.folder;
  nav.projectName = a.name;
  nav.npcId = null;
  npcs = a.npcs ?? [];
  render();
  return true;
}

function showNpc(id) {
  nav.npcId = id;
  const a = call('phone.npc', key());
  if (!a.ok || !a.npc) { setStatus(a); nav.npcId = null; render(); return; }
  npc = a.npc;
  nav.view = 'npc';
  render();
}

function goBack() {
  status = { text: '', kind: '' };
  if (nav.view === 'npc') showNpcs(nav.folder);
  else showProjects();
}

function act(name) {
  const a = call(name, key());
  setStatus(a);
  if (a.npc) npc = a.npc;
  render();
}

document.addEventListener('back', (e) => {
  if (nav.view === 'projects') return;
  e.preventDefault();
  goBack();
});

$('back').addEventListener('click', goBack);

// ---- drawing -----------------------------------------------------------------------------------------------

function render() {
  $('back').style.display = nav.view === 'projects' ? 'none' : 'flex';
  const body = $('body');
  body.replaceChildren();

  if (nav.view === 'projects') {
    $('title').textContent = 'Personify';
    $('sub').textContent = t('Projects');
    body.appendChild(projectList());
  } else if (nav.view === 'npcs') {
    $('title').textContent = nav.projectName;
    $('sub').textContent = t('NPCs');
    body.appendChild(npcList());
  } else {
    $('title').textContent = npc.name;
    $('sub').textContent = npc.project;
    body.appendChild(npcScreen());
  }

  const s = $('status');
  s.textContent = status.text;
  s.className = 'status' + (status.kind ? ' ' + status.kind : '');
}

function row(name, meta, onClick) {
  const r = el('div', 'row');
  const text = el('div', 'row-text');
  text.appendChild(el('div', 'row-name', name));
  if (meta) text.appendChild(el('div', 'row-meta', meta));
  r.appendChild(text);
  r.appendChild(icon('i-next'));
  r.addEventListener('click', onClick);
  return r;
}

function projectList() {
  const box = el('div', 'scroll');
  if (projects.length === 0) {
    box.appendChild(el('div', 'empty', t('No projects. The editor in the main menu creates them.')));
    return box;
  }
  for (const p of projects) {
    const meta = p.npcs === 1 ? t('1 NPC') : t('{0} NPCs', p.npcs);
    box.appendChild(row(p.name, meta, () => { status = { text: '', kind: '' }; showNpcs(p.folder); }));
  }
  return box;
}

function npcList() {
  const box = el('div', 'scroll');
  if (npcs.length === 0) {
    box.appendChild(el('div', 'empty', t('This project has no NPCs.')));
    return box;
  }
  for (const n of npcs) {
    const steps = n.steps === 1 ? t('1 step') : t('{0} steps', n.steps);
    const meta = n.spawn ? steps : steps + ', ' + t('no start point');
    box.appendChild(row(n.name, meta, () => { status = { text: '', kind: '' }; showNpc(n.id); }));
  }
  return box;
}

function section(label) {
  const s = el('div', 'section');
  s.appendChild(el('div', 'label', label));
  return s;
}

function npcScreen() {
  const screen = el('div', 'npc');
  const info = el('div', 'scroll');

  const live = el('div', 'live' + (npc.live ? ' on' : ''), npc.live ? t('In this world') : t('Not in this world'));
  info.appendChild(live);

  const start = section(t('Start point'));
  if (npc.spawn) {
    const where = npc.spawn.region ? npc.spawn.region + ' (' + npc.spawn.at + ')' : npc.spawn.at;
    start.appendChild(el('div', 'value', where));
  } else {
    start.appendChild(el('div', 'value unset', t('Not set')));
  }
  info.appendChild(start);

  const route = section(t('Route'));
  if (!npc.steps || npc.steps.length === 0) {
    route.appendChild(el('div', 'value unset', t('No steps')));
  } else {
    for (const s of npc.steps) {
      const r = el('div', 'step');
      r.appendChild(el('div', 'step-time', s.time));
      r.appendChild(el('div', 'step-text', s.text));
      route.appendChild(r);
    }
  }
  info.appendChild(route);
  screen.appendChild(info);

  const actions = el('div', 'actions');
  actions.appendChild(button(t('Set start point here'), 'i-pin', () => act('phone.spawn')));
  actions.appendChild(button(t('Add route step here'), 'i-route', () => act('phone.step')));
  actions.appendChild(button(t('Undo last step'), 'i-undo', () => act('phone.undo'), { disabled: !npc.canUndo }));
  actions.appendChild(button(t('Jump to NPC'), 'i-jump', () => act('phone.jump'), { disabled: !npc.live }));
  actions.appendChild(button(t('Apply look now'), 'i-look', () => act('phone.look'), { wide: true, primary: true }));
  screen.appendChild(actions);
  return screen;
}

function button(label, img, onClick, opts = {}) {
  const b = el('button', 'act' + (opts.wide ? ' wide' : '') + (opts.primary ? ' primary' : ''));
  b.appendChild(icon(img));
  b.appendChild(el('span', 'act-label', label));
  if (opts.disabled) b.disabled = true;
  b.addEventListener('click', () => { if (!b.disabled) onClick(); });
  return b;
}

// ---- start -------------------------------------------------------------------------------------------------

function start() {
  const init = call('phone.init');
  strings = init.strings ?? {};
  if (!init.ok) setStatus(init);

  // Open where the player left off, or at the project of the last "Test" launch.
  const last = init.last ?? {};
  if (last.project && showNpcs(last.project, true)) {
    if (last.npc && npcs.some((n) => n.id.toLowerCase() === String(last.npc).toLowerCase())) showNpc(last.npc);
    return;
  }
  showProjects();
}

start();
