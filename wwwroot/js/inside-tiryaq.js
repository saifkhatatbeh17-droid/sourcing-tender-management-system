import { createCityScene } from './tiryaq-city-scene.js';

const page = document.querySelector('[data-campus]');
if (page) boot();

function boot() {
    const stages = [
        [
            'Start with a clear need.',
            'Request needs',
            'A department submits required items, quantities, technical specifications, and target delivery dates in a two-stage purchase request.',
            [['Department', 'responsible team'], ['2 stages', 'structured intake']],
            [-15, -9],
            'PR-0417 · Purchase request',
            'Department → Sourcing officer'
        ],
        [
            'Publish a clear tender.',
            'Publish tender',
            'The sourcing officer validates the request, sets compliance terms and closing dates, and invites approved suppliers in the category.',
            [['Sourcing officer', 'responsible role'], ['Up to 4', 'offer rounds']],
            [-15, 7],
            'TND-0417 · Published tender',
            'Sourcing officer → Approved suppliers'
        ],
        [
            'Bring supplier offers together.',
            'Submit offers',
            'Invited suppliers submit JD item prices, tax percentage, delivery terms, and supporting certificates before the closing deadline.',
            [['Suppliers', 'qualified bidders'], ['3', 'sample offers']],
            [0, 12],
            '3 sealed quotations (JD + Tax %)',
            'Invited suppliers → Compliance check'
        ],
        [
            'Compare offers fairly.',
            'Evaluate offers',
            'After compliance checks, four committee members lock technical and financial weights, score every eligible offer, and resolve ties unanimously.',
            [['4 reviewers', 'evaluation committee'], ['Locked %', 'agreed weights']],
            [-1, -9],
            '4× weighted scorecards & report',
            'Committee → Officer → Sourcing manager'
        ],
        [
            'Confirm the selection.',
            'Approve selection',
            'The sourcing manager reviews the report and refers the case. Four CEO & Chiefs positions review the comparison and record final votes.',
            [['Sourcing manager', 'referral review'], ['4 / 4 votes', 'CEO & Chiefs']],
            [14, -9],
            'Manager referral + 4/4 approvals',
            'Sourcing manager → CEO & Chiefs'
        ],
        [
            'Record the award.',
            'Finalize award',
            'With executive approval recorded, the manager confirms the shared report and the officer finalizes the award and closes the tender.',
            [['Recorded award', 'process outcome'], ['Full audit', 'request to award']],
            [15, 7],
            'AWD-0417 · Closed award file',
            'Sourcing officer → Awarded supplier & department'
        ]
    ];

    const $ = s => page.querySelector(s), $$ = s => [...page.querySelectorAll(s)];
    const clamp = (n, a, b) => Math.max(a, Math.min(b, n));
    const host = $('[data-campus-world]'), canvas = $('[data-campus-canvas]');
    const labels = $$('[data-campus-place]'), buttons = $$('[data-campus-step]');
    const detailRows = $$('[data-campus-detail-row]'), detailJumps = $$('[data-campus-detail-jump]');
    const dots = $$('[data-campus-dot]');
    const copyBlock = $('[data-campus-copy]');
    const autoplayBtn = $('[data-campus-autoplay]'), autoplayLabel = $('[data-campus-autoplay-label]'), autoplayFill = $('[data-campus-autoplay-fill]');
    const enterBtn = $('[data-campus-enter]');
    const progressBar = $('[data-campus-progress-bar]');
    const hud = $('[data-campus-hud]'), liveBadge = $('[data-campus-live]');
    const supplierNote = $('[data-campus-supplier-note]');

    const reduced = matchMedia('(prefers-reduced-motion: reduce)'), small = matchMedia('(max-width:900px)');
    let mode = 'journey', active = -1, count = 3, paused = reduced.matches, api = null;
    page.dataset.mode = mode;

    const STAGE_MS = 6000, TOTAL_MS = STAGE_MS * stages.length;
    let playing = false, elapsed = 0, playLast = 0, playFrame = 0;

    function setAutoplayFill(ratio) {
        if (autoplayFill) autoplayFill.style.transform = `scaleX(${clamp(ratio, 0, 1).toFixed(4)})`;
    }

    function stopTour() {
        if (!playing) return;
        playing = false;
        cancelAnimationFrame(playFrame);
        autoplayLabel.textContent = 'Autoplay tour';
        autoplayBtn.setAttribute('aria-pressed', 'false');
        setAutoplayFill(0);
    }

    function tickTour(now) {
        if (!playing) return;
        const dt = playLast ? Math.min(now - playLast, 100) : 0;
        playLast = now;
        if (!document.hidden && !paused) {
            elapsed += dt;
            const stage = Math.min(5, Math.floor(elapsed / STAGE_MS));
            const phase = (elapsed % STAGE_MS) / STAGE_MS;
            if (active !== stage) card(stage, true);
            const routeProgress = Math.min(1, (stage + Math.max(0, (phase - .45) / .55)) / 5);
            api?.progress(routeProgress);
            api?.follow(routeProgress);
            setAutoplayFill(elapsed / TOTAL_MS);
            if (elapsed >= TOTAL_MS) {
                stopTour();
                return;
            }
        }
        playFrame = requestAnimationFrame(tickTour);
    }

    autoplayBtn.addEventListener('click', () => {
        if (playing) { stopTour(); return; }
        setMode('explore');
        api?.enter(true);
        paused = false;
        motionState();
        elapsed = 0;
        playLast = 0;
        card(0, true);
        api?.progress(0);
        playing = true;
        autoplayLabel.textContent = 'Stop autoplay';
        autoplayBtn.setAttribute('aria-pressed', 'true');
        setAutoplayFill(0);
        playFrame = requestAnimationFrame(tickTour);
    });

    enterBtn.addEventListener('click', () => {
        stopTour();
        const nextInside = !api?.isInside();
        if (nextInside) setMode('explore');
        api?.enter(nextInside);
        if (nextInside && active < 0) go(0);
        else syncViewControls();
    });

    page.addEventListener('click', e => {
        if (e.target.closest('[data-campus-step],[data-campus-place],[data-campus-back],[data-campus-start],[data-campus-overview],[data-campus-replay],[data-campus-mode],[data-campus-rotate],[data-campus-zoom],[data-campus-detail-jump]')) {
            stopTour();
        }
    }, true);
    canvas.addEventListener('pointerdown', stopTour);
    canvas.addEventListener('keydown', stopTour);
    canvas.addEventListener('wheel', stopTour, { passive: true });
    window.addEventListener('pagehide', stopTour);

    function fact(value, label) {
        const span = document.createElement('span'), strong = document.createElement('strong');
        strong.textContent = value;
        span.append(strong, document.createTextNode(label));
        return span;
    }

    function animateCopySwap() {
        if (reduced.matches || !copyBlock?.animate) return;
        copyBlock.animate(
            [
                { opacity: .38, transform: 'translateY(5px)' },
                { opacity: 1, transform: 'translateY(0)' }
            ],
            { duration: 190, easing: 'cubic-bezier(0.23, 1, 0.32, 1)' }
        );
    }

    function syncViewControls() {
        const inside = Boolean(api?.isInside());
        enterBtn.setAttribute('aria-pressed', String(inside));
        enterBtn.textContent = inside ? 'Exit close-up' : 'Close-up view';
        const s = stages[active];
        if (hud) {
            hud.textContent = inside
                ? `${s ? `0${active + 1} ${s[1]}` : 'Close-up'} · Drag to orbit · Shift-drag to pan`
                : mode === 'explore'
                    ? 'Free explore · Drag to orbit · Shift-drag to pan · Scroll to zoom'
                    : small.matches
                        ? 'Tap any numbered station or step below'
                        : 'Isometric campus · Select a station or drag to orbit';
        }
        if (liveBadge) {
            liveBadge.textContent = s
                ? `Step 0${active + 1} of 06 · ${s[1]}`
                : 'Interactive campus · Overview';
        }
    }

    function updateSupplierCopy() {
        if (!supplierNote) return;
        supplierNote.textContent = count === 3
            ? '3 compliant offers meet Tiryaq’s standard committee handoff rule.'
            : `${count} competing supplier offers streaming into market & committee review.`;
    }

    function card(index, announce = false) {
        const changed = active !== index;
        active = index;
        const s = stages[index];
        if (changed) animateCopySwap();

        if (s) {
            $('#campus-title').textContent = s[0];
            $('[data-campus-chapter]').textContent = `Step ${String(index + 1).padStart(2, '0')} of 06 · ${s[1]}`;
            $('[data-campus-description]').textContent = s[2];
            $('[data-campus-artifact]').textContent = index === 2 ? `${count} sealed quotations (JD + Tax %)` : s[5];
            $('[data-campus-handoff]').textContent = s[6];
            $('[data-campus-facts]').replaceChildren(...s[3].map(([v, l]) =>
                fact(index === 2 && l === 'sample offers' ? String(count) : v, l)));
            $('[data-campus-start]').innerHTML = index === 5
                ? 'Return to overview <span aria-hidden="true">↺</span>'
                : `Next: ${stages[index + 1][1]} <span aria-hidden="true">→</span>`;
        } else {
            $('#campus-title').replaceChildren('One request.', document.createElement('br'), 'Six clear steps.');
            $('[data-campus-chapter]').textContent = 'From request to award';
            $('[data-campus-description]').textContent = 'Follow a pharmaceutical purchase request across Tiryaq. Every team works from one connected dossier from first specification to final award.';
            $('[data-campus-artifact]').textContent = 'Shared procurement file · PR-0417';
            $('[data-campus-handoff]').textContent = 'Department → Sourcing → Suppliers → Committees';
            $('[data-campus-facts]').replaceChildren(fact('6', 'connected stages'), fact('1', 'auditable dossier'));
            $('[data-campus-start]').innerHTML = 'Begin the journey <span aria-hidden="true">→</span>';
        }

        $('[data-campus-back]').disabled = index < 0;
        const ratio = index < 0 ? 0 : (index + 1) / 6;
        if (progressBar) progressBar.style.transform = `scaleX(${ratio.toFixed(4)})`;
        dots.forEach((dot, i) => {
            dot.classList.toggle('is-done', index >= 0 && i < index);
            dot.classList.toggle('is-active', i === index);
        });
        $('[data-campus-progress]').textContent = index < 0
            ? 'Six steps · One connected process'
            : `Step ${index + 1} of 6 · ${s[1]}`;

        buttons.forEach((b, i) => {
            b.classList.toggle('is-completed', index >= 0 && i < index);
            if (i === index) b.setAttribute('aria-current', 'step');
            else b.removeAttribute('aria-current');
        });
        labels.forEach((b, i) => {
            b.classList.toggle('is-completed', index >= 0 && i < index);
            if (i === index) b.setAttribute('aria-current', 'step');
            else b.removeAttribute('aria-current');
        });
        detailRows.forEach((row, i) => {
            row.classList.toggle('is-active', i === index);
            if (i === index) row.setAttribute('aria-current', 'step');
            else row.removeAttribute('aria-current');
        });

        if (announce) {
            $('[data-campus-status]').textContent = s ? `Step ${index + 1} of 6: ${s[1]}. ${s[2]}` : 'Campus overview';
        }
        api?.select(index);
        syncViewControls();
    }

    function setMode(value) {
        if (mode === value) return;
        mode = value;
        page.dataset.mode = mode;
        $$('[data-campus-mode]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.campusMode === mode)));
        api?.mode(mode);
        syncViewControls();
    }

    function go(index) {
        index = clamp(index, -1, 5);
        card(index, true);
        api?.progress(index < 0 ? 0 : index / 5);
    }

    function overview() {
        setMode('journey');
        api?.reset();
        go(-1);
    }

    $('[data-campus-back]').addEventListener('click', () => go(active - 1));
    $('[data-campus-start]').addEventListener('click', () => active < 5 ? go(active + 1) : overview());
    buttons.forEach((b, i) => b.addEventListener('click', () => go(i)));
    labels.forEach((b, i) => b.addEventListener('click', () => { setMode('explore'); go(i); }));

    detailRows.forEach((row, i) => {
        row.addEventListener('click', e => {
            if (e.target.closest('[data-campus-detail-jump]')) return;
            go(i);
        });
    });
    detailJumps.forEach((btn, i) => {
        btn.addEventListener('click', () => {
            go(i);
            page.scrollIntoView({ behavior: reduced.matches ? 'instant' : 'smooth', block: 'start' });
        });
    });

    $$('[data-campus-mode]').forEach(b => b.addEventListener('click', () => {
        const targetMode = b.dataset.campusMode;
        setMode(targetMode);
        if (targetMode === 'journey') {
            api?.enter(false);
            if (active < 0) api?.reset();
            syncViewControls();
        }
    }));

    $('[data-campus-overview]').addEventListener('click', overview);
    $('[data-campus-replay]').addEventListener('click', () => {
        overview();
        page.scrollIntoView({ behavior: reduced.matches ? 'instant' : 'smooth', block: 'start' });
    });

    $$('[data-campus-rotate]').forEach(b => b.addEventListener('click', () => {
        setMode('explore');
        api?.rotate(Number(b.dataset.campusRotate));
    }));
    canvas.addEventListener('keydown', e => {
        if (!['ArrowLeft', 'ArrowRight', '+', '=', '-'].includes(e.key)) return;
        e.preventDefault();
        setMode('explore');
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') api?.rotate(e.key === 'ArrowLeft' ? -1 : 1);
        else api?.zoom(e.key === '-' ? -1 : 1);
    });
    $$('[data-campus-zoom]').forEach(b => b.addEventListener('click', () => api?.zoom(Number(b.dataset.campusZoom))));

    const motion = $('[data-campus-motion]');
    function motionState() {
        motion.textContent = paused ? 'Resume motion' : 'Pause motion';
        motion.setAttribute('aria-pressed', String(paused));
        api?.pause(paused);
    }
    motion.addEventListener('click', () => { paused = !paused; motionState(); });
    reduced.addEventListener('change', () => { paused = reduced.matches; motionState(); });

    $('#campus-suppliers').addEventListener('input', e => {
        count = Number(e.target.value);
        $('#campus-supplier-count').value = String(count);
        updateSupplierCopy();
        api?.suppliers(count, true);
        if (active === 2) card(2);
    });
    small.addEventListener('change', () => card(active));

    function fallback(error) {
        console.warn('Campus view unavailable', error);
        page.dataset.render = 'fallback';
        $('[data-campus-loading]').hidden = true;
        $('[data-campus-fallback]').hidden = false;
        labels.forEach(b => { b.hidden = true; });
    }

    try {
        api = createCityScene({
            host, canvas, labels, stages, reduced, small, paused,
            onInteract() { stopTour(); setMode('explore'); },
            onSelect(index) { setMode('explore'); go(index); },
            onStateChange() { syncViewControls(); },
            onFallback: fallback
        });
        motionState();
        updateSupplierCopy();
        api.suppliers(count, false);
        card(-1);
        $('[data-campus-loading]').hidden = true;
    } catch (e) {
        fallback(e);
    }
}
