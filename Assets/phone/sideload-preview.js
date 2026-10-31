// The browser preview shell every Sideload app shares.
//
// It runs the SHIPPED index.html, app.css and app.js in Chrome against a stand-in bridge, so structure, copy, state
// and flow can be checked without launching the game. What it deliberately does not do is emulate the renderer:
// Chrome lays the page out with its own engine. Judge structure, copy, state and flow here; judge the layout itself
// in the game with F9 and Ctrl+F10.
//
// The reason this is one file rather than one per app: five apps hand-wrote this shell and no two of them agreed.
// Two ran their controls as `display: block`, which the engine never does; two placed absolutely positioned
// children against the wrong ancestor; none of them set the engine's 15px base, so every page previewed about 7%
// too large. A preview that lies is worse than no preview, because it is believed.
//
// Usage, from a preview.html beside the bundle:
//
//   import { mount } from '../../../Workspace/tools/sideload-preview/sideload-preview.js';
//   mount({
//     title: 'WhatsDab',
//     call(name, arg) { ... return a string, same as the C# handler ... },
//     scenarios: { 'two unread': () => { ... } },
//   });

import { SURFACE } from './dom-surface.js';
import { PRESENT, ABSENT } from './globals.js';

const DENSITY = 1.6375;   // the phone panel is ~1201x655 for a 733x400 page; WebView.cs computes exactly this
const LANDSCAPE = { w: 733, h: 400 };
const PORTRAIT = { w: 400, h: 733 };

export function mount(app) {
  const opts = {
    title: 'Sideload app',
    note: '',
    bundle: '.',
    background: '#0E1113',
    accent: '#5E6AD2',
    call: () => '',
    scenarios: null,
    actions: null,
    fence: true,
    ...app,
  };

  const dom = chrome(opts);
  const bridge = makeBridge(opts, dom);
  installHostExtras(dom);

  // Handed over before the page runs, because a stand-in that only ever answers `call` cannot show a state the game
  // pushes at the page - the offline screen, an arriving message, a lobby emptying out. Those are exactly the parts
  // that can only be seen in the game otherwise.
  opts.ready?.(bridge);

  load(opts, dom, bridge).catch((e) => fail(dom, e));
  return { bridge, ...dom };
}


// --- the page around the viewport ------------------------------------------------------------------------------

function chrome(opts) {
  document.title = `${opts.title} - Sideload preview`;

  const style = document.createElement('style');
  style.textContent = shellCss(opts);
  document.head.appendChild(style);

  document.body.innerHTML = `
    <h1>${escape(opts.title)} - browser preview</h1>
    <p class="note">Runs the shipped bundle against a stand-in host, so structure, copy, state and flow can be
      checked without launching the game. Close, not identical: Chrome has real CSS and the game implements a
      subset of it, and text is measured by a browser rather than by TextMeshPro.
      ${escape(opts.note)}</p>
    <div class="controls" id="sl-controls"></div>
    <div class="frame-label" id="sl-label"></div>
    <div id="sl-stage"><div id="sl-viewport" data-sideload></div></div>
    <pre id="sl-findings" hidden></pre>`;

  return {
    stage: document.getElementById('sl-stage'),
    viewport: document.getElementById('sl-viewport'),
    controls: document.getElementById('sl-controls'),
    label: document.getElementById('sl-label'),
    findings: document.getElementById('sl-findings'),
    portrait: false,
    raw: false,
  };
}

