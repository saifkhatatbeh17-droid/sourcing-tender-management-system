(() => {
    const root = document.querySelector("[data-ai-intelligence]");
    if (!root) return;

    const workspace = root.dataset.workspace || "";
    const csrfToken = root.querySelector("input[name='__RequestVerificationToken']")?.value || "";

    const esc = (val) => {
        const div = document.createElement("div");
        div.textContent = val == null ? "" : String(val);
        return div.innerHTML;
    };

    const fmtMoney = (n, cur) => {
        if (n == null || Number.isNaN(Number(n))) return "N/A";
        return `${Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${cur || "JOD"}`;
    };

    // 1. Render SVG Donut Chart
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
            }
        } catch { /* ignore */ }
    }

    // 2. Render Per-Tender Interactive Visual Analytical Report
    const tenderContainer = root.querySelector("[data-tender-report-container]");
    const tenderSelect = root.querySelector("[data-tender-select]");
    const tenderStatus = root.querySelector("[data-tender-status]");
    const tenderAiBox = root.querySelector("[data-tender-ai-box]");
    const exportPdfBtn = root.querySelector("[data-export-tender-pdf]");
    const exportExcelBtn = root.querySelector("[data-export-tender-excel]");

    const stageLabels = [
        "1. Published",
        "2. Closed",
        "3. Committee Eval",
        "4. Manager Review",
        "5. CEO & Chiefs",
        "6. Awarded & Notified"
    ];

    function renderTenderReport(report) {
        if (!tenderContainer) return;
        if (!report) {
            tenderContainer.innerHTML = `<p class="ai-intel-status-msg">Select a tender to view its visual report.</p>`;
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
            return `<div class="ai-intel-step ${cls}">${esc(lbl)}</div>`;
        }).join("");

        const weightsBadge = report.financialWeight != null
            ? `<span class="ai-intel-tag">Weights: ${report.financialWeight}% Financial / ${report.technicalWeight}% Technical</span>`
            : `<span class="ai-intel-tag">Weights: Pending Committee Lock</span>`;

        // Visual Offer Comparison Bars
        let offersVisualHtml = "";
        if (!report.canViewSupplierOffers) {
            offersVisualHtml = `<p class="ai-intel-status-msg">Individual supplier offers and pricing are kept confidential in the Department workspace until procurement completion.</p>`;
        } else if (!Array.isArray(report.offers) || report.offers.length === 0) {
            offersVisualHtml = `<p class="ai-intel-status-msg">No submitted supplier offers recorded on this tender yet.</p>`;
        } else {
            const maxOfferTotal = Math.max(1, ...report.offers.map(o => Number(o.total || 0)));
            const bars = report.offers.map(o => {
                const pct = Math.max(6, Math.round((Number(o.total || 0) / maxOfferTotal) * 100));
                const badges = [
                    o.isAwardedWinner ? `<span class="ai-intel-tag is-win">★ Awarded Winner</span>` : "",
                    o.isRecommended && !o.isAwardedWinner ? `<span class="ai-intel-tag is-win">Recommended</span>` : "",
                    o.isLowestPrice ? `<span class="ai-intel-tag">Lowest Price</span>` : "",
                    o.isFastestDelivery ? `<span class="ai-intel-tag">Fastest (${o.deliveryDays}d)</span>` : ""
                ].filter(Boolean).join(" ");

                const scoreText = o.averageScore != null ? ` · Score: ${Number(o.averageScore).toFixed(2)}/100` : "";
                const barColor = o.isAwardedWinner || o.isRecommended ? "#185735" : (o.complianceStatus === "Compliant" ? "#1d5560" : "#c98b2e");

                return `
                    <div class="ai-intel-bar-row">
                        <div class="ai-intel-bar-top">
                            <span class="ai-intel-bar-label">#${o.bidId} ${esc(o.supplierName)} ${badges}</span>
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
                    <td>${o.averageScore != null ? `${Number(o.averageScore).toFixed(2)} / 100` : "—"}</td>
                    <td>${o.isAwardedWinner ? `<span class="ai-intel-tag is-win">Awarded</span>` : o.isRecommended ? `<span class="ai-intel-tag is-win">Recommended</span>` : esc(o.status)}</td>
                </tr>`).join("");

            offersVisualHtml = `
                <div class="ai-intel-chart-card" style="margin-bottom:1rem">
                    <h3>Visual Quotation Price, Delivery &amp; Score Comparison</h3>
                    <div class="ai-intel-bars">${bars}</div>
                </div>
                <div class="ai-intel-table-wrap" style="margin-bottom:1rem">
                    <table class="ai-intel-table">
                        <thead>
                            <tr>
                                <th>Offer</th><th>Supplier</th><th>Compliance</th><th>Subtotal</th><th>Tax</th><th>Add. Costs</th><th>Total</th><th>Delivery</th><th>Committee Avg</th><th>Outcome</th>
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
            <div class="ai-intel-badges" style="margin-bottom:0.65rem">
                <span class="ai-intel-tag">Tender #${report.tenderId}</span>
                <span class="ai-intel-tag">Request #${report.purchaseRequestId} (${esc(report.department)})</span>
                <span class="ai-intel-tag">Round ${report.round}</span>
                <span class="ai-intel-tag">Lifecycle: ${esc(report.lifecycleFormatted)}</span>
                ${weightsBadge}
                ${report.topCommitteeApprovals > 0 ? `<span class="ai-intel-tag is-win">CEO &amp; Chiefs Approvals: ${report.topCommitteeApprovals}/4</span>` : ""}
            </div>
            <p class="ai-intel-section-sub" style="margin-bottom:0.5rem">
                <strong>Scope:</strong> ${esc(report.description)}
                ${report.terms ? ` · <strong>Terms:</strong> ${esc(report.terms)}` : ""}
            </p>
            <p class="ai-intel-section-sub">
                Created: ${esc(report.createdUtc)} UTC · Closes: ${esc(report.closesUtc)} UTC
                ${report.awardedUtc ? ` · Awarded: ${esc(report.awardedUtc)} UTC` : ""}
                ${report.winnerNotifiedUtc ? ` · Winner Notified: ${esc(report.winnerNotifiedUtc)} UTC` : ""}
            </p>

            <div class="ai-intel-stepper">${stepsHtml}</div>

            ${offersVisualHtml}

            <h3 style="font-size:0.96rem;font-weight:720;margin:0.75rem 0 0.5rem">Requested Purchase Line Items</h3>
            <div class="ai-intel-table-wrap">
                <table class="ai-intel-table">
                    <thead><tr><th>Item ID</th><th>Item Name</th><th>Quantity</th><th>Requested Specifications</th></tr></thead>
                    <tbody>${itemRowsHtml || `<tr><td colspan="4">No items recorded.</td></tr>`}</tbody>
                </table>
            </div>
        `;
    }

    try {
        const initial = JSON.parse(root.dataset.initialTender || "null");
        if (initial) renderTenderReport(initial);
    } catch { /* ignore */ }

    async function loadTenderById(tenderId) {
        if (!tenderId) return;
        if (tenderSelect) tenderSelect.value = String(tenderId);
        if (tenderAiBox) { tenderAiBox.hidden = true; tenderAiBox.innerHTML = ""; }
        if (tenderStatus) {
            tenderStatus.hidden = false;
            delete tenderStatus.dataset.error;
            tenderStatus.textContent = `Loading Tender #${tenderId} visual analytical report…`;
        }

        root.querySelectorAll("[data-tender-row]").forEach(tr => {
            tr.classList.toggle("is-selected", tr.dataset.tenderRow === String(tenderId));
        });

        try {
            const resp = await fetch(`/AiIntelligence/TenderReport?tenderId=${encodeURIComponent(tenderId)}&workspace=${encodeURIComponent(workspace)}`);
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) throw new Error(data?.error || "Could not load tender report.");
            renderTenderReport(data);
            if (tenderStatus) tenderStatus.hidden = true;
        } catch (err) {
            if (tenderStatus) {
                tenderStatus.hidden = false;
                tenderStatus.dataset.error = "true";
                tenderStatus.textContent = err.message || "Error loading tender report.";
            }
        }
    }

    tenderSelect?.addEventListener("change", () => loadTenderById(tenderSelect.value));

    root.querySelectorAll("[data-tender-row]").forEach(tr => {
        tr.addEventListener("click", (e) => {
            const id = tr.dataset.tenderRow;
            loadTenderById(id);
            document.getElementById("tender-explorer")?.scrollIntoView({ behavior: "smooth", block: "start" });
            e.stopPropagation();
        });
    });

    // 3. Render AI Insights Helper
    function buildInsightsMarkup(res) {
        const cards = (res.insights || []).map(item => `
            <article class="ai-intel-card">
                <div class="ai-intel-card-head">
                    <span class="ai-intel-card-cat">${esc(item.category)}</span>
                    <span class="ai-intel-card-metric">${esc(item.tone || "advisory")}</span>
                </div>
                <h4>${esc(item.title)}</h4>
                <p>${esc(item.finding)}</p>
                <div class="ai-intel-rec"><strong>Advisory Recommendation:</strong> ${esc(item.recommendation)}</div>
            </article>
        `).join("");

        return `
            <div class="ai-intel-chart-card" style="border-color:#00a3a6">
                <div>
                    <span class="ai-intel-card-cat">✦ Live AI Synthesis</span>
                    <h3 style="margin-top:0.2rem">${esc(res.headline)}</h3>
                    <p class="ai-intel-section-sub" style="margin-top:0.35rem">${esc(res.executiveBrief)}</p>
                </div>
                <div class="ai-intel-cards-grid">${cards}</div>
            </div>`;
    }

    // Portfolio Live AI Analysis
    const portfolioForm = root.querySelector("[data-portfolio-form]");
    const portfolioHeroBtn = root.querySelector("[data-generate-portfolio]");
    const portfolioStatus = root.querySelector("[data-portfolio-status]");
    const portfolioOut = root.querySelector("[data-ai-portfolio-output]");
    const portfolioSubmit = root.querySelector("[data-portfolio-submit]");

    async function runPortfolioAi(questionText) {
        if (portfolioSubmit) portfolioSubmit.disabled = true;
        if (portfolioHeroBtn) portfolioHeroBtn.disabled = true;
        if (portfolioStatus) {
            portfolioStatus.hidden = false;
            delete portfolioStatus.dataset.error;
            portfolioStatus.textContent = "AI is analyzing your live Tiryaq procurement & supplier data…";
        }
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
            if (!resp.ok || !data) throw new Error(data?.error || "Could not generate AI insights right now.");
            if (portfolioOut) {
                portfolioOut.innerHTML = buildInsightsMarkup(data);
                portfolioOut.hidden = false;
                portfolioOut.scrollIntoView({ behavior: "smooth", block: "nearest" });
            }
            if (portfolioStatus) portfolioStatus.hidden = true;
        } catch (err) {
            if (portfolioStatus) {
                portfolioStatus.hidden = false;
                portfolioStatus.dataset.error = "true";
                portfolioStatus.textContent = err.message;
            }
        } finally {
            if (portfolioSubmit) portfolioSubmit.disabled = false;
            if (portfolioHeroBtn) portfolioHeroBtn.disabled = false;
        }
    }

    portfolioForm?.addEventListener("submit", (e) => {
        e.preventDefault();
        const q = portfolioForm.elements.question?.value?.trim() || "";
        runPortfolioAi(q);
    });

    portfolioHeroBtn?.addEventListener("click", () => runPortfolioAi(""));

    // Per-Tender Live AI Recommendation
    const analyzeTenderBtn = root.querySelector("[data-analyze-tender]");
    analyzeTenderBtn?.addEventListener("click", async () => {
        const tid = Number(tenderSelect?.value || 0);
        if (!tid) return;
        analyzeTenderBtn.disabled = true;
        if (tenderStatus) {
            tenderStatus.hidden = false;
            delete tenderStatus.dataset.error;
            tenderStatus.textContent = `Generating advisory AI evaluation & recommendations for Tender #${tid}…`;
        }
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
            if (!resp.ok || !data) throw new Error(data?.error || "Could not generate tender AI recommendation.");
            if (tenderAiBox) {
                tenderAiBox.innerHTML = buildInsightsMarkup(data);
                tenderAiBox.hidden = false;
            }
            if (tenderStatus) tenderStatus.hidden = true;
        } catch (err) {
            if (tenderStatus) {
                tenderStatus.hidden = false;
                tenderStatus.dataset.error = "true";
                tenderStatus.textContent = err.message;
            }
        } finally {
            analyzeTenderBtn.disabled = false;
        }
    });

    // 4. Excel & PDF Upload & AI Extraction Studio
    const uploadForm = root.querySelector("[data-upload-form]");
    const uploadSubmit = root.querySelector("[data-upload-submit]");
    const uploadStatus = root.querySelector("[data-upload-status]");
    const uploadResults = root.querySelector("[data-upload-results]");

    uploadForm?.addEventListener("dragover", (e) => {
        e.preventDefault();
        uploadForm.classList.add("is-dragover");
    });
    uploadForm?.addEventListener("dragleave", () => uploadForm.classList.remove("is-dragover"));
    uploadForm?.addEventListener("drop", (e) => {
        e.preventDefault();
        uploadForm.classList.remove("is-dragover");
        const fileInput = uploadForm.querySelector("[data-upload-file]");
        if (e.dataTransfer?.files?.length && fileInput) {
            fileInput.files = e.dataTransfer.files;
        }
    });

    uploadForm?.addEventListener("submit", async (e) => {
        e.preventDefault();
        const fileInput = uploadForm.querySelector("[data-upload-file]");
        if (!fileInput?.files?.length) return;

        const fd = new FormData(uploadForm);
        fd.set("workspace", workspace);

        if (uploadSubmit) uploadSubmit.disabled = true;
        if (uploadStatus) {
            uploadStatus.hidden = false;
            delete uploadStatus.dataset.error;
            uploadStatus.textContent = `Parsing '${fileInput.files[0].name}' and generating AI document intelligence…`;
        }

        try {
            const resp = await fetch("/AiIntelligence/AnalyzeUpload", {
                method: "POST",
                headers: { "RequestVerificationToken": csrfToken },
                body: fd
            });
            const data = await resp.json().catch(() => null);
            if (!resp.ok || !data) throw new Error(data?.error || "Could not analyze the uploaded file.");

            const kpiHtml = (data.extractedMetrics || []).map(k => `
                <div class="ai-intel-kpi" data-tone="${esc(k.tone || "primary")}">
                    <div class="ai-intel-kpi-label">${esc(k.label)}</div>
                    <div class="ai-intel-kpi-value" style="font-size:1.2rem">${esc(k.value)}</div>
                    <div class="ai-intel-kpi-sub">${esc(k.subtext)}</div>
                </div>`).join("");

            const dlHtml = (data.downloads || []).map(dl => `
                <a class="ai-intel-btn is-primary" href="${esc(dl.url)}" download="${esc(dl.fileName)}">
                    ⬇ ${esc(dl.label || dl.fileName)}
                </a>`).join("");

            const insightsHtml = (data.insights || []).map(item => `
                <article class="ai-intel-card">
                    <div class="ai-intel-card-head">
                        <span class="ai-intel-card-cat">${esc(item.category)}</span>
                        <span class="ai-intel-card-metric">${esc(item.tone)}</span>
                    </div>
                    <h4>${esc(item.title)}</h4>
                    <p>${esc(item.finding)}</p>
                    <div class="ai-intel-rec"><strong>Recommendation:</strong> ${esc(item.recommendation)}</div>
                </article>`).join("");

            let tableHtml = "";
            if (Array.isArray(data.tableHeaders) && data.tableHeaders.length > 0) {
                const ths = data.tableHeaders.map(h => `<th>${esc(h)}</th>`).join("");
                const trs = (data.tableRows || []).map(r => `<tr>${r.map(c => `<td>${esc(c)}</td>`).join("")}</tr>`).join("");
                tableHtml = `
                    <h3 style="font-size:0.96rem;font-weight:720;margin:1rem 0 0.5rem">Extracted Structured Data</h3>
                    <div class="ai-intel-table-wrap">
                        <table class="ai-intel-table">
                            <thead><tr>${ths}</tr></thead>
                            <tbody>${trs}</tbody>
                        </table>
                    </div>`;
            }

            if (uploadResults) {
                uploadResults.innerHTML = `
                    <div class="ai-intel-chart-card" style="border-color:#00a3a6">
                        <div class="ai-intel-section-head" style="margin-bottom:0.5rem">
                            <div>
                                <span class="ai-intel-card-cat">${esc(data.fileType)}</span>
                                <h3 style="margin-top:0.2rem">${esc(data.fileName)}</h3>
                            </div>
                            <div class="ai-intel-toolbar">${dlHtml}</div>
                        </div>
                        <p class="ai-intel-section-sub" style="margin-bottom:0.75rem">${esc(data.summary)}</p>
                        <div class="ai-intel-kpi-grid" style="margin-bottom:0.85rem">${kpiHtml}</div>
                        <div class="ai-intel-cards-grid">${insightsHtml}</div>
                        ${tableHtml}
                    </div>`;
                uploadResults.hidden = false;
            }
            if (uploadStatus) uploadStatus.hidden = true;
        } catch (err) {
            if (uploadStatus) {
                uploadStatus.hidden = false;
                uploadStatus.dataset.error = "true";
                uploadStatus.textContent = err.message;
            }
        } finally {
            if (uploadSubmit) uploadSubmit.disabled = false;
        }
    });

    // 5. Table Filtering & Search
    const tenderSearch = root.querySelector("[data-tender-search]");
    const tenderStatusFilter = root.querySelector("[data-tender-status-filter]");
    const tenderRows = Array.from(root.querySelectorAll("[data-tenders-table] tbody tr[data-tender-row]"));

    function filterTendersTable() {
        const q = (tenderSearch?.value || "").trim().toLowerCase();
        const st = (tenderStatusFilter?.value || "").trim().toLowerCase();
        for (const tr of tenderRows) {
            const matchText = !q || tr.textContent.toLowerCase().includes(q);
            const rowSt = (tr.dataset.status || "").toLowerCase();
            const matchStatus = !st || rowSt === st || rowSt.replace(/\s+/g, "") === st.replace(/\s+/g, "");
            tr.hidden = !(matchText && matchStatus);
        }
    }

    tenderSearch?.addEventListener("input", filterTendersTable);
    tenderStatusFilter?.addEventListener("change", filterTendersTable);

    root.querySelectorAll("[data-filter-status]").forEach(btn => {
        btn.addEventListener("click", () => {
            const raw = (btn.dataset.filterStatus || "").replace(/\s+/g, "").toLowerCase();
            if (!tenderStatusFilter) return;
            const matchOpt = Array.from(tenderStatusFilter.options).find(o => o.value.toLowerCase() === raw);
            tenderStatusFilter.value = matchOpt ? matchOpt.value : "";
            filterTendersTable();
            root.querySelector("[data-tenders-table]")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        });
    });

    const supplierSearch = root.querySelector("[data-supplier-search]");
    const supplierRows = Array.from(root.querySelectorAll("[data-suppliers-table] tbody tr"));
    supplierSearch?.addEventListener("input", () => {
        const q = supplierSearch.value.trim().toLowerCase();
        for (const tr of supplierRows) {
            tr.hidden = Boolean(q) && !tr.textContent.toLowerCase().includes(q);
        }
    });
})();
