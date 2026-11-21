(() => {
    const root = document.querySelector("[data-ai-intelligence]");
    if (!root) return;

    const workspace = root.dataset.workspace || "";
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const scrollBehavior = reduceMotion ? "auto" : "smooth";
    const csrfToken = root.querySelector("input[name='__RequestVerificationToken']")?.value || "";

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
        if (isError) el.dataset.error = "true";
        else delete el.dataset.error;
    }

    function hideStatus(el) {
        if (el) el.hidden = true;
    }

    // 1. Status donut
    const donutSvg = root.querySelector("[data-status-donut]");
    if (donutSvg) {
        try {
            const segments = JSON.parse(root.dataset.statusChart || "[]");
            const total = segments.reduce((acc, s) => acc + Number(s.value || 0), 0);
            if (total > 0) {
                const r = 44;
                const c = 2 * Math.PI * r;
                let offset = 0;
                const ns = "http://www.w3.org/2000/svg";

                const bg = document.createElementNS(ns, "circle");
                bg.setAttribute("cx", "60");
                bg.setAttribute("cy", "60");
                bg.setAttribute("r", String(r));
                bg.setAttribute("fill", "none");
                bg.setAttribute("stroke", "rgba(120,140,150,0.18)");
                bg.setAttribute("stroke-width", "18");
                donutSvg.append(bg);

                for (const seg of segments) {
                    const val = Number(seg.value || 0);
                    if (val <= 0) continue;
                    const len = (val / total) * c;
                    const circle = document.createElementNS(ns, "circle");
                    circle.setAttribute("cx", "60");
                    circle.setAttribute("cy", "60");
                    circle.setAttribute("r", String(r));
                    circle.setAttribute("fill", "none");
                    circle.setAttribute("stroke", seg.color || "#1d5560");
                    circle.setAttribute("stroke-width", "18");
                    circle.setAttribute("stroke-dasharray", `${len} ${c - len}`);
                    circle.setAttribute("stroke-dashoffset", String(-offset));
                    circle.setAttribute("transform", "rotate(-90 60 60)");
                    const title = document.createElementNS(ns, "title");
                    title.textContent = `${seg.label}: ${seg.valueFormatted} (${seg.secondaryFormatted})`;
                    circle.append(title);
                    donutSvg.append(circle);
                    offset += len;
                }

                const centerText = document.createElementNS(ns, "text");
                centerText.setAttribute("x", "60");
                centerText.setAttribute("y", "65");
                centerText.setAttribute("text-anchor", "middle");
                centerText.setAttribute("font-size", "18");
                centerText.setAttribute("font-weight", "800");
                centerText.setAttribute("fill", "currentColor");
                centerText.textContent = String(total);
                donutSvg.append(centerText);
                donutSvg.setAttribute("aria-label", `${plural(total, "tender", "tenders")} by status`);
            }
        } catch { /* chart stays empty; the legend still lists every status */ }
    }

    // 2. In-page navigation: mark the section in view
    const navLinks = Array.from(root.querySelectorAll("[data-section-nav] a[href^='#']"));
    const navTargets = navLinks
        .map(a => ({ link: a, section: document.getElementById(a.getAttribute("href").slice(1)) }))
        .filter(x => x.section);

    function markNav(id) {
        for (const { link, section } of navTargets) {
            if (section.id === id) link.setAttribute("aria-current", "location");
            else link.removeAttribute("aria-current");
        }
    }

    // After a nav click, keep that link current until the scroll it started has settled.
    let navLockId = null;
    let navLockTimer = 0;
    const releaseNavLock = () => { navLockId = null; clearTimeout(navLockTimer); };

    const atPageBottom = () =>
        window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2;

    if ("IntersectionObserver" in window && navTargets.length > 0) {
        const visible = new Map();
        const observer = new IntersectionObserver((entries) => {
            for (const entry of entries) visible.set(entry.target.id, entry.isIntersecting);
            if (navLockId || atPageBottom()) return;
            const current = navTargets.find(x => visible.get(x.section.id));
            if (current) markNav(current.section.id);
        }, { rootMargin: "-20% 0px -65% 0px" });
        navTargets.forEach(x => observer.observe(x.section));

        // The last section is short and may never reach the band above; at the bottom of the page it is current.
        let frame = 0;
        window.addEventListener("scroll", () => {
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = 0;
                if (!navLockId && atPageBottom()) markNav(navTargets[navTargets.length - 1].section.id);
            });
        }, { passive: true });
    }

    window.addEventListener("scrollend", () => { if (navLockId) releaseNavLock(); });

    navLinks.forEach(link => link.addEventListener("click", () => {
        const id = link.getAttribute("href").slice(1);
        markNav(id);
        navLockId = id;
        clearTimeout(navLockTimer);
        navLockTimer = setTimeout(releaseNavLock, 1500); // fallback where scrollend is unsupported
    }));

    // 3. Tender report
    const tenderContainer = root.querySelector("[data-tender-report-container]");
    const tenderSelect = root.querySelector("[data-tender-select]");
    const tenderStatus = root.querySelector("[data-tender-status]");
    const tenderAiBox = root.querySelector("[data-tender-ai-box]");
    const reportHeading = root.querySelector("#tender-report-heading");
    const exportPdfBtn = root.querySelector("[data-export-tender-pdf]");
    const exportExcelBtn = root.querySelector("[data-export-tender-excel]");

    const stageLabels = [
        "Published",
        "Closed",
        "Committee evaluation",
        "Manager review",
        "CEO & Chiefs",
        "Awarded and notified"
    ];

    // The table already shows each status with its readable label; reuse it so labels stay in one place.
    const statusLabelFor = (tenderId, fallback) =>
        root.querySelector(`[data-tender-row="${CSS.escape(String(tenderId))}"] .ai-intel-tag`)?.textContent.trim() || fallback;

    function renderTenderReport(report) {
        if (!tenderContainer) return;
        if (!report) {
            tenderContainer.innerHTML = `<p class="ai-intel-status-msg">Select a tender to see its report.</p>`;
            return;
        }

        if (exportPdfBtn) {
            exportPdfBtn.href = `/AiIntelligence/ExportTender?tenderId=${encodeURIComponent(report.tenderId)}&workspace=${encodeURIComponent(workspace)}&format=pdf`;
        }
        if (exportExcelBtn) {
            exportExcelBtn.href = `/AiIntelligence/ExportTender?tenderId=${encodeURIComponent(report.tenderId)}&workspace=${encodeURIComponent(workspace)}&format=excel`;
        }

        const stepsHtml = stageLabels.map((lbl, idx) => {
            const stepNum = idx + 1;
            const cls = stepNum < report.lifecycleStageIndex || report.status === "Awarded"
                ? "is-done"
                : stepNum === report.lifecycleStageIndex
                    ? "is-current"
                    : "";
            return `<li class="ai-intel-step ${cls}"${cls === "is-current" ? ' aria-current="step"' : ""}>${esc(lbl)}</li>`;
        }).join("");

        const weightsBadge = report.financialWeight != null
            ? `<span class="ai-intel-tag">Weights: ${report.financialWeight}% financial, ${report.technicalWeight}% technical</span>`
            : `<span class="ai-intel-tag">Weights: not yet agreed by the committee</span>`;

        let offersVisualHtml = "";
        if (!report.canViewSupplierOffers) {
            offersVisualHtml = `<p class="ai-intel-note">Supplier offers and prices are hidden in the Department workspace to keep competing quotations confidential.</p>`;
        } else if (!Array.isArray(report.offers) || report.offers.length === 0) {
            offersVisualHtml = `<p class="ai-intel-note">No supplier offers have been submitted for this tender yet.</p>`;
        } else {
            const maxOfferTotal = Math.max(1, ...report.offers.map(o => Number(o.total || 0)));
            const bars = report.offers.map(o => {
                const pct = Math.max(6, Math.round((Number(o.total || 0) / maxOfferTotal) * 100));
                const badges = [
                    o.isAwardedWinner ? `<span class="ai-intel-tag is-win">Awarded</span>` : "",
                    o.isRecommended && !o.isAwardedWinner ? `<span class="ai-intel-tag is-win">Recommended</span>` : "",
                    o.isLowestPrice ? `<span class="ai-intel-tag">Lowest price</span>` : "",
                    o.isFastestDelivery ? `<span class="ai-intel-tag">Fastest, ${o.deliveryDays} days</span>` : ""
                ].filter(Boolean).join(" ");

                const scoreText = o.averageScore != null ? ` · Score ${Number(o.averageScore).toFixed(2)}/100` : "";
                const barColor = o.isAwardedWinner || o.isRecommended ? "#185735" : (o.complianceStatus === "Compliant" ? "#1d5560" : "#c98b2e");

                return `
                    <div class="ai-intel-bar-row">
                        <div class="ai-intel-bar-top">
                            <span class="ai-intel-bar-label" title="${esc(o.supplierName)}">#${o.bidId} ${esc(o.supplierName)} ${badges}</span>
                            <span class="ai-intel-bar-meta"><strong>${esc(fmtMoney(o.total, o.currency))}</strong> · ${o.deliveryDays}d · ${esc(o.complianceStatus)}${esc(scoreText)}</span>
                        </div>
                        <div class="ai-intel-bar-track">
                            <div class="ai-intel-bar-fill" style="width:${pct}%;background:${barColor}"></div>
                        </div>
                    </div>`;
            }).join("");

            const offerRowsHtml = report.offers.map(o => `
                <tr>
                    <td><strong>#${o.bidId}</strong></td>
                    <td>${esc(o.supplierName)}</td>
                    <td><span class="ai-intel-tag ${o.complianceStatus === "Compliant" ? "is-win" : ""}">${esc(o.complianceStatus)}</span></td>
                    <td>${esc(fmtMoney(o.subtotal, o.currency))}</td>
                    <td>${esc(fmtMoney(o.taxAmount, o.currency))}</td>
                    <td>${esc(fmtMoney(o.additionalCosts, o.currency))}</td>
                    <td><strong>${esc(fmtMoney(o.total, o.currency))}</strong></td>
                    <td>${o.deliveryDays} days</td>
                    <td>${o.averageScore != null ? `${Number(o.averageScore).toFixed(2)} / 100` : "–"}</td>
                    <td>${o.isAwardedWinner ? `<span class="ai-intel-tag is-win">Awarded</span>` : o.isRecommended ? `<span class="ai-intel-tag is-win">Recommended</span>` : esc(o.status)}</td>
                </tr>`).join("");

            offersVisualHtml = `
                <div class="ai-intel-chart-card is-spaced">
                    <h4>Offer comparison: price, delivery and score</h4>
                    <div class="ai-intel-bars">${bars}</div>
                </div>
                <div class="ai-intel-table-wrap is-spaced">
                    <table class="ai-intel-table">
                        <thead>
                            <tr>
                                <th scope="col">Offer</th><th scope="col">Supplier</th><th scope="col">Compliance</th><th scope="col">Subtotal</th><th scope="col">Tax</th><th scope="col">Extra costs</th><th scope="col">Total</th><th scope="col">Delivery</th><th scope="col">Committee average</th><th scope="col">Outcome</th>
                            </tr>
                        </thead>
                        <tbody>${offerRowsHtml}</tbody>
                    </table>
                </div>`;
        }

        const itemRowsHtml = (report.requestedItems || []).map(i => `
            <tr>
                <td>#${i.itemId}</td>
                <td><strong>${esc(i.itemName)}</strong></td>
                <td>${i.quantity} ${esc(i.unit)}</td>
                <td>${esc(i.specifications)}</td>
            </tr>`).join("");

        tenderContainer.innerHTML = `
            <div class="ai-intel-report-title">
                <span class="ai-intel-report-name">#${report.tenderId} ${esc(report.title)}</span>
                <span class="ai-intel-tag ${report.status === "Awarded" ? "is-win" : ""}">${esc(statusLabelFor(report.tenderId, report.status))}</span>
            </div>
            <div class="ai-intel-badges">
                <span class="ai-intel-tag">Request #${report.purchaseRequestId} (${esc(report.department)})</span>
                <span class="ai-intel-tag">Round ${report.round}</span>
                <span class="ai-intel-tag">Duration: ${esc(report.lifecycleFormatted)}</span>
                ${weightsBadge}
                ${report.topCommitteeApprovals > 0 ? `<span class="ai-intel-tag is-win">CEO &amp; Chiefs approvals: ${report.topCommitteeApprovals} of 4</span>` : ""}
            </div>
            <p class="ai-intel-section-sub"><strong>Scope:</strong> ${esc(report.description)}${report.terms ? ` <strong>Terms:</strong> ${esc(report.terms)}` : ""}</p>
            <p class="ai-intel-section-sub">
                Created ${esc(report.createdUtc)} UTC · Closes ${esc(report.closesUtc)} UTC${report.awardedUtc ? ` · Awarded ${esc(report.awardedUtc)} UTC` : ""}${report.winnerNotifiedUtc ? ` · Winner notified ${esc(report.winnerNotifiedUtc)} UTC` : ""}
            </p>

            <ol class="ai-intel-stepper" aria-label="Tender lifecycle">${stepsHtml}</ol>

            ${offersVisualHtml}

            <h4 class="ai-intel-subhead">Requested items</h4>
            <div class="ai-intel-table-wrap">
                <table class="ai-intel-table">
                    <thead><tr><th scope="col">Item</th><th scope="col">Name</th><th scope="col">Quantity</th><th scope="col">Specifications</th></tr></thead>
                    <tbody>${itemRowsHtml || `<tr><td colspan="4" class="ai-intel-empty-cell">No items recorded.</td></tr>`}</tbody>
                </table>
            </div>
        `;
    }

    try {
        const initial = JSON.parse(root.dataset.initialTender || "null");
        if (initial) renderTenderReport(initial);
    } catch { /* the select still lets the user load a report */ }

    function markSelectedRow(tenderId) {
        root.querySelectorAll("[data-tender-row]").forEach(tr => {
            tr.classList.toggle("is-selected", tr.dataset.tenderRow === String(tenderId));
        });
    }

    let reportRequest = 0;
    async function loadTenderById(tenderId, { focusReport = false } = {}) {
        if (!tenderId) return;
        const requestId = ++reportRequest;
        if (tenderSelect) tenderSelect.value = String(tenderId);
        if (tenderAiBox) { tenderAiBox.hidden = true; tenderAiBox.innerHTML = ""; }
        markSelectedRow(tenderId);
        showStatus(tenderStatus, `Loading the report for tender #${tenderId}…`);
        tenderContainer?.setAttribute("aria-busy", "true");

        try {
            const resp = await fetch(`/AiIntelligence/TenderReport?tenderId=${encodeURIComponent(tenderId)}&workspace=${encodeURIComponent(workspace)}`);
            const data = await resp.json().catch(() => null);
            if (requestId !== reportRequest) return; // a newer selection won
            if (!resp.ok || !data) throw new Error(data?.error || "The report could not be loaded. Try again.");
            renderTenderReport(data);
            hideStatus(tenderStatus);
            if (focusReport) reportHeading?.focus({ preventScroll: true });
        } catch (err) {
            if (requestId !== reportRequest) return;
            showStatus(tenderStatus, err.message || "The report could not be loaded. Try again.", true);
        } finally {
            if (requestId === reportRequest) tenderContainer?.setAttribute("aria-busy", "false");
        }
    }

    tenderSelect?.addEventListener("change", () => loadTenderById(tenderSelect.value));

    function openTenderFromTable(id) {
        loadTenderById(id, { focusReport: true });
        document.getElementById("tender-explorer")?.scrollIntoView({ behavior: scrollBehavior, block: "start" });
    }

    root.querySelectorAll("[data-tender-row]").forEach(tr => {
        tr.addEventListener("click", (e) => {
            e.stopPropagation();
            openTenderFromTable(tr.dataset.tenderRow);
        });
    });

    // 4. AI insight cards
    function buildInsightsMarkup(res) {
        const cards = (res.insights || []).map(item => `
            <article class="ai-intel-card" data-tone="${esc(item.tone || "info")}">
                <div class="ai-intel-card-head">
                    <span class="ai-intel-card-cat">${esc(item.category)}</span>
                </div>
                <h4>${esc(item.title)}</h4>
                <p>${esc(item.finding)}</p>
                <div class="ai-intel-rec"><strong>Recommendation:</strong> ${esc(item.recommendation)}</div>
            </article>
        `).join("");

        return `
            <div class="ai-intel-chart-card is-live">
                <div>
                    <span class="ai-intel-card-cat">AI summary</span>
                    <h3>${esc(res.headline)}</h3>
                    <p class="ai-intel-section-sub">${esc(res.executiveBrief)}</p>
                </div>
                <div class="ai-intel-cards-grid">${cards}</div>
            </div>`;
    }

    // Ask AI
    const portfolioForm = root.querySelector("[data-portfolio-form]");
    const portfolioStatus = root.querySelector("[data-portfolio-status]");
    const portfolioOut = root.querySelector("[data-ai-portfolio-output]");
    const portfolioSubmit = root.querySelector("[data-portfolio-submit]");
    const questionInput = portfolioForm?.elements.question;

    async function runPortfolioAi(questionText) {
        setBusy(portfolioSubmit, true);
        showStatus(portfolioStatus, questionText
            ? "Analyzing your data to answer the question. This can take up to a minute…"
            : "Preparing a general brief from your data. This can take up to a minute…");
        try {
            const resp = await fetch("/AiIntelligence/PortfolioInsights", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": csrfToken
                },
                body: JSON.stringify({ workspace, question: questionText || null })
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) throw new Error(data?.error || "The AI brief could not be generated. Try again in a moment.");
            if (portfolioOut) {
                portfolioOut.innerHTML = buildInsightsMarkup(data);
                portfolioOut.hidden = false;
                portfolioOut.focus({ preventScroll: true });
                portfolioOut.scrollIntoView({ behavior: scrollBehavior, block: "nearest" });
            }
            hideStatus(portfolioStatus);
        } catch (err) {
            showStatus(portfolioStatus, err.message, true);
        } finally {
            setBusy(portfolioSubmit, false);
        }
    }

    portfolioForm?.addEventListener("submit", (e) => {
        e.preventDefault();
        if (portfolioSubmit?.disabled) return;
        runPortfolioAi(questionInput?.value?.trim() || "");
    });

    // Suggested questions only fill the box; nothing is sent until the user submits.
    root.querySelectorAll("[data-suggestion]").forEach(chip => {
        chip.addEventListener("click", () => {
            if (!questionInput) return;
            questionInput.value = chip.dataset.suggestion || "";
            questionInput.focus();
        });
    });

    // Per-tender AI recommendation
    const analyzeTenderBtn = root.querySelector("[data-analyze-tender]");
    analyzeTenderBtn?.addEventListener("click", async () => {
        const tid = Number(tenderSelect?.value || 0);
        if (!tid) return;
        setBusy(analyzeTenderBtn, true);
        showStatus(tenderStatus, `Preparing an AI recommendation for tender #${tid}. This can take up to a minute…`);
        try {
            const resp = await fetch("/AiIntelligence/TenderInsights", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": csrfToken
                },
                body: JSON.stringify({ tenderId: tid, workspace })
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) throw new Error(data?.error || "The AI recommendation could not be generated. Try again in a moment.");
            if (tenderAiBox) {
                tenderAiBox.innerHTML = buildInsightsMarkup(data);
                tenderAiBox.hidden = false;
                tenderAiBox.focus({ preventScroll: true });
            }
            hideStatus(tenderStatus);
        } catch (err) {
            showStatus(tenderStatus, err.message, true);
        } finally {
            setBusy(analyzeTenderBtn, false);
        }
    });

    // 5. Document upload
    const uploadForm = root.querySelector("[data-upload-form]");
    const uploadSubmit = root.querySelector("[data-upload-submit]");
    const uploadStatus = root.querySelector("[data-upload-status]");
    const uploadResults = root.querySelector("[data-upload-results]");
    const fileInput = uploadForm?.querySelector("[data-upload-file]");
    const fileLabel = uploadForm?.querySelector("[data-upload-filename]");
    const fileLabelDefault = fileLabel?.textContent || "";

    function validateFile(file) {
        if (!file) return "Choose an Excel (.xlsx) or PDF (.pdf) file first.";
        const name = file.name.toLowerCase();
        if (!UPLOAD_EXTENSIONS.some(ext => name.endsWith(ext))) {
            return `${file.name} is not supported. Choose an Excel (.xlsx) or PDF (.pdf) file.`;
        }
        if (file.size === 0) return `${file.name} is empty. Choose a file with content.`;
        if (file.size > UPLOAD_MAX_BYTES) {
            return `${file.name} is ${fmtBytes(file.size)}. Choose a file under 10 MB.`;
        }
        return null;
    }

    function refreshFileState() {
        const file = fileInput?.files?.[0];
        const problem = file ? validateFile(file) : null;
        uploadForm?.classList.toggle("has-file", Boolean(file) && !problem);
        uploadForm?.classList.toggle("has-error", Boolean(problem));
        if (fileLabel) fileLabel.textContent = file ? `${file.name} (${fmtBytes(file.size)})` : fileLabelDefault;
        if (uploadSubmit) uploadSubmit.disabled = !file || Boolean(problem);
        if (problem) showStatus(uploadStatus, problem, true);
        else hideStatus(uploadStatus);
    }

    fileInput?.addEventListener("change", refreshFileState);

    uploadForm?.addEventListener("dragover", (e) => {
        e.preventDefault();
        uploadForm.classList.add("is-dragover");
    });
    uploadForm?.addEventListener("dragleave", (e) => {
        if (!uploadForm.contains(e.relatedTarget)) uploadForm.classList.remove("is-dragover");
    });
    uploadForm?.addEventListener("drop", (e) => {
        e.preventDefault();
        uploadForm.classList.remove("is-dragover");
        if (e.dataTransfer?.files?.length && fileInput) {
            fileInput.files = e.dataTransfer.files;
            refreshFileState();
        }
    });

    uploadForm?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const file = fileInput?.files?.[0];
        const problem = validateFile(file);
        if (problem) {
            showStatus(uploadStatus, problem, true);
            fileInput?.focus();
            return;
        }

        const fd = new FormData(uploadForm);
        fd.set("workspace", workspace);

        setBusy(uploadSubmit, true);
        showStatus(uploadStatus, `Reading ${file.name} and preparing the analysis. Larger files can take a minute…`);

        try {
            const resp = await fetch("/AiIntelligence/AnalyzeUpload", {
                method: "POST",
                headers: { "RequestVerificationToken": csrfToken },
                body: fd
            });
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
                <a class="ai-intel-btn is-primary" href="${esc(dl.url)}" download="${esc(dl.fileName)}">${esc(dl.label || dl.fileName)}</a>`).join("");

            const insightsHtml = (data.insights || []).map(item => `
                <article class="ai-intel-card" data-tone="${esc(item.tone || "info")}">
                    <div class="ai-intel-card-head">
                        <span class="ai-intel-card-cat">${esc(item.category)}</span>
                    </div>
                    <h4>${esc(item.title)}</h4>
                    <p>${esc(item.finding)}</p>
                    <div class="ai-intel-rec"><strong>Recommendation:</strong> ${esc(item.recommendation)}</div>
                </article>`).join("");

            let tableHtml = "";
            if (Array.isArray(data.tableHeaders) && data.tableHeaders.length > 0) {
                const ths = data.tableHeaders.map(h => `<th scope="col">${esc(h)}</th>`).join("");
                const trs = (data.tableRows || []).map(r => `<tr>${r.map(c => `<td>${esc(c)}</td>`).join("")}</tr>`).join("");
                tableHtml = `
                    <h4 class="ai-intel-subhead">Extracted data</h4>
                    <div class="ai-intel-table-wrap">
                        <table class="ai-intel-table">
                            <thead><tr>${ths}</tr></thead>
                            <tbody>${trs}</tbody>
                        </table>
                    </div>`;
            }

            if (uploadResults) {
                uploadResults.innerHTML = `
                    <div class="ai-intel-chart-card is-live">
                        <div class="ai-intel-section-head">
                            <div>
                                <span class="ai-intel-card-cat">${esc(data.fileType)}</span>
                                <h3>${esc(data.fileName)}</h3>
                            </div>
                            <div class="ai-intel-toolbar">${dlHtml}</div>
                        </div>
                        <p class="ai-intel-section-sub">${esc(data.summary)}</p>
                        ${kpiHtml ? `<div class="ai-intel-kpi-grid">${kpiHtml}</div>` : ""}
                        ${insightsHtml ? `<div class="ai-intel-cards-grid">${insightsHtml}</div>` : ""}
                        ${tableHtml}
                    </div>`;
                uploadResults.hidden = false;
                uploadResults.focus({ preventScroll: true });
                uploadResults.scrollIntoView({ behavior: scrollBehavior, block: "nearest" });
            }
            hideStatus(uploadStatus);
        } catch (err) {
            showStatus(uploadStatus, err.message, true);
        } finally {
            setBusy(uploadSubmit, false);
            if (uploadSubmit) uploadSubmit.disabled = Boolean(validateFile(fileInput?.files?.[0]));
        }
    });

    // 6. Table search and filters
    const tenderSearch = root.querySelector("[data-tender-search]");
    const tenderStatusFilter = root.querySelector("[data-tender-status-filter]");
    const tenderClear = root.querySelector("[data-tender-clear]");
    const tenderCount = root.querySelector("[data-tender-count]");
    const tenderEmpty = root.querySelector("[data-tender-empty]");
    const tenderRows = Array.from(root.querySelectorAll("[data-tenders-table] tbody tr[data-tender-row]"));
    const legendButtons = Array.from(root.querySelectorAll("[data-filter-status]"));

    // Search the data cells only, so the "Open" button never matches a search for "open".
    const searchTextOf = (tr) => Array.from(tr.cells)
        .filter(td => !td.querySelector("button"))
        .map(td => td.textContent)
        .join(" ")
        .toLowerCase();
    const tenderSearchText = new Map(tenderRows.map(tr => [tr, searchTextOf(tr)]));

    function filterTendersTable() {
        const q = (tenderSearch?.value || "").trim().toLowerCase();
        const st = tenderStatusFilter?.value || "";
        let shown = 0;
        for (const tr of tenderRows) {
            const matchText = !q || tenderSearchText.get(tr).includes(q);
            const matchStatus = !st || tr.dataset.status === st;
            tr.hidden = !(matchText && matchStatus);
            if (!tr.hidden) shown++;
        }

        const filtered = Boolean(q || st);
        if (tenderEmpty) tenderEmpty.hidden = shown > 0 || tenderRows.length === 0;
        if (tenderClear) tenderClear.hidden = !filtered;
        if (tenderCount) {
            tenderCount.textContent = filtered
                ? `Showing ${shown} of ${plural(tenderRows.length, "tender", "tenders")}`
                : plural(tenderRows.length, "tender", "tenders");
        }
        legendButtons.forEach(btn => btn.setAttribute("aria-pressed", String(Boolean(st) && btn.dataset.filterStatus === st)));
    }

    tenderSearch?.addEventListener("input", filterTendersTable);
    tenderStatusFilter?.addEventListener("change", filterTendersTable);

    tenderClear?.addEventListener("click", () => {
        if (tenderSearch) tenderSearch.value = "";
        if (tenderStatusFilter) tenderStatusFilter.value = "";
        filterTendersTable();
        tenderSearch?.focus();
    });

    legendButtons.forEach(btn => {
        btn.addEventListener("click", () => {
            if (!tenderStatusFilter) return;
            const value = btn.dataset.filterStatus || "";
            const alreadyActive = btn.getAttribute("aria-pressed") === "true";
            const hasOption = Array.from(tenderStatusFilter.options).some(o => o.value === value);
            // Pressing the active status again clears it.
            tenderStatusFilter.value = !alreadyActive && hasOption ? value : "";
            filterTendersTable();
            document.getElementById("tenders")?.scrollIntoView({ behavior: scrollBehavior, block: "start" });
        });
    });

    const supplierSearch = root.querySelector("[data-supplier-search]");
    const supplierCount = root.querySelector("[data-supplier-count]");
    const supplierEmpty = root.querySelector("[data-supplier-empty]");
    const supplierRows = Array.from(root.querySelectorAll("[data-suppliers-table] tbody tr[data-supplier-row]"));
    const supplierSearchText = new Map(supplierRows.map(tr => [tr, searchTextOf(tr)]));
    supplierSearch?.addEventListener("input", () => {
        const q = supplierSearch.value.trim().toLowerCase();
        let shown = 0;
        for (const tr of supplierRows) {
            tr.hidden = Boolean(q) && !supplierSearchText.get(tr).includes(q);
            if (!tr.hidden) shown++;
        }
        if (supplierEmpty) supplierEmpty.hidden = shown > 0 || supplierRows.length === 0;
        if (supplierCount) {
            supplierCount.textContent = q
                ? `Showing ${shown} of ${plural(supplierRows.length, "supplier", "suppliers")}`
                : plural(supplierRows.length, "supplier", "suppliers");
        }
    });
})();
