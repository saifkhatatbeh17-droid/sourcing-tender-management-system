// Public home page, scene 2: the 3D cleanroom, packaging cell K-06, dispatch and the windowsill at home (three.js).
import * as THREE from '../lib/login-scene/three.module.js';

const RM = matchMedia('(prefers-reduced-motion: reduce)').matches;
const MOB = innerWidth < 860;
const $ = s => document.querySelector(s);
const $$ = s => [...document.querySelectorAll(s)];
const cl = (x, a, b) => Math.min(b, Math.max(a, x));
const sm = t => {
  const u = cl(t, 0, 1);
  return u * u * (3 - 2 * u);
};
const lerp = (a, b, t) => a + (b - a) * t;

/* ============ SHARED INTERACTIVE STATE ============ */
const state = {
  selectedStream: 0,    // 0, 1, or 2 in Beat 1 (Sourcing Convergence)
  towerPulse: 0,        // glow pulse timer when switching streams
  atriumCam: 'crane',   // 'crane' | 'chase' | 'zenith' in Beat 5
  formFocus: 'oral'     // 'oral' | 'solid' | 'sterile' | 'topical' in Beat 5
};

/* ============ STATION CHAPTER DEFINITIONS (0..10 BEAT SCALE) ============ */
const STATIONS = [
  { beat: 0.00, label: 'Origin' },
  { beat: 1.62, label: '01 · Sourcing' },
  { beat: 3.05, label: '02 · Filling' },
  { beat: 4.55, label: '03 · Sealing' },
  { beat: 6.05, label: '04 · Vision QC' },
  { beat: 7.68, label: '05 · 12-Line Scale' },
  { beat: 9.40, label: '06 · Delivered' }
];

const trackEl = $('#track');
const stageEl = $('#stage');
const beatsEls = $$('.tj-beat').map(el => ({
  el,
  bIn: parseFloat(el.dataset.in),
  bOut: parseFloat(el.dataset.out)
}));

/* ============ UNIFIED BOTTOM TIMELINE + DRAG SCRUBBER ============ */
const nodesHost = $('#rail-nodes');
const railTrackEl = $('#rail-track');

if (nodesHost) {
  nodesHost.innerHTML = STATIONS.map((st, idx) =>
    `<button type="button" data-station-idx="${idx}" aria-label="Jump to ${st.label}"><span>${st.label}</span></button>`
  ).join('');
  nodesHost.addEventListener('click', e => {
    const btn = e.target.closest('button');
    if (!btn) return;
    e.stopPropagation();
    const st = STATIONS[Number(btn.dataset.stationIdx)];
    if (st) scrollToBeat(st.beat);
  });
}
const nodeBtns = $$('#rail-nodes button');

function scrollToBeat(beat, instant = false) {
  stopCruise();
  if (!trackEl) return;
  const span = Math.max(1, trackEl.offsetHeight - innerHeight);
  const targetY = trackEl.offsetTop + (cl(beat, 0, 10) / 10) * span;
  scrollTo({ top: targetY, behavior: (instant || RM) ? 'instant' : 'smooth' });
}

// Click or drag-scrub anywhere along the bottom timeline track!
if (railTrackEl) {
  let scrubbing = false;
  const scrubAt = clientX => {
    const rect = railTrackEl.getBoundingClientRect();
    const p = cl((clientX - rect.left) / Math.max(1, rect.width), 0, 1);
    scrollToBeat(p * 10, true);
  };
  railTrackEl.addEventListener('pointerdown', e => {
    if (e.target.closest('button')) return;
    scrubbing = true;
    railTrackEl.setPointerCapture(e.pointerId);
    scrubAt(e.clientX);
  });
  railTrackEl.addEventListener('pointermove', e => {
    if (scrubbing) scrubAt(e.clientX);
  });
  const stopScrub = e => {
    if (!scrubbing) return;
    scrubbing = false;
    if (e && railTrackEl.hasPointerCapture(e.pointerId)) railTrackEl.releasePointerCapture(e.pointerId);
  };
  railTrackEl.addEventListener('pointerup', stopScrub);
  railTrackEl.addEventListener('pointercancel', stopScrub);
}

$$('[data-goto-beat]').forEach(btn => {
  btn.addEventListener('click', () => scrollToBeat(parseFloat(btn.dataset.gotoBeat)));
});

/* ============ AUTO-RUN CRUISE CONTROLLER ============ */
let cruising = false;
function syncCruiseUI() {
  $$('[data-cruise-toggle]').forEach(btn => {
    if (btn.id === 'autorun-btn') {
      btn.setAttribute('aria-pressed', String(cruising));
      btn.textContent = cruising ? '❚❚ Pause' : '▶ Auto-run';
    }
  });
}
function stopCruise() {
  if (!cruising) return;
  cruising = false;
  syncCruiseUI();
}
function toggleCruise() {
  if (cruising) { stopCruise(); return; }
  if (!trackEl) return;
  const span = Math.max(1, trackEl.offsetHeight - innerHeight);
  if (scrollY >= trackEl.offsetTop + span - 24) {
    scrollTo({ top: 0, behavior: 'instant' });
  }
  cruising = true;
  syncCruiseUI();
}
$$('[data-cruise-toggle]').forEach(btn => btn.addEventListener('click', toggleCruise));
addEventListener('wheel', stopCruise, { passive: true });
addEventListener('touchstart', stopCruise, { passive: true });
addEventListener('keydown', e => {
  if (['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp', ' ', 'Home', 'End'].includes(e.key)) stopCruise();
});

/* ============ INTERACTIVE CARD CONTROLS ============ */
const STREAM_META = [
  '4/4 scored · Glass lot cleared',
  '4/4 scored · Bio-active cleared',
  '4/4 scored · Closure lot cleared'
];
$$('[data-stream]').forEach(btn => {
  btn.addEventListener('click', () => {
    const idx = Number(btn.dataset.stream);
    state.selectedStream = idx;
    state.towerPulse = 1.4;
    $$('[data-stream]').forEach((b, i) => {
      b.classList.toggle('is-selected', i === idx);
      b.setAttribute('aria-selected', String(i === idx));
    });
    const seatEl = $('#r-eval-seat');
    if (seatEl) seatEl.textContent = STREAM_META[idx];
  });
});

$$('[data-atrium-cam]').forEach(btn => {
  btn.addEventListener('click', () => {
    state.atriumCam = btn.dataset.atriumCam;
    $$('[data-atrium-cam]').forEach(b => b.classList.toggle('is-on', b === btn));
  });
});

$$('[data-form-focus]').forEach(btn => {
  btn.addEventListener('click', () => {
    state.formFocus = btn.dataset.formFocus;
    $$('[data-form-focus]').forEach(b => {
      const on = b === btn;
      b.classList.toggle('is-on', on);
      b.setAttribute('aria-selected', String(on));
    });
  });
});

/* ============ PROCEDURAL HIGH-DPI STUDIO TEXTURES ============ */
function buildStudioEnv(renderer) {
  const pmrem = new THREE.PMREMGenerator(renderer);
  const envScene = new THREE.Scene();
  envScene.background = new THREE.Color(0x0d2b35);

  const addPanel = (w, h, col, intensity, x, y, z, rx, ry) => {
    const m = new THREE.Mesh(
      new THREE.PlaneGeometry(w, h),
      new THREE.MeshBasicMaterial({ color: new THREE.Color(col).multiplyScalar(intensity), side: THREE.DoubleSide })
    );
    m.position.set(x, y, z);
    m.rotation.set(rx, ry, 0);
    envScene.add(m);
  };
  // Tall vertical photographer's softboxes for razor-sharp bottle shoulder speculars!
  addPanel(5, 22, 0xffffff, 2.8, -12, 5, 10, 0, 0.7);
  addPanel(4, 22, 0xffe4b5, 2.4, 13, 6, 9, 0, -0.75);
  addPanel(24, 8, 0xe8f8f5, 1.9, 0, 16, 0, Math.PI / 2, 0);
  addPanel(16, 12, 0x5de2b0, 1.1, -14, 2, -10, 0, 2.3);

  const envRT = pmrem.fromScene(envScene, 0.035);
  pmrem.dispose();
  return envRT.texture;
}

function makeEtchTexture() {
  const c = document.createElement('canvas');
  c.width = 512;
  c.height = 128;
  const g = c.getContext('2d');
  g.clearRect(0, 0, 512, 128);
  g.font = '600 28px "JetBrains Mono", Consolas, monospace';
  g.fillStyle = 'rgba(232, 246, 244, 0.68)';
  g.textAlign = 'center';
  g.fillText('TQ-0417', 256, 78);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/* ============ TEXTURES REDRAWN FROM THE ILLUSTRATED LINE ============ */
// The cleanroom bottle, belts and hall are drawn from the same palette and the same artwork as the illustrated line,
// so a bottle that crosses between the two shots keeps its label, cap and glass.
const INK = '#0a1f26';

// Fonts are embedded in the page; canvas text needs them loaded before it is drawn, so textures redraw once they are.
const fontRedraws = [];
if (document.fonts) {
  Promise.all(['400 20px "Instrument Serif"', '500 20px "Geist"', '600 20px "Geist"', '500 20px "JetBrains Mono"'].map(f => document.fonts.load(f).catch(() => null)))
    .then(() => fontRedraws.forEach(fn => fn()));
}

// The line's detailed label (drawLabel), 68 x 64 units, flat. Returned as a canvas so it can be wrapped around the bottle.
function drawFlatLabel(g, k) {
  const w = 68 * k, h = 64 * k, u = v => v * k;
  g.clearRect(0, 0, w, h);
  g.fillStyle = '#fbf7ef'; g.fillRect(0, 0, w, h);
  g.fillStyle = '#1d5560'; g.fillRect(0, 0, w, u(9));
  g.fillStyle = '#e07a5f'; g.fillRect(0, u(9), w, u(1.6));
  const text = (s, x, y, size, color, font, weight) => { g.font = `${weight} ${u(size)}px ${font}`; g.fillStyle = color; g.textAlign = 'left'; g.textBaseline = 'alphabetic'; g.fillText(s, u(x + 34), u(y)); };
  text('TIRYAQ PHARMA', -27, 6.6, 4.4, '#f4eee2', 'Geist, system-ui, sans-serif', 600);
  text('Rx', 22, 6.6, 4.4, '#efb85c', 'Geist, system-ui, sans-serif', 600);
  text('Velmira', -27, 25, 13, '#1d5560', '"Instrument Serif", Georgia, serif', 400);
  text('5 mg / 5 mL', -27, 33, 5, INK, 'Geist, system-ui, sans-serif', 600);
  text('Oral solution · 100 mL', -27, 39.5, 4, '#3b5052', 'Geist, system-ui, sans-serif', 500);
  g.fillStyle = '#e07a5f'; g.fillRect(u(-27 + 34), u(42.5), u(22), u(.9));
  text('LOT TQ-0417', -27, 50, 3.6, INK, '"JetBrains Mono", Consolas, monospace', 500);
  text('EXP 11/2028', -27, 55, 3.6, INK, '"JetBrains Mono", Consolas, monospace', 500);
  g.fillStyle = INK;
  for (let i = 0; i < 26; i++) g.fillRect(u(8 + 34 + i * .85), u(45), u((i * 7 % 3) * .35 + .35), u(11));
  text('Illustrative product', 8, 60, 2.6, '#7a8a8a', 'Geist, system-ui, sans-serif', 500);
}

// Wrapped label: the flat art is pre-warped so that, seen side-on, it covers the bottle exactly as it does on the line.
const LABEL_ARC = 3.5;                          // radians of cylinder the label wraps (about 200 degrees)
function makeLabelTexture() {
  const c = document.createElement('canvas'); c.width = 1024; c.height = 512;
  const g = c.getContext('2d');
  const flat = document.createElement('canvas'); flat.width = 544; flat.height = 512;
  const fg = flat.getContext('2d');
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 4;
  const draw = () => {
    drawFlatLabel(fg, 8);
    g.fillStyle = '#fbf7ef'; g.fillRect(0, 0, 1024, 512);
    const r = 0.348;
    for (let cx = 0; cx < 1024; cx++) {
      const th = (cx / 1024 - .5) * LABEL_ARC;
      if (Math.abs(th) >= 1.52) continue;
      const fx = 272 + Math.sin(th) * r * 100 * 8;          // flat-art column that sits at this angle when seen from the front
      g.drawImage(flat, Math.max(0, Math.min(543, Math.floor(fx))), 0, 1, 512, cx, 0, 1, 512);
    }
    tex.needsUpdate = true;
  };
  draw(); fontRedraws.push(draw);
  return tex;
}

// Simplified label for the bottles waiting on the belts (the line draws them without text as well).
function makeLabelStripTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const g = c.getContext('2d');
  g.fillStyle = '#fbf7ef'; g.fillRect(0, 0, 128, 128);
  g.fillStyle = '#e07a5f'; g.fillRect(0, 18, 128, 3);
  g.fillStyle = '#1d5560'; g.fillRect(34, 40, 36, 8); g.fillRect(34, 56, 22, 4);
  g.fillStyle = INK; g.fillRect(74, 84, 26, 16);
  const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

// Chain belt: pale ticks on a darker ground, like the line's belt lip. Greyscale, so any belt colour multiplies it.
function makeBeltTexture() {
  const c = document.createElement('canvas'); c.width = 64; c.height = 64;
  const g = c.getContext('2d');
  g.fillStyle = '#9aa4a4'; g.fillRect(0, 0, 64, 64);
  g.fillStyle = '#ffffff'; for (let x = 0; x < 64; x += 16) g.fillRect(x, 6, 3, 52);
  g.fillStyle = '#6c7a7a'; g.fillRect(0, 0, 64, 6); g.fillRect(0, 58, 64, 6);
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = THREE.RepeatWrapping; tex.wrapT = THREE.ClampToEdgeWrapping; tex.anisotropy = 4;
  return tex;
}

// Soft contact shadow (the line puts a dark ellipse under every bottle).
function makeShadowTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const g = c.getContext('2d');
  const q = g.createRadialGradient(64, 64, 6, 64, 64, 62);
  q.addColorStop(0, 'rgba(5,15,18,.85)'); q.addColorStop(.55, 'rgba(5,15,18,.45)'); q.addColorStop(1, 'rgba(5,15,18,0)');
  g.fillStyle = q; g.fillRect(0, 0, 128, 128);
  return new THREE.CanvasTexture(c);
}

// Radial warm glow under the followed bottle.
function makeGlowTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const g = c.getContext('2d');
  const q = g.createRadialGradient(64, 64, 4, 64, 64, 62);
  q.addColorStop(0, 'rgba(255,214,140,.95)'); q.addColorStop(1, 'rgba(255,214,140,0)');
  g.fillStyle = q; g.fillRect(0, 0, 128, 128);
  return new THREE.CanvasTexture(c);
}

// Hall wall: the line's hall (hall() in the illustrated line) rebuilt as a wall texture, in its night and its lifted day state.
// One tile = four windows. Day keeps the same teal but lifts it, so the cleanroom reads as a brighter room of the same factory.
function makeHallTexture(day) {
  const W = 1536, H = 1000, pitch = W / 4;
  const c = document.createElement('canvas'); c.width = W; c.height = H;
  const g = c.getContext('2d');
  const wall = g.createLinearGradient(0, 0, 0, H);
  if (day) { wall.addColorStop(0, '#2b6a72'); wall.addColorStop(.55, '#3f8187'); wall.addColorStop(1, '#6aa49d'); }
  else { wall.addColorStop(0, '#0a222a'); wall.addColorStop(.55, '#123842'); wall.addColorStop(1, '#174450'); }
  g.fillStyle = wall; g.fillRect(0, 0, W, H);
  g.strokeStyle = day ? 'rgba(244,238,226,.07)' : 'rgba(207,230,218,.045)'; g.lineWidth = 2;
  for (let x = 0; x <= W; x += pitch / 3.5) { g.beginPath(); g.moveTo(x, H * .60); g.lineTo(x, H); g.stroke(); }
  g.strokeStyle = day ? 'rgba(244,238,226,.14)' : 'rgba(207,230,218,.08)'; g.lineWidth = 3;
  g.beginPath(); g.moveTo(0, H * .895); g.lineTo(W, H * .895); g.stroke();
  // tall windows with muntins
  const ww = pitch * .54, wx0 = (pitch - ww) / 2, wy0 = H * .567, wh = H * .30;
  for (let i = 0; i < 4; i++) {
    const x = i * pitch + wx0;
    const glass = g.createLinearGradient(0, wy0, 0, wy0 + wh);
    glass.addColorStop(0, '#9fc9c2'); glass.addColorStop(.6, '#d8e7dc'); glass.addColorStop(1, '#f0dcc0');
    g.fillStyle = glass; g.fillRect(x, wy0, ww, wh);
    g.strokeStyle = '#0c2a32'; g.lineWidth = 7; g.strokeRect(x, wy0, ww, wh);
    g.lineWidth = 3;
    for (let k = 1; k < 4; k++) { g.beginPath(); g.moveTo(x + k * ww / 4, wy0); g.lineTo(x + k * ww / 4, wy0 + wh); g.stroke(); }
    for (let k = 1; k < 6; k++) { g.beginPath(); g.moveTo(x, wy0 + k * wh / 6); g.lineTo(x + ww, wy0 + k * wh / 6); g.stroke(); }
    // column between windows
    const cg = g.createLinearGradient(x - pitch * .24, 0, x - pitch * .12, 0);
    cg.addColorStop(0, day ? '#2a666d' : '#0b2128'); cg.addColorStop(.5, day ? '#4a8f93' : '#163a43'); cg.addColorStop(1, day ? '#2a666d' : '#0b2128');
    g.fillStyle = cg; g.fillRect(x - pitch * .24, 0, pitch * .12, H);
  }
  // roof trusses along the top
  g.strokeStyle = day ? 'rgba(244,238,226,.20)' : 'rgba(207,230,218,.12)'; g.lineWidth = 3;
  g.beginPath(); for (let x = 0; x < W; x += pitch / 3) { g.moveTo(x, H * .035); g.lineTo(x + pitch / 6, 0); g.lineTo(x + pitch / 3, H * .035); } g.stroke();
  g.beginPath(); g.moveTo(0, H * .035); g.lineTo(W, H * .035); g.moveTo(0, 1); g.lineTo(W, 1); g.stroke();
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace; tex.wrapS = THREE.RepeatWrapping; tex.anisotropy = 4;
  return tex;
}

