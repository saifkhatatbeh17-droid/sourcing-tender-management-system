// Tiryaq homepage controller: one scroll position drives two scenes on one sticky stage, always moving forward.
//   Scene 1  illustrated production line, hero to inspection          (tiryaq-line.js, 2D canvas, line beats 0..7.34)
//   Handoff  the line pushes in on TQ-0417, the cleanroom takes the same framing, then cranes back to reveal the room
//   Scene 2  3D twelve-line cleanroom -> packaging cell K-06 -> dispatch -> the windowsill at home
//            (tiryaq-journey.js, WebGL, cleanroom beats 6.9..10)
// Once the cleanroom has taken over, the illustrated line is never shown again: packaging, dispatch and home all happen in 3D.
// Distances are in viewport heights scrolled into #track ("d"). Both engines read their beat from window.TiryaqStage.
(() => {
  'use strict';

  /* ---------- Shared artwork: one carton and one label for every scene ---------- */
  // The carton face, drawn in a 92 x 172 unit box (the line's carton). The line draws it in world space, the cleanroom
  // draws it into a texture, so the carton that leaves the cleanroom is the one the line shows waiting in its magazine.
  const ART = {
    ink: '#0a1f26', teal: '#1d5560', ivory: '#f4eee2', coral: '#e07a5f', paper: '#fbf7ef',
    serif: '"Instrument Serif", Georgia, serif', sans: 'Geist, system-ui, sans-serif', mono: '"JetBrains Mono", Consolas, monospace',
    cartonFace(g, opts = {}) {
      const t = (s, x, y, size, color, font, weight) => { g.font = `${weight} ${size}px ${font}`; g.fillStyle = color; g.textAlign = 'left'; g.textBaseline = 'alphabetic'; g.fillText(s, x, y); };
      if (opts.ground !== false) {
        const q = g.createLinearGradient(0, 0, 92, 0); q.addColorStop(0, '#1b555f'); q.addColorStop(1, '#22656f');
        g.fillStyle = q; g.fillRect(0, 0, 92, 172);
      }
      g.fillStyle = ART.ivory; g.fillRect(0, 70, 92, 54);
      g.fillStyle = ART.coral; g.fillRect(0, 124, 92, 3);
      t('Tiryaq', 8, 30, 20, ART.ivory, ART.serif, 400);
      t('PHARMA', 9, 40, 5.5, 'rgba(244,238,226,.75)', ART.sans, 600);
      t('Velmira', 8, 92, 15, ART.teal, ART.serif, 400);
      t('5 mg / 5 mL oral solution', 8, 103, 4.8, ART.ink, ART.sans, 600);
      t('100 mL · illustrative product', 8, 111, 4.2, '#4a5e60', ART.sans, 500);
      t('LOT TQ-0417', 8, 140, 4.6, ART.ivory, ART.mono, 500);
      t('EXP 11/2028', 8, 148, 4.6, ART.ivory, ART.mono, 500);
    },
    // Carton colours the line uses for the side face and the flaps.
    cartonSide: '#123f47', cartonFlap: '#2a6b74'
  };
  window.TiryaqArt = ART;

  const track = document.getElementById('track');
  if (!track) return;
  const lineCanvas = document.getElementById('line-canvas');
  const glCanvas = document.getElementById('gl-canvas');
  const glFallback = document.getElementById('gl-fallback');
  const rail = document.getElementById('home-rail');
  const thread = document.getElementById('tq-thread');
  const threadState = thread && thread.querySelector('[data-tq-state]');
  const RM = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const sm = (v, a, b) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };

  // Scene 1: the line runs from d = 0 to A_END, ending on TQ-0417 in the inspection cell (line beat 7.34).
  const A_END = 8.4, LINE_END_A = 7.34;
  // Handoff: the line keeps pushing in while the cleanroom fades in over it on the same framing.
  const PUSH = [8.2, 9.3];          // anticipation push-in on the bottle (both engines frame it identically)
  const X1 = [8.45, 9.3];           // crossfade, line -> cleanroom (one way: it never fades back)
  // Scene 2: cleanroom beats by depth. Piecewise, so each act gets the scroll room it needs.
  const GLMAP = [
    [8.40, 6.90],   // cleanroom holds the line's framing of TQ-0417
    [9.30, 7.02],   // crossfade complete
    [10.50, 7.30],  // crane back and up: the room is revealed as the lines come online
    [11.20, 7.52],  // twelve-line panorama
    [12.05, 7.80],
    [12.95, 8.20],  // down to the packaging cell
    [14.75, 8.74],  // picked, cartoned, flaps closed
    [15.75, 9.12],  // the carton rides out through dispatch into daylight
    [16.25, 9.38],  // daylight becomes the morning at home
    [17.15, 10.0]   // the windowsill
  ];
  const TOTAL = 17.6;               // + 1 viewport = #track height (see tiryaq-line.css)

  const depth = () => clamp((scrollY - track.offsetTop) / Math.max(1, innerHeight), 0, TOTAL);
  const lineBeat = d => lerp(0, LINE_END_A, clamp(d / A_END));
  const glBeat = d => {
    if (d <= GLMAP[0][0]) return GLMAP[0][1];
    for (let i = 0; i < GLMAP.length - 1; i++) {
      const [d0, b0] = GLMAP[i], [d1, b1] = GLMAP[i + 1];
      if (d <= d1) return lerp(b0, b1, (d - d0) / (d1 - d0));
    }
    return GLMAP[GLMAP.length - 1][1];
  };
  const glAlpha = d => sm(d, X1[0], X1[1]);
  const push = d => sm(d, PUSH[0], PUSH[1]);

  window.TiryaqStage = {
    lineBeat: () => lineBeat(depth()),
    glBeat: () => glBeat(depth()),
    glAlpha: () => glAlpha(depth()),
    handoffPush: () => push(depth()),
    lineVisible: () => glAlpha(depth()) < .999,
    glVisible: () => glAlpha(depth()) > .001,
    // Screen-space position of the lot TQ-0417 bottle, written every frame by whichever engine is drawing it:
    // { x, y, h } = centre and pixel height of the bottle, v = how strongly the lot tag should show, s = state text.
    thread2d: { x: 0, y: 0, h: 0, v: 0 },
    thread3d: { x: 0, y: 0, h: 0, v: 0, s: '' },
    // Framing hook the 3D camera uses to start on exactly the 2D framing of the bottle.
    lineFraming: null
  };

  // One rail for the whole journey: [label, d, lit-from].
  const STATIONS = [
    ['Start', 0, 0], ['Formulation', 2.64, 2.44], ['Filling', 4.07, 3.87], ['Capping', 5.56, 5.36], ['Labelling', 6.65, 6.45],
    ['Inspection', 7.97, 7.45], ['Cleanroom', 11.3, 9.3], ['Packaging', 13.4, 12.9], ['Home', 16.9, 16.2]
  ];
  const goTo = d => scrollTo({ top: track.offsetTop + d * innerHeight, behavior: RM ? 'auto' : 'smooth' });
  let railButtons = [];
  if (rail) {
    STATIONS.forEach(([name, at]) => {
      const bt = document.createElement('button');
      bt.type = 'button';
      bt.style.left = (at / TOTAL * 100) + '%';
      bt.dataset.goD = at;
      bt.setAttribute('aria-label', 'Go to ' + name);
      bt.innerHTML = `<span>${name}</span><i></i>`;
      rail.appendChild(bt);
    });
    railButtons = [...rail.querySelectorAll('button')];
  }
  // Any element with data-go-d jumps to that depth (hero "Follow the bottle", rail stops). data-go-next continues past the stage.
  document.addEventListener('click', e => {
    const bt = e.target.closest('[data-go-d]');
    if (bt) goTo(+bt.dataset.goD);
    const nx = e.target.closest('[data-go-next]');
    if (nx) { const el = document.getElementById(nx.dataset.goNext); if (el) el.scrollIntoView({ behavior: RM ? 'auto' : 'smooth' }); }
  });

  // Scene 2 cards follow the cleanroom beat (they used to belong to the illustrated line's third shot).
  const glCards = [...document.querySelectorAll('[data-gl-in]')];
  const glSteps = [...document.querySelectorAll('[data-gl-at]')];

  let lastText = '';
  function threadText(d, a) {
    if (a > .5) return window.TiryaqStage.thread3d.s || 'Line 04 spine · unboxed';
    return 'Inspection passed · unboxed';
  }

  let last = -1;
  function frame() {
    requestAnimationFrame(frame);
    const d = depth();
    const a = glAlpha(d);
    // The lot tag follows the bottle every frame (the engines move it as the camera eases), even when scroll is idle.
    if (thread) {
      const s = window.TiryaqStage, t2 = s.thread2d, t3 = s.thread3d;
      const v = lerp(t2.v, t3.v, a);
      if (v > .01) {
        const x = lerp(t2.x, t3.x, a), y = lerp(t2.y, t3.y, a), h = lerp(t2.h, t3.h, a);
        // The tag sits up and to the right of the bottle's shoulder, scaled with the bottle so it never covers it.
        const off = clamp(h * .16, 6, 34);
        thread.style.transform = `translate3d(${(x + off).toFixed(1)}px, ${(y - h * .46).toFixed(1)}px, 0)`;
        const txt = threadText(d, a);
        if (threadState && txt !== lastText) { lastText = txt; threadState.textContent = txt; }
      }
      thread.style.opacity = v.toFixed(3);
    }
    if (Math.abs(d - last) < .001) return;
    last = d;
    // The illustrated line stays underneath while the cleanroom fades in over it, so the handoff never dips to black.
    // After the handoff it is switched off for good.
    if (lineCanvas) lineCanvas.style.opacity = a > .999 ? '0' : '1';
    for (const el of [glCanvas, glFallback]) {
      if (!el) continue;
      el.style.opacity = a.toFixed(3);
      el.style.pointerEvents = a > .5 ? '' : 'none';
    }
    const gb = glBeat(d), inGl = a > .5;
    glCards.forEach(c => c.classList.toggle('on', inGl && gb >= +c.dataset.glIn && gb < +(c.dataset.glOut || 99)));
    glSteps.forEach(li => li.classList.toggle('on', inGl && gb >= +li.dataset.glAt));
    if (rail) {
      rail.style.setProperty('--p', (d / TOTAL).toFixed(4));
      // The rail steps back while the handoff plays and once the bottle is home, so both moments get the whole frame.
      rail.classList.toggle('dim', d > 16.2);
      rail.classList.toggle('hush', d > 8.55 && d < 10.2);
      let cur = 0;
      STATIONS.forEach(([, , from], i) => { if (d >= from) cur = i; });
      railButtons.forEach((bt, i) => {
        bt.classList.toggle('on', i <= cur);
        bt.classList.toggle('now', i === cur && d > .5);
      });
    }
  }
  requestAnimationFrame(frame);
})();
