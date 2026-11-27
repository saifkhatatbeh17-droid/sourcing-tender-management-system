// Choosing which suppliers to invite, including the 'invite all' option.
(() => {
    const sync = picker => {
        const all = picker.querySelector('[data-invite-all]');
        if (!all) return;
        const options = [...picker.querySelectorAll('input[name="supplierProfileIds"]')];
        const selected = options.filter(input => input.checked).length;
        all.checked = options.length > 0 && selected === options.length;
        all.indeterminate = selected > 0 && selected < options.length;
    };
    document.addEventListener('change', event => {
        const picker = event.target.closest('.tw-suppliers');
        if (!picker) return;
        if (event.target.matches('[data-invite-all]')) {
            picker.querySelectorAll('input[name="supplierProfileIds"]').forEach(input => { input.checked = event.target.checked; });
        }
        sync(picker);
    });
    document.querySelectorAll('.tw-suppliers').forEach(sync);
})();
