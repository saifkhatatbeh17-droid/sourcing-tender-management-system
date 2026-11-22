// Supplier links map (Admin AI Intelligence): suppliers on a ring, curved lines between linked pairs.
// Selecting a line or a pair in the list opens its evidence; "Explain with AI" asks for a short brief.
(() => {
    const root = document.querySelector("[data-supplier-links]");
    if (!root || !root.querySelector("[data-sl-graph]")) return;
    const svg = root.querySelector("[data-sl-graph]");
    const list = root.querySelector("[data-sl-list]");
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

    // ---------- Drawing ----------
    const W = 700, H = 440, cx = W / 2, cy = H / 2, R = Math.min(W, H) / 2 - 64;
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
    const pos = new Map();
    // Linked suppliers sit next to each other so their lines stay short and readable.
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
        // Bend each line toward the centre so lines between neighbours do not hug the ring.
        const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
        const qx = mx + (cx - mx) * 0.45, qy = my + (cy - my) * 0.45;
        const d = `M${a.x},${a.y} Q${qx},${qy} ${b.x},${b.y}`;
        const key = keyOf(link.a, link.b);
        const g = el("g", { class: `sl-edge is-${link.level}`, tabindex: "0", role: "button", "data-key": key,
            "aria-label": `${name(link.a)} and ${name(link.b)}: ${link.label}. Show why.` }, edgeLayer);
        el("path", { d, class: "sl-edge-hit" }, g);
        el("path", { d, class: "sl-edge-line", "stroke-width": Math.min(7, 2 + link.score * 0.6) }, g);
        g.addEventListener("click", () => open(key));
        g.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); open(key); } });
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
        const title = el("title", {}, g);
        title.textContent = `${n.name}: ${n.offers} offer${n.offers === 1 ? "" : "s"}`;
    }

    if (nodes.length === 0) {
        const t = el("text", { x: cx, y: cy, "text-anchor": "middle", class: "sl-empty-text" });
        t.textContent = "No submitted offers yet";
    }

    function name(id) { return nodes.find(n => n.id === id)?.name || "Supplier"; }

    // ---------- Selection ----------
    function open(key) {
        root.querySelectorAll("[data-sl-detail]").forEach(d => d.hidden = d.dataset.slDetail !== key);
        list.hidden = true;
        svg.classList.add("has-selection");
        edgeByKey.forEach((g, k) => g.classList.toggle("is-selected", k === key));
        const [a, b] = key.split("-").map(Number);
        nodeLayer.querySelectorAll(".sl-node").forEach(n => n.classList.toggle("is-selected", +n.dataset.id === a || +n.dataset.id === b));
        root.querySelector(`[data-sl-detail="${key}"]`)?.focus({ preventScroll: true });
    }

    function close() {
        root.querySelectorAll("[data-sl-detail]").forEach(d => d.hidden = true);
        list.hidden = false;
        svg.classList.remove("has-selection");
        svg.querySelectorAll(".is-selected").forEach(n => n.classList.remove("is-selected"));
        list.querySelector("button")?.focus({ preventScroll: true });
    }

    root.addEventListener("click", e => {
        const opener = e.target.closest("[data-sl-open]");
        if (opener) return open(opener.dataset.slOpen);
        if (e.target.closest("[data-sl-back]")) return close();
        const explain = e.target.closest("[data-sl-explain]");
        if (explain) brief(explain);
    });
    root.addEventListener("keydown", e => { if (e.key === "Escape" && list.hidden) close(); });

    // Hovering a pair in the list previews its line on the map.
    list.addEventListener("pointerover", e => {
        const key = e.target.closest("[data-sl-open]")?.dataset.slOpen;
        edgeByKey.forEach((g, k) => g.classList.toggle("is-hover", k === key));
    });
    list.addEventListener("pointerleave", () => edgeByKey.forEach(g => g.classList.remove("is-hover")));

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
                body: JSON.stringify({ supplierA: +button.dataset.a, supplierB: +button.dataset.b })
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
})();