function shellCss(opts) {
  return `
    body { margin: 0; background: #1b1e22; color: #ccc; font: 13px/1.4 system-ui, sans-serif; padding: 20px; }
    h1 { font-size: 13px; font-weight: 600; margin: 0 0 4px; letter-spacing: .4px; color: #8a8f9e; }
    p.note { font-size: 12px; color: #6b7378; margin: 0 0 14px; max-width: 760px; }
    .controls { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 16px; max-width: 800px; }
    .controls button {
      background: #262b30; color: #ccc; border: 1px solid #363c42; padding: 5px 10px;
      font: inherit; cursor: pointer; border-radius: 2px;
    }
    .controls button:hover { border-color: ${opts.accent}; color: #fff; }
    .controls button.on { border-color: ${opts.accent}; color: ${opts.accent}; }
    .frame-label { font-size: 11px; color: #6b7378; margin: 0 0 6px; letter-spacing: .5px; }

    /* The game does NOT draw the page at 733x400 device pixels. The phone panel is about 1201x655 and the renderer
       scales the whole viewport to fit, so one CSS pixel is ${DENSITY} device pixels. A browser showing 733x400 at
       1:1 draws the same page smaller and at a different density, which is most of what makes the two look
       unalike - so the stage scales by the same factor. The 1:1 toggle is still the honest way to check whether
       something fits. */
    #sl-stage { width: ${Math.round(LANDSCAPE.w * DENSITY)}px; height: ${Math.round(LANDSCAPE.h * DENSITY)}px; }
    #sl-viewport {
      width: ${LANDSCAPE.w}px; height: ${LANDSCAPE.h}px; overflow: hidden;
      border: 1px solid #3a4148; background: ${opts.background};
      transform: scale(${DENSITY}); transform-origin: top left;
    }
    #sl-stage.raw { width: ${LANDSCAPE.w + 2}px; height: ${LANDSCAPE.h + 2}px; }
    #sl-stage.raw #sl-viewport { transform: none; }
    #sl-stage.portrait { width: ${Math.round(PORTRAIT.w * DENSITY)}px; height: ${Math.round(PORTRAIT.h * DENSITY)}px; }
    #sl-stage.portrait #sl-viewport { width: ${PORTRAIT.w}px; height: ${PORTRAIT.h}px; }
    #sl-stage.raw.portrait { width: ${PORTRAIT.w + 2}px; height: ${PORTRAIT.h + 2}px; }

    #sl-findings {
      margin-top: 16px; max-width: 900px; white-space: pre-wrap; font: 12px/1.5 ui-monospace, monospace;
      color: #E8B84B; background: #201d16; border-left: 2px solid #E8B84B; padding: 10px 12px;
    }`;
}

function chip(dom, label, onClick) {
  const b = document.createElement('button');
  b.textContent = label;
  b.onclick = () => onClick(b);
  dom.controls.appendChild(b);
  return b;
}

function fail(dom, e) {
  console.error(e);
  dom.findings.hidden = false;
  dom.findings.textContent = String(e && e.stack ? e.stack : e);
}


// --- the stand-in host ------------------------------------------------------------------------------------------

function makeBridge(opts, dom) {
  const listeners = {};

  // The engine's storage survives a restart - it is a MelonPreferences file - so the preview's has to survive a
  // reload, or the one behaviour worth checking (what the app shows on its SECOND run) cannot be checked at all.
  const appId = opts.appId ?? opts.title.toLowerCase().replace(/[^a-z0-9]/g, '');
  const key = (k) => `sideload:${appId}:${k}`;

  const s1 = {
    appId,
    get orientation() { return dom.portrait ? 'portrait' : 'landscape'; },
    setOrientation(value) { setShape(opts, dom, s1, value === 'portrait'); },
    call: (name, arg = '') => opts.call(name, arg) ?? '',
    on(name, fn) { (listeners[name] ??= []).push(fn); },
    log: (...args) => console.log('[' + appId + ']', ...args),
    storage: {
      get: (k, fallback = '') => localStorage.getItem(key(k)) ?? fallback,
      set: (k, v) => localStorage.setItem(key(k), String(v)),
      remove: (k) => localStorage.removeItem(key(k)),
      clear() {
        for (const k of Object.keys(localStorage)) if (k.startsWith(`sideload:${appId}:`)) localStorage.removeItem(k);
      },
    },
    // Not part of the bridge the game exposes - this is how a scenario tells the page something changed.
    emit(name, payload = '') { (listeners[name] ?? []).forEach((fn) => fn(payload)); },
  };

  return s1;
}

// The two things the host adds that a browser has no equivalent for, and the two events it raises that a browser
// never will. Without them the offline screen and the back handler are the parts of an app that can only be looked
// at in the game, which is the trip this preview exists to avoid.
function installHostExtras(dom) {
  Element.prototype.scrollToEnd = function scrollToEnd() {
    requestAnimationFrame(() => { this.scrollTop = this.scrollHeight; });
  };

  // In the game a right-click or Escape raises a cancellable `back` at the page, and the app closes unless a
  // handler takes it. There is no app to close here, so the preview raises the same event and stops - enough to
  // prove the handler navigates, which is the half that can be wrong.
  const raiseBack = (source) => {
    const target = dom.viewport.firstElementChild ?? dom.viewport;
    const event = new CustomEvent('back', { bubbles: true, cancelable: true });
    event.source = source;
    target.dispatchEvent(event);
    if (!event.defaultPrevented) console.info('[preview] nobody took the back - the game would close the app here.');
  };

  document.addEventListener('contextmenu', (e) => {
    if (!e.target.closest('#sl-viewport')) return;
    e.preventDefault();                       // the game has no context menu either
    raiseBack('rightClick');
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') raiseBack('escape');
  });
}


