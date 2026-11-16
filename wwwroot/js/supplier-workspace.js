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
                item.querySelector('[data-line-currency]').textContent = (currency.value.toUpperCase() === 'JOD' ? 'JD' : currency.value.toUpperCase());
            });
            const percentage = Number(form.querySelector('#TaxPercentage').value) || 0;
            const tax = Math.round((sum * percentage / 100 + Number.EPSILON) * 100) / 100;
            const extras = Number(form.querySelector('#AdditionalCosts').value) || 0;
            document.querySelector('#offer-total').textContent = format.format(sum + tax + extras);
            document.querySelector('#offer-currency').textContent = (currency.value.toUpperCase() === 'JOD' ? 'JD' : currency.value.toUpperCase());
        };
        form.querySelectorAll('.line-quantity,.line-price,#Currency,#TaxPercentage,#AdditionalCosts').forEach(input => input.addEventListener('input', update));
        update();
        const panels = [...form.querySelectorAll('[data-offer-step]')];
        const steps = form.querySelector('[data-offer-steps]');
        const next = form.querySelector('[data-offer-next]');
        const back = form.querySelector('[data-offer-back]');
        const submit = form.querySelector('[data-offer-submit]');
        let current = 0;
        form.noValidate = true;
        steps.hidden = false;
        const displayCurrency = () => currency.value === 'JOD' ? 'JD' : currency.value;
        const review = () => {
            const target = form.querySelector('[data-review-content]');
            target.replaceChildren();
            const list = document.createElement('dl');
            list.className = 'supplier-review-list';
            const add = (label, value) => {
                const row = document.createElement('div');
                const term = document.createElement('dt'); term.textContent = label;
                const detail = document.createElement('dd'); detail.textContent = value;
                row.append(term, detail); list.append(row);
            };
            form.querySelectorAll('.supplier-price-item').forEach(item => {
                add(item.querySelector('h3').textContent, `${item.querySelector('.line-quantity').value} × ${item.querySelector('.line-price').value} ${displayCurrency()} = ${item.querySelector('.line-total').textContent} ${displayCurrency()}`);
                add('Technical response', item.querySelector('textarea').value);
            });
            add('Currency', displayCurrency());
            add('Tax', `${form.querySelector('#TaxPercentage').value}% of the item subtotal`);
            add('Extra costs', `${form.querySelector('#AdditionalCosts').value} ${displayCurrency()}`);
            add('Delivery', `${form.querySelector('#DeliveryDays').value} days`);
            add('Payment terms', form.querySelector('#PaymentTerms').value || 'Not specified');
            add('Technical notes', form.querySelector('#TechnicalDetails').value || 'Not provided');
            add('New documents', [...form.querySelector('#documents').files].map(file => file.name).join(', ') || 'No new documents selected');
            target.append(list);
            form.querySelector('[data-review-total]').textContent = `${document.querySelector('#offer-total').textContent} ${displayCurrency()}`;
        };
        const show = (index, focus = true) => {
            current = index;
            panels.forEach((panel, i) => { panel.hidden = i !== index; });
            steps.querySelectorAll('li').forEach((step, i) => {
                if (i === index) step.setAttribute('aria-current', 'step');
                else step.removeAttribute('aria-current');
            });
            steps.querySelector('[data-step-status]').textContent = `Step ${index + 1} of 3`;
            next.hidden = index === 2;
            back.hidden = index === 0;
            submit.hidden = index !== 2;
            if (index === 2) review();
            if (focus) {
                const heading = panels[index].querySelector('h2');
                heading.tabIndex = -1; heading.focus();
            }
        };
        const invalidIn = panel => [...panel.querySelectorAll('input,select,textarea')].find(input => !input.checkValidity());
        next.addEventListener('click', () => {
            const invalid = invalidIn(panels[current]);
            if (invalid) { invalid.reportValidity(); return; }
            show(current + 1);
        });
        back.addEventListener('click', () => show(current - 1));
        const validateAll = (positivePrices = false) => {
            for (let i = 0; i < panels.length; i++) {
                const invalid = invalidIn(panels[i]);
                if (invalid) { show(i, false); invalid.reportValidity(); return false; }
            }
            if (positivePrices) {
                const price = [...form.querySelectorAll('.line-price')].find(input => Number(input.value) <= 0);
                if (price) {
                    show(0, false);
                    price.setCustomValidity('Enter a positive unit price before submitting.');
                    price.reportValidity();
                    price.addEventListener('input', () => price.setCustomValidity(''), { once: true });
                    return false;
                }
            }
            return true;
        };
        // The shared confirmation dialog calls this before native validity checks.
        form.tiryaqValidate = () => validateAll(true);
        form.addEventListener('submit', event => {
            const isSubmission = event.submitter?.value === 'Submit';
            if (isSubmission && current !== 2) { event.preventDefault(); next.click(); return; }
            if (!validateAll(isSubmission)) event.preventDefault();
        });
        // Reopen the stage containing the server-reported field error.
        const errorIndex = panels.findIndex(panel => panel.querySelector('.input-validation-error,.field-validation-error'));
        show(errorIndex >= 0 ? errorIndex : 0, false);
        const uploads = form.querySelector('#documents');
        uploads.addEventListener('change', () => {
            const files = [...uploads.files];
            const oversized = files.some(file => file.size > 10_000_000);
            uploads.setCustomValidity(oversized ? 'Each document must be 10 MB or smaller.' : '');
            form.querySelector('[data-upload-status]').textContent = oversized ? 'A selected file exceeds 10 MB. Choose a smaller file.' : files.map(file => file.name).join(', ');
        });
    }
    const dialog = document.querySelector('[data-offer-dialog]');
    if (dialog && typeof dialog.showModal === 'function') {
        let opener;
        let request;
        const close = () => { request?.abort(); dialog.close(); opener?.focus(); };
        dialog.querySelector('[data-offer-dialog-close]').addEventListener('click', close);
        dialog.addEventListener('cancel', () => { request?.abort(); });
        document.querySelectorAll('[data-offer-details]').forEach(link => link.addEventListener('click', async event => {
            if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
            event.preventDefault();
            opener = link;
            request?.abort(); request = new AbortController();
            dialog.querySelector('[data-dialog-content]').replaceChildren();
            const status = dialog.querySelector('[data-dialog-status]');
            status.textContent = 'Loading offer details…';
            dialog.showModal();
            try {
                const response = await fetch(link.href, { signal: request.signal, credentials: 'same-origin' });
                if (!response.ok || response.redirected) throw new Error('Unavailable');
                const page = new DOMParser().parseFromString(await response.text(), 'text/html');
                const content = page.querySelector('.supplier-workspace--offer');
                if (!content) throw new Error('Unavailable');
                content.querySelector('.supplier-offer-back')?.remove();
                content.querySelectorAll('[id]').forEach(element => { element.id = `popup-${element.id}`; });
                content.querySelectorAll('[href^="#"]').forEach(element => { element.setAttribute('href', '#popup-' + element.getAttribute('href').slice(1)); });
                content.querySelectorAll('[aria-labelledby]').forEach(element => { element.setAttribute('aria-labelledby', element.getAttribute('aria-labelledby').split(' ').map(id => `popup-${id}`).join(' ')); });
                dialog.querySelector('[data-dialog-content]').append(document.importNode(content, true));
                status.textContent = '';
            } catch (error) {
                if (error.name === 'AbortError') return;
                status.textContent = 'Offer details could not be loaded. ';
                const fallback = document.createElement('a'); fallback.href = link.href; fallback.textContent = 'Open offer details'; status.append(fallback);
            }
        }));
    }
    const errors = document.querySelector('[data-supplier-errors].validation-summary-errors');
    if (errors) errors.focus();
})();
