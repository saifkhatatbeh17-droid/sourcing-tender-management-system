// Link signals (AI Intelligence, Admin and Top Committee): flagged supplier pairs, a map, and the evidence for one pair.
// The pair list stays beside the evidence on wide screens; on narrow screens the evidence replaces the list.
// Routes (set by ai-intelligence.js): #links, #links/3-7 (open a pair), #links?supplier=5 (pairs involving supplier 5).
(() => {
    const root = document.querySelector("[data-supplier-links]");
    if (!root || !root.querySelector("[data-sl-graph]")) return;
    const page = document.querySelector("[data-ai-intelligence]") || document;
    const svg = root.querySelector("[data-sl-graph]");
    const ws = root.querySelector("[data-sl-ws]");
    const overview = root.querySelector("[data-sl-overview]");
    const items = Array.from(root.querySelectorAll("[data-sl-item]"));
    const none = root.querySelector("[data-sl-none]");
    const levelButtons = Array.from(root.querySelectorAll("[data-sl-level]"));
    const supplierNote = root.querySelector("[data-sl-supplier-filter]");
    const supplierNoteText = root.querySelector("[data-sl-supplier-filter-text]");
    const csrf = document.querySelector("input[name='__RequestVerificationToken']")?.value || "";
    const NS = "http://www.w3.org/2000/svg";
    let graph;
    try { graph = JSON.parse(root.dataset.graph || "{}"); } catch { graph = { nodes: [], links: [] }; }
    const nodes = graph.nodes || [], links = graph.links || [];

    const el = (name, attrs = {}, parent = svg) => {
        const node = document.createElementNS(NS, name);
        for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
        parent.appendChild(node);
        return node;
    };
    const keyOf = (a, b) => `${Math.min(a, b)}-${Math.max(a, b)}`;
    const name = (id) => nodes.find(n => n.id === id)?.name || "Supplier";
    // Server keys keep the order SupplierA-SupplierB; map keys are min-max. Find whichever exists.
    const detailFor = (key) => {
        const [a, b] = key.split("-");
        return root.querySelector(`[data-sl-detail="${a}-${b}"]`) || root.querySelector(`[data-sl-detail="${b}-${a}"]`);
    };

    // ---------- Map: suppliers on a ring, curved lines between flagged pairs ----------
    const W = 700, H = 440, cx = W / 2, cy = H / 2, R = Math.min(W, H) / 2 - 64;
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
    const pos = new Map();
    const order = [...nodes].sort((x, y) => (y.linked - x.linked));
    order.forEach((n, i) => {
        const angle = -Math.PI / 2 + (i / Math.max(1, order.length)) * Math.PI * 2;
        pos.set(n.id, { x: cx + R * Math.cos(angle), y: cy + R * Math.sin(angle), angle });
    });
    el("circle", { cx, cy, r: R, class: "sl-ring" });
    const edgeLayer = el("g", { class: "sl-edges" });
    const nodeLayer = el("g", { class: "sl-nodes" });
    const edgeByKey = new Map();

    for (const link of links) {
        const a = pos.get(link.a), b = pos.get(link.b);
        if (!a || !b) continue;
        const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
        const qx = mx + (cx - mx) * 0.45, qy = my + (cy - my) * 0.45;
        const d = `M${a.x},${a.y} Q${qx},${qy} ${b.x},${b.y}`;
        const key = keyOf(link.a, link.b);
        const g = el("g", { class: `sl-edge is-${link.level}`, tabindex: "0", role: "button", "data-key": key,
            "aria-label": `${name(link.a)} and ${name(link.b)}: ${link.label}. Show the evidence.` }, edgeLayer);
        el("path", { d, class: "sl-edge-hit" }, g);
        el("path", { d, class: "sl-edge-line", "stroke-width": Math.min(7, 2 + link.score * 0.6) }, g);
        g.addEventListener("click", () => goPair(key));
        g.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); goPair(key); } });
        edgeByKey.set(key, g);
    }
    for (const n of order) {
        const p = pos.get(n.id);
        const g = el("g", { class: `sl-node ${n.linked ? "is-linked" : ""}`, "data-id": n.id }, nodeLayer);
        el("circle", { cx: p.x, cy: p.y, r: n.linked ? 9 : 6 }, g);
        const outward = Math.cos(p.angle);
        const label = el("text", {
            x: p.x + Math.cos(p.angle) * 16, y: p.y + Math.sin(p.angle) * 16 + 4,
            "text-anchor": Math.abs(outward) < 0.2 ? "middle" : outward > 0 ? "start" : "end"
        }, g);
        const max = Math.abs(outward) < 0.2 ? 40 : 24;
        label.textContent = n.name.length > max ? n.name.slice(0, max - 1) + "…" : n.name;
        el("title", {}, g).textContent = `${n.name}: ${n.offers} offer${n.offers === 1 ? "" : "s"}`;
    }
    if (nodes.length === 0) el("text", { x: cx, y: cy, "text-anchor": "middle", class: "sl-empty-text" }).textContent = "No submitted offers yet";

    // ---------- Filters: signal strength and one supplier ----------
    let level = "", supplier = null;
    function applyFilters() {
        let shown = 0;
        for (const li of items) {
            const okLevel = !level || li.dataset.level === level;
            const okSupplier = supplier == null || li.dataset.a === String(supplier) || li.dataset.b === String(supplier);
            li.hidden = !(okLevel && okSupplier);
            if (!li.hidden) shown++;
        }
        levelButtons.forEach(b => b.setAttribute("aria-pressed", String(b.dataset.slLevel === level)));
        if (none) none.hidden = shown > 0;
        if (supplierNote) {
            supplierNote.hidden = supplier == null;
            if (supplier != null) {
                const li = items.find(i => i.dataset.a === String(supplier) || i.dataset.b === String(supplier));
                const [na, nb] = (li?.dataset.names || "|").split("|");
                const who = li ? (li.dataset.a === String(supplier) ? na : nb) : `supplier #${supplier}`;
                supplierNoteText.textContent = `Showing pairs with ${who}.`;
            }
        }
        // Dim map lines that the filters hide, so the map and the list agree.
        edgeByKey.forEach((g, key) => {
            const li = items.find(i => keyOf(+i.dataset.a, +i.dataset.b) === key);
            g.classList.toggle("is-filtered-out", Boolean(li?.hidden));
        });
    }
    levelButtons.forEach(b => b.addEventListener("click", () => { level = b.dataset.slLevel; applyFilters(); }));
    root.querySelector("[data-sl-supplier-clear]")?.addEventListener("click", () => {
        supplier = null;
        history.replaceState(null, "", location.pathname + location.search + "#links");
        applyFilters();
    });

    // ---------- Selection ----------
    let openKey = null;
    function show(key, { focus = true } = {}) {
        const detail = key ? detailFor(key) : null;
        openKey = detail ? key : null;
        root.querySelectorAll("[data-sl-detail]").forEach(d => { d.hidden = d !== detail; });
        if (overview) overview.hidden = Boolean(detail);
        ws?.classList.toggle("has-detail", Boolean(detail));
        const mapKey = detail ? keyOf(...detail.dataset.slDetail.split("-").map(Number)) : null;
        root.querySelectorAll("[data-sl-open]").forEach(btn => {
            const on = detail && btn.dataset.slOpen === detail.dataset.slDetail;
            btn.setAttribute("aria-pressed", String(Boolean(on)));
            btn.classList.toggle("is-current", Boolean(on));
        });
        svg.classList.toggle("has-selection", Boolean(detail));
        edgeByKey.forEach((g, k) => g.classList.toggle("is-selected", k === mapKey));
        if (detail && focus) detail.focus({ preventScroll: true });
        // On narrow screens the evidence replaces the list, so bring its top into view.
        if (detail && window.matchMedia("(max-width: 960px)").matches) detail.scrollIntoView({ block: "start" });
    }
    const goPair = (key) => {
        const detail = detailFor(key);
        if (!detail) return;
        const hash = `#links/${detail.dataset.slDetail}`;
        if (location.hash === hash) show(detail.dataset.slDetail); else location.hash = hash;
    };

    root.addEventListener("click", e => {
        const opener = e.target.closest("[data-sl-open]");
        if (opener) return goPair(opener.dataset.slOpen);
        if (e.target.closest("[data-sl-back]")) {
            const from = openKey;
            location.hash = supplier != null ? `#links?supplier=${supplier}` : "#links";
            requestAnimationFrame(() => root.querySelector(`[data-sl-open="${from}"]`)?.focus({ preventScroll: true }));
            return;
        }
        const explain = e.target.closest("[data-sl-explain]");
        if (explain) brief(explain);
    });
    root.addEventListener("keydown", e => { if (e.key === "Escape" && openKey) root.querySelector("[data-sl-back]:not([hidden])")?.click(); });

    // Hovering a pair previews its line on the map (when the map is showing).
    root.addEventListener("pointerover", e => {
        const btn = e.target.closest("[data-sl-open]");
        const key = btn ? keyOf(...btn.dataset.slOpen.split("-").map(Number)) : null;
        edgeByKey.forEach((g, k) => g.classList.toggle("is-hover", k === key));
    });

    // Follow the router: #links, #links/3-7, #links?supplier=5. Arriving from a tender page (?pair=3-7) gives a short highlight.
    page.addEventListener("ai:route", (e) => {
        const r = e.detail;
        if (r.view !== "links") return;
        const sup = r.params.get("supplier");
        supplier = sup && /^\d+$/.test(sup) ? Number(sup) : (r.sub ? supplier : null);
        applyFilters();
        if (r.sub && /^\d+-\d+$/.test(r.sub)) {
            show(r.sub, { focus: !r.legacy });
            if (r.legacy) {
                const detail = detailFor(r.sub);
                detail?.classList.add("is-arrived");
                setTimeout(() => detail?.classList.remove("is-arrived"), 4000);
            }
        } else {
            show(null);
            if (supplier != null) {
                const first = items.find(i => !i.hidden);
                // One matching pair: open it straight away.
                if (first && items.filter(i => !i.hidden).length === 1) goPair(first.querySelector("[data-sl-open]").dataset.slOpen);
            }
        }
    });

    // ---------- AI brief ----------
    async function brief(button) {
        const box = button.closest("[data-sl-brief]");
        const label = button.textContent;
        button.disabled = true;
        button.textContent = button.dataset.busyLabel || "Working…";
        try {
            const response = await fetch("/AiIntelligence/SupplierLinkBrief", {
                method: "POST",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": csrf },
                body: JSON.stringify({ supplierA: +button.dataset.a, supplierB: +button.dataset.b, workspace: root.dataset.workspace || null })
            });
            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data.error || "The AI brief is not available right now.");
            box.replaceChildren();
            const head = document.createElement("p");
            head.className = "sl-brief-label";
            head.textContent = "AI brief";
            const summary = document.createElement("p");
            summary.className = "sl-brief-text";
            summary.textContent = data.summary;
            box.append(head, summary);
            if (data.checks?.length) {
                const sub = document.createElement("p");
                sub.className = "sl-brief-label";
                sub.textContent = "Check next";
                const ul = document.createElement("ul");
                ul.className = "sl-brief-checks";
                for (const c of data.checks) { const li = document.createElement("li"); li.textContent = c; ul.appendChild(li); }
                box.append(sub, ul);
            }
        } catch (err) {
            button.disabled = false;
            button.textContent = label;
            let note = box.querySelector(".sl-brief-error");
            if (!note) { note = document.createElement("p"); note.className = "sl-brief-error"; note.setAttribute("role", "status"); box.appendChild(note); }
            note.textContent = err.message;
        }
    }

    applyFilters();
    // ai-intelligence.js routed before this script loaded; apply the current route now.
    if (location.hash.startsWith("#links")) {
        const [path, qs] = location.hash.slice(1).split("?");
        const sub = path.split("/")[1] || "";
        const arrived = Boolean(sub) && new URLSearchParams(location.search).get("pair") === sub;   // came from a tender page
        page.dispatchEvent(new CustomEvent("ai:route", { detail: { view: "links", sub, params: new URLSearchParams(qs || ""), legacy: arrived } }));
    }
})();
