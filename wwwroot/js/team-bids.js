// Team page, sealed bids: envelopes deal onto the desk, then open (seal cracks, flap lifts, card slides out).
(() => {
    const page = document.querySelector('[data-bids]');
    if (!page) return;

    const desk = page.querySelector('[data-bids-desk]');
    const bids = [...desk.querySelectorAll('[data-bid]')];
    const templates = [...desk.querySelectorAll('template[data-bid-card]')];
    const dialog = page.querySelector('[data-bids-dialog]');
    const scrim = dialog.querySelector('[data-bids-scrim]');
    const env = dialog.querySelector('.env-big');
    const card = dialog.querySelector('[data-bids-card]');
    const inner = env.querySelector('.env-inner');
    const pocket = env.querySelector('.env-pocket');
    const flap = env.querySelector('.env-flap');
    const seal = env.querySelector('.env-seal');
    const sealL = seal.querySelector('.seal-l');
    const sealR = seal.querySelector('.seal-r');
    const sparks = seal.querySelector('[data-bids-sparks]');
    const closeButton = dialog.querySelector('[data-bids-close]');
    const openedCount = page.querySelector('[data-bids-opened]');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const opened = new Set();
    let current = -1;
    let busy = false;

    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    const run = (element, frames, options) =>
        element.animate(frames, { fill: 'forwards', ...options }).finished.catch(() => {});
    const center = rect => [rect.left + rect.width / 2, rect.top + rect.height / 2];
    const tiltOf = bid => parseFloat(getComputedStyle(bid).getPropertyValue('--rot')) || 0;

    // Load: the stack drops onto the desk, deals out, and each envelope gets its seal stamped.
    async function deal() {
        if (reduced || typeof desk.animate !== 'function') { desk.classList.add('is-dealt'); return; }
        const box = desk.getBoundingClientRect();
        const [cx] = center(box);
        const cy = box.top + box.height / 2;
        const offsets = bids.map(bid => {
            const [bx, by] = center(bid.getBoundingClientRect());
            return [cx - bx, cy - by];
        });
        const seals = bids.map(bid => bid.querySelector('.env-seal'));
        seals.forEach(s => { s.style.opacity = '0'; });

        const drops = bids.map((bid, i) => {
            const [dx, dy] = offsets[i];
            const spin = (i % 2 ? 1 : -1) * (6 + i * 2);
            return bid.animate([
                { transform: `translate(${dx}px, ${dy - 420}px) rotate(${spin * 3}deg) scale(.7)`, opacity: 0 },
                { transform: `translate(${dx}px, ${dy - i * 3}px) rotate(${spin}deg) scale(.82)`, opacity: 1 }
            ], { duration: 560, delay: i * 55, easing: 'cubic-bezier(.55,0,.8,.3)', fill: 'both' });
        });
        await Promise.all(drops.map(a => a.finished)).catch(() => {});
        await wait(180);

        const dealt = bids.map((bid, i) => {
            const [dx, dy] = offsets[i];
            const spin = (i % 2 ? 1 : -1) * (6 + i * 2);
            const move = bid.animate([
                { transform: `translate(${dx}px, ${dy - i * 3}px) rotate(${spin}deg) scale(.82)` },
                { transform: `translate(${dx * .4}px, ${dy * .4 - 60}px) rotate(${-spin}deg) scale(1.08)`, offset: .45 },
                { transform: 'none' }
            ], { duration: 950, delay: i * 95, easing: 'cubic-bezier(.25,.9,.3,1)', fill: 'both' });
            drops[i].cancel();
            return move.finished.then(() => {
                move.cancel();
                seals[i].style.opacity = '';
                seals[i].animate([
                    { transform: 'scale(2.8) rotate(-30deg)', opacity: 0 },
                    { transform: 'scale(.86) rotate(4deg)', opacity: 1, offset: .7 },
                    { transform: 'none', opacity: 1 }
                ], { duration: 420, easing: 'cubic-bezier(.5,0,.75,0)' });
                bid.querySelector('.env').animate([
                    { transform: 'none' }, { transform: 'scale(1.03, .95)', offset: .75 }, { transform: 'none' }
                ], { duration: 520 });
            }).catch(() => {});
        });
        await Promise.all(dealt);
        desk.classList.add('is-dealt');
    }

    function burst() {
        sparks.replaceChildren();
        for (let k = 0; k < 18; k++) {
            const spark = document.createElement('i');
            sparks.append(spark);
            const angle = (k / 18) * Math.PI * 2 + Math.random() * .5;
            const distance = 70 + Math.random() * 110;
            spark.animate([
                { transform: 'translate(0, 0) scale(1.2)', opacity: 1 },
                { transform: `translate(${Math.cos(angle) * distance}px, ${Math.sin(angle) * distance + 40}px) scale(0)`, opacity: 0 }
            ], { duration: 650 + Math.random() * 450, easing: 'cubic-bezier(.15,.8,.3,1)', fill: 'forwards' });
        }
    }

    function reset() {
        [scrim, env, card, inner, pocket, flap, seal, sealL, sealR].forEach(el => el.getAnimations().forEach(a => a.cancel()));
        flap.style.zIndex = '';
        sparks.replaceChildren();
        dialog.classList.remove('is-final', 'is-revealed');
        card.replaceChildren();
        card.style.transform = '';
    }

    function markOpened(i) {
        opened.add(i);
        bids[i].classList.add('is-opened');
        openedCount.textContent = String(opened.size);
        if (opened.size === bids.length) page.classList.add('is-complete');
    }

    async function openBid(i) {
        if (busy) return;
        busy = true;
        current = i;
        const bid = bids[i];
        reset();
        card.append(templates[i].content.cloneNode(true));
        dialog.showModal();

        if (reduced || typeof env.animate !== 'function') {
            [inner, pocket, flap, seal].forEach(el => { el.style.visibility = 'hidden'; });
            card.style.transform = 'translate(-50%, -50%)';
            dialog.classList.add('is-final', 'is-revealed');
            markOpened(i);
            closeButton.focus();
            busy = false;
            return;
        }
        [inner, pocket, flap, seal].forEach(el => { el.style.visibility = ''; });

        const envW = env.offsetWidth;
        const envH = env.offsetHeight;
        const fit = Math.min(envW * .82 / card.offsetWidth, envH * .78 / card.offsetHeight);
        const inside = `translate(-50%, calc(-50% + ${envH * .06}px)) scale(${fit})`;
        const risen = `translate(-50%, calc(-50% - ${envH * .62}px)) scale(${fit * 1.12}) rotate(-2deg)`;
        card.style.transform = inside;

        const origin = bid.querySelector('.env');
        const [ox, oy] = center(origin.getBoundingClientRect());
        const [fx, fy] = center(env.getBoundingClientRect());
        const scale = origin.offsetWidth / envW;
        bid.classList.add('is-flying');

        run(scrim, [{ opacity: 0 }, { opacity: 1 }], { duration: 500 });
        await run(env, [
            { transform: `translate(${ox - fx}px, ${oy - fy}px) scale(${scale}) rotate(${tiltOf(bid)}deg)` },
            { transform: 'translate(0, -5vh) scale(1.08) rotate(-3deg)', offset: .65 },
            { transform: 'none' }
        ], { duration: 850, easing: 'cubic-bezier(.3,.8,.3,1)' });

        await run(seal, [
            { transform: 'none' },
            { transform: 'rotate(-14deg) scale(1.1)', offset: .25 },
            { transform: 'rotate(12deg) scale(1.1)', offset: .5 },
            { transform: 'rotate(-7deg) scale(1.05)', offset: .75 },
            { transform: 'none' }
        ], { duration: 460 });
        burst();
        const fallOff = { duration: 760, easing: 'cubic-bezier(.4,0,.9,.6)' };
        run(sealL, [{ translate: '0 0', rotate: '0deg', opacity: 1 }, { translate: '-110% 220%', rotate: '-80deg', opacity: 0 }], fallOff);
        run(sealR, [{ translate: '0 0', rotate: '0deg', opacity: 1 }, { translate: '110% 250%', rotate: '95deg', opacity: 0 }], fallOff);
        await wait(170);

        const lift = run(flap, [{ transform: 'rotateX(0deg)' }, { transform: 'rotateX(180deg)' }], { duration: 620, easing: 'cubic-bezier(.5,0,.3,1)' });
        await wait(320);
        flap.style.zIndex = '1';
        await lift;

        await run(card, [{ transform: inside }, { transform: risen }], { duration: 720, easing: 'cubic-bezier(.3,.9,.3,1)' });

        dialog.classList.add('is-final');
        const drop = { duration: 820, easing: 'cubic-bezier(.5,0,.9,.5)' };
        [inner, pocket, flap].forEach((el, k) =>
            run(el, [{ translate: '0 0', rotate: '0deg', opacity: 1 }, { translate: '0 75vh', rotate: `${6 + k * 3}deg`, opacity: 0 }], drop));
        await run(card, [{ transform: risen }, { transform: 'translate(-50%, -50%) scale(1)' }], { duration: 950, easing: 'cubic-bezier(.34,1.4,.5,1)' });

        dialog.classList.add('is-revealed');
        markOpened(i);
        closeButton.focus();
        busy = false;
    }

    async function closeBid() {
        if (busy || current < 0) return;
        busy = true;
        const bid = bids[current];
        dialog.classList.remove('is-revealed');
        if (!reduced && typeof card.animate === 'function') {
            const origin = bid.querySelector('.env');
            const [ox, oy] = center(origin.getBoundingClientRect());
            const [fx, fy] = center(env.getBoundingClientRect());
            const scale = origin.offsetWidth / card.offsetWidth;
            run(scrim, [{ opacity: 1 }, { opacity: 0 }], { duration: 650 });
            await run(card, [
                { transform: 'translate(-50%, -50%) scale(1)', opacity: 1 },
                { transform: `translate(calc(-50% + ${ox - fx}px), calc(-50% + ${oy - fy}px)) scale(${scale}) rotate(${tiltOf(bid)}deg)`, opacity: 0 }
            ], { duration: 680, easing: 'cubic-bezier(.6,0,.3,1)' });
        }
        dialog.close();
        reset();
        bid.classList.remove('is-flying');
        if (!reduced) bid.animate([{ scale: 1.22 }, { scale: .94 }, { scale: 1 }], { duration: 520, easing: 'ease-out' });
        bid.focus({ preventScroll: true });
        current = -1;
        busy = false;
    }

    bids.forEach((bid, i) => bid.addEventListener('click', () => openBid(i)));
    closeButton.addEventListener('click', closeBid);
    scrim.addEventListener('click', closeBid);
    dialog.addEventListener('cancel', event => { event.preventDefault(); closeBid(); });

    deal();
})();
