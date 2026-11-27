// AI Intelligence workspace: one view at a time (Overview, Ask AI, Tenders, Suppliers, Link signals).
// The URL hash holds the view, so refresh, Back/Forward and deep links all land in the same place:
//   #overview  #ask  #ask/file  #tenders  #tenders/12  #suppliers  #links  #links/3-7  #links?supplier=5
// Older links still work: ?tenderId=12#tender-explorer opens that tender, #supplier-links opens Link signals.
// Link signals has its own script (supplier-links.js) for the pair list, map and evidence.
(() => {
    const root = document.querySelector("[data-ai-intelligence]");
    if (!root) return;

    const workspace = root.dataset.workspace || "";
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const scrollBehavior = reduceMotion ? "auto" : "smooth";
    const csrfToken = root.querySelector("input[name='__RequestVerificationToken']")?.value || "";
    const storeKey = `ai-intel:${workspace}:`;

    // The sticky tab and filter bars are translucent; tint them with the shell's real canvas colour,
    // which differs between role shells and themes.
    const paintCanvas = () => {
        for (let el = root.parentElement; el; el = el.parentElement) {
            const bg = getComputedStyle(el).backgroundColor;
            if (bg && bg !== "transparent" && !/rgba\(.*,\s*0\)$/.test(bg)) { root.style.setProperty("--ai-canvas", bg); return; }
        }
    };
    paintCanvas();
    new MutationObserver(paintCanvas).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme", "data-bs-theme"] });

    // Matches the server limit on /AiIntelligence/AnalyzeUpload. The request cap is 10 MiB for the whole
    // multipart body, so the file itself must stay a little under it. The server stays authoritative.
    const UPLOAD_MAX_BYTES = 10 * 1024 * 1024 - 64 * 1024;
    const UPLOAD_EXTENSIONS = [".xlsx", ".pdf"];

    const esc = (val) => {
        const div = document.createElement("div");
        div.textContent = val == null ? "" : String(val);
        return div.innerHTML;
    };
    const fmtMoney = (n, cur) => {
        if (n == null || Number.isNaN(Number(n))) return "N/A";
        return `${Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${cur || "JOD"}`;
    };
    const fmtBytes = (bytes) => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };
    const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;
    const store = {
        get(key) { try { return JSON.parse(sessionStorage.getItem(storeKey + key) || "null"); } catch { return null; } },
        set(key, value) { try { sessionStorage.setItem(storeKey + key, JSON.stringify(value)); } catch { /* private mode: state just isn't remembered */ } }
    };

    // Busy state for any button that starts a request: disabled, aria-busy and a short progress label.
    function setBusy(button, busy) {
        if (!button) return;
        if (busy) {
            button.dataset.idleLabel ??= button.textContent.trim();
            button.textContent = button.dataset.busyLabel || "Working…";
            button.disabled = true;
            button.setAttribute("aria-busy", "true");
        } else {
            if (button.dataset.idleLabel) button.textContent = button.dataset.idleLabel;
            button.disabled = false;
            button.removeAttribute("aria-busy");
        }
    }
    function showStatus(el, text, isError = false) {
        if (!el) return;
        el.hidden = false;
        el.textContent = text;
        if (isError) el.dataset.error = "true"; else delete el.dataset.error;
    }
    const hideStatus = (el) => { if (el) el.hidden = true; };

    // Small keyboard helper for tab lists: arrows, Home and End move between tabs.
    function arrowKeys(tabs, onPick) {
        tabs.forEach((tab, i) => tab.addEventListener("keydown", (e) => {
            const next = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: tabs.length - 1 }[e.key];
            if (next === undefined) return;
            e.preventDefault();
            const target = tabs[(next + tabs.length) % tabs.length];
            target.focus();
            onPick(target);
        }));
    }

    /* =====================================================================
       1. Views and routing
       ===================================================================== */
    const viewTabs = Array.from(root.querySelectorAll("[data-view-tab]"));
    const views = new Map(Array.from(root.querySelectorAll("[data-view]")).map(v => [v.dataset.view, v]));
    let currentView = null;

    function showView(name) {
        if (!views.has(name)) name = "overview";
        for (const tab of viewTabs) {
            const on = tab.dataset.viewTab === name;
            tab.setAttribute("aria-selected", String(on));
            tab.tabIndex = on ? 0 : -1;
            // On narrow screens the tab bar scrolls sideways; keep the active tab in sight.
            if (on && tab.parentElement.scrollWidth > tab.parentElement.clientWidth) {
                tab.parentElement.scrollTo({ left: tab.offsetLeft - 8, behavior: "auto" });
            }
        }
        views.forEach((view, key) => { view.hidden = key !== name; });
        const changed = currentView !== name;
        currentView = name;
        root.dataset.activeView = name;
        return changed;
    }

    // Hash -> { view, sub, params }. "tenders/12" -> view tenders, sub "12".
    function parseHash() {
        let raw = decodeURIComponent(location.hash.replace(/^#/, ""));
        const query = new URLSearchParams(location.search);
        let [path, qs] = raw.split("?");
        const legacy = {
            "": null, "ask-ai": "ask", "tender-explorer": "tenders", "supplier-links": "links", "overview": "overview"
        };
        if (path in legacy) {
            // Old anchors and server links: ?tenderId=12#tender-explorer, ?pair=3-7#supplier-links
            if (path === "tender-explorer" || (path === "" && query.get("tenderId") && !query.get("pair"))) {
                const id = query.get("tenderId");
                return { view: "tenders", sub: id || "", params: new URLSearchParams(), legacy: true };
            }
            if (path === "supplier-links" || (path === "" && query.get("pair"))) {
                return { view: "links", sub: query.get("pair") || "", params: new URLSearchParams(), legacy: true };
            }
            path = legacy[path] || "overview";
        }
        const [view, ...rest] = path.split("/");
        return { view, sub: rest.join("/"), params: new URLSearchParams(qs || ""), legacy: false };
    }

    function route({ initial = false } = {}) {
        const r = parseHash();
        if (r.legacy) {
            const canonical = `#${r.view}${r.sub ? "/" + r.sub : ""}`;
            history.replaceState(null, "", location.pathname + location.search + canonical);
        }
        const changed = showView(r.view);
        if (r.view === "tenders") {
            if (r.sub && /^\d+$/.test(r.sub)) openTenderDetail(Number(r.sub));
            else showTenderList();
        } else if (r.view === "ask") {
            selectAiTab(r.sub === "file" ? aiTabs[1] : aiTabs[0]);
        }
        root.dispatchEvent(new CustomEvent("ai:route", { detail: r }));
        if (changed && !initial) {
            const top = root.querySelector(".ai-ws-tabs-wrap");
            if (top && top.getBoundingClientRect().top < 0) top.scrollIntoView({ behavior: "auto", block: "start" });
        }
    }

    const go = (hash) => { if (location.hash === hash) route(); else location.hash = hash; };

    viewTabs.forEach(tab => tab.addEventListener("click", () => go(`#${tab.dataset.viewTab}`)));
    arrowKeys(viewTabs, tab => go(`#${tab.dataset.viewTab}`));
    window.addEventListener("hashchange", () => route());

    /* =====================================================================
       2. Overview: donut, shortcuts into filtered tender lists, quick ask
       ===================================================================== */
    const donutSvg = root.querySelector("[data-status-donut]");
    if (donutSvg) {
        try {
            const segments = JSON.parse(root.dataset.statusChart || "[]");
            const total = segments.reduce((acc, s) => acc + Number(s.value || 0), 0);
            if (total > 0) {
                const r = 44, c = 2 * Math.PI * r, ns = "http://www.w3.org/2000/svg";
                let offset = 0;
                const ring = (attrs) => {
                    const el = document.createElementNS(ns, "circle");
                    for (const [k, v] of Object.entries({ cx: 60, cy: 60, r, fill: "none", "stroke-width": 18, ...attrs })) el.setAttribute(k, String(v));
                    donutSvg.append(el);
                    return el;
                };
                ring({ stroke: "rgba(120,140,150,0.18)" });
                for (const seg of segments) {
                    const val = Number(seg.value || 0);
                    if (val <= 0) continue;
                    const len = (val / total) * c;
                    const circle = ring({ stroke: seg.color || "#1d5560", "stroke-dasharray": `${len} ${c - len}`, "stroke-dashoffset": -offset, transform: "rotate(-90 60 60)" });
                    const title = document.createElementNS(ns, "title");
                    title.textContent = `${seg.label}: ${seg.valueFormatted} (${seg.secondaryFormatted})`;
                    circle.append(title);
                    offset += len;
                }
                const text = document.createElementNS(ns, "text");
                for (const [k, v] of Object.entries({ x: 60, y: 65, "text-anchor": "middle", "font-size": 18, "font-weight": 800, fill: "currentColor" })) text.setAttribute(k, String(v));
                text.textContent = String(total);
                donutSvg.append(text);
                donutSvg.setAttribute("aria-label", `${plural(total, "tender", "tenders")} by status`);
            }
        } catch { /* chart stays empty; the legend still lists every status */ }
    }

    // "All 4 waiting", status legend and alert shortcuts open the Tenders list already filtered.
    root.querySelectorAll("[data-go-tenders]").forEach(link => link.addEventListener("click", (e) => {
        e.preventDefault();
        setTenderFilters({ q: "", status: link.dataset.filter || "" });
        go("#tenders");
        tenderSearch?.focus({ preventScroll: true });
    }));

    root.querySelector("[data-quick-ask]")?.addEventListener("submit", (e) => {
        e.preventDefault();
        const quick = e.currentTarget.querySelector("input");
        const text = quick.value.trim();
        go("#ask");
        if (questionInput) questionInput.value = text;
        quick.value = "";
        runPortfolioAi(text);
    });

    /* =====================================================================
       3. Ask AI: question or file, answers kept as a thread
       ===================================================================== */
    const aiTabs = Array.from(root.querySelectorAll("[data-ai-tabs] [role='tab']"));
    function selectAiTab(tab) {
        if (!tab) return;
        for (const t of aiTabs) {
            const on = t === tab;
            t.setAttribute("aria-selected", String(on));
            t.tabIndex = on ? 0 : -1;
            const panel = document.getElementById(t.getAttribute("aria-controls"));
            if (panel) panel.hidden = !on;
        }
    }
    aiTabs.forEach((tab, i) => tab.addEventListener("click", () => go(i === 1 ? "#ask/file" : "#ask")));
    arrowKeys(aiTabs, tab => go(aiTabs.indexOf(tab) === 1 ? "#ask/file" : "#ask"));

    const thread = root.querySelector("[data-ai-thread]");
    const threadEmpty = root.querySelector("[data-thread-empty]");
    const threadClear = root.querySelector("[data-thread-clear]");
    const timeNow = () => new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

    const skeletonMarkup = `
        <div class="ai-answer ai-skeleton" aria-hidden="true">
            <span class="ai-skel-line is-title"></span>
            <span class="ai-skel-line"></span>
            <span class="ai-skel-line is-short"></span>
        </div>`;

    // One entry in the thread: what was asked, then the answer (or a placeholder while the AI works).
    function addThreadEntry(kind, prompt) {
        if (threadEmpty) threadEmpty.hidden = true;
        if (threadClear) threadClear.hidden = false;
        const entry = document.createElement("article");
        entry.className = "ai-thread-entry";
        entry.setAttribute("aria-busy", "true");
        entry.tabIndex = -1;
        entry.innerHTML = `
            <header class="ai-thread-q">
                <span class="ai-thread-kind" data-kind="${kind}">${kind === "file" ? "File" : "You asked"}</span>
                <span class="ai-thread-text">${esc(prompt)}</span>
                <time class="ai-intel-muted">${esc(timeNow())}</time>
            </header>
            <div class="ai-thread-a">${skeletonMarkup}</div>`;
        thread.prepend(entry);
        return entry;
    }
    function settleEntry(entry, html, { error = false } = {}) {
        entry.removeAttribute("aria-busy");
        entry.querySelector(".ai-thread-a").innerHTML = error
            ? `<p class="ai-intel-status-msg" data-error="true">${esc(html)}</p>`
            : html + `<div class="ai-thread-actions"><button type="button" class="ai-intel-btn is-surface is-small" data-follow-up>Ask a follow-up</button></div>`;
        entry.focus({ preventScroll: true });
        entry.scrollIntoView({ behavior: scrollBehavior, block: "nearest" });
    }
    thread?.addEventListener("click", (e) => {
        if (!e.target.closest("[data-follow-up]")) return;
        go("#ask");
        questionInput?.focus();
    });
    threadClear?.addEventListener("click", () => {
        thread.querySelectorAll(".ai-thread-entry").forEach(n => n.remove());
        if (threadEmpty) threadEmpty.hidden = false;
        threadClear.hidden = true;
        questionInput?.focus();
    });

    // AI answers: headline, a short brief, then at most three points with their next step.
    function insightListMarkup(items) {
        const rows = (items || []).slice(0, 3).map(item => `
            <li data-tone="${esc(item.tone || "info")}">
                <span class="ai-answer-dot" aria-hidden="true"></span>
                <div>
                    <strong>${esc(item.title)}.</strong> ${esc(item.finding)}
                    ${item.recommendation ? `<span class="ai-answer-next"><b>Next step:</b> ${esc(item.recommendation)}</span>` : ""}
                </div>
            </li>`).join("");
        return rows ? `<ul class="ai-answer-list">${rows}</ul>` : "";
    }
    const buildInsightsMarkup = (res, label = "AI answer") => `
        <div class="ai-answer">
            <p class="ai-result-label">${esc(label)}</p>
            <h3 class="ai-answer-head">${esc(res.headline)}</h3>
            ${res.executiveBrief ? `<p class="ai-result-brief">${esc(res.executiveBrief)}</p>` : ""}
            ${insightListMarkup(res.insights)}
        </div>`;

    const portfolioForm = root.querySelector("[data-portfolio-form]");
    const portfolioStatus = root.querySelector("[data-portfolio-status]");
    const portfolioSubmit = root.querySelector("[data-portfolio-submit]");
    const questionInput = portfolioForm?.elements.question;

    async function runPortfolioAi(questionText) {
        if (portfolioSubmit?.disabled) return;
        setBusy(portfolioSubmit, true);
        hideStatus(portfolioStatus);
        const entry = addThreadEntry("question", questionText || "General brief of my workspace");
        try {
            const resp = await fetch("/AiIntelligence/PortfolioInsights", {
                method: "POST",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken },
                body: JSON.stringify({ workspace, question: questionText || null })
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) throw new Error(data?.error || "The AI brief could not be generated. Try again in a moment.");
            settleEntry(entry, buildInsightsMarkup(data));
            if (questionInput) questionInput.value = "";
        } catch (err) {
            settleEntry(entry, err.message, { error: true });
        } finally {
            setBusy(portfolioSubmit, false);
        }
    }

    portfolioForm?.addEventListener("submit", (e) => {
        e.preventDefault();
        runPortfolioAi(questionInput?.value?.trim() || "");
    });
    // Enter sends; Shift+Enter keeps a new line.
    questionInput?.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            portfolioForm.requestSubmit();
        }
    });
    // Suggested questions only fill the box; nothing is sent until the user submits.
    root.querySelectorAll("[data-suggestion]").forEach(chip => chip.addEventListener("click", () => {
        if (!questionInput) return;
        questionInput.value = chip.dataset.suggestion || "";
        questionInput.focus();
    }));

    // File analysis
    const uploadForm = root.querySelector("[data-upload-form]");
    const uploadSubmit = root.querySelector("[data-upload-submit]");
    const uploadStatus = root.querySelector("[data-upload-status]");
    const fileInput = uploadForm?.querySelector("[data-upload-file]");
    const fileLabel = uploadForm?.querySelector("[data-upload-filename]");
    const fileLabelDefault = fileLabel?.textContent || "";

    function validateFile(file) {
        if (!file) return "Choose an Excel (.xlsx) or PDF (.pdf) file first.";
        const name = file.name.toLowerCase();
        if (!UPLOAD_EXTENSIONS.some(ext => name.endsWith(ext))) return `${file.name} is not supported. Choose an Excel (.xlsx) or PDF (.pdf) file.`;
        if (file.size === 0) return `${file.name} is empty. Choose a file with content.`;
        if (file.size > UPLOAD_MAX_BYTES) return `${file.name} is ${fmtBytes(file.size)}. Choose a file under 10 MB.`;
        return null;
    }
    function refreshFileState() {
        const file = fileInput?.files?.[0];
        const problem = file ? validateFile(file) : null;
        uploadForm?.classList.toggle("has-file", Boolean(file) && !problem);
        uploadForm?.classList.toggle("has-error", Boolean(problem));
        if (fileLabel) fileLabel.textContent = file ? `${file.name} (${fmtBytes(file.size)})` : fileLabelDefault;
        if (uploadSubmit) uploadSubmit.disabled = !file || Boolean(problem);
        if (problem) showStatus(uploadStatus, problem, true); else hideStatus(uploadStatus);
    }
    fileInput?.addEventListener("change", refreshFileState);
    uploadForm?.addEventListener("dragover", (e) => { e.preventDefault(); uploadForm.classList.add("is-dragover"); });
    uploadForm?.addEventListener("dragleave", (e) => { if (!uploadForm.contains(e.relatedTarget)) uploadForm.classList.remove("is-dragover"); });
    uploadForm?.addEventListener("drop", (e) => {
        e.preventDefault();
        uploadForm.classList.remove("is-dragover");
        if (e.dataTransfer?.files?.length && fileInput) { fileInput.files = e.dataTransfer.files; refreshFileState(); }
    });

    uploadForm?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const file = fileInput?.files?.[0];
        const problem = validateFile(file);
        if (problem) { showStatus(uploadStatus, problem, true); fileInput?.focus(); return; }

        const fd = new FormData(uploadForm);
        fd.set("workspace", workspace);
        const instruction = (fd.get("prompt") || "").toString().trim();
        setBusy(uploadSubmit, true);
        hideStatus(uploadStatus);
        const entry = addThreadEntry("file", instruction ? `${file.name}: ${instruction}` : file.name);

        try {
            const resp = await fetch("/AiIntelligence/AnalyzeUpload", { method: "POST", headers: { "RequestVerificationToken": csrfToken }, body: fd });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) {
                throw new Error(resp.status === 413
                    ? `${file.name} is too large. Choose a file under 10 MB.`
                    : data?.error || "The file could not be analyzed. Check the file and try again.");
            }
            const kpiHtml = (data.extractedMetrics || []).map(k => `
                <div class="ai-intel-kpi" data-tone="${esc(k.tone || "primary")}">
                    <div class="ai-intel-kpi-label">${esc(k.label)}</div>
                    <div class="ai-intel-kpi-value is-text">${esc(k.value)}</div>
                    <div class="ai-intel-kpi-sub">${esc(k.subtext)}</div>
                </div>`).join("");
            const dlHtml = (data.downloads || []).map(dl => `
                <a class="ai-intel-btn is-surface is-small" href="${esc(dl.url)}" download="${esc(dl.fileName)}">${esc(dl.label || dl.fileName)}</a>`).join("");
            let tableHtml = "";
            if (Array.isArray(data.tableHeaders) && data.tableHeaders.length > 0) {
                const ths = data.tableHeaders.map(h => `<th scope="col">${esc(h)}</th>`).join("");
                const trs = (data.tableRows || []).map(r => `<tr>${r.map(c => `<td>${esc(c)}</td>`).join("")}</tr>`).join("");
                tableHtml = `
                    <details class="ai-intel-details">
                        <summary>Extracted data (${plural((data.tableRows || []).length, "row", "rows")})</summary>
                        <div class="ai-intel-table-wrap"><table class="ai-intel-table"><thead><tr>${ths}</tr></thead><tbody>${trs}</tbody></table></div>
                    </details>`;
            }
            settleEntry(entry, `
                <div class="ai-answer">
                    <div class="ai-answer-file">
                        <div>
                            <p class="ai-result-label">AI file analysis · ${esc(data.fileType)}</p>
                            <h3 class="ai-answer-head">${esc(data.fileName)}</h3>
                        </div>
                        <div class="ai-intel-toolbar">${dlHtml}</div>
                    </div>
                    <p class="ai-result-brief">${esc(data.summary)}</p>
                    ${kpiHtml ? `<div class="ai-intel-kpi-grid is-compact">${kpiHtml}</div>` : ""}
                    ${insightListMarkup(data.insights)}
                    ${tableHtml}
                </div>`);
        } catch (err) {
            settleEntry(entry, err.message, { error: true });
        } finally {
            setBusy(uploadSubmit, false);
            if (uploadSubmit) uploadSubmit.disabled = Boolean(validateFile(fileInput?.files?.[0]));
        }
    });

    /* =====================================================================
       4. Tenders: filtered list, then one tender at a time
       ===================================================================== */
    const listPane = root.querySelector("[data-tender-list]");
    const detailPane = root.querySelector("[data-tender-detail]");
    const tenderSearch = root.querySelector("[data-tender-search]");
    const tenderStatusFilter = root.querySelector("[data-tender-status-filter]");
    const tenderClear = root.querySelector("[data-tender-clear]");
    const tenderCount = root.querySelector("[data-tender-count]");
    const tenderEmpty = root.querySelector("[data-tender-empty]");
    const tenderRows = Array.from(root.querySelectorAll("[data-tenders-table] tbody tr[data-tender-row]"));

    // Search the data cells only, so the open arrow never matches anything.
    const searchTextOf = (tr) => Array.from(tr.cells).map(td => td.textContent).join(" ").toLowerCase();
    const tenderSearchText = new Map(tenderRows.map(tr => [tr, searchTextOf(tr)]));

    // Paging: 15 rows a page. Filters mark rows with data-match; the pager shows the matching rows of one page.
    const PAGE_SIZE = 15;
    function makePager(table, rows, { label, storeKey: key, noun }) {
        const wrap = table?.closest(".ai-intel-table-wrap");
        if (!wrap) return { render() {}, setPage() {}, showRow() {} };
        const nav = document.createElement("nav");
        nav.className = "ai-pager";
        nav.setAttribute("aria-label", label);
        nav.innerHTML = `
            <button type="button" class="ai-intel-btn is-small" data-page-prev><span aria-hidden="true">‹</span> Previous</button>
            <p class="ai-pager-info" aria-live="polite"></p>
            <button type="button" class="ai-intel-btn is-small" data-page-next>Next <span aria-hidden="true">›</span></button>`;
        wrap.after(nav);
        const info = nav.querySelector(".ai-pager-info");
        const prev = nav.querySelector("[data-page-prev]");
        const next = nav.querySelector("[data-page-next]");
        let page = Number(store.get(key)) || 1;

        // Rows in their current DOM order, so sorting is respected.
        const matching = () => Array.from(table.querySelectorAll("tbody tr")).filter(tr => rows.includes(tr) && tr.dataset.match !== "0");
        function render() {
            const list = matching();
            const pages = Math.max(1, Math.ceil(list.length / PAGE_SIZE));
            page = Math.min(Math.max(1, page), pages);
            const start = (page - 1) * PAGE_SIZE;
            for (const tr of rows) tr.hidden = tr.dataset.match === "0";
            list.forEach((tr, i) => { tr.hidden = i < start || i >= start + PAGE_SIZE; });
            nav.hidden = list.length <= PAGE_SIZE;
            info.innerHTML = `Page <strong>${page}</strong> of ${pages} <span class="ai-intel-muted">· ${start + 1}–${Math.min(start + PAGE_SIZE, list.length)} of ${plural(list.length, noun[0], noun[1])}</span>`;
            prev.disabled = page <= 1;
            next.disabled = page >= pages;
            store.set(key, page);
        }
        function setPage(n, { scroll = false } = {}) {
            page = n;
            render();
            if (scroll) {
                const top = wrap.getBoundingClientRect().top;
                if (top < 0 || top > window.innerHeight * .6) wrap.scrollIntoView({ block: "start", behavior: "smooth" });
            }
        }
        // Jump to the page holding this row (used when coming back from a tender).
        function showRow(tr) {
            const i = matching().indexOf(tr);
            if (i >= 0) setPage(Math.floor(i / PAGE_SIZE) + 1);
        }
        prev.addEventListener("click", () => setPage(page - 1, { scroll: true }));
        next.addEventListener("click", () => setPage(page + 1, { scroll: true }));
        return { render, setPage, showRow };
    }
    const tenderPager = makePager(root.querySelector("[data-tenders-table]"), tenderRows,
        { label: "Tender pages", storeKey: "tenderPage", noun: ["tender", "tenders"] });

    function rowMatches(tr, q, st) {
        const matchText = !q || tenderSearchText.get(tr).includes(q);
        const matchStatus = !st
            || (st === "__waiting" ? tr.dataset.waiting === "1"
            : st === "__nooffers" ? tr.dataset.nooffers === "1"
            : tr.dataset.status === st);
        return matchText && matchStatus;
    }

    function filterTendersTable({ keepPage = false } = {}) {
        const q = (tenderSearch?.value || "").trim().toLowerCase();
        const st = tenderStatusFilter?.value || "";
        let shown = 0;
        for (const tr of tenderRows) {
            const match = rowMatches(tr, q, st);
            tr.dataset.match = match ? "1" : "0";
            if (match) shown++;
        }
        if (keepPage) tenderPager.render(); else tenderPager.setPage(1);
        const filtered = Boolean(q || st);
        if (tenderEmpty) tenderEmpty.hidden = shown > 0 || tenderRows.length === 0;
        if (tenderClear) tenderClear.hidden = !filtered;
        tenderStatusFilter?.classList.toggle("is-active", Boolean(st));
        if (tenderCount) {
            tenderCount.textContent = filtered
                ? `Showing ${shown} of ${plural(tenderRows.length, "tender", "tenders")}`
                : plural(tenderRows.length, "tender", "tenders");
        }
        store.set("tenders", { q: tenderSearch?.value || "", status: st });
    }
    function setTenderFilters({ q, status }, opts) {
        if (tenderSearch) tenderSearch.value = q ?? "";
        if (tenderStatusFilter) {
            const ok = Array.from(tenderStatusFilter.options).some(o => o.value === status);
            tenderStatusFilter.value = ok ? status : "";
        }
        filterTendersTable(opts);
    }
    tenderSearch?.addEventListener("input", () => filterTendersTable());
    tenderStatusFilter?.addEventListener("change", () => filterTendersTable());
    const clearFilters = () => { setTenderFilters({ q: "", status: "" }); tenderSearch?.focus(); };
    tenderClear?.addEventListener("click", clearFilters);
    root.querySelector("[data-tender-clear-inline]")?.addEventListener("click", clearFilters);

    // Remember search and status for this browser tab, so returning to the page restores them.
    const savedFilters = store.get("tenders");
    if (savedFilters) setTenderFilters(savedFilters, { keepPage: true }); else filterTendersTable({ keepPage: true });

    // Whole row opens the tender; the title link does the same and is what keyboard users reach.
    tenderRows.forEach(tr => tr.addEventListener("click", (e) => {
        if (e.target.closest("a")) return;
        go(`#tenders/${tr.dataset.tenderRow}`);
    }));

    let listScroll = 0;
    let lastOpenedId = null;
    function showTenderList() {
        const wasDetail = detailPane && !detailPane.hidden;
        if (listPane) listPane.hidden = false;
        if (detailPane) detailPane.hidden = true;
        if (wasDetail) {
            // Previous/next may have moved past this page; open the page that holds the tender you came from.
            const fromRow = lastOpenedId && tenderRows.find(tr => tr.dataset.tenderRow === String(lastOpenedId));
            if (fromRow && fromRow.hidden) { tenderPager.showRow(fromRow); listScroll = Math.min(listScroll, listPane.offsetTop); }
            // Back where you were: same filters, same scroll position, focus on the tender you came from.
            requestAnimationFrame(() => {
                window.scrollTo({ top: listScroll, behavior: "auto" });
                const link = lastOpenedId && root.querySelector(`[data-tender-row="${lastOpenedId}"] a[data-open-tender]`);
                link?.focus({ preventScroll: true });
                const row = link?.closest("tr");
                if (row) { row.classList.add("is-recent"); setTimeout(() => row.classList.remove("is-recent"), 2200); }
            });
        }
    }

    // Detail view
    const tenderStatus = root.querySelector("[data-tender-status]");
    const tenderAiBox = root.querySelector("[data-tender-ai-box]");
    const reportHeading = root.querySelector("[data-tender-title]");
    const reportMeta = root.querySelector("[data-tender-meta]");
    const reportContainer = root.querySelector("[data-tender-report-container]");
    const exportPdfBtn = root.querySelector("[data-export-tender-pdf]");
    const exportExcelBtn = root.querySelector("[data-export-tender-excel]");
    const backNote = root.querySelector("[data-tender-back-note]");
    const positionText = root.querySelector("[data-tender-position]");
    const prevBtn = root.querySelector("[data-tender-prev]");
    const nextBtn = root.querySelector("[data-tender-next]");
    const offerCount = root.querySelector("[data-offer-count]");
    const itemCount = root.querySelector("[data-item-count]");
    const panels = Object.fromEntries(Array.from(root.querySelectorAll("[data-detail-panel]")).map(p => [p.dataset.detailPanel, p]));
    const detailTabs = Array.from(root.querySelectorAll("[data-detail-tab]"));
    let currentTenderId = null;

    function selectDetailTab(name) {
        if (!panels[name]) name = "report";
        for (const tab of detailTabs) {
            const on = tab.dataset.detailTab === name;
            tab.setAttribute("aria-selected", String(on));
            tab.tabIndex = on ? 0 : -1;
        }
        Object.entries(panels).forEach(([key, panel]) => { panel.hidden = key !== name; });
        store.set("detailTab", name);
    }
    detailTabs.forEach(tab => tab.addEventListener("click", () => selectDetailTab(tab.dataset.detailTab)));
    arrowKeys(detailTabs, tab => selectDetailTab(tab.dataset.detailTab));
    selectDetailTab(store.get("detailTab") || "report");

    const stageLabels = ["Published", "Closed", "Committee evaluation", "Manager review", "CEO & Chiefs", "Awarded and notified"];
    const statusTagFor = (tenderId, status) => {
        const tag = root.querySelector(`[data-tender-row="${CSS.escape(String(tenderId))}"] .ai-intel-tag`);
        return tag ? tag.outerHTML : `<span class="ai-intel-tag">${esc(status)}</span>`;
    };
    // "2026-11-11 13:13" (UTC) -> "11 Nov 2026"
    const fmtDay = (utc) => {
        const d = new Date(String(utc || "").replace(" ", "T") + "Z");
        return Number.isNaN(d.getTime()) ? String(utc || "") : d.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });
    };

    function renderTenderReport(report) {
        currentTenderId = report.tenderId;
        const base = `/AiIntelligence/ExportTender?tenderId=${encodeURIComponent(report.tenderId)}&workspace=${encodeURIComponent(workspace)}`;
        if (exportPdfBtn) exportPdfBtn.href = `${base}&format=pdf`;
        if (exportExcelBtn) exportExcelBtn.href = `${base}&format=excel`;

        reportHeading.innerHTML = `<span>#${report.tenderId} ${esc(report.title)}</span> ${statusTagFor(report.tenderId, report.status)}`;
        reportMeta.textContent = `${report.department} · Request #${report.purchaseRequestId} · Round ${report.round}`;
        document.title = `#${report.tenderId} ${report.title} · AI Intelligence`;

        // --- Report: where it is, key facts, scope ---
        const stepsHtml = stageLabels.map((lbl, idx) => {
            const n = idx + 1;
            const cls = n < report.lifecycleStageIndex || report.status === "Awarded" ? "is-done" : n === report.lifecycleStageIndex ? "is-current" : "";
            return `<li class="ai-intel-step ${cls}"${cls === "is-current" ? ' aria-current="step"' : ""}>${esc(lbl)}</li>`;
        }).join("");
        const weightsHtml = report.financialWeight != null
            ? `<dd><div class="ai-intel-weights" role="img" aria-label="${report.financialWeight}% price, ${report.technicalWeight}% technical">
                    <span class="is-price" style="width:${report.financialWeight}%"></span><span class="is-tech" style="width:${report.technicalWeight}%"></span>
               </div></dd>
               <small><span class="ai-intel-dot is-price"></span>Price ${report.financialWeight}% <span class="ai-intel-dot is-tech"></span>Technical ${report.technicalWeight}%</small>`
            : `<dd>Not agreed yet</dd><small>The committee sets these before scoring</small>`;
        const offers = Array.isArray(report.offers) ? report.offers : [];
        const recommended = offers.find(o => o.isAwardedWinner) || offers.find(o => o.isRecommended);
        const lowest = offers.find(o => o.isLowestPrice);
        panels.report.innerHTML = `
            <ol class="ai-intel-stepper" aria-label="Tender lifecycle">${stepsHtml}</ol>
            <dl class="ai-intel-facts">
                <div class="ai-intel-fact"><dt>Duration</dt><dd>${esc(report.lifecycleFormatted)}</dd></div>
                <div class="ai-intel-fact">
                    <dt>Timeline</dt>
                    <dd>${esc(fmtDay(report.createdUtc))} <span aria-label="to">→</span> ${esc(fmtDay(report.closesUtc))}</dd>
                    <small>Created, then closes${report.awardedUtc ? `. Awarded ${esc(fmtDay(report.awardedUtc))}` : ""}</small>
                </div>
                <div class="ai-intel-fact"><dt>Scoring weights</dt>${weightsHtml}</div>
                ${report.canViewSupplierOffers ? `
                <div class="ai-intel-fact">
                    <dt>Offers</dt>
                    <dd>${offers.length}</dd>
                    <small>${recommended ? `${recommended.isAwardedWinner ? "Awarded" : "Recommended"}: ${esc(recommended.supplierName)}` : lowest ? `Lowest: ${esc(lowest.supplierName)}, ${esc(fmtMoney(lowest.total, lowest.currency))}` : "None submitted yet"}</small>
                </div>` : ""}
                ${report.topCommitteeApprovals > 0 ? `<div class="ai-intel-fact"><dt>CEO &amp; Chiefs approvals</dt><dd>${report.topCommitteeApprovals} of 4</dd></div>` : ""}
            </dl>
            <div class="ai-td-scope">
                <h3 class="ai-intel-subhead">Scope</h3>
                <p>${esc(report.description)}</p>
                ${report.terms ? `<h3 class="ai-intel-subhead">Terms</h3><p>${esc(report.terms)}</p>` : ""}
                ${report.winnerNotifiedUtc ? `<p class="ai-intel-muted">Winner notified ${esc(report.winnerNotifiedUtc)} UTC</p>` : ""}
            </div>`;

        // --- Offer comparison ---
        if (offerCount) offerCount.textContent = report.canViewSupplierOffers ? String(offers.length) : "";
        if (!report.canViewSupplierOffers) {
            panels.offers.innerHTML = `<p class="ai-intel-note">Supplier offers and prices are hidden in the Department workspace to keep competing quotations confidential.</p>`;
        } else if (offers.length === 0) {
            panels.offers.innerHTML = `<p class="ai-intel-note">No supplier offers have been submitted for this tender yet.</p>`;
        } else {
            const maxTotal = Math.max(1, ...offers.map(o => Number(o.total || 0)));
            const tone = o => o.isAwardedWinner || o.isRecommended ? "win" : o.complianceStatus === "Compliant" ? "ok" : "warn";
            const bars = offers.map(o => {
                const pct = Math.max(6, Math.round((Number(o.total || 0) / maxTotal) * 100));
                const badges = [
                    o.isAwardedWinner ? `<span class="ai-intel-tag" data-tone="done">Awarded</span>` : "",
                    o.isRecommended && !o.isAwardedWinner ? `<span class="ai-intel-tag" data-tone="done">Recommended</span>` : "",
                    o.isLowestPrice ? `<span class="ai-intel-tag" data-tone="open">Lowest price</span>` : "",
                    o.isFastestDelivery ? `<span class="ai-intel-tag" data-tone="open">Fastest, ${o.deliveryDays} days</span>` : ""
                ].filter(Boolean).join(" ");
                const scoreText = o.averageScore != null ? ` · Score ${Number(o.averageScore).toFixed(2)}/100` : "";
                return `
                    <div class="ai-intel-bar-row" data-offer-tone="${tone(o)}">
                        <div class="ai-intel-bar-top">
                            <span class="ai-intel-bar-label" title="${esc(o.supplierName)}">#${o.bidId} ${esc(o.supplierName)} ${badges}</span>
                            <span class="ai-intel-bar-meta"><strong>${esc(fmtMoney(o.total, o.currency))}</strong> · ${o.deliveryDays}d · ${esc(o.complianceStatus)}${esc(scoreText)}</span>
                        </div>
                        <div class="ai-intel-bar-track"><div class="ai-intel-bar-fill" style="width:${pct}%"></div></div>
                    </div>`;
            }).join("");
            const rows = offers.map(o => `
                <tr>
                    <td><strong>#${o.bidId}</strong></td>
                    <td>${esc(o.supplierName)}</td>
                    <td><span class="ai-intel-tag" data-tone="${o.complianceStatus === "Compliant" ? "done" : "waiting"}">${esc(o.complianceStatus)}</span></td>
                    <td class="ai-num">${esc(fmtMoney(o.subtotal, o.currency))}</td>
                    <td class="ai-num">${esc(fmtMoney(o.taxAmount, o.currency))}</td>
                    <td class="ai-num">${esc(fmtMoney(o.additionalCosts, o.currency))}</td>
                    <td class="ai-num"><strong>${esc(fmtMoney(o.total, o.currency))}</strong></td>
                    <td class="ai-nowrap">${o.deliveryDays} days</td>
                    <td>${o.averageScore != null ? `${Number(o.averageScore).toFixed(2)} / 100` : "–"}</td>
                    <td>${o.isAwardedWinner ? `<span class="ai-intel-tag" data-tone="done">Awarded</span>` : o.isRecommended ? `<span class="ai-intel-tag" data-tone="done">Recommended</span>` : esc(o.status)}</td>
                </tr>`).join("");
            panels.offers.innerHTML = `
                <div class="ai-offer-key" aria-hidden="true">
                    <span><span class="ai-intel-dot" data-offer-tone="win"></span>Recommended or awarded</span>
                    <span><span class="ai-intel-dot" data-offer-tone="ok"></span>Compliant</span>
                    <span><span class="ai-intel-dot" data-offer-tone="warn"></span>Not compliant</span>
                </div>
                <div class="ai-intel-bars ai-offer-bars">${bars}</div>
                <div class="ai-intel-table-wrap">
                    <table class="ai-intel-table">
                        <thead><tr>
                            <th scope="col">Offer</th><th scope="col">Supplier</th><th scope="col">Compliance</th><th scope="col">Subtotal</th><th scope="col">Tax</th>
                            <th scope="col">Extra costs</th><th scope="col">Total</th><th scope="col">Delivery</th><th scope="col">Committee average</th><th scope="col">Outcome</th>
                        </tr></thead>
                        <tbody>${rows}</tbody>
                    </table>
                </div>`;
        }

        // --- Requested items ---
        const items = report.requestedItems || [];
        if (itemCount) itemCount.textContent = String(items.length);
        panels.items.innerHTML = `
            <div class="ai-intel-table-wrap">
                <table class="ai-intel-table">
                    <thead><tr><th scope="col">Item</th><th scope="col">Name</th><th scope="col">Quantity</th><th scope="col">Specifications</th></tr></thead>
                    <tbody>${items.map(i => `
                        <tr>
                            <td>#${i.itemId}</td>
                            <td><strong>${esc(i.itemName)}</strong></td>
                            <td class="ai-nowrap">${i.quantity} ${esc(i.unit)}</td>
                            <td class="ai-spec">${esc(i.specifications)}</td>
                        </tr>`).join("") || `<tr><td colspan="4" class="ai-intel-empty-cell">No items recorded.</td></tr>`}</tbody>
                </table>
            </div>`;
    }

    // Previous / next follow the list as it is currently filtered.
    function updateStepper(id) {
        const visible = tenderRows.filter(tr => tr.dataset.match !== "0");
        const idx = visible.findIndex(tr => tr.dataset.tenderRow === String(id));
        const filtered = visible.length !== tenderRows.length;
        if (backNote) backNote.textContent = filtered ? `(filtered: ${visible.length} of ${tenderRows.length})` : "";
        if (positionText) positionText.textContent = idx >= 0 ? `${idx + 1} of ${visible.length}` : "";
        if (prevBtn) { prevBtn.disabled = idx <= 0; prevBtn.dataset.target = idx > 0 ? visible[idx - 1].dataset.tenderRow : ""; }
        if (nextBtn) { nextBtn.disabled = idx < 0 || idx >= visible.length - 1; nextBtn.dataset.target = idx >= 0 && idx < visible.length - 1 ? visible[idx + 1].dataset.tenderRow : ""; }
    }
    [prevBtn, nextBtn].forEach(btn => btn?.addEventListener("click", () => { if (btn.dataset.target) go(`#tenders/${btn.dataset.target}`); }));

    let initialReport = null;
    try { initialReport = JSON.parse(root.dataset.initialTender || "null"); } catch { /* reports load on demand */ }
    const reportCache = new Map();
    if (initialReport) reportCache.set(initialReport.tenderId, initialReport);

    let reportRequest = 0;
    async function openTenderDetail(tenderId) {
        if (listPane && !listPane.hidden) listScroll = window.scrollY;
        if (listPane) listPane.hidden = true;
        if (detailPane) detailPane.hidden = false;
        lastOpenedId = tenderId;
        updateStepper(tenderId);
        if (currentTenderId === tenderId && !reportContainer.hasAttribute("data-failed")) { reportHeading?.focus({ preventScroll: true }); return; }

        const requestId = ++reportRequest;
        if (tenderAiBox) { tenderAiBox.hidden = true; tenderAiBox.innerHTML = ""; }
        window.scrollTo({ top: Math.min(window.scrollY, root.querySelector(".ai-ws-tabs-wrap")?.offsetTop ?? 0), behavior: "auto" });

        const cached = reportCache.get(tenderId);
        if (cached) {
            reportContainer.removeAttribute("data-failed");
            renderTenderReport(cached);
            hideStatus(tenderStatus);
            reportHeading?.focus({ preventScroll: true });
            return;
        }
        reportHeading.textContent = `Tender #${tenderId}`;
        reportMeta.textContent = "";
        showStatus(tenderStatus, `Loading the report for tender #${tenderId}…`);
        reportContainer.setAttribute("aria-busy", "true");
        try {
            const resp = await fetch(`/AiIntelligence/TenderReport?tenderId=${encodeURIComponent(tenderId)}&workspace=${encodeURIComponent(workspace)}`);
            const data = await resp.json().catch(() => null);
            if (requestId !== reportRequest) return; // a newer selection won
            if (!resp.ok || !data) throw new Error(data?.error || "The report could not be loaded. Try again.");
            reportCache.set(tenderId, data);
            reportContainer.removeAttribute("data-failed");
            renderTenderReport(data);
            hideStatus(tenderStatus);
            reportHeading?.focus({ preventScroll: true });
        } catch (err) {
            if (requestId !== reportRequest) return;
            currentTenderId = null;
            reportContainer.setAttribute("data-failed", "");
            Object.values(panels).forEach(p => { p.innerHTML = ""; });
            showStatus(tenderStatus, err.message || "The report could not be loaded. Try again.", true);
        } finally {
            if (requestId === reportRequest) reportContainer.setAttribute("aria-busy", "false");
        }
    }

    // Per-tender AI recommendation, shown above the detail tabs.
    const analyzeTenderBtn = root.querySelector("[data-analyze-tender]");
    analyzeTenderBtn?.addEventListener("click", async () => {
        const tid = currentTenderId;
        if (!tid) return;
        setBusy(analyzeTenderBtn, true);
        showStatus(tenderStatus, `Preparing an AI recommendation for tender #${tid}. This can take up to a minute…`);
        if (tenderAiBox) { tenderAiBox.innerHTML = skeletonMarkup; tenderAiBox.hidden = false; }
        try {
            const resp = await fetch("/AiIntelligence/TenderInsights", {
                method: "POST",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken },
                body: JSON.stringify({ tenderId: tid, workspace })
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) throw new Error(data?.error || "The AI recommendation could not be generated. Try again in a moment.");
            if (currentTenderId !== tid) return;
            if (tenderAiBox) {
                tenderAiBox.innerHTML = buildInsightsMarkup(data, `AI recommendation for tender #${tid}`);
                tenderAiBox.hidden = false;
                tenderAiBox.focus({ preventScroll: true });
            }
            hideStatus(tenderStatus);
        } catch (err) {
            if (tenderAiBox) { tenderAiBox.hidden = true; tenderAiBox.innerHTML = ""; }
            showStatus(tenderStatus, err.message, true);
        } finally {
            setBusy(analyzeTenderBtn, false);
        }
    });

    /* =====================================================================
       5. Suppliers: search and sort
       ===================================================================== */
    const supplierTable = root.querySelector("[data-suppliers-table] tbody");
    const supplierSearch = root.querySelector("[data-supplier-search]");
    const supplierSort = root.querySelector("[data-supplier-sort]");
    const supplierCount = root.querySelector("[data-supplier-count]");
    const supplierEmpty = root.querySelector("[data-supplier-empty]");
    const supplierRows = Array.from(root.querySelectorAll("[data-suppliers-table] tbody tr[data-supplier-row]"));
    const supplierSearchText = new Map(supplierRows.map(tr => [tr, searchTextOf(tr)]));
    const supplierPager = makePager(root.querySelector("[data-suppliers-table]"), supplierRows,
        { label: "Supplier pages", storeKey: "supplierPage", noun: ["supplier", "suppliers"] });

    function filterSuppliers() {
        const q = (supplierSearch?.value || "").trim().toLowerCase();
        let shown = 0;
        for (const tr of supplierRows) {
            const match = !q || supplierSearchText.get(tr).includes(q);
            tr.dataset.match = match ? "1" : "0";
            if (match) shown++;
        }
        supplierPager.setPage(1);
        if (supplierEmpty) supplierEmpty.hidden = shown > 0 || supplierRows.length === 0;
        if (supplierCount) {
            supplierCount.textContent = q
                ? `Showing ${shown} of ${plural(supplierRows.length, "supplier", "suppliers")}`
                : plural(supplierRows.length, "supplier", "suppliers");
        }
    }
    function sortSuppliers() {
        const key = supplierSort?.value || "offers";
        const num = (tr, k) => Number(tr.dataset[k] || 0);
        const sorted = [...supplierRows].sort((a, b) => key === "name"
            ? a.dataset.name.localeCompare(b.dataset.name)
            : num(b, key) - num(a, key) || a.dataset.name.localeCompare(b.dataset.name));
        sorted.forEach(tr => supplierTable.insertBefore(tr, supplierEmpty || null));
        store.set("supplierSort", key);
    }
    supplierSearch?.addEventListener("input", filterSuppliers);
    supplierSort?.addEventListener("change", () => { sortSuppliers(); supplierPager.setPage(1); });
    if (supplierSort) {
        const saved = store.get("supplierSort");
        if (saved && Array.from(supplierSort.options).some(o => o.value === saved)) supplierSort.value = saved;
        sortSuppliers();
    }
    supplierPager.render();

    /* =====================================================================
       6. Start on the view in the URL (Overview by default)
       ===================================================================== */
    route({ initial: true });
})();
