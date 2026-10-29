/* A stand-in for App/PhoneApp.cs, so the Personify phone app can be checked in a browser.
 *
 * It answers the same call names with the same JSON shapes. Actions answer { ok, msg, npc } like the C# side,
 * including the refusals: an undo with nothing recorded, a jump to an NPC that is not in the world.
 */

let host = null;

const PROJECTS = [
  { folder: 'Northtown Regulars', name: 'Northtown Regulars', npcs: 3 },
  { folder: 'Empty Pack', name: 'Empty Pack', npcs: 0 },
];

const NPCS = {
  'Northtown Regulars': [
    { id: 'northtown_regulars_rita', name: 'Rita Vance', live: true, spawn: { region: 'Northtown', at: '-64.3, 1.1, 84.5' },
      steps: [{ time: '09:00', text: 'Go inside Motel Office' }, { time: '14:00', text: 'Walk to Northtown' }] },
    { id: 'northtown_regulars_ben', name: 'Ben Okafor', live: false, spawn: null, steps: [] },
    { id: 'northtown_regulars_lou', name: 'Lou with a name long enough to need the whole row', live: true, spawn: null, steps: [] },
  ],
  'Empty Pack': [],
};

let last = { project: 'Northtown Regulars', npc: 'northtown_regulars_rita' };
let clock = 9 * 60 + 30;
const recorded = {};

function find(arg) {
  const [folder, id] = String(arg).split('\n');
  return { folder, n: (NPCS[folder] ?? []).find((x) => x.id === id) };
}

function detail(folder, n) {
  const p = PROJECTS.find((x) => x.folder === folder);
  return { ...n, folder, project: p.name, canUndo: (recorded[folder + '\n' + n.id] ?? 0) > 0 };
}

function answer(ok, msg, folder, n) {
  return JSON.stringify({ ok, msg, npc: detail(folder, n) });
}

const hhmm = (m) => String(Math.floor(m / 60) % 24).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');

export const ready = (s1) => { host = s1; };

export function call(name, arg = '') {
  switch (name) {
    case 'phone.init':
      return JSON.stringify({ ok: true, strings: {}, last });
    case 'phone.projects':
      return JSON.stringify({ ok: true, projects: PROJECTS });
    case 'phone.npcs': {
      const p = PROJECTS.find((x) => x.folder === arg);
      if (!p) return JSON.stringify({ ok: false, msg: 'Project not found.' });
      return JSON.stringify({ ok: true, folder: p.folder, name: p.name,
        npcs: NPCS[p.folder].map((n) => ({ id: n.id, name: n.name, steps: n.steps.length, spawn: !!n.spawn })) });
    }
    case 'phone.npc': {
      const { folder, n } = find(arg);
      if (!n) return JSON.stringify({ ok: false, msg: 'NPC not found.' });
      last = { project: folder, npc: n.id };
      return JSON.stringify({ ok: true, msg: null, npc: detail(folder, n) });
    }
    case 'phone.spawn': {
      const { folder, n } = find(arg);
      n.spawn = { region: 'Westville', at: '-120.4, 0.9, 40.2' };
      return answer(true, 'Start point set in Westville.', folder, n);
    }
    case 'phone.step': {
      const { folder, n } = find(arg);
      clock += 17;
      n.steps.push({ time: hhmm(clock), text: 'Walk to Westville' });
      const k = folder + '\n' + n.id;
      recorded[k] = (recorded[k] ?? 0) + 1;
      return answer(true, `Step ${n.steps.length} added at ${hhmm(clock)}.`, folder, n);
    }
    case 'phone.undo': {
      const { folder, n } = find(arg);
      const k = folder + '\n' + n.id;
      if (!recorded[k]) return answer(false, 'No recorded step to remove.', folder, n);
      recorded[k]--;
      n.steps.pop();
      return answer(true, 'Last step removed.', folder, n);
    }
    case 'phone.jump': {
      const { folder, n } = find(arg);
      return n.live ? answer(true, `Jumped to ${n.name}.`, folder, n) : answer(false, `${n.name} is not in this world.`, folder, n);
    }
    case 'phone.look': {
      const { folder, n } = find(arg);
      return n.live ? answer(true, 'Look applied.', folder, n) : answer(true, `Installed. ${n.name} is not in this world.`, folder, n);
    }
    default:
      console.warn('[preview] no stand-in for s1.call("' + name + '")' + (arg ? ' <- ' + arg : ''));
      return '';
  }
}

export const scenarios = {
  'no last choice': () => { last = {}; },
  'no projects': () => { PROJECTS.length = 0; },
};