// Light shaft: warm gradient that fades along its length (the line's window light).
function makeShaftTexture() {
  const c = document.createElement('canvas'); c.width = 32; c.height = 128;
  const g = c.getContext('2d');
  const q = g.createLinearGradient(0, 0, 0, 128);
  q.addColorStop(0, 'rgba(255,236,205,1)'); q.addColorStop(1, 'rgba(255,236,205,0)');
  g.fillStyle = q; g.fillRect(0, 0, 32, 128);
  const h = g.createLinearGradient(0, 0, 32, 0);
  h.addColorStop(0, 'rgba(0,0,0,0)'); h.addColorStop(.5, 'rgba(0,0,0,1)'); h.addColorStop(1, 'rgba(0,0,0,0)');
  g.globalCompositeOperation = 'destination-in'; g.fillStyle = h; g.fillRect(0, 0, 32, 128);
  return new THREE.CanvasTexture(c);
}

// Dashed amber floor line, as on the line's floor.
function makeDashTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 8;
  const g = c.getContext('2d');
  g.fillStyle = 'rgba(239,184,92,1)'; g.fillRect(0, 0, 76, 8);
  const tex = new THREE.CanvasTexture(c); tex.wrapS = THREE.RepeatWrapping; tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

// The carton face is the line's own carton (window.TiryaqArt.cartonFace), so the carton TQ-0417 is packed into in the
// cleanroom, the blanks in the line's magazine and the carton on the windowsill are one design.
function makeCartonTexture() {
  const k = 6, c = document.createElement('canvas');
  c.width = 92 * k; c.height = 172 * k;
  const g = c.getContext('2d');
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 4;
  const draw = () => {
    g.setTransform(1, 0, 0, 1, 0, 0); g.clearRect(0, 0, c.width, c.height);
    g.setTransform(k, 0, 0, k, 0, 0);
    if (window.TiryaqArt) window.TiryaqArt.cartonFace(g);
    else { g.fillStyle = '#1d5560'; g.fillRect(0, 0, 92, 172); }
    tex.needsUpdate = true;
  };
  draw(); fontRedraws.push(draw);
  return tex;
}

// Small machine nameplates: mono text on the machines' dark plates.
function makePlateTexture(text, sub) {
  const c = document.createElement('canvas'); c.width = 512; c.height = 128;
  const g = c.getContext('2d');
  const tex = new THREE.CanvasTexture(c); tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = 4;
  const draw = () => {
    g.clearRect(0, 0, 512, 128);
    g.fillStyle = '#0b1f25'; g.fillRect(0, 0, 512, 128);
    g.fillStyle = '#efb85c'; g.fillRect(0, 0, 8, 128);
    g.font = '600 46px "JetBrains Mono", Consolas, monospace'; g.fillStyle = '#cfe6da'; g.textBaseline = 'alphabetic'; g.fillText(text, 30, 62);
    if (sub) { g.font = '500 28px "JetBrains Mono", Consolas, monospace'; g.fillStyle = 'rgba(207,230,218,.62)'; g.fillText(sub, 30, 104); }
    tex.needsUpdate = true;
  };
  draw(); fontRedraws.push(draw);
  return tex;
}

