// Team spotlight: sliced photo transitions, scattered-letter names, tilt, magnetic thumbs and autoplay.
(() => {
    const page = document.querySelector('[data-spot]');
    if (!page) return;

    const texts = [...page.querySelectorAll('[data-spot-text]')];
    const photos = [...page.querySelectorAll('[data-spot-photo]')];
    const thumbs = [...page.querySelectorAll('[data-spot-thumb]')];
    const frame = page.querySelector('[data-spot-frame]');
    const count = page.querySelector('[data-spot-count]');
    const watermark = page.querySelector('[data-spot-watermark]');
    const toggle = page.querySelector('[data-spot-toggle]');
    const toggleLabel = page.querySelector('[data-spot-toggle-label]');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const finePointer = matchMedia('(hover: hover) and (pointer: fine)').matches;
    const total = texts.length;
    const SLICES = 7;
    let active = 0;
    let busy = false;
    let queued = null;

    const rand = (min, max) => min + Math.random() * (max - min);
    const pad = n => String(n + 1).padStart(2, '0');
    const firstName = i => texts[i].querySelector('h2').getAttribute('aria-label').split(' ')[0];

    function scatter(text, prefix, spread) {
        text.querySelectorAll('.spot-ch').forEach(ch => {
            ch.style.setProperty(`--${prefix}x`, `${rand(-spread, spread).toFixed(0)}px`);
            ch.style.setProperty(`--${prefix}y`, `${rand(-spread * .8, spread * .8).toFixed(0)}px`);
            ch.style.setProperty(`--${prefix}r`, `${rand(-80, 80).toFixed(0)}deg`);
            if (prefix === 'r') ch.style.setProperty('--rs', rand(.4, 1.8).toFixed(2));
        });
    }

    function showText(next, prev) {
        if (prev != null) {
            const old = texts[prev];
            scatter(old, 'l', 220);
            old.classList.remove('is-active');
            old.classList.add('is-leaving');
            old.inert = true;
            setTimeout(() => old.classList.remove('is-leaving'), 900);
        }
        const text = texts[next];
        scatter(text, 'r', 260);
        text.classList.remove('is-active');
        void text.offsetWidth;
        text.classList.add('is-active');
        text.inert = false;
    }

    function roll(element, value, direction) {
        if (reduced || typeof element.animate !== 'function') { element.textContent = value; return; }
        const out = element.animate([{ transform: 'none', opacity: 1 }, { transform: `translateY(${-100 * direction}%)`, opacity: 0 }],
            { duration: 220, easing: 'ease-in' });
        out.finished.then(() => {
            element.textContent = value;
            element.animate([{ transform: `translateY(${100 * direction}%)`, opacity: 0 }, { transform: 'none', opacity: 1 }],
                { duration: 420, easing: 'cubic-bezier(.34,1.5,.5,1)' });
        }).catch(() => {});
    }

    function swapWatermark(value) {
        if (reduced || typeof watermark.animate !== 'function') { watermark.textContent = value; return; }
        watermark.animate([{ opacity: 1, filter: 'blur(0)' }, { opacity: 0, filter: 'blur(12px)' }], { duration: 300, easing: 'ease-in' })
            .finished.then(() => {
                watermark.textContent = value;
                watermark.animate([{ opacity: 0, letterSpacing: '.3em' }, { opacity: 1, letterSpacing: '0em' }], { duration: 900, easing: 'cubic-bezier(.22,.8,.2,1)' });
            }).catch(() => {});
    }

    // Incoming photo arrives as vertical slices that shoot in from alternating ends and lock together.
    async function slicePhoto(next, prev, direction) {
        const incoming = photos[next];
        const outgoing = photos[prev];
        if (reduced || typeof frame.animate !== 'function') {
            outgoing.classList.remove('is-active');
            incoming.classList.add('is-active');
            return;
        }
        outgoing.classList.remove('is-active');
        outgoing.classList.add('is-prev');
        const width = 100 / SLICES;
        const slices = Array.from({ length: SLICES }, (_, i) => {
            const slice = incoming.cloneNode();
            slice.className = 'spot-slice';
            slice.removeAttribute('data-spot-photo');
            slice.style.clipPath = `inset(0 ${(100 - (i + 1) * width).toFixed(3)}% 0 ${(i * width).toFixed(3)}%)`;
            frame.insertBefore(slice, frame.querySelector('.spot-glare'));
            return slice;
        });
        const order = direction > 0 ? i => i : i => SLICES - 1 - i;
        const runs = slices.map((slice, i) => slice.animate([
            { translate: `0 ${i % 2 ? -110 : 110}%`, rotate: `${i % 2 ? 6 : -6}deg`, filter: 'brightness(1.8) saturate(0)' },
            { translate: '0 0', rotate: '0deg', filter: 'brightness(1) saturate(1)' }
        ], { duration: 820, delay: order(i) * 65, easing: 'cubic-bezier(.75,0,.15,1)', fill: 'backwards' }).finished);
        const fade = outgoing.animate([{ scale: 1, filter: 'brightness(1)' }, { scale: .85, filter: 'brightness(.3) blur(4px)' }],
            { duration: 820 + SLICES * 65, easing: 'ease-in', fill: 'forwards' });
        await Promise.all(runs).catch(() => {});
        incoming.classList.add('is-active');
        outgoing.classList.remove('is-prev');
        fade.cancel();
        slices.forEach(slice => slice.remove());
    }

    async function go(next, direction = next > active ? 1 : -1) {
        next = (next + total) % total;
        if (next === active) return;
        if (busy) { queued = [next, direction]; return; }
        busy = true;
        const prev = active;
        active = next;
        thumbs.forEach((thumb, i) => {
            thumb.classList.toggle('is-active', i === next);
            thumb.setAttribute('aria-current', String(i === next));
        });
        restartRing();
        showText(next, prev);
        roll(count, pad(next), direction);
        swapWatermark(firstName(next));
        await slicePhoto(next, prev, direction);
        busy = false;
        if (queued) { const [n, d] = queued; queued = null; go(n, d); }
    }

    // Autoplay is driven by the ring animation on the active thumb, so pausing the ring pauses the reel.
    function restartRing() {
        const circle = thumbs[active].querySelector('.spot-ring circle');
        circle.style.animation = 'none';
        void circle.getBoundingClientRect();
        circle.style.animation = '';
    }
    function setPaused(paused) {
        if (!paused) page.classList.add('is-autoplay');
        page.classList.toggle('is-paused', paused);
        toggle.setAttribute('aria-pressed', String(paused));
        toggleLabel.textContent = paused ? 'Play' : 'Pause';
    }
    page.addEventListener('animationend', event => {
        if (event.animationName === 'spot-ring') go(active + 1, 1);
    });
    toggle.addEventListener('click', () => setPaused(!page.classList.contains('is-paused')));
    const hold = on => page.classList.toggle('is-holding', on);
    page.querySelector('[data-spot-stage]').addEventListener('pointerenter', () => hold(true));
    page.querySelector('[data-spot-stage]').addEventListener('pointerleave', () => hold(false));
    page.addEventListener('focusin', () => hold(true));
    page.addEventListener('focusout', event => { if (!page.contains(event.relatedTarget)) hold(false); });

    page.querySelector('[data-spot-prev]').addEventListener('click', () => go(active - 1, -1));
    page.querySelector('[data-spot-next]').addEventListener('click', () => go(active + 1, 1));
    thumbs.forEach((thumb, i) => thumb.addEventListener('click', () => go(i)));
    page.addEventListener('keydown', event => {
        if (event.target.closest('a')) return;
        if (event.key === 'ArrowRight') { event.preventDefault(); go(active + 1, 1); }
        if (event.key === 'ArrowLeft') { event.preventDefault(); go(active - 1, -1); }
    });

    // Pointer tilt and glare on the photo, magnetic pull on thumbs.
    if (finePointer && !reduced) {
        frame.addEventListener('pointermove', event => {
            const box = frame.getBoundingClientRect();
            frame.style.setProperty('--tx', (((event.clientX - box.left) / box.width) * 2 - 1).toFixed(3));
            frame.style.setProperty('--ty', (((event.clientY - box.top) / box.height) * 2 - 1).toFixed(3));
            frame.classList.add('is-tilting');
        });
        frame.addEventListener('pointerleave', () => {
            frame.style.setProperty('--tx', 0);
            frame.style.setProperty('--ty', 0);
            frame.classList.remove('is-tilting');
        });
        thumbs.forEach(thumb => {
            thumb.addEventListener('pointermove', event => {
                const box = thumb.getBoundingClientRect();
                thumb.style.setProperty('--mx', `${((event.clientX - box.left - box.width / 2) * .35).toFixed(1)}px`);
                thumb.style.setProperty('--my', `${((event.clientY - box.top - box.height / 2) * .35).toFixed(1)}px`);
            });
            thumb.addEventListener('pointerleave', () => {
                thumb.style.setProperty('--mx', '0px');
                thumb.style.setProperty('--my', '0px');
            });
        });
    }

    texts.forEach((text, i) => { text.inert = i !== active; });
    if (reduced) {
        setPaused(true);
    } else {
        page.classList.add('is-autoplay');
        showText(active, null);
    }
})();
