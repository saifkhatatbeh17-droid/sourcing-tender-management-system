/* Inside Tiryaq — 2D interior scenes.
   Data-driven animated interiors for the six campus buildings.
   Loaded before inside-tiryaq.js; exposes window.TiryaqScenes for the campus to bind. */
(function () {
'use strict';
var page = document.querySelector('[data-campus]');
if (!page) return;
var overlay = page.querySelector('[data-interior]');

/* ---------- palette (matches campus) ---------- */
var C = { ink:'#17383c', teal:'#315c60', teal2:'#087f85', mint:'#a6cfbf', orange:'#ee8d54',
          paper:'#ffffff', wall:'#e3eee7', floor:'#cfdfd3', side:'#c3d6c9', glass:'#cfe8e4',
          soft:'#f7fbf8', line:'#b9cec2', gold:'#e9b44c', red:'#c96a54', skin:'#eab88f', hair:'#5a4632' };

var ITEMS = ['Paracetamol 500 mg tablets', 'Amoxicillin 500 mg capsules', 'Saline 0.9% 500 ml'];
var SUPPLIERS = ['Al Shifa Pharma', 'Gulf Med Supply', 'Nour Pharma', 'Al Amal Trading', 'CureLine Pharma', 'Falcon Medical'];
var PR = 'PR-2041';

var reduced = matchMedia('(prefers-reduced-motion: reduce)');
var small = matchMedia('(max-width:900px)');
var count = 3;

/* ---------- easing ---------- */
var E = {
  lin: function (t) { return t; },
  out: function (t) { return 1 - Math.pow(1 - t, 3); },
  in: function (t) { return t * t * t; },
  io: function (t) { return t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; },
  back: function (t) { var c = 1.70158 * .9; return 1 + (c + 1) * Math.pow(t - 1, 3) + c * Math.pow(t - 1, 2); }
};

/* ---------- timeline engine ----------
   Every animated element registers an initial state (mark). Seeking restores all
   initial states, replays scheduled sets/trail ticks with t<=seek, then evaluates
   every tween at the seek time. Only transform + opacity are animated. */
function Engine() {
  var tweens = [], sets = [], ticks = [], registry = [];
  var time = 0, duration = 0, playing = false, ended = false;
  var onUpdate=null, onTick = null, onEnd = null, raf = 0, last = 0, destroyed = false;

  function applyProps(el, p) {
    el.style.transform = 'translate(' + (p.x || 0) + 'px,' + (p.y || 0) + 'px) rotate(' + (p.r || 0) + 'deg) scale(' + (p.s == null ? 1 : p.s) + ')';
    el.style.opacity = p.o == null ? 1 : p.o;
  }
  function mark(el, init) {
    if (!el) return el;
    if(el.__init) return el;
    el.classList.add('anim');
    el.style.transformBox = 'fill-box';
    el.style.transformOrigin = 'center';
    var st = { x: 0, y: 0, s: 1, r: 0, o: Number(el.getAttribute('opacity') ?? 1) };
    if (init) for (var k in init) st[k] = init[k];
    el.__init = st; el.__cur = { x: st.x, y: st.y, s: st.s, r: st.r, o: st.o };
    registry.push(el);
    applyProps(el, st);
    return el;
  }
  function to(el, props, t0, dur, ease) {
    if (!el) return;
    if (!el.__init) mark(el);
    var from = { x: el.__cur.x, y: el.__cur.y, s: el.__cur.s, r: el.__cur.r, o: el.__cur.o };
    tweens.push({ el: el, from: from, to: props, t0: t0, dur: Math.max(.01, dur), ease: ease || E.out });
    for (var k in props) el.__cur[k] = props[k];
    duration = Math.max(duration, t0 + dur);
  }
  function at(t, fn) { sets.push({ t: t, fn: fn }); }
  function tick(t, i) { ticks.push({ t: t, i: i }); duration = Math.max(duration, t); }

  function apply(t) {
    var i, el, k;
    for (i = 0; i < registry.length; i++) { el = registry[i]; el.__cur = { x: el.__init.x, y: el.__init.y, s: el.__init.s, r: el.__init.r, o: el.__init.o }; }
    sets.sort(function (a, b) { return a.t - b.t; });
    for (i = 0; i < sets.length; i++) if (sets[i].t <= t) sets[i].fn();
    for (i = 0; i < tweens.length; i++) {
      var tw = tweens[i];
      if(t < tw.t0) continue;
      var p = Math.max(0, Math.min(1, (t-tw.t0)/tw.dur)), v=tw.ease(p);
      for(k in tw.to) tw.el.__cur[k]=tw.from[k]+(tw.to[k]-tw.from[k])*v;
    }
    for(i=0;i<registry.length;i++) applyProps(registry[i],registry[i].__cur);
    ticks.sort(function (a, b) { return a.t - b.t; });
    for (i = 0; i < ticks.length; i++) if (ticks[i].t <= t && onTick) onTick(ticks[i].i);
  }

  function loop(now) {
    if (destroyed) return;
    raf = requestAnimationFrame(loop);
    if (document.hidden) { last = now; return; }
    var dt = Math.min(.05, (now - (last || now)) / 1000); last = now;
    if (!playing) return;
    time = Math.min(duration, time + dt);
    apply(time); if(onUpdate)onUpdate();
    if (time >= duration && !ended) { ended = true; playing = false; if (onEnd) onEnd(); }
  }

  return {
    mark: mark, to: to, at: at, tick: tick, apply: apply,
    setDuration: function(v){duration=v; tweens.sort(function(a,b){return a.t0-b.t0;});},
    get time() { return time; }, get duration() { return duration; }, get playing() { return playing; },
    play: function () { if (time >= duration) { time = 0; ended = false; apply(0); } playing = true; last = 0; },
    pause: function () { playing = false; },
    seek: function (t) { time = Math.max(0, Math.min(duration, t)); ended = time >= duration; apply(time);  },
    resetSession: function () { time = 0; ended = false; playing = false; apply(0); },
    start: function () { cancelAnimationFrame(raf); last = 0; raf = requestAnimationFrame(loop); },
    stop: function () { destroyed = true; cancelAnimationFrame(raf); },
    set onUpdate(fn){onUpdate=fn;},
    set onTick(fn) { onTick = fn; }, set onEnd(fn) { onEnd = fn; }
  };
}

/* ---------- svg building helpers ---------- */
function svgWrap(inner) {
  return '<svg viewBox="0 0 1200 675" xmlns="http://www.w3.org/2000/svg" role="presentation" aria-hidden="true">' + inner + '</svg>';
}
function roomBase(title, windows) {
  var s = '<rect width="1200" height="675" fill="' + C.soft + '"/>' +
    '<rect width="1200" height="566" fill="' + C.wall + '"/>' +
    '<rect y="470" width="1200" height="96" fill="' + C.side + '" opacity=".45"/>' +
    '<rect y="560" width="1200" height="115" fill="' + C.floor + '"/>' +
    '<rect y="558" width="1200" height="4" fill="' + C.line + '"/>';
  var i, x = 150;
  for (i = 0; i < (windows || 0); i++) { s += window_(x + i * 260, 96); }
  if (title) s += '<text x="600" y="46" text-anchor="middle" font-size="22" font-weight="600" fill="' + C.teal + '" letter-spacing="4">' + title + '</text>';
  return s;
}
function window_(x, y) {
  return '<g><rect x="' + x + '" y="' + y + '" width="150" height="150" rx="8" fill="' + C.glass + '"/>' +
    '<rect x="' + (x + 8) + '" y="' + (y + 8) + '" width="134" height="134" rx="4" fill="#e9f4ef"/>' +
    '<rect x="' + (x + 8) + '" y="' + (y + 8) + '" width="134" height="58" rx="4" fill="' + C.glass + '"/>' +
    '<circle cx="' + (x + 40) + '" cy="' + (y + 38) + '" r="14" fill="' + C.gold + '"/>' +
    '<rect x="' + (x + 70) + '" y="' + (y + 18) + '" width="52" height="18" rx="9" fill="#ffffff" opacity=".85"/>' +
    '<rect x="' + (x + 66) + '" y="' + (x ? y + 76 : y + 76) + '" width="60" height="0" fill="none"/>' +
    '<line x1="' + (x + 75) + '" y1="' + (y + 90) + '" x2="' + (x + 75) + '" y2="' + (y + 142) + '" stroke="' + C.teal2 + '" stroke-width="8"/>' +
    '<line x1="' + (x + 60) + '" y1="' + (y + 108) + '" x2="' + (x + 60) + '" y2="' + (y + 142) + '" stroke="' + C.mint + '" stroke-width="14"/>' +
    '<line x1="' + (x + 75) + '" y1="' + (y + 8) + '" x2="' + (x + 75) + '" y2="' + (y + 142) + '" stroke="' + C.wall + '" stroke-width="6"/>' +
    '<line x1="' + (x + 8) + '" y1="' + (y + 75) + '" x2="' + (x + 142) + '" y2="' + (y + 75) + '" stroke="' + C.wall + '" stroke-width="6"/></g>';
}
function door(id, x, flip) {
  var w = 108, h = 196, px = flip ? x : x;
  return '<g id="' + id + '">' +
    '<rect x="' + (x - 8) + '" y="' + (566 - h - 10) + '" width="' + (w + 16) + '" height="' + (h + 10) + '" rx="6" fill="' + C.teal + '"/>' +
    '<g class="door-panel"><rect x="' + x + '" y="' + (566 - h) + '" width="' + w + '" height="' + h + '" rx="4" fill="' + C.teal + '"/>' +
    '<rect x="' + (x + 14) + '" y="' + (566 - h + 18) + '" width="' + (w - 28) + '" height="70" rx="4" fill="' + C.teal2 + '" opacity=".55"/>' +
    '<rect x="' + (x + 14) + '" y="' + (566 - h + 104) + '" width="' + (w - 28) + '" height="70" rx="4" fill="' + C.teal2 + '" opacity=".55"/>' +
    '<circle cx="' + (flip ? x + w - 16 : x + 16) + '" cy="' + (566 - h / 2) + '" r="6" fill="' + C.gold + '"/></g>' +
    '<rect x="' + (x - 14) + '" y="556" width="' + (w + 28) + '" height="12" rx="3" fill="' + C.side + '"/></g>';
}
function person(x, y, o) {
  o = o || {};
  var shirt = o.shirt || C.teal2, h = o.h || 112, s = '';
  var flipT = o.flip ? ' scale(-1,1)' : '';
  s += '<g class="ch" transform="translate(' + x + ',' + y + ')' + flipT + '">';
  s += '<rect x="-11" y="' + (-34) + '" width="9" height="34" rx="4" fill="' + C.ink + '"/>';
  s += '<rect x="2" y="' + (-34) + '" width="9" height="34" rx="4" fill="' + C.ink + '"/>';
  s += '<rect x="-14" y="' + (-h) + '" width="28" height="' + (h - 30) + '" rx="12" fill="' + shirt + '"/>';
  s += '<circle cx="0" cy="' + (-h - 14) + '" r="15" fill="' + (o.skin || C.skin) + '"/>';
  s += '<path d="M -15 ' + (-h - 15) + ' a 15 15 0 0 1 30 0 l 0 -5 a 15 17 0 0 0 -30 0 z" fill="' + (o.hair || C.hair) + '"/>';
  if (o.tie) s += '<rect x="-3" y="' + (-h + 12) + '" width="6" height="18" rx="3" fill="' + C.orange + '"/>';
  s += '</g>';
  if (o.label) s += '<text x="' + x + '" y="' + (y + 24) + '" text-anchor="middle" font-size="18" font-weight="600" fill="' + C.ink + '">' + o.label + '</text>';
  return s;
}
function desk(x, y, w) {
  return '<g><rect x="' + (x - w / 2) + '" y="' + (y - 92) + '" width="' + w + '" height="16" rx="6" fill="#fdfdf6" stroke="' + C.line + '" stroke-width="2"/>' +
    '<rect x="' + (x - w / 2 + 10) + '" y="' + (y - 78) + '" width="12" height="78" fill="' + C.side + '"/>' +
    '<rect x="' + (x + w / 2 - 22) + '" y="' + (y - 78) + '" width="12" height="78" fill="' + C.side + '"/></g>';
}
function monitor(x, y, w, h) {
  w = w || 118; h = h || 78;
  return '<g><rect x="' + (x - 6) + '" y="' + (y - 6) + '" width="' + (w + 12) + '" height="' + (h + 12) + '" rx="8" fill="' + C.ink + '"/>' +
    '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="4" fill="#e8f4f0"/>' +
    '<rect x="' + (x + w / 2 - 8) + '" y="' + (y + h + 6) + '" width="16" height="12" fill="' + C.ink + '"/>' +
    '<rect x="' + (x + w / 2 - 24) + '" y="' + (y + h + 18) + '" width="48" height="6" rx="3" fill="' + C.ink + '"/></g>';
}
function chair(x, y) {
  return '<g><rect x="' + (x - 22) + '" y="' + (y - 58) + '" width="44" height="10" rx="5" fill="' + C.teal + '"/>' +
    '<rect x="' + (x - 22) + '" y="' + (y - 108) + '" width="10" height="56" rx="5" fill="' + C.teal + '"/>' +
    '<rect x="' + (x - 26) + '" y="' + (y - 116) + '" width="12" height="48" rx="6" fill="' + C.teal + '"/>' +
    '<rect x="' + (x - 4) + '" y="' + (y - 48) + '" width="8" height="48" fill="' + C.side + '"/></g>';
}
function plant(x, y) {
  return '<g><path d="M ' + (x - 22) + ' ' + y + ' l 6 -44 h 32 l 6 44 z" fill="' + C.orange + '"/>' +
    '<ellipse cx="' + (x - 14) + '" cy="' + (y - 62) + '" rx="12" ry="26" fill="' + C.mint + '" transform="rotate(-24 ' + (x - 14) + ' ' + (y - 62) + ')"/>' +
    '<ellipse cx="' + (x + 14) + '" cy="' + (y - 62) + '" rx="12" ry="26" fill="' + C.mint + '" transform="rotate(24 ' + (x + 14) + ' ' + (y - 62) + ')"/>' +
    '<ellipse cx="' + x + '" cy="' + (y - 74) + '" rx="13" ry="30" fill="#8fbfa4"/></g>';
}
function clock(x, y) {
  return '<g><circle cx="' + x + '" cy="' + y + '" r="26" fill="#ffffff" stroke="' + C.line + '" stroke-width="4"/>' +
    '<line x1="' + x + '" y1="' + y + '" x2="' + x + '" y2="' + (y - 15) + '" stroke="' + C.ink + '" stroke-width="4" stroke-linecap="round"/>' +
    '<line x1="' + x + '" y1="' + y + '" x2="' + (x + 11) + '" y2="' + (y + 5) + '" stroke="' + C.teal2 + '" stroke-width="4" stroke-linecap="round"/></g>';
}
function board(x, y, w, h, title) {
  return '<g><rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="10" fill="#ffffff" stroke="' + C.line + '" stroke-width="3"/>' +
    (title ? '<text x="' + (x + w / 2) + '" y="' + (y + 32) + '" text-anchor="middle" font-size="19" font-weight="700" fill="' + C.teal + '">' + title + '</text>' : '') + '</g>';
}
function doc(x, y, w, h, accent) {
  return '<g class="hero-doc"><rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="6" fill="#ffffff" stroke="' + C.orange + '" stroke-width="4"/>' +
    '<path d="M ' + (x + w - 26) + ' ' + y + ' l 26 0 0 26 z" fill="' + C.orange + '" opacity=".85"/>' +
    '<rect x="' + (x + 14) + '" y="' + (y + 20) + '" width="' + (w - 52) + '" height="12" rx="4" fill="' + C.orange + '"/>' +
    '<rect x="' + (x + 14) + '" y="' + (y + 44) + '" width="' + (w - 40) + '" height="7" rx="3" fill="' + C.line + '"/>' +
    '<rect x="' + (x + 14) + '" y="' + (y + 58) + '" width="' + (w - 56) + '" height="7" rx="3" fill="' + C.line + '"/>' +
    '<rect x="' + (x + 14) + '" y="' + (y + 72) + '" width="' + (w - 48) + '" height="7" rx="3" fill="' + C.line + '"/></g>';
}
function envelope(x, y, s) {
  s = s || 1;
  return '<g class="env"><rect x="' + (x - 34 * s) + '" y="' + (y - 22 * s) + '" width="' + (68 * s) + '" height="' + (44 * s) + '" rx="5" fill="#ffffff" stroke="' + C.teal + '" stroke-width="3"/>' +
    '<path class="flap" d="M ' + (x - 34 * s) + ' ' + (y - 22 * s) + ' l ' + (34 * s) + ' ' + (24 * s) + ' l ' + (34 * s) + ' -' + (24 * s) + '" fill="none" stroke="' + C.teal + '" stroke-width="3"/>' +
    '<circle cx="' + x + '" cy="' + (y + 8 * s) + '" r="7" fill="' + C.orange + '"/></g>';
}
function stamp(text, x, y, w, color) {
  return '<g class="stamp"><rect x="' + (x - w / 2) + '" y="' + (y - 24) + '" width="' + w + '" height="48" rx="8" fill="none" stroke="' + (color || C.teal2) + '" stroke-width="5"/>' +
    '<text x="' + x + '" y="' + (y + 8) + '" text-anchor="middle" font-size="24" font-weight="800" fill="' + (color || C.teal2) + '" letter-spacing="2">' + text + '</text></g>';
}
function cal(x, y, top, bottom) {
  return '<g><rect x="' + x + '" y="' + y + '" width="120" height="112" rx="8" fill="#ffffff" stroke="' + C.line + '" stroke-width="3"/>' +
    '<rect x="' + x + '" y="' + y + '" width="120" height="30" rx="8" fill="' + C.teal2 + '"/>' +
    '<text x="' + (x + 60) + '" y="' + (y + 21) + '" text-anchor="middle" font-size="18" font-weight="700" fill="#fff">' + top + '</text>' +
    '<text x="' + (x + 60) + '" y="' + (y + 76) + '" text-anchor="middle" font-size="34" font-weight="800" fill="' + C.ink + '">' + bottom + '</text></g>';
}
function kiosk(x, name) {
  return '<g><rect x="' + (x - 92) + '" y="392" width="184" height="16" rx="6" fill="#fdfdf6" stroke="' + C.line + '" stroke-width="2"/>' +
    '<rect x="' + (x - 80) + '" y="408" width="14" height="152" fill="' + C.side + '"/>' +
    '<rect x="' + (x + 66) + '" y="408" width="14" height="152" fill="' + C.side + '"/>' +
    '<rect x="' + (x - 70) + '" y="300" width="140" height="92" rx="8" fill="' + C.teal + '"/>' +
    '<rect x="' + (x - 58) + '" y="316" width="116" height="52" rx="5" fill="' + C.glass + '"/>' +
    '<text x="' + x + '" y="454" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.ink + '">' + name + '</text></g>';
}

/* ---------- scene builders ----------
   Each returns { html, ref map, beats:[{dur, caption(ctx)|string, build(eng, t0, q)}] }
   q(sel) resolves elements inside the freshly inserted svg. */

/* ===== 01 Department building ===== */
function sceneDept() {
  var fields = [
    { y: 214, label: 'Items', w: 300 },
    { y: 268, label: 'Quantities', w: 220 },
    { y: 322, label: 'Specifications', w: 260 },
    { y: 376, label: 'Target date', w: 180 }
  ];
  var form='<g id="s1-form"><rect x="630" y="100" width="450" height="445" rx="12" fill="#fff" stroke="'+C.line+'" stroke-width="3"/><rect x="630" y="100" width="450" height="50" rx="12" fill="'+C.teal+'"/><text x="650" y="132" font-size="22" font-weight="700" fill="#fff">Purchase request · sample</text>';
  form+='<g class="fld"><text x="650" y="183" font-size="18" font-weight="700" fill="'+C.teal+'">Items</text>';
  ITEMS.forEach(function(it,k){form+='<text class="it'+k+'" x="650" y="'+(216+k*26)+'" font-size="18" opacity="0">'+it+'</text>';});form+='</g>';
  form+='<g class="fld"><text x="990" y="183" font-size="18" font-weight="700">Qty.</text>';
  ['4,000','2,000','1,000'].forEach(function(v,k){form+='<text class="it'+k+'" x="1048" y="'+(216+k*26)+'" text-anchor="end" font-size="18" opacity="0">'+v+'</text>';});form+='</g>';
  form+='<g class="fld"><text x="650" y="310" font-size="18" font-weight="700" fill="'+C.teal+'">Specifications</text>';
  ['Registered pharmaceutical brands','Standard storage conditions','Delivery to central pharmacy'].forEach(function(v,k){form+='<text class="it'+k+'" x="650" y="'+(339+k*24)+'" font-size="18" opacity="0">'+v+'</text>';});form+='</g>';
  form+='<g class="fld"><text x="650" y="428" font-size="18" font-weight="700">Target date</text><text class="it0" x="810" y="428" font-size="20" fill="'+C.orange+'" opacity="0">12 December</text></g><g class="submit"><rect x="650" y="467" width="230" height="48" rx="24" fill="'+C.orange+'"/><text x="765" y="498" text-anchor="middle" font-size="20" font-weight="700">Submit request</text></g></g>';

  var html = svgWrap(
    roomBase('DEPARTMENT BUILDING · PHARMACY', 2) +
    door('s1-exit', 1100, true) +
    door('s1-entry', 8, false) +
    board(150, 120, 200, 110, 'Pharmacy dept.') +
    '<text x="250" y="172" text-anchor="middle" font-size="18" fill="' + C.ink + '">Stock is running low.</text>' +
    '<text x="250" y="196" text-anchor="middle" font-size="18" fill="' + C.ink + '">Time to reorder.</text>' +
    clock(520, 130) + plant(120, 560) +
    desk(430, 560, 330) + chair(330, 560) +
    monitor(392, 446) +
    person(330, 560, { label: 'Rana · Requester', shirt: C.teal2 }) +
    '<g id="s1-doc">' + doc(26, 380, 96, 118) + '<text x="74" y="522" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.orange + '">' + PR + '</text></g>' +
    form +
    '<g id="s1-calA">' + cal(1070, 140, 'NOV', '28') + '</g>' +
    '<g id="s1-calB" opacity="0">' + cal(1070, 140, 'DEC', '12') + '<circle cx="1130" cy="216" r="24" fill="none" stroke="' + C.orange + '" stroke-width="4"/></g>' +
    '<g id="s1-stamp">' + stamp('PR-2041', 838, 470, 190) + '</g>'
  );

  function build(eng, t0, q) {
    var exitPanel = q('#s1-exit .door-panel'), entryPanel = q('#s1-entry .door-panel');
    var d = q('#s1-doc'), form_ = q('#s1-form'), calA = q('#s1-calA'), calB = q('#s1-calB'), st = q('#s1-stamp'), sub = q('.submit');
    eng.mark(exitPanel); eng.mark(entryPanel, { x: 0 });
    eng.mark(d, { x: 0, y: 0, o: 1 });
    eng.mark(form_, { x: 60, o: 0 });
    eng.mark(calA); eng.mark(calB, { o: 0, s: .6 }); eng.mark(st, { o: 0, s: 2.2, r: -14 }); eng.mark(sub, { s: 1 });


    /* beat 1 — the need */
    eng.to(d, { x: 250 }, t0, 1.1, E.out);
    eng.to(d, { y: -26 }, t0 + 1.1, .35, E.out); eng.to(d, { y: 0 }, t0 + 1.45, .4, E.io);
    eng.to(form_, { x: 0, o: 1 }, t0 + .5, .9, E.out);

    /* beat 2 — fields complete */
    fields.forEach(function (f, i) {
      var tt = t0 + 4 + i * 1.35, grp = q('#s1-form').querySelectorAll('.fld')[i];
      eng.mark(grp);
      eng.to(grp, { s: 1.04 }, tt, .3, E.out); eng.to(grp, { s: 1 }, tt + .3, .5, E.io);
      for (var k = 0; k < (i === 3 ? 1 : 3); k++) {
        var it = grp.querySelector('.it' + (i === 3 ? 0 : k));
        if (!it) break;
        eng.mark(it, { o: 0, x: -10 });
        eng.to(it, { o: 1, x: 0 }, tt + .15 + (i === 3 ? 0 : k * .22), .45, E.out);
      }
    });

    /* beat 3 — target date */
    eng.to(calA, { s: .7, o: 0 }, t0 + 10, .4, E.in);
    eng.to(calB, { o: 1, s: 1 }, t0 + 10.4, .55, E.back);

    /* beat 4 — submit + stamp */
    eng.to(sub, { s: .88 }, t0 + 14.2, .18, E.in); eng.to(sub, { s: 1 }, t0 + 14.4, .5, E.back);
    eng.tick(t0 + 14.8, 0);
    eng.to(st, { o: 1, s: 1, r: -8 }, t0 + 15.1, .6, E.back);
    eng.to(d, { x: 250 + 130, y: -40 }, t0 + 15.7, .8, E.out); /* doc lifted off the form */

    /* beat 5 — leaves through the door */
    eng.to(entryPanel, { x: -120 }, t0 + 21.4, .4, E.out);


    eng.to(entryPanel, { x: 0 }, t0 + 23.4, .35, E.out);
  }

  return {
    html: html, refs: { exit: '#s1-exit', doc: '#s1-doc' },
    beats: [
      { dur: 4, caption: 'The pharmacy team spots a need: three medicines are running low.' },
      { dur: 6, caption: 'Rana fills in the purchase request — items, quantities, specifications.' },
      { dur: 4, caption: 'The target date is set: 12 December.' },
      { dur: 4, caption: 'Submitted. The request gets its reference: PR-2041.' },
      { dur: 4, caption: 'The request leaves the department for the tender office.' }
    ],
    build: build, exitSel: '#s1-exit', exitOpen: 21.4
  };
}

/* ===== 02 Tender office ===== */
function sceneTender() {
  var checks = ['Items & quantities', 'Specifications', 'Budget code', 'Target date', 'Attachments'];
  var list = '';
  checks.forEach(function (c, i) {
    list += '<g class="chk"><rect x="812" y="' + (188 + i * 52) + '" width="30" height="30" rx="6" fill="#eef4f1" stroke="' + C.line + '" stroke-width="2"/>' +
      '<path class="tick" d="M 818 ' + (204 + i * 52) + ' l 7 8 l 14 -16" fill="none" stroke="' + C.teal2 + '" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>' +
      '<text x="856" y="' + (210 + i * 52) + '" font-size="19" fill="' + C.ink + '" opacity=".45">' + c + '</text></g>';
  });
  var tender = '<g id="s2-tender"><rect x="790" y="470" width="330" height="80" rx="10" fill="#ffffff" stroke="' + C.teal + '" stroke-width="3"/>' +
    '<rect x="790" y="470" width="330" height="26" rx="9" fill="' + C.teal + '"/>' +
    '<text x="955" y="489" text-anchor="middle" font-size="18" font-weight="700" fill="#fff">TENDER · T-2041-A</text>' +
    '<g class="sec"><text x="806" y="527" font-size="18" font-weight="600" fill="' + C.teal2 + '">Requirements</text>' +
    '<text x="806" y="544" font-size="18" fill="' + C.ink + '">3 medicines</text></g>' +
    '<g class="sec"><text x="1080" y="527" text-anchor="end" font-size="18" font-weight="600" fill="' + C.teal2 + '">Terms & closing</text>' +
    '<text x="1080" y="544" text-anchor="end" font-size="18" fill="' + C.ink + '">Closes in 5 days</text></g></g>';
  var html = svgWrap(
    roomBase('TENDER OFFICE · SOURCING', 2) +
    door('s2-exit', 1100, true) + door('s2-entry', 8, false) +
    desk(360, 560, 360) + chair(280, 560) + monitor(322, 446) +
    person(280, 560, { label: 'Omar · Sourcing officer', shirt: C.teal, tie: true }) +
    '<g id="s2-inbox"><rect x="470" y="440" width="120" height="20" rx="6" fill="' + C.side + '"/>' +
    '<rect x="478" y="424" width="104" height="18" rx="5" fill="#ffffff" stroke="' + C.line + '" stroke-width="2"/>' +
    '<text x="530" y="489" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.teal + '">Inbox</text></g>' +
    board(780, 130, 360, 310, 'Request review') + list +
    clock(600, 120) + plant(150, 560) +
    tender +
    '<g id="s2-cal">' + cal(480, 130, 'DEC', '12') + '</g>' +
    '<g id="s2-count"><rect x="600" y="150" width="150" height="64" rx="12" fill="' + C.ink + '"/>' +
    '<text x="675" y="178" text-anchor="middle" font-size="18" fill="' + C.mint + '">CLOSING IN</text>' +
    '<text class="n" x="675" y="204" text-anchor="middle" font-size="26" font-weight="800" fill="#fff">5 days</text></g>' +
    '<g id="s2-stamp">' + stamp('PUBLISHED', 955, 555, 230) + '</g>' +
    '<g id="s2-envs">' + envelope(300, 610) + envelope(420, 630) + envelope(540, 610) + '</g>' +
    '<g id="s2-doc">' + doc(26, 380, 96, 118) + '<text x="74" y="522" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.orange + '">' + PR + '</text></g>'
  );

  function build(eng, t0, q) {
    var exitPanel = q('#s2-exit .door-panel'), entryPanel = q('#s2-entry .door-panel');
    var d = q('#s2-doc'), inbox = q('#s2-inbox'), td = q('#s2-tender'), cal = q('#s2-cal'), cnt = q('#s2-count'),
        st = q('#s2-stamp'), envs = q('#s2-envs');
    eng.mark(exitPanel); eng.mark(entryPanel);
    eng.mark(d, { x: 0, y: 0, o: 1 }); eng.mark(inbox); eng.mark(td, { y: 60, o: 0 }); eng.mark(cal, { y: -40, o: 0 });
    eng.mark(cnt, { o: 0, s: .5 }); eng.mark(st, { o: 0, s: 2.2, r: -12 }); eng.mark(envs, { o: 0 });


    /* beat 1 — lands in inbox */
    eng.to(d, { x: 404, y: 10 }, t0, 1.3, E.io);
    eng.to(d, { s: .8 }, t0 + 1.3, .3, E.in);
    eng.to(d, { y: 44, o: 0 }, t0 + 1.6, .4, E.in); /* drops into the tray */
    eng.to(inbox, { y: 4 }, t0 + 1.9, .2, E.in); eng.to(inbox, { y: 0 }, t0 + 2.1, .4, E.out);

    /* beat 2 — checklist */
    for (var i = 0; i < 5; i++) {
      var grp = q('svg').querySelectorAll('.chk')[i], tick = grp.querySelector('.tick'), txt = grp.querySelector('text');
      eng.mark(tick, { o: 0, s: .4 }); eng.mark(txt, { o: .45 });
      eng.to(tick, { o: 1, s: 1 }, t0 + 4 + i * .8, .4, E.back);
      eng.to(txt, { o: 1 }, t0 + 4 + i * .8, .4, E.out);
    }

    /* beat 3 — tender builds itself */
    eng.to(cal, { y: 0, o: 1 }, t0 + 9, .7, E.back);
    eng.to(td, { y: 0, o: 1 }, t0 + 9.6, .8, E.out);
    td.querySelectorAll('.sec').forEach(function (s, k) {
      eng.mark(s, { x: 30, o: 0 });
      eng.to(s, { x: 0, o: 1 }, t0 + 10.4 + k * .5, .5, E.out);
    });
    eng.to(cnt, { o: 1, s: 1 }, t0 + 11.4, .5, E.back);

    /* beat 4 — published */
    eng.tick(t0 + 14, 1);
    eng.to(st, { o: 1, s: 1, r: -6 }, t0 + 14.2, .6, E.back);

    /* beat 5 — invitations leave */
    eng.to(envs, { o: 1 }, t0 + 16, .3, E.out);
    for (var j = 0; j < 3; j++) {
      var e = envs.querySelectorAll('.env')[j];
      eng.mark(e, { x: 0, y: 0, r: 0, o: 1 });
      eng.to(e, { x: 620 + j * 40, y: -60 - j * 14, r: 8 }, t0 + 16.3 + j * .18, 1.2, E.io);
      eng.to(e, { o: 0 }, t0 + 17.3 + j * .18, .3, E.in);
    }
    eng.to(entryPanel, { x: -120 }, t0 + 16, .4, E.out);
    eng.to(entryPanel, { x: 0 }, t0 + 18, .35, E.out);
  }

  return {
    html: html, beats: [
      { dur: 4, caption: 'The request lands in Omar\u2019s inbox at the tender office.' },
      { dur: 5, caption: 'Every detail is checked against the request.' },
      { dur: 6, caption: 'The tender builds itself: requirements, terms, and a closing date.' },
      { dur: 4, caption: 'Published. The tender is open to qualified suppliers.' },
      { dur: 4, caption: 'Invitations fly out to the supplier courtyard.' }
    ],
    build: build, exitSel: '#s2-exit', exitOpen: 16
  };
}

/* ===== 03 Supplier courtyard ===== */
function sceneSuppliers() {
  var n = count, s = '';
  for (var i = 0; i < n; i++) s += kiosk(240 + i * 150, SUPPLIERS[i]);
  var offers = '', statuses = '';
  for (i = 0; i < n; i++) {
    var x = 240 + i * 150;
    offers += '<g class="offer" id="s3-offer' + i + '"><rect x="' + (x - 62) + '" y="236" width="124" height="86" rx="8" fill="#ffffff" stroke="' + C.orange + '" stroke-width="3"/>' +
      '<rect x="' + (x - 62) + '" y="236" width="124" height="20" rx="8" fill="' + C.orange + '"/>' +
      '<text x="' + x + '" y="251" text-anchor="middle" font-size="18" font-weight="700" fill="#fff">OFFER ' + String.fromCharCode(65 + i) + '</text>' +
      '<text class="p0" x="' + (x - 52) + '" y="278" font-size="18" fill="' + C.ink + '" opacity="0">Para · 1.20</text>' +
      '<text class="p1" x="' + (x - 52) + '" y="296" font-size="18" fill="' + C.ink + '" opacity="0">Amox · 2.85</text>' +
      '<text class="p2" x="' + (x - 52) + '" y="314" font-size="18" fill="' + C.ink + '" opacity="0">Saline · 0.95</text>' +
      '<text class="dl" x="' + x + '" y="268" text-anchor="middle" font-size="18" fill="' + C.teal2 + '" opacity="0">10-day delivery</text>' +
      '<text class="tx" x="' + x + '" y="286" text-anchor="middle" font-size="18" fill="' + C.teal2 + '" opacity="0">+ 5% tax</text>' +
      '<path class="clip" d="M ' + (x + 40) + ' 210 l 0 14 a 8 8 0 1 1 -16 0 l 0 -14" fill="none" stroke="' + C.teal2 + '" stroke-width="4" opacity="0"/>' +
      '<path class="seal" d="M ' + (x - 62) + ' 236 l 62 30 l 62 -30" fill="none" stroke="' + C.ink + '" stroke-width="3" opacity="0"/></g>';
    statuses += '<g class="stt" id="s3-stt' + i + '"><circle cx="' + x + '" cy="356" r="15" fill="#eef4f1" stroke="' + C.line + '" stroke-width="2"/>' +
      '<path class="ok" d="M ' + (x - 7) + ' 356 l 5 6 l 10 -12" fill="none" stroke="' + C.teal2 + '" stroke-width="4" stroke-linecap="round" opacity="0"/>' +
      '<path class="no" d="M ' + (x - 6) + ' 350 l 12 12 M ' + (x + 6) + ' 350 l -12 12" fill="none" stroke="' + C.red + '" stroke-width="4" stroke-linecap="round" opacity="0"/>' +
      '<text class="lb" x="' + x + '" y="392" text-anchor="middle" font-size="18" font-weight="600" fill="' + C.ink + '" opacity="0">Accepted</text>' +
      '<text class="rs" x="' + x + '" y="412" text-anchor="middle" font-size="18" fill="' + C.red + '" opacity="0">Date unavailable</text></g>';
  }
  var html = svgWrap(
    roomBase('SUPPLIER COURTYARD · ' + n + ' INVITED', 0) +
    door('s3-exit', 1100, true) + door('s3-entry', 8, false) +
    '<rect x="120" y="470" width="960" height="10" rx="5" fill="' + C.line + '"/>' +
    s + offers + statuses +
    '<g id="s3-envs">' + (function () { var r = ''; for (i = 0; i < n; i++) r += envelope(30, 330 + i * 8, .8); return r; })() + '</g>' +
    '<g id="s3-count"><rect x="980" y="120" width="150" height="64" rx="12" fill="' + C.ink + '"/>' +
    '<text x="1055" y="148" text-anchor="middle" font-size="18" fill="' + C.mint + '">DEADLINE</text>' +
    '<text class="n3" x="1055" y="174" text-anchor="middle" font-size="25" font-weight="800" fill="#fff">3 days</text>' +
    '<text class="n2" x="1055" y="174" text-anchor="middle" font-size="25" font-weight="800" fill="#fff" opacity="0">2 days</text>' +
    '<text class="n1" x="1055" y="174" text-anchor="middle" font-size="25" font-weight="800" fill="#fff" opacity="0">1 day</text>' +
    '<text class="n0" x="1055" y="174" text-anchor="middle" font-size="25" font-weight="800" fill="' + C.orange + '" opacity="0">Closed</text></g>' +
    '<g id="s3-gate"><rect x="1040" y="380" width="8" height="180" fill="' + C.teal + '"/>' +
    '<rect x="1140" y="380" width="8" height="180" fill="' + C.teal + '"/>' +
    '<rect x="1040" y="380" width="108" height="12" rx="4" fill="' + C.teal + '"/></g>'
  );

  function build(eng, t0, q) {
    var exitPanel = q('#s3-exit .door-panel'), entryPanel = q('#s3-entry .door-panel');
    var envs = q('#s3-envs'), cnt = q('#s3-count');
    eng.mark(exitPanel); eng.mark(entryPanel);
    eng.mark(cnt);


    /* beat 1 — invitations arrive */
    for (var i = 0; i < n; i++) {
      var e = envs.querySelectorAll('.env')[i], x = 240 + i * 150;
      eng.mark(e, { x: 0, y: 0, r: -8, o: 1 });
      eng.to(e, { x: x - 34, y: 250, r: 0 }, t0 + i * .3, 1.1, E.io);
    }
    /* beat 2 — accept / decline */
    var declineIdx = n >= 4 ? n - 1 : -1;
    for (i = 0; i < n; i++) {
      var g = q('#s3-stt' + i), ok = g.querySelector('.ok'), no = g.querySelector('.no'), lb = g.querySelector('.lb'), rs = g.querySelector('.rs');
      eng.mark(g, { s: 1 }); eng.mark(ok, { o: 0, s: .4 }); eng.mark(no, { o: 0, s: .4 }); eng.mark(lb, { o: 0, y: 6 }); eng.mark(rs, { o: 0 });
      var tt = t0 + 4 + i * .7;
      if (i === declineIdx) {
        eng.to(no, { o: 1, s: 1 }, tt, .5, E.back);
        eng.to(rs, { o: 1 }, tt + .3, .4, E.out);
        eng.to(g, { y: 10 }, tt + .6, .4, E.io); /* set aside */
      } else {
        eng.to(ok, { o: 1, s: 1 }, tt, .5, E.back);
        eng.to(lb, { o: 1, y: 0 }, tt + .2, .4, E.out);
      }
    }
    /* beat 3 — offers take shape */
    for (i = 0; i < n; i++) {
      if (i === declineIdx) { eng.mark(q('#s3-offer'+i),{o:0}); continue; }
      var o = q('#s3-offer' + i);
      eng.mark(o, { y: 60, o: 0 });
      eng.to(o, { y: 0, o: 1 }, t0 + 9 + i * .35, .6, E.out);
      for (var k = 0; k < 3; k++) {
        var p = o.querySelector('.p' + k);
        eng.mark(p, { o: 0, x: -8 });
        eng.to(p, { o: 1, x: 0 }, t0 + 9.6 + i * .35 + k * .3, .4, E.out);
      }
    }
    /* beat 4 — terms + documents attached */
    for (i = 0; i < n; i++) {
      if (i === declineIdx) continue;
      var o2 = q('#s3-offer' + i), dl = o2.querySelector('.dl'), tx = o2.querySelector('.tx'), clip = o2.querySelector('.clip');
      for(var price=0;price<3;price++) eng.to(o2.querySelector('.p'+price),{o:0},t0+15+i*.25,.2);
      eng.mark(dl, { o: 0 }); eng.mark(tx, { o: 0 }); eng.mark(clip, { o: 0, y: -14 });
      eng.to(dl, { o: 1 }, t0 + 15.1 + i * .25, .4, E.out);
      eng.to(tx, { o: 1 }, t0 + 15.4 + i * .25, .4, E.out);
      eng.to(clip, { o: 1, y: 0 }, t0 + 18.5 + i * .3, .5, E.back);
    }
    /* beat 5 — sealed & sent, trail tick */
    eng.tick(t0 + 18.5, 2);
    for (i = 0; i < n; i++) {
      if (i === declineIdx) continue;
      var o3 = q('#s3-offer' + i), seal = o3.querySelector('.seal');
      eng.mark(seal, { o: 0 });
      eng.to(seal, { o: 1 }, t0 + 18.5 + i * .2, .4, E.out);
      eng.to(o3, { x: 720 - i * 60, y: 60, r: 6, s: .7 }, t0 + 19.1 + i * .18, 1.2, E.io);
      eng.to(o3, { o: 0 }, t0 + 20.1 + i * .18, .3, E.in);
    }
    /* beat 6 — countdown to zero */
    var nums = ['n3', 'n2', 'n1', 'n0'];
    for (i = 0; i < 4; i++) {
      var el = cnt.querySelector('.' + nums[i]);
      eng.mark(el, { o: i === 0 ? 1 : 0, s: 1 });
      if (i > 0) eng.to(el, { o: 1, s: 1.15 }, t0 + 22 + i * 1.1, .3, E.back);
      if(i<3) eng.to(el,{o:0},t0+23+(i)*1.1,.15,E.out);
    }
    eng.to(entryPanel, { x: -120 }, t0 + 20, .4, E.out);
    eng.to(entryPanel, { x: 0 }, t0 + 24.4, .4, E.out);
  }

  var beats = [
    { dur: 4, caption: 'Invitations reach ' + n + ' qualified suppliers.' },
    { dur: 5, caption: n >= 4 ? 'Suppliers decide — one declines, the rest accept.' : 'Every supplier accepts the invitation.' },
    { dur: 6, caption: 'Accepted suppliers type in item prices for the three products.' },
    { dur: 3.5, caption: 'Delivery terms, tax, and supporting documents are attached.' },
    { dur: 4, caption: 'Offers are sealed and sent back before the deadline.' },
    { dur: 3, caption: 'The countdown hits zero — every offer is in.' }
  ];
  return { html: html, beats: beats, build: build, exitSel: '#s3-exit', exitOpen: 20, dynamic: true };
}

/* ===== 04 Evaluation lab ===== */
function sceneEval() {
  var n = count >= 4 ? count - 1 : count, declineIdx = count >= 4 ? n - 1 : -1;
  var eligible = [];
  for (var i = 0; i < n; i++) if (i !== declineIdx) eligible.push(i);
  var m = eligible.length;

  /* compliance desk (left) with offer cards */
  var offers = '';
  for (i = 0; i < n; i++) {
    offers += '<g class="coffer" id="s4-c' + i + '"><rect x="96" y="' + (300 + i * 66) + '" width="270" height="54" rx="8" fill="#ffffff" stroke="' + C.line + '" stroke-width="2"/>' +
      '<rect x="96" y="' + (300 + i * 66) + '" width="6" height="54" fill="' + C.orange + '"/>' +
      '<text x="120" y="' + (324 + i * 66) + '" font-size="18" font-weight="600" fill="' + C.ink + '">Offer ' + String.fromCharCode(65 + i) + '</text>' +
      '<text x="120" y="' + (344 + i * 66) + '" font-size="18" fill="' + C.teal + '">' + SUPPLIERS[i] + '</text>' +
      '<path class="ok" d="M 322 ' + (326 + i * 66) + ' l 8 9 l 16 -18" fill="none" stroke="' + C.teal2 + '" stroke-width="5" stroke-linecap="round" opacity="0"/>' +
      '<g class="flag" opacity="0"><rect x="228" y="' + (308 + i * 66) + '" width="144" height="26" rx="6" fill="' + C.red + '"/>' +
      '<text x="300" y="' + (326 + i * 66) + '" text-anchor="middle" font-size="18" font-weight="700" fill="#fff">Missing file</text></g></g>';
  }

  /* scoreboard */
  var rows = '';
  var score = { 0: [88, 85, 90, 87], 1: [82, 80, 84, 81], 2: [76, 78, 75, 77], 3: [70, 72, 71, 70], 4: [64, 66, 63, 65], 5: [58, 60, 59, 57] };
  for (var r = 0; r < m; r++) {
    var idx = eligible[r];
    rows += '<g class="srow" id="s4-r' + r + '" data-i="' + idx + '"><rect x="600" y="' + (218 + r * 62) + '" width="430" height="52" rx="8" fill="#ffffff" stroke="' + C.line + '" stroke-width="2"/>' +
      '<text class="nm" x="616" y="' + (237 + r * 62) + '" font-size="18" font-weight="600" fill="' + C.ink + '">Offer ' + String.fromCharCode(65 + idx) + ' · ' + SUPPLIERS[idx].split(' ')[0] + ' ' + SUPPLIERS[idx].split(' ')[1] + '</text>' +
      '<text class="nom" x="1010" y="' + (237 + r * 62) + '" text-anchor="end" font-size="18" font-weight="700" fill="' + C.teal2 + '" opacity="0">Nominated</text>' +
      (function () { var c = ''; for (var k = 0; k < 4; k++) c += '<text class="sc' + k + '" x="' + (776 + k * 44) + '" y="' + (260 + r * 62) + '" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.ink + '" opacity="0">' + score[idx][k] + '</text>'; return c; })() +
      '<rect class="totb" x="928" y="' + (222 + r * 62) + '" width="60" height="44" rx="6" fill="' + C.mint + '" opacity="0"/>' +
      '<text class="tot" x="958" y="' + (260 + r * 62) + '" text-anchor="middle" font-size="18" font-weight="800" fill="' + C.teal + '" opacity="0">' + (score[idx].reduce(function(a,b){return a+b;},0)/4).toFixed(1) + '</text></g>';
  }

  var members = '';
  for (i = 0; i < 4; i++) members += person(660 + i * 100, 560, { label: ['Sami', 'Huda', 'Faisal', 'Dina'][i] , shirt: i % 2 ? C.teal : C.teal2, h: 100 });

  var html = svgWrap(
    roomBase('EVALUATION LAB · COMMITTEE OF 4', 0) +
    door('s4-exit', 1100, true) + door('s4-entry', 8, false) +
    desk(240, 560, 320) + person(240, 560, { label: 'Omar · Compliance check', shirt: C.teal, tie: true, h: 108 }) +
    '<text x="231" y="256" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.teal + '">Compliance</text>' +
    offers +
    board(560, 130, 500, 70 + m * 62, 'Scoreboard · weights 50 / 50') +
    '<g id="s4-bal"><rect x="590" y="196" width="150" height="26" rx="13" fill="' + C.mint + '"/><text x="665" y="214" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.teal + '">Financial 50%</text>' +
    '<rect x="870" y="196" width="150" height="26" rx="13" fill="' + C.mint + '"/><text x="945" y="214" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.teal + '">Technical 50%</text>' +
    '<g class="beam"><rect x="640" y="188" width="330" height="8" rx="4" fill="' + C.teal + '"/><circle cx="805" cy="192" r="12" fill="' + C.ink + '"/></g></g>' +
    rows +
    '<g id="s4-report"><rect x="452" y="470" width="220" height="76" rx="10" fill="#ffffff" stroke="' + C.teal + '" stroke-width="3"/>' +
    '<text x="562" y="500" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.teal + '">Combined report</text>' +
    '<text x="562" y="524" text-anchor="middle" font-size="18" fill="' + C.ink + '">Scores · ranking · nomination</text></g>' +
    members + plant(90, 560)
  );

  function build(eng, t0, q) {
    var exitPanel = q('#s4-exit .door-panel'), entryPanel = q('#s4-entry .door-panel');
    eng.mark(exitPanel); eng.mark(entryPanel);


    /* beat 1 — compliance check */
    for (var i = 0; i < n; i++) {
      var c = q('#s4-c' + i), ok = c.querySelector('.ok');
      eng.mark(c, { x: -60, o: 0 }); eng.mark(ok, { o: 0, s: .4 });
      eng.to(c, { x: 0, o: 1 }, t0 + i * .5, .6, E.out);
      eng.to(ok, { o: 1, s: 1 }, t0 + .5 + i * .6, .45, E.back);
    }
    /* beat 2 — non-compliant set aside */
    if (declineIdx >= 0) {
      var cf = q('#s4-c' + declineIdx), flag = cf.querySelector('.flag'), ok2 = cf.querySelector('.ok');
      eng.mark(flag, { o: 0, s: .6 }); eng.mark(ok2, { o: 1 });
      eng.to(ok2, { o: 0 }, t0 + 4.4, .3, E.in);
      eng.to(flag, { o: 1, s: 1 }, t0 + 4.7, .5, E.back);
      eng.to(cf, { x: -30, y: 14, o: .45 }, t0 + 5.3, .7, E.io);
    }
    /* beat 3 — weights agreed */
    var bal = q('#s4-bal'), beam = bal.querySelector('.beam');
    eng.mark(bal, { o: 0, y: -16 }); eng.mark(beam, { r: -12 });
    eng.to(bal, { o: 1, y: 0 }, t0 + 8.5, .6, E.out);
    eng.to(beam, { r: 0 }, t0 + 9.1, .9, E.back);
    /* beat 4 — scoreboard fills */
    for (var r = 0; r < m; r++) {
      var row = q('#s4-r' + r), idx = eligible[r];
      eng.mark(row, { x: 40, o: 0 });
      eng.to(row, { x: 0, o: 1 }, t0 + 14 + r * .25, .5, E.out);
      for (var k = 0; k < 4; k++) {
        var sc = row.querySelector('.sc' + k);
        eng.mark(sc, { o: 0, s: .4 });
        eng.to(sc, { o: 1, s: 1 }, t0 + 14 + r * .3 + k * .18, .35, E.back);
      }
      var tot = row.querySelector('.tot'), totb = row.querySelector('.totb');
      eng.mark(tot, { o: 0, s: .4 }); eng.mark(totb, { o: 0 });
      eng.to(totb, { o: 1 }, t0 + 14.6 + r * .3, .3, E.out);
      eng.to(tot, { o: 1, s: 1 }, t0 + 14.8 + r * .3, .4, E.back);
    }
    /* beat 5 — ranked + nominated (top row = highest score = first eligible) */
    eng.tick(t0 + 19.5, 3);
    var top = q('#s4-r0'), nom = top.querySelector('.nom');
    eng.mark(nom, { o: 0, s: .5 });
    eng.to(top, { s: 1.05 }, t0 + 19.7, .4, E.back); eng.to(top, { s: 1.03 }, t0 + 20.2, .5, E.io);
    eng.to(top.querySelector('rect'), { o: 1 }, t0 + 19.7, .4, E.out);
    eng.to(nom, { o: 1, s: 1 }, t0 + 20.1, .5, E.back);
    /* beat 6 — combined report */
    var rep = q('#s4-report');
    eng.mark(rep, { y: 40, o: 0, s: .8 });
    eng.to(rep, { y: 0, o: 1, s: 1 }, t0 + 24.1, .7, E.back);
    eng.to(rep, { y: -6 }, t0 + 25.1, .4, E.out); eng.to(rep, { y: 0 }, t0 + 25.5, .5, E.io);
  }

  return {
    html: html, beats: [
      { dur: 4.5, caption: 'Offers line up for the compliance check, one by one.' },
      { dur: 4, caption: count >= 4 ? 'One offer is non-compliant — a missing GMP certificate sets it aside.' : 'All ' + n + ' offers are fully compliant.' },
      { dur: 5, caption: 'The committee agrees the criteria: 50% financial, 50% technical.' },
      { dur: 6, caption: 'Each member scores every eligible offer. The scoreboard fills live.' },
      { dur: 4.5, caption: 'Offers are ranked — Offer A is nominated.' },
      { dur: 3, caption: 'A combined evaluation report is produced.' }
    ],
    build: build, exitSel: '#s4-exit', exitOpen: 30.9, dynamic: true
  };
}

/* ===== 05 Decision boardroom ===== */
function sceneBoard() {
  var seats = '';
  for (var i = 0; i < 4; i++) seats += person(620 + i * 110, 560, { label: ['Sami', 'Huda', 'Faisal', 'Dina'][i], shirt: i % 2 ? C.teal2 : C.teal, h: 98 });
  var lamps = '';
  for (i = 0; i < 4; i++) lamps += '<g class="lamp" id="s5-lamp' + i + '"><line x1="' + (665 + i * 110) + '" y1="150" x2="' + (665 + i * 110) + '" y2="196" stroke="' + C.line + '" stroke-width="4"/>' +
    '<circle class="bulb" cx="' + (665 + i * 110) + '" cy="206" r="14" fill="' + C.mint + '" opacity=".35"/>' +
    '<circle class="glow" cx="' + (665 + i * 110) + '" cy="206" r="30" fill="' + C.gold + '" opacity="0"/></g>';
  var html = svgWrap(
    roomBase('DECISION BOARDROOM · FINAL COMMITTEE', 0) +
    door('s5-exit', 1100, true) + door('s5-entry', 8, false) +
    /* three review stations along the floor */
    '<path d="M 140 500 L 520 500 L 520 300 L 1000 300" fill="none" stroke="' + C.line + '" stroke-width="5" stroke-dasharray="2 14" stroke-linecap="round"/>' +
    desk(150, 380, 220) +
    person(150, 380, { label: 'Omar · Sourcing officer', shirt: C.teal, tie: true, h: 106 }) +
    desk(460, 380, 240) +
    person(460, 380, { label: 'Layla · Sourcing manager', shirt: C.teal2, h: 106 }) +
    desk(850, 380, 300) +
    person(810, 380, { label: 'CEO', shirt: C.ink, tie: true, h: 112 }) +
    person(930, 380, { label: 'Chiefs', shirt: C.teal, h: 106 }) +
    clock(330, 130) + plant(60, 560) +
    /* committee table */
    '<ellipse cx="785" cy="530" rx="230" ry="26" fill="' + C.teal + '"/>' +
    '<rect x="560" y="470" width="450" height="58" rx="18" fill="#fdfdf6" stroke="' + C.line + '" stroke-width="2"/>' +
    seats + lamps +
    '<g id="s5-doc">' + doc(30, 380, 96, 118) + '<text x="78" y="522" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.orange + '">Report</text></g>' +
    '<g id="s5-stamp">' + stamp('APPROVED', 880, 428, 220) + '</g>'
  );

  function build(eng, t0, q) {
    var exitPanel = q('#s5-exit .door-panel'), entryPanel = q('#s5-entry .door-panel');
    var d = q('#s5-doc'), st = q('#s5-stamp');
    eng.mark(exitPanel); eng.mark(entryPanel);
    eng.mark(d, { x: 0, y: 0, o: 1 }); eng.mark(st, { o: 0, s: 2.2, r: -10 });


    /* beat 1 — officer reviews, passes along */
    eng.to(d, { x: 150, y: 20 }, t0, 1.4, E.io);
    eng.to(d, { y: 26 }, t0 + 1.4, .3, E.in); /* set on the desk */
    /* beat 2 — manager reviews and refers */
    eng.to(d, { x: 520, y: -60 }, t0 + 4.5, 1.6, E.io);
    /* beat 3 — CEO & chiefs */
    eng.to(d, { x: 880, y: 20 }, t0 + 9, 1.6, E.io);
    /* beat 4 — lamps on one by one */
    for (var i = 0; i < 4; i++) {
      var lamp = q('#s5-lamp' + i), bulb = lamp.querySelector('.bulb'), glow = lamp.querySelector('.glow');
      eng.mark(bulb, { o: .35 }); eng.mark(glow, { o: 0, s: .5 });
      eng.to(bulb, { o: 1 }, t0 + 13 + i * .9, .3, E.out);
      eng.to(glow, { o: .5, s: 1 }, t0 + 13 + i * .9, .5, E.back);
    }
    /* beat 5 — approved */
    eng.tick(t0 + 17, 4);
    eng.to(st, { o: 1, s: 1, r: -6 }, t0 + 17.2, .6, E.back);
  }

  return {
    html: html, beats: [
      { dur: 4.5, caption: 'The officer reviews the committee report and passes it along.' },
      { dur: 4.5, caption: 'Layla, the sourcing manager, reviews the nominated offer and refers it upward.' },
      { dur: 4, caption: 'The CEO & Chiefs receive the proposal.' },
      { dur: 5, caption: 'The final committee approves — their lights switch on one by one.' },
      { dur: 3, caption: 'Approved. The decision is recorded.' }
    ],
    build: build, exitSel: '#s5-exit', exitOpen: 30.9
  };
}

/* ===== 06 Award archive ===== */
function sceneArchive() {
  var n = count, declineIdx = n >= 4 ? n - 1 : -1;
  var winner = 'Offer A · ' + SUPPLIERS[0];
  /* five folders */
  var folders = '', names = ['Request ' + PR, 'Offers (' + (count >= 4 ? count - 1 : count) + ')', 'Evaluation', 'Approvals', 'Decision'];
  for (var i = 0; i < 5; i++) {
    folders += '<g class="fold" id="s6-f' + i + '"><path d="M 0 0 h 60 l 10 12 h 110 v 58 h -180 z" transform="translate(' + (40 + i * 6) + ',' + (150 + i * 74) + ')" fill="' + (i === 1 ? C.orange : C.mint) + '" stroke="' + C.teal + '" stroke-width="2"/>' +
      '<text x="' + (62 + i * 6) + '" y="' + (188 + i * 74) + '" font-size="18" font-weight="600" fill="' + (i === 1 ? '#fff' : C.ink) + '">' + names[i] + '</text></g>';
  }
  var threads = '';
  for (i = 0; i < 5; i++) threads += '<line class="th" id="s6-t' + i + '" x1="250" y1="' + (186 + i * 74) + '" x2="600" y2="430" stroke="' + C.teal2 + '" stroke-width="3" stroke-dasharray="7 7"/>';
  var notices = '<g class="ntc" id="s6-n0">' + envelope(0, 0, .7) + '<text x="0" y="34" text-anchor="middle" font-size="18" font-weight="600" fill="' + C.ink + '">Winner: ' + SUPPLIERS[0] + '</text></g>' +
    '<g class="ntc" id="s6-n1">' + envelope(0, 0, .7) + '<text x="0" y="34" text-anchor="middle" font-size="18" font-weight="600" fill="' + C.ink + '">' + Math.max(n - 1 - (declineIdx >= 0 ? 1 : 0), 0) + ' other suppliers</text></g>' +
    '<g class="ntc" id="s6-n2">' + envelope(0, 0, .7) + '<text x="0" y="34" text-anchor="middle" font-size="18" font-weight="600" fill="' + C.ink + '">Pharmacy department</text></g>';
  var shelf = '';
  for (i = 0; i < 3; i++) shelf += '<rect x="880" y="' + (180 + i * 120) + '" width="280" height="14" rx="5" fill="' + C.side + '"/>';
  var boxes = '';
  for (i = 0; i < 6; i++) boxes += '<rect x="' + (895 + (i % 3) * 88) + '" y="' + (140 + Math.floor(i / 3) * 120) + '" width="66" height="' + (34 + (i % 3) * 8) + '" rx="4" fill="' + (i % 2 ? C.mint : '#fdfdf6') + '" stroke="' + C.line + '" stroke-width="2"/>';

  var html = svgWrap(
    roomBase('AWARD ARCHIVE · RECORDS', 0) +
    door('s6-entry', 8, false) +
    shelf + boxes +
    desk(680, 560, 280) + person(680, 560, { label: 'Nadia · Archive clerk', shirt: C.teal2, h: 104 }) +
    folders + threads +
    '<g id="s6-doc">' + doc(30, 380, 96, 118) + '<text x="78" y="522" text-anchor="middle" font-size="18" font-weight="700" fill="' + C.orange + '">Award</text></g>' +
    '<g id="s6-award"><rect x="540" y="380" width="180" height="96" rx="10" fill="#ffffff" stroke="' + C.orange + '" stroke-width="4"/>' +
    '<text x="630" y="416" text-anchor="middle" font-size="19" font-weight="800" fill="' + C.teal + '">AWARD</text>' +
    '<text x="630" y="442" text-anchor="middle" font-size="18" fill="' + C.ink + '">' + winner + '</text>' +
    '<text x="630" y="462" text-anchor="middle" font-size="18" fill="' + C.teal2 + '">T-2041-A · ' + PR + '</text></g>' +
    '<g id="s6-notices">' + notices + '</g>' +
    '<g id="s6-stamp">' + stamp('FULL TRAIL KEPT', 600, 540, 320) + '</g>' +
    '<g id="s6-final"><rect x="380" y="250" width="440" height="90" rx="14" fill="' + C.teal + '"/>' +
    '<text x="600" y="290" text-anchor="middle" font-size="26" font-weight="800" fill="#fff">Tender awarded</text>' +
    '<text x="600" y="320" text-anchor="middle" font-size="18" fill="' + C.mint + '">Full trail kept — every step on one record</text></g>'
  );

  function build(eng, t0, q) {
    var entryPanel = q('#s6-entry .door-panel');
    var d = q('#s6-doc'), award = q('#s6-award'), st = q('#s6-stamp'), fin = q('#s6-final');
    eng.mark(entryPanel);
    eng.mark(d, { x: 0, y: 0, o: 1 }); eng.mark(award, { o: 0, s: .7 }); eng.mark(st, { o: 0, s: 2.2, r: -10 }); eng.mark(fin, { o: 0, y: 30 });
    eng.to(entryPanel, { x: -120 }, t0 + 16.4, .4, E.out); eng.to(entryPanel, { x: 0 }, t0 + 17.6, .35, E.out);

    /* beat 1 — award recorded */
    eng.tick(t0 + .2, 5);
    eng.to(d, { x: 520, y: 0 }, t0, 1.5, E.io);
    eng.to(award, { o: 1, s: 1 }, t0 + 1.6, .6, E.back);

    /* beat 2 — notifications */
    var targets = [[160, 640], [600, 640], [1040, 640]];
    for (var i = 0; i < 3; i++) {
      var ntc = q('#s6-n' + i);
      eng.mark(ntc, { x: 600, y: 380, o: 0 });
      eng.to(ntc, { o: 1 }, t0 + 4.4 + i * .3, .3, E.out);
      eng.to(ntc, { x: targets[i][0], y: targets[i][1] - 35 }, t0 + 4.7 + i * .3, 1.2, E.io);
    }
    /* beat 3 — folders + threads */
    for (i = 0; i < 5; i++) {
      var f = q('#s6-f' + i), th = q('#s6-t' + i);
      eng.mark(f, { x: -320, o: 0 }); eng.mark(th, { o: 0, s: 0 });
      eng.to(f, { x: 0, o: 1 }, t0 + 8 + i * .45, .7, E.io);
      eng.to(th, { o: .8, s: 1 }, t0 + 8.6 + i * .45, .6, E.out);
    }
    /* beat 4 — final */
    eng.to(st, { o: 1, s: 1, r: -4 }, t0 + 13.2, .6, E.back);
    eng.to(fin, { o: 1, y: 0 }, t0 + 14, .7, E.out);
  }

  return {
    html: html, beats: [
      { dur: 4, caption: 'The award is recorded against tender T-2041-A.' },
      { dur: 4.5, caption: 'Notifications go out: the winner, the other suppliers, and the department.' },
      { dur: 6, caption: 'Every record — request, offers, evaluation, approvals, decision — connects to the award.' },
      { dur: 4, caption: 'Tender awarded · full trail kept.' }
    ],
    build: build, exitSel: null, exitOpen: 0, dynamic: true
  };
}

/* Scene artwork and timelines are embedded directly in the continuous city. */
var factories=[sceneDept,sceneTender,sceneSuppliers,sceneEval,sceneBoard,sceneArchive];
window.TiryaqCityScenes={
 create:function(index,suppliers){count=suppliers;return factories[index]();},
 engine:Engine
};
})();
