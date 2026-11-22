(() => {
    'use strict';
    const form = document.getElementById('purchase-request-form');
    if (!form) return;
    const items = document.getElementById('request-items');
    const template = document.getElementById('request-item-template');
    const add = document.getElementById('add-request-item');
    const count = document.getElementById('items-count-badge');
    const status = document.getElementById('request-item-status');
    const summary = document.getElementById('request-errors');
    const errors = summary.querySelector('[data-error-list]');
    const undo = document.getElementById('item-undo');
    let removed = null;
    let errorUpdatePending = false;

    function renumber() {
        const rows = [...items.children];
        rows.forEach((row, index) => {
            row.querySelectorAll('[data-item-label]').forEach(label => { label.textContent = `Item ${index + 1}`; });
            row.querySelectorAll('[data-field]').forEach(field => {
                const property = field.dataset.field;
                const id = `Items_${index}__${property}`;
                const error = row.querySelector(`[data-error-for="${property}"]`);
                field.name = `Items[${index}].${property}`;
                field.id = id;
                row.querySelector(`[data-for="${property}"]`).htmlFor = id;
                error.id = `${id}-error`;
                error.dataset.valmsgFor = field.name;
                field.setAttribute('aria-describedby', error.id);
            });
            row.querySelectorAll('input[type="hidden"][name="__Invariant"]').forEach(field => {
                field.value = `Items[${index}].${field.value.split('.').pop()}`;
            });
            const remove = row.querySelector('[data-remove]');
            remove.disabled = rows.length === 1;
            remove.setAttribute('aria-label', `Remove item ${index + 1}`);
        });
        count.textContent = rows.length;
    }

    function errorElement(field) { return field.closest('.rq-field')?.querySelector('.rq-field-error'); }
    function message(field) {
        if (field.validity.valueMissing) return field.dataset.requiredMessage || 'Choose a value.';
        if (field.type === 'date' && field.validity.rangeUnderflow) return 'Choose today or a future date.';
        if (field.dataset.field === 'Quantity') return 'Enter a positive quantity with up to three decimal places.';
        return field.validationMessage;
    }
    function showFieldError(field) {
        const error = errorElement(field);
        if (!error) return;
        error.textContent = message(field);
        field.setAttribute('aria-invalid', 'true');
    }
    function updateSummary() {
        const invalid = [...form.querySelectorAll('input, select, textarea')].filter(field => field.willValidate && !field.validity.valid);
        errors.replaceChildren();
        invalid.forEach(field => {
            showFieldError(field);
            const entry = document.createElement('li');
            const link = document.createElement('a');
            const row = field.closest('.rq-item');
            link.href = `#${field.id}`;
            link.textContent = (row ? row.querySelector('[data-item-label]').textContent + ': ' : '') + message(field);
            entry.append(link); errors.append(entry);
        });
        summary.hidden = invalid.length === 0;
    }

    // Native validity also runs when the existing confirmation checks this form.
    form.addEventListener('invalid', event => {
        event.preventDefault(); showFieldError(event.target);
        if (errorUpdatePending) return;
        errorUpdatePending = true;
        queueMicrotask(() => { errorUpdatePending = false; updateSummary(); summary.focus(); });
    }, true);
    form.addEventListener('input', event => {
        const field = event.target;
        if (!field.matches('input, select, textarea')) return;
        if (field.getAttribute('aria-invalid') === 'true' || field.classList.contains('input-validation-error')) {
            if (field.validity.valid) {
                field.removeAttribute('aria-invalid'); field.classList.remove('input-validation-error');
                const error = errorElement(field); if (error) error.textContent = '';
            } else showFieldError(field);
            if (!summary.hidden) updateSummary();
        }
    });
    summary.addEventListener('click', event => {
        const link = event.target.closest('a[href^="#"]');
        if (!link) return;
        const field = document.getElementById(link.hash.slice(1));
        if (field) { event.preventDefault(); field.focus(); }
    });
    add.addEventListener('click', () => {
        items.append(template.content.cloneNode(true)); renumber();
        status.textContent = `Item added. ${items.children.length} items in this request.`;
        items.lastElementChild.querySelector('[data-field]').focus();
        if (!summary.hidden) updateSummary();
    });
    items.addEventListener('click', event => {
        const button = event.target.closest('[data-remove]');
        if (!button || items.children.length <= 1) return;
        const row = button.closest('.rq-item');
        removed = { row, next: row.nextElementSibling };
        const number = [...items.children].indexOf(row) + 1;
        const nextFocus = row.nextElementSibling || row.previousElementSibling;
        row.remove(); renumber(); undo.hidden = false;
        status.textContent = `Item ${number} removed. ${items.children.length} items remain. Undo is available.`;
        nextFocus.querySelector('[data-field]').focus();
        if (!summary.hidden) updateSummary();
    });
    document.getElementById('undo-remove-item').addEventListener('click', () => {
        if (!removed) return;
        const row = removed.row;
        items.insertBefore(row, removed.next?.parentNode === items ? removed.next : null);
        removed = null; undo.hidden = true; renumber();
        status.textContent = `Item restored. ${items.children.length} items in this request.`;
        row.querySelector('[data-field]').focus();
        if (!summary.hidden) updateSummary();
    });
    if (items.children.length === 0) items.append(template.content.cloneNode(true));
    renumber();
    form.querySelectorAll('.input-validation-error').forEach(field => field.setAttribute('aria-invalid', 'true'));
    if (!summary.hidden) summary.focus();
})();
