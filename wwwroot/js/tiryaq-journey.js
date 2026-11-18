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

function makeLabelTexture() {
  const c = document.createElement('canvas');
  c.width = 1024;
  c.height = 512;
  const g = c.getContext('2d');

  g.fillStyle = '#fcf9f2';
  g.fillRect(0, 0, 1024, 512);

  g.fillStyle = '#1d5560';
  g.fillRect(0, 0, 1024, 96);

  g.fillStyle = '#f5efe3';
  g.font = '600 30px system-ui, sans-serif';
  g.fillText('TIRYAQ PHARMA', 330, 60);
  g.fillStyle = '#efb85c';
  g.font = '700 28px monospace';
  g.fillText('Rx', 652, 60);

  g.fillStyle = '#0a1f26';
  g.font = 'italic 108px Georgia, serif';
  g.fillText('Velmira', 326, 228);

  g.fillStyle = '#efb85c';
  g.fillRect(330, 256, 350, 6);

  g.fillStyle = '#2b4549';
  g.font = '500 30px system-ui, sans-serif';
  g.fillText('5 mg / 5 mL Oral Solution  ·  100 mL', 330, 312);

  g.fillStyle = '#0a1f26';
  g.font = '600 26px Consolas, monospace';
  g.fillText('LOT TQ-0417   EXP 11/2028', 330, 386);

  for (let i = 0; i < 28; i++) {
    const w = (i % 3 === 0) ? 5 : (i % 2 === 0 ? 3 : 2);
    g.fillRect(718 + i * 7, 342, w, 96);
  }

  g.fillStyle = '#1d5560';
  g.fillRect(0, 488, 1024, 24);

  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

function makeCartonTexture() {
  const c = document.createElement('canvas');
  c.width = 512;
  c.height = 1024;
  const g = c.getContext('2d');

  g.fillStyle = '#1d5560';
  g.fillRect(0, 0, 512, 1024);

  g.fillStyle = '#f5efe3';
  g.font = '88px Georgia, serif';
  g.fillText('Tiryaq', 54, 175);
  g.font = '600 22px system-ui, sans-serif';
  g.fillStyle = 'rgba(245, 239, 227, 0.78)';
  g.fillText('PHARMA  ·  ORAL CARE', 58, 218);

  g.fillStyle = '#fcf9f2';
  g.fillRect(0, 430, 512, 340);

  g.fillStyle = '#0a1f26';
  g.font = 'italic 92px Georgia, serif';
  g.fillText('Velmira', 52, 558);

  g.fillStyle = '#efb85c';
  g.fillRect(54, 588, 404, 7);

  g.fillStyle = '#2c464a';
  g.font = '500 28px system-ui, sans-serif';
  g.fillText('5 mg / 5 mL Oral Solution', 54, 646);
  g.fillText('100 mL Amber Bottle + Cup', 54, 692);

  g.fillStyle = '#efb85c';
  g.font = '600 25px Consolas, monospace';
  g.fillText('LOT TQ-0417  ·  EXP 11/2028', 54, 888);

  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
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

/* ============ PIECEWISE HERO SPECIMEN PATH ALONG LINE 04 ============ */
function getHeroX(b) {
  if (b <= 0.72) return lerp(-38.0, -37.2, sm(b / 0.72));
  if (b <= 2.62) return lerp(-37.2, 8.0, sm((b - 0.72) / 1.90));
  if (b <= 3.42) return 8.0; // Paused directly beneath Diving Dosing Nozzle
  if (b <= 4.06) return lerp(8.0, 26.0, sm((b - 3.42) / 0.64));
  if (b <= 4.92) return 26.0; // Paused at Torque Spindle & Rotary Wrap Labeller
  if (b <= 5.55) return lerp(26.0, 43.2, sm((b - 4.92) / 0.63));
  if (b <= 6.45) return lerp(43.2, 44.8, (b - 5.55) / 0.90); // Slow scan glide through Vision Arch
  return lerp(44.8, 120.0, sm(cl((b - 6.45) / 2.15, 0, 1)));
}

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
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.14;
  renderer.shadowMap.enabled = !MOB;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  const scene = new THREE.Scene();
  scene.environment = buildStudioEnv(renderer);

  const cSkyNight = new THREE.Color(0x081e26);
  const cSkyDay = new THREE.Color(0xdcebe7);
  const cSkyHome = new THREE.Color(0xf4e8d4);
  const cGndNight = new THREE.Color(0x0b2630);
  const cGndDay = new THREE.Color(0xc8e0db);

  const curSky = cSkyNight.clone();
  const curGnd = cGndNight.clone();
  scene.background = curSky;
  scene.fog = new THREE.Fog(0x081e26, 30, 112);

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

  /* ---------- Studio Physically-Based Materials ---------- */
  const matSteel = new THREE.MeshStandardMaterial({ color: 0xdbe6e4, roughness: 0.22, metalness: 0.78, envMapIntensity: 1.1 });
  const matChrome = new THREE.MeshStandardMaterial({ color: 0xf2f7f6, roughness: 0.12, metalness: 0.9, envMapIntensity: 1.25 });
  const matAnodized = new THREE.MeshStandardMaterial({ color: 0x13353f, roughness: 0.36, metalness: 0.35, envMapIntensity: 0.6, side: THREE.DoubleSide });
  const matChassis = new THREE.MeshStandardMaterial({ color: 0xf2efe7, roughness: 0.34, metalness: 0.08, envMapIntensity: 0.45, side: THREE.DoubleSide });
  const matTeal = new THREE.MeshStandardMaterial({ color: 0x1d5560, roughness: 0.26, metalness: 0.18, envMapIntensity: 0.7 });
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
    const pos = [], idx = [];
    const profile = [[[-1, h1], [1, h1]], [[1, h1], [1, h0]], [[-1, h0], [-1, h1]]];
    for (let i = 0; i <= segs; i++) {
      const t = i / segs;
      const p = curve.getPointAt(t);
      const tan = curve.getTangentAt(t);
      const lenXZ = Math.hypot(tan.x, tan.z) || 1;
      const nx = -tan.z / lenXZ, nz = tan.x / lenXZ;
      profile.forEach(face => face.forEach(([s, dy]) => {
        pos.push(p.x + nx * s * hw, p.y + dy, p.z + nz * s * hw);
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
  const vesselPts = [];
  vesselPts.push(new THREE.Vector2(0.0, 0.0));
  vesselPts.push(new THREE.Vector2(0.285, 0.0));
  // Smooth base heel fillet
  for (let i = 0; i <= 5; i++) {
    const a = (i / 5) * (Math.PI * 0.5);
    vesselPts.push(new THREE.Vector2(0.285 + Math.sin(a) * 0.055, 0.055 - Math.cos(a) * 0.055));
  }
  vesselPts.push(new THREE.Vector2(0.34, 0.50));
  // Silky 14-step cosine shoulder curve (zero polygonal faceting!)
  for (let i = 1; i <= 14; i++) {
    const t = i / 14;
    const r = lerp(0.34, 0.146, sm(t));
    const y = lerp(0.50, 0.79, t);
    vesselPts.push(new THREE.Vector2(r, y));
  }
  vesselPts.push(new THREE.Vector2(0.146, 0.93));
  vesselPts.push(new THREE.Vector2(0.166, 0.945));
  vesselPts.push(new THREE.Vector2(0.166, 0.965));
  vesselPts.push(new THREE.Vector2(0.132, 0.965));

  const vesselGeo = new THREE.LatheGeometry(vesselPts, 48);
  const liqUnitGeo = new THREE.CylinderGeometry(0.302, 0.302, 0.51, 32).translate(0, 0.255, 0);
  const capUnitGeo = new THREE.CylinderGeometry(0.184, 0.184, 0.17, 28).translate(0, 0.95, 0);

  /* =========================================================================
     ZONE 0: LINE 04 — PRECISION ANODIZED & CHROME RAIL (x = -72 .. +128)
     ========================================================================= */
  const line04Curve = new THREE.LineCurve3(new THREE.Vector3(-72, 0, 0), new THREE.Vector3(128, 0, 0));
  factoryWorld.add(
    sweepChannel(line04Curve, 1.16, 0.62, 0.93, 160, matAnodized),
    sweepChannel(line04Curve, 1.19, 0.91, 0.955, 160, matChrome),
    sweepChannel(line04Curve, 0.95, 0.945, 1.00, 160, matSpineBed)
  );
  const line04BusMat = new THREE.MeshStandardMaterial({ color: 0xefb85c, emissive: 0xd98a2b, emissiveIntensity: 0.72, roughness: 0.25 });
  factoryWorld.add(sweepChannel(line04Curve, 0.075, 0.996, 1.018, 160, line04BusMat));

  // Brushed stainless twin guide-rails along Line 04
  [-0.86, 0.86].forEach(zSide => {
    const railLow = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 198, 14), matChrome);
    railLow.rotation.z = Math.PI / 2;
    railLow.position.set(28, 1.32, zSide);
    factoryWorld.add(railLow);

    const railHigh = new THREE.Mesh(new THREE.CylinderGeometry(0.022, 0.022, 198, 14), matSteel);
    railHigh.rotation.z = Math.PI / 2;
    railHigh.position.set(28, 1.62, zSide);
    factoryWorld.add(railHigh);
  });

  // Precision spoked drive wheels & stanchions underneath Line 04
  const wheelCount = 82;
  const wheelGeo = new THREE.CylinderGeometry(0.23, 0.23, 0.14, 22);
  wheelGeo.rotateX(Math.PI / 2);
  const wheelRimMat = new THREE.MeshStandardMaterial({ color: 0x163d47, roughness: 0.3, metalness: 0.55 });
  const wheelsInst = new THREE.InstancedMesh(wheelGeo, wheelRimMat, wheelCount);
  const hubInst = new THREE.InstancedMesh(new THREE.BoxGeometry(0.41, 0.038, 0.16), matChrome, wheelCount);

  const stanchionPositions = [];
  for (let i = 0; i < wheelCount; i++) stanchionPositions.push(-66 + i * 2.35);

  const stanchionInst = new THREE.InstancedMesh(new THREE.BoxGeometry(0.055, 0.70, 0.055), matChrome, stanchionPositions.length * 2);
  const dummy = new THREE.Object3D();
  let stIdx = 0;
  stanchionPositions.forEach(sx => {
    [-0.86, 0.86].forEach(sz => {
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
  const HERO_SCALE = 2.12;
  const heroBottle = new THREE.Group();

  const hbOuter = new THREE.Mesh(vesselGeo, glassOuterMat);
  const hbInner = new THREE.Mesh(vesselGeo, glassRimMat);
  hbInner.scale.setScalar(0.95);
  hbOuter.castShadow = !MOB;
  hbOuter.renderOrder = 3;
  heroBottle.add(hbInner, hbOuter);

  // Precision neck threads
  for (let ti = 0; ti < 3; ti++) {
    const th = new THREE.Mesh(new THREE.TorusGeometry(0.15, 0.0085, 8, 36), glassOuterMat);
    th.rotation.x = Math.PI / 2;
    th.position.y = 0.835 + ti * 0.032;
    heroBottle.add(th);
  }

  // Frosted laser-etched TQ-0417 base mark
  const etchMesh = new THREE.Mesh(
    new THREE.CylinderGeometry(0.343, 0.343, 0.10, 32, 1, true, -0.52, 1.04),
    new THREE.MeshBasicMaterial({ map: makeEtchTexture(), transparent: true, depthWrite: false })
  );
  etchMesh.position.y = 0.085;
  etchMesh.renderOrder = 4;
  heroBottle.add(etchMesh);

  // Dynamic amber oral solution inside TQ-0417
  const heroLiqMat = new THREE.MeshPhysicalMaterial({
    color: 0xe89c2c,
    emissive: 0x945206,
    emissiveIntensity: 0.46,
    roughness: 0.12,
    clearcoat: 0.75,
    transparent: true,
    opacity: 0.93
  });
  const heroLiq = new THREE.Mesh(liqUnitGeo, heroLiqMat);
  heroLiq.position.y = 0.03;
  heroLiq.scale.set(1, 0.0001, 1);
  heroLiq.visible = false;
  heroLiq.renderOrder = 1;
  heroBottle.add(heroLiq);

  // Shimmering surface meniscus disc + rising micro-bubbles during dosing!
  const meniscusDisc = new THREE.Mesh(
    new THREE.CircleGeometry(0.301, 36),
    new THREE.MeshBasicMaterial({ color: 0xffe29e, transparent: true, opacity: 0.82, side: THREE.DoubleSide })
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

  // Dynamic deep-teal ribbed closure + gold tamper band
  const heroCapGrp = new THREE.Group();
  const capBody = new THREE.Mesh(new THREE.CylinderGeometry(0.186, 0.189, 0.16, 32), matTeal);
  const tamperBand = new THREE.Mesh(new THREE.CylinderGeometry(0.192, 0.192, 0.028, 32), matGold);
  tamperBand.position.y = -0.088;
  heroCapGrp.add(capBody, tamperBand);
  for (let ri = 0; ri < 20; ri++) {
    const a = (ri / 20) * Math.PI * 2;
    const rib = new THREE.Mesh(new THREE.BoxGeometry(0.013, 0.14, 0.016), matChassis);
    rib.position.set(Math.cos(a) * 0.188, 0.005, Math.sin(a) * 0.188);
    rib.rotation.y = -a;
    heroCapGrp.add(rib);
  }
  heroCapGrp.visible = false;
  heroBottle.add(heroCapGrp);

  // Dynamic wrap-around Velmira pharmaceutical label
  const heroLabelMat = new THREE.MeshStandardMaterial({
    map: makeLabelTexture(),
    roughness: 0.34,
    side: THREE.DoubleSide
  });
  const heroLabel = new THREE.Mesh(
    new THREE.CylinderGeometry(0.345, 0.345, 0.31, 48, 1, true, -Math.PI * 0.86, Math.PI * 1.72),
    heroLabelMat
  );
  heroLabel.position.y = 0.325;
  heroLabel.visible = false;
  heroLabel.renderOrder = 4;
  heroBottle.add(heroLabel);

  // Subtle base reticle / tracking halo
  const heroHaloMat = new THREE.MeshBasicMaterial({ color: 0xefb85c, transparent: true, opacity: 0.25, depthWrite: false });
  const heroHalo = new THREE.Mesh(new THREE.RingGeometry(0.44, 0.50, 48), heroHaloMat);
  heroHalo.rotation.x = -Math.PI / 2;
  heroHalo.position.y = 0.012;
  heroBottle.add(heroHalo);

  const heroGlowLight = new THREE.PointLight(0xffe5b0, 1.25, 11);
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
  factoryWorld.add(
    sweepChannel(rejectSpurCurve, 0.72, 0.58, 0.90, 28, matAnodized),
    sweepChannel(rejectSpurCurve, 0.55, 0.89, 0.98, 28, matCoral)
  );
  const rejectBottle = new THREE.Group();
  const rbGlass = new THREE.Mesh(vesselGeo, glassOuterMat);
  const rbLowLiq = new THREE.Mesh(liqUnitGeo, new THREE.MeshStandardMaterial({ color: 0xd98a2b, roughness: 0.3 }));
  rbLowLiq.scale.set(0.96, 0.40, 0.96);
  rejectBottle.add(rbGlass, rbLowLiq);
  rejectBottle.scale.setScalar(HERO_SCALE * 0.92);
  factoryWorld.add(rejectBottle);

  /* =========================================================================
     STATION 05 (x = +57 .. +126): THE 12-RAIL SUNLIT CLEANROOM ATRIUM
     Dual-sided West Airlock Wall: dark cleanroom cladding on west, bright white on east!
     ========================================================================= */
  addBox(0.45, 13, 20, matDeepTeal, 56.25, 6.5, -13.5);
  addBox(0.45, 13, 20, matDeepTeal, 56.25, 6.5, 13.5);
  addBox(0.45, 7.2, 7.2, matDeepTeal, 56.25, 9.4, 0);

  addBox(0.55, 13, 20, matChassis, 56.8, 6.5, -13.5);
  addBox(0.55, 13, 20, matChassis, 56.8, 6.5, 13.5);
  addBox(0.55, 7.2, 7.2, matChassis, 56.8, 9.4, 0);
  addBox(1.15, 0.30, 7.2, matGold, 56.55, 5.75, 0);

  // East Golden Dispatch Threshold Wall (x = 126)
  addBox(1.2, 16, 28, matChassis, 126.5, 8, -17.5);
  addBox(1.2, 16, 28, matChassis, 126.5, 8, 17.5);
  addBox(1.2, 10, 7.2, matChassis, 126.5, 11, 0);
  const portalGlow = new THREE.Mesh(
    new THREE.PlaneGeometry(6.8, 5.8),
    new THREE.MeshBasicMaterial({ color: 0xfff5dc, side: THREE.DoubleSide })
  );
  portalGlow.rotation.y = -Math.PI / 2;
  portalGlow.position.set(125.8, 2.9, 0);
  factoryWorld.add(portalGlow);

  // 8 Atrium Conveyor Rails + Line 04 + 3 Glass Sky-Tubes = 12 Synchronized Atrium Lines
  const mkAtriumWave = (zBase, y0, amp, ph, dir) => {
    const pts = [];
    for (let x = 57; x <= 126; x += 5.5) {
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
      c: mkCurve([[57, 2.5, 19], [76, 3.1, 8], [94, 3.2, -4], [112, 2.9, -14], [126, 2.5, -19]]),
      dir: 1, bridge: true, group: 'oral', amber: true, hw: 0.92, speed: 1.24
    },
    {
      c: mkCurve([[57, 2.5, -19], [76, 3.0, -9], [94, 3.2, 4], [112, 2.9, 14], [126, 2.5, 19]]),
      dir: -1, bridge: true, group: 'sterile', amber: false, hw: 0.92, speed: 1.20
    },
    { ...mkAtriumLoop(78, -11.8, 6.2, 3.8, 1),    group: 'sterile', amber: false, hw: 0.9, speed: 0.92 },
    { ...mkAtriumLoop(106, 11.8, 6.2, 3.8, -1),   group: 'topical', amber: true,  hw: 0.9, speed: 0.95 }
  ];

  const trafficRails = [
    ...feederCurves.map((fc, fi) => ({ c: fc, dir: 1, amber: fi === 1, feederIdx: fi, hw: 0.76, speed: 0.85, group: 'feeder' })),
    {
      c: new THREE.LineCurve3(new THREE.Vector3(58, 0, 0), new THREE.Vector3(125, 0, 0)),
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
      atriumBusMats.push({ group: r.group, mat: busMat });

      factoryWorld.add(
        sweepChannel(r.c, r.hw, 0.58, 0.90, segs, matChassis),
        sweepChannel(r.c, r.hw * 0.78, 0.89, 1.00, segs, r.bridge ? matBridgeBed : matSpineBed),
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

  const totalTraffic = trafficRails.reduce((s, r) => s + r.n, 0);
  const instGlass = new THREE.InstancedMesh(
    vesselGeo,
    new THREE.MeshPhysicalMaterial({ color: 0xffffff, transparent: true, opacity: 0.38, roughness: 0.06, clearcoat: 1.0, envMapIntensity: 1.3 }),
    totalTraffic
  );
  const instLiq = new THREE.InstancedMesh(
    liqUnitGeo,
    new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.22 }),
    totalTraffic
  );
  const instCap = new THREE.InstancedMesh(
    capUnitGeo,
    new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.28 }),
    totalTraffic
  );
  const colAmber = new THREE.Color(0xefb85c), colTeal = new THREE.Color(0x28a4a0);
  const colCapTeal = new THREE.Color(0x1d5560), colCapIvory = new THREE.Color(0xf5efe3);
  let tCursor = 0;
  trafficRails.forEach(r => {
    for (let j = 0; j < r.n; j++) {
      const amb = r.amber ? (j % 4 !== 0) : (j % 4 === 0);
      instLiq.setColorAt(tCursor, amb ? colAmber : colTeal);
      instCap.setColorAt(tCursor, amb ? colCapTeal : colCapIvory);
      tCursor++;
    }
  });
  instLiq.instanceColor.needsUpdate = true;
  instCap.instanceColor.needsUpdate = true;
  factoryWorld.add(instGlass, instLiq, instCap);

  // 3 Overhead Clearcoat Glass Pneumatic Capsule Sky-Tubes
  const skyTubeMat = new THREE.MeshPhysicalMaterial({
    color: 0xffffff, transparent: true, opacity: 0.28, roughness: 0.04, clearcoat: 1.0, envMapIntensity: 1.4
  });
  const skyCurves = [
    mkCurve([[57, 4.8, -6], [78, 6.8, -12], [102, 6.5, 4], [126, 4.8, 8]]),
    mkCurve([[57, 5.2, 7],  [80, 7.2, 11],  [104, 6.6, -5], [126, 5.0, -7]]),
    mkCurve([[57, 5.6, 0],  [84, 7.6, -2],  [106, 7.2, 2],  [126, 5.4, 0]])
  ];
  skyCurves.forEach(sc => factoryWorld.add(new THREE.Mesh(new THREE.TubeGeometry(sc, 56, 0.28, 14, false), skyTubeMat)));

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
  const cartonSideMat = new THREE.MeshStandardMaterial({ color: 0x16444e, roughness: 0.44 });
  const cartonMats = [cartonSideMat, cartonSideMat, cartonSideMat, cartonSideMat, cartonFrontMat, cartonSideMat];
  for (let ci = 0; ci < 8; ci++) {
    const dc = new THREE.Mesh(new THREE.BoxGeometry(1.1, 1.45, 1.0), cartonMats);
    dc.position.set(114 + (ci % 4) * 1.6, 0.75 + (ci > 3 ? 1.48 : 0), -4.2);
    dc.castShadow = !MOB;
    factoryWorld.add(dc);
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
  addBox(14.65, 12, 0.36, matSunlitPlaster, -6.675, 5.5, -1.8, homeWorld);
  // Right plaster wall (+5.15 .. +14) — leaves warm plaster margin on right of window too!
  addBox(8.85, 12, 0.36, matSunlitPlaster, 9.575, 5.5, -1.8, homeWorld);
  // Lower & upper wall aprons framing the window cleanly below the top navbar
  addBox(4.50, 1.52, 0.36, matSunlitPlaster, 2.90, 0.68, -1.8, homeWorld);
  addBox(4.50, 7.30, 0.36, matSunlitPlaster, 2.90, 7.80, -1.8, homeWorld);
  addBox(28, 0.2, 18, matSunlitPlaster, 0, -0.1, 0, homeWorld);

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
    new THREE.MeshBasicMaterial({ color: 0xfff7dd, transparent: true, opacity: 0.16, side: THREE.DoubleSide, depthWrite: false })
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

  // 2) Folded 3D Tiryaq · Velmira Carton (x = 1.92)
  const homeCarton = new THREE.Mesh(new THREE.BoxGeometry(0.72, 1.32, 0.60), cartonMats);
  homeCarton.position.set(1.92, 1.405 + 0.66, -1.14);
  homeCarton.rotation.y = 0.12;
  homeCarton.castShadow = !MOB;
  homeWorld.add(homeCarton);
  const openFlap = addBox(0.70, 0.26, 0.035, cartonSideMat, 1.92, 1.405 + 1.43, -1.41, homeWorld);
  openFlap.rotation.y = 0.12;
  openFlap.rotation.x = -0.22;

  // 3) Completed 3D Bottle Lot TQ-0417 (x = 2.92 — hero centerpiece on the sill!)
  const homeBottle = new THREE.Group();
  const hbHomeGlass = new THREE.Mesh(vesselGeo, glassOuterMat);
  const hbHomeInner = new THREE.Mesh(vesselGeo, glassRimMat);
  hbHomeInner.scale.setScalar(0.95);
  const hbHomeLiq = new THREE.Mesh(liqUnitGeo, heroLiqMat);
  hbHomeLiq.position.y = 0.03;
  const hbHomeCap = heroCapGrp.clone();
  hbHomeCap.visible = true;
  hbHomeCap.position.y = 0.94;
  const hbHomeLbl = new THREE.Mesh(
    new THREE.CylinderGeometry(0.345, 0.345, 0.31, 48, 1, true, -Math.PI * 0.86, Math.PI * 1.72),
    heroLabelMat
  );
  hbHomeLbl.position.y = 0.325;
  hbHomeGlass.renderOrder = 3;
  hbHomeLbl.renderOrder = 4;
  homeBottle.add(hbHomeInner, hbHomeGlass, hbHomeLiq, hbHomeCap, hbHomeLbl);
  homeBottle.scale.setScalar(1.20);
  homeBottle.position.set(2.92, 1.405, -1.06);
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
  const CAM_KF = [
    // Beat 0: Macro Studio Portrait — shows full crystal bottle, chrome rails & spoked drive wheels!
    { b: 0.00, pos: [-38.0, 2.15, 7.15],  tgt: [-38.0, 1.88, 0.0],  shiftX: 0.18,  fov: 31 },
    { b: 0.72, pos: [-37.2, 2.22, 7.25],  tgt: [-37.2, 1.90, 0.0],  shiftX: 0.18,  fov: 31 },
    // Beat 1: Sourcing Convergence — 3 supplier streams & Twin-Bridge 4-Tower Gate
    { b: 1.62, pos: [-21.5, 6.6, 13.8],   tgt: [-15.2, 1.95, -1.0], shiftX: 0.16,  fov: 34 },
    // Beat 2: Formulation & CNC Diving Nozzle Filling
    { b: 2.65, pos: [6.4, 3.05, 8.4],     tgt: [7.5, 2.25, -0.5],   shiftX: -0.17, fov: 33 },
    { b: 3.40, pos: [7.2, 2.98, 8.2],     tgt: [7.8, 2.20, -0.4],   shiftX: -0.17, fov: 33 },
    // Beat 3: Torque Sealing & Rotary Wrap Label
    { b: 4.10, pos: [27.4, 2.95, 8.0],    tgt: [26.0, 2.15, -0.25], shiftX: 0.17,  fov: 33 },
    { b: 4.90, pos: [27.8, 2.90, 7.9],    tgt: [26.1, 2.12, -0.20], shiftX: 0.17,  fov: 33 },
    // Beat 4: Dark-Field 5-Check Vision Portal & HUD Screen
    { b: 5.65, pos: [42.6, 3.00, 8.7],    tgt: [44.1, 2.28, 0.10],  shiftX: -0.17, fov: 34 },
    { b: 6.45, pos: [43.4, 3.08, 8.8],    tgt: [44.8, 2.28, 0.20],  shiftX: -0.17, fov: 34 },
    // Beat 5: Daylight Bloom & 12-Rail Cleanroom Atrium Crane Panorama
    { b: 7.25, pos: [65.5, 16.5, 24.5],   tgt: [89.0, 2.0, -0.5],   shiftX: 0.0,   fov: 37 },
    { b: 8.25, pos: [76.0, 14.5, 21.5],   tgt: [98.0, 2.2, 0.0],    shiftX: 0.0,   fov: 36 },
    // Golden Dispatch Portal -> Sunlit Home Windowsill (framed cleanly on right, warm plaster on left!)
    { b: 8.58, pos: [118.5, 3.0, 4.2],    tgt: [126.0, 2.8, 0.0],   shiftX: 0.0,   fov: 35 },
    { b: 8.85, pos: [175.85, 2.58, 7.85], tgt: [175.85, 2.52, -1.2],shiftX: 0.0,   fov: 33 },
    { b: 10.0, pos: [175.85, 2.55, 7.55], tgt: [175.85, 2.50, -1.2],shiftX: 0.0,   fov: 32 }
  ];

  function sampleCamera(b) {
    if (b <= CAM_KF[0].b) return { ...CAM_KF[0] };
    const lastIdx = CAM_KF.length - 1;
    if (b >= CAM_KF[lastIdx].b) return { ...CAM_KF[lastIdx] };
    for (let i = 0; i < lastIdx; i++) {
      const a = CAM_KF[i], c = CAM_KF[i + 1];
      if (b >= a.b && b <= c.b) {
        const k = sm((b - a.b) / Math.max(0.0001, c.b - a.b));
        return {
          pos: [lerp(a.pos[0], c.pos[0], k), lerp(a.pos[1], c.pos[1], k), lerp(a.pos[2], c.pos[2], k)],
          tgt: [lerp(a.tgt[0], c.tgt[0], k), lerp(a.tgt[1], c.tgt[1], k), lerp(a.tgt[2], c.tgt[2], k)],
          shiftX: lerp(a.shiftX, c.shiftX, k),
          fov: lerp(a.fov, c.fov, k)
        };
      }
    }
    return { ...CAM_KF[0] };
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
  const cGold = new THREE.Color(0xefb85c);
  const cMint = new THREE.Color(0x5de2b0);
  const cDim = new THREE.Color(0x2d545c);

  let curBeat = 0, tgtBeat = 0, time = 0, lastNow = performance.now(), renderErrors = 0;

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

    tgtBeat = cl(((scrollY - topOff) / span) * 10, 0, 10);
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

    const veilAlpha = Math.max(0, 1 - Math.abs((curBeat - 8.61) / 0.23));
    if (sunVeil) sunVeil.style.opacity = (sm(veilAlpha) * 0.94).toFixed(3);

    const inHome = curBeat >= 8.61;
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
      const dayMix = sm(cl((curBeat - 6.62) / 0.55, 0, 1));
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
        heroBottle.position.set(hx, 1.0, 0);
        // Sweeps Velmira front typography smoothly INTO camera view as label wraps!
        heroBottle.rotation.y = (labelFrac > 0 && labelFrac < 1) ? (1 - labelFrac) * Math.PI * 0.65 : 0;

        keyLight.position.set(hx + 8, 28, 18);
        keyLight.target.position.set(hx, 1.5, 0);
        keyLight.target.updateMatrixWorld();

        heroLiq.visible = fillFrac > 0.004;
        heroLiq.scale.set(1, Math.max(0.001, fillFrac), 1);
        meniscusDisc.visible = fillFrac > 0.01;
        meniscusDisc.position.y = 0.032 + fillFrac * 0.51;

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
        heroCapGrp.position.y = lerp(1.36, 0.94, capFrac);
        heroCapGrp.rotation.y = capFrac * Math.PI * 4;

        heroLabel.visible = labelFrac > 0.02;
        heroLabel.scale.set(1, Math.max(0.02, labelFrac), 1);

        const originReticle = (1 - sm(cl(curBeat / 0.75, 0, 1))) * 0.24;
        heroHaloMat.opacity = originReticle + dayMix * 0.85;
        heroHalo.scale.setScalar(1 + 0.08 * Math.sin(time * 3.8));

        // Drive wheels turning beneath Line 04
        const wheelSpin = hx * 1.4 + time * (curBeat > 6.6 ? 2.8 : 0.35);
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

        // Station 05: 12-Rail Atrium Traffic, Differentiated Feeder Cargo & Sky-Tubes
        const flow = time * 3.2 + Math.abs(curBeat - prevBeat) * 45;
        let vCur = 0;
        trafficRails.forEach(r => {
          for (let j = 0; j < r.n; j++) {
            let u = (j / r.n + (r.dir * flow * r.speed) / r.len) % 1;
            if (u < 0) u += 1;
            const p = r.c.getPointAt(u);
            let sc = r.closed ? 1.15 : cl(Math.min(u, 1 - u) / 0.04, 0, 1) * 1.15;
            if (r.atriumSpine && Math.abs(p.x - hx) < 2.2) sc = 0;

            dummy.position.set(p.x, p.y + 1.0, p.z);
            dummy.rotation.set(0, 0, 0);
            dummy.scale.setScalar(sc || 0.0001);
            dummy.updateMatrix();
            instGlass.setMatrixAt(vCur, dummy.matrix);

            // Differentiate Supplier Stream 0 (pure empty glass vessels!) vs Active/Closure streams
            if (r.feederIdx === 0) {
              dummy.scale.setScalar(0.0001);
              dummy.updateMatrix();
              instLiq.setMatrixAt(vCur, dummy.matrix);
              instCap.setMatrixAt(vCur, dummy.matrix);
            } else {
              instLiq.setMatrixAt(vCur, dummy.matrix);
              instCap.setMatrixAt(vCur, dummy.matrix);
            }
            vCur++;
          }
        });
        instGlass.instanceMatrix.needsUpdate = true;
        instLiq.instanceMatrix.needsUpdate = true;
        instCap.instanceMatrix.needsUpdate = true;

        atriumBusMats.forEach(item => {
          const match = item.group === state.formFocus;
          item.mat.color.copy(match ? cGold : cDim);
          item.mat.emissive.copy(match ? cGold : cMint);
          item.mat.emissiveIntensity = match ? 1.08 : 0.22;
        });
        line04BusMat.color.copy(state.formFocus === 'oral' || curBeat < 6.8 ? cGold : cDim);
        skyCapsMat.emissiveIntensity = state.formFocus === 'solid' ? 0.65 : 0.16;

        for (let i = 0; i < CAPS_COUNT; i++) {
          const sc = skyCurves[i % skyCurves.length];
          const slot = Math.floor(i / skyCurves.length) / (CAPS_COUNT / skyCurves.length);
          const u = (((slot + flow * 0.016) % 1) + 1) % 1;
          const pt = sc.getPointAt(u), tan = sc.getTangentAt(u);
          dummy.position.copy(pt);
          dummy.quaternion.setFromUnitVectors(upVec, tan);
          dummy.scale.setScalar(1);
          dummy.updateMatrix();
          skyCapsInst.setMatrixAt(i, dummy.matrix);
        }
        skyCapsInst.instanceMatrix.needsUpdate = true;

        atrCarousel.rotation.y = time * 1.8;
        atrNozzles.forEach((nz, ni) => {
          nz.position.y = -0.72 + 0.24 * Math.sin(time * 4.5 + ni);
        });
        [robNorth, robSouth].forEach((rb, ri) => {
          rb.sh.rotation.y = Math.sin(time * 1.6 + ri * 2.1) * 0.78;
          rb.el.rotation.x = Math.sin(time * 2.8 + ri) * 0.32;
        });
      }

      // Camera Spline + Atrium Rig Override + Orbit + Portrait Mobile Framing
      const kf = sampleCamera(curBeat);
      vPos.set(kf.pos[0], kf.pos[1], kf.pos[2]);
      vTgt.set(kf.tgt[0], kf.tgt[1], kf.tgt[2]);
      let shiftX = kf.shiftX;

      const atrWeight = sm(cl((curBeat - 6.85) / 0.35, 0, 1)) * (1 - sm(cl((curBeat - 8.20) / 0.28, 0, 1)));
      if (atrWeight > 0.001 && state.atriumCam !== 'crane') {
        const hx = getHeroX(curBeat);
        const altPos = state.atriumCam === 'chase'
          ? new THREE.Vector3(hx - 8.5, 4.2, 5.8)
          : new THREE.Vector3(91.5, 58.0, 4.5);
        const altTgt = state.atriumCam === 'chase'
          ? new THREE.Vector3(hx + 2.5, 1.9, 0)
          : new THREE.Vector3(91.5, 0.5, 0);
        vPos.lerp(altPos, atrWeight);
        vTgt.lerp(altTgt, atrWeight);
        shiftX *= (1 - atrWeight);
      }

      vOff.copy(vPos).sub(vTgt);
      const totalYaw = curYaw + (RM ? 0 : hoverX * 0.020);
      const totalPitch = curPitch + (RM ? 0 : hoverY * 0.014);
      if (Math.abs(totalYaw) > 0.0004 || Math.abs(totalPitch) > 0.0004) {
        const radiusXZ = Math.hypot(vOff.x, vOff.z);
        const ang = Math.atan2(vOff.z, vOff.x) + totalYaw;
        vOff.x = Math.cos(ang) * radiusXZ;
        vOff.z = Math.sin(ang) * radiusXZ;
        vOff.y = Math.max(1.35, vOff.y + totalPitch * Math.max(4, radiusXZ * 0.35));
      }

      cam.position.copy(vTgt).add(vOff);
      cam.lookAt(vTgt);

      const isPortrait = cam.aspect < 1;
      const targetFov = isPortrait ? kf.fov + 15 : kf.fov;
      if (Math.abs(cam.fov - targetFov) > 0.05) {
        cam.fov = targetFov;
        cam.updateProjectionMatrix();
      }

      const w = innerWidth, h = innerHeight;
      if (!isPortrait && Math.abs(shiftX) > 0.001) {
        cam.setViewOffset(w, h, -shiftX * w, 0, w, h);
      } else if (isPortrait && !inHome) {
        // Shift 3D focal subject into the upper open half above bottom mobile cards!
        cam.setViewOffset(w, h, 0, 0.13 * h, w, h);
      } else if (isPortrait && inHome) {
        // Shift windowsill cleanly into lower half below top mobile finale headline!
        cam.setViewOffset(w, h, 0.22 * w, -0.20 * h, w, h);
      } else {
        cam.clearViewOffset();
      }

      renderer.render(scene, cam);
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
