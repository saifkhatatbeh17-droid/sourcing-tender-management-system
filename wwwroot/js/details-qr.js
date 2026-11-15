document.querySelectorAll('[data-details-qr]').forEach(widget => {
    const trigger = widget.querySelector('[data-qr-trigger]');
    const codeDialog = document.getElementById(trigger.getAttribute('aria-controls'));
    trigger.addEventListener('click', () => codeDialog.showModal());

    const dialogs = widget.querySelectorAll('dialog');
    dialogs.forEach(dialog => {
        dialog.addEventListener('close', () => trigger.focus({ preventScroll: true }));
    });

    const automatic = widget.querySelectorAll('dialog[data-qr-auto="true"]');
    automatic[automatic.length - 1]?.showModal();
});
