(() => {
    const page = document.querySelector('[data-team]');
    if (!page) return;

    const pack = page.querySelector('[data-team-pack]');
    const stage = pack.parentElement;
    const mouth = page.querySelector('.team-carton-mouth');
    const carton = mouth.parentElement;
    const flap = mouth.querySelector('.team-carton-flap');
    const capsules = [...pack.querySelectorAll('[data-member]')];
    const dialog = page.querySelector('#team-profile');
    const profile = dialog.querySelector('.team-profile');
    const content = dialog.querySelector('[data-team-profile]');
    const closeButton = dialog.querySelector('[data-team-close]');
    const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
    const finePointer = matchMedia('(hover: hover) and (pointer: fine)');
    let selected;
    let transitionInProgress = false;
    let closeRequested = false;
    let pointerOnBackdrop = false;
    let lightingFrame = 0;
    let visit = 0;

    function watchPortrait(image) {
        const showFallback = () => { image.hidden = true; };
        image.addEventListener('error', showFallback, { once: true });
        if (image.complete && image.naturalWidth === 0) showFallback();
    }

    function markDiscovered(capsule) {
        capsule.dataset.discovered = '';
        capsule.setAttribute('aria-description', 'Profile already discovered. Open again.');
    }

    function animate(element, frames, options) {
        return element.animate(frames, options).finished.catch(() => {});
    }

    async function openCarton() {
        const currentVisit = visit;
        if (reducedMotion.matches) {
            flap.classList.add('is-open');
            return;
        }

        const opening = mouth.getBoundingClientRect();
        const landing = stage.getBoundingClientRect();
        const fromBelow = opening.top > landing.top + landing.height / 2;
        const anchorX = fromBelow ? landing.left + landing.width / 2 : landing.left;
        const anchorY = fromBelow ? landing.bottom : landing.top + landing.height / 2;
        const x = opening.left + opening.width / 2 - anchorX;
        const y = opening.top + opening.height / 2 - anchorY;
        const closed = fromBelow ? 'inset(100% 0 0 0)' : 'inset(0 100% 0 0)';
        const emerging = fromBelow ? 'inset(28% 0 0 0)' : 'inset(0 26% 0 0)';
        const clear = 'inset(-5rem -5rem -6rem -5rem)';
        stage.style.transformOrigin = fromBelow ? 'center bottom' : 'left center';
        stage.inert = true;

        try {
            await Promise.all([
                animate(carton, [
                    { translate: '0 0' },
                    { translate: '-3px -9px', offset: .46 },
                    { translate: '0 0' }
                ], { duration: 990, easing: 'cubic-bezier(.24,.7,.28,1)' }),
                animate(flap, [
                    { transform: 'scaleX(1)' },
                    { transform: 'scaleX(.16)', offset: .76 },
                    { transform: 'scaleX(.27)' }
                ], { duration: 610, delay: 340, easing: 'ease-in-out', fill: 'backwards' })
                    .then(() => flap.classList.add('is-open')),
                animate(stage, [
                    { opacity: .25, transform: `translate(${x}px, ${y}px) scale(.26) rotate(-8deg)`, clipPath: closed },
                    { opacity: 1, transform: `translate(${x * .64}px, ${y * .64}px) scale(.57) rotate(-5deg)`, clipPath: emerging, offset: .41 },
                    { opacity: 1, transform: 'translateY(-9px) scale(1.015) rotate(.8deg)', clipPath: clear, offset: .83 },
                    { opacity: 1, transform: 'none', clipPath: clear }
                ], { duration: 1860, delay: 740, easing: 'cubic-bezier(.35,.02,.28,1)', fill: 'backwards' })
            ]);
            if (currentVisit === visit) stage.classList.add('is-revealed');
        } finally {
            stage.inert = false;
            stage.style.removeProperty('transform-origin');
        }
    }

    async function movePortrait(source, destination, reverse = false) {
        const start = source.getBoundingClientRect();
        const end = destination.getBoundingClientRect();
        if (!start.width || !end.width) return;

        const image = source.querySelector('img');
        const overlay = image.cloneNode();
        overlay.className = 'team-flying-portrait';
        overlay.alt = '';
        overlay.setAttribute('aria-hidden', 'true');
        Object.assign(overlay.style, {
            left: `${end.left}px`, top: `${end.top}px`, width: `${end.width}px`, height: `${end.height}px`,
            objectPosition: getComputedStyle(image).objectPosition
        });
        if (image.hidden || !image.complete || !image.naturalWidth) return;

        const startFrame = {
            transform: `translate(${start.left - end.left}px, ${start.top - end.top}px) scale(${start.width / end.width}, ${start.height / end.height})`,
            borderRadius: reverse ? '1rem' : '50%',
            opacity: reverse ? 1 : 0
        };
        const endFrame = { transform: 'none', borderRadius: reverse ? '50%' : '1rem', opacity: reverse ? 0 : 1 };
        dialog.append(overlay);
        destination.style.visibility = 'hidden';
        if (reverse) source.style.visibility = 'hidden';
        try {
            await animate(overlay, [startFrame, endFrame], { duration: reverse ? 320 : 440, easing: 'cubic-bezier(.2,.75,.2,1)', fill: 'both' });
        } finally {
            overlay.remove();
            destination.style.visibility = '';
            source.style.visibility = '';
        }
    }

    async function openMember(capsule) {
        if (selected || transitionInProgress) return;
        const currentVisit = visit;
        transitionInProgress = true;
        selected = capsule;
        capsule.setAttribute('aria-expanded', 'true');
        const source = capsule.querySelector('.team-capsule-portrait');
        if (!reducedMotion.matches && !capsule.hasAttribute('data-discovered')) {
            capsule.classList.add('is-pressed');
            try {
                await animate(source, [
                    { transform: 'none', filter: 'brightness(1)' },
                    { transform: 'translateY(3px) scale(.86)', filter: 'brightness(.86)', offset: .33 },
                    { opacity: 1, transform: 'translateY(-9px) scale(1.08)', filter: 'brightness(1.12)', offset: .72 },
                    { opacity: 0, transform: 'translateY(-19px) scale(1.13)', filter: 'brightness(1.1)' }
                ], { duration: 470, easing: 'cubic-bezier(.24,.72,.26,1)' });
            } finally {
                capsule.classList.remove('is-pressed');
            }
        }

        if (currentVisit !== visit) return;
        const template = document.getElementById(`team-member-${capsule.dataset.member}`);
        content.replaceChildren(template.content.cloneNode(true));
        const destination = content.querySelector('.team-profile-portrait');
        watchPortrait(destination.querySelector('img'));
        dialog.showModal();
        markDiscovered(capsule);
        if (reducedMotion.matches) {
            dialog.classList.add('is-visible');
        } else {
            await new Promise(resolve => requestAnimationFrame(resolve));
            if (currentVisit !== visit) return;
            dialog.classList.add('is-visible');
            const motions = [movePortrait(source, destination)];
            content.querySelectorAll('.team-profile-copy > *').forEach((part, index) => {
                motions.push(animate(part, [{ opacity: 0, transform: 'translateY(7px)' }, { opacity: 1, transform: 'none' }],
                    { duration: 260, delay: 105 + index * 48, easing: 'ease-out', fill: 'backwards' }));
            });
            await Promise.all(motions);
        }
        if (currentVisit !== visit) return;
        transitionInProgress = false;
        if (closeRequested) void closeMember();
    }

    async function closeMember() {
        if (transitionInProgress) { closeRequested = dialog.open; return; }
        if (!dialog.open) return;
        closeRequested = false;
        transitionInProgress = true;
        if (!reducedMotion.matches) {
            const movement = movePortrait(content.querySelector('.team-profile-portrait'), selected.querySelector('.team-capsule-portrait'), true);
            await Promise.all([movement, animate(profile, [{ opacity: 1 }, { opacity: 0 }], { duration: 250, fill: 'forwards' })]);
            profile.getAnimations().forEach(animation => animation.cancel());
        }
        dialog.close();
    }

    capsules.forEach(capsule => {
        watchPortrait(capsule.querySelector('img'));
        capsule.addEventListener('click', () => void openMember(capsule));
    });
    closeButton.addEventListener('click', () => void closeMember());
    dialog.addEventListener('cancel', event => { event.preventDefault(); void closeMember(); });
    dialog.addEventListener('pointerdown', event => { pointerOnBackdrop = event.target === dialog; });
    dialog.addEventListener('click', event => {
        if (pointerOnBackdrop && event.target === dialog) void closeMember();
        pointerOnBackdrop = false;
    });
    dialog.addEventListener('close', () => {
        dialog.classList.remove('is-visible');
        selected?.removeAttribute('aria-expanded');
        selected?.focus({ preventScroll: true });
        selected = undefined;
        transitionInProgress = false;
    });

    function resetLighting() {
        cancelAnimationFrame(lightingFrame);
        lightingFrame = 0;
        ['--pack-x', '--pack-y', '--pack-tilt-x', '--pack-tilt-y'].forEach(property => pack.style.removeProperty(property));
    }

    stage.addEventListener('pointermove', event => {
        if (lightingFrame || !finePointer.matches || reducedMotion.matches || selected || stage.inert) return;
        lightingFrame = requestAnimationFrame(() => {
            lightingFrame = 0;
            const bounds = stage.getBoundingClientRect();
            const x = (event.clientX - bounds.left) / bounds.width;
            const y = (event.clientY - bounds.top) / bounds.height;
            pack.style.setProperty('--pack-x', `${x * 100}%`);
            pack.style.setProperty('--pack-y', `${y * 100}%`);
            pack.style.setProperty('--pack-tilt-x', `${(0.5 - y) * 4}deg`);
            pack.style.setProperty('--pack-tilt-y', `${(x - 0.5) * 4}deg`);
        });
    });
    stage.addEventListener('pointerleave', resetLighting);
    reducedMotion.addEventListener('change', resetLighting);

    function resetVisit() {
        visit++;
        selected = undefined;
        page.getAnimations({ subtree: true }).forEach(animation => animation.cancel());
        if (dialog.open) dialog.close();
        dialog.classList.remove('is-visible');
        transitionInProgress = false;
        closeRequested = false;
        pointerOnBackdrop = false;
        stage.inert = false;
        stage.classList.remove('is-revealed');
        flap.classList.remove('is-open');
        capsules.forEach(capsule => {
            delete capsule.dataset.discovered;
            capsule.classList.remove('is-pressed');
            capsule.removeAttribute('aria-expanded');
            capsule.removeAttribute('aria-description');
            capsule.querySelector('.team-capsule-portrait').style.visibility = '';
        });
        content.replaceChildren();
        resetLighting();
    }
    window.addEventListener('pagehide', resetVisit);
    window.addEventListener('pageshow', event => {
        if (event.persisted) { resetVisit(); void openCarton(); }
    });

    void openCarton();
})();