function makeVisionHudTexture() {
  const c = document.createElement('canvas');
  c.width = 768;
  c.height = 896;
  const g = c.getContext('2d');

  g.fillStyle = 'rgba(7, 31, 34, 0.88)';
  g.fillRect(0, 0, 768, 896);

  // Subtle dark-field optical scan grid
  g.strokeStyle = 'rgba(93, 226, 176, 0.09)';
  g.lineWidth = 1;
  for (let y = 32; y < 896; y += 28) {
    g.beginPath(); g.moveTo(24, y); g.lineTo(744, y); g.stroke();
  }

  g.fillStyle = '#5de2b0';
  g.font = '600 22px Consolas, monospace';
  g.fillText('CAMERA 02 · DARK-FIELD INLINE', 46, 62);
  g.fillStyle = 'rgba(239, 184, 92, 0.9)';
  g.fillText('LOT TQ-0417', 575, 62);

  // Calibration brackets on right side of specimen
  g.strokeStyle = '#5de2b0';
  g.lineWidth = 3;
  // Cap bracket
  g.beginPath(); g.moveTo(505, 195); g.lineTo(540, 195); g.lineTo(540, 295); g.lineTo(505, 295); g.stroke();
  g.font = '600 20px Consolas, monospace';
  g.fillStyle = '#5de2b0';
  g.fillText('CAP SEAL', 558, 238);
  g.fillText('1.44 N·m PASS', 558, 268);

  // Meniscus bracket
  g.beginPath(); g.moveTo(505, 415); g.lineTo(540, 415); g.lineTo(540, 475); g.lineTo(505, 475); g.stroke();
  g.fillText('MENISCUS', 558, 440);
  g.fillText('100.0 mL PASS', 558, 468);

  // Label bracket
  g.strokeStyle = '#efb85c';
  g.beginPath(); g.moveTo(505, 505); g.lineTo(540, 505); g.lineTo(540, 720); g.lineTo(505, 720); g.stroke();
  g.fillStyle = '#efb85c';
  g.fillText('LABEL & OCR', 558, 602);
  g.fillText('VERIFIED', 558, 632);

  // Corner reticles
  g.strokeStyle = 'rgba(93, 226, 176, 0.55)';
  [[40, 100, 1, 1], [728, 100, -1, 1], [40, 840, 1, -1], [728, 840, -1, -1]].forEach(([x, y, sx, sy]) => {
    g.beginPath();
    g.moveTo(x, y + sy * 28); g.lineTo(x, y); g.lineTo(x + sx * 28, y);
    g.stroke();
  });

  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

function makeSunriseTexture() {
  const c = document.createElement('canvas');
  c.width = 1024;
  c.height = 768;
  const g = c.getContext('2d');

  // Warm poetic morning sky gradient
  const skyGrad = g.createLinearGradient(0, 0, 0, 620);
  skyGrad.addColorStop(0.0, '#f9e6c4');
  skyGrad.addColorStop(0.55, '#fcefd9');
  skyGrad.addColorStop(1.0, '#dce8e1');
  g.fillStyle = skyGrad;
  g.fillRect(0, 0, 1024, 768);

  // Soft glowing morning sun
  const sunGrad = g.createRadialGradient(700, 340, 12, 700, 340, 190);
  sunGrad.addColorStop(0.0, '#fffdf4');
  sunGrad.addColorStop(0.35, 'rgba(255, 240, 198, 0.85)');
  sunGrad.addColorStop(1.0, 'rgba(255, 240, 198, 0)');
  g.fillStyle = sunGrad;
  g.fillRect(480, 120, 440, 440);

  // Distant misty hills & city skyline silhouette (echoing home-b99-end.png!)
  g.fillStyle = '#bfd2ca';
  g.beginPath();
  g.moveTo(0, 520);
  g.bezierCurveTo(220, 455, 420, 535, 680, 480);
  g.bezierCurveTo(830, 450, 940, 490, 1024, 470);
  g.lineTo(1024, 768); g.lineTo(0, 768); g.closePath();
  g.fill();

  // Subtle quiet morning town silhouettes
  g.fillStyle = 'rgba(130, 162, 156, 0.55)';
  [[140, 500, 42, 120], [215, 485, 48, 135], [560, 495, 44, 125], [635, 510, 38, 110], [810, 482, 46, 138], [885, 502, 40, 118]].forEach(([bx, by, bw, bh]) => {
    g.fillRect(bx, by, bw, bh);
  });

  g.fillStyle = '#9bb8b0';
  g.beginPath();
  g.moveTo(0, 585);
  g.bezierCurveTo(280, 540, 580, 600, 1024, 545);
  g.lineTo(1024, 768); g.lineTo(0, 768); g.closePath();
  g.fill();

  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/* ============ TQ-0417 PATH THROUGH THE CLEANROOM ============ */
// The bottle rests where the illustrated line hands it over, rides the spine belt the length of the room and stops at the
// cartoner's pick position (x = 116.9). From there the packaging timeline (PK below) moves it.
const HERO_X0 = 53.0, HERO_X1 = 116.9;
const ss = t => { const u = cl(t, 0, 1); return u * u * u * (u * (u * 6 - 15) + 10); };   // smootherstep: gentler starts and stops
const seg = (b, a, c) => cl((b - a) / (c - a), 0, 1);
function getHeroX(b) {
  return lerp(HERO_X0, HERO_X1, sm(seg(b, 7.04, 8.24)));
}
// Cleanroom beats for the handoff, packaging, dispatch and home.
const HOLD_END = 7.02;          // the cleanroom holds the line's framing until the crossfade is complete
const REVEAL_END = 7.52;        // the crane-back reveal settles on the twelve-line panorama
const HOME_B = 9.25;            // the daylight veil peaks and the windowsill replaces the factory
const PK = {
  gripDown: [8.19, 8.26], close: [8.26, 8.28], lift: [8.28, 8.36], blank: [8.29, 8.36], erect: [8.36, 8.43],
  across: [8.36, 8.46], lower: [8.46, 8.55], open: [8.55, 8.57], up: [8.57, 8.63], back: [8.63, 8.72],
  dust: [8.57, 8.63], tuck: [8.63, 8.71], out: [8.74, 9.16]
};

/* ============ WEBGL 3D WORLD ============ */
function activateFallback() {
  const fb = $('#gl-fallback'), cv = $('#gl-canvas');
  if (fb) fb.hidden = false;
  cv?.classList.add('is-off');
}

(() => {
  const canvas = $('#gl-canvas');
  if (!canvas) return;

  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: !MOB, powerPreference: 'high-performance' });
  } catch {
    activateFallback();
    return;
  }

  renderer.setPixelRatio(Math.min(devicePixelRatio || 1, MOB ? 1.5 : 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.LinearToneMapping;   // flat, saturated palette like the illustrated line (no filmic desaturation)
  renderer.toneMappingExposure = 1.0;
  renderer.shadowMap.enabled = !MOB;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  const scene = new THREE.Scene();
  scene.environment = buildStudioEnv(renderer);

  const cSkyNight = new THREE.Color(0x123b46);
  const cSkyDay = new THREE.Color(0x6aa49d);
  const cSkyHome = new THREE.Color(0xf4e8d4);
  const cGndNight = new THREE.Color(0x10343e);
  const cGndDay = new THREE.Color(0x8fbcae);

  const curSky = cSkyNight.clone();
  const curGnd = cGndNight.clone();
  scene.background = curSky;
  scene.fog = new THREE.Fog(0x123b46, 30, 112);

  const cam = new THREE.PerspectiveCamera(31, 1, 0.1, 280);

  const hemi = new THREE.HemisphereLight(0xe8faf7, 0x153a44, 0.95);
  scene.add(hemi);

  const keyLight = new THREE.DirectionalLight(0xfff5e4, 1.55);
  keyLight.position.set(-28, 30, 20);
  keyLight.castShadow = !MOB;
  Object.assign(keyLight.shadow.camera, { left: -46, right: 46, top: 36, bottom: -36, near: 2, far: 120 });
  keyLight.shadow.mapSize.set(2048, 2048);
  keyLight.shadow.bias = -0.00035;
  scene.add(keyLight);
  scene.add(keyLight.target);

  const rimLight = new THREE.DirectionalLight(0x7be8cf, 0.68);
  rimLight.position.set(-44, 12, -16);
  scene.add(rimLight);

  /* ---------- Factory World vs Sunlit Home Sanctuary ---------- */
  const factoryWorld = new THREE.Group();
  scene.add(factoryWorld);

  const homeWorld = new THREE.Group();
  homeWorld.position.set(175, 0, 0);
  homeWorld.visible = false;
  scene.add(homeWorld);

  const groundMat = new THREE.MeshStandardMaterial({ color: curGnd, roughness: 0.9, metalness: 0.04, envMapIntensity: 0.2 });
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(520, 320), groundMat);
  ground.rotation.x = -Math.PI / 2;
  ground.position.x = 30;
  ground.receiveShadow = true;
  factoryWorld.add(ground);

  /* ---------- The hall behind the cleanroom ---------- */
  // The line's hall (windows, columns, trusses and window light) continues behind the twelve lines, in the line's night colours and in a
  // lifted day state, so the cleanroom is a brighter room of the same factory rather than a different place.
  const HALL_X0 = -30, HALL_X1 = 230, HALL_Z = -34, HALL_HT = 30, HALL_TILE = 44;
  const hallTexN = makeHallTexture(false), hallTexD = makeHallTexture(true);
  [hallTexN, hallTexD].forEach(t => t.repeat.set((HALL_X1 - HALL_X0) / HALL_TILE, 1));
  const hallGeo = new THREE.PlaneGeometry(HALL_X1 - HALL_X0, HALL_HT);
  const hallN = new THREE.Mesh(hallGeo, new THREE.MeshBasicMaterial({ map: hallTexN, fog: false }));
  const hallDMat = new THREE.MeshBasicMaterial({ map: hallTexD, fog: false, transparent: true, opacity: 0, depthWrite: false });
  const hallD = new THREE.Mesh(hallGeo, hallDMat);
  hallN.position.set((HALL_X0 + HALL_X1) / 2, HALL_HT / 2, HALL_Z);
  hallD.position.set(hallN.position.x, hallN.position.y, HALL_Z + 0.03);
  factoryWorld.add(hallN, hallD);
  // window light falling across the floor
  const shaftTex = makeShaftTexture();
  const shaftMat = new THREE.MeshBasicMaterial({ map: shaftTex, color: 0xffe6c0, transparent: true, opacity: 0.1, depthWrite: false, blending: THREE.AdditiveBlending, fog: false, side: THREE.DoubleSide });
  for (let j = 0; j < 20; j++) {
    const cx = HALL_X0 + j * HALL_TILE / 4 + HALL_TILE / 8;
    if (cx < 30 || cx > 150) continue;
    const g0 = new THREE.BufferGeometry();
    const w0 = 2.6, w1 = 3.6, off = 9;
    g0.setAttribute('position', new THREE.Float32BufferAttribute([cx - w0, 12.6, HALL_Z + 0.2, cx + w0, 12.6, HALL_Z + 0.2, cx + off - w1, 0.03, -13, cx + off + w1, 0.03, -13], 3));
    g0.setAttribute('uv', new THREE.Float32BufferAttribute([0, 1, 1, 1, 0, 0, 1, 0], 2));
    g0.setIndex([0, 1, 2, 1, 3, 2]);
    factoryWorld.add(new THREE.Mesh(g0, shaftMat));
  }
  // dashed amber floor line, as on the line's floor
  const dashTex = makeDashTexture(); dashTex.repeat.set(200 / 2.0, 1);
  const dashMat = new THREE.MeshBasicMaterial({ map: dashTex, transparent: true, opacity: 0.32, depthWrite: false });
  [-4.6, 4.6].forEach(dz => {
    const ds = new THREE.Mesh(new THREE.PlaneGeometry(200, 0.14).rotateX(-Math.PI / 2), dashMat);
    ds.position.set(28, 0.02, dz);
    factoryWorld.add(ds);
  });

  /* ---------- Studio Physically-Based Materials ---------- */
  const matSteel = new THREE.MeshStandardMaterial({ color: 0xc9d4d4, roughness: 0.42, metalness: 0.5, envMapIntensity: 0.5 });
  const matChrome = new THREE.MeshStandardMaterial({ color: 0xdfe8e8, roughness: 0.3, metalness: 0.65, envMapIntensity: 0.6 });
  const matAnodized = new THREE.MeshStandardMaterial({ color: 0x13353f, roughness: 0.36, metalness: 0.35, envMapIntensity: 0.6, side: THREE.DoubleSide });
  const matChassis = new THREE.MeshStandardMaterial({ color: 0xc3cfcf, roughness: 0.48, metalness: 0.12, envMapIntensity: 0.3, side: THREE.DoubleSide });
  const matTeal = new THREE.MeshStandardMaterial({ color: 0x1d5560, roughness: 0.5, metalness: 0.08, envMapIntensity: 0.35 });
  const matDeepTeal = new THREE.MeshStandardMaterial({ color: 0x0b2730, roughness: 0.45, envMapIntensity: 0.3 });
  const matSpineBed = new THREE.MeshPhysicalMaterial({ color: 0x184954, roughness: 0.2, clearcoat: 0.9, clearcoatRoughness: 0.08, envMapIntensity: 0.8, side: THREE.DoubleSide });
  const matBridgeBed = new THREE.MeshPhysicalMaterial({ color: 0xd98a2b, roughness: 0.24, clearcoat: 0.85, envMapIntensity: 0.75, side: THREE.DoubleSide });
  const matGold = new THREE.MeshStandardMaterial({ color: 0xefb85c, emissive: 0x9e6312, emissiveIntensity: 0.32, roughness: 0.22, metalness: 0.45 });
  const matCoral = new THREE.MeshStandardMaterial({ color: 0x163d47, emissive: 0x0b2228, emissiveIntensity: 0.25, roughness: 0.34, side: THREE.DoubleSide });
  const matMintGlow = new THREE.MeshStandardMaterial({ color: 0x5de2b0, emissive: 0x1b9c72, emissiveIntensity: 0.9, roughness: 0.2 });

  const glassOuterMat = new THREE.MeshPhysicalMaterial({
    color: 0xf0faf8,
    transparent: true,
    opacity: 0.30,
    roughness: 0.03,
    metalness: 0.04,
    clearcoat: 1.0,
    clearcoatRoughness: 0.02,
    reflectivity: 1.0,
    envMapIntensity: 1.85,
    depthWrite: false
  });
  const glassRimMat = new THREE.MeshPhysicalMaterial({
    color: 0xcbe8e4,
    transparent: true,
    opacity: 0.18,
    roughness: 0.06,
    envMapIntensity: 1.2,
    side: THREE.BackSide,
    depthWrite: false
  });

  /* ---------- Geometry Helpers ---------- */
  function sweepChannel(curve, hw, h0, h1, segs, material) {
    const pos = [], idx = [], uvs = [];
    const len = curve.getLength();
    const profile = [[[-1, h1], [1, h1]], [[1, h1], [1, h0]], [[-1, h0], [-1, h1]]];
    for (let i = 0; i <= segs; i++) {
      const t = i / segs;
      const p = curve.getPointAt(t);
      const tan = curve.getTangentAt(t);
      const lenXZ = Math.hypot(tan.x, tan.z) || 1;
      const nx = -tan.z / lenXZ, nz = tan.x / lenXZ;
      profile.forEach(face => face.forEach(([s, dy]) => {
        pos.push(p.x + nx * s * hw, p.y + dy, p.z + nz * s * hw);
        uvs.push(t * len, (s + 1) / 2);
      }));
    }
    for (let i = 0; i < segs; i++) {
      for (let f = 0; f < 3; f++) {
        const a = i * 6 + f * 2, b = a + 6;
        idx.push(a, a + 1, b, a + 1, b + 1, b);
      }
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    geo.setIndex(idx);
    geo.computeVertexNormals();
    const m = new THREE.Mesh(geo, material);
    m.castShadow = !MOB;
    m.receiveShadow = true;
    return m;
  }

  const addBox = (w, h, d, m, x, y, z, par) => {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), m);
    mesh.position.set(x, y, z);
    mesh.castShadow = !MOB;
    mesh.receiveShadow = true;
    (par || factoryWorld).add(mesh);
    return mesh;
  };
  const addCyl = (rt, rb, h, m, x, y, z, par, segs = 24) => {
    const mesh = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, segs), m);
    mesh.position.set(x, y, z);
    mesh.castShadow = !MOB;
    mesh.receiveShadow = true;
    (par || factoryWorld).add(mesh);
    return mesh;
  };

  /* ---------- Ultra-Smooth Cosine-Filleted Boston-Round Vessel ---------- */
    /* ---------- Boston-round bottle, redrawn on the illustrated line's proportions ---------- */
  // 1 bottle unit = 100 drawing units of the line: body radius .34, straight to y .98, shoulder to a .13 neck at y 1.30, neck to 1.50.
  // A 28-unit cap (r .17) sits from 1.28 to 1.56, so the finished bottle is BOT_H = 1.56 tall and 2.2 times as tall as wide.
  const BOT_H = 1.56;
  const vesselPts = [new THREE.Vector2(0, 0), new THREE.Vector2(0.25, 0)];
  for (let i = 1; i <= 6; i++) { const a = i / 6 * Math.PI / 2; vesselPts.push(new THREE.Vector2(0.25 + Math.sin(a) * 0.09, 0.09 - Math.cos(a) * 0.09)); }
  vesselPts.push(new THREE.Vector2(0.34, 0.98));
  const bez = (t, a, b, c, d) => { const u = 1 - t; return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d; };
  for (let i = 1; i <= 16; i++) { const t = i / 16; vesselPts.push(new THREE.Vector2(bez(t, 0.34, 0.34, 0.20, 0.13), bez(t, 0.98, 1.17, 1.23, 1.30))); }
  vesselPts.push(new THREE.Vector2(0.13, 1.50), new THREE.Vector2(0.105, 1.50));
  const vesselGeo = new THREE.LatheGeometry(vesselPts, 48);
  // Liquid fills to the line's fill mark (.92) and shades from honey at the surface to deep amber at the base.
  const liqUnitGeo = new THREE.CylinderGeometry(0.31, 0.31, 0.89, 32).translate(0, 0.445, 0);
  {
    const p = liqUnitGeo.attributes.position, col = new Float32Array(p.count * 3);
    for (let i = 0; i < p.count; i++) { const k = 0.66 + 0.40 * (p.getY(i) / 0.89); col[i * 3] = k; col[i * 3 + 1] = k; col[i * 3 + 2] = k; }
    liqUnitGeo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  }
  const capUnitGeo = new THREE.CylinderGeometry(0.17, 0.17, 0.28, 24).translate(0, 1.42, 0);
  const capBandGeo = new THREE.CylinderGeometry(0.174, 0.174, 0.045, 24).translate(0, 1.5375, 0);
  const LABEL_R = 0.348;
  const labelGeo = new THREE.CylinderGeometry(LABEL_R, LABEL_R, 0.64, 40, 1, true, -LABEL_ARC / 2, LABEL_ARC).translate(0, 0.5, 0);
  const labelStripGeo = new THREE.CylinderGeometry(LABEL_R, LABEL_R, 0.64, 24, 1, true, -1.35, 2.7).translate(0, 0.5, 0);
  const labelBandGeo = new THREE.CylinderGeometry(LABEL_R + 0.002, LABEL_R + 0.002, 0.09, 24, 1, true, -1.35, 2.7).translate(0, 0.775, 0);

  // Glass: clear, with the dark ink edge the line draws around every bottle, made as a rim shader because the glass is transparent.
  const makeBottleGlass = (rimEnd, opacity) => {
    const m = new THREE.MeshPhysicalMaterial({ color: 0xd6eee9, transparent: true, opacity, roughness: 0.1, metalness: 0, clearcoat: 0.6, clearcoatRoughness: 0.12, envMapIntensity: 0.55, depthWrite: false });
    m.onBeforeCompile = sh => {
      sh.uniforms.uRimEnd = { value: rimEnd };
      sh.fragmentShader = 'uniform float uRimEnd;\n' + sh.fragmentShader.replace('#include <opaque_fragment>', `
        float ndv = abs(dot(normalize(normal), normalize(vViewPosition)));
        float rimK = 1.0 - smoothstep(0.0, uRimEnd, ndv);
        outgoingLight = mix(outgoingLight, vec3(0.039, 0.122, 0.149), rimK * 0.9);
        diffuseColor.a = mix(diffuseColor.a, 0.97, rimK);
        #include <opaque_fragment>`);
    };
    return m;
  };
  // Opaque parts get a real ink outline: an inverted hull pushed out by a constant number of screen pixels.
  const outlineK = { value: 0.002 };           // world units per pixel per unit of distance, set from the camera each frame
  const makeOutlineMat = pxWidth => {
    const m = new THREE.MeshBasicMaterial({ color: 0x0a1f26, side: THREE.BackSide });
    m.onBeforeCompile = sh => {
      sh.uniforms.uK = outlineK; sh.uniforms.uPx = { value: pxWidth };
      sh.vertexShader = 'uniform float uK; uniform float uPx;\n' + sh.vertexShader
        .replace('#include <begin_vertex>', `
          #ifdef USE_INSTANCING
            mat4 om = modelMatrix * instanceMatrix;
          #else
            mat4 om = modelMatrix;
          #endif
          float oSc = length(om[0].xyz);
          float oDist = max(0.2, -(viewMatrix * om * vec4(0.0, 0.0, 0.0, 1.0)).z);
          vec3 transformed = position + normalize(normal) * (uPx * uK * oDist / max(oSc, 1e-5));`)
        .replace('#include <project_vertex>', `
          #include <project_vertex>
          if (oSc < 0.02) gl_Position = vec4(2.0, 2.0, 2.0, 1.0);`);
    };
    return m;
  };
  const capOutlineGeo = new THREE.CylinderGeometry(0.17, 0.17, 0.28, 24).translate(0, 1.42, 0);
  const TRAFFIC_SCALE = 0.82;

  /* =========================================================================
     ZONE 0: LINE 04 — PRECISION ANODIZED & CHROME RAIL (x = -72 .. +128)
     ========================================================================= */
  const BELT_TILE = 1.6;
  const beltTexBase = makeBeltTexture();
  const makeBedTex = () => { const t = beltTexBase.clone(); t.needsUpdate = true; t.repeat.set(1 / BELT_TILE, 1); return t; };
  const line04BedTex = makeBedTex();
  const line04BedMat = new THREE.MeshStandardMaterial({ color: 0x1b5560, map: line04BedTex, roughness: 0.55, metalness: 0.1, side: THREE.DoubleSide });
  const LINE04_END = 117.6;                       // the spine belt ends at cartoner K-06's infeed stop
  const line04Curve = new THREE.LineCurve3(new THREE.Vector3(-72, 0, 0), new THREE.Vector3(LINE04_END, 0, 0));
  factoryWorld.add(
    sweepChannel(line04Curve, 1.16, 0.62, 0.93, 160, new THREE.MeshStandardMaterial({ color: 0xb4c0c1, roughness: 0.42, metalness: 0.25, envMapIntensity: 0.4, side: THREE.DoubleSide })),
    sweepChannel(line04Curve, 1.19, 0.91, 0.955, 160, matChrome),
    sweepChannel(line04Curve, 0.95, 0.945, 1.00, 160, line04BedMat)
  );
  const line04BusMat = new THREE.MeshStandardMaterial({ color: 0xefb85c, emissive: 0xd98a2b, emissiveIntensity: 0.72, roughness: 0.25 });
  factoryWorld.add(sweepChannel(line04Curve, 0.075, 0.996, 1.018, 160, line04BusMat));

  // Brushed stainless twin guide-rails along Line 04
  [-0.86].forEach(zSide => {
    const railLen = LINE04_END - 0.4 + 71, railMid = (LINE04_END - 0.4 - 71) / 2;
    const railLow = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, railLen, 14), matChrome);
    railLow.rotation.z = Math.PI / 2;
    railLow.position.set(railMid, 1.32, zSide);
    factoryWorld.add(railLow);

    const railHigh = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, railLen, 14), matSteel);
    railHigh.rotation.z = Math.PI / 2;
    railHigh.position.set(railMid, 1.62, zSide);
    factoryWorld.add(railHigh);
  });

  // Precision spoked drive wheels & stanchions underneath Line 04
  const stanchionPositions = [];
  for (let i = 0; i < 82; i++) { const sx = -66 + i * 2.35; if (sx < LINE04_END - 0.8) stanchionPositions.push(sx); }
  const wheelCount = stanchionPositions.length;
  const wheelGeo = new THREE.CylinderGeometry(0.23, 0.23, 0.14, 22);
  wheelGeo.rotateX(Math.PI / 2);
  const wheelRimMat = new THREE.MeshStandardMaterial({ color: 0x163d47, roughness: 0.3, metalness: 0.55 });
  const wheelsInst = new THREE.InstancedMesh(wheelGeo, wheelRimMat, wheelCount);
  const hubInst = new THREE.InstancedMesh(new THREE.BoxGeometry(0.41, 0.038, 0.16), matChrome, wheelCount);


  const stanchionInst = new THREE.InstancedMesh(new THREE.BoxGeometry(0.055, 0.70, 0.055), matChrome, stanchionPositions.length);
  const dummy = new THREE.Object3D();
  let stIdx = 0;
  stanchionPositions.forEach(sx => {
    [-0.86].forEach(sz => {
      dummy.position.set(sx, 1.30, sz);
      dummy.rotation.set(0, 0, 0);
      dummy.scale.set(1, 1, 1);
      dummy.updateMatrix();
      stanchionInst.setMatrixAt(stIdx++, dummy.matrix);
    });
  });
  factoryWorld.add(stanchionInst, wheelsInst, hubInst);

  // Cleanroom architectural observation windows behind Beat 0 — spaced cleanly around x = -38!
  [-44.5, -31.5].forEach(wx => {
    addBox(3.4, 6.4, 0.12, matDeepTeal, wx, 4.6, -6.4);
    const pane = new THREE.Mesh(
      new THREE.PlaneGeometry(2.9, 5.8),
      new THREE.MeshBasicMaterial({ color: 0x15404b, transparent: true, opacity: 0.32 })
    );
    pane.position.set(wx, 4.6, -6.32);
    factoryWorld.add(pane);
  });

  // Companion empty glass vessel peeking subtly on far right edge of Beat 0
  const originCompanion = new THREE.Group();
  const ocOuter = new THREE.Mesh(vesselGeo, glassOuterMat);
  const ocInner = new THREE.Mesh(vesselGeo, glassRimMat);
  ocInner.scale.setScalar(0.95);
  originCompanion.add(ocInner, ocOuter);
  originCompanion.scale.setScalar(2.12);
  originCompanion.position.set(-34.15, 1.0, 0);
  factoryWorld.add(originCompanion);

  /* =========================================================================
     HERO SPECIMEN: LOT TQ-0417
     ========================================================================= */
    const HERO_SCALE = 1.4;
  const HERO_H = BOT_H * HERO_SCALE;
  const heroBottle = new THREE.Group();

  const hbOuter = new THREE.Mesh(vesselGeo, makeBottleGlass(0.34, 0.2));
  hbOuter.castShadow = false;
  hbOuter.renderOrder = 3;
  heroBottle.add(hbOuter);

  // Frosted laser-etched TQ-0417 base mark
  const etchMesh = new THREE.Mesh(
    new THREE.CylinderGeometry(0.343, 0.343, 0.10, 32, 1, true, -0.52, 1.04),
    new THREE.MeshBasicMaterial({ map: makeEtchTexture(), transparent: true, depthWrite: false })
  );
  etchMesh.position.y = 0.085;
  etchMesh.renderOrder = 4;
  etchMesh.visible = false;
  heroBottle.add(etchMesh);

  // Amber oral solution inside TQ-0417 (same honey-to-amber shading as the line)
  const heroLiqMat = new THREE.MeshStandardMaterial({ color: 0xe89c2c, vertexColors: true, emissive: 0x6b3a05, emissiveIntensity: 0.35, roughness: 0.3 });
  const heroLiq = new THREE.Mesh(liqUnitGeo, heroLiqMat);
  heroLiq.position.y = 0.03;
  heroLiq.scale.set(1, 0.0001, 1);
  heroLiq.visible = false;
  heroLiq.renderOrder = 1;
  heroBottle.add(heroLiq);

  const meniscusDisc = new THREE.Mesh(
    new THREE.CircleGeometry(0.309, 36),
    new THREE.MeshBasicMaterial({ color: 0xf6c773, transparent: true, opacity: 0.85, side: THREE.DoubleSide })
  );
  meniscusDisc.rotation.x = -Math.PI / 2;
  meniscusDisc.visible = false;
  meniscusDisc.renderOrder = 2;
  heroBottle.add(meniscusDisc);

  const dosingBubbles = Array.from({ length: 14 }, (_, bi) => {
    const bm = new THREE.Mesh(
      new THREE.SphereGeometry(0.012 + (bi % 3) * 0.005, 8, 8),
      new THREE.MeshBasicMaterial({ color: 0xfff0c2, transparent: true, opacity: 0.75 })
    );
    bm.visible = false;
    bm.renderOrder = 2;
    heroBottle.add(bm);
    return bm;
  });

  // Child-resistant cap as drawn on the line: teal body, ivory top band, darker tamper ring, light ribs, ink outline.
  // The group is centred on the cap; the render loop slides it down onto the neck.
  const heroCapGrp = new THREE.Group();
  const capBody = new THREE.Mesh(new THREE.CylinderGeometry(0.17, 0.17, 0.28, 32), new THREE.MeshStandardMaterial({ color: 0x1f5a64, roughness: 0.5, metalness: 0.05 }));
  const capTop = new THREE.Mesh(new THREE.CylinderGeometry(0.174, 0.174, 0.045, 32), new THREE.MeshStandardMaterial({ color: 0xf4eee2, roughness: 0.45 }));
  capTop.position.y = 0.1175;
  const tamperBand = new THREE.Mesh(new THREE.CylinderGeometry(0.173, 0.173, 0.05, 32), new THREE.MeshStandardMaterial({ color: 0x123f47, roughness: 0.55 }));
  tamperBand.position.y = -0.115;
  const capOutline = new THREE.Mesh(new THREE.CylinderGeometry(0.17, 0.17, 0.28, 32), makeOutlineMat(1.7));
  heroCapGrp.add(capBody, capTop, tamperBand, capOutline);
  for (let ri = 0; ri < 18; ri++) {
    const a = (ri / 18) * Math.PI * 2;
    const rib = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.15, 0.012), new THREE.MeshStandardMaterial({ color: 0x2f7a80, roughness: 0.5 }));
    rib.position.set(Math.sin(a) * 0.171, -0.005, Math.cos(a) * 0.171);
    rib.rotation.y = a;
    heroCapGrp.add(rib);
  }
  heroCapGrp.visible = false;
  heroBottle.add(heroCapGrp);

  // Wrapped label, the same artwork as the line's label
  const heroLabelMat = new THREE.MeshStandardMaterial({ map: makeLabelTexture(), roughness: 0.55, side: THREE.DoubleSide });
  const heroLabel = new THREE.Mesh(labelGeo, heroLabelMat);
  heroLabel.position.y = 0;
  heroLabel.visible = false;
  heroLabel.renderOrder = 4;
  heroBottle.add(heroLabel);

  // Tracking halo and glow on the belt, the same honey ring the line draws under TQ-0417
  const heroHaloMat = new THREE.MeshBasicMaterial({ color: 0xefb85c, transparent: true, opacity: 0.25, depthWrite: false });
  const heroHalo = new THREE.Mesh(new THREE.RingGeometry(0.47, 0.53, 64), heroHaloMat);
  heroHalo.rotation.x = -Math.PI / 2;
  heroHalo.position.y = 0.012;
  heroBottle.add(heroHalo);
  const heroGlowMat = new THREE.MeshBasicMaterial({ map: makeGlowTexture(), transparent: true, opacity: 0.0, depthWrite: false, blending: THREE.AdditiveBlending });
  const heroGlow = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 2.6).rotateX(-Math.PI / 2), heroGlowMat);
  heroGlow.position.y = 0.01;
  heroBottle.add(heroGlow);
  const shadowTex = makeShadowTexture();
  const heroShadow = new THREE.Mesh(new THREE.PlaneGeometry(1.05, 1.05).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: shadowTex, transparent: true, opacity: 0.6, depthWrite: false }));
  heroShadow.position.y = 0.008;
  heroBottle.add(heroShadow);

  const heroGlowLight = new THREE.PointLight(0xffe5b0, 0.9, 9);
  heroGlowLight.position.set(0.8, 0.85, 1.1);
  heroBottle.add(heroGlowLight);

  heroBottle.scale.setScalar(HERO_SCALE);
  factoryWorld.add(heroBottle);

  // Ambient studio bokeh motes
  const moteCount = 85;
  const motePos = new Float32Array(moteCount * 3);
  for (let i = 0; i < moteCount; i++) {
    motePos[i * 3] = -44 + Math.random() * 168;
    motePos[i * 3 + 1] = 1.2 + Math.random() * 6.2;
    motePos[i * 3 + 2] = (Math.random() - 0.5) * 14;
  }
  const moteGeo = new THREE.BufferGeometry();
  moteGeo.setAttribute('position', new THREE.BufferAttribute(motePos, 3));
  const moteMat = new THREE.PointsMaterial({ color: 0xffe4ad, size: 0.08, transparent: true, opacity: 0.38, depthWrite: false });
  factoryWorld.add(new THREE.Points(moteGeo, moteMat));

  /* =========================================================================
     STATION 01 (x = -13.5): 3 SUPPLIER FEEDER STREAMS & TWIN-BRIDGE 4-TOWER GATE
     ========================================================================= */
  const mkCurve = (pts, closed = false) =>
    new THREE.CatmullRomCurve3(pts.map(([x, y, z]) => new THREE.Vector3(x, y, z)), closed, 'centripetal');

  const feederCurves = [
    mkCurve([[-28.5, 1.5, -11.5], [-23, 1.0, -7.2], [-18.5, 0.35, -2.4], [-15.4, 0.02, -0.55]]),
    mkCurve([[-28.5, 0.9, -5.2],  [-23, 0.6, -3.4], [-18.5, 0.20, -1.3], [-15.4, 0.01, -0.20]]),
    mkCurve([[-28.5, 1.3, 9.2],   [-23, 0.85, 5.6], [-18.5, 0.28, 1.9],  [-15.4, 0.02, 0.45]])
  ];
  const quarantineSpur = mkCurve([[-22.5, 0.2, -7.0], [-19.5, 0.1, -10.2], [-15.0, 0.1, -11.4]]);

  [[-28.6, 1.5, -11.5], [-28.6, 0.9, -5.2], [-28.6, 1.3, 9.2]].forEach(([px, py, pz]) => {
    addBox(0.9, py + 2.8, 2.8, matDeepTeal, px, (py + 2.8) * 0.5, pz);
    addBox(0.96, 0.22, 2.9, matGold, px, py + 2.82, pz);
  });

  const feederBusMats = feederCurves.map((_, fi) =>
    new THREE.MeshStandardMaterial({
      color: fi === 0 ? 0xefb85c : 0x5de2b0,
      emissive: fi === 0 ? 0xd98a2b : 0x1b9c72,
      emissiveIntensity: fi === 0 ? 1.15 : 0.5,
      roughness: 0.25
    })
  );

  feederCurves.forEach((fc, fi) => {
    factoryWorld.add(
      sweepChannel(fc, 0.76, 0.58, 0.89, 48, matAnodized),
      sweepChannel(fc, 0.78, 0.88, 0.91, 48, matChrome),
      sweepChannel(fc, 0.60, 0.90, 0.97, 48, matSpineBed),
      sweepChannel(fc, 0.08, 0.965, 0.99, 48, feederBusMats[fi])
    );
    for (let s = 0.22; s < 0.9; s += 0.26) {
      const p = fc.getPointAt(s);
      addCyl(0.065, 0.075, p.y + 0.6, matSteel, p.x, (p.y + 0.6) * 0.5, p.z, factoryWorld, 12);
    }
  });
  factoryWorld.add(
    sweepChannel(quarantineSpur, 0.66, 0.58, 0.89, 32, matAnodized),
    sweepChannel(quarantineSpur, 0.52, 0.88, 0.96, 32, matCoral)
  );

  // Structural Twin-Bridge 4-Tower Governance Portal (grounded directly onto all 4 towers!)
  const gateTowers = [[-15.2, -2.6], [-11.8, -2.6], [-15.2, 2.6], [-11.8, 2.6]].map(([tx, tz]) => {
    addCyl(0.46, 0.52, 4.4, matChassis, tx, 2.2, tz, factoryWorld, 24);
    addCyl(0.52, 0.52, 0.26, matTeal, tx, 4.45, tz, factoryWorld, 24);
    const crownMat = new THREE.MeshStandardMaterial({ color: 0x5de2b0, emissive: 0x1ea87a, emissiveIntensity: 0.9, roughness: 0.2 });
    addCyl(0.32, 0.32, 0.48, crownMat, tx, 4.82, tz, factoryWorld, 20);
    const ring = new THREE.Mesh(new THREE.TorusGeometry(0.58, 0.032, 8, 32), matGold);
    ring.rotation.x = Math.PI / 2;
    ring.position.set(tx, 4.82, tz);
    factoryWorld.add(ring);
    return crownMat;
  });
  // Cross-lintels directly spanning each pair of towers + longitudinal truss beams
  [-15.2, -11.8].forEach(bx => {
    addBox(0.42, 0.32, 5.6, matTeal, bx, 4.35, 0);
    addBox(0.46, 0.12, 5.65, matGold, bx, 4.54, 0);
  });
  [-2.6, 2.6].forEach(bz => addBox(3.8, 0.24, 0.32, matChrome, -13.5, 4.35, bz));

  /* =========================================================================
     STATION 02 (x = +8.0): STAINLESS FORMULATION VESSEL & CNC DIVING NOZZLE
     ========================================================================= */
  const stFill = new THREE.Group();
  stFill.position.set(8.0, 0, 0);
  factoryWorld.add(stFill);

  // Polished 316L Stainless Jacketed Formulation Tank
  addCyl(1.42, 1.42, 3.2, matSteel, -1.9, 3.2, -3.25, stFill, 36);
  addCyl(1.46, 1.46, 0.14, matChrome, -1.9, 4.75, -3.25, stFill, 36);
  addCyl(1.46, 1.46, 0.14, matChrome, -1.9, 1.65, -3.25, stFill, 36);
  const coneBot = new THREE.Mesh(new THREE.ConeGeometry(1.42, 0.95, 36), matSteel);
  coneBot.rotation.x = Math.PI;
  coneBot.position.set(-1.9, 1.12, -3.25);
  stFill.add(coneBot);
  const domeTop = new THREE.Mesh(new THREE.SphereGeometry(1.42, 36, 18, 0, Math.PI * 2, 0, Math.PI * 0.5), matChrome);
  domeTop.position.set(-1.9, 4.8, -3.25);
  stFill.add(domeTop);
  [[-2.85, -2.45], [-0.95, -2.45], [-2.85, -4.05], [-0.95, -4.05]].forEach(([lx, lz]) => {
    addCyl(0.08, 0.09, 2.2, matChrome, lx, 1.1, lz, stFill, 14);
  });
  // Recessed stainless sight-glass bezel + glowing amber column
  addBox(0.32, 2.28, 0.14, matAnodized, -1.9, 3.2, -1.82, stFill);
  addBox(0.14, 2.08, 0.16, heroLiqMat, -1.9, 3.2, -1.81, stFill);

  // Sanitary overhead transfer pipe
  const pipeCurve = mkCurve([[-1.9, 5.45, -3.25], [-0.9, 5.75, -1.6], [0, 5.45, 0], [0, 4.65, 0]]);
  stFill.add(new THREE.Mesh(new THREE.TubeGeometry(pipeCurve, 32, 0.068, 14, false), matChrome));

  // Twin vertical guide shafts + sculpted CNC anodized/chrome Diving Nozzle Head
  [-0.58, 0.58].forEach(gx => addCyl(0.04, 0.04, 3.2, matChrome, gx, 4.4, 0, stFill, 16));
  const nozzleHead = new THREE.Group();
  nozzleHead.position.set(0, 4.15, 0);
  stFill.add(nozzleHead);
  addBox(1.44, 0.34, 0.34, matTeal, 0, 0, 0, nozzleHead);
  addBox(1.48, 0.09, 0.38, matChrome, 0, 0.18, 0, nozzleHead);
  addBox(1.48, 0.09, 0.38, matChrome, 0, -0.18, 0, nozzleHead);
  [-0.58, 0.58].forEach(bx => {
    addCyl(0.09, 0.09, 0.48, matChrome, bx, 0, 0, nozzleHead, 16);
    addCyl(0.065, 0.065, 0.06, matGold, bx, 0, 0.19, nozzleHead, 14).rotation.x = Math.PI / 2;
  });
  addCyl(0.045, 0.032, 1.48, matChrome, 0, -0.84, 0, nozzleHead, 18);

  const pourStream = new THREE.Mesh(
    new THREE.CylinderGeometry(0.025, 0.032, 1.92, 14),
    new THREE.MeshBasicMaterial({ color: 0xf6c365, transparent: true, opacity: 0.94 })
  );
  pourStream.position.set(0, 2.04, 0);
  pourStream.visible = false;
  stFill.add(pourStream);

  // Optical meniscus sensor pod
  addBox(0.24, 0.32, 0.24, matAnodized, -1.15, 2.1, 0.65, stFill);
  addCyl(0.045, 0.045, 0.06, matMintGlow, -1.02, 2.1, 0.65, stFill, 12).rotation.z = Math.PI / 2;

  /* =========================================================================
     STATION 03 (x = +26.0): CAP FEEDER CHUTE, TORQUE SPINDLE & WRAP LABELLER
     ========================================================================= */
  const stSeal = new THREE.Group();
  stSeal.position.set(26.0, 0, 0);
  factoryWorld.add(stSeal);

  addCyl(0.95, 0.78, 0.55, matChrome, -2.4, 5.1, -0.8, stSeal, 32);
  const chuteCurve = mkCurve([[-2.0, 4.85, -0.6], [-1.1, 4.25, -0.3], [-0.25, 3.65, 0]]);
  stSeal.add(sweepChannel(chuteCurve, 0.2, -0.05, 0.08, 24, matChrome));
  for (let ci = 1; ci <= 5; ci++) {
    const cp = chuteCurve.getPointAt(ci / 6);
    addCyl(0.16, 0.16, 0.14, matTeal, cp.x, cp.y + 0.08, cp.z, stSeal, 18);
  }

  addCyl(0.11, 0.11, 2.2, matChrome, 0, 4.9, 0, stSeal, 20);
  const spindleChuck = new THREE.Group();
  spindleChuck.position.set(0, 3.65, 0);
  stSeal.add(spindleChuck);
  addCyl(0.32, 0.28, 0.42, matTeal, 0, 0.22, 0, spindleChuck, 24);
  addCyl(0.23, 0.23, 0.18, matGold, 0, -0.05, 0, spindleChuck, 22);

  // Rotary wrap-around label web & peel roller
  addBox(1.85, 2.65, 0.46, matAnodized, 0.72, 2.12, -1.68, stSeal);
  const labelReel = addCyl(0.56, 0.56, 0.36, matChassis, 1.02, 2.68, -1.34, stSeal, 32);
  labelReel.rotation.x = Math.PI / 2;
  addCyl(0.17, 0.17, 0.40, matTeal, 1.02, 2.68, -1.34, stSeal, 20).rotation.x = Math.PI / 2;
  addCyl(0.11, 0.11, 0.74, matChrome, 0.52, 1.78, -0.82, stSeal, 18);
  // Taut paper label web strip running from reel to peel roller!
  const labelWeb = new THREE.Mesh(
    new THREE.PlaneGeometry(1.05, 0.62),
    new THREE.MeshStandardMaterial({ color: 0xfcf9f2, roughness: 0.4, side: THREE.DoubleSide })
  );
  labelWeb.position.set(0.76, 2.18, -1.06);
  labelWeb.rotation.set(0.45, 0.42, 0.35);
  stSeal.add(labelWeb);

  /* =========================================================================
     STATION 04 (x = +44.0): DARK-FIELD 5-CHECK VISION ARCH & LIVE HUD SCREEN
     ========================================================================= */
  const stQC = new THREE.Group();
  stQC.position.set(44.0, 0, 0);
  factoryWorld.add(stQC);

  addBox(3.55, 4.15, 0.36, matChrome, 0, 2.48, -1.28, stQC);
  addBox(3.05, 3.60, 0.38, matDeepTeal, 0, 2.48, -1.24, stQC);
  // Crisp technical HUD backplane with live calibration brackets!
  const hudScreen = new THREE.Mesh(
    new THREE.PlaneGeometry(2.82, 3.32),
    new THREE.MeshBasicMaterial({ map: makeVisionHudTexture(), transparent: true, opacity: 0.92 })
  );
  hudScreen.position.set(0, 2.48, -1.03);
  stQC.add(hudScreen);

  const qcHalo = new THREE.Mesh(new THREE.TorusGeometry(1.18, 0.055, 10, 48), matGold);
  qcHalo.rotation.x = Math.PI / 2;
  qcHalo.position.set(0, 4.30, 0);
  stQC.add(qcHalo);
  [-1.62, 1.62].forEach(cx => {
    addBox(0.32, 0.32, 0.48, matChrome, cx, 3.55, 0.85, stQC);
    addCyl(0.1, 0.1, 0.12, matMintGlow, cx, 3.55, 0.58, stQC, 14).rotation.x = Math.PI / 2;
  });

  const laserSheetMat = new THREE.MeshBasicMaterial({
    color: 0x5de2b0,
    transparent: true,
    opacity: 0,
    side: THREE.DoubleSide,
    depthWrite: false
  });
  const laserSheet = new THREE.Mesh(new THREE.PlaneGeometry(2.25, 2.25), laserSheetMat);
  laserSheet.rotation.x = -Math.PI / 2;
  laserSheet.position.set(0, 2.8, 0);
  stQC.add(laserSheet);

  // Reject spur + defective unlabelled bottle
  const rejectSpurCurve = mkCurve([[45.8, 0, 0.3], [47.8, 0, 1.8], [50.8, 0, 4.6], [53.5, 0, 5.4]]);
  // (the reject spur is not built: it would cross the cleanroom handoff framing)
  const rejectBottle = new THREE.Group();
  const rbGlass = new THREE.Mesh(vesselGeo, glassOuterMat);
  const rbLowLiq = new THREE.Mesh(liqUnitGeo, new THREE.MeshStandardMaterial({ color: 0xd98a2b, roughness: 0.3 }));
  rbLowLiq.scale.set(0.96, 0.40, 0.96);
  rejectBottle.add(rbGlass, rbLowLiq);
  rejectBottle.scale.setScalar(HERO_SCALE * 0.92);
  rejectBottle.visible = false;
  factoryWorld.add(rejectBottle);

  /* =========================================================================
     STATION 05 (x = +57 .. +126): THE 12-RAIL SUNLIT CLEANROOM ATRIUM
     Dual-sided West Airlock Wall: dark cleanroom cladding on west, bright white on east!
     ========================================================================= */
  // The cleanroom's west partition stands behind Line 04 only. Its south half and the header over the line used to sit
  // beside the handoff camera (a black slab in the crossfade) and across the crane-back, so the line enters through an
  // open pass-through, marked by a honey threshold stripe on the floor.
  addBox(0.45, 13, 20, matDeepTeal, 56.25, 6.5, -13.5);
  addBox(0.55, 13, 20, matChassis, 56.8, 6.5, -13.5);
  addBox(0.66, 5.8, 0.12, matGold, 56.55, 2.9, -3.44);
  addBox(0.9, 0.02, 7.0, new THREE.MeshStandardMaterial({ color: 0xefb85c, roughness: 0.5, emissive: 0x6b4a14, emissiveIntensity: 0.3 }), 56.55, 0.02, 0);

  // East Golden Dispatch Threshold Wall (x = 126)
  addBox(1.2, 16, 28, matChassis, 126.5, 8, -17.5);
  addBox(1.2, 16, 28, matChassis, 126.5, 8, 17.5);
  addBox(1.2, 10, 7.2, matChassis, 126.5, 11, 0);
  const portalGlowMat = new THREE.MeshBasicMaterial({ color: 0xfff5dc, side: THREE.DoubleSide, fog: false });
  const portalGlow = new THREE.Mesh(new THREE.PlaneGeometry(40, 22), portalGlowMat);
  portalGlow.rotation.y = -Math.PI / 2;
  portalGlow.position.set(150, 10, 0);
  factoryWorld.add(portalGlow);

  // 8 Atrium Conveyor Rails + Line 04 + 3 Glass Sky-Tubes = 12 Synchronized Atrium Lines
  // Each wave starts inside a filling monoblock against the west wall and ends inside a cartoner against the east wall.
  const CELL_W = 59.5, CELL_E = 123.5;
  const mkAtriumWave = (zBase, y0, amp, ph, dir) => {
    const pts = [];
    for (let k = 0; k <= 11; k++) {
      const x = CELL_W + (CELL_E - CELL_W) * k / 11;
      pts.push([x, y0, zBase + amp * Math.sin((x - 57) * 0.21 + ph)]);
    }
    return { c: mkCurve(pts), dir };
  };
  const mkAtriumLoop = (cx, cz, rx, rz, dir) => {
    const pts = [];
    for (let i = 0; i < 16; i++) {
      const a = (i / 16) * Math.PI * 2;
      pts.push([cx + rx * Math.cos(a), 0.12, cz + rz * Math.sin(a)]);
    }
    return { c: mkCurve(pts, true), closed: true, dir };
  };

  const atriumDefs = [
    { ...mkAtriumWave(-8.2, 0.0, 2.2, 0.3, 1),    group: 'sterile', amber: false, hw: 0.98, speed: 1.05 },
    { ...mkAtriumWave(-15.5, 0.65, 2.6, 2.1, -1), group: 'sterile', amber: false, hw: 0.98, speed: 1.18 },
    { ...mkAtriumWave(8.2, 0.0, 2.2, 1.4, -1),    group: 'topical', amber: true,  hw: 0.98, speed: 1.02 },
    { ...mkAtriumWave(15.5, 0.65, 2.6, 3.0, 1),   group: 'topical', amber: true,  hw: 0.98, speed: 1.15 },
    {
      c: mkCurve([[CELL_W, 2.5, 18.4], [76, 3.1, 8], [94, 3.2, -4], [112, 2.9, -14], [CELL_E, 2.5, -19.6]]),
      dir: 1, bridge: true, group: 'oral', amber: true, hw: 0.92, speed: 1.24
    },
    {
      c: mkCurve([[CELL_W, 2.5, -18.4], [76, 3.0, -9], [94, 3.2, 4], [112, 2.9, 14], [CELL_E, 2.5, 19.6]]),
      dir: -1, bridge: true, group: 'sterile', amber: false, hw: 0.92, speed: 1.20
    },
    { ...mkAtriumLoop(78, -11.8, 6.2, 3.8, 1),    group: 'sterile', amber: false, hw: 0.9, speed: 0.92 },
    { ...mkAtriumLoop(106, 11.8, 6.2, 3.8, -1),   group: 'topical', amber: true,  hw: 0.9, speed: 0.95 }
  ];

  const trafficRails = [
    ...feederCurves.map((fc, fi) => ({ c: fc, dir: 1, amber: fi === 1, feederIdx: fi, hw: 0.76, speed: 0.85, group: 'feeder' })),
    {
      c: new THREE.LineCurve3(new THREE.Vector3(58, 0, 0), new THREE.Vector3(LINE04_END - 0.2, 0, 0)),
      dir: 1, amber: true, hw: 1.1, speed: 1.0, group: 'oral', atriumSpine: true, skipMesh: true
    },
    ...atriumDefs
  ];

  const atriumBusMats = [];
  const atriumLegs = [];

  trafficRails.forEach(r => {
    r.len = r.c.getLength();
    r.n = Math.max(5, Math.floor(r.len / (MOB ? 4.8 : 3.1)));
    if (!r.skipMesh && r.group !== 'feeder') {
      const segs = Math.ceil(r.len * 1.8);
      const busMat = new THREE.MeshStandardMaterial({
        color: r.amber ? 0xefb85c : 0x5de2b0,
        emissive: r.amber ? 0xc57b1c : 0x1b9c72,
        emissiveIntensity: 0.45,
        roughness: 0.26
      });
      atriumBusMats.push({ group: r.group, mat: busMat, rail: r });

      r.bedTex = makeBedTex();
      const bedMat = new THREE.MeshStandardMaterial({ color: r.bridge ? 0xd98a2b : 0x1b5560, map: r.bedTex, roughness: 0.55, metalness: 0.1, side: THREE.DoubleSide });
      factoryWorld.add(
        sweepChannel(r.c, r.hw, 0.58, 0.90, segs, matChassis),
        sweepChannel(r.c, r.hw * 0.78, 0.89, 1.00, segs, bedMat),
        sweepChannel(r.c, 0.085, 0.995, 1.025, segs, busMat)
      );
      for (let d = 2.4; d < r.len; d += r.bridge ? 7.2 : 5.4) {
        const p = r.c.getPointAt(d / r.len);
        atriumLegs.push({ x: p.x, z: p.z, h: p.y + 0.62 });
      }
    }
  });

  const atriumPierInst = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.08, 0.095, 1, 10), matChassis, atriumLegs.length);
  atriumLegs.forEach((l, i) => {
    dummy.position.set(l.x, l.h * 0.5, l.z);
    dummy.scale.set(1, l.h, 1);
    dummy.rotation.set(0, 0, 0);
    dummy.updateMatrix();
    atriumPierInst.setMatrixAt(i, dummy.matrix);
  });
  factoryWorld.add(atriumPierInst);

  /* ---------- Machine cells: every belt has a machine at each end ---------- */
  // A cell is a stainless cabinet with a teal skin, a framed infeed/outfeed port with a strip curtain where the belt passes
  // through, an HMI, a nameplate and a stack light whose green lamp comes on when its line comes online.
  const cells = [];
  const matPort = new THREE.MeshStandardMaterial({ color: 0x061519, roughness: 0.9 });
  const matStrip = new THREE.MeshStandardMaterial({ color: 0xcfe6da, transparent: true, opacity: 0.32, roughness: 0.2, depthWrite: false, side: THREE.DoubleSide });
  const matScreen = new THREE.MeshStandardMaterial({ color: 0x0b1f25, emissive: 0x1b9c72, emissiveIntensity: 0.35, roughness: 0.3 });
  const matRedLamp = new THREE.MeshStandardMaterial({ color: 0x5a2a22, roughness: 0.4 });
  const matAmberLamp = new THREE.MeshStandardMaterial({ color: 0x5b4422, roughness: 0.4 });
  function addCell({ x, z, w, d, h, face, portY, portW = 2.1, portH = 1.85, plate, sub, power = -1 }) {
    const grp = new THREE.Group();
    grp.position.set(x, 0, z);
    factoryWorld.add(grp);
    addBox(w, 0.22, d, matDeepTeal, 0, 0.11, 0, grp);                         // plinth
    addBox(w, h - 0.42, d, matChassis, 0, 0.22 + (h - 0.42) / 2, 0, grp);      // stainless body
    addBox(w + 0.04, 0.2, d + 0.04, matTeal, 0, h - 0.1, 0, grp);              // teal top band
    addBox(w * 0.62, Math.min(1.1, h * 0.32), 0.04, matDeepTeal, -w * 0.08, h * 0.62, d / 2 + 0.02, grp);   // window
    // port where the belt passes through, on the face the belt arrives from
    if (face) {
      const px = face * (w / 2 + 0.03);
      addBox(0.06, portH, portW, matPort, px, portY, 0, grp);
      addBox(0.12, 0.12, portW + 0.24, matChrome, px + face * 0.03, portY + portH / 2 + 0.06, 0, grp);
      [-1, 1].forEach(sd => addBox(0.12, portH + 0.12, 0.12, matChrome, px + face * 0.03, portY, sd * (portW / 2 + 0.06), grp));
      for (let k = 0; k < 7; k++) addBox(0.02, portH * 0.82, portW / 7 - 0.03, matStrip, px + face * 0.1, portY + portH * 0.09, -portW / 2 + (k + 0.5) * portW / 7, grp);
    }
    // HMI on the front, nameplate on the top band, stack light on the corner
    addBox(0.62, 0.42, 0.06, matScreen, w * 0.3, h * 0.6, d / 2 + 0.03, grp);
    if (plate) {
      const pl = new THREE.Mesh(new THREE.PlaneGeometry(1.4, 0.35), new THREE.MeshBasicMaterial({ map: makePlateTexture(plate, sub), fog: true }));
      pl.position.set(-w * 0.1, h - 0.1, d / 2 + 0.035);
      grp.add(pl);
    }
    const sx = -face * (w / 2 - 0.2) || w / 2 - 0.2;
    addCyl(0.03, 0.03, 0.5, matChrome, sx, h + 0.25, d / 2 - 0.2, grp, 8);
    addCyl(0.07, 0.07, 0.12, matRedLamp, sx, h + 0.56, d / 2 - 0.2, grp, 12);
    addCyl(0.07, 0.07, 0.12, matAmberLamp, sx, h + 0.69, d / 2 - 0.2, grp, 12);
    const green = new THREE.MeshStandardMaterial({ color: 0x2b5a44, emissive: 0x5de2b0, emissiveIntensity: 0, roughness: 0.3 });
    addCyl(0.07, 0.07, 0.12, green, sx, h + 0.82, d / 2 - 0.2, grp, 12);
    cells.push({ green, power });
    return grp;
  }
  const kraftMat = new THREE.MeshStandardMaterial({ color: 0xc8a273, roughness: 0.8 });
  const tapeMat = new THREE.MeshStandardMaterial({ color: 0x1d5560, roughness: 0.6 });
  const palletMat = new THREE.MeshStandardMaterial({ color: 0xb8936a, roughness: 0.85 });
  function addPallet(x, z, layers) {
    addBox(1.3, 0.16, 1.1, palletMat, x, 0.08, z);
    for (let l = 0; l < layers; l++) for (let i = 0; i < 2; i++) for (let j = 0; j < 2; j++) {
      const cx = x - 0.31 + i * 0.62, cz = z - 0.26 + j * 0.52, cy = 0.16 + 0.21 + l * 0.42;
      addBox(0.6, 0.4, 0.5, kraftMat, cx, cy, cz);
      addBox(0.61, 0.05, 0.12, tapeMat, cx, cy + 0.2, cz);
    }
  }

  // Power-up order for the handoff: lines nearest the spine come online first, then outward.
  const LINE_ORDER = { 0: 0, 2: 1, 1: 3, 3: 4, 6: 7, 7: 8, 4: 9, 5: 10 };   // atriumDefs index -> step (sky-tubes take 2, 5, 6)
  atriumDefs.forEach((r, i) => {
    r.power = LINE_ORDER[i];
    if (r.closed) return;
    const p0 = r.c.getPointAt(0), p1 = r.c.getPointAt(1);
    const h = p0.y + 2.95, n = i + 1;
    addCell({ x: CELL_W - 0.4, z: p0.z, w: 3.8, d: r.bridge ? 2.2 : 2.6, h, face: 1, portY: p0.y + 1.45, portW: r.hw * 2 + 0.2, plate: `F-${String(n).padStart(2, '0')}`, sub: 'FILL · CAP · LABEL', power: r.power });
    addCell({ x: CELL_E + 0.3, z: p1.z, w: 4.0, d: r.bridge ? 2.2 : 2.6, h: p1.y + 2.95, face: -1, portY: p1.y + 1.45, portW: r.hw * 2 + 0.2, plate: `K-${String(n + 10).padStart(2, '0')}`, sub: 'CARTON · CASE', power: r.power });
  });
  // finished cases palletised beside the east cartoners
  addPallet(123.4, -11.2, 3);
  addPallet(123.4, 16.4, 2);
  addPallet(120.9, -11.2, 1);

  // The two closed loops are rotary accumulation tables: a turning table in the middle buffers bottles between machines.
  const accTables = [];
  atriumDefs.filter(r => r.closed).forEach((r, i) => {
    const bb = new THREE.Box3().setFromPoints(r.c.getPoints(32));
    const cx = (bb.min.x + bb.max.x) / 2, cz = (bb.min.z + bb.max.z) / 2;
    addCyl(0.5, 0.62, 0.9, matChassis, cx, 0.45, cz, factoryWorld, 20);
    const table = addCyl(2.3, 2.3, 0.1, matChrome, cx, 0.95, cz, factoryWorld, 40);
    addCyl(2.34, 2.34, 0.05, matGold, cx, 1.01, cz, factoryWorld, 40);
    for (let k = 0; k < 4; k++) { const sp = addBox(4.4, 0.03, 0.06, matSteel, cx, 1.03, cz, factoryWorld); sp.rotation.y = k * Math.PI / 4; table.userData.spokes = (table.userData.spokes || []).concat(sp); }
    const pl = new THREE.Mesh(new THREE.PlaneGeometry(1.4, 0.35), new THREE.MeshBasicMaterial({ map: makePlateTexture(`ACC-${i + 1}`, 'ROTARY BUFFER') }));
    pl.rotation.x = -Math.PI / 2; pl.position.set(cx, 1.06, cz + 1.2); factoryWorld.add(pl);
    accTables.push(table);
  });

    const totalTraffic = trafficRails.reduce((s, r) => s + r.n, 0);
  // A set of instanced bottles (glass, liquid, cap, ivory cap band, ink cap outline, label, label band, contact shadow)
  // that all share one matrix per bottle, so the belts carry the same bottle the line draws.
  const stripTex = makeLabelStripTexture();
  const contactShadowTex = makeShadowTexture();
  const mkBottleSet = count => {
    const mk = (geo, mat) => { const m = new THREE.InstancedMesh(geo, mat, count); m.frustumCulled = false; return m; };
    const set = {
      glass: mk(vesselGeo, makeBottleGlass(0.55, 0.22)),
      liq: mk(liqUnitGeo, new THREE.MeshStandardMaterial({ color: 0xffffff, vertexColors: true, roughness: 0.3, emissive: 0x2a1704, emissiveIntensity: 0.3 })),
      cap: mk(capUnitGeo, new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.5 })),
      band: mk(capBandGeo, new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.45 })),
      capLine: mk(capOutlineGeo, makeOutlineMat(1.2)),
      label: mk(labelStripGeo, new THREE.MeshStandardMaterial({ map: stripTex, roughness: 0.6, side: THREE.DoubleSide })),
      labelBand: mk(labelBandGeo, new THREE.MeshStandardMaterial({ color: 0x1d5560, roughness: 0.6, side: THREE.DoubleSide })),
      shadow: mk(new THREE.PlaneGeometry(1.05, 1.05).rotateX(-Math.PI / 2).translate(0, 0.014, 0), new THREE.MeshBasicMaterial({ map: contactShadowTex, transparent: true, opacity: 0.5, depthWrite: false }))
    };
    set.all = [set.glass, set.liq, set.cap, set.band, set.capLine, set.label, set.labelBand, set.shadow];
    set.glass.renderOrder = 3;
    set.put = (i, m) => { for (const x of set.all) x.setMatrixAt(i, m); };
    set.commit = () => { for (const x of set.all) x.instanceMatrix.needsUpdate = true; };
    set.color = (i, liq, cap, band) => { set.liq.setColorAt(i, liq); set.cap.setColorAt(i, cap); set.band.setColorAt(i, band); };
    set.commitColors = () => { set.liq.instanceColor.needsUpdate = true; set.cap.instanceColor.needsUpdate = true; set.band.instanceColor.needsUpdate = true; };
    set.addTo = grp => set.all.forEach(x => grp.add(x));
    return set;
  };
  const colAmber = new THREE.Color(0xf2a840), colTeal = new THREE.Color(0x46b3aa);
  const colCapTeal = new THREE.Color(0x1d5560), colCapIvory = new THREE.Color(0xf5efe3);
  const traffic = mkBottleSet(totalTraffic);
  let tCursor = 0;
  trafficRails.forEach(r => {
    for (let j = 0; j < r.n; j++) {
      // One product everywhere: amber Velmira, teal child-resistant cap, ivory cap band, the line's label.
      traffic.color(tCursor, colAmber, colCapTeal, colCapIvory);
      tCursor++;
    }
  });
  traffic.commitColors();
  traffic.addTo(factoryWorld);

  // The bottles queued beside TQ-0417 on Line 04, at the hero's own size and spacing: they are what the illustrated line shows at
  // the two ends of the cleanroom shot, and they fade into the cleanroom's wider traffic as the camera pulls back.
  const CONVOY_K = [-8, -7, -6, -5, -4, -3, -2, -1, 1, 3];
  const CONVOY_GAP = 1.96;
  const convoy = mkBottleSet(CONVOY_K.length);
  CONVOY_K.forEach((k, i) => convoy.color(i, colAmber, colCapTeal, colCapIvory));
  convoy.commitColors();
  convoy.addTo(factoryWorld);

  // 3 Overhead Clearcoat Glass Pneumatic Capsule Sky-Tubes
  const skyTubeMat = new THREE.MeshPhysicalMaterial({
    color: 0xffffff, transparent: true, opacity: 0.28, roughness: 0.04, clearcoat: 1.0, envMapIntensity: 1.4
  });
  // Each tube rises out of a capsule filler's hopper on the west side and drops into a blister packer's receiver near the cartoner.
  const SKY = [
    { src: 62.4, dst: 106.6, mid: [[69, 6.9, -9.5], [84, 7.0, -12], [98, 6.8, -7]] },
    { src: 66.6, dst: 110.8, mid: [[73, 7.3, 4.5], [88, 7.5, 9.5], [102, 7.2, 3]] },
    { src: 70.8, dst: 115.0, mid: [[77, 7.9, -1.5], [92, 8.1, 1.5], [106, 7.8, -0.5]] }
  ];
  const SKY_Z = -3.6, SKY_TOP = 3.42;
  const skyCurves = SKY.map(t => mkCurve([[t.src, SKY_TOP, SKY_Z], [t.src, SKY_TOP + 1.3, SKY_Z], ...t.mid, [t.dst, SKY_TOP + 1.3, SKY_Z], [t.dst, SKY_TOP, SKY_Z]]));
  skyCurves.forEach(sc => factoryWorld.add(new THREE.Mesh(new THREE.TubeGeometry(sc, 96, 0.28, 14, false), skyTubeMat)));
  SKY.forEach((t, i) => {
    const pw = [2, 5, 6][i];
    // capsule filler with its product hopper; the tube leaves from the hopper lid
    addCell({ x: t.src, z: SKY_Z, w: 2.4, d: 1.6, h: 2.4, face: 0, plate: `CF-0${i + 1}`, sub: 'CAPSULE FILL', power: pw });
    addCyl(0.5, 0.5, 0.62, matSteel, t.src, 2.75, SKY_Z, factoryWorld, 24);
    const cone = new THREE.Mesh(new THREE.ConeGeometry(0.5, 0.36, 24), matSteel); cone.rotation.x = Math.PI; cone.position.set(t.src, 2.3, SKY_Z); factoryWorld.add(cone);
    addCyl(0.36, 0.5, 0.18, matChrome, t.src, SKY_TOP - 0.25, SKY_Z, factoryWorld, 24);
    addCyl(0.34, 0.34, 0.12, matGold, t.src, SKY_TOP, SKY_Z, factoryWorld, 20);
    // blister packer with a cyclone receiver on its roof
    addCell({ x: t.dst, z: SKY_Z, w: 2.6, d: 1.6, h: 2.0, face: 0, plate: `BP-0${i + 1}`, sub: 'BLISTER PACK', power: pw });
    addCyl(0.42, 0.42, 0.62, matSteel, t.dst, 2.88, SKY_Z, factoryWorld, 24);
    const cyc = new THREE.Mesh(new THREE.ConeGeometry(0.42, 0.55, 24), matSteel); cyc.rotation.x = Math.PI; cyc.position.set(t.dst, 2.3, SKY_Z); factoryWorld.add(cyc);
    addCyl(0.34, 0.34, 0.12, matGold, t.dst, SKY_TOP, SKY_Z, factoryWorld, 20);
    [-0.34, 0.34].forEach(o => addCyl(0.03, 0.03, 1.0, matChrome, t.dst + o, 2.5, SKY_Z + 0.34, factoryWorld, 8));
  });

  const CAPS_COUNT = 54;
  const capGeo = THREE.CapsuleGeometry ? new THREE.CapsuleGeometry(0.15, 0.22, 6, 12) : new THREE.SphereGeometry(0.2, 12, 10);
  const skyCapsMat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.2, emissive: 0x221808, emissiveIntensity: 0.2 });
  const skyCapsInst = new THREE.InstancedMesh(capGeo, skyCapsMat, CAPS_COUNT);
  const capPalette = [0xefb85c, 0x1d5560, 0xf4eee2, 0xd98a2b, 0x3b8ea0];
  for (let i = 0; i < CAPS_COUNT; i++) skyCapsInst.setColorAt(i, new THREE.Color(capPalette[i % capPalette.length]));
  skyCapsInst.instanceColor.needsUpdate = true;
  factoryWorld.add(skyCapsInst);

  // Atrium 6-Nozzle Rotary Turret + Dual 6-Axis Robots + Palletized Tiryaq Cartons
  const atrTurret = new THREE.Group();
  atrTurret.position.set(84, 0, -8.2);
  factoryWorld.add(atrTurret);
  addCyl(0.85, 0.92, 4.4, matSteel, 0, 2.2, -3.1, atrTurret, 24);
  const atrCarousel = new THREE.Group();
  atrCarousel.position.set(0, 3.7, 0);
  atrTurret.add(atrCarousel);
  addCyl(1.48, 1.48, 0.42, matTeal, 0, 0, 0, atrCarousel, 28);
  addCyl(1.52, 1.52, 0.08, matGold, 0, 0.24, 0, atrCarousel, 28);
  const atrNozzles = Array.from({ length: 6 }, (_, i) => {
    const a = (i / 6) * Math.PI * 2;
    return addCyl(0.08, 0.08, 1.2, matChrome, Math.cos(a) * 1.05, -0.75, Math.sin(a) * 1.05, atrCarousel, 14);
  });

  const buildAtriumRobot = (rx, rz, side) => {
    const base = new THREE.Group();
    base.position.set(rx, 0, rz);
    factoryWorld.add(base);
    addCyl(0.76, 0.84, 0.52, matChassis, 0, 0.26, 0, base, 20);
    const sh = new THREE.Group();
    sh.position.y = 0.8;
    base.add(sh);
    addCyl(0.38, 0.38, 0.9, matTeal, 0, 0.4, 0, sh, 18);
    addBox(0.42, 0.42, 2.1, matTeal, 0, 0.86, -side * 0.85, sh);
    const el = new THREE.Group();
    el.position.set(0, 0.86, -side * 1.85);
    sh.add(el);
    addBox(0.32, 0.32, 1.55, matChassis, 0, 0, -side * 0.65, el);
    addBox(0.78, 0.16, 0.3, matGold, 0, 0, -side * 1.45, el);
    return { sh, el };
  };
  const robNorth = buildAtriumRobot(98, -3.2, -1);
  const robSouth = buildAtriumRobot(98, 3.2, 1);

  const cartonFrontMat = new THREE.MeshStandardMaterial({ map: makeCartonTexture(), roughness: 0.38 });
  const cartonSideMat = new THREE.MeshStandardMaterial({ color: 0x123f47, roughness: 0.44 });   // the line's carton side colour
  const cartonMats = [cartonSideMat, cartonSideMat, cartonSideMat, cartonSideMat, cartonFrontMat, cartonSideMat];
  /* =========================================================================
     PACKAGING CELL K-06 (x = 115.4 .. 121.8): where Line 04 ends and TQ-0417 meets its carton
     A pick-and-place gripper lifts the bottle off the spine's stop, a blank drops from the magazine and opens into a carton,
     the bottle is lowered in, the flaps fold, and the carton leaves on the outfeed through the dispatch door.
     ========================================================================= */
  const PICK_X = 116.9, CARTON_X = 119.6, OUTFEED_END = 133.5, GANTRY_Y = 6.75, PARK_Y = 6.0;
  const cellK = new THREE.Group();
  factoryWorld.add(cellK);
  // frame: four posts, top rails and the gantry beam the gripper carriage runs on
  [[115.45, -1.95], [115.45, 1.95], [121.75, -1.95], [121.75, 1.95]].forEach(([fx, fz]) => addBox(0.14, GANTRY_Y + 0.2, 0.14, matChrome, fx, (GANTRY_Y + 0.2) / 2, fz, cellK));
  [-1.95, 1.95].forEach(fz => addBox(6.44, 0.14, 0.14, matChrome, 118.6, GANTRY_Y + 0.2, fz, cellK));
  [115.45, 121.75].forEach(fx => addBox(0.14, 0.14, 4.04, matChrome, fx, GANTRY_Y + 0.2, 0, cellK));
  addBox(6.3, 0.3, 0.4, matTeal, 118.6, GANTRY_Y, 0, cellK);
  addBox(6.34, 0.06, 0.44, matGold, 118.6, GANTRY_Y - 0.17, 0, cellK);
  // back cabinet (the line's tall teal cartoner) with HMI and nameplate
  addBox(6.4, 4.6, 0.5, matTeal, 118.6, 2.3, -2.3, cellK);
  addBox(6.44, 0.18, 0.54, matChrome, 118.6, 4.62, -2.3, cellK);
  addBox(1.1, 0.72, 0.06, matScreen, 116.6, 3.1, -2.02, cellK);
  {
    const pl = new THREE.Mesh(new THREE.PlaneGeometry(2.2, 0.55), new THREE.MeshBasicMaterial({ map: makePlateTexture('CARTONER K-06', 'LINE 04 · ORAL LIQUIDS') }));
    pl.position.set(119.4, 4.1, -2.03); cellK.add(pl);
  }
  // guard: low polycarbonate panels on the aisle side so the cell can be watched while it runs
  const guardMat = new THREE.MeshPhysicalMaterial({ color: 0xe8f6f3, transparent: true, opacity: 0.1, roughness: 0.05, clearcoat: 1, envMapIntensity: 1.2, depthWrite: false, side: THREE.DoubleSide });
  const guard = new THREE.Mesh(new THREE.PlaneGeometry(6.2, 1.5), guardMat); guard.position.set(118.6, 1.55, 1.97); guard.renderOrder = 5; cellK.add(guard);
  addBox(6.3, 0.06, 0.06, matChrome, 118.6, 2.32, 1.97, cellK);
  addBox(6.3, 0.06, 0.06, matChrome, 118.6, 0.8, 1.97, cellK);
  // stack light
  addCyl(0.03, 0.03, 0.5, matChrome, 121.75, GANTRY_Y + 0.52, 1.95, cellK, 8);
  addCyl(0.08, 0.08, 0.13, matRedLamp, 121.75, GANTRY_Y + 0.84, 1.95, cellK, 12);
  const kAmber = new THREE.MeshStandardMaterial({ color: 0x5b4422, emissive: 0xefb85c, emissiveIntensity: 0, roughness: 0.4 });
  addCyl(0.08, 0.08, 0.13, kAmber, 121.75, GANTRY_Y + 0.98, 1.95, cellK, 12);
  const kGreen = new THREE.MeshStandardMaterial({ color: 0x2b5a44, emissive: 0x5de2b0, emissiveIntensity: 0.9, roughness: 0.3 });
  addCyl(0.08, 0.08, 0.13, kGreen, 121.75, GANTRY_Y + 1.12, 1.95, cellK, 12);
  // the spine's stop gate: TQ-0417 waits here to be picked
  addBox(0.12, 0.5, 1.5, matChrome, LINE04_END - 0.06, 1.24, 0, cellK);
  addBox(0.14, 0.08, 1.54, matGold, LINE04_END - 0.06, 1.5, 0, cellK);

  // outfeed belt: carton position -> through the dispatch door -> case packer outside
  const outfeedCurve = new THREE.LineCurve3(new THREE.Vector3(LINE04_END + 0.5, 0, 0), new THREE.Vector3(OUTFEED_END, 0, 0));
  const outfeedBedTex = makeBedTex();
  factoryWorld.add(
    sweepChannel(outfeedCurve, 1.0, 0.62, 0.93, 40, matChassis),
    sweepChannel(outfeedCurve, 1.03, 0.91, 0.955, 40, matChrome),
    sweepChannel(outfeedCurve, 0.8, 0.945, 1.0, 40, new THREE.MeshStandardMaterial({ color: 0x1b5560, map: outfeedBedTex, roughness: 0.55, metalness: 0.1, side: THREE.DoubleSide })),
    sweepChannel(outfeedCurve, 0.07, 0.996, 1.018, 40, line04BusMat)
  );
  for (let lx = LINE04_END + 1.2; lx < OUTFEED_END; lx += 2.4) [-0.8, 0.8].forEach(lz => addCyl(0.05, 0.06, 0.62, matChrome, lx, 0.31, lz, factoryWorld, 10));

  // carton magazine: a stainless tray bolted to the cabinet face, flat blanks standing in it, leaning back
  const magazine = new THREE.Group();
  magazine.position.set(120.9, 3.05, -1.75);
  magazine.rotation.set(-0.16, 0, 0);
  cellK.add(magazine);
  const blankMat = [cartonSideMat, cartonSideMat, cartonSideMat, cartonSideMat, cartonFrontMat, cartonSideMat];
  for (let i = 0; i < 7; i++) {
    const bl = new THREE.Mesh(new THREE.BoxGeometry(1.26, 2.34, 0.035), blankMat);
    bl.position.set(0, 1.2, 0.05 - 0.045 * i); magazine.add(bl);
  }
  addBox(1.46, 0.08, 0.5, matChrome, 0, 0.0, -0.12, magazine);           // floor of the tray
  addBox(1.46, 0.3, 0.05, matChrome, 0, 0.12, 0.12, magazine);           // front lip that holds the stack
  [-0.72, 0.72].forEach(sx => addBox(0.05, 2.5, 0.5, matChrome, sx, 1.25, -0.12, magazine));   // side guides
  addBox(1.46, 0.08, 0.08, matChrome, 0, 2.48, -0.3, magazine);
  [-0.6, 0.6].forEach(sx => addBox(0.12, 0.12, 0.42, matSteel, sx, 1.0, -0.42, magazine));    // brackets to the cabinet

  // The carton: open-topped box with two dust flaps and a tuck flap, hinged at the top edges.
  // Built once as a factory so the windowsill gets exactly the same carton.
  const CW = 1.26, CH = 2.36, CD = 1.06;
  const cartonFlapMat = new THREE.MeshStandardMaterial({ color: 0x2a6b74, roughness: 0.45, side: THREE.DoubleSide });
  const cartonInnerMat = new THREE.MeshStandardMaterial({ color: 0x123f47, roughness: 0.5, side: THREE.DoubleSide });
  const hiddenMat = new THREE.MeshBasicMaterial({ visible: false });
  function makeCarton() {
    const root = new THREE.Group();
    const body = new THREE.Group();          // scaled while the blank opens
    root.add(body);
    const box = new THREE.Mesh(new THREE.BoxGeometry(CW, CH, CD), [cartonInnerMat, cartonInnerMat, hiddenMat, cartonInnerMat, cartonFrontMat, cartonInnerMat]);
    box.position.y = CH / 2; box.castShadow = !MOB; box.receiveShadow = true;
    body.add(box);
    const outline = new THREE.Mesh(new THREE.BoxGeometry(CW, CH, CD), makeOutlineMat(1.2));
    outline.position.y = CH / 2; body.add(outline);
    const hinge = (px, py, pz) => { const h = new THREE.Group(); h.position.set(px, py, pz); body.add(h); return h; };
    const dustL = hinge(-CW / 2, CH, 0), dustR = hinge(CW / 2, CH, 0), tuck = hinge(0, CH, -CD / 2);
    const fl = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.46, CD * 0.94), cartonFlapMat); fl.position.y = 0.23; dustL.add(fl);
    const fr = fl.clone(); dustR.add(fr);
    const tk = new THREE.Mesh(new THREE.BoxGeometry(CW * 0.98, CD, 0.02), cartonFlapMat); tk.position.y = CD / 2; tuck.add(tk);
    // f1: dust flaps 0 (standing) .. 1 (folded in); f2: tuck flap 0 (standing) .. 1 (closed over the top)
    root.userData.setFlaps = (f1, f2) => {
      dustL.rotation.z = -f1 * Math.PI / 2 * 0.98;
      dustR.rotation.z = f1 * Math.PI / 2 * 0.98;
      tuck.rotation.x = f2 * Math.PI / 2;
      tuck.position.y = CH + 0.012 * f2;
    };
    // e: 0 = flat blank .. 1 = square carton
    root.userData.setErect = e => { body.scale.set(0.96 + 0.04 * e, 1, Math.max(0.035, e)); };
    root.userData.setErect(1); root.userData.setFlaps(0, 0);
    return root;
  }
  const carton3d = makeCarton();
  carton3d.visible = false;
  factoryWorld.add(carton3d);

  // gripper: carriage on the gantry, telescopic rod, head and two jaws that close on the cap
  const gripper = new THREE.Group();
  factoryWorld.add(gripper);
  const gCarriage = addBox(0.62, 0.36, 0.62, matChassis, 0, GANTRY_Y, 0, gripper);
  const gRod = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.05, 1, 12).translate(0, -0.5, 0), matChrome);
  gripper.add(gRod);
  const gHead = new THREE.Group(); gripper.add(gHead);
  addBox(0.62, 0.2, 0.46, matTeal, 0, 0.32, 0, gHead);
  addBox(0.66, 0.05, 0.5, matGold, 0, 0.2, 0, gHead);
  const jawL = addBox(0.07, 0.36, 0.3, matSteel, -0.36, 0.05, 0, gHead);
  const jawR = addBox(0.07, 0.36, 0.3, matSteel, 0.36, 0.05, 0, gHead);

  /* ---------- Dispatch: the door the finished carton leaves by ---------- */
  const dispatchFrameMat = matGold;
  addBox(0.34, 6.1, 0.34, matChassis, 125.75, 3.05, -3.62); addBox(0.34, 6.1, 0.34, matChassis, 125.75, 3.05, 3.62);
  addBox(0.12, 6.1, 0.1, dispatchFrameMat, 125.56, 3.05, -3.45); addBox(0.12, 6.1, 0.1, dispatchFrameMat, 125.56, 3.05, 3.45);
  addBox(0.4, 0.4, 7.6, matChassis, 125.75, 6.0, 0);
  // roller shutter, rolled up into its drum with a few slats still showing
  const drum = addCyl(0.36, 0.36, 7.2, matChassis, 125.6, 6.5, 0, factoryWorld, 20); drum.rotation.x = Math.PI / 2;
  for (let i = 0; i < 4; i++) addBox(0.06, 0.2, 7.0, i % 2 ? matChassis : matSteel, 125.6, 5.66 - i * 0.21, 0);
  // dock bumpers and a warning-striped threshold
  [-2.9, 2.9].forEach(bz => addBox(0.3, 0.5, 0.4, matDeepTeal, 125.6, 0.3, bz));
  addBox(0.9, 0.02, 6.8, new THREE.MeshStandardMaterial({ color: 0xefb85c, roughness: 0.5, emissive: 0x6b4a14, emissiveIntensity: 0.3 }), 125.4, 0.02, 0);
  {
    const sign = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 0.62), new THREE.MeshBasicMaterial({ map: makePlateTexture('DISPATCH', 'BAY 2 · OUTBOUND') }));
    sign.rotation.y = -Math.PI / 2; sign.position.set(125.52, 7.05, 0); factoryWorld.add(sign);
  }
  // daylight outside: a bright yard and the case packer the outfeed delivers to
  const yardMat = new THREE.MeshBasicMaterial({ color: 0xfff1d8, fog: false });
  const yard = new THREE.Mesh(new THREE.PlaneGeometry(40, 40).rotateX(-Math.PI / 2), yardMat); yard.position.set(147, 0.01, 0); factoryWorld.add(yard);
  const casePacker = addBox(2.2, 2.6, 2.4, new THREE.MeshStandardMaterial({ color: 0xe9e2d2, roughness: 0.6, emissive: 0x8a7a60, emissiveIntensity: 0.4 }), OUTFEED_END + 1.0, 1.3, 0);
  const lightSpillMat = new THREE.MeshBasicMaterial({ map: makeShaftTexture(), color: 0xffe9c4, transparent: true, opacity: 0.22, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide });
  {
    const g0 = new THREE.BufferGeometry();
    g0.setAttribute('position', new THREE.Float32BufferAttribute([125.5, 0.03, -3.4, 125.5, 0.03, 3.4, 116.5, 0.03, -5.5, 116.5, 0.03, 5.5], 3));
    g0.setAttribute('uv', new THREE.Float32BufferAttribute([0, 1, 1, 1, 0, 0, 1, 0], 2));
    g0.setIndex([0, 2, 1, 1, 2, 3]);
    factoryWorld.add(new THREE.Mesh(g0, lightSpillMat));
  }


  /* =========================================================================
     STATION 06 (homeWorld at x = 175): SUNLIT MORNING WINDOWSILL AT HOME
     Window & objects positioned cleanly on the RIGHT half (x = +0.55 .. +5.45)
     while left half (x = -12 .. +0.55) is pure warm sunlit cream plaster!
     ========================================================================= */
  // Warm self-illuminated sunlit cream plaster so interior wall NEVER renders grey!
  const matSunlitPlaster = new THREE.MeshStandardMaterial({
    color: 0xf4e7d2,
    emissive: 0xdccab0,
    emissiveIntensity: 0.52,
    roughness: 0.92,
    envMapIntensity: 0.15
  });
  const matOak = new THREE.MeshStandardMaterial({
    color: 0xcfaa7e,
    emissive: 0x4a3418,
    emissiveIntensity: 0.18,
    roughness: 0.54,
    envMapIntensity: 0.35
  });
  const matWinFrame = new THREE.MeshStandardMaterial({
    color: 0x628683,
    emissive: 0x1d2e2d,
    emissiveIntensity: 0.2,
    roughness: 0.44
  });
  const matPlanterTeal = new THREE.MeshStandardMaterial({ color: 0x1d5560, roughness: 0.42, metalness: 0.1 });
  const matSageLeaf = new THREE.MeshStandardMaterial({ color: 0x5d8d7c, roughness: 0.52, side: THREE.DoubleSide });

  // Dedicated warm morning interior fill light inside homeWorld
  const homeWarmLight = new THREE.PointLight(0xfff4dc, 2.2, 28);
  homeWarmLight.position.set(1.5, 4.8, 5.2);
  homeWorld.add(homeWarmLight);

  // Window opening spans x = [0.65 .. 5.15], y = [1.44 .. 4.15] at z = -1.8
  // Left plaster wall (-14 .. +0.65) sits cleanly behind #b-home text!
  addBox(14.65, 16, 0.36, matSunlitPlaster, -6.675, 3.5, -1.8, homeWorld);
  // Right plaster wall (+5.15 .. +14) — leaves warm plaster margin on right of window too!
  addBox(8.85, 16, 0.36, matSunlitPlaster, 9.575, 3.5, -1.8, homeWorld);
  // Lower & upper wall aprons framing the window cleanly below the top navbar
  addBox(4.50, 5.52, 0.36, matSunlitPlaster, 2.90, -1.32, -1.8, homeWorld);
  addBox(4.50, 7.30, 0.36, matSunlitPlaster, 2.90, 7.80, -1.8, homeWorld);
  addBox(28, 0.2, 18, matSunlitPlaster, 0, -4.2, 0, homeWorld);

  // Painted morning sunrise sky, misty hills & town outside the window!
  const skyBackdrop = new THREE.Mesh(
    new THREE.PlaneGeometry(11.0, 6.8),
    new THREE.MeshBasicMaterial({ map: makeSunriseTexture() })
  );
  skyBackdrop.position.set(2.90, 3.1, -4.6);
  homeWorld.add(skyBackdrop);

  // 4-Pane Slate-Teal Architectural Window Casement (centered at x = 2.90, y = 1.44..4.15)
  addBox(4.66, 0.10, 0.20, matWinFrame, 2.90, 4.15, -1.70, homeWorld);
  addBox(4.66, 0.10, 0.20, matWinFrame, 2.90, 1.44, -1.70, homeWorld);
  addBox(0.10, 2.81, 0.20, matWinFrame, 0.62, 2.795, -1.70, homeWorld);
  addBox(0.10, 2.81, 0.20, matWinFrame, 5.18, 2.795, -1.70, homeWorld);
  addBox(0.075, 2.70, 0.14, matWinFrame, 2.90, 2.795, -1.70, homeWorld);
  addBox(4.52, 0.075, 0.14, matWinFrame, 2.90, 2.84, -1.70, homeWorld);

  // Natural Warm Oak Windowsill Ledge (x = 0.32 .. 5.48, y = 1.33)
  addBox(5.16, 0.15, 1.25, matOak, 2.90, 1.33, -1.24, homeWorld);

  // Soft diagonal warm morning sunbeam plane
  const sunBeam = new THREE.Mesh(
    new THREE.PlaneGeometry(5.2, 3.8),
    new THREE.MeshBasicMaterial({ color: 0xfff7dd, transparent: true, opacity: 0.07, side: THREE.DoubleSide, depthWrite: false })
  );
  sunBeam.position.set(2.75, 2.65, -0.85);
  sunBeam.rotation.y = -0.24;
  sunBeam.rotation.z = -0.20;
  homeWorld.add(sunBeam);

  // Objects arranged cleanly along the Oak Sill (x = +0.92 .. +4.48 — all 5 fit cleanly inside frame!):
  // 1) Primary-Teal Ceramic Planter with Honey Collar + Sage Botanical Leaves (x = 0.96)
  addCyl(0.22, 0.18, 0.34, matPlanterTeal, 0.96, 1.405 + 0.17, -1.18, homeWorld, 24);
  addCyl(0.228, 0.225, 0.032, matGold, 0.96, 1.405 + 0.33, -1.18, homeWorld, 24);
  [-0.55, -0.25, 0.05, 0.35, 0.62].forEach((ang, li) => {
    const leaf = new THREE.Mesh(new THREE.SphereGeometry(0.11, 12, 10), matSageLeaf);
    leaf.scale.set(0.60, 2.1, 0.24);
    leaf.position.set(0.96 + Math.sin(ang) * 0.18, 1.81 + (li % 2) * 0.07, -1.18 + Math.cos(ang) * 0.05);
    leaf.rotation.z = -ang * 0.72;
    homeWorld.add(leaf);
  });

  // 2) The carton TQ-0417 left the cleanroom in, opened (x = 1.92). Same factory as the packaging cell, same artwork.
  const HOME_BOTTLE = 0.98, HOME_K = HOME_BOTTLE / HERO_SCALE;   // windowsill bottle scale; the carton keeps the cleanroom's proportion to it
  const homeCarton = makeCarton();
  homeCarton.userData.setFlaps(0.15, 0.08);
  homeCarton.scale.setScalar(HOME_K);
  homeCarton.position.set(1.98, 1.405, -1.16);
  homeCarton.rotation.y = 0.12;
  homeWorld.add(homeCarton);

  // 3) Lot TQ-0417 itself (x = 2.92): built from the cleanroom bottle's own parts, so glass, solution, cap and label match.
  const homeBottle = new THREE.Group();
  const hbHomeGlass = new THREE.Mesh(vesselGeo, hbOuter.material);
  hbHomeGlass.renderOrder = 3;
  const hbHomeLiq = new THREE.Mesh(liqUnitGeo, heroLiqMat);
  hbHomeLiq.position.y = 0.03; hbHomeLiq.renderOrder = 1;
  const hbHomeMen = meniscusDisc.clone(); hbHomeMen.visible = true; hbHomeMen.position.y = 0.92;
  const hbHomeCap = heroCapGrp.clone();
  hbHomeCap.visible = true;
  hbHomeCap.position.y = 1.42;
  const hbHomeLbl = new THREE.Mesh(labelGeo, heroLabelMat);
  hbHomeLbl.renderOrder = 4;
  const hbHomeShadow = new THREE.Mesh(new THREE.PlaneGeometry(1.05, 1.05).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: shadowTex, transparent: true, opacity: 0.35, depthWrite: false }));
  hbHomeShadow.position.y = 0.006;
  homeBottle.add(hbHomeGlass, hbHomeLiq, hbHomeMen, hbHomeCap, hbHomeLbl, hbHomeShadow);
  homeBottle.scale.setScalar(HOME_BOTTLE);
  homeBottle.position.set(2.92, 1.405, -1.02);
  homeBottle.rotation.y = 0.06;
  homeWorld.add(homeBottle);

  // 4) Graduated 5 mL Dosing Cup with amber liquid (x = 3.72)
  const dosingCup = addCyl(0.17, 0.14, 0.32, glassOuterMat, 3.72, 1.405 + 0.16, -1.02, homeWorld, 24);
  dosingCup.renderOrder = 3;
  addCyl(0.145, 0.13, 0.15, heroLiqMat, 3.72, 1.405 + 0.085, -1.02, homeWorld, 20);

  // 5) Tall Crystal Drinking Glass of Water (x = 4.38)
  const waterGlass = addCyl(0.22, 0.185, 0.84, glassOuterMat, 4.38, 1.405 + 0.42, -1.10, homeWorld, 28);
  waterGlass.renderOrder = 3;
  addCyl(0.198, 0.175, 0.58, new THREE.MeshPhysicalMaterial({
    color: 0xd9f2ef, transparent: true, opacity: 0.46, roughness: 0.05, envMapIntensity: 1.2
  }), 4.38, 1.405 + 0.30, -1.10, homeWorld, 24);

  /* =========================================================================
     SCROLL-DRIVEN CAMERA KEYFRAMES (7-BEAT CINEMATIC SPLINE)
     ========================================================================= */
    // Cleanroom camera: continuous splines, one per act, each eased as a whole so the camera accelerates and settles
  // instead of stopping at every keyframe. The reveal's first point is replaced by the illustrated line's own framing of
  // TQ-0417 (see "Camera" in the render loop), so the bottle stays the same size and in the same place across the handoff.
  const GL_B0 = 6.9;
  const LINE_BEAT_OUT_OF_LINE = 7.34;       // beat at which the illustrated line hands TQ-0417 to the cleanroom
  const V3 = a => new THREE.Vector3(a[0], a[1], a[2]);
  const mkPath = pts => new THREE.CatmullRomCurve3(pts.map(V3), false, 'centripetal');
  const CAM_ACTS = [
    // 1. the reveal: rise and pull back from the bottle, out over the floor, to the whole room
    { b0: HOLD_END, b1: REVEAL_END, ease: ss,
      pos: mkPath([[53.0, 2.1, 7.7], [54.0, 4.4, 10.0], [57.4, 9.6, 16.4], [65.5, 16.5, 24.5]]),
      tgt: mkPath([[53.0, 2.1, 0.0], [57.0, 2.0, -0.2], [70.5, 1.9, -0.5], [89.0, 2.0, -0.5]]),
      fov: [31, 37], shift: [0.17, 0.0] },
    // 2. the twelve-line panorama drifts east
    { b0: REVEAL_END, b1: 7.84, ease: sm,
      pos: mkPath([[65.5, 16.5, 24.5], [80.0, 14.5, 21.5]]), tgt: mkPath([[89.0, 2.0, -0.5], [100.0, 2.2, 0.0]]),
      fov: [37, 36], shift: [0, 0] },
    // 3. down to the packaging cell, landing side-on like the line's own framing
    { b0: 7.84, b1: 8.2, ease: ss,
      pos: mkPath([[80.0, 14.5, 21.5], [101.0, 10.2, 20.0], [112.5, 6.4, 16.4], [118.3, 4.3, 13.8]]),
      tgt: mkPath([[100.0, 2.2, 0.0], [109.0, 2.8, 0.0], [115.5, 3.4, 0.0], [118.3, 3.6, 0.0]]),
      fov: [36, 31], shift: [0, -0.16] },
    // 4. packaging: a slow push while the cell works
    { b0: 8.2, b1: 8.74, ease: sm,
      pos: mkPath([[118.3, 4.3, 13.8], [118.6, 4.1, 12.9]]), tgt: mkPath([[118.3, 3.6, 0.0], [118.7, 3.5, 0.0]]),
      fov: [31, 31], shift: [-0.16, -0.16] },
    // 5. dispatch: turn to follow the carton out through the door into the daylight
    { b0: 8.74, b1: HOME_B, ease: ss,
      pos: mkPath([[118.6, 4.1, 12.9], [117.2, 3.7, 8.6], [119.8, 3.1, 4.9]]), tgt: mkPath([[118.7, 3.5, 0.0], [125.0, 2.7, 0.0], [133.0, 2.4, 0.0]]),
      fov: [31, 33], shift: [-0.16, 0] },
    // 6. the windowsill at home: a slow settle
    { b0: HOME_B, b1: 10.0, ease: sm,
      pos: mkPath([[176.4, 2.85, 10.6], [176.25, 2.8, 9.8]]), tgt: mkPath([[176.4, 2.78, -1.8], [176.25, 2.75, -1.8]]),
      fov: [28, 28], shift: [0, 0] }
  ];
  const camOut = { pos: new THREE.Vector3(), tgt: new THREE.Vector3(), fov: 31, shiftX: 0 };
  function sampleCamera(b) {
    let act = CAM_ACTS[0];
    if (b <= act.b0) { act.pos.getPoint(0, camOut.pos); act.tgt.getPoint(0, camOut.tgt); camOut.fov = act.fov[0]; camOut.shiftX = act.shift[0]; return camOut; }
    for (const a of CAM_ACTS) if (b >= a.b0) act = a;
    const e = act.ease(seg(b, act.b0, act.b1));
    act.pos.getPoint(e, camOut.pos); act.tgt.getPoint(e, camOut.tgt);
    camOut.fov = lerp(act.fov[0], act.fov[1], e); camOut.shiftX = lerp(act.shift[0], act.shift[1], e);
    return camOut;
  }

  /* ---------- Interactive Pointer Drag-Orbit & Subtle Hover Parallax ---------- */
  let drag = null, userYaw = 0, userPitch = 0, curYaw = 0, curPitch = 0, hoverX = 0, hoverY = 0;

  canvas.addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    drag = { id: e.pointerId, x: e.clientX, y: e.clientY };
    canvas.setPointerCapture(e.pointerId);
  });
  addEventListener('pointermove', e => {
    hoverX = (e.clientX / Math.max(1, innerWidth) - 0.5) * 2;
    hoverY = (e.clientY / Math.max(1, innerHeight) - 0.5) * 2;
    if (!drag || drag.id !== e.pointerId) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    drag.x = e.clientX;
    drag.y = e.clientY;
    userYaw = cl(userYaw - dx * 0.0042, -0.58, 0.58);
    userPitch = cl(userPitch + dy * 0.0030, -0.22, 0.34);
  }, { passive: true });

  const endDrag = e => {
    if (drag && (!e || drag.id === e.pointerId)) {
      if (e && canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
      drag = null;
    }
  };
  canvas.addEventListener('pointerup', endDrag);
  canvas.addEventListener('pointercancel', endDrag);

  /* ---------- Cached DOM Instrument References ---------- */
  const heroScrim = $('#hero-scrim');
  const sunVeil = $('#sun-veil');
  const instStream = $('#inst-stream');
  const instFill = $('#inst-fill');
  const rNozzle = $('#r-nozzle');
  const rVolume = $('#r-volume');
  const rMeniscus = $('#r-meniscus');
  const instDialArc = $('#inst-dial-arc');
  const instNeedle = $('#inst-needle');
  const rCap = $('#r-cap');
  const rTorque = $('#r-torque');
  const rLabel = $('#r-label');
  const qcRows = $$('#inst-qc li');
  const railDone = $('#rail-done');
  const miniBottle = $('#mini-bottle');
  const mbLiq = $('#mb-liq');
  const mbCap = $('#mb-cap');
  const mbLbl = $('#mb-lbl');
  const mbLblBand = $('#mb-lbl-band');
  const orbitHint = $('#orbit-hint');

  const upVec = new THREE.Vector3(0, 1, 0);
  const vPos = new THREE.Vector3();
  const vTgt = new THREE.Vector3();
  const vOff = new THREE.Vector3();
  const vMatchPos = new THREE.Vector3(), vMatchTgt = new THREE.Vector3(), vThread = new THREE.Vector3();
  const cGold = new THREE.Color(0xefb85c);
  const cMint = new THREE.Color(0x5de2b0);
  const cDim = new THREE.Color(0x2d545c);

  let curBeat = 0, tgtBeat = 0, time = 0, lastNow = performance.now(), renderErrors = 0, creepAcc = 0;
  const threadPt = new THREE.Vector3(HERO_X0, 1.0 + BOT_H * 1.4 / 2, 0);
  let threadH = BOT_H * 1.4, threadV = 1, threadS = '';

  function onResize() {
    const w = innerWidth, h = innerHeight;
    renderer.setSize(w, h, false);
    cam.aspect = w / h;
    cam.updateProjectionMatrix();
  }
  addEventListener('resize', onResize);
  onResize();

  /* ============ 60FPS RENDER & INSTRUMENT LOOP ============ */
  function frame(now) {
    requestAnimationFrame(frame);
    const dt = cl((now - lastNow) / 1000, 0, 0.05);
    lastNow = now;

    const span = Math.max(1, (trackEl?.offsetHeight || innerHeight * 9) - innerHeight);
    const topOff = trackEl?.offsetTop || 0;

    if (cruising) {
      const nextY = scrollY + (span / 36) * dt;
      if (nextY >= topOff + span) {
        scrollTo(0, topOff + span);
        stopCruise();
      } else {
        scrollTo(0, nextY);
      }
    }

    // On the homepage the controller (tiryaq-home-stage.js) owns the scroll and hands us only the cleanroom beats.
    const homeStage = window.TiryaqStage;
    tgtBeat = homeStage ? homeStage.glBeat() : cl(((scrollY - topOff) / span) * 10, 0, 10);
    const prevBeat = curBeat;
    curBeat = RM ? tgtBeat : curBeat + (tgtBeat - curBeat) * (1 - Math.exp(-dt * 7.2));
    if (!RM) time += dt;
    if (state.towerPulse > 0) state.towerPulse = Math.max(0, state.towerPulse - dt);

    if (!drag && Math.abs(tgtBeat - curBeat) > 0.03) {
      userYaw *= Math.exp(-dt * 2.6);
      userPitch *= Math.exp(-dt * 2.6);
    }
    curYaw += (userYaw - curYaw) * (1 - Math.exp(-dt * 10));
    curPitch += (userPitch - curPitch) * (1 - Math.exp(-dt * 10));

    // 1. Beat Card Visibility & Overlays
    beatsEls.forEach(({ el, bIn, bOut }) => {
      el.classList.toggle('is-on', curBeat >= bIn && curBeat <= bOut);
    });
    heroScrim?.classList.toggle('is-off', curBeat > 0.80);
    orbitHint?.classList.toggle('is-dim', curBeat > 0.45);

    // The dispatch door's daylight fills the frame and becomes the morning at home; the swap happens under the full veil.
    const veilAlpha = cl(1 - Math.abs(curBeat - HOME_B) / 0.2, 0, 1);
    if (sunVeil) sunVeil.style.opacity = ss(veilAlpha).toFixed(3);

    const inHome = curBeat >= HOME_B;
    factoryWorld.visible = !inHome;
    homeWorld.visible = inHome;

    // 2. Unified Bottom Timeline Rail & Mini-Bottle State
    const normP = cl(curBeat / 10, 0, 1);
    if (railDone) railDone.style.transform = `scaleX(${normP.toFixed(4)})`;
    if (miniBottle && railTrackEl) {
      const tw = railTrackEl.clientWidth;
      miniBottle.style.transform = `translate3d(${(normP * tw).toFixed(1)}px, 0, 0)`;
    }
    let activeStationIdx = 0;
    STATIONS.forEach((st, idx) => {
      if (curBeat >= st.beat - 0.55) activeStationIdx = idx;
      nodeBtns[idx]?.classList.toggle('is-passed', curBeat >= st.beat - 0.15);
    });
    nodeBtns.forEach((btn, idx) => btn.classList.toggle('is-active', idx === activeStationIdx));

    // 3. Synchronized Bottle Physical State & Scientific Instruments
    const fillFrac = sm(cl((curBeat - 2.66) / 0.58, 0, 1));
    const nozzleDive = curBeat < 3.24
      ? sm(cl((curBeat - 2.52) / 0.18, 0, 1))
      : 1 - sm(cl((curBeat - 3.24) / 0.18, 0, 1));
    const isPouring = curBeat >= 2.65 && curBeat <= 3.24 && fillFrac < 0.998;

    const capFrac = sm(cl((curBeat - 4.06) / 0.38, 0, 1));
    const labelFrac = sm(cl((curBeat - 4.46) / 0.40, 0, 1));

    if (mbLiq) mbLiq.setAttribute('opacity', fillFrac.toFixed(2));
    if (mbCap) mbCap.setAttribute('opacity', (0.22 + capFrac * 0.78).toFixed(2));
    if (mbLbl) mbLbl.setAttribute('opacity', labelFrac.toFixed(2));
    if (mbLblBand) mbLblBand.setAttribute('opacity', labelFrac.toFixed(2));

    // Beat 2 Burette Readouts
    if (instFill) instFill.style.transform = `scaleY(${(fillFrac * 0.78).toFixed(3)})`;
    if (instStream) instStream.style.opacity = isPouring ? '1' : '0';
    if (rNozzle) rNozzle.textContent = curBeat < 2.54 ? 'raised' : (curBeat <= 3.26 ? 'in neck · dosing' : 'withdrawn');
    if (rVolume) rVolume.textContent = `${(fillFrac * 100).toFixed(1)} mL`;
    if (rMeniscus) rMeniscus.textContent = fillFrac <= 0.01 ? 'awaiting' : (fillFrac < 0.99 ? 'rising calm' : 'locked ±0.2%');

    // Beat 3 Torque Dial & Label Readouts
    const torqueVal = capFrac * 1.44;
    if (instDialArc) instDialArc.setAttribute('stroke-dasharray', `${(capFrac * 82).toFixed(1)} 100`);
    if (instNeedle) instNeedle.style.transform = `rotate(${(-125 + capFrac * 212).toFixed(1)}deg)`;
    if (rCap) rCap.textContent = capFrac <= 0.02 ? 'in chute' : (capFrac < 0.98 ? 'seating' : 'torqued & locked');
    if (rTorque) rTorque.textContent = `${torqueVal.toFixed(2)} N·m`;
    if (rLabel) rLabel.textContent = labelFrac <= 0.02 ? 'standby' : (labelFrac < 0.98 ? 'wrapping 360°' : 'bonded · TQ-0417');

    // Beat 4 Optical 5-Check Vision Matrix
    const qcThresholds = [5.68, 5.83, 5.98, 6.13, 6.28];
    qcRows.forEach((li, qi) => {
      const th = qcThresholds[qi];
      const em = li.querySelector('em');
      const passed = curBeat >= th + 0.14;
      const scanning = !passed && curBeat >= th - 0.06;
      li.classList.toggle('is-pass', passed);
      li.classList.toggle('is-scan', scanning);
      if (em) em.textContent = passed ? 'pass' : (scanning ? 'scanning' : 'queued');
    });

    try {
      // Dark like the line while TQ-0417 is framed tight, lifting as the camera pulls back, easing back as it lands on the packaging cell.
      // Dark like the line while TQ-0417 is framed tight; the room lifts into day as the camera cranes back and the lines
      // come online one after another, nearest the spine first.
      const dayMix = sm(seg(curBeat, 7.06, 7.46));
      const online = sm(seg(curBeat, 7.05, 7.4));
      const power = i => sm(seg(curBeat, 7.07 + i * 0.027, 7.14 + i * 0.027));
      if (!RM) creepAcc += dt * 0.14 * online;
      if (inHome) {
        curSky.copy(cSkyHome);
        scene.fog.color.copy(cSkyHome);
        scene.fog.near = 32;
        scene.fog.far = 110;
        hemi.intensity = 1.65;
        keyLight.intensity = 1.6;
        // Front-right warm morning sun illuminating windowsill & interior plaster without grey self-shadow!
        keyLight.position.set(182, 11, 10);
        keyLight.target.position.set(177.5, 1.8, -1.2);
        keyLight.target.updateMatrixWorld();
        if (stageEl) stageEl.dataset.tone = 'warm';
      } else {
        curSky.copy(cSkyNight).lerp(cSkyDay, dayMix);
        curGnd.copy(cGndNight).lerp(cGndDay, dayMix);
        groundMat.color.copy(curGnd);
        scene.fog.color.copy(curSky);
        scene.fog.near = lerp(30, 50, dayMix);
        scene.fog.far = lerp(112, 180, dayMix);

        const nightGlow = 1 - dayMix;
        hemi.intensity = 0.92 + dayMix * 0.58;
        keyLight.intensity = 1.50 + dayMix * 0.45;
        line04BusMat.emissiveIntensity = 0.25 + nightGlow * 0.68;
        moteMat.opacity = 0.14 + nightGlow * 0.28;
        if (stageEl) stageEl.dataset.tone = dayMix > 0.5 ? 'day' : 'dark';

        // Update Hero Specimen TQ-0417 on Line 04
        const hx = getHeroX(curBeat);

        // ---- Packaging cell K-06: pick, carton, place, close, out through dispatch ----
        const pk = k => seg(curBeat, PK[k][0], PK[k][1]);
        const BASE_Y = 1.0, LIFT_Y = BASE_Y + CH + 0.56, IN_Y = BASE_Y + 0.04, CAP_Y = 1.42 * HERO_SCALE;
        const outT = ss(pk('out'));
        const cartonX = lerp(CARTON_X, 129.6, outT);
        let bx = hx, by = BASE_Y;
        if (curBeat >= PK.lift[0]) {
          bx = curBeat >= PK.out[0] ? cartonX : lerp(PICK_X, CARTON_X, ss(pk('across')));
          by = curBeat < PK.lower[0] ? lerp(BASE_Y, LIFT_Y, ss(pk('lift'))) : lerp(LIFT_Y, IN_Y, ss(pk('lower')));
        }
        heroBottle.position.set(bx, by, 0);
        // the belt halo, glow and contact shadow stay with the bottle only while it stands on the belt
        const onBelt = curBeat < PK.lift[0] + 0.004;
        heroHalo.visible = heroGlow.visible = heroShadow.visible = onBelt;

        // gripper: down to the cap, close, lift, across, down into the carton, open, up, back for the next bottle
        let gx = PICK_X, gy = PARK_Y;
        if (curBeat < PK.lift[0]) gy = lerp(PARK_Y, BASE_Y + CAP_Y, ss(pk('gripDown')));
        else if (curBeat < PK.open[1]) { gx = bx; gy = by + CAP_Y; }
        else { gx = lerp(CARTON_X, PICK_X, ss(pk('back'))); gy = lerp(IN_Y + CAP_Y, PARK_Y, ss(pk('up'))); }
        gripper.position.x = gx;
        gHead.position.y = gy - 0.05;
        gRod.position.y = GANTRY_Y - 0.18;
        gRod.scale.y = Math.max(0.01, (GANTRY_Y - 0.18) - (gy + 0.37));
        const jawOpen = cl(1 - ss(pk('close')) + ss(pk('open')), 0, 1);
        jawL.position.x = -lerp(0.29, 0.38, jawOpen); jawR.position.x = lerp(0.29, 0.38, jawOpen);
        kAmber.emissiveIntensity = curBeat > PK.gripDown[0] && curBeat < PK.tuck[1] ? 0.9 + 0.3 * Math.sin(time * 6) : 0;

        // carton: a blank drops from the magazine, opens, receives the bottle, folds shut and rides out
        carton3d.visible = curBeat >= PK.blank[0];
        if (curBeat < PK.out[0]) {
          const bt = ss(pk('blank'));
          carton3d.position.set(lerp(120.9, CARTON_X, bt), lerp(3.1, BASE_Y, bt), lerp(-1.62, 0, bt));
          carton3d.rotation.set(lerp(-0.16, 0, bt), 0, 0);
        } else {
          carton3d.position.set(cartonX, BASE_Y, 0);
          carton3d.rotation.set(0, 0, 0);
        }
        carton3d.userData.setErect(ss(pk('erect')));
        carton3d.userData.setFlaps(ss(pk('dust')), ss(pk('tuck')));
        outfeedBedTex.offset.x = -(cartonX - CARTON_X) / BELT_TILE;

        // lot tag: the bottle until its carton closes, then the carton
        const boxed = curBeat >= PK.dust[1];
        if (boxed) { threadPt.set(carton3d.position.x, BASE_Y + CH / 2, 0); threadH = CH; }
        else { threadPt.set(bx, by + HERO_H / 2, 0); threadH = HERO_H; }
        threadV = 1 - sm(seg(curBeat, 9.0, 9.12));
        threadS = curBeat < 7.9 ? 'Line 04 spine · unboxed' : curBeat < PK.lift[0] ? 'To cartoner K-06 · unboxed'
          : !boxed ? 'Packaging · K-06' : 'Boxed · out to dispatch';
        // Sweeps Velmira front typography smoothly INTO camera view as label wraps!
        heroBottle.rotation.y = (labelFrac > 0 && labelFrac < 1) ? (1 - labelFrac) * Math.PI * 0.65 : 0;

        keyLight.position.set(bx + 8, 28, 18);
        keyLight.target.position.set(bx, 1.5, 0);
        keyLight.target.updateMatrixWorld();

        heroLiq.visible = fillFrac > 0.004;
        heroLiq.scale.set(1, Math.max(0.001, fillFrac), 1);
        meniscusDisc.visible = fillFrac > 0.01;
        meniscusDisc.position.y = 0.03 + fillFrac * 0.89;

        // Rising effervescent micro-bubbles during dosing!
        dosingBubbles.forEach((bm, bi) => {
          bm.visible = isPouring && fillFrac > 0.08;
          if (bm.visible) {
            const prog = ((time * 1.8 + bi * 0.21) % 1);
            const rad = 0.05 + (bi % 4) * 0.045;
            const ang = bi * 1.7 + time * 1.4;
            bm.position.set(
              Math.cos(ang) * rad,
              0.04 + prog * fillFrac * 0.48,
              Math.sin(ang) * rad
            );
          }
        });

        heroCapGrp.visible = capFrac > 0.01;
        heroCapGrp.position.y = lerp(1.9, 1.42, capFrac);
        heroCapGrp.rotation.y = capFrac * Math.PI * 4;

        heroLabel.visible = labelFrac > 0.02;
        heroLabel.scale.set(1, Math.max(0.02, labelFrac), 1);

        // The honey ring and glow under TQ-0417 are always on in the cleanroom: they are the thread the eye follows from the line.
        heroHaloMat.opacity = 0.9;
        heroGlowMat.opacity = 0.35 + dayMix * 0.2;
        heroHalo.scale.setScalar(1 + (RM ? 0 : 0.05 * Math.sin(time * 3.8)));
        // while the cleanroom still holds the line's framing, a warm key light breathes on the bottle: the room is about to open
        heroGlowLight.intensity = 0.9 + (1 - online) * (0.35 + (RM ? 0 : 0.15 * Math.sin(time * 2.2)));

        // Drive wheels turning beneath Line 04
        const wheelSpin = hx * 1.4 + time * 0.35;
        stanchionPositions.forEach((wx, wi) => {
          dummy.position.set(wx, 0.43, 0);
          dummy.rotation.set(0, 0, -wheelSpin);
          dummy.scale.set(1, 1, 1);
          dummy.updateMatrix();
          wheelsInst.setMatrixAt(wi, dummy.matrix);
          hubInst.setMatrixAt(wi, dummy.matrix);
        });
        wheelsInst.instanceMatrix.needsUpdate = true;
        hubInst.instanceMatrix.needsUpdate = true;

        // Station 01: Supplier Stream Highlight & 4-Tower Pulse
        feederBusMats.forEach((bm, fi) => {
          const sel = fi === state.selectedStream;
          bm.color.copy(sel ? cGold : cMint);
          bm.emissive.copy(sel ? cGold : cMint);
          bm.emissiveIntensity = sel ? 1.2 : 0.45;
        });
        gateTowers.forEach((cm, ti) => {
          const boost = state.towerPulse > 0 ? Math.sin(state.towerPulse * Math.PI * 3) * 0.7 : 0;
          cm.emissiveIntensity = 0.55 + 0.3 * Math.sin(time * 2.6 + ti * 1.5) + Math.max(0, boost);
        });

        // Station 02: Diving Nozzle & Pour Stream
        nozzleHead.position.y = lerp(4.25, 3.38, nozzleDive);
        pourStream.visible = isPouring;

        // Station 03: Torque Spindle & Label Reel
        const chuckDip = curBeat >= 4.04 && curBeat <= 4.52
          ? Math.sin(cl((curBeat - 4.04) / 0.48, 0, 1) * Math.PI)
          : 0;
        spindleChuck.position.y = lerp(3.75, 3.06, chuckDip);
        spindleChuck.rotation.y = capFrac * Math.PI * 4;
        labelReel.rotation.z = -labelFrac * Math.PI * 3;

        // Station 04: Vision Laser Sheet & Reject Bottle Divert
        const scanActive = curBeat >= 5.58 && curBeat <= 6.46;
        laserSheetMat.opacity = scanActive ? 0.45 : 0;
        if (scanActive) {
          const sw = cl((curBeat - 5.60) / 0.78, 0, 1);
          laserSheet.position.y = lerp(3.15, 1.12, sw);
        }
        const rejU = sm(cl((curBeat - 5.45) / 0.75, 0, 1));
        const rejPt = rejectSpurCurve.getPointAt(rejU);
        rejectBottle.position.set(rejPt.x, rejPt.y + 1.0, rejPt.z);

        // Station 05: the twelve-line cleanroom. Belts are driven by the scroll like the line's belt (the spine carries TQ-0417
        // and the bottles around it in lock-step); the other rails add only a slow creep so the room is never frozen.
        const spineTravel = hx - HERO_X0;
        // Bottles beside TQ-0417 at the hero's own size while the camera is close (the line's framing at the start, the
        // packaging cell at the end); the room's wider traffic takes over while the camera is far.
        const startPres = 1 - sm(seg(curBeat, 7.05, 7.27));
        const endPres = sm(seg(curBeat, 7.92, 8.12));
        const flow = spineTravel + creepAcc;
        let vCur = 0;
        trafficRails.forEach(r => {
          const rf = r.atriumSpine ? spineTravel : flow;
          for (let j = 0; j < r.n; j++) {
            let u = (j / r.n + (r.dir * rf * r.speed) / r.len) % 1;
            if (u < 0) u += 1;
            const p = r.c.getPointAt(u);
            // bottles appear and disappear only inside the machines at each end of their belt
            let sc = (r.closed ? 1 : cl(Math.min(u, 1 - u) * r.len / 0.9, 0, 1)) * TRAFFIC_SCALE;
            if (r.atriumSpine) {
              const dx = p.x - hx;
              if (Math.abs(dx) < 2.4) sc = 0;
              else if (dx < 0 && dx > -18) sc *= 1 - Math.max(startPres, endPres);
              else if (dx > 0 && dx < 8) sc *= 1 - startPres;
              if (dx > 0) sc *= 1 - endPres;
            }
            dummy.position.set(p.x, p.y + 1.0, p.z);
            dummy.rotation.set(0, 0, 0);
            dummy.scale.setScalar(sc || 0.0001);
            dummy.updateMatrix();
            traffic.put(vCur, dummy.matrix);
            vCur++;
          }
          if (r.bedTex) r.bedTex.offset.x = -(r.dir * rf * r.speed) / BELT_TILE;
        });
        traffic.commit();
        line04BedTex.offset.x = -hx / BELT_TILE;

        CONVOY_K.forEach((k, i) => {
          const pres = k < 0 ? Math.max(startPres, endPres) : startPres;
          dummy.position.set(hx + k * CONVOY_GAP, 1.0, 0);
          dummy.rotation.set(0, 0, 0);
          dummy.scale.setScalar(Math.max(0.0001, HERO_SCALE * pres));
          dummy.updateMatrix();
          convoy.put(i, dummy.matrix);
        });
        convoy.commit();

        hallDMat.opacity = dayMix;
        shaftMat.opacity = 0.07 + dayMix * 0.12;
        dashMat.opacity = 0.26 + dayMix * 0.1;

        atriumBusMats.forEach(item => {
          const match = item.group === state.formFocus;
          item.mat.color.copy(match ? cGold : cDim);
          item.mat.emissive.copy(match ? cGold : cMint);
          item.mat.emissiveIntensity = (match ? 1.08 : 0.22) * (0.06 + 0.94 * power(item.rail.power || 0));
        });
        line04BusMat.color.copy(state.formFocus === 'oral' || curBeat < 6.8 ? cGold : cDim);
        skyCapsMat.emissiveIntensity = state.formFocus === 'solid' ? 0.65 : 0.16;

        for (let i = 0; i < CAPS_COUNT; i++) {
          const sc = skyCurves[i % skyCurves.length];
          const slot = Math.floor(i / skyCurves.length) / (CAPS_COUNT / skyCurves.length);
          const u = (((slot + flow * 0.006) % 1) + 1) % 1;
          const capEdge = cl(Math.min(u, 1 - u) / 0.03, 0, 1);       // capsules enter and leave inside the hopper and receiver
          const pt = sc.getPointAt(u), tan = sc.getTangentAt(u);
          dummy.position.copy(pt);
          dummy.quaternion.setFromUnitVectors(upVec, tan);
          dummy.scale.setScalar(Math.max(0.0001, capEdge));
          dummy.updateMatrix();
          skyCapsInst.setMatrixAt(i, dummy.matrix);
        }
        skyCapsInst.instanceMatrix.needsUpdate = true;

        cells.forEach(c => { c.green.emissiveIntensity = 1.1 * (c.power < 0 ? 1 : power(c.power)); });
        accTables.forEach((t, ti) => { const a = flow * 0.06 * (ti ? -1 : 1); t.userData.spokes.forEach((sp, k) => { sp.rotation.y = k * Math.PI / 4 + a; }); });
        atrCarousel.rotation.y = time * 1.8;
        atrNozzles.forEach((nz, ni) => {
          nz.position.y = -0.72 + 0.24 * Math.sin(time * 4.5 + ni);
        });
        [robNorth, robSouth].forEach((rb, ri) => {
          rb.sh.rotation.y = Math.sin(time * 1.6 + ri * 2.1) * 0.78;
          rb.el.rotation.x = Math.sin(time * 2.8 + ri) * 0.32;
        });
      }

      // Camera: a spline through the cleanroom whose two ends are locked to the illustrated line's own framing of TQ-0417,
      // so the bottle stays the same size and in the same place on screen across both handoffs.
      const kf = sampleCamera(curBeat);
      vPos.copy(kf.pos);
      vTgt.copy(kf.tgt);
      let shiftX = kf.shiftX;
      // Phones: the ending card sits at the top, so the windowsill is centred lower in the frame.
      if (inHome && cam.aspect < 1) {
        vPos.set(177.9, 4.0 + (kf.pos.y - 2.8), 12.0 + (kf.pos.z - 9.8));
        vTgt.set(177.9, 4.0, -1.8);
      }
      const hxc = getHeroX(curBeat);

      const atrWeight = inHome ? 0 : sm(seg(curBeat, 7.36, 7.52)) * (1 - sm(seg(curBeat, 7.8, 7.95)));
      if (atrWeight > 0.001 && state.atriumCam !== 'crane') {
        const altPos = state.atriumCam === 'chase'
          ? new THREE.Vector3(hxc - 8.5, 4.2, 5.8)
          : new THREE.Vector3(91.5, 58.0, 4.5);
        const altTgt = state.atriumCam === 'chase'
          ? new THREE.Vector3(hxc + 2.5, 1.9, 0)
          : new THREE.Vector3(91.5, 0.5, 0);
        vPos.lerp(altPos, atrWeight);
        vTgt.lerp(altTgt, atrWeight);
        shiftX *= (1 - atrWeight);
      }

      vOff.copy(vPos).sub(vTgt);
      const orbitK = inHome ? 0.25 : 1;
      const totalYaw = (curYaw + (RM ? 0 : hoverX * 0.020)) * orbitK;
      const totalPitch = (curPitch + (RM ? 0 : hoverY * 0.014)) * orbitK;
      if (Math.abs(totalYaw) > 0.0004 || Math.abs(totalPitch) > 0.0004) {
        const radiusXZ = Math.hypot(vOff.x, vOff.z);
        const ang = Math.atan2(vOff.z, vOff.x) + totalYaw;
        vOff.x = Math.cos(ang) * radiusXZ;
        vOff.z = Math.sin(ang) * radiusXZ;
        vOff.y = Math.max(1.35, vOff.y + totalPitch * Math.max(4, radiusXZ * 0.35));
      }
      vPos.copy(vTgt).add(vOff);

      const isPortrait = cam.aspect < 1;
      const w = innerWidth, h = innerHeight;
      let fovNow = isPortrait ? kf.fov + 15 : kf.fov;
      let offX = isPortrait ? 0 : -shiftX * w;
      let offY = isPortrait && !inHome ? 0.13 * h : 0;
      const heroMidY = 1.0 + HERO_H / 2;
      // Locked to the illustrated line's framing of TQ-0417 (including its push-in) until the crossfade is complete, then
      // released gradually into the reveal spline, so the handoff has no cut and no jump.
      const lockW = curBeat <= HOLD_END ? 1 : 1 - ss(seg(curBeat, HOLD_END, 7.27));
      if (lockW > 0.001 && !inHome) {
        const fr = homeStage && homeStage.lineFraming ? homeStage.lineFraming(LINE_BEAT_OUT_OF_LINE) : null;
        if (fr) {
          const D = HERO_H * h / (2 * Math.max(8, fr.h) * Math.tan(31 * Math.PI / 360));
          vPos.lerp(vMatchPos.set(hxc, heroMidY, D), lockW);
          vTgt.lerp(vMatchTgt.set(hxc, heroMidY, 0), lockW);
          fovNow = lerp(fovNow, 31, lockW);
          offX = lerp(offX, w / 2 - fr.x, lockW);
          offY = lerp(offY, h / 2 - fr.y, lockW);
        }
      }

      cam.position.copy(vPos);
      cam.lookAt(vTgt);
      cam.fov = fovNow;
      cam.updateProjectionMatrix();
      if (Math.abs(offX) > 0.5 || Math.abs(offY) > 0.5) cam.setViewOffset(w, h, offX, offY, w, h);
      else cam.clearViewOffset();
      outlineK.value = 2 * Math.tan(fovNow * Math.PI / 360) / h;

      // Where TQ-0417 is on screen, for the lot tag (the line writes the same thing for its own frames).
      if (homeStage && homeStage.thread3d) {
        cam.updateMatrixWorld();
        vThread.copy(threadPt);
        const dist = cam.position.distanceTo(vThread);
        vThread.project(cam);
        const t3 = homeStage.thread3d;
        t3.x = (vThread.x * 0.5 + 0.5) * w;
        t3.y = (1 - (vThread.y * 0.5 + 0.5)) * h;
        t3.h = threadH * h / (2 * dist * Math.tan(fovNow * Math.PI / 360));
        t3.v = inHome ? 0 : threadV;
        t3.s = threadS;
      }

      // Skip GPU work while the illustrated line covers the stage.
      if (!homeStage || homeStage.glVisible()) renderer.render(scene, cam);
    } catch (err) {
      if (renderErrors++ < 3) console.error('Tiryaq Genesis Line error:', err);
      if (renderErrors > 20) activateFallback();
    }
  }

  requestAnimationFrame(frame);

  canvas.addEventListener('webglcontextlost', e => {
    e.preventDefault();
    activateFallback();
  });
  addEventListener('pagehide', () => {
    scene.traverse(obj => {
      obj.geometry?.dispose?.();
      if (Array.isArray(obj.material)) obj.material.forEach(m => m.dispose?.());
      else obj.material?.dispose?.();
    });
    renderer.dispose();
  });
})();
