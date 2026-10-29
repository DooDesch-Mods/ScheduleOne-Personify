/* Stand-in host for the Personify editor page.
 *
 * Loaded by preview.html only, never in the game. Installed when there is no `s1` (a plain browser) or when the host does not answer `app.init` (the surface was
 * mounted without the C# side). It holds one example project with five NPCs, the catalogs, saves and packs, and
 * answers every call of the contract with the same JSON shapes, so the page can be driven without the game.
 * `#many` in the URL adds 100 more NPCs to check the list; `#de` loads the German table.
 */
(function () {
  const inBrowser = typeof s1 === 'undefined';
  let needed = inBrowser;
  if (!inBrowser) {
    let raw = '';
    try { raw = s1.call('app.init', ''); }
    catch (e) { console.error('[personify-mock] app.init threw: ' + (e && e.message)); }
    needed = !raw;
  }
  if (!needed) return;
  console.warn('[personify-mock] no editor host answered, the page runs on example data');

  const hash = (typeof location !== 'undefined' && location.hash) || '';
  const clone = v => (v === undefined ? undefined : JSON.parse(JSON.stringify(v)));

  /* ---------- browser stand-ins for Sideload extras ---------- */

  if (inBrowser) {
    if (!Element.prototype.rect) {
      Element.prototype.rect = function () {
        const r = this.getBoundingClientRect();
        return { x: r.left, y: r.top, width: r.width, height: r.height };
      };
    }
    if (!Element.prototype.scrollToEnd) Element.prototype.scrollToEnd = function () { this.scrollTop = this.scrollHeight; };
    // Sideload lays every box out as a column flexbox; the browser gets the same base so the page looks alike.
    const base = document.createElement('style');
    base.textContent = '*{box-sizing:border-box;margin:0;}' +
      'div,body,#app{display:flex;flex-direction:column;}' +
      'html,body{height:100%;}body{background:#0B0C10;font-family:sans-serif;}' +
      'input,select,textarea{font:inherit;}' +
      '.stage:not(.map){background:repeating-linear-gradient(45deg,#14161d,#14161d 12px,#171a22 12px,#171a22 24px);}';
    document.head.insertBefore(base, document.head.firstChild);
  }

  /* ---------- example data ---------- */

  const LOOK = {
    hair: ['Avatar/Hair/Buzzcut/Buzzcut', 'Avatar/Hair/Spiky/Spiky', 'Avatar/Hair/Bowlcut/Bowlcut', 'Avatar/Hair/Afro/Afro', 'Avatar/Hair/Ponytail/Ponytail', 'Avatar/Hair/BunHair/BunHair', 'Avatar/Hair/Mohawk/Mohawk'],
    tops: ['Avatar/Layers/Top/T-Shirt', 'Avatar/Layers/Top/Hoodie', 'Avatar/Layers/Top/ButtonUp', 'Avatar/Layers/Top/Tanktop', 'Avatar/Layers/Top/Overalls'],
    bottoms: ['Avatar/Layers/Bottom/Jeans', 'Avatar/Layers/Bottom/Shorts', 'Avatar/Layers/Bottom/CargoPants', 'Avatar/Layers/Bottom/Skirt'],
    shoes: ['Avatar/Accessories/Feet/Sneakers/Sneakers', 'Avatar/Accessories/Feet/CombatBoots/CombatBoots', 'Avatar/Accessories/Feet/Sandals/Sandals'],
    headwear: ['Avatar/Accessories/Head/Cap/Cap', 'Avatar/Accessories/Head/Beanie/Beanie', 'Avatar/Accessories/Head/BucketHat/BucketHat'],
    eyewear: ['Avatar/Accessories/Head/RectangleFrameGlasses/RectangleFrameGlasses', 'Avatar/Accessories/Head/Oakleys/Oakleys'],
    mouths: ['Avatar/Layers/Face/Face_Neutral', 'Avatar/Layers/Face/Face_Smile', 'Avatar/Layers/Face/Face_Agitated', 'Avatar/Layers/Face/Face_SlightSmile'],
    facialHair: ['Avatar/Layers/Face/FacialHair_Stubble', 'Avatar/Layers/Face/FacialHair_Goatee', 'Avatar/Layers/Face/FacialHair_Swirl'],
    facialDetails: ['Avatar/Layers/Face/EyeShadow', 'Avatar/Layers/Face/Freckles', 'Avatar/Layers/Face/OldPersonWrinkles'],
    tattoos: ['Avatar/Layers/Tattoos/Face/Face_Teardrop', 'Avatar/Layers/Tattoos/Face/Face_Star', 'Avatar/Layers/Tattoos/Body/Chest_Skull', 'Avatar/Layers/Tattoos/Body/Arm_Snake'],
    accessories: ['Avatar/Accessories/Chest/Necklace/Necklace', 'Avatar/Accessories/Waist/Belt/Belt', 'Avatar/Accessories/Hands/Watch/Watch'],
  };
  const entry = (path, extra) => Object.assign({ path, name: path.slice(path.lastIndexOf('/') + 1).replace(/_/g, ' '), tint: '#FFFFFF' }, extra || {});
  const catalogLook = {};
  for (const k in LOOK) catalogLook[k] = LOOK[k].map(p => entry(p, k === 'tattoos' ? { group: p.indexOf('/Face/') >= 0 ? 'face' : 'body' } : k === 'mouths' || k === 'facialHair' || k === 'facialDetails' ? { tint: '#000000' } : null));
  catalogLook.faceLayers = [].concat(catalogLook.mouths, catalogLook.facialHair, catalogLook.facialDetails, catalogLook.tattoos.filter(x => x.group === 'face'));
  catalogLook.bodyLayers = [].concat(catalogLook.tops, catalogLook.bottoms, catalogLook.tattoos.filter(x => x.group === 'body'));
  catalogLook.accessories = [].concat(catalogLook.shoes, catalogLook.headwear, catalogLook.eyewear, catalogLook.accessories);
  catalogLook.eyeballMaterials = ['Default', 'Bloodshot', 'Glowing'];
  catalogLook.templates = [{ id: 'menu', name: 'Menu character' }];

  const WORLD = {
    hasWorld: true, hasNpcs: true,
    buildings: ['Motel Office', 'Taco Ticklers', 'Hyland Bank', 'Bungalow', 'North Apartments', 'Barn'],
    regions: ['Northtown', 'Westville', 'Downtown', 'Docks', 'Suburbia', 'Uptown'],
    npcs: [
      { id: 'jessi_waters', name: 'Jessi Waters', region: 'Northtown', role: 'customer', source: 'vanilla' },
      { id: 'kyle_cooley', name: 'Kyle Cooley', region: 'Northtown', role: 'customer', source: 'vanilla' },
      { id: 'benji_coleman', name: 'Benji Coleman', region: 'Northtown', role: 'dealer', source: 'vanilla' },
      { id: 'albert_hoover', name: 'Albert Hoover', region: 'Northtown', role: 'supplier', source: 'vanilla' },
      { id: 'donna_martin', name: 'Donna Martin', region: 'Northtown', role: 'civilian', source: 'vanilla' },
      { id: 'chloe_bowers', name: 'Chloe Bowers', region: 'Westville', role: 'customer', source: 'vanilla' },
      { id: 'ray_hoffman', name: 'Ray Hoffman', region: 'Downtown', role: 'customer', source: 'vanilla' },
      { id: 'zara_knox', name: 'Zara Knox', region: 'Docks', role: 'customer', source: 'Night Shift Pack' },
    ],
    items: [
      { id: 'cuke', name: 'Cuke', category: 'Consumable' }, { id: 'energydrink', name: 'Energy Drink', category: 'Consumable' },
      { id: 'banana', name: 'Banana', category: 'Consumable' }, { id: 'ogkushseed', name: 'OG Kush Seed', category: 'Seeds' },
      { id: 'sourdieselseed', name: 'Sour Diesel Seed', category: 'Seeds' }, { id: 'soil', name: 'Soil', category: 'Growing' },
      { id: 'fertilizer', name: 'Fertilizer', category: 'Growing' }, { id: 'baggie', name: 'Baggie', category: 'Packaging' },
      { id: 'jar', name: 'Jar', category: 'Packaging' }, { id: 'm1911', name: 'M1911', category: 'Weapons' },
    ],
  };
  const EFFECTS = [
    { id: 'calming', name: 'Calming', tier: 1 }, { id: 'energizing', name: 'Energizing', tier: 2 }, { id: 'euphoric', name: 'Euphoric', tier: 1 },
    { id: 'focused', name: 'Focused', tier: 2 }, { id: 'munchies', name: 'Munchies', tier: 1 }, { id: 'paranoia', name: 'Paranoia', tier: 1 },
    { id: 'sneaky', name: 'Sneaky', tier: 2 }, { id: 'athletic', name: 'Athletic', tier: 3 }, { id: 'glowie', name: 'Glowing', tier: 4 },
    { id: 'zombifying', name: 'Zombifying', tier: 5 },
  ];
  const MAP = {
    hasMap: true, image: '', minX: -200, minZ: -150, maxX: 200, maxZ: 150,
    places: [
      { kind: 'building', name: 'Motel Office', label: 'Motel Office', x: -120, y: 1, z: 60 },
      { kind: 'building', name: 'Taco Ticklers', label: 'Taco Ticklers', x: -40, y: 1, z: 30 },
      { kind: 'building', name: 'Hyland Bank', label: 'Hyland Bank', x: 60, y: 1, z: -10 },
      { kind: 'building', name: 'North Apartments', label: 'North Apartments', x: -60, y: 1, z: 110 },
      { kind: 'carpark', name: 'motel_carpark', label: 'Motel car park', x: -110, y: 0.5, z: 45 },
      { kind: 'carpark', name: 'docks_carpark', label: 'Docks car park', x: 150, y: 0.5, z: -100 },
      { kind: 'seat', name: 'Map/Northtown/Park/Bench (1)', label: 'Park bench', x: -20, y: 1, z: 80 },
      { kind: 'seat', name: 'Map/Downtown/Plaza/Bench', label: 'Plaza bench', x: 40, y: 1, z: 10 },
      { kind: 'atm', name: 'atm_north', label: 'ATM Northtown', x: -50, y: 1, z: 40 },
      { kind: 'vending', name: 'vend_motel', label: 'Vending machine', x: -118, y: 1, z: 52 },
    ],
    vehicles: [{ code: 'shitbox', label: 'Shitbox' }, { code: 'veeper', label: 'Veeper' }, { code: 'bruiser', label: 'Bruiser' }],
  };

  const look = (gender, skin, hair, hairColor, top, bottom, shoes, mouth) => ({
    gender, height: 0.98, weight: 0.4, skinColor: skin, hairPath: hair, hairColor,
    leftEyeLidColor: skin, rightEyeLidColor: skin,
    faceLayers: [{ path: mouth, tint: '#000000' }],
    bodyLayers: [{ path: top, tint: '#3A5A8A' }, { path: bottom, tint: '#2A2A30' }],
    accessories: [{ path: shoes, tint: '#E0E0E0' }],
  });

  function exampleProject() {
    return {
      folder: 'night-shift', name: 'Night Shift',
      editor: { modVersion: '1.0.0', description: 'Five people who keep Northtown busy.' },
      manifest: { name: 'Night Shift', author: 'DooDesch', schemaVersion: 3, packId: 'night-shift', autoRegister: true },
      npcs: [
        {
          id: 'mara_quinn', name: 'Mara Quinn',
          appearance: look(1, '#C8A07A', LOOK.hair[4], '#4A2A1A', LOOK.tops[1], LOOK.bottoms[0], LOOK.shoes[0], LOOK.mouths[1]),
          behavior: { conversation: 'customer' },
          spawn: { x: -60, y: 1, z: 100, region: 'Northtown', physical: true },
          contact: { visible: true, mapMarker: true },
          relationships: { delta: 1.5, unlocked: false, unlockType: 'Recommendation', connections: ['jessi_waters', 'dex_romero'] },
          customer: {
            spending: { min: 200, max: 500 }, ordersPerWeek: { min: 2, max: 4 }, standards: 'Moderate',
            preferredOrderDay: 'Friday', orderTime: '18:30', callPoliceChance: 0.3,
            affinities: { marijuana: 0.8, cocaine: -0.4 }, preferredProperties: ['calming', 'euphoric'],
          },
          schedule: [
            { type: 'walkTo', time: '08:00', position: [-40, 1, 30], faceDestination: true },
            { type: 'stayInBuilding', time: '09:00', building: 'Taco Ticklers', duration: 480 },
            { type: 'sit', time: '17:30', seatSetPath: 'Map/Northtown/Park/Bench (1)', duration: 60 },
            { type: 'stayInBuilding', time: '22:00', building: 'North Apartments', duration: 600 },
          ],
          lines: {
            greeting: ['Hey, you again.', 'What is up?'],
            phone: { contract_request: ['Need <PRODUCT>, can pay <PRICE>. Meet at <LOCATION>?'] },
            reactions: { hurt: ['Ow, what the hell!'] },
          },
        },
        {
          id: 'dex_romero', name: 'Dex Romero',
          appearance: look(0, '#8A6448', LOOK.hair[0], '#101014', LOOK.tops[0], LOOK.bottoms[2], LOOK.shoes[1], LOOK.mouths[2]),
          behavior: { conversation: 'dealer', aggression: 0.4 },
          spawn: { x: -118, y: 1, z: 58, region: 'Northtown', physical: true },
          relationships: { delta: 2, unlocked: true, connections: ['mara_quinn', 'benji_coleman'] },
          dealer: { type: 'player', cut: 0.2, signingFee: 500, home: 'Motel Office' },
          schedule: [
            { type: 'dealSignal', time: '10:00' },
            { type: 'locationAction', time: '20:00', position: [-112, 1, 50], action: 'smokeBreak', duration: 30 },
          ],
          lines: { phone: { inventory_depleted: ['I am out. Bring more.'] } },
        },
        {
          id: 'sully_bright', name: 'Sully Bright',
          appearance: look(0, '#E0B898', LOOK.hair[3], '#2A1A10', LOOK.tops[4], LOOK.bottoms[0], LOOK.shoes[2], LOOK.mouths[0]),
          behavior: { conversation: 'none' },
          spawn: { x: 150, y: 1, z: -90, region: 'Docks', physical: true },
          relationships: { connections: ['albert_hoover'] },
          supplier: { items: ['ogkushseed', 'soil', 'fertilizer'], minOrderLimit: 50, maxOrderLimit: 800, recommendMessage: 'Sully at the docks has seeds.', unlockHint: 'Ask around the docks.' },
          schedule: [{ type: 'driveToCarPark', time: '07:00', parkingLot: 'docks_carpark', createVehicle: { code: 'veeper' } }],
          lines: { phone: { deaddrop_ready: ['Your stuff is at <LOCATION>.'] } },
        },
        {
          id: 'old_pete', name: 'Old Pete',
          appearance: Object.assign(look(0, '#D8B090', LOOK.hair[2], '#B0B0B0', LOOK.tops[2], LOOK.bottoms[1], LOOK.shoes[0], LOOK.mouths[3]), { height: 0.92, weight: 0.7 }),
          spawn: { x: 40, y: 1, z: 10, region: 'Downtown', physical: true },
          relationships: { connections: ['donna_martin'] },
          schedule: [
            { type: 'sit', time: '09:00', seatSetPath: 'Map/Downtown/Plaza/Bench', duration: 240 },
            { type: 'useAtm', time: '13:30' },
            { type: 'useSlotMachine', time: '15:00', position: [55, 1, -20], bet: 10, mode: 'untilBroke' },
          ],
          lines: { greeting: ['Back in my day...'], reactions: { annoyed: ['Kids these days.'] } },
        },
        {
          id: 'ivy_lane', name: 'Ivy Lane',
          appearance: look(1, '#6A4A34', LOOK.hair[5], '#1A1010', LOOK.tops[3], LOOK.bottoms[3], LOOK.shoes[0], LOOK.mouths[1]),
          spawn: { x: -20, y: 1, z: 80, region: 'Northtown', physical: true },
          relationships: { connections: ['mara_quinn'] },
          schedule: [{ type: 'walkTo', time: '11:00', position: [-20, 1, 80] }, { type: 'locationAction', time: '12:00', position: [-22, 1, 84], action: 'drinking', duration: 90 }],
        },
      ],
    };
  }

  /* ---------- state ---------- */

  let project = null;
  let undoStack = [];
  let redoStack = [];
  let installed = 'not_installed';
  let lastLiveKey = '';
  const history = [];
  const handlers = {};
  const projects = [
    { folder: 'night-shift', name: 'Night Shift', npcs: 5, formatVersion: 1, install: 'changed' },
    { folder: 'docks-crew', name: 'Docks Crew', npcs: 2, formatVersion: 1, install: 'installed' },
  ];
  let packs = [
    { folder: 'night-shift', name: 'Night Shift', version: '0.9.0', author: 'DooDesch', npcs: 5, enabled: true, ownProject: 'night-shift' },
    { folder: 'CityLife', name: 'City Life', version: '2.1.0', author: 'someone', npcs: 14, enabled: true },
    { folder: 'HorrorNights', name: 'Horror Nights', version: '1.0.3', author: 'another', npcs: 3, enabled: false },
  ];

  function addMany(p) {
    const roles = ['civilian', 'customer', 'dealer', 'supplier'];
    for (let i = 0; i < 100; i++) {
      const n = clone(p.npcs[i % 5]);
      n.id = 'extra_' + i;
      n.name = 'Extra ' + (i + 1);
      delete n.customer; delete n.dealer; delete n.supplier;
      const r = roles[i % 4];
      if (r !== 'civilian') n[r] = roleDefaults(r);
      p.npcs.push(n);
    }
  }

  function roleDefaults(role) {
    if (role === 'customer') return { spending: { min: 200, max: 500 }, ordersPerWeek: { min: 1, max: 5 }, standards: 'Moderate' };
    if (role === 'dealer') return { type: 'player', cut: 0.2, signingFee: 0 };
    if (role === 'supplier') return { items: [], minOrderLimit: 50, maxOrderLimit: 500 };
    return null;
  }

  function problems() {
    const out = [];
    if (!project) return out;
    if (!project.manifest.author) out.push({ severity: 'warning', code: 'author_missing', npc: -1, area: 'pack', field: 'author', text: 'The pack has no author.' });
    project.npcs.forEach((n, i) => {
      if (!n.name || !String(n.name).trim()) out.push({ severity: 'error', code: 'name_missing', npc: i, area: 'role', field: 'name', text: 'The NPC has no name.' });
      if (n.customer && !(n.relationships && n.relationships.connections && n.relationships.connections.length))
        out.push({ severity: 'warning', code: 'customer_unconnected', npc: i, area: 'relations', field: 'relationships.connections', text: 'A customer without connections can only be unlocked directly.' });
      if (n.dealer && !n.dealer.home) out.push({ severity: 'warning', code: 'dealer_homeless', npc: i, area: 'role', field: 'dealer.home', text: 'The dealer has no home.' });
      if (n.supplier && !(n.supplier.items && n.supplier.items.length)) out.push({ severity: 'error', code: 'supplier_empty', npc: i, area: 'role', field: 'supplier.items', text: 'The supplier sells nothing.' });
      (n.schedule || []).forEach(s => {
        if (s.type === 'stayInBuilding' && !s.building) out.push({ severity: 'error', code: 'step_no_building', npc: i, area: 'routine', field: 'schedule', text: 'A step has no building.' });
      });
    });
    return out;
  }

  const delta = extra => Object.assign({ ok: true, problems: problems(), install: installed, canUndo: undoStack.length > 0, canRedo: redoStack.length > 0 }, extra || {});
  const state = extra => Object.assign(delta(), { project: clone(project) }, extra || {});
  const fail = code => ({ ok: false, error: code });

  function snapshot() {
    undoStack.push(JSON.stringify(project));
    if (undoStack.length > 200) undoStack.shift();
    redoStack = [];
    if (installed === 'installed') installed = 'changed';
  }

  function getPath(o, path) { for (const k of path.split('.')) { if (!o || typeof o !== 'object') return undefined; o = o[k]; } return o; }
  function setPath(o, path, v) {
    const ks = path.split('.');
    for (let i = 0; i < ks.length - 1; i++) { if (!o[ks[i]] || typeof o[ks[i]] !== 'object') o[ks[i]] = {}; o = o[ks[i]]; }
    o[ks[ks.length - 1]] = clone(v);
  }
  function unsetPath(o, path) {
    const ks = path.split('.');
    for (let i = 0; i < ks.length - 1; i++) { if (!o || typeof o !== 'object') return; o = o[ks[i]]; }
    if (o && typeof o === 'object') delete o[ks[ks.length - 1]];
  }

  function applyChanges(a) {
    const changes = a.changes || [a.unset ? { path: a.path, unset: true } : { path: a.path, value: a.value }];
    // Live slider values merge into one undo step until the final write of the same field.
    const key = JSON.stringify([a.ids, changes.map(c => c.path)]);
    if (!(lastLiveKey && lastLiveKey === key)) snapshot();
    lastLiveKey = a.live ? key : '';
    for (const c of changes) {
      if (!a.ids || a.ids.length === 0) {
        const target = c.path.indexOf('editor.') === 0 ? project.editor : project.manifest;
        const p = c.path.indexOf('editor.') === 0 ? c.path.slice(7) : c.path;
        if (c.unset) unsetPath(target, p); else setPath(target, p, c.value);
        if (c.path === 'name' && !c.unset) project.name = c.value;
        continue;
      }
      for (const id of a.ids) {
        const n = project.npcs.find(x => x.id === id);
        if (!n) { console.warn('[personify-mock] set on unknown NPC ' + id); continue; }
        if (c.unset) unsetPath(n, c.path); else setPath(n, c.path, c.value);
      }
    }
  }

  const slug = s => String(s).toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '') || 'npc';
  function freeId(base) { let id = base, i = 2; while (project.npcs.some(n => n.id === id)) id = base + '_' + i++; return id; }
  const pickOne = a => a[Math.floor(Math.random() * a.length)];
  function randomLook() {
    const skins = ['#F0D0B0', '#D8B090', '#C8A07A', '#96785F', '#6A4A34', '#4A3020'];
    const skin = pickOne(skins);
    const l = look(Math.random() < 0.5 ? 0 : 1, skin, pickOne(LOOK.hair), pickOne(['#101014', '#4A2A1A', '#B08040', '#B0B0B0']),
      pickOne(LOOK.tops), pickOne(LOOK.bottoms), pickOne(LOOK.shoes), pickOne(LOOK.mouths));
    l.height = Math.round((0.9 + Math.random() * 0.15) * 100) / 100;
    l.weight = Math.round(Math.random() * 100) / 100;
    l.bodyLayers[0].tint = pickOne(['#3A5A8A', '#8A3A3A', '#3A8A5A', '#E0E0E0', '#202020']);
    return l;
  }

  /* ---------- calls ---------- */

  const H = {
    'app.init': () => {
      let strings = {};
      if (hash.indexOf('de') >= 0 && inBrowser) {
        try {
          const x = new XMLHttpRequest();
          x.open('GET', 'de.json', false);
          x.send();
          strings = JSON.parse(x.responseText);
        } catch (e) { console.warn('[personify-mock] de.json not loaded: ' + e.message); }
      }
      return { lang: Object.keys(strings).length ? 'de' : 'en', strings, expert: false, personnelVersion: '2.3.0', hasWorld: true, sideloadVersion: 'mock' };
    },
    'app.setExpert': () => ({ ok: true }),
    'projects.list': () => projects,
    'projects.create': a => {
      if (!a.name) return fail('name_missing');
      const folder = slug(a.name).replace(/_/g, '-');
      if (projects.some(p => p.folder === folder)) return fail('exists');
      projects.push({ folder, name: a.name, npcs: 0, formatVersion: 1, install: 'not_installed' });
      return { ok: true, folder };
    },
    'projects.open': folder => {
      const meta = projects.find(p => p.folder === folder);
      if (!meta) return fail('not_found');
      if (folder === 'night-shift') { project = exampleProject(); if (hash.indexOf('many') >= 0) addMany(project); installed = 'changed'; }
      else { project = { folder, name: meta.name, editor: {}, manifest: { name: meta.name, schemaVersion: 3 }, npcs: [] }; installed = meta.install; }
      undoStack = []; redoStack = []; lastLiveKey = '';
      history.length = 0;
      history.push({ id: 3, utc: new Date(Date.now() - 600000).toISOString(), npcs: project.npcs.length });
      history.push({ id: 2, utc: new Date(Date.now() - 86400000).toISOString(), npcs: Math.max(0, project.npcs.length - 1) });
      history.push({ id: 1, utc: new Date(Date.now() - 3 * 86400000).toISOString(), npcs: Math.max(0, project.npcs.length - 2) });
      return state();
    },
    'projects.delete': folder => { const i = projects.findIndex(p => p.folder === folder); if (i < 0) return fail('not_found'); projects.splice(i, 1); return { ok: true }; },
    'projects.importable': () => [
      { path: 'Mods/Personnel/Packs/CityLife', name: 'City Life', kind: 'installed', npcs: 14 },
      { path: 'UserData/Personify/Import/gangs.zip', name: 'Gangs', kind: 'zip', npcs: 6 },
    ],
    'projects.import': path => {
      const name = path.slice(path.lastIndexOf('/') + 1).replace('.zip', '');
      const folder = slug(name).replace(/_/g, '-');
      if (!projects.some(p => p.folder === folder)) projects.push({ folder, name, npcs: 0, formatVersion: 1, install: 'installed' });
      return { ok: true, folders: [folder] };
    },
    'project.set': a => { if (!project) return fail('no_project'); applyChanges(a); return delta(); },
    'project.unset': a => { if (!project) return fail('no_project'); applyChanges(Object.assign({}, a, { unset: true })); return delta(); },
    'project.undo': () => { if (!undoStack.length) return state(); redoStack.push(JSON.stringify(project)); project = JSON.parse(undoStack.pop()); lastLiveKey = ''; return state(); },
    'project.redo': () => { if (!redoStack.length) return state(); undoStack.push(JSON.stringify(project)); project = JSON.parse(redoStack.pop()); lastLiveKey = ''; return state(); },
    'project.history': () => history,
    'project.restore': id => {
      const h = history.find(x => String(x.id) === String(id));
      if (!h) return fail('restore_failed');
      snapshot();
      project.npcs = project.npcs.slice(0, h.npcs);
      return state();
    },
    'npc.add': a => {
      if (!project) return fail('no_project');
      if (!a.name) return fail('name_missing');
      snapshot();
      const id = freeId(slug(a.name));
      let appearance = randomLook();
      if (String(a.look).indexOf('copy:') === 0) { const src = project.npcs.find(n => n.id === a.look.slice(5)); if (src) appearance = clone(src.appearance); }
      else if (a.look === 'template:menu' || a.look === 'menu') appearance = look(0, '#96785F', LOOK.hair[0], '#101014', LOOK.tops[0], LOOK.bottoms[0], LOOK.shoes[0], LOOK.mouths[0]);
      const n = { id, name: a.name, appearance };
      const r = roleDefaults(a.role);
      if (r) n[a.role] = r;
      project.npcs.push(n);
      return state({ added: id });
    },
    'npc.remove': a => { snapshot(); project.npcs = project.npcs.filter(n => a.ids.indexOf(n.id) < 0); return state(); },
    'npc.duplicate': a => {
      const src = project.npcs.find(n => n.id === a.id);
      if (!src) return fail('npc_not_found');
      snapshot();
      const n = clone(src);
      n.id = freeId(src.id + '_copy');
      n.name = src.name + ' 2';
      project.npcs.splice(project.npcs.indexOf(src) + 1, 0, n);
      return state({ added: n.id });
    },
    'npc.setRole': a => {
      if (['civilian', 'customer', 'dealer', 'supplier'].indexOf(a.role) < 0) return fail('role_unknown');
      snapshot();
      for (const id of a.ids) {
        const n = project.npcs.find(x => x.id === id);
        if (!n) continue;
        for (const r of ['customer', 'dealer', 'supplier']) if (r !== a.role) delete n[r];
        if (a.role !== 'civilian' && !n[a.role]) n[a.role] = roleDefaults(a.role);
        if (!n.behavior) n.behavior = {};
        n.behavior.conversation = a.role === 'customer' ? 'customer' : a.role === 'dealer' ? 'dealer' : 'none';
      }
      return state();
    },
    'npc.randomLook': a => {
      const n = project.npcs.find(x => x.id === a.id);
      if (!n) return fail('npc_not_found');
      snapshot();
      n.appearance = randomLook();
      return state();
    },
    'preview.show': () => ({ ok: true }),
    'preview.rotate': () => ({ ok: true }),
    'preview.zoom': () => ({ ok: true }),
    // Faces: the mock has no renderer, so every NPC shows its initials.
    'faces.capture': () => ({ ok: true, faces: {} }),
    'faces.wall': () => {
      const w = {};
      const crew = { 'night-shift': ['Mara Quinn', 'Theo Vance', 'Lena Brooks', 'Dex Moreno', 'Ivy Chen'], 'docks-crew': ['Sal Ortega', 'June Park'] };
      for (const p of projects) w[p.folder] = (crew[p.folder] || []).map(n => ({ id: n.toLowerCase().replace(' ', '_'), name: n, face: null }));
      return w;
    },
    'portrait.capture': id => {
      const n = project.npcs.find(x => x.id === id);
      if (!n) return fail('npc_not_found');
      snapshot();
      n.portrait = 'portraits/' + id + '.png';
      return delta({ portrait: n.portrait });
    },
    'stage.rect': () => ({ ok: true }),
    'stage.mode': () => ({ ok: true }),
    'world.map': () => MAP,
    'world.ground': a => {
      const region = a.x < -80 ? 'Westville' : a.z > 40 ? 'Northtown' : a.x > 100 ? 'Docks' : 'Downtown';
      return { ok: true, x: a.x, y: 1, z: a.z, region, walkable: true };
    },
    'catalog.look': () => catalogLook,
    'catalog.world': () => WORLD,
    'catalog.effects': () => EFFECTS,
    'install': () => {
      if (problems().some(p => p.severity === 'error')) return delta({ ok: false, code: 'blocked', text: 'Fix the errors first.' });
      installed = 'installed';
      return delta({ code: 'installed', text: 'The pack is in your game.' });
    },
    'uninstall': () => { installed = 'not_installed'; return delta(); },
    'publish': () => ({ ok: true, path: 'UserData/Personify/Publish/night-shift-1.0.0.zip', text: 'The zip is ready to upload.' }),
    'test.saves': () => [
      { slot: 1, name: 'Main run', lastPlayed: new Date(Date.now() - 3600000).toISOString(), org: 'Benzies', newest: true },
      { slot: 2, name: 'Speedrun', lastPlayed: new Date(Date.now() - 5 * 86400000).toISOString(), org: 'Hyland Holdings' },
    ],
    'test.start': a => (a.slot ? { ok: true } : fail('no_save')),
    'packs.list': () => packs,
    'packs.toggle': a => { const k = packs.find(x => x.folder === a.folder); if (!k) return fail('not_found'); k.enabled = !!a.enabled; return { ok: true }; },
    'packs.remove': folder => { const n = packs.length; packs = packs.filter(k => k.folder !== folder); return n === packs.length ? fail('remove_failed') : { ok: true }; },
    'editor.exit': () => { console.log('[personify-mock] editor.exit, the hub would open now'); return { ok: true }; },
  };

  window.personifyMock = {
    call(name, arg) {
      const h = H[name];
      if (!h) { console.warn('[personify-mock] no handler for ' + name); return ''; }
      let a = arg;
      if (typeof arg === 'string' && arg && (arg[0] === '{' || arg[0] === '[')) {
        try { a = JSON.parse(arg); }
        catch (e) { console.error('[personify-mock] ' + name + ' got unreadable JSON: ' + e.message); return ''; }
      }
      try { return JSON.stringify(h(a)); }
      catch (e) { console.error('[personify-mock] ' + name + ' threw: ' + e.message); return ''; }
    },
    on(name, fn) { (handlers[name] || (handlers[name] = [])).push(fn); },
    // Lets a test raise a host event: personifyMock.emit('toast', { severity:'info', text:'Hi' }).
    emit(name, payload) { for (const fn of handlers[name] || []) fn(JSON.stringify(payload)); },
  };
})();
