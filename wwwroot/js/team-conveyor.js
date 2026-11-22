// Team conveyor: each crate rides in, passes the scanner, pops its lid and a member card rises out.
// The card then flies into the shipping manifest and the empty crate rides off.
(() => {
    const page = document.querySelector('[data-cv]');
    if (!page) return;

    const stage = page.querySelector('[data-cv-stage]');
    const slot = stage.querySelector('[data-cv-slot-crate]');
    const crate = stage.querySelector('[data-cv-crate]');
    const lid = stage.querySelector('[data-cv-lid]');
    const card = stage.querySelector('[data-cv-card]');
    const belt = stage.querySelector('[data-cv-belt]');
    const laser = stage.querySelector('[data-cv-laser]');
    const screen = stage.querySelector('[data-cv-screen]');
    const peanuts = stage.querySelector('[data-cv-peanuts]');
    const crateNo = stage.querySelector('[data-cv-crate-no]');
    const crateRole = stage.querySelector('[data-cv-crate-role]');
    const templates = [...page.querySelectorAll('template[data-cv-template]')];
    const rows = [...page.querySelectorAll('[data-cv-row]')];
    const toggle = page.querySelector('[data-cv-toggle]');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches || typeof stage.animate !== 'function';
    const total = templates.length;
    const HOLD_MS = 5200;

    let current = -1;
    let busy = false;
    let queued = null;
    let paused = reduced;
    let timer = 0;

    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    const run = (element, frames, options) =>
        element.animate(frames, { fill: 'forwards', ...options }).finished.catch(() => {});
    const pad = n => String(n + 1).padStart(2, '0');
    const em = () => parseFloat(getComputedStyle(stage).fontSize);
    const insideTransform = 'translate(-50%, 0) scale(.4)';
    const risenTransform = () => `translate(-50%, ${-(crate.offsetHeight * .6 + em() * 3.2)}px) scale(1)`;

    function setScreen(text, ok = false) {
        screen.textContent = text;
        screen.classList.toggle('is-ok', ok);
    }

    function setRowState(i, state) {
        const row = rows[i];
        row.classList.toggle('is-current', state === 'current');
        if (state === 'delivered') row.classList.add('is-delivered');
        row.querySelector('[data-cv-status]').textContent =
            state === 'current' ? 'On belt' : row.classList.contains('is-delivered') ? 'Delivered' : 'Queued';
    }

    function loadCrate(i) {
        card.replaceChildren(templates[i].content.cloneNode(true));
        crateNo.textContent = `Crate ${pad(i)}`;
        crateRole.textContent = `Contents: 1 × ${templates[i].dataset.role}`;
        [slot, crate, lid, card].forEach(el => el.getAnimations().forEach(a => a.cancel()));
        card.style.transform = insideTransform;
        stage.classList.remove('is-out', 'is-revealed');
    }

    function moveBelt(on) { belt.classList.toggle('is-moving', on); }

    function popcorn() {
        const box = crate.getBoundingClientRect();
        const host = stage.getBoundingClientRect();
        const x = box.left - host.left + box.width / 2;
        const y = box.top - host.top;
        for (let k = 0; k < 34; k++) {
            const nut = document.createElement('i');
            nut.style.left = `${x + (Math.random() - .5) * box.width * .7}px`;
            nut.style.top = `${y}px`;
            peanuts.append(nut);
            const vx = (Math.random() - .5) * box.width * 1.6;
            const up = -(60 + Math.random() * 160);
            const floor = box.height + 20 + Math.random() * 30;
            const spin = (Math.random() - .5) * 900;
            nut.animate([
                { transform: 'translate(0, 0) rotate(0deg)', opacity: 1 },
                { transform: `translate(${vx * .5}px, ${up}px) rotate(${spin * .4}deg)`, opacity: 1, offset: .35 },
                { transform: `translate(${vx}px, ${floor}px) rotate(${spin}deg)`, opacity: 1, offset: .85 },
                { transform: `translate(${vx * 1.05}px, ${floor}px) rotate(${spin}deg)`, opacity: 0 }
            ], { duration: 1300 + Math.random() * 600, easing: 'cubic-bezier(.25,.6,.5,1)', fill: 'forwards' })
                .finished.then(() => nut.remove()).catch(() => {});
        }
    }

    async function enter() {
        setScreen('Crate incoming');
        moveBelt(true);
        await run(slot, [{ transform: `translateX(${-stage.offsetWidth * .75}px)` }, { transform: 'translateX(0)' }],
            { duration: 1500, easing: 'cubic-bezier(.3,.6,.3,1)' });
        moveBelt(false);
        await run(crate, [
            { transform: 'none' }, { transform: 'scale(1.04, .93) skewX(-3deg)', offset: .3 },
            { transform: 'scale(.98, 1.03) skewX(1deg)', offset: .65 }, { transform: 'none' }
        ], { duration: 420, easing: 'ease-out' });
    }

    async function scan(i) {
        setScreen(`Scanning crate ${pad(i)}…`);
        const travel = crate.offsetHeight + em() * 3;
        await run(laser, [
            { opacity: 0, transform: 'translateY(0)' },
            { opacity: 1, transform: `translateY(${travel * .1}px)`, offset: .1 },
            { opacity: 1, transform: `translateY(${travel}px)`, offset: .55 },
            { opacity: 1, transform: `translateY(${travel * .1}px)`, offset: .9 },
            { opacity: 0, transform: 'translateY(0)' }
        ], { duration: 1300, easing: 'ease-in-out' });
        setScreen(`Cleared · ${templates[i].dataset.role}`, true);
    }

    async function open() {
        await run(crate, [
            { transform: 'none' }, { transform: 'rotate(-2deg)' }, { transform: 'rotate(2.5deg)' },
            { transform: 'rotate(-1.5deg) translateY(-.2em)' }, { transform: 'rotate(1deg)' }, { transform: 'none' }
        ], { duration: 520 });
        popcorn();
        run(lid, [
            { transform: 'none', opacity: 1 },
            { transform: `translate(${em() * 9}px, ${-em() * 13}px) rotate(38deg)`, opacity: 1, offset: .4 },
            { transform: `translate(${em() * 20}px, ${em() * 18}px) rotate(120deg)`, opacity: 0 }
        ], { duration: 1200, easing: 'cubic-bezier(.3,.7,.6,1)' });
        await wait(160);
        stage.classList.add('is-out');
        await run(card, [{ transform: insideTransform }, { transform: risenTransform() }],
            { duration: 900, easing: 'cubic-bezier(.34,1.45,.55,1)' });
        stage.classList.add('is-revealed');
    }

    async function shipOut() {
        if (current < 0) return;
        const done = current;
        stage.classList.remove('is-revealed');
        const target = rows[done].querySelector('[data-cv-avatar]').getBoundingClientRect();
        const now = card.getBoundingClientRect();
        if (!reduced && target.width > 0) {
            const scale = target.width / card.offsetWidth;
            const dx = target.left + target.width / 2 - (now.left + now.width / 2);
            const dy = target.top + target.height / 2 - now.bottom + (card.offsetHeight * scale) / 2;
            const lift = -(crate.offsetHeight * .6 + em() * 3.2);
            stage.style.overflow = 'visible';
            await run(card, [
                { transform: risenTransform(), opacity: 1 },
                { transform: `translate(calc(-50% + ${dx * .3}px), ${lift + dy * .3 - em() * 4}px) scale(${(1 + scale) / 2}) rotate(-6deg)`, opacity: 1, offset: .4 },
                { transform: `translate(calc(-50% + ${dx}px), ${lift + dy}px) scale(${scale}) rotate(0deg)`, opacity: .2 }
            ], { duration: 800, easing: 'cubic-bezier(.5,0,.3,1)' });
            stage.style.overflow = '';
        }
        stage.classList.remove('is-out');
        setRowState(done, 'delivered');
        rows[done].querySelector('[data-cv-avatar]').animate(
            [{ transform: 'scale(1.5)' }, { transform: 'scale(.9)' }, { transform: 'none' }], { duration: 500, easing: 'ease-out' });
        setScreen('Next crate');
        moveBelt(true);
        await run(slot, [{ transform: 'translateX(0)' }, { transform: `translateX(${stage.offsetWidth * .75}px)` }],
            { duration: 1100, easing: 'cubic-bezier(.6,0,.7,.5)' });
        moveBelt(false);
    }

    async function deliver(i) {
        i = (i + total) % total;
        if (busy) { queued = i; return; }
        if (i === current && stage.classList.contains('is-revealed')) { schedule(); return; }
        busy = true;
        clearTimeout(timer);
        if (reduced) {
            if (current >= 0) setRowState(current, 'delivered');
            loadCrate(i);
            lid.style.opacity = '0';
            card.style.transform = risenTransform();
            stage.classList.add('is-out', 'is-revealed');
            setScreen(`Cleared · ${templates[i].dataset.role}`, true);
        } else {
            await shipOut();
            loadCrate(i);
            setRowState(i, 'current');
            await enter();
            await scan(i);
            await open();
        }
        if (current >= 0 && current !== i) rows[current].classList.remove('is-current');
        current = i;
        setRowState(i, 'current');
        busy = false;
        if (queued !== null) { const next = queued; queued = null; deliver(next); return; }
        schedule();
    }

    function schedule() {
        clearTimeout(timer);
        if (!paused) timer = setTimeout(() => deliver(current + 1), HOLD_MS);
    }

    function setPaused(value) {
        paused = value;
        page.classList.toggle('is-paused', paused);
        toggle.setAttribute('aria-pressed', String(paused));
        toggle.setAttribute('aria-label', paused ? 'Play the conveyor' : 'Pause the conveyor');
        if (paused) clearTimeout(timer); else if (!busy && current >= 0) schedule();
    }

    rows.forEach((row, i) => row.addEventListener('click', () => deliver(i)));
    page.querySelector('[data-cv-next]').addEventListener('click', () => deliver(current + 1));
    page.querySelector('[data-cv-prev]').addEventListener('click', () => deliver(current - 1));
    toggle.addEventListener('click', () => setPaused(!paused));
    // Hold the current card while someone reads it or reaches for a link.
    card.addEventListener('pointerenter', () => clearTimeout(timer));
    card.addEventListener('pointerleave', () => { if (!busy) schedule(); });
    card.addEventListener('focusin', () => clearTimeout(timer));

    setPaused(paused);
    setScreen('Awaiting crate');
    setTimeout(() => deliver(0), reduced ? 0 : 1300);
})();
