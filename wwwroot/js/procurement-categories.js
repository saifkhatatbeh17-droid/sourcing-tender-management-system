// Category picker: shows the 'Other category' box when 'Other' is chosen.
document.querySelectorAll('[data-category-fields]').forEach(fields => {
    const category = fields.querySelector('[data-category-select]');
    const otherField = fields.querySelector('[data-other-category]');
    const otherInput = otherField.querySelector('input');

    function updateOtherField() {
        const showOther = category.value === 'Other';
        otherField.hidden = !showOther;
        otherInput.disabled = !showOther;
        otherInput.required = showOther;
    }

    category.addEventListener('change', updateOtherField);
    updateOtherField();
});

document.querySelectorAll('[data-filter-suppliers]').forEach(form => {
    const category = form.querySelector('[data-category-select]');
    const custom = form.querySelector('[name="OtherCategory"]');
    const tender = form.querySelector('[data-tender-category-select]');
    const suppliers = Array.from(form.querySelectorAll('[data-supplier-category]'));
    const inviteAll = form.querySelector('[data-invite-all]');
    const noMatches = form.querySelector('[data-no-matching-suppliers]');
    const normalize = value => (value ?? '').trim().toUpperCase();

    function filterSuppliers() {
        const selected = category
            ? normalize(category.value === 'Other' ? custom.value : category.value)
            : normalize(tender.selectedOptions[0]?.dataset.category);
        const ready = category ? selected.length > 0 : tender.value !== '';
        let count = 0;
        for (const supplier of suppliers) {
            const matches = ready && (!selected || normalize(supplier.dataset.supplierCategory) === selected);
            const checkbox = supplier.querySelector('input');
            supplier.hidden = !matches;
            checkbox.disabled = !matches;
            if (!matches) checkbox.checked = false;
            if (matches) count++;
        }
        if (inviteAll) {
            inviteAll.checked = false;
            inviteAll.disabled = count === 0;
        }
        noMatches.hidden = ready && count > 0;
        noMatches.textContent = ready ? 'No approved suppliers match this category.' : (category ? 'Choose a category to see matching suppliers.' : 'Choose a tender to see matching suppliers.');
    }

    if (category) {
        category.addEventListener('change', filterSuppliers);
        custom.addEventListener('input', filterSuppliers);
    } else {
        tender.addEventListener('change', filterSuppliers);
    }
    filterSuppliers();
});
