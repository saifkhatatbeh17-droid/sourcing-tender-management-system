/* Supplier-only enhancements; routes and validation remain server controlled. */
(() => {
    'use strict';
    document.querySelectorAll('[data-supplier-search]').forEach(input => {
        const records = [...document.querySelectorAll(input.dataset.supplierSearch)];
        const status = input.parentElement.querySelector('[data-supplier-search-status]');
        const update = () => {
            const query = input.value.trim().toLocaleLowerCase();
            let shown = 0;
            records.forEach(record => {
                record.hidden = !record.textContent.toLocaleLowerCase().includes(query);
                if (!record.hidden) shown++;
            });
            status.textContent = query ? (shown ? `${shown} of ${records.length} results shown.` : 'No matches. Try a different search.') : `${records.length} results. Search this list.`;
        };
        input.addEventListener('input', update);
        update();
    });
    const form = document.querySelector('[data-offer-form]');
    if (form) {
        const currency = form.querySelector('#Currency');
        const format = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const update = () => {
            let sum = 0;
            form.querySelectorAll('.supplier-price-item').forEach(item => {
                const value = Number(item.querySelector('.line-quantity').value) * Number(item.querySelector('.line-price').value);
                const amount = Number.isFinite(value) ? value : 0;
                sum += amount;
                item.querySelector('.line-total').textContent = format.format(amount);
                item.querySelector('[data-line-currency]').textContent = currency.value.toUpperCase();
            });
            const tax = Number(form.querySelector('#TaxAmount').value) || 0;
            const extras = Number(form.querySelector('#AdditionalCosts').value) || 0;
            document.querySelector('#offer-total').textContent = format.format(sum + tax + extras);
            document.querySelector('#offer-currency').textContent = currency.value.toUpperCase();
        };
        form.querySelectorAll('.line-quantity,.line-price,#Currency,#TaxAmount,#AdditionalCosts').forEach(input => input.addEventListener('input', update));
        update();
        const uploads = form.querySelector('#documents');
        uploads.addEventListener('change', () => {
            const files = [...uploads.files];
            const oversized = files.some(file => file.size > 10_000_000);
            uploads.setCustomValidity(oversized ? 'Each document must be 10 MB or smaller.' : '');
            form.querySelector('[data-upload-status]').textContent = oversized ? 'A selected file exceeds 10 MB. Choose a smaller file.' : files.map(file => file.name).join(', ');
        });
    }
    const errors = document.querySelector('[data-supplier-errors].validation-summary-errors');
    if (errors) errors.focus();
})();
