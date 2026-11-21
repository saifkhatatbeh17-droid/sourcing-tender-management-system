// Supplier award popup: envelope drops in, the supplier breaks the seal, the letter slides out with confetti.
(() => {
    const dialog = document.querySelector('[data-award]');
    if (!dialog || typeof dialog.showModal !== 'function') return;

    const env = dialog.querySelector('[data-aw-env]');
    const letter = dialog.querySelector('[data-aw-letter]');
    const inner = env.querySelector('.aw-inner');
    const pocket = env.querySelector('.aw-pocket');
    const flap = env.querySelector('.aw-flap');
    const seal = env.querySelector('.aw-seal');
    const halves = [...seal.querySelectorAll('.aw-half')];
    const sparks = seal.querySelector('[data-aw-sparks]');
    const openButton = dialog.querySelector('[data-aw-open]');
    const confetti = dialog.querySelector('[data-aw-confetti]');
    const dismissForm = dialog.querySelector('[data-aw-dismiss]');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches || typeof env.animate !== 'function';
    let opened = false;
    let closing = false;

    const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
    const run = (element, frames, options) =>
        element.animate(frames, { fill: 'forwards', ...options }).finished.catch(() => {});

    function burstSparks() {
        for (let k = 0; k < 20; k++) {
            const spark = document.createElement('i');
            sparks.append(spark);
            const angle = (k / 20) * Math.PI * 2 + Math.random() * .4;
            const distance = 80 + Math.random() * 120;
            spark.animate([
                { transform: 'translate(0, 0) scale(1.3)', opacity: 1 },
                { transform: `translate(${Math.cos(angle) * distance}px, ${Math.sin(angle) * distance + 50}px) scale(0)`, opacity: 0 }
            ], { duration: 700 + Math.random() * 500, easing: 'cubic-bezier(.15,.8,.3,1)', fill: 'forwards' });
        }
    }

    function throwConfetti() {
        const colours = ['#efb85c', '#f4eee2', '#2c8a8f', '#e7765a', '#ffd98a'];
        const height = window.innerHeight;
        for (let k = 0; k < 90; k++) {
            const piece = document.createElement('i');
            const wide = Math.random() > .35;
            piece.style.width = `${wide ? 8 : 9}px`;
            piece.style.height = `${wide ? 14 : 9}px`;
            piece.style.borderRadius = wide ? '2px' : '50%';
            piece.style.background = colours[k % colours.length];
            confetti.append(piece);
            const side = k % 2 ? 1 : -1;
            const vx = side * (80 + Math.random() * Math.min(window.innerWidth * .45, 520));
            const vy = -(height * .25 + Math.random() * height * .35);
            const spin = (Math.random() * 900 + 300) * side;
            piece.animate([
                { transform: 'translate(0, 0) rotate(0deg) rotateX(0deg)', opacity: 1 },
                { transform: `translate(${vx * .8}px, ${vy}px) rotate(${spin * .4}deg) rotateX(360deg)`, opacity: 1, offset: .3 },
                { transform: `translate(${vx}px, ${height * .75}px) rotate(${spin}deg) rotateX(1080deg)`, opacity: 0 }
            ], { duration: 2400 + Math.random() * 1600, delay: Math.random() * 180, easing: 'cubic-bezier(.2,.6,.4,1)', fill: 'forwards' })
                .finished.then(() => piece.remove()).catch(() => {});
        }
    }

    async function arrive() {
        dialog.showModal();
        if (reduced) { openLetter(); return; }
        await run(env, [
            { transform: 'translateY(-110vh) rotate(-28deg) scale(.8)' },
            { transform: 'translateY(4vh) rotate(6deg) scale(1.02)', offset: .6 },
            { transform: 'translateY(-1.5vh) rotate(-2deg)', offset: .8 },
            { transform: 'none' }
        ], { duration: 1100, easing: 'cubic-bezier(.3,.8,.35,1)' });
        dialog.classList.add('is-waiting');
        openButton.focus({ preventScroll: true });
        // A small nudge every few seconds while it waits to be opened.
        (async function nudge() {
            while (!opened) {
                await wait(2600);
                if (opened) break;
                env.animate([
                    { rotate: '0deg' }, { rotate: '-3deg' }, { rotate: '3deg' }, { rotate: '-1.5deg' }, { rotate: '0deg' }
                ], { duration: 650, easing: 'ease-in-out' });
            }
        })();
    }

    async function openLetter() {
        if (opened) return;
        opened = true;
        dialog.classList.remove('is-waiting');
        dialog.classList.add('is-opening');

        if (reduced) {
            [inner, pocket, flap, seal].forEach(el => { el.style.visibility = 'hidden'; });
            letter.style.transform = 'translate(-50%, -50%)';
            dialog.classList.add('is-final', 'is-revealed');
            letter.querySelector('.aw-primary').focus();
            return;
        }

        const envW = env.offsetWidth;
        const envH = env.offsetHeight;
        const fit = Math.min(envW * .84 / letter.offsetWidth, envH * .8 / letter.offsetHeight);
        const inside = `translate(-50%, calc(-50% + ${envH * .06}px)) scale(${fit})`;
        const risen = `translate(-50%, calc(-50% - ${envH * .6}px)) scale(${fit * 1.12}) rotate(-2deg)`;
        letter.style.transform = inside;

        await run(seal, [
            { transform: 'none' },
            { transform: 'rotate(-15deg) scale(1.12)', offset: .25 },
            { transform: 'rotate(13deg) scale(1.12)', offset: .5 },
            { transform: 'rotate(-7deg) scale(1.05)', offset: .75 },
            { transform: 'none' }
        ], { duration: 460 });
        burstSparks();
        const fall = { duration: 800, easing: 'cubic-bezier(.4,0,.9,.6)' };
        run(halves[0], [{ translate: '0 0', rotate: '0deg', opacity: 1 }, { translate: '-120% 240%', rotate: '-85deg', opacity: 0 }], fall);
        run(halves[1], [{ translate: '0 0', rotate: '0deg', opacity: 1 }, { translate: '120% 270%', rotate: '100deg', opacity: 0 }], fall);
        await wait(170);

        const lift = run(flap, [{ transform: 'rotateX(0deg)' }, { transform: 'rotateX(180deg)' }], { duration: 620, easing: 'cubic-bezier(.5,0,.3,1)' });
        await wait(320);
        flap.style.zIndex = '1';
        await lift;

        await run(letter, [{ transform: inside }, { transform: risen }], { duration: 720, easing: 'cubic-bezier(.3,.9,.3,1)' });

        dialog.classList.add('is-final');
        throwConfetti();
        [inner, pocket, flap].forEach((el, k) =>
            run(el, [{ translate: '0 0', rotate: '0deg', opacity: 1 }, { translate: '0 80vh', rotate: `${6 + k * 3}deg`, opacity: 0 }],
                { duration: 850, easing: 'cubic-bezier(.5,0,.9,.5)' }));
        await run(letter, [{ transform: risen }, { transform: 'translate(-50%, -50%) scale(1)' }], { duration: 950, easing: 'cubic-bezier(.34,1.4,.5,1)' });
        dialog.classList.add('is-revealed');
        letter.querySelector('.aw-primary').focus({ preventScroll: true });
    }

    // Close marks the notification read so the popup does not come back.
    async function dismiss() {
        if (closing) return;
        closing = true;
        const request = fetch(dismissForm.action, {
            method: 'POST',
            body: new FormData(dismissForm),
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        }).catch(() => {});
        if (!reduced) {
            const scene = [...dialog.querySelectorAll('.aw-scrim, .aw-rays')];
            scene.forEach(el => run(el, [{ opacity: getComputedStyle(el).opacity }, { opacity: 0 }], { duration: 600 }));
            await run(letter, [
                { transform: getComputedStyle(letter).transform, opacity: 1 },
                { transform: 'translate(-50%, -30%) scale(.85) rotate(4deg)', opacity: 0 }
            ], { duration: 520, easing: 'cubic-bezier(.6,0,.8,.4)' });
        }
        await request;
        dialog.close();
        dialog.remove();
    }

    openButton.addEventListener('click', openLetter);
    dismissForm.addEventListener('submit', event => { event.preventDefault(); dismiss(); });
    dialog.addEventListener('cancel', event => {
        event.preventDefault();
        if (opened) dismiss(); else openLetter();
    });

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => setTimeout(arrive, 450));
    else setTimeout(arrive, 450);
})();
