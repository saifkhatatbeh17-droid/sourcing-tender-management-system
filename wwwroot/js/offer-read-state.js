(() => {
    document.querySelectorAll('.os-offer-row[data-offer-id]').forEach(row => {
        const details = row.querySelector('.os-offer-details');
        let saving = false;
        async function markOpened() {
            if (!details.open || row.dataset.unread !== 'true' || saving || document.hidden) return;
            const bounds = details.querySelector('summary').getBoundingClientRect();
            if (bounds.bottom <= 0 || bounds.top >= window.innerHeight) return;
            saving = true;
            try {
                const token = row.closest('form')?.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const response = await fetch(row.dataset.openUrl, {method: 'POST', credentials: 'same-origin', headers: {'Content-Type': 'application/x-www-form-urlencoded'}, body: new URLSearchParams({__RequestVerificationToken: token ?? ''})});
                if (response.ok) { row.dataset.unread = 'false'; row.querySelector('.os-unread-dot').hidden = true; }
            } catch { /* Keep the unread marker when saving fails. */ }
            finally { saving = false; }
        }
        details.addEventListener('toggle', markOpened);
        // An automatically expanded offer is read only once it is actually on screen.
        if ('IntersectionObserver' in window) {
            const observer = new IntersectionObserver(entries => {
                if (entries.some(entry => entry.isIntersecting) && details.open) markOpened();
            }, {threshold: 0.25});
            observer.observe(details.querySelector('summary'));
        } else { markOpened(); }
    });
})();