// --- the fence ---------------------------------------------------------------------------------------------------

// A browser Element has about three hundred members and the engine's wrapper has fifty-eight. The gap is the whole
// class of bug this preview is for: `el.closest('.card')` works perfectly in Chrome and returns nothing at all in
// the game, with no error on either side. So every element handed to the page goes out behind a proxy that names
// the first use of anything the engine does not have, and then gets out of the way.
//
// It names rather than throws on purpose. A page that reaches for one missing member usually still renders, and a
// preview that dies on the first line tells you less than one that runs and lists what will be lost.
const named = new Set();

function warn(kind, name) {
  const key = kind + '.' + name;
  if (named.has(key)) return;
  named.add(key);
  console.warn(`[preview] ${kind}.${name} does not exist in the game - it is a browser member the engine's `
             + `wrapper has no equivalent for (Sideload/Script/DomApi.cs).`);
  const box = document.getElementById('sl-findings');
  if (!box) return;
  box.hidden = false;
  box.textContent += `${kind}.${name} - exists in Chrome, not in the game\n`;
}

const RAW = Symbol('raw');
const unwrap = (v) => (v && v[RAW]) || v;

function fence(node, kind = 'element') {
  if (!node || typeof node !== 'object') return node;
  if (node[RAW]) return node;

  const allowed = SURFACE[kind] ?? SURFACE.element;

  /*
    Names the page hung on the node ITSELF are legal and must not be warned about.

    Sideload 1.16.0 made a node take arbitrary properties - `el.whatever = x` used to throw, and that
    was the one thing stopping a virtual-DOM renderer from mounting. React uses it on every container
    (`__reactContainer$...`), so a fence that warns about a name the page invented is describing an
    engine two releases old.
  */
  const own = new Set();

  /*
    `onclick`, `oninput`, and every other all-lower-case `on...` name.

    The engine tests the SHAPE of the name rather than keeping a list (DomApi.IsHandlerName), because a
    list answers no for the next event the web adds - and a renderer that asks `name.toLowerCase() in
    dom` then registers its listener under the wrong spelling and the page silently does nothing when
    touched. The fence mirrors the test rather than the list, for the same reason.
  */
  const isHandler = (prop) => typeof prop === 'string' && /^on[a-z]+$/.test(prop);

  return new Proxy(node, {
    get(target, prop, receiver) {
      if (prop === RAW) return target;
      if (typeof prop === 'symbol') return Reflect.get(target, prop, target);
      if (!allowed.has(prop) && !own.has(prop) && !isHandler(prop)) warn(kind, String(prop));

      const value = Reflect.get(target, prop, target);
      if (typeof value === 'function') return (...args) => wrapResult(value.apply(target, args.map(unwrap)));
      return wrapResult(value);
    },
    set(target, prop, value) {
      own.add(prop);
      return Reflect.set(target, prop, unwrap(value), target);
    },
  });
}

// A collection comes back as a real Array, because that is what the engine hands over: `querySelectorAll` and
// `children` both return a JS array there (DomApi.cs WrapAll), so `.forEach` and `.map` work in the game and would
// not on Chrome's HTMLCollection. Converting here makes the preview stricter and the game's own shape the one an
// app is written against.
function wrapResult(value) {
  if (!value || typeof value !== 'object') return value;
  if (value.nodeType === 1) return fence(value);

  // Duck-typed rather than `instanceof NodeList`, because the shell also runs under the headless DOM the smoke test
  // uses, where those constructors are not globals and `instanceof` throws rather than returning false.
  const collection = typeof value.length === 'number'
                     && (typeof value.item === 'function' || Array.isArray(value));
  return collection ? Array.from(value, (n) => fence(n)) : value;
}

