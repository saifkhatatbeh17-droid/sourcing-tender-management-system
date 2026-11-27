// Public Contact page: shows a live preview of the message and the sending status.
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
    preview.querySelectorAll('[data-note-for]').forEach(note => {
        const field = form.querySelector('#' + note.dataset.noteFor);
        const row = note.closest('[data-note-row]');
        if (!field || !row) return;

        const sync = () => {
            const value = field.value;
            const empty = !value.trim();
            note.textContent = empty ? note.dataset.placeholder : value;
            note.classList.toggle('is-placeholder', empty);
            if (field.id === 'contact-name') {
                signature.textContent = empty ? 'Your signature' : value.trim();
            }
        };
        bindings.push(sync);
        field.addEventListener('input', () => {
            sync();
            if (field.id === 'contact-message') row.scrollTop = row.scrollHeight;
        });
        field.addEventListener('change', sync);
        field.addEventListener('focus', () => {
            sync();
            row.classList.add('is-active');
        });
        field.addEventListener('blur', () => {
            sync();
            row.classList.remove('is-active');
        });
        field.addEventListener('animationstart', event => {
            if (event.animationName === 'contact-autofill') sync();
        });
        sync();
    });
    form.addEventListener('reset', () => requestAnimationFrame(() => bindings.forEach(sync => sync())));
    form.addEventListener('submit', event => {
        if (ready) return;
        event.preventDefault();
        if (sending || !form.reportValidity()) return;
        if (window.jQuery && !window.jQuery(form).valid()) return;
        sending = true;
        bindings.forEach(sync => sync());
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
        bindings.forEach(sync => sync());
    });
})();
