// Supplier registration form: splits it into steps with next/previous buttons.
(() => {
    'use strict';
    const form = document.querySelector('.supplier-registration-form');
    if (!form) return;
    const steps = [...form.querySelectorAll('[data-registration-step]')];
    const markers = [...document.querySelectorAll('.registration-stepper li')];
    const previous = form.querySelector('.registration-previous');
    const next = form.querySelector('.registration-continue');
    const submit = form.querySelector('.registration-submit');
    const progress = document.querySelector('.registration-progress');
    let current = 0;
    form.noValidate = true;
    const validator = () => window.jQuery?.(form).data('validator');
    function show(index, focus = false) {
        current = index;
        steps.forEach((step, i) => { step.hidden = i !== index; });
        markers.forEach((marker, i) => {
            if (i === index) marker.setAttribute('aria-current', 'step');
            else marker.removeAttribute('aria-current');
            marker.classList.toggle('is-complete', i < index);
        });
        progress.textContent = `Step ${index + 1} of 3 · ${steps[index].dataset.registrationStep}`;
        previous.hidden = index === 0;
        next.hidden = index === 2;
        submit.hidden = index !== 2;
        if (focus) {
            const heading = steps[index].querySelector('legend, h2');
            heading.tabIndex = -1;
            heading.focus();
        }
    }
    function validate(index) {
        let firstInvalid;
        steps[index].querySelectorAll('input,select,textarea').forEach(field => {
            if (field.disabled || field.type === 'hidden' || field.closest('[hidden]')) return;
            const valid = field.checkValidity() && (!validator() || validator().element(field));
            if (!valid && !firstInvalid) firstInvalid = field;
        });
        if (firstInvalid) {
            firstInvalid.focus();
            firstInvalid.reportValidity();
            return false;
        }
        return true;
    }
    const summary = document.createElement('dl');
    summary.className = 'registration-summary';
    steps[2].prepend(summary);
    function updateSummary() {
        summary.replaceChildren();
        [['Company', 'CompanyName'], ['Contact', 'ContactName'], ['Email', 'Email']].forEach(([label, name]) => {
            const term = document.createElement('dt'); term.textContent = label;
            const value = document.createElement('dd'); value.textContent = form.elements[name].value;
            summary.append(term, value);
        });
    }
    form.tiryaqValidate = () => {
        for (let index = 0; index < steps.length; index++) {
            show(index);
            if (!validate(index)) return false;
        }
        show(2);
        updateSummary();
        return true;
    };
    // Resolve inline errors while editing, before a later blur can move the navigation button under the pointer.
    form.querySelectorAll('input:not([type="file"]):not([type="hidden"]), select, textarea').forEach(field => {
        const validateField = () => { if (!field.closest('[hidden]')) validator()?.element(field); };
        field.addEventListener('input', validateField);
        field.addEventListener('change', validateField);
    });
    next.addEventListener('click', () => { if (validate(current)) { show(current + 1, true); updateSummary(); } });
    previous.addEventListener('click', () => show(current - 1, true));
    form.addEventListener('keydown', event => {
        if (event.key === 'Enter' && current < 2 && event.target.matches('input:not([type="file"])')) {
            event.preventDefault();
            next.click();
        }
    });
    // Run before the shared document-level confirmation handler, so it only sees a complete application.
    window.addEventListener('submit', event => {
        if (event.target !== form) return;
        if (current < 2) {
            event.preventDefault(); event.stopImmediatePropagation(); next.click(); return;
        }
        if (!form.tiryaqValidate()) {
            event.preventDefault(); event.stopImmediatePropagation();
        }
    }, true);
    form.addEventListener('invalid', event => {
        const index = steps.findIndex(step => step.contains(event.target));
        if (index >= 0 && index !== current) show(index);
    }, true);
    form.querySelectorAll('[data-password-toggle]').forEach(button => {
        button.addEventListener('click', () => {
            const field = document.getElementById(button.dataset.passwordToggle);
            const visible = field.type === 'password';
            field.type = visible ? 'text' : 'password';
            button.setAttribute('aria-pressed', String(visible));
            button.setAttribute('aria-label', visible ? 'Hide password' : 'Show password');
            button.title = visible ? 'Hide password' : 'Show password';
            field.focus({ preventScroll: true });
        });
    });
    const documents = document.getElementById('officialDocuments');
    const output = form.querySelector('.document-file-list');
    documents.addEventListener('change', () => {
        const files = [...documents.files];
        const invalid = files.some(file => file.size === 0 || file.size > 10000000 || !/\.(pdf|docx|jpe?g|png)$/i.test(file.name));
        documents.setCustomValidity(invalid ? 'Choose PDF, DOCX, JPG or PNG files, up to 10 MB each. Empty files are not accepted.' : '');
        output.textContent = invalid ? documents.validationMessage : files.length ? files.map(file => file.name).join(' · ') : 'PDF, DOCX, JPG or PNG · Maximum 10 MB per file';
        output.classList.toggle('has-files', files.length > 0 && !invalid);
        output.classList.toggle('text-danger', invalid);
    });
    // Server validation is authoritative. Open the first section with a field error after a failed POST.
    const failed = steps.findIndex(step => step.querySelector('.input-validation-error, .field-validation-error'));
    show(failed >= 0 ? failed : 0);
    if (failed >= 0) form.querySelector('.registration-validation')?.focus();
})();