// `document` in the game is six members on a wrapper, not the browser's document. Handing the page the real one
// lets it reach `document.head`, `document.createTextNode` and `document.cookie` - none of which exist in the
// engine, and all of which look fine here.
function fencedDocument(viewport, fenced) {
  const wrap = fenced ? fence : (x) => x;
  const facade = {
    get body() { return wrap(viewport); },
    getElementById: (id) => wrap(viewport.querySelector('#' + CSS.escape(id))),
    querySelector: (sel) => wrap(viewport.querySelector(sel)),
    querySelectorAll: (sel) => Array.from(viewport.querySelectorAll(sel), wrap),
    createElement: (tag) => wrap(document.createElement(tag)),
    addEventListener: (type, fn) => viewport.addEventListener(type, fn),
  };
  return fenced ? new Proxy(facade, {
    get(target, prop) {
      if (typeof prop !== 'symbol' && !SURFACE.document.has(prop)) warn('document', String(prop));
      return Reflect.get(target, prop);
    },
  }) : facade;
}


// --- loading the bundle -------------------------------------------------------------------------------------------

// Everything bypasses the HTTP cache. A preview quietly showing the previous edit costs more time than no preview.
const fresh = (url) => fetch(url, { cache: 'no-store' }).then((r) => {
  if (!r.ok) throw new Error(`${url} - ${r.status} ${r.statusText}`);
  return r.text();
});

async function load(opts, dom, s1) {
  const base = opts.bundle.replace(/\/$/, '');
  const stamp = Date.now();

  // The SHIPPED index.html, not a copy pasted into the shell: a duplicate drifts silently, and the day it does the
  // preview is checking a page the game does not have. Its script tags go because app.js is run below instead.
  const [markup, src] = await Promise.all([fresh(`${base}/index.html`), fresh(`${base}/app.js`)]);

  dom.viewport.innerHTML = markup
    .replace(/<script\b[\s\S]*?<\/script>/gi, '')
    .replace(/href="app\.css"/g, `href="${base}/app.css?v=${stamp}"`);

  buildControls(opts, dom, s1);
  setLabel(dom);

  run(src, opts, dom, s1);

  // After the page has had a frame to build itself, say what the preview cannot show correctly.
  requestAnimationFrame(() => requestAnimationFrame(() => checkDivergence(dom)));
}

/*
  The globals the page is handed, built from the GENERATED list.

  PRESENT comes out of Sideload/Script - ScriptHost's SetValue and FastSetProperty calls, plus DomTypes'
  bootstrap - so it cannot drift from the engine. It used to be typed out here instead, under a comment
  claiming the engine installs "document, s1, console, fetch, the four timer functions, and nothing
  else". That was true when written and stopped being true twice: 1.16.0 made `window` the global object
  and added requestAnimationFrame, 1.17.0 added the DOM type chain. Nobody noticed, because a fence that
  hides too much fails silently until something reaches for what it hid - react-dom reads `window.event`
  when it schedules, so the first React page to run here died on its first render.

  Run `node gen-globals.mjs` when the engine gains a global; `--check` says when this is stale.
*/
function engineGlobals(doc, s1, dom) {
  /*
    The window the engine hands a page IS its global object, so this stands in for that rather than for
    the browser's. Handing over the real `window` would put localStorage and navigator back within
    reach, which is the one thing the fence exists to prevent.
  */
  const win = {};

  // The names that need a specific value. Everything else in PRESENT is a constructor, and the
  // browser's own is the right stand-in: nothing here is ever an instance of the engine's version, and
  // naming it is all a framework does with it.
  const bound = {
    document: doc,
    s1,
    console,
    window: win,
    self: win,
    globalThis: win,
    // Accessors, not values: the phone turns while the page is running, and the engine's own
    // innerWidth/innerHeight are accessors for exactly that reason (ScriptHost.cs:775-781).
    get innerWidth() { return dom.portrait ? PORTRAIT.w : LANDSCAPE.w; },
    get innerHeight() { return dom.portrait ? PORTRAIT.h : LANDSCAPE.h; },
    devicePixelRatio: 1,
    setTimeout: (...a) => setTimeout(...a),
    clearTimeout: (...a) => clearTimeout(...a),
    setInterval: (...a) => setInterval(...a),
    clearInterval: (...a) => clearInterval(...a),
    // The engine schedules one frame at 60 Hz rather than tying this to a real vsync.
    requestAnimationFrame: (fn) => setTimeout(fn, 16),
    cancelAnimationFrame: (id) => clearTimeout(id),
    addEventListener: (...a) => doc.addEventListener?.(...a),
    removeEventListener: (...a) => doc.removeEventListener?.(...a),
  };

  /*
    A constructor the HOST lacks still has to exist, because the engine has it.

    react-dom does `b instanceof a.HTMLIFrameElement`, and under a headless DOM - which is how smoke.mjs
    runs this - `globalThis.HTMLIFrameElement` is undefined, so the expression throws instead of
    answering false. A named empty function answers false, which is the same thing the engine's own
    chain answers: nothing here is ever an iframe.
  */
  const stub = (name) => {
    const fn = function () {};
    Object.defineProperty(fn, 'name', { value: name });
    return fn;
  };

  const value = (name) =>
    name in bound ? bound[name] : (globalThis[name] ?? stub(name));

  for (const name of PRESENT) win[name] = value(name);

  return { win, value };
}

