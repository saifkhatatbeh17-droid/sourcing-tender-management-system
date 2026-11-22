(() => {
    'use strict';
    document.querySelectorAll('[data-guarantee-editor]').forEach(editor => {
        const toggle = editor.querySelector('[data-guarantee-toggle]');
        const fields = editor.querySelector('[data-guarantee-fields]');
        const start = editor.querySelector('[data-guarantee-start]');
        const end = editor.querySelector('[data-guarantee-end]');
        const documentInput = editor.querySelector('[data-guarantee-document]');
        const duration = editor.querySelector('[data-guarantee-duration]');
        const update = () => {
            fields.hidden = !toggle.checked;
            fields.querySelectorAll('input,select,textarea').forEach(input => {
                input.disabled = !toggle.checked;
                input.required = toggle.checked && input.hasAttribute('data-guarantee-required');
            });
            documentInput.required = toggle.checked && !documentInput.dataset.existingFile;
            const days = Math.round((Date.parse(end.value) - Date.parse(start.value)) / 86400000) + 1;
            end.setCustomValidity(Number.isFinite(days) && days < 1 ? 'Expiry must be on or after the start date.' : '');
            duration.textContent = Number.isFinite(days) && days > 0
                ? days + ' days · ' + start.value + ' to ' + end.value
                : 'Set the validity period required for this product or service.';
        };
        toggle.addEventListener('change', update);
        start.addEventListener('input', update);
        end.addEventListener('input', update);
        update();
    });
})();
