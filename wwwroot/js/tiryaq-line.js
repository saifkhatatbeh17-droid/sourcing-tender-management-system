// Tiryaq homepage, acts 1 and 3: the illustrated production line (hero to packaging) and the windowsill ending.
// The page controller (tiryaq-home-stage.js) decides the beat B; this file only draws it and syncs the line cards.
(() => {
'use strict';
/* =========================================================================
   Tiryaq Pharma — "From the smallest detail to the greatest human impact"
   One scroll value B (0..10) drives everything: camera, machines, bottle state, cards.
   Scrolling back reverses every state, because nothing stores progress; it is all derived from B.
   ========================================================================= */
const $ = s => document.querySelector(s), $$ = s => [...document.querySelectorAll(s)];
const RM = matchMedia('(prefers-reduced-motion: reduce)').matches;
const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const sstep = t => t * t * (3 - 2 * t);
const lerp = (a, b, t) => a + (b - a) * t;
const seg = (b, a, c) => clamp((b - a) / (c - a));            // linear 0..1 over [a,c]
const sm = (b, a, c) => sstep(seg(b, a, c));                   // eased
const path = (b, pts) => {                                      // eased piecewise [[beat, value], ...]
  if (b <= pts[0][0]) return pts[0][1];
  for (let i = 0; i < pts.length - 1; i++) if (b <= pts[i + 1][0]) return lerp(pts[i][1], pts[i + 1][1], sm(b, pts[i][0], pts[i + 1][0]));
  return pts[pts.length - 1][1];
};
const BMAX = 10;
const cv = $('#line-canvas'), navbar = $('.pn-overlay'), g = cv && cv.getContext && cv.getContext('2d');
const stage = () => window.TiryaqStage;
if (!g) { document.documentElement.classList.add('ln-nocanvas'); return; }

/* ---------------- palette ---------------- */
const P = { night: '#0c252d', deep: '#11333d', teal: '#1d5560', teal2: '#2a6b74', sage: '#8db8a8', mint: '#cfe6da', ivory: '#f4eee2', paper: '#fbf7ef',
  amber: '#d98a2b', honey: '#efb85c', coral: '#e07a5f', peach: '#efc4a8', ink: '#0a1f26', signal: '#6fe0a8',
  s1: '#e7ecec', s2: '#c4cfd0', s3: '#8e9fa2', s4: '#5b6e72', s5: '#33464b' };

/* ---------------- viewport ---------------- */
let W = 0, H = 0, DPR = 1, BZ = 1, navClearance = 0, portrait = false;
function resize() {
  DPR = Math.min(devicePixelRatio || 1, 2); W = innerWidth; H = innerHeight; portrait = W / H < .9;
  cv.width = Math.round(W * DPR); cv.height = Math.round(H * DPR);
  BZ = portrait ? Math.min(W / 620, H / 900) : Math.min(W / 1180, H / 820);
  navClearance = (navbar?.offsetHeight ?? 0) + 8;
}
addEventListener('resize', resize); resize();

/* ---------------- journey data ---------------- */
// Station positions along the conveyor (world units; belt top is y = 0, up is negative).
const X = { form: 420, fill: 900, cap: 1500, label: 2100, qc: 2700, pack: 3300, dock: 4150 };
const heroPath = [[0, 0], [.85, 0], [3.1, 900], [4.0, 900], [4.4, 1500], [5.1, 1500], [5.45, 2100], [6.1, 2100], [6.5, 2700], [7.3, 2700], [7.62, 3300]];
const cartonPath = [[8.45, 3300], [9.05, 3880], [9.5, 4080]];
// Camera keyframes: [beat, x, y, zoom, screen-offset-x, screen-offset-y]
const CAM = [
  [0, 0, -88, 3.3, .2, .02], [.7, 0, -88, 3.0, .2, .02],
  [1.45, 1750, -470, .3, 0, -.04], [1.85, 1750, -470, .31, 0, -.04],
  [2.2, 430, -770, 1.12, .17, 0], [2.6, 430, -760, 1.22, .17, 0],
  [2.95, 720, -540, 1.35, 0, 0], [3.25, 900, -200, 1.9, -.16, 0], [3.85, 900, -150, 2.6, -.16, .02],
  [4.15, 1200, -150, 1.4, 0, 0], [4.45, 1500, -230, 1.75, .16, 0], [4.95, 1500, -158, 3.3, .16, .02],
  [5.25, 1800, -150, 1.4, 0, 0], [5.55, 2100, -110, 1.75, -.16, 0], [5.92, 2100, -99, 2.75, -.16, 0], [6.1, 2100, -99, 2.75, -.16, 0],
  [6.3, 2450, -180, 1.35, 0, 0], [6.55, 2730, -130, 2.4, .17, 0], [7.25, 2730, -110, 2.7, .17, 0],
  [7.5, 3000, -200, 1.35, 0, 0], [7.75, 3300, -170, 1.55, -.16, 0], [8.42, 3300, -135, 2.0, -.16, 0],
  [8.9, 2050, -560, .26, 0, -.17], [9.2, 2250, -560, .275, 0, -.17],
  [9.45, 4205, -200, 1.1, 0, 0], [9.75, 4205, -150, 7, 0, 0], [10, 4205, -150, 7, 0, 0]
];
function camAt(b) {
  let i = 0; while (i < CAM.length - 2 && b > CAM[i + 1][0]) i++;
  const a = CAM[i], c = CAM[i + 1], k = sm(b, a[0], c[0]);
  const homeIn = sm(b, 9.4, 9.6);   // on phones the cards sit at the bottom, so the subject moves up and stays centred
  const camera = { x: lerp(a[1], c[1], k), y: lerp(a[2], c[2], k), z: Math.exp(lerp(Math.log(a[3]), Math.log(c[3]), k)), ox: portrait ? 0 : lerp(a[4], c[4], k), oy: portrait ? -.2 * (1 - homeIn) : lerp(a[5], c[5], k) };
  const labelView = sm(b, 5.25, 5.5) * (1 - sm(b, 6.15, 6.3));
  if (labelView) {
    const bottom = H - 72;
    const fittedZoom = Math.min(camera.z, Math.max(1, bottom - navClearance) / (174 * BZ));
    const scale = fittedZoom * BZ;
    const beltY = H * (.5 + camera.oy) - camera.y * scale;
    const fittedBelt = clamp(beltY, navClearance + 170 * scale, bottom);
    camera.z = lerp(camera.z, fittedZoom, labelView);
    camera.oy += (fittedBelt - beltY) * labelView / H;
  }
  const overview = sm(b, .72, 1.05) * (1 - sm(b, 1.75, 2.15));
  if (!overview) return camera;

  // Fit the belt and dispatch dock above the overview caption, including the belt supports.
  const rightEdge = X.dock + 300;
  const scale = Math.min(W * .94 / (rightEdge - BELT0), H * .4 / 1300);
  return {
    x: lerp(camera.x, (BELT0 + rightEdge) / 2, overview),
    y: lerp(camera.y, 160, overview),
    z: lerp(camera.z, scale / BZ, overview),
    ox: lerp(camera.ox, 0, overview),
    oy: lerp(camera.oy, .1, overview)
  };
}

/* ---------------- drawing helpers ---------------- */
let Z = 1, T = 0;                         // current world zoom (screen px per world unit), idle time
const px = n => n / Z;                    // constant-on-screen line width
function layer(cam, f = 1, kz = 1) {      // parallax layer transform
  const z = Z * kz, ox = W / 2 + cam.ox * W, oy = H / 2 + cam.oy * H;
  g.setTransform(DPR * z, 0, 0, DPR * z, DPR * (ox - cam.x * f * z), DPR * (oy - cam.y * (f === 1 ? 1 : (.6 + f * .4)) * z));
}
const lg = (x0, y0, x1, y1, stops) => { const q = g.createLinearGradient(x0, y0, x1, y1); stops.forEach(([o, c]) => q.addColorStop(o, c)); return q; };
const rg = (x, y, r0, r1, stops) => { const q = g.createRadialGradient(x, y, r0, x, y, r1); stops.forEach(([o, c]) => q.addColorStop(o, c)); return q; };
function rr(x, y, w, h, r) { g.beginPath(); g.roundRect ? g.roundRect(x, y, w, h, r) : g.rect(x, y, w, h); }
function box(x, y, w, h, fill, r = 0, stroke = true) { rr(x, y, w, h, r); g.fillStyle = fill; g.fill(); if (stroke) { g.lineWidth = px(1.1); g.strokeStyle = 'rgba(10,31,38,.75)'; g.stroke(); } }
const steelV = (x, w) => lg(x, 0, x + w, 0, [[0, P.s3], [.35, P.s1], [.6, P.s2], [1, P.s4]]);
const steelH = (y, h) => lg(0, y, 0, y + h, [[0, P.s1], [.5, P.s2], [1, P.s4]]);
function line(x0, y0, x1, y1, c, w) { g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.strokeStyle = c; g.lineWidth = w; g.stroke(); }
function text(s, x, y, size, color, font = 'Geist', weight = 500, align = 'left') { g.font = `${weight} ${size}px ${font}`; g.fillStyle = color; g.textAlign = align; g.textBaseline = 'alphabetic'; g.fillText(s, x, y); }
function led(x, y, r, on, col) { g.beginPath(); g.arc(x, y, r, 0, 7); g.fillStyle = on ? col : '#2b3d42'; g.fill(); if (on) { g.beginPath(); g.arc(x, y, r * 2.6, 0, 7); g.fillStyle = rg(x, y, 0, r * 2.6, [[0, col + '66'], [1, col + '00']]); g.fill(); } }
function screw(x, y) { g.beginPath(); g.arc(x, y, 2.2, 0, 7); g.fillStyle = P.s2; g.fill(); g.lineWidth = px(.8); g.strokeStyle = P.s5; g.stroke(); line(x - 1.4, y, x + 1.4, y, P.s5, px(.8)); }

/* ---------------- the bottle (Boston round, amber solution, child-resistant cap) ---------------- */
const BW = 34, NW = 13;
function bottlePath(i = 0) {
  const bw = BW - i, nw = NW - i * .6, r = 9;
  g.beginPath();
  g.moveTo(-bw + r, -i); g.lineTo(bw - r, -i); g.quadraticCurveTo(bw, -i, bw, -r - i);
  g.lineTo(bw, -98); g.bezierCurveTo(bw, -117, nw + 7, -123, nw, -130); g.lineTo(nw, -150);
  g.lineTo(-nw, -150); g.lineTo(-nw, -130); g.bezierCurveTo(-nw - 7, -123, -bw, -117, -bw, -98);
  g.lineTo(-bw, -r - i); g.quadraticCurveTo(-bw, -i, -bw + r, -i); g.closePath();
}
const FILL_TOP = -92;  // fill line
function drawLabel(w, cover, detail) {
  // label occupies y -18..-82 of the body; cover 0..1 reveals from the right (it comes off the peel plate on the right)
  const x0 = -BW + .5, x1 = BW - .5, y0 = -82, y1 = -18, left = lerp(x1, x0, cover);
  g.save(); g.beginPath(); g.rect(left, y0, x1 - left, y1 - y0); g.clip();
  g.fillStyle = P.paper; g.fillRect(x0, y0, x1 - x0, y1 - y0);
  g.fillStyle = P.teal; g.fillRect(x0, y0, x1 - x0, 9);
  g.fillStyle = P.coral; g.fillRect(x0, y0 + 9, x1 - x0, 1.6);
  if (detail) {
    text('TIRYAQ PHARMA', -27, y0 + 6.6, 4.4, P.ivory, 'Geist', 600);
    text('Rx', 22, y0 + 6.6, 4.4, P.honey, 'Geist', 600);
    text('Velmira', -27, y0 + 25, 13, P.teal, 'Instrument Serif', 400);
    text('5 mg / 5 mL', -27, y0 + 33, 5, P.ink, 'Geist', 600);
    text('Oral solution · 100 mL', -27, y0 + 39.5, 4, '#3b5052', 'Geist', 500);
    g.fillStyle = P.coral; g.fillRect(-27, y0 + 42.5, 22, .9);
    text('LOT TQ-0417', -27, y0 + 50, 3.6, P.ink, 'JetBrains Mono', 500);
    text('EXP 11/2028', -27, y0 + 55, 3.6, P.ink, 'JetBrains Mono', 500);
    for (let i = 0; i < 26; i++) { g.fillStyle = P.ink; g.fillRect(8 + i * .85, y0 + 45, (i * 7 % 3) * .35 + .35, 11); }
    text('Illustrative product', 8, y0 + 60, 2.6, '#7a8a8a', 'Geist', 500);
  } else { g.fillStyle = P.teal; g.fillRect(-26, y0 + 18, 30, 4); g.fillRect(-26, y0 + 27, 18, 2); g.fillStyle = P.ink; g.fillRect(8, y0 + 44, 20, 10); }
  g.restore();
  // cylinder shading over the label
  g.fillStyle = lg(x0, 0, x1, 0, [[0, 'rgba(10,31,38,.22)'], [.18, 'rgba(10,31,38,0)'], [.75, 'rgba(255,255,255,.12)'], [1, 'rgba(10,31,38,.2)']]);
  g.fillRect(left, y0, x1 - left, y1 - y0);
  // leading edge still lifting off the peel plate
  if (cover > 0 && cover < 1) line(left, y0, left, y1, 'rgba(10,31,38,.35)', px(1));
}
function drawCap(base, rot, tamper = true) {
  // child-resistant cap, 34 wide, 28 tall; base = y of its lower edge; rot = spin in radians (knurl lines move)
  const top = base - 28;
  g.fillStyle = lg(-17, 0, 17, 0, [[0, '#123f47'], [.35, '#2f7a80'], [.65, '#1f5c66'], [1, '#0e333a']]);
  rr(-17, top, 34, 28, 3); g.fill(); g.lineWidth = px(1.1); g.strokeStyle = P.ink; g.stroke();
  g.save(); rr(-17, top, 34, 28, 3); g.clip();
  for (let i = -8; i <= 8; i++) {
    const a = i / 8 * 1.45 + (rot % (Math.PI / 8)) * 8 / 8;   // knurl ridges wrap around the cylinder
    if (Math.abs(a) > 1.5) continue;
    const x = Math.sin(a) * 17; line(x, top + 6, x, base - (tamper ? 6 : 1), `rgba(255,255,255,${.12 + .15 * Math.cos(a)})`, px(1));
  }
  g.restore();
  g.fillStyle = P.ivory; g.fillRect(-17, top, 34, 4.5);
  if (tamper) { g.fillStyle = '#163f46'; g.fillRect(-16, base - 5, 32, 5); for (let i = -14; i <= 14; i += 4) line(i, base - 5, i, base, 'rgba(255,255,255,.25)', px(.7)); }
}
function drawBottle(x, y, st, s = 1) {
  g.save(); g.translate(x, y); g.scale(s, s);
  // contact shadow on the belt
  g.fillStyle = 'rgba(5,15,18,.35)'; g.beginPath(); g.ellipse(2, 1, BW + 4, 3.2, 0, 0, 7); g.fill();
  // glass body (back wall)
  bottlePath(); g.fillStyle = st.dim ? 'rgba(150,170,170,.16)' : 'rgba(214,236,230,.16)'; g.fill();
  // liquid
  if (st.fill > 0.001) {
    g.save(); bottlePath(3); g.clip();
    const lvl = lerp(-3, FILL_TOP, st.fill) + (st.wobble || 0);
    g.fillStyle = lg(0, lvl, 0, 0, [[0, '#f3b55a'], [.5, '#d98a2b'], [1, '#a95f17']]); g.fillRect(-BW, lvl, BW * 2, -lvl + 2);
    g.fillStyle = lg(-BW, 0, BW, 0, [[0, 'rgba(90,40,5,.35)'], [.3, 'rgba(255,220,160,0)'], [.7, 'rgba(255,230,180,.18)'], [1, 'rgba(90,40,5,.35)']]); g.fillRect(-BW, lvl, BW * 2, -lvl);
    g.beginPath(); g.ellipse(0, lvl, BW - 3, 2.2, 0, 0, 7); g.fillStyle = '#f7cd85'; g.fill();
    if (st.stream) { g.fillStyle = lg(0, -140, 0, lvl, [[0, '#f3b55a'], [1, '#d98a2b']]); g.fillRect(-1.6, -138, 3.2, lvl + 138); for (let i = 0; i < 9; i++) { const by = lvl + ((T * 40 + i * 13) % Math.max(4, -lvl)); g.beginPath(); g.arc(Math.sin(i * 2.3 + T) * 14, -by + lvl * 2 - lvl, 1 + (i % 3) * .5, 0, 7); g.fillStyle = 'rgba(255,240,210,.55)'; g.fill(); } }
    if (st.particles) { for (let i = 0; i < 6; i++) { g.beginPath(); g.arc(-18 + i * 7, -20 - (i * 11 % 40), .9, 0, 7); g.fillStyle = 'rgba(60,30,10,.6)'; g.fill(); } }
    g.restore();
  }
  if (st.label > 0) drawLabel(BW, st.label, st.detail);
  // etched lot mark near the base (only our bottle)
  if (st.lot && Z > 1.3) text('TQ·0417', -12, -4, 3.4, 'rgba(244,238,226,.55)', 'JetBrains Mono', 500);
  // glass: outline, wall thickness, highlights
  bottlePath(); g.lineWidth = px(1.3); g.strokeStyle = st.dim ? 'rgba(10,31,38,.6)' : 'rgba(10,31,38,.9)'; g.stroke();
  bottlePath(2.6); g.lineWidth = px(.9); g.strokeStyle = 'rgba(255,255,255,.32)'; g.stroke();
  g.fillStyle = lg(-27, 0, -20, 0, [[0, 'rgba(255,255,255,0)'], [.5, 'rgba(255,255,255,.55)'], [1, 'rgba(255,255,255,0)']]); rr(-27, -96, 6, 84, 3); g.fill();
  g.fillStyle = 'rgba(255,255,255,.18)'; rr(24, -92, 3, 70, 2); g.fill();
  g.fillStyle = 'rgba(255,255,255,.35)'; rr(-9, -146, 3, 14, 1.5); g.fill();
  // neck finish (threads) and base heel
  for (const ty of [-139, -144]) line(-NW + 1, ty + 2, NW - 1, ty, 'rgba(10,31,38,.35)', px(.9));
  line(-BW + 6, -3, BW - 6, -3, 'rgba(255,255,255,.22)', px(1));
  if (st.cap) drawCap(st.cap.base, st.cap.rot, st.cap.tamper !== false);
  g.restore();
}

/* ---------------- environment ---------------- */
function hall(cam) {
  layer(cam, .55, .82);
  // back wall
  g.fillStyle = lg(0, -1700, 0, 40, [[0, '#0a222a'], [.55, '#123842'], [1, '#174450']]); g.fillRect(-3000, -1800, 10000, 1840);
  // wall panels
  for (let x = -3000; x < 7000; x += 160) line(x, -620, x, 40, 'rgba(207,230,218,.045)', px(1));
  line(-3000, -620, 7000, -620, 'rgba(207,230,218,.08)', px(1.4));
  // tall windows with light shafts
  for (let x = -1600; x < 6400; x += 560) {
    g.fillStyle = lg(0, -1380, 0, -720, [[0, '#9fc9c2'], [.6, '#d8e7dc'], [1, '#f0dcc0']]); g.fillRect(x, -1380, 300, 660);
    g.strokeStyle = '#0c2a32'; g.lineWidth = 12; g.strokeRect(x, -1380, 300, 660);
    for (let i = 1; i < 4; i++) line(x + i * 75, -1380, x + i * 75, -720, '#0c2a32', 5);
    for (let j = 1; j < 6; j++) line(x, -1380 + j * 110, x + 300, -1380 + j * 110, '#0c2a32', 5);
    g.fillStyle = lg(0, -720, 0, 40, [[0, 'rgba(240,220,190,.10)'], [1, 'rgba(240,220,190,0)']]);
    g.beginPath(); g.moveTo(x, -720); g.lineTo(x + 300, -720); g.lineTo(x + 520, 40); g.lineTo(x + 160, 40); g.closePath(); g.fill();
    // column
    g.fillStyle = lg(x - 130, 0, x - 70, 0, [[0, '#0b2128'], [.5, '#163a43'], [1, '#0b2128']]); g.fillRect(x - 130, -1500, 60, 1540);
  }
  // roof trusses
  g.strokeStyle = 'rgba(207,230,218,.12)'; g.lineWidth = 4;
  g.beginPath(); for (let x = -3000; x < 7000; x += 140) { g.moveTo(x, -1460); g.lineTo(x + 70, -1560); g.lineTo(x + 140, -1460); } g.stroke();
  line(-3000, -1460, 7000, -1460, 'rgba(207,230,218,.16)', 6); line(-3000, -1560, 7000, -1560, 'rgba(207,230,218,.16)', 6);
  // signage
  text('TIRYAQ', 1300, -650, 120, 'rgba(244,238,226,.05)', 'Instrument Serif', 400);
  text('LINE 04 · ORAL LIQUIDS · GRADE D', 1310, -600, 22, 'rgba(207,230,218,.22)', 'JetBrains Mono', 500);
}
function floor(cam) {
  layer(cam);
  g.fillStyle = lg(0, 40, 0, 900, [[0, '#18434c'], [1, '#0a2129']]); g.fillRect(-2500, 40, 9000, 1200);
  line(-2500, 40, 7000, 40, 'rgba(207,230,218,.18)', px(1.4));
  g.setLineDash([60, 40]); line(-2500, 190, 7000, 190, 'rgba(239,184,92,.22)', 6); g.setLineDash([]);
  for (let x = -2400; x < 7000; x += 240) line(x, 40, x - 120, 900, 'rgba(207,230,218,.035)', px(1));
}

/* ---------------- conveyor ---------------- */
const BELT0 = -800, BELT1 = 4120;
function conveyorBack(b) {
  // rear side guide (behind bottles)
  line(BELT0, -40, BELT1, -40, P.s3, 2.6);
  for (let x = BELT0 + 30; x < BELT1; x += 120) { g.fillStyle = P.s4; g.fillRect(x, -44, 4, 46); }
}
function conveyorFront(belt) {
  // frame, chain belt and rollers. Rollers turn with the distance the belt has travelled, so they reverse with scrolling.
  const top = 0;
  g.fillStyle = lg(0, top, 0, top + 9, [[0, '#a9b6b8'], [1, '#6c7d80']]); g.fillRect(BELT0, top, BELT1 - BELT0, 9);
  for (let x = BELT0 - (belt % 12); x < BELT1; x += 12) line(x, top + 1, x, top + 8, 'rgba(10,31,38,.35)', px(.9));
  g.fillStyle = steelH(top + 9, 34); g.fillRect(BELT0, top + 9, BELT1 - BELT0, 34);
  line(BELT0, top + 9, BELT1, top + 9, P.ink, px(1.2)); line(BELT0, top + 43, BELT1, top + 43, P.ink, px(1.2));
  const ang = belt / 9;
  for (let x = BELT0 + 20; x < BELT1; x += 40) {
    g.beginPath(); g.arc(x, top + 26, 9, 0, 7); g.fillStyle = '#2c3e43'; g.fill();
    g.beginPath(); g.arc(x, top + 26, 9, 0, 7); g.lineWidth = px(1); g.strokeStyle = P.s2; g.stroke();
    for (let k = 0; k < 3; k++) { const a = ang + k * 2.094; line(x, top + 26, x + Math.cos(a) * 8, top + 26 + Math.sin(a) * 8, P.s3, px(1.3)); }
    g.beginPath(); g.arc(x, top + 26, 2.4, 0, 7); g.fillStyle = P.s1; g.fill();
  }
  // legs, feet, motors
  for (let x = BELT0 + 60; x < BELT1; x += 300) { g.fillStyle = steelV(x, 10); g.fillRect(x, top + 43, 10, 0 + 97); g.fillStyle = P.s4; g.fillRect(x - 8, top + 136, 26, 6); }
  for (const mx of [-520, 1800, 3720]) {
    box(mx, top + 52, 70, 46, lg(mx, 0, mx + 70, 0, [[0, P.teal], [1, '#123f47']]), 6);
    for (let i = 0; i < 6; i++) line(mx + 10 + i * 9, top + 60, mx + 10 + i * 9, top + 90, 'rgba(255,255,255,.18)', px(1));
    led(mx + 62, top + 60, 2.4, true, P.signal);
  }
  // front side guide (in front of the bottles' feet)
  g.fillStyle = steelH(-30, 6); g.fillRect(BELT0, -30, BELT1 - BELT0, 5); line(BELT0, -30, BELT1, -30, 'rgba(10,31,38,.6)', px(1));
  for (let x = BELT0 + 90; x < BELT1; x += 120) { g.fillStyle = P.s4; g.fillRect(x, -26, 5, 28); screw(x + 2.5, -28); }
}

/* ---------------- formulation (mezzanine) ---------------- */
function formulation(b) {
  const x = X.form, mixLvl = lerp(.12, .72, sm(b, 2.0, 2.45)), ang = b * 34 + (RM ? 0 : T * .6);
  // mezzanine platform, columns, stairs, railing
  g.fillStyle = P.s5; g.fillRect(80, -540, 700, 18); line(80, -540, 780, -540, P.s2, px(1.4));
  for (const cx of [110, 740]) { g.fillStyle = steelV(cx, 16); g.fillRect(cx, -522, 16, 562); }
  for (let i = 0; i < 9; i++) { const sx = 80 - i * 22, sy = -540 + i * 62; g.fillStyle = P.s4; g.fillRect(sx - 30, sy, 34, 6); }
  line(80, -540, -120, 40, P.s4, 5);
  line(80, -580, 780, -580, P.s2, 3); for (let rx = 80; rx <= 780; rx += 50) line(rx, -580, rx, -540, P.s3, 2);
  // purified water supply with flow meter
  g.fillStyle = steelH(-1010, 10); g.fillRect(-300, -1010, 640, 10); g.fillRect(330, -1010, 10, 70);
  box(120, -1026, 46, 42, '#0f2d35', 6); text('PW', 132, -999, 12, P.mint, 'JetBrains Mono', 500);
  // ingredient hoppers with weigh scales
  [[300, 'INGR. A', 1.98], [530, 'INGR. B', 2.08]].forEach(([hx, name, t0]) => {
    g.fillStyle = steelV(hx - 40, 80); g.beginPath(); g.moveTo(hx - 40, -1160); g.lineTo(hx + 40, -1160); g.lineTo(hx + 40, -1100); g.lineTo(hx + 8, -1060); g.lineTo(hx - 8, -1060); g.lineTo(hx - 40, -1100); g.closePath(); g.fill(); g.lineWidth = px(1.1); g.strokeStyle = P.ink; g.stroke();
    text(name, hx - 22, -1124, 11, P.ink, 'JetBrains Mono', 500);
    const w = sm(b, t0, t0 + .22), open = w > 0 && w < 1;
    box(hx + 46, -1150, 58, 36, '#0b1f25', 5); g.fillStyle = 'rgba(111,224,168,.2)'; g.fillRect(hx + 52, -1128, 46, 6); g.fillStyle = P.signal; g.fillRect(hx + 52, -1128, 46 * w, 6);
    text(w >= 1 ? 'WEIGHED ✓' : 'WEIGH', hx + 52, -1134, 8, P.signal, 'JetBrains Mono', 500);
    g.fillStyle = steelV(hx - 6, 12); g.fillRect(hx - 6, -1060, 12, 100);   // drop chute to vessel
    if (open) for (let i = 0; i < 14; i++) { const fy = -1050 + ((w * 3 + i / 14) % 1) * 90; g.beginPath(); g.arc(hx + Math.sin(i * 7) * 3, fy, 2.2, 0, 7); g.fillStyle = i % 2 ? P.ivory : P.honey; g.fill(); }
  });
  // vessel legs + agitator drive
  for (const lx of [x - 100, x + 90]) { g.fillStyle = steelV(lx, 12); g.fillRect(lx, -640, 12, 100); }
  box(x - 34, -1030, 68, 46, lg(x - 34, 0, x + 34, 0, [[0, P.teal], [1, '#0f3a42']]), 6); box(x - 18, -984, 36, 24, steelV(x - 18, 36), 3);
  for (let i = 0; i < 6; i++) line(x - 26 + i * 10, -1022, x - 26 + i * 10, -992, 'rgba(255,255,255,.2)', px(1));
  // vessel: jacketed shell, dished head, conical bottom
  const vx0 = x - 120, vx1 = x + 120, vt = -930, vb = -660;
  g.beginPath(); g.moveTo(vx0, vt); g.quadraticCurveTo(x, vt - 70, vx1, vt); g.lineTo(vx1, vb); g.lineTo(x + 18, -610); g.lineTo(x - 18, -610); g.lineTo(vx0, vb); g.closePath();
  g.fillStyle = steelV(vx0, 240); g.fill(); g.lineWidth = px(1.4); g.strokeStyle = P.ink; g.stroke();
  for (const yy of [-880, -780, -700]) line(vx0, yy, vx1, yy, 'rgba(10,31,38,.25)', px(1));
  for (let yy = -900; yy < -670; yy += 26) { screw(vx0 + 7, yy); screw(vx1 - 7, yy); }
  // sight glass showing the solution, the impeller and the vortex
  const sx0 = x - 46, sx1 = x + 46, st0 = -890, st1 = -680;
  g.save(); rr(sx0, st0, sx1 - sx0, st1 - st0, 30); g.clip();
  g.fillStyle = '#0d2a31'; g.fillRect(sx0, st0, sx1 - sx0, st1 - st0);
  const ly = lerp(st1, st0 + 20, mixLvl);
  g.fillStyle = lg(0, ly, 0, st1, [[0, '#f0bd6c'], [1, '#b46a1f']]);
  g.beginPath(); g.moveTo(sx0, ly); g.quadraticCurveTo(x, ly + 16 * sm(b, 2.2, 2.3), sx1, ly); g.lineTo(sx1, st1); g.lineTo(sx0, st1); g.closePath(); g.fill();
  line(x, st0 - 10, x, st1 - 22, P.s2, 5);
  for (let k = 0; k < 2; k++) { const c = Math.cos(ang + k * Math.PI / 2) * 34; g.fillStyle = P.s1; g.fillRect(x - Math.abs(c), st1 - 32 + k * 0, Math.abs(c) * 2, 7); g.fillRect(x - Math.abs(c) * .8, st1 - 62, Math.abs(c) * 1.6, 6); }
  for (let i = 0; i < 6; i++) { const yy = ly + 10 + ((i * 37 + b * 160) % 140); if (yy < st1) { g.beginPath(); g.arc(x + Math.sin(i * 4 + ang) * 30, yy, 1.6, 0, 7); g.fillStyle = 'rgba(255,240,210,.5)'; g.fill(); } }
  g.restore();
  rr(sx0, st0, sx1 - sx0, st1 - st0, 30); g.lineWidth = 6; g.strokeStyle = P.s4; g.stroke();
  g.fillStyle = 'rgba(255,255,255,.14)'; rr(sx0 + 8, st0 + 14, 8, st1 - st0 - 28, 4); g.fill();
  // level gauge tube with graduations
  const gx = x + 132; box(gx, -900, 14, 230, 'rgba(214,236,230,.12)', 7);
  g.fillStyle = P.amber; g.fillRect(gx + 3, lerp(-672, -894, mixLvl), 8, lerp(-672, -894, mixLvl) * -1 - 672);
  for (let yy = -890; yy < -680; yy += 22) line(gx + 15, yy, gx + 24, yy, P.s2, px(1));
  // outlet valve (handle turns as the transfer opens)
  const vOpen = sm(b, 2.62, 2.8);
  g.fillStyle = steelV(x - 8, 16); g.fillRect(x - 8, -610, 16, 30);
  box(x - 16, -592, 32, 18, P.s3, 3);
  g.save(); g.translate(x, -592); g.rotate(-vOpen * Math.PI / 2); g.fillStyle = P.coral; g.fillRect(-2.5, -28, 5, 28); g.restore();
  // HMI
  box(x + 170, -870, 112, 84, '#0b1f25', 8); text('MIX · V-201', x + 180, -850, 9, P.mint, 'JetBrains Mono', 500);
  const mp = sm(b, 2.15, 2.5); g.beginPath(); g.arc(x + 206, -815, 17, -1.57, -1.57 + 6.283 * mp); g.lineWidth = 4; g.strokeStyle = P.signal; g.stroke();
  g.beginPath(); g.arc(x + 206, -815, 17, 0, 7); g.lineWidth = 1; g.strokeStyle = 'rgba(207,230,218,.25)'; g.stroke();
  text(mp >= 1 ? 'RELEASED' : 'MIXING', x + 230, -818, 8, mp >= 1 ? P.signal : P.honey, 'JetBrains Mono', 500);
  text('SAMPLE ' + (b > 2.5 ? 'OK' : '—'), x + 230, -804, 8, P.mint, 'JetBrains Mono', 400);
  // vessel tag
  text('V-201', x - 22, -948, 12, 'rgba(10,31,38,.7)', 'JetBrains Mono', 500);
}
/* transfer line: vessel -> filler header, with flowing solution after the valve opens */
const PIPE = [[X.form, -580], [X.form, -500], [640, -500], [640, -470], [X.fill - 90, -470]];
function transferPipe(b) {
  const flow = sm(b, 2.7, 2.85);
  g.lineJoin = 'round'; g.lineCap = 'round';
  g.beginPath(); PIPE.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.lineWidth = 16; g.strokeStyle = P.s4; g.stroke();
  g.lineWidth = 12; g.strokeStyle = P.s2; g.stroke();
  // transparent section with visible flow
  g.lineWidth = 7; g.strokeStyle = flow > 0 ? `rgba(217,138,43,${.25 + .6 * flow})` : 'rgba(10,31,38,.25)'; g.stroke();
  if (flow > 0) { g.setLineDash([10, 16]); g.lineDashOffset = -(b * 900 + (RM ? 0 : T * 40)); g.lineWidth = 3.4; g.strokeStyle = `rgba(255,220,160,${flow})`; g.stroke(); g.setLineDash([]); }
  for (const [x, y] of PIPE.slice(1, -1)) { g.beginPath(); g.arc(x, y, 10, 0, 7); g.fillStyle = P.s3; g.fill(); }
  // inline flow meter
  box(560, -515, 44, 30, '#0b1f25', 5); text(flow > .5 ? 'FLOW' : 'IDLE', 566, -496, 8, flow > .5 ? P.signal : P.s3, 'JetBrains Mono', 500);
  g.lineCap = 'butt';
}

/* ---------------- filling station ---------------- */
function fillerBack(b) {
  const x = X.fill;
  for (const cx of [x - 130, x + 118]) { g.fillStyle = steelV(cx, 14); g.fillRect(cx, -440, 14, 480); }
  box(x - 140, -455, 280, 26, steelH(-455, 26), 4);
  // header manifold
  box(x - 92, -490, 184, 30, lg(0, -490, 0, -460, [[0, P.s1], [1, P.s3]]), 15);
  text('FILL HEADER', x - 30, -471, 8, 'rgba(10,31,38,.6)', 'JetBrains Mono', 500);
  // dosing pump: piston strokes during the dose
  const dose = sm(b, 3.3, 3.8), stroke = Math.sin(dose * Math.PI * 3) * 12 * (dose > 0 && dose < 1 ? 1 : 0);
  box(x + 50, -428, 40, 70, steelV(x + 50, 40), 6); g.fillStyle = P.s4; g.fillRect(x + 66, -358 + stroke, 8, 26);
  // guide rods for the nozzle carriage
  for (const rx of [x - 46, x + 40]) { g.fillStyle = steelV(rx, 6); g.fillRect(rx, -428, 6, 250); }
  // photo-eye sensor on the belt
  box(x - 72, -66, 18, 20, '#1f2f33', 3); led(x - 63, -61, 2.2, b > 3.05 && b < 4.05, P.signal);
  if (b > 3.05 && b < 4.05) { g.setLineDash([3, 4]); line(x - 54, -56, x + 60, -56, 'rgba(255,90,70,.6)', px(1.2)); g.setLineDash([]); }
}
function fillerFront(b) {
  const x = X.fill, down = sm(b, 3.13, 3.3) - sm(b, 3.8, 3.96);
  const cy = lerp(-342, -262, down);              // carriage plate; nozzle tip = cy + 126
  box(x - 54, cy - 14, 108, 22, lg(0, cy - 14, 0, cy + 8, [[0, P.s1], [1, P.s3]]), 5);
  screw(x - 44, cy - 3); screw(x + 44, cy - 3);
  g.fillStyle = steelV(x - 5, 10); g.fillRect(x - 5, cy + 8, 10, 104);
  g.fillStyle = P.s4; g.fillRect(x - 3.5, cy + 112, 7, 14);
  g.beginPath(); g.moveTo(x + 70, -360); g.bezierCurveTo(x + 70, cy - 60, x + 10, cy - 70, x + 6, cy - 14); g.lineWidth = 6; g.strokeStyle = 'rgba(214,236,230,.55)'; g.stroke();
  // transparent guard
  g.fillStyle = 'rgba(207,230,218,.07)'; g.fillRect(x - 160, -420, 320, 390);
  g.strokeStyle = 'rgba(207,230,218,.35)'; g.lineWidth = px(1.2); g.strokeRect(x - 160, -420, 320, 390);
  g.save(); g.beginPath(); g.rect(x - 160, -420, 320, 390); g.clip();
  g.fillStyle = 'rgba(255,255,255,.06)'; g.beginPath(); g.moveTo(x - 120, -420); g.lineTo(x - 40, -420); g.lineTo(x - 200, -30); g.lineTo(x - 280, -30); g.fill();
  g.beginPath(); g.moveTo(x + 40, -420); g.lineTo(x + 60, -420); g.lineTo(x - 100, -30); g.lineTo(x - 120, -30); g.fill(); g.restore();
  // HMI on swing arm
  line(x + 140, -400, x + 200, -400, P.s3, 5);
  box(x + 196, -420, 92, 66, '#0b1f25', 8);
  const dose = sm(b, 3.3, 3.8);
  text('FILLER F-04', x + 204, -403, 8, P.mint, 'JetBrains Mono', 500);
  text(dose >= 1 ? 'DOSE OK' : dose > 0 ? 'DOSING' : 'READY', x + 204, -388, 10, dose >= 1 ? P.signal : P.honey, 'JetBrains Mono', 500);
  g.fillStyle = 'rgba(207,230,218,.18)'; g.fillRect(x + 204, -376, 76, 6); g.fillStyle = P.signal; g.fillRect(x + 204, -376, 76 * dose, 6);
  led(x + 278, -406, 2.5, true, dose > 0 && dose < 1 ? P.honey : P.signal);
  text('F-04', x - 150, -430, 12, P.mint, 'JetBrains Mono', 500);
}

/* ---------------- capping station ---------------- */
const capFeed = b => sm(b, 4.4, 4.56);
const chuckY = b => path(b, [[4.5, -300], [4.6, -213], [4.66, -213], [4.76, -156], [4.96, -156], [5.06, -300]]);
const capRot = b => sm(b, 4.76, 4.96) * Math.PI * 4;
function capperBack(b) {
  const x = X.cap;
  for (const cx of [x - 120, x + 104]) { g.fillStyle = steelV(cx, 14); g.fillRect(cx, -500, 14, 540); }
  box(x - 130, -515, 260, 24, steelH(-515, 24), 4);
  // vibratory bowl feeder
  const bx = x + 250, by = -560;
  g.fillStyle = steelV(bx - 70, 140); g.fillRect(bx - 70, by, 140, 70); g.beginPath(); g.ellipse(bx, by, 70, 16, 0, 0, 7); g.fillStyle = P.s1; g.fill(); g.lineWidth = px(1.1); g.strokeStyle = P.ink; g.stroke();
  g.strokeRect(bx - 70, by, 140, 70); for (let i = 0; i < 3; i++) { g.beginPath(); g.ellipse(bx, by + 18 + i * 16, 70, 14, 0, .1, 3.04); g.strokeStyle = 'rgba(10,31,38,.3)'; g.stroke(); }
  for (let i = 0; i < 7; i++) { const a = i * .9 + b * 3; g.save(); g.translate(bx + Math.cos(a) * 52, by + Math.sin(a) * 9); g.scale(.45, .45); drawCap(0, a, false); g.restore(); }
  box(bx - 40, by + 70, 80, 60, '#163f46', 6); text('CAP FEED', bx - 30, by + 102, 8, P.mint, 'JetBrains Mono', 500);
  // chute rails from bowl to escapement above the neck
  line(bx - 60, by + 4, x + 24, -200, P.s3, 3); line(bx - 60, by + 20, x + 24, -184, P.s3, 3);
  // escapement gate
  box(x + 22, -214, 18, 40, P.s4, 3);
  // capping head drive
  box(x - 36, -500, 72, 50, lg(x - 36, 0, x + 36, 0, [[0, P.teal], [1, '#0f3a42']]), 6);
  text('C-02', x - 14, -470, 10, P.mint, 'JetBrains Mono', 500);
}
function capperFront(b) {
  const x = X.cap, bx = x + 250, by = -560, f = capFeed(b), cyb = chuckY(b), rot = capRot(b);
  // caps waiting on the chute (they index forward as one leaves)
  for (let k = 1; k < 5; k++) {
    const u = 1 - .2 * (k + 1 - f); const cx = lerp(bx - 60, x + 24, u), cyy = lerp(by + 12, -192, u);
    g.save(); g.translate(cx, cyy + 14); g.rotate(-.38); drawCap(0, 0); g.restore();
  }
  // the cap for our bottle: chute -> escapement -> chuck -> neck
  let capBase = null;
  if (b < 4.6) { const u = lerp(.8, 1, f); capBase = [lerp(bx - 60, x + 24, u) - (f >= 1 ? 24 : 0), lerp(by + 12, -192, u) + 14 + (f >= 1 ? 7 : 0), f >= 1 ? 0 : -.38]; }
  // spindle and chuck
  g.fillStyle = steelV(x - 7, 14); g.fillRect(x - 7, -450, 14, cyb + 450 - 34);
  const jaw = b > 4.6 && b < 4.97 ? 0 : 3;
  box(x - 22, cyb - 40, 44, 14, steelH(cyb - 40, 14), 3);
  g.save(); g.translate(x, 0);
  for (const s of [-1, 1]) { g.fillStyle = P.s3; g.fillRect(s * (18 + jaw) - (s > 0 ? 5 : 0), cyb - 28, 5, 30); }
  g.restore();
  if (capBase) { g.save(); g.translate(capBase[0], capBase[1]); g.rotate(capBase[2]); drawCap(0, 0); g.restore(); }
  else if (b < 4.96) { g.save(); g.translate(x, 0); drawCap(cyb + 28, rot); g.restore(); }
  // torque indicator lamp
  led(x + 30, -470, 3, b > 4.76, b > 4.95 ? P.signal : P.honey);
  return null;
}
function heroCap(b) {
  if (b < 4.6) return null;
  if (b < 4.96) return null;                       // carried by the chuck (drawn by the capper)
  return { base: -128, rot: Math.PI * 4 };
}

/* ---------------- labelling station ---------------- */
const labelCover = b => sm(b, 5.5, 5.86);
function labellerBack(b) {
  const x = X.label;
  box(x + 70, -340, 200, 300, lg(x + 70, 0, x + 270, 0, [[0, '#1b4f58'], [1, '#123d45']]), 10);
  for (let yy = -320; yy < -60; yy += 40) line(x + 80, yy, x + 260, yy, 'rgba(255,255,255,.05)', px(1));
  box(x + 160, -320, 92, 56, '#0b1f25', 6); text('LABELLER L-01', x + 166, -304, 7.5, P.mint, 'JetBrains Mono', 500);
  const c = labelCover(b); text(c >= 1 ? 'APPLIED' : c > 0 ? 'WRAPPING' : 'READY', x + 166, -288, 9, c >= 1 ? P.signal : P.honey, 'JetBrains Mono', 500);
  g.fillStyle = 'rgba(207,230,218,.18)'; g.fillRect(x + 166, -280, 76, 5); g.fillStyle = P.signal; g.fillRect(x + 166, -280, 76 * c, 5);
  // unwind roll (spins with the label being fed)
  const ua = -(b * 2.2 + c * 9), rx = x + 210, ry = -420;
  g.beginPath(); g.arc(rx, ry, 74, 0, 7); g.fillStyle = P.paper; g.fill(); g.lineWidth = px(1.2); g.strokeStyle = P.ink; g.stroke();
  for (let r = 30; r < 74; r += 5) { g.beginPath(); g.arc(rx, ry, r, 0, 7); g.lineWidth = px(.6); g.strokeStyle = 'rgba(29,85,96,.22)'; g.stroke(); }
  g.beginPath(); g.arc(rx, ry, 26, 0, 7); g.fillStyle = P.s4; g.fill();
  for (let k = 0; k < 3; k++) { const a = ua + k * 2.094; line(rx, ry, rx + Math.cos(a) * 24, ry + Math.sin(a) * 24, P.s1, 4); }
  // rewind roll for the backing liner
  const wx = x + 245, wy = -225, wr = 30 + c * 6;
  g.beginPath(); g.arc(wx, wy, wr, 0, 7); g.fillStyle = '#c9b9a0'; g.fill(); g.lineWidth = px(1); g.strokeStyle = P.ink; g.stroke();
  for (let k = 0; k < 3; k++) { const a = -ua * 1.3 + k * 2.094; line(wx, wy, wx + Math.cos(a) * 12, wy + Math.sin(a) * 12, P.s4, 3); }
  // pressure belt on the far side
  const pp = c > 0 && c < 1 ? 3 : 0;
  box(x - 74 + pp, -110, 20, 96, '#2d3f44', 6);
  for (let i = 0; i < 4; i++) { g.beginPath(); g.arc(x - 64 + pp, -100 + i * 26, 6, 0, 7); g.fillStyle = P.s3; g.fill(); }
}
const WEB = [[2210 - 70, -400], [2100 + 150, -300], [2100 + 110, -200], [2100 + 46, -118]];   // roll -> guide rollers -> peel plate tip
function labellerFront(b) {
  const x = X.label, c = labelCover(b);
  // web (labels on liner) running to the peel plate
  g.beginPath(); WEB.forEach(([a, d], i) => i ? g.lineTo(a, d) : g.moveTo(a, d)); g.lineWidth = 5; g.strokeStyle = '#d8c9ae'; g.stroke();
  for (const [a, d] of WEB.slice(1, 3)) { g.beginPath(); g.arc(a, d, 9, 0, 7); g.fillStyle = P.s2; g.fill(); g.lineWidth = px(1); g.strokeStyle = P.ink; g.stroke(); }
  // label pieces travel along the web toward the bottle
  const segs = []; let L = 0; for (let i = 0; i < WEB.length - 1; i++) { const l = Math.hypot(WEB[i + 1][0] - WEB[i][0], WEB[i + 1][1] - WEB[i][1]); segs.push(l); L += l; }
  const feed = b * 60 + c * 120;
  for (let k = 0; k < 5; k++) {
    let d = ((k * 70 + feed) % (L + 20)); if (d > L - 8) continue;
    let i = 0; while (d > segs[i]) { d -= segs[i]; i++; }
    const [ax, ay] = WEB[i], [bx2, by2] = WEB[i + 1], t = d / segs[i];
    g.save(); g.translate(lerp(ax, bx2, t), lerp(ay, by2, t)); g.rotate(Math.atan2(by2 - ay, bx2 - ax)); g.fillStyle = P.paper; g.fillRect(-12, -5, 24, 5); g.fillStyle = P.teal; g.fillRect(-12, -5, 24, 1.4); g.restore();
  }
  // peel plate
  g.fillStyle = steelV(x + 46, 40); g.beginPath(); g.moveTo(x + 40, -120); g.lineTo(x + 92, -150); g.lineTo(x + 92, -108); g.closePath(); g.fill(); g.lineWidth = px(1); g.strokeStyle = P.ink; g.stroke();
  // backing liner returns to the rewind roll
  g.beginPath(); g.moveTo(x + 46, -116); g.lineTo(x + 130, -160); g.lineTo(x + 245, -225 + 30); g.lineWidth = 2; g.strokeStyle = '#c9b9a0'; g.stroke();
  text('L-01', x + 80, -350, 12, P.mint, 'JetBrains Mono', 500);
}

/* ---------------- inspection station ---------------- */
const QC = [6.6, 6.72, 6.84, 6.96, 7.08];
function qcBack(b) {
  const x = X.qc;
  box(x - 140, -280, 280, 270, lg(x - 140, 0, x + 140, 0, [[0, '#13363e'], [1, '#0d2a31']]), 22);
  // diffuse backlight panel behind the bottle
  const pass = sm(b, 7.1, 7.22), on = b > 6.5 && b < 7.3;
  g.fillStyle = rg(x, -110, 10, 120, [[0, on ? 'rgba(244,238,226,.55)' : 'rgba(244,238,226,.12)'], [1, 'rgba(244,238,226,0)']]); g.fillRect(x - 120, -260, 240, 240);
  // reject lane + pusher (behind the belt)
  g.fillStyle = '#0e2b32'; g.fillRect(x + 160, -60, 160, 22); line(x + 160, -60, x + 320, -60, P.s3, 2);
  text('REJECT', x + 250, -66, 9, P.coral, 'JetBrains Mono', 500);
}
function qcFront(b, rejectP) {
  const x = X.qc, pass = sm(b, 7.1, 7.22);
  // front panel with a viewing window (even-odd cut-out)
  g.beginPath(); rrPath(x - 140, -280, 280, 270, 22); rrPath(x - 96, -248, 192, 228, 14); g.fillStyle = lg(x - 140, 0, x + 140, 0, [[0, P.s2], [.5, P.s1], [1, P.s3]]); g.fill('evenodd');
  g.lineWidth = px(1.2); g.strokeStyle = P.ink; g.stroke();
  // camera heads and ring light
  for (const [cx, cyy, a] of [[x - 60, -296, .5], [x + 60, -296, -.5]]) {
    g.save(); g.translate(cx, cyy); g.rotate(a); box(-18, -26, 36, 40, P.s4, 5); g.beginPath(); g.arc(0, 18, 12, 0, 7); g.fillStyle = '#0a1a1f'; g.fill(); g.beginPath(); g.arc(0, 18, 6, 0, 7); g.fillStyle = '#1f5c66'; g.fill(); g.beginPath(); g.arc(-3, 15, 2, 0, 7); g.fillStyle = 'rgba(255,255,255,.6)'; g.fill(); g.restore();
  }
  const ringCol = b > 6.5 && b < 7.3 ? (pass > .5 ? P.signal : P.honey) : '#3a5157';
  g.beginPath(); g.ellipse(x, -252, 70, 6, 0, 0, 7); g.lineWidth = 4; g.strokeStyle = ringCol; g.stroke();
  // stack light
  box(x + 112, -360, 14, 70, P.s4, 3);
  led(x + 119, -350, 4.5, rejectP > 0 && rejectP < 1, P.coral); led(x + 119, -333, 4.5, b > 6.5 && b < 7.1, P.honey); led(x + 119, -316, 4.5, pass > .5 && b < 7.6, P.signal);
  // pusher
  const ext = Math.sin(rejectP * Math.PI) * 20;
  box(x + 200, -110, 60, 26, P.s3, 4); g.fillStyle = P.s1; g.fillRect(x + 186 - ext, -100, 14 + ext, 6);
  text('QC-1', x - 128, -292, 12, P.mint, 'JetBrains Mono', 500);
}
function rrPath(x, y, w, h, r) { g.moveTo(x + r, y); g.arcTo(x + w, y, x + w, y + h, r); g.arcTo(x + w, y + h, x, y + h, r); g.arcTo(x, y + h, x, y, r); g.arcTo(x, y, x + w, y, r); g.closePath(); }
function scanOverlay(b) {
  const v = sm(b, 6.5, 6.62) * (1 - sm(b, 7.2, 7.32)); if (v <= 0) return;
  g.save(); g.globalAlpha = v; g.translate(X.qc, -24);
  // sensor view: the camera's rendering of the bottle
  g.fillStyle = 'rgba(6,22,27,.92)'; rr(-88, -205, 176, 202, 10); g.fill();
  g.strokeStyle = 'rgba(111,224,168,.4)'; g.lineWidth = px(1); g.stroke();
  g.clip();
  for (let yy = -200; yy < -6; yy += 4) line(-78, yy, 78, yy, 'rgba(111,224,168,.04)', px(1));
  bottlePath(); g.strokeStyle = P.signal; g.lineWidth = px(1.4); g.stroke();
  g.save(); bottlePath(3); g.clip(); g.fillStyle = 'rgba(217,138,43,.35)'; g.fillRect(-40, FILL_TOP, 80, -FILL_TOP); g.restore();
  g.save(); g.translate(0, 0); g.globalAlpha = v * .9; drawCapOutline(); g.restore();
  // sweeping scan line
  const sy = lerp(-6, -186, seg(b, 6.55, 7.1)); line(-80, sy, 80, sy, 'rgba(111,224,168,.9)', px(1.6));
  g.fillStyle = 'rgba(111,224,168,.12)'; g.fillRect(-80, sy, 160, 14);
  const ann = (i, y0, y1, label) => {
    if (b < QC[i]) return; const a = sm(b, QC[i], QC[i] + .06), ok = b > QC[i] + .1;
    g.globalAlpha = v * a; const c = ok ? P.signal : P.honey;
    line(36, y0, 44, y0, c, px(1.2)); line(36, y1, 44, y1, c, px(1.2)); line(44, y0, 44, y1, c, px(1.2));
    line(44, (y0 + y1) / 2, 60, (y0 + y1) / 2, c, px(1));
    text(label, 49, (y0 + y1) / 2 - 2, 4.8, c, 'JetBrains Mono', 500);
    text(ok ? 'PASS' : '···', 49, (y0 + y1) / 2 + 8, 4.6, ok ? P.signal : P.honey, 'JetBrains Mono', 500);
  };
  // 1 fill level: measured line against the target band
  if (b > QC[0]) { g.globalAlpha = v * sm(b, QC[0], QC[0] + .06); g.fillStyle = 'rgba(111,224,168,.18)'; g.fillRect(-30, FILL_TOP - 2.5, 60, 5); line(-34, FILL_TOP, 34, FILL_TOP, P.signal, px(1.2)); }
  ann(0, FILL_TOP - 4, FILL_TOP + 4, 'FILL LEVEL');
  ann(1, -160, -128, 'CAP SEAL');
  if (b > QC[2]) { g.globalAlpha = v * sm(b, QC[2], QC[2] + .06); g.setLineDash([2, 2]); g.strokeStyle = P.signal; g.lineWidth = px(1); g.strokeRect(-34, -82, 68, 64); g.setLineDash([]); }
  ann(2, -82, -18, 'LABEL');
  if (b > QC[3]) { g.globalAlpha = v * sm(b, QC[3], QC[3] + .06); g.strokeStyle = P.honey; g.lineWidth = px(1); g.strokeRect(-29, -36, 32, 13); }
  ann(3, -36, -23, 'LOT CODE');
  if (b > QC[4]) { g.globalAlpha = v * sm(b, QC[4], QC[4] + .06); for (let i = 0; i < 5; i++) for (let j = 0; j < 4; j++) { g.beginPath(); g.arc(-22 + i * 11, -86 + j * 16 + 6, .9, 0, 7); g.fillStyle = P.signal; g.fill(); } }
  ann(4, -88, -40, 'CLARITY');
  g.globalAlpha = v; text('CAMERA 2 · SIDE VIEW', -76, -195, 5, 'rgba(111,224,168,.7)', 'JetBrains Mono', 500);
  text('ILLUSTRATIVE', -76, -9, 4, 'rgba(111,224,168,.45)', 'JetBrains Mono', 500);
  g.restore();
}
function drawCapOutline() { rr(-17, -156, 34, 28, 3); g.strokeStyle = P.signal; g.lineWidth = px(1.2); g.stroke(); line(-16, -133, 16, -133, P.signal, px(.8)); }

/* ---------------- packaging station ---------------- */
const erect = b => sm(b, 7.9, 8.04), gripY = b => path(b, [[7.62, -380], [7.72, -134], [7.76, -134], [7.88, -350], [8.02, -350], [8.16, -140], [8.19, -140], [8.3, -400]]);
const bottleLift = b => path(b, [[7.76, 0], [7.88, -216], [8.02, -216], [8.16, -6]]);
function packBack(b) {
  const x = X.pack;
  box(x - 170, -560, 400, 540, lg(x - 170, 0, x + 230, 0, [[0, '#164650'], [1, '#0f343c']]), 12);
  for (let yy = -540; yy < -40; yy += 60) line(x - 160, yy, x + 220, yy, 'rgba(255,255,255,.04)', px(1));
  // carton magazine with flat blanks
  for (let i = 0; i < 9; i++) { g.save(); g.translate(x + 150 + i * 5, -470 + i * 2); g.rotate(.2); box(0, 0, 70, 120, i === 8 ? P.teal2 : P.teal, 2); g.fillStyle = P.ivory; g.fillRect(4, 40, 62, 8); g.restore(); }
  text('CARTONER K-06', x - 150, -530, 10, P.mint, 'JetBrains Mono', 500);
  // gripper gantry rail
  box(x - 160, -500, 340, 16, steelH(-500, 16), 3);
}
function carton(cx, e, flaps, print) {
  // 2.5D carton: front face + right side face + top flaps; e = erect progress (0 flat .. 1 square)
  const w = 92 * (.16 + .84 * e), d = 30 * e, h = 172, x0 = cx - w / 2, top = -h;
  // inner back (visible through the open top)
  g.fillStyle = '#0d2c33'; g.beginPath(); g.moveTo(x0, top); g.lineTo(x0 + d, top - d * .55); g.lineTo(x0 + w + d, top - d * .55); g.lineTo(x0 + w, top); g.closePath(); g.fill();
  const f1 = sm(flaps, 0, .45), f2 = sm(flaps, .45, 1);
  // Draw the rear flap behind the bottle and gripper as they pass into the carton.
  if (f2 < 1 && e > .5) {
    const backTop = top - d * .55, height = 46 * Math.cos(f2 * Math.PI / 2);
    g.fillStyle = '#1e5a63'; g.strokeStyle = P.ink; g.lineWidth = px(1.1);
    g.fillRect(x0 + d, backTop - height, w, height);
    g.strokeRect(x0 + d, backTop - height, w, height);
  }
  return { w, d, h, x0, top, front() {
    // side face
    g.fillStyle = lg(x0 + w, 0, x0 + w + d, 0, [[0, '#123f47'], [1, '#0c2f36']]);
    g.beginPath(); g.moveTo(x0 + w, top); g.lineTo(x0 + w + d, top - d * .55); g.lineTo(x0 + w + d, -d * .55); g.lineTo(x0 + w, 0); g.closePath(); g.fill(); g.lineWidth = px(1.1); g.strokeStyle = P.ink; g.stroke();
    // front face with print
    g.fillStyle = lg(x0, 0, x0 + w, 0, [[0, '#1b555f'], [1, '#22656f']]); g.fillRect(x0, top, w, h); g.strokeRect(x0, top, w, h);
    if (print > 0 && e > .7) {
      g.save(); g.globalAlpha = print; g.beginPath(); g.rect(x0, top, w, h); g.clip();
      g.fillStyle = P.ivory; g.fillRect(x0, top + 70, w, 54);
      g.fillStyle = P.coral; g.fillRect(x0, top + 124, w, 3);
      text('Tiryaq', x0 + 8, top + 30, 20, P.ivory, 'Instrument Serif', 400); text('PHARMA', x0 + 9, top + 40, 5.5, 'rgba(244,238,226,.75)', 'Geist', 600);
      text('Velmira', x0 + 8, top + 92, 15, P.teal, 'Instrument Serif', 400);
      text('5 mg / 5 mL oral solution', x0 + 8, top + 103, 4.8, P.ink, 'Geist', 600); text('100 mL · illustrative product', x0 + 8, top + 111, 4.2, '#4a5e60', 'Geist', 500);
      text('LOT TQ-0417', x0 + 8, top + 140, 4.6, P.ivory, 'JetBrains Mono', 500); text('EXP 11/2028', x0 + 8, top + 148, 4.6, P.ivory, 'JetBrains Mono', 500);
      g.restore();
    }
    // top flaps: two dust flaps stand up and fold in, then the tuck flap closes over them
    const flap = (hgt, col) => { g.fillStyle = col; g.fillRect(x0, top - hgt, w, hgt); g.strokeRect(x0, top - hgt, w, hgt); };
    if (f1 < 1) flap(34 * Math.cos(f1 * Math.PI / 2), '#2a6b74');
    if (f2 > 0) { g.fillStyle = '#2a6b74'; g.beginPath(); g.moveTo(x0, top); g.lineTo(x0 + d * f2, top - d * .55 * f2); g.lineTo(x0 + w + d * f2, top - d * .55 * f2); g.lineTo(x0 + w, top); g.closePath(); g.fill(); g.stroke(); }
  } };
}
function gripper(x, y, closed) {
  g.fillStyle = steelV(x - 6, 12); g.fillRect(x - 6, -500, 12, y + 500 - 30);
  box(x - 26, y - 34, 52, 16, steelH(y - 34, 16), 3);
  const o = closed ? 0 : 6; g.fillStyle = P.s3; g.fillRect(x - 22 - o, y - 20, 6, 24); g.fillRect(x + 16 + o, y - 20, 6, 24);
}
function dock() {
  const x = X.dock;
  g.fillStyle = '#0d2a32'; g.fillRect(x - 110, -520, 360, 560);
  // doorway: daylight outside
  g.fillStyle = lg(0, -340, 0, 40, [[0, '#fbe6c6'], [.6, '#f7efe0'], [1, '#e8dcc6']]); g.fillRect(x - 60, -340, 230, 380);
  g.fillStyle = rg(x + 55, -150, 20, 260, [[0, 'rgba(255,240,215,.7)'], [1, 'rgba(255,240,215,0)']]); g.fillRect(x - 300, -500, 700, 700);
  // roller door slats, half open
  for (let i = 0; i < 6; i++) { g.fillStyle = i % 2 ? '#c4cfd0' : '#b2bfc1'; g.fillRect(x - 64, -380 + i * 8, 238, 8); }
  line(x - 64, -332, x + 174, -332, P.ink, px(1.2));
  text('DISPATCH', x - 50, -394, 12, P.mint, 'JetBrains Mono', 500);
  // light spill on the floor
  g.fillStyle = lg(0, 40, 0, 300, [[0, 'rgba(250,225,190,.35)'], [1, 'rgba(250,225,190,0)']]);
  g.beginPath(); g.moveTo(x - 60, 40); g.lineTo(x + 170, 40); g.lineTo(x + 320, 300); g.lineTo(x - 200, 300); g.closePath(); g.fill();
}

/* ---------------- home (final scene, screen space) ---------------- */
function home(b, a) {
  if (a <= 0) return;
  g.setTransform(DPR, 0, 0, DPR, 0, 0); g.globalAlpha = a;
  // warm wall
  g.fillStyle = lg(0, 0, 0, H, [[0, '#f3e6d0'], [1, '#ead6b8']]); g.fillRect(0, 0, W, H);
  const stacked = W <= 960;
  const s = stacked ? Math.min(W / 1020, H / 1420) : Math.min(W * .52 / 940, H / 860);
  const cx = stacked ? W / 2 : W * .70, sill = H * (stacked ? .88 : .8);
  // window
  const ww = 760 * s, wh = 470 * s, wx = cx - ww / 2, wy = sill - wh - 26 * s;
  g.fillStyle = lg(0, wy, 0, wy + wh, [[0, '#f8d9b5'], [.55, '#f7e7cf'], [1, '#e9eee6']]); g.fillRect(wx, wy, ww, wh);
  g.fillStyle = rg(wx + ww * .72, wy + wh * .55, 10 * s, 180 * s, [[0, 'rgba(255,246,225,1)'], [.3, 'rgba(255,226,180,.6)'], [1, 'rgba(255,226,180,0)']]); g.fillRect(wx, wy, ww, wh);
  const hill = (y, amp, col, ph) => { g.fillStyle = col; g.beginPath(); g.moveTo(wx, wy + wh); for (let i = 0; i <= 40; i++) { const xx = wx + ww * i / 40; g.lineTo(xx, wy + wh * y - Math.sin(i * .35 + ph) * amp * s - Math.sin(i * .9 + ph) * amp * .3 * s); } g.lineTo(wx + ww, wy + wh); g.fill(); };
  hill(.74, 16, 'rgba(141,184,168,.55)', 1); hill(.83, 12, 'rgba(29,85,96,.35)', 3);
  for (let i = 0; i < 9; i++) { const bx = wx + ww * (.06 + i * .1), bh = (24 + (i * 37 % 40)) * s; g.fillStyle = 'rgba(29,85,96,.28)'; g.fillRect(bx, wy + wh * .86 - bh, 34 * s, bh + wh * .14); }
  // frame and mullions (the factory rail line becomes the window's horizon)
  g.strokeStyle = '#7f9e93'; g.lineWidth = 16 * s; g.strokeRect(wx, wy, ww, wh);
  g.lineWidth = 8 * s; line(cx, wy, cx, wy + wh, '#7f9e93', 8 * s); line(wx, wy + wh * .42, wx + ww, wy + wh * .42, '#7f9e93', 8 * s);
  // sunlight on the wall
  g.fillStyle = 'rgba(255,240,215,.35)'; g.beginPath(); g.moveTo(wx + ww, wy + 40 * s); g.lineTo(W, wy + 140 * s); g.lineTo(W, sill + 60 * s); g.lineTo(wx + ww, sill); g.closePath(); g.fill();
  // sill
  g.fillStyle = lg(0, sill, 0, sill + 26 * s, [[0, '#d8c3a2'], [1, '#b89d78']]); g.fillRect(wx - 70 * s, sill, ww + 140 * s, 26 * s);
  g.fillStyle = 'rgba(80,50,30,.18)'; g.fillRect(wx - 70 * s, sill + 26 * s, ww + 140 * s, 12 * s);
  // objects: open carton, our bottle, a dosing cup, a glass of water. Long morning shadows.
  const shadow = (x0, w0) => { g.fillStyle = 'rgba(90,60,30,.18)'; g.beginPath(); g.moveTo(x0, sill); g.lineTo(x0 + w0, sill); g.lineTo(x0 + w0 - 140 * s, sill + 2 * s); g.lineTo(x0 - 140 * s, sill + 2 * s); g.fill(); g.beginPath(); g.ellipse(x0 + w0 / 2 - 60 * s, sill + 1, w0 * .9, 3 * s, 0, 0, 7); g.fill(); };
  const bs = 1.2 * s, bxx = cx - 22 * s;
  shadow(cx - 230 * s, 90 * s); shadow(bxx - 46 * bs, 92 * bs); shadow(cx + 170 * s, 70 * s);
  g.save(); g.translate(cx - 185 * s, sill); g.scale(s * .92, s * .92); g.lineWidth = 1; const ct = carton(0, 1, 0, 1); ct.front(); g.restore();
  g.save(); Z = bs; drawBottle(bxx, sill, { fill: 1, cap: { base: -128, rot: 0 }, label: 1, detail: true, lot: true }, bs); g.restore();
  // dosing cup with graduations
  g.save(); g.translate(cx + 115 * s, sill); g.scale(s, s);
  g.beginPath(); g.moveTo(-18, -44); g.lineTo(18, -44); g.lineTo(14, 0); g.lineTo(-14, 0); g.closePath(); g.fillStyle = 'rgba(255,255,255,.45)'; g.fill(); g.strokeStyle = 'rgba(10,31,38,.6)'; g.lineWidth = 1; g.stroke();
  g.fillStyle = 'rgba(217,138,43,.75)'; g.beginPath(); g.moveTo(-16.2, -22); g.lineTo(16.2, -22); g.lineTo(14, 0); g.lineTo(-14, 0); g.closePath(); g.fill();
  for (let i = 0; i < 4; i++) line(-15 + i * .5, -8 - i * 9, -8 + i * .5, -8 - i * 9, 'rgba(10,31,38,.5)', .8);
  text('5 mL', -6, -24, 5, 'rgba(10,31,38,.7)', 'JetBrains Mono', 500); g.restore();
  // glass of water
  g.save(); g.translate(cx + 205 * s, sill); g.scale(s, s);
  g.beginPath(); g.moveTo(-30, -120); g.lineTo(30, -120); g.lineTo(25, 0); g.lineTo(-25, 0); g.closePath(); g.fillStyle = 'rgba(214,236,230,.35)'; g.fill(); g.strokeStyle = 'rgba(10,31,38,.45)'; g.lineWidth = 1.2; g.stroke();
  g.fillStyle = 'rgba(180,215,210,.45)'; g.beginPath(); g.moveTo(-28.4, -84); g.lineTo(28.4, -84); g.lineTo(25, 0); g.lineTo(-25, 0); g.closePath(); g.fill();
  g.beginPath(); g.ellipse(0, -84, 28.4, 3, 0, 0, 7); g.fillStyle = 'rgba(255,255,255,.5)'; g.fill();
  g.fillStyle = 'rgba(255,255,255,.5)'; g.fillRect(-22, -112, 4, 100); g.restore();
  // plant leaves at the left edge of the window
  g.save(); g.translate(wx - 30 * s, sill); g.scale(s, s); g.fillStyle = '#6f8f78';
  for (let i = 0; i < 6; i++) { g.save(); g.rotate(-1.2 + i * .32 + (RM ? 0 : Math.sin(T * .6 + i) * .015)); g.beginPath(); g.ellipse(0, -70, 12, 46, 0, 0, 7); g.fill(); g.restore(); }
  box(-26, -44, 52, 44, '#c98b5f', 4, false); g.restore();
  g.globalAlpha = 1;
}

/* ---------------- frame ---------------- */
let B = 0, tgt = 0, last = performance.now();
const targetFromScroll = () => stage()?.lineBeat() ?? 0;
function render() {
  const cam = camAt(B); Z = cam.z * BZ;
  g.setTransform(DPR, 0, 0, DPR, 0, 0); g.fillStyle = P.night; g.fillRect(0, 0, W, H);
  hall(cam); floor(cam);
  layer(cam);
  // derived states
  const hx = path(B, heroPath), belt = hx;
  formulation(B); transferPipe(B);
  fillerBack(B); capperBack(B); labellerBack(B); qcBack(B); packBack(B); dock();
  conveyorBack(B);
  if (B < 1.3) { const a = 1 - sm(B, .7, 1.3); g.fillStyle = rg(hx, -80, 10, 240, [[0, `rgba(255,232,190,${.32 * a})`], [1, 'rgba(255,232,190,0)']]); g.fillRect(hx - 260, -330, 520, 340); }
  // the queue of bottles on the belt; each bottle's state follows from where it is on the line
  let rejectP = 0;
  for (let k = -12; k <= 14; k++) {
    if (k === 0) continue;
    let x = hx + k * 140, y = 0, dim = false;
    const under = k === 2;                                           // the one bottle that will fail inspection
    if (x < BELT0 + 30) continue;
    if (under) {
      rejectP = clamp((x - (X.qc + 150)) / 70);
      if (rejectP === 1) continue;
      if (rejectP > 0) { y = -rejectP * 34; dim = true; }
    }
    if (!under && x > X.pack - 70) continue;
    const st = { fill: x > X.fill + 30 ? (under ? .62 : 1) : 0, cap: x > X.cap + 30 ? { base: -128, rot: 0 } : null, label: x > X.label + 30 ? 1 : 0, detail: false, dim };
    g.save();
    if (under) g.globalAlpha = 1 - sm(rejectP, .35, 1);
    drawBottle(x, y, st, y ? .94 : 1);
    g.restore();
  }
  // our bottle
  const hb = { fill: sm(B, 3.3, 3.8), stream: B > 3.3 && B < 3.8, cap: heroCap(B), label: labelCover(B), detail: true, lot: true, wobble: B > 3.8 && B < 4.2 ? Math.sin((B - 3.8) * 60) * 1.2 * (1 - seg(B, 3.8, 4.2)) : 0 };
  const inPack = B > 7.62, lift = inPack ? bottleLift(B) : 0;
  let ct = null;
  if (B > 7.89) { const cxp = B > 8.45 ? path(B, cartonPath) : X.pack; ct = carton(cxp, erect(B), sm(B, 8.2, 8.42), sm(B, 8.0, 8.1)); }
  if (B < 8.36) {
    if (B > 4.6 && B < 4.96) hb.cap = null;   // the chuck holds it
    drawBottle(B > 7.62 ? X.pack : hx, lift, hb);
  }
  if (B > 7.55 && B < 8.35) gripper(X.pack, gripY(B), B > 7.73 && B < 8.17);
  if (ct) ct.front();
  // further cartons waiting at dispatch
  const cpx = B > 8.45 ? path(B, cartonPath) : X.pack;
  for (let j = 1; j < 6; j++) { const x = Math.max(cpx, X.pack + 140) + j * 128; if (x < X.dock - 60) carton(x, 1, 1, 1).front(); }
  fillerFront(B); capperFront(B); labellerFront(B); qcFront(B, rejectP);
  conveyorFront(belt);
  scanOverlay(B);
  // match cut: the doorway's daylight becomes the morning at home
  const homeA = sm(B, 9.58, 9.78);
  if (B > 9.4) { g.setTransform(DPR, 0, 0, DPR, 0, 0); g.fillStyle = `rgba(250,236,214,${sm(B, 9.45, 9.65) * (1 - homeA * .0)})`; g.fillRect(0, 0, W, H); }
  home(B, homeA);
  // vignette
  g.setTransform(DPR, 0, 0, DPR, 0, 0);
  g.fillStyle = rg(W / 2, H / 2, Math.min(W, H) * .35, Math.max(W, H) * .8, [[0, 'rgba(0,0,0,0)'], [1, `rgba(4,14,18,${.45 * (1 - homeA)})`]]); g.fillRect(0, 0, W, H);
}

/* ---------------- DOM sync: cards, readouts, rail ---------------- */
const cards = $$('.ln-card[data-in]'), formSteps = $$('#form-steps li'), packSteps = $$('#pack-steps li'), qcItems = $$('#ln-qc li');
const tube = $('#ln-tube'), arc = $('#ln-dial-arc'), needle = $('#ln-needle'), hint = $('#ln-hint');
let lastUi = -1;
function ui() {
  if (Math.abs(B - lastUi) < .002) return; lastUi = B;
  cards.forEach(c => c.classList.toggle('on', B >= +c.dataset.in && B < +c.dataset.out));
  formSteps.forEach(li => li.classList.toggle('on', B >= +li.dataset.at));
  packSteps.forEach(li => li.classList.toggle('on', B >= +li.dataset.at));
  qcItems.forEach(li => { const t = +li.dataset.at; li.classList.toggle('run', B >= t && B < t + .1); li.classList.toggle('ok', B >= t + .1); });
  const fill = sm(B, 3.3, 3.8), down = sm(B, 3.13, 3.3) - sm(B, 3.8, 3.96);
  tube.style.setProperty('--f', fill);
  $('#ln-noz').textContent = down > .95 ? 'in neck' : down > .02 ? 'moving' : 'raised';
  $('#ln-dose').textContent = Math.round(fill * 100) + '%';
  $('#ln-lvl').textContent = fill >= 1 ? 'at fill line' : fill > 0 ? 'rising' : '—';
  const tq = sm(B, 4.76, 4.96);
  arc.setAttribute('stroke-dasharray', `${tq * 82} 100`); needle.style.transform = `rotate(${-135 + tq * 230}deg)`;
  $('#ln-capst').textContent = B < 4.56 ? 'in chute' : B < 4.62 ? 'picked' : B < 4.76 ? 'placing' : B < 4.96 ? 'tightening' : 'seated';
  $('#ln-tq').textContent = tq >= 1 ? 'in window' : tq > 0 ? 'rising' : '—';
  if (hint) hint.style.opacity = B < .3 ? .6 : 0;
}

/* ---------------- loop ---------------- */
let raf = 0, failures = 0;
function frame(now) {
  raf = requestAnimationFrame(frame);
  // Allow larger steps so the line still catches up when the 3D scene makes frames slow.
  const dt = Math.min(.25, (now - last) / 1000); last = now;
  if (!RM) T += dt;
  tgt = targetFromScroll();
  // Big jumps (the cut from the cleanroom back to the windowsill, rail clicks) snap instead of replaying the line.
  B = RM || Math.abs(tgt - B) > .6 ? tgt : B + (tgt - B) * (1 - Math.exp(-dt * 7));
  if (Math.abs(tgt - B) < 1e-4) B = tgt;
  try { if (stage()?.lineVisible() !== false) render(); ui(); }
  catch (err) { if (failures++ < 3) console.error('Tiryaq scene error', err); if (failures > 20) { cancelAnimationFrame(raf); document.documentElement.classList.add('nocanvas'); } }
}
document.addEventListener('visibilitychange', () => { if (document.hidden) cancelAnimationFrame(raf); else { last = performance.now(); raf = requestAnimationFrame(frame); } });
B = tgt = targetFromScroll();
raf = requestAnimationFrame(frame);
if (document.fonts) document.fonts.ready.then(() => { lastUi = -1; });
})();