function run(src, opts, dom, s1) {
  const doc = fencedDocument(dom.viewport, opts.fence);
  const { win, value } = engineGlobals(doc, s1, dom);

  /*
    Shadow the globals the engine does NOT install. An app that reaches for one gets undefined here,
    which is the same nothing it gets in the game - except here it says so on the first line rather than
    on a player's machine.

    `fetch` is on neither list on purpose: the engine has it, and a preview that let a page reach the
    network would be exercising something the game never does on the main thread.
  */
  const names = opts.fence ? [...PRESENT, ...ABSENT] : ['document', 's1', 'console'];
  const values = opts.fence
    ? [...PRESENT.map(value), ...ABSENT.map(() => undefined)]
    : [doc, s1, console];

  // `win` is referenced through the bindings above; naming it here keeps the linter honest about it.
  void win;

  new Function(...names, `"use strict";\n${src}\n//# sourceURL=app.js`)(...values);
}


// --- what CSS cannot say ------------------------------------------------------------------------------------------

// The engine folds an element into one line of text only when it has DIRECT TEXT of its own; an element whose
// children are ALL inline tags is a flex container with one item per child, and stacks them in a column. There is
// no selector for "my parent has a text node", so the compatibility stylesheet cannot express it and this does.
function checkDivergence(dom) {
  say(dom, inlineOnlyParents(dom),
      'hold only inline children and no text of their own. Here they sit on one line; the game gives each child',
      'its own flex item and stacks them in a column. Add a word of text, or set the direction the element really',
      'wants (Sideload/Dom/DomBuilder.cs IsInlineOnly):');

  say(dom, falseScrollBoxes(dom),
      'match the stylesheet\'s scroll-container names but set no overflow, so they were handed back the',
      '`flex-shrink` the base rule takes away and can now be squeezed here while the game holds them at their',
      'content. Rename them, or write `flex-shrink: 0` on them:');
}

function say(dom, hits, ...lines) {
  if (!hits.length) return;
  dom.findings.hidden = false;
  dom.findings.textContent += `${hits.length} element(s) ` + lines.join('\n') + '\n'
                            + hits.slice(0, 12).map((h) => '    ' + h).join('\n') + '\n';
}

// The engine folds an element into one line of text only when it has DIRECT TEXT of its own; an element whose
// children are ALL inline tags is a flex container with one item per child, and stacks them in a column. There is
// no selector for "my parent has a text node", so the stylesheet cannot express it and this does.
function inlineOnlyParents(dom) {
  const INLINE = new Set(['SPAN', 'B', 'STRONG', 'I', 'EM', 'U', 'SMALL', 'CODE', 'A', 'BR']);
  const hits = [];

  for (const el of dom.viewport.querySelectorAll('*')) {
    if (el.children.length < 2) continue;
    if (![...el.children].every((c) => INLINE.has(c.tagName))) continue;

    const hasText = [...el.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim().length > 0);
    if (hasText) continue;

    hits.push(describe(el));
  }
  return hits;
}

