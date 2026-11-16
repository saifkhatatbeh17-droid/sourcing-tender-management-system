/*
 * TIRYAQ — Meet the team
 * Renders the seats around the capsule table from js/team-data.js
 * and drives the profile card, browsing, progress and completion view.
 * No dependencies. Works from file:// (just open index.html).
 */
(() => {
  "use strict";

  const team = Array.isArray(window.TIRYAQ_TEAM) ? window.TIRYAQ_TEAM : [];
  const app = document.querySelector("[data-app]");
  const seatsHost = document.querySelector("[data-seats]");
  const sheet = document.querySelector("[data-sheet]");
  const card = document.querySelector("[data-card]");
  const body = document.querySelector("[data-card-body]");
  const closeBtn = document.querySelector("[data-close]");
  const batch = document.querySelector("[data-batch]");
  const batchFill = document.querySelector("[data-batch-fill]");
  const batchLabel = document.querySelector("[data-batch-label]");
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
  if (!app || !seatsHost || !team.length) return;

  /* ---------------------------------------------------------------
   * Seating plan (percent of the scene). Seven seats, clockwise,
   * starting at the head of the table.
   *   x,y   = person      px,py = name placard on the table
   *   rot   = chair rotation (backrest faces away from the table)
   *   m*    = same values for the vertical (phone) layout
   * ------------------------------------------------------------- */
  const SEATING = [
    { x: 5.5,  y: 50, px: 23, py: 50, rot: -90, mx: 50,   my: 6.5,  mpx: 50, mpy: 21,   mrot: 0 },
    { x: 32,   y: 13, px: 32, py: 34, rot: 0,   mx: 88.5, my: 30.7, mpx: 54, mpy: 30.7, mrot: 90 },
    { x: 50,   y: 13, px: 50, py: 34, rot: 0,   mx: 88.5, my: 50,   mpx: 54, mpy: 50,   mrot: 90 },
    { x: 68,   y: 13, px: 68, py: 34, rot: 0,   mx: 88.5, my: 69.3, mpx: 54, mpy: 69.3, mrot: 90 },
    { x: 94.5, y: 50, px: 77, py: 50, rot: 90,  mx: 50,   my: 93.5, mpx: 50, mpy: 79,   mrot: 180 },
    { x: 59,   y: 87, px: 59, py: 66, rot: 180, mx: 11.5, my: 59.7, mpx: 46, mpy: 59.7, mrot: -90 },
    { x: 41,   y: 87, px: 41, py: 66, rot: 180, mx: 11.5, my: 40.3, mpx: 46, mpy: 40.3, mrot: -90 }
  ];

  const ICONS = {
    linkedin: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M20.45 20.45h-3.56v-5.57c0-1.33-.02-3.04-1.85-3.04-1.85 0-2.14 1.45-2.14 2.94v5.67H9.35V9h3.41v1.56h.05c.48-.9 1.64-1.85 3.37-1.85 3.6 0 4.27 2.37 4.27 5.46v6.28ZM5.34 7.43a2.06 2.06 0 1 1 0-4.12 2.06 2.06 0 0 1 0 4.12ZM7.12 20.45H3.56V9h3.56v11.45ZM22.22 0H1.77C.79 0 0 .77 0 1.73v20.54C0 23.23.79 24 1.77 24h20.45c.98 0 1.78-.77 1.78-1.73V1.73C24 .77 23.2 0 22.22 0Z"/></svg>',
    github: '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M12 .5a11.5 11.5 0 0 0-3.64 22.41c.58.1.79-.25.79-.56v-2c-3.2.7-3.88-1.37-3.88-1.37-.52-1.33-1.28-1.69-1.28-1.69-1.05-.71.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.55-.29-5.24-1.28-5.24-5.69 0-1.26.45-2.29 1.19-3.09-.12-.29-.52-1.46.11-3.05 0 0 .97-.31 3.17 1.18a11 11 0 0 1 5.77 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.59.23 2.76.11 3.05.74.8 1.19 1.83 1.19 3.09 0 4.42-2.69 5.39-5.26 5.68.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56A11.5 11.5 0 0 0 12 .5Z"/></svg>',
    portfolio: '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9.5"/><path d="M2.5 12h19M12 2.5c2.6 2.7 3.9 5.9 3.9 9.5S14.6 18.8 12 21.5C9.4 18.8 8.1 15.6 8.1 12S9.4 5.2 12 2.5Z"/></svg>'
  };
  const LABELS = { linkedin: "LinkedIn", github: "GitHub", portfolio: "Portfolio" };
  const ARROW = '<svg class="arrow" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 17 17 7M9 7h8v8"/></svg>';
  const CHECK = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 12.5 4 4 8-9"/></svg>';
  const CHEVRON_L = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 6-6 6 6 6"/></svg>';
  const CHEVRON_R = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 6 6 6-6 6"/></svg>';

  const pad = n => String(n).padStart(2, "0");
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const total = team.length;
  const year = new Date().getFullYear();

  /* ---------- seats ---------- */
  const seats = team.map((m, i) => {
    const p = SEATING[i % SEATING.length];
    const li = document.createElement("li");
    li.innerHTML = `
      <button class="seat" type="button" data-index="${i}"
        aria-label="${esc(m.name)}, ${esc(m.role)}. Stage ${pad(i + 1)}: ${esc(m.stage)}. Open profile"
        aria-haspopup="dialog"
        style="--i:${i};--x:${p.x}%;--y:${p.y}%;--px:${p.px}%;--py:${p.py}%;--rot:${p.rot}deg;--mx:${p.mx}%;--my:${p.my}%;--mpx:${p.mpx}%;--my-p:${p.mpy}%;--mrot:${p.mrot}deg">
        <span class="person">
          <span class="chair" aria-hidden="true"></span>
          <span class="avatar"><img src="${esc(m.avatar)}" alt="" width="320" height="320" decoding="async"></span>
          <span class="check" aria-hidden="true">${CHECK}</span>
        </span>
        <span class="placard" aria-hidden="true">
          <span class="placard-no">${pad(i + 1)}</span>
          <span class="placard-name">${esc(m.name)}</span>
          <span class="placard-role">${esc(m.role)}</span>
        </span>
      </button>`;
    seatsHost.appendChild(li);
    return li.querySelector(".seat");
  });

  /* ---------- state ---------- */
  const visited = new Set();
  let current = -1;
  let mode = "member";          // "member" | "done"
  let completionShown = false;
  let closing = false;
  let returnFocus = null;

  function updateProgress() {
    const n = visited.size;
    const complete = n === total;
    batchFill.style.width = `${(n / total) * 100}%`;
    batchLabel.textContent = complete ? "Batch approved" : `${n} / ${total} met`;
    batch.disabled = !complete;
    batch.classList.toggle("is-complete", complete);
    batch.setAttribute("aria-label", complete ? "Batch approved. View the full team" : `${n} of ${total} teammates met`);
  }

  /* ---------- barcode (deterministic from a string) ---------- */
  function barcode(seed) {
    let h = 2166136261;
    for (const c of seed) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
    let x = 0, bars = "";
    for (let i = 0; i < 46; i++) {
      h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0;
      const w = 1 + (h % 3);
      if (i % 2 === 0) bars += `<rect x="${x}" y="0" width="${w}" height="30"/>`;
      x += w + (i % 2 ? 0 : 1);
    }
    return `<svg class="barcode" viewBox="0 0 ${x} 30" width="${x}" height="30" aria-hidden="true" fill="#06302c">${bars}</svg>`;
  }

  /* ---------- views ---------- */
  function profileView(i) {
    const m = team[i];
    const links = (m.links || []).map((l, k) => `
      <a class="link${k === 0 ? " primary" : ""}" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer">
        ${ICONS[l.type] || ICONS.portfolio}<span>${esc(l.label || LABELS[l.type] || "Link")}</span>${ARROW}
      </a>`).join("");
    const track = team.map((t, k) => `
      <li class="${k === i ? "is-current" : ""} ${visited.has(k) ? "is-visited" : ""}">
        <button type="button" data-go="${k}" aria-label="${esc(t.name)}, ${esc(t.stage)}"${k === i ? ' aria-current="step"' : ""}>
          <span class="dot"><img src="${esc(t.avatar)}" alt="" width="36" height="36"></span>
          <small>${pad(k + 1)}</small>
        </button>
      </li>`).join("");
    return `
      <article class="profile">
        <div class="photo">
          <img src="${esc(m.photo)}" alt="Portrait of ${esc(m.name)}" width="720" height="900">
          <span class="photo-chip"><b>${pad(i + 1)}</b>${esc(m.stage)}</span>
          <div class="photo-name" aria-hidden="true"><span>${esc(m.role)}</span><strong>${esc(m.name)}</strong></div>
        </div>
        <div class="info">
          <div class="label-top"><span class="rx" aria-hidden="true">R<i>x</i></span><span>Tiryaq · Team formulary</span><span class="no">${pad(i + 1)} / ${pad(total)}</span></div>
          <p class="role">${esc(m.role)}</p>
          <h2 id="card-name">${esc(m.name)}</h2>
          <div class="stage-block">
            <span class="stage-kicker">Production stage ${pad(i + 1)}</span>
            <h3>${esc(m.stage)}</h3>
            <p>${esc(m.stageText || "")}</p>
          </div>
          <ol class="track" aria-label="Production line — jump to a teammate">${track}</ol>
          ${links ? `<div class="links">${links}</div>` : ""}
          <div class="card-foot">
            ${barcode(m.id + m.name)}
            <span class="lot"><span>Lot</span><b>TQ-${year}-${pad(i + 1)}</b></span>
            <div class="nav">
              <button class="icon-btn" type="button" data-prev aria-label="Previous teammate">${CHEVRON_L}</button>
              <button class="icon-btn" type="button" data-next aria-label="Next teammate">${CHEVRON_R}</button>
            </div>
          </div>
        </div>
      </article>`;
  }

  function doneView() {
    const roster = team.map((m, k) => `
      <li style="--i:${k}"><img src="${esc(m.avatar)}" alt="" width="58" height="58"><b>${esc(m.name)}</b><small>${pad(k + 1)} ${esc(m.stage)}</small></li>`).join("");
    return `
      <section class="done">
        <div class="stamp" role="img" aria-label="Approved"><div><b>Approved</b><small>QC pass · Tiryaq</small></div></div>
        <h2 id="card-name">Formula complete</h2>
        <p>You’ve met all ${total} people behind Tiryaq — every stage, one team.</p>
        <ol class="roster">${roster}</ol>
        <button class="btn" type="button" data-back>Back to the table</button>
      </section>`;
  }

  /* ---------- open / close ---------- */
  function originFrom(el) {
    const target = el && (el.querySelector(".avatar") || el);
    if (!target) return;
    const r = target.getBoundingClientRect();
    card.style.setProperty("--dx", `${r.left + r.width / 2 - innerWidth / 2}px`);
    card.style.setProperty("--dy", `${r.top + r.height / 2 - innerHeight / 2}px`);
  }

  function setActiveSeat(i) {
    seats.forEach((s, k) => s.classList.toggle("is-active", k === i));
  }

  function markVisited(i) {
    visited.add(i);
    seats[i].classList.add("is-visited");
    updateProgress();
  }

  function showMember(i, dir = 0) {
    const focused = document.activeElement;
    const restoreFocus = sheet.open && body.contains(focused);
    const focusSelector = focused?.hasAttribute("data-next") ? "[data-next]"
      : focused?.hasAttribute("data-prev") ? "[data-prev]"
      : focused?.hasAttribute("data-go") ? `[data-go="${focused.dataset.go}"]` : null;
    current = (i + total) % total;
    mode = "member";
    markVisited(current);
    body.innerHTML = profileView(current);
    const view = body.firstElementChild;
    if (dir) view.classList.add(dir > 0 ? "swap-next" : "swap-prev");
    closeBtn.setAttribute("aria-label", `Close ${team[current].name}’s profile`);
    setActiveSeat(current);
    card.scrollTop = 0;
    if (restoreFocus) {
      let nextFocus = focusSelector && body.querySelector(focusSelector) || closeBtn;
      const targetBounds = nextFocus.getBoundingClientRect();
      const cardBounds = card.getBoundingClientRect();
      if (targetBounds.top < cardBounds.top || targetBounds.bottom > cardBounds.bottom) nextFocus = closeBtn;
      nextFocus.focus({ preventScroll: true });
    }
    // preload neighbours for instant browsing
    [current + 1, current - 1].forEach(k => { const img = new Image(); img.src = team[(k + total) % total].photo; });
  }

  function open(i, fromEl) {
    if (sheet.open || closing) return;
    returnFocus = fromEl || document.activeElement;
    originFrom(fromEl);
    showMember(i);
    openSheet();
  }

  function openDone(fromEl) {
    if (sheet.open || closing) return;
    returnFocus = fromEl || batch;
    card.style.setProperty("--dx", "0px");
    card.style.setProperty("--dy", "30px");
    mode = "done";
    completionShown = true;
    current = -1;
    setActiveSeat(-1);
    body.innerHTML = doneView();
    closeBtn.setAttribute("aria-label", "Close");
    openSheet();
  }

  function openSheet() {
    card.style.animation = "none";
    void card.offsetWidth;   // restart entrance animation
    card.style.animation = "";
    sheet.showModal();
    document.documentElement.classList.add("has-sheet");
  }

  function close() {
    if (!sheet.open || closing) return;
    closing = true;
    sheet.classList.add("is-closing");
    window.setTimeout(() => sheet.close(), reduced.matches ? 0 : 220);
  }

  sheet.addEventListener("close", () => {
    closing = false;
    sheet.classList.remove("is-closing");
    document.documentElement.classList.remove("has-sheet");
    setActiveSeat(-1);
    const finishedNow = mode === "member" && visited.size === total && !completionShown;
    if (finishedNow) {
      window.setTimeout(() => openDone(batch), reduced.matches ? 0 : 180);
      return;
    }
    if (returnFocus && document.contains(returnFocus)) returnFocus.focus({ preventScroll: true });
  });

  /* ---------- events ---------- */
  seatsHost.addEventListener("click", e => {
    const seat = e.target.closest(".seat");
    if (seat) open(Number(seat.dataset.index), seat);
  });

  // Arrow keys move between seats on the table
  seatsHost.addEventListener("keydown", e => {
    const seat = e.target.closest(".seat");
    if (!seat) return;
    const i = Number(seat.dataset.index);
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
    if (step) { e.preventDefault(); seats[(i + step + total) % total].focus(); }
  });

  body.addEventListener("click", e => {
    const go = e.target.closest("[data-go]");
    if (go) { const k = Number(go.dataset.go); if (k !== current) showMember(k, k > current ? 1 : -1); return; }
    if (e.target.closest("[data-next]")) return showMember(current + 1, 1);
    if (e.target.closest("[data-prev]")) return showMember(current - 1, -1);
    if (e.target.closest("[data-back]")) return close();
  });

  closeBtn.addEventListener("click", close);
  sheet.addEventListener("cancel", e => { e.preventDefault(); close(); });

  // click on the backdrop closes
  let downOnBackdrop = false;
  sheet.addEventListener("pointerdown", e => { downOnBackdrop = e.target === sheet; });
  sheet.addEventListener("click", e => { if (downOnBackdrop && e.target === sheet) close(); downOnBackdrop = false; });

  sheet.addEventListener("keydown", e => {
    if (mode !== "member") return;
    if (e.key === "ArrowRight") { e.preventDefault(); showMember(current + 1, 1); }
    if (e.key === "ArrowLeft") { e.preventDefault(); showMember(current - 1, -1); }
  });

  // swipe left / right on the card (touch)
  let sx = 0, sy = 0, swiping = false;
  card.addEventListener("touchstart", e => { const t = e.touches[0]; sx = t.clientX; sy = t.clientY; swiping = true; }, { passive: true });
  card.addEventListener("touchend", e => {
    if (!swiping || mode !== "member") return;
    swiping = false;
    const t = e.changedTouches[0];
    const dx = t.clientX - sx, dy = t.clientY - sy;
    if (Math.abs(dx) > 60 && Math.abs(dy) < 50) showMember(current + (dx < 0 ? 1 : -1), dx < 0 ? 1 : -1);
  }, { passive: true });

  batch.addEventListener("click", () => { if (!batch.disabled) openDone(batch); });

  /* ---------- go ---------- */
  updateProgress();
  requestAnimationFrame(() => requestAnimationFrame(() => app.classList.add("is-ready")));
  window.addEventListener("load", () => {
    team.forEach(m => { const img = new Image(); img.decoding = "async"; img.src = m.photo; });
  });
})();
