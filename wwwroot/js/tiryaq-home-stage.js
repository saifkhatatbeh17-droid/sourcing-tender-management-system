// Tiryaq homepage controller: one scroll position drives three acts on one sticky stage.
//   Act 1  illustrated production line, hero to packaging   (tiryaq-line.js, 2D canvas, beats 0..8.55)
//   Act 2  3D twelve-line cleanroom                          (tiryaq-journey.js, WebGL, beats 6.9..8.15)
//   Act 3  illustrated windowsill ending                     (tiryaq-line.js, beats 9.56..10)
// Distances are in viewport heights scrolled into #track ("d"). Both engines read their beat from window.TiryaqStage.
(() => {
  'use strict';
  const track = document.getElementById('track');
  if (!track) return;
  const lineCanvas = document.getElementById('line-canvas');
  const glCanvas = document.getElementById('gl-canvas');
  const glFallback = document.getElementById('gl-fallback');
  const rail = document.getElementById('home-rail');
  const RM = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const sm = (v, a, b) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };

  // Act boundaries in viewport heights. TOTAL + 1 viewport = #track height (see tiryaq-line.css).
  const A_END = 9.8;                 // line reaches the closed carton
  const X1 = [9.3, 10.0];            // line -> cleanroom crossfade
  const B = [9.5, 12.6];             // cleanroom beats GL_START -> GL_END
  const X2 = [12.3, 12.8];           // cleanroom -> windowsill crossfade
  const C = [12.45, 13.6];           // windowsill beats LINE_START_C -> 10, then hold
  const TOTAL = 14.6;

  // The cleanroom range starts after the 3D scene's own inspection station and ends before it heads to its own home
  // scene, so both crossfades stay inside the cleanroom view.
  const LINE_END_A = 8.55, GL_START = 6.9, GL_END = 8.15, LINE_START_C = 9.56;

  const depth = () => clamp((scrollY - track.offsetTop) / Math.max(1, innerHeight), 0, TOTAL);
  const lineBeat = d => d < C[0] ? lerp(0, LINE_END_A, clamp(d / A_END)) : lerp(LINE_START_C, 10, clamp((d - C[0]) / (C[1] - C[0])));
  const glBeat = d => lerp(GL_START, GL_END, clamp((d - B[0]) / (B[1] - B[0])));
  const glAlpha = d => sm(d, X1[0], X1[1]) * (1 - sm(d, X2[0], X2[1]));

  window.TiryaqStage = {
    lineBeat: () => lineBeat(depth()),
    glBeat: () => glBeat(depth()),
    lineVisible: () => glAlpha(depth()) < .999,
    glVisible: () => glAlpha(depth()) > .001
  };

  // One rail for the whole journey: [label, d].
  const STATIONS = [
    ['Start', 0], ['Formulation', 2.64], ['Filling', 4.07], ['Capping', 5.56], ['Labelling', 6.65],
    ['Inspection', 7.97], ['Packaging', 9.28], ['Cleanroom', 11.4], ['Home', 13.4]
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
  // Any element with data-go-d jumps to that depth (hero "Follow the bottle", "Watch it again", rail stops).
  document.addEventListener('click', e => {
    const bt = e.target.closest('[data-go-d]');
    if (bt) goTo(+bt.dataset.goD);
  });

  let last = -1;
  function frame() {
    requestAnimationFrame(frame);
    const d = depth();
    if (Math.abs(d - last) < .001) return;
    last = d;
    const a = glAlpha(d);
    if (lineCanvas) lineCanvas.style.opacity = (1 - a).toFixed(3);
    for (const el of [glCanvas, glFallback]) {
      if (!el) continue;
      el.style.opacity = a.toFixed(3);
      el.style.pointerEvents = a > .5 ? '' : 'none';
    }
    if (rail) {
      rail.style.setProperty('--p', (d / TOTAL).toFixed(4));
      rail.classList.toggle('dim', d > 13.3);
      let cur = 0;
      STATIONS.forEach(([, at], i) => { if (d >= at - .2) cur = i; });
      railButtons.forEach((bt, i) => {
        bt.classList.toggle('on', i <= cur);
        bt.classList.toggle('now', i === cur && d > .5);
      });
    }
  }
  requestAnimationFrame(frame);
})();