// A scroll container is the one box the engine shrinks below its content, so the stylesheet gives it back the
// `flex-shrink` the base rule takes away. CSS cannot ask "does this element have overflow set", only whether it
// says so inline, so the rest of that selector is a naming convention - and a convention is a guess until somebody
// checks it. Here the computed style knows, so it is checked rather than trusted.
function falseScrollBoxes(dom) {
  // Only a browser can answer this. The headless DOM the smoke test runs against has no CSS engine, so there the
  // check is skipped rather than answered wrongly.
  if (typeof getComputedStyle !== 'function') return [];

  const hits = [];

  for (const el of dom.viewport.querySelectorAll('.scroll, .rows, .messages, .list')) {
    if (el.getAttribute('style')?.includes('overflow')) continue;

    const style = getComputedStyle(el);
    if (style.overflowY !== 'visible' || style.overflowX !== 'visible') continue;

    hits.push(describe(el));
  }
  return hits;
}

const describe = (el) =>
  el.tagName.toLowerCase()
  + (el.id ? '#' + el.id : '')
  + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).join('.') : '');


// --- shape and controls -------------------------------------------------------------------------------------------

function buildControls(opts, dom, s1) {
  const zoom = chip(dom, 'show at 1:1 instead', () => {
    dom.raw = dom.stage.classList.toggle('raw');
    zoom.classList.toggle('on', dom.raw);
    zoom.textContent = dom.raw ? `show at game density (${DENSITY}x)` : 'show at 1:1 instead';
    setLabel(dom);
  });

  const turn = chip(dom, 'turn the phone', () => {
    setShape(opts, dom, s1, !dom.portrait);
    turn.classList.toggle('on', dom.portrait);
  });

  if (opts.scenarios) {
    const picks = [];
    for (const [name, apply] of Object.entries(opts.scenarios)) {
      const b = chip(dom, name, () => {
        apply(s1);
        for (const other of picks) other.classList.toggle('on', other === b);
      });
      picks.push(b);
    }
  }

  if (opts.actions) for (const [name, fn] of Object.entries(opts.actions)) chip(dom, name, () => fn(s1));
}

// The app declares both orientations, so 400x733 is a shape it really has to survive.
//
// Chrome evaluates `@media (orientation: portrait)` against the BROWSER WINDOW, never against a 400x733 div, so the
// portrait rules in app.css would silently never apply here and the preview would show a landscape layout in a
// portrait box. The engine derives orientation from the host rect instead. So: lift that block out of the
// stylesheet and apply it as plain rules while the toggle is on. The stylesheet stays untouched and carries no
// preview-only selector.
let portraitStyle = null;

async function setShape(opts, dom, s1, portrait) {
  if (dom.portrait === portrait) return;
  dom.portrait = portrait;
  dom.stage.classList.toggle('portrait', portrait);
  setLabel(dom);

  if (portrait && !portraitStyle) {
    const css = await fresh(`${opts.bundle.replace(/\/$/, '')}/app.css`);
    const block = extractAtRule(css, '@media (orientation: portrait)');
    if (block === null) {
      console.warn('[preview] no portrait block in app.css - the same layout is being shown in a portrait box');
    } else {
      portraitStyle = document.createElement('style');
      portraitStyle.textContent = block;
      // At the END of the document, not in <head>: app.css arrives via a <link> inside the viewport markup, and
      // these rules carry the same specificity as the ones they override. Document order is the tie-break.
      document.body.appendChild(portraitStyle);
    }
  } else if (!portrait && portraitStyle) {
    portraitStyle.remove();
    portraitStyle = null;
  }

  const target = dom.viewport.firstElementChild ?? dom.viewport;
  target.dispatchEvent(Object.assign(new CustomEvent('orientationchange', { bubbles: true }), {
    value: portrait ? 'portrait' : 'landscape',
  }));
}

function extractAtRule(css, prelude) {
  const at = css.indexOf(prelude);
  if (at < 0) return null;

  let depth = 0, start = -1;
  for (let i = css.indexOf('{', at); i < css.length; i++) {
    if (css[i] === '{') { if (depth++ === 0) start = i + 1; }
    else if (css[i] === '}') { if (--depth === 0) return css.slice(start, i); }
  }
  return null;
}

function setLabel(dom) {
  const { w, h } = dom.portrait ? PORTRAIT : LANDSCAPE;
  dom.label.textContent = dom.raw
    ? `${w} x ${h} css at 1:1 - smaller and denser than the game draws it`
    : `${w} x ${h} css, drawn at ${DENSITY}x - the density the game uses`;
}

const escape = (s) => String(s).replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
