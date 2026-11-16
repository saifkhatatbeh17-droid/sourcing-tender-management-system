(() => {
    'use strict';
    const successMessage = document.querySelector('.contact-panel .alert-success[role="status"]');
    if (successMessage) window.setTimeout(() => successMessage.remove(), 3000);
    const form = document.getElementById('contact-form');
    const preview = document.querySelector('.contact-preview');
    if (!form || !preview) return;
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const bindings = [];
    let sending = false, ready = false, sendTimer;
    const submit = form.querySelector('[type="submit"]');
    const status = preview.querySelector('.contact-send-status');
    const signature = preview.querySelector('[data-note-signature]');
    const inkPaths = [
        'M4 14c8-12 6 9 13 0s6 6 13-1 8 7 15-1 7 6 15-1 7 6 16 0',
        'M4 14c7-6 9 6 16 0s8 5 17-1 7 6 16 0 9 5 18-1 10 5 19-1 9 6 18 0',
        'M4 14c7-9 9 6 17-1s8 8 16 0 9 7 17-1 8 8 18-1 8 8 18 0 9 7 19-1 9 7 20 0 8 6 17-1',
        'M4 14c6-8 8 7 15 0s8 6 17 0 7 7 16-1 8 8 17 0 8 6 17-1 9 6 18 0 9 6 18-1 9 6 18 0 8 5 16 0 9 5 17-1'
    ];
    const svgNS = 'http://www.w3.org/2000/svg';
    preview.querySelectorAll('[data-note-for]').forEach((note, index) => {
        const field = document.getElementById(note.dataset.noteFor);
        if (!field) return;
        const row = note.closest('[data-note-row]');
        const placeholder = document.createElement('span');
        placeholder.className = 'contact-ink-placeholder';
        placeholder.textContent = note.dataset.placeholder;
        const svg = document.createElementNS(svgNS, 'svg');
        const message = field.id === 'contact-message';
        svg.setAttribute('viewBox', message ? '0 0 200 85' : '0 0 200 24');
        svg.setAttribute('aria-hidden', 'true');
        svg.classList.add('contact-live-ink');
        const paths = Array.from({ length: message ? 3 : 1 }, (_, line) => {
            const path = document.createElementNS(svgNS, 'path');
            path.setAttribute('d', inkPaths[message ? 3 : index]);
            path.setAttribute('pathLength', '1');
            if (line) path.setAttribute('transform', `translate(0 ${line * 28})`);
            svg.append(path);
            return path;
        });
        note.replaceChildren(placeholder, svg);
        const sync = () => {
            const length = Array.from(field.value.trim()).length;
            const capacity = message ? 55 : [26, 32, 45][index];
            paths.forEach((path, line) => {
                const progress = Math.max(0, Math.min(1, (length - line * capacity) / capacity));
                path.style.strokeDashoffset = String(1 - progress);
            });
            placeholder.hidden = length > 0;
            svg.classList.toggle('has-ink', length > 0);
            note.classList.toggle('is-placeholder', !length);
            if (field.id === 'contact-name') signature.textContent = length ? '~~~~ ~~~~~' : 'Your signature';
        };
        bindings.push(sync);
        field.addEventListener('input', sync);
        field.addEventListener('focus', () => row.classList.add('is-active'));
        field.addEventListener('blur', () => row.classList.remove('is-active'));
        sync();
    });
    form.addEventListener('reset', () => requestAnimationFrame(() => bindings.forEach(sync => sync(true))));
    form.addEventListener('submit', event => {
        if (ready) return;
        event.preventDefault();
        if (sending || !form.reportValidity()) return;
        if (window.jQuery && !window.jQuery(form).valid()) return;
        sending = true;
        bindings.forEach(sync => sync(true));
        form.inert = true;
        submit.disabled = true;
        preview.classList.add('is-signing');
        status.textContent = 'Signing your note and sending…';
        if (window.innerWidth < 701 && !reducedMotion.matches) preview.scrollIntoView({ behavior:'smooth', block:'center' });
        sendTimer = window.setTimeout(() => {
            ready = true; form.inert = false; submit.disabled = false;
            form.requestSubmit(submit);
            if (!form.checkValidity()) { ready = false; sending = false; preview.classList.remove('is-signing'); status.textContent = ''; }
        }, reducedMotion.matches ? 0 : 1650);
    });
    window.addEventListener('pageshow', () => {
        clearTimeout(sendTimer); sending = false; ready = false;
        form.inert = false; submit.disabled = false;
        preview.classList.remove('is-signing'); status.textContent = '';
        bindings.forEach(sync => sync(true));
    });
})();
