// Sourcing Officer offers: marks an offer as read once it is opened.
(() => {
    document.querySelectorAll('.ow-offer-modal[data-offer-id]').forEach(modal => {
        const row = document.querySelector(`.os-offer-row[data-offer-id="${modal.dataset.offerId}"]`);
        if (!row) return;
        let saving = false;
        async function markOpened() {
            if (!modal.classList.contains('show') || row.dataset.unread !== 'true' || saving || document.hidden) return;
            saving = true;
            try {
                const token = row.closest('form')?.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const response = await fetch(row.dataset.openUrl, {method: 'POST', credentials: 'same-origin', headers: {'Content-Type': 'application/x-www-form-urlencoded'}, body: new URLSearchParams({__RequestVerificationToken: token ?? ''})});
                if (response.ok) {
                    row.dataset.unread = 'false';
                    const marker = row.querySelector('.os-unread-dot');
                    if (marker) marker.hidden = true;
                }
            } catch { /* Keep the unread marker when saving fails. */ }
            finally { saving = false; }
        }
        modal.addEventListener('shown.bs.modal', markOpened);
        document.addEventListener('visibilitychange', markOpened);
    });
})();
