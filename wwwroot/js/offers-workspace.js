(() => {
 document.querySelectorAll('.offer-redesign .modal').forEach(modal => {
   let trigger;
   modal.addEventListener('show.bs.modal', event => { trigger = event.relatedTarget; });
   modal.addEventListener('hide.bs.modal', () => { if (modal.contains(document.activeElement)) document.activeElement.blur(); });
   modal.addEventListener('hidden.bs.modal', () => {
     const fallback = modal.id === 'tender-edit' ? document.querySelector('.ow-header-actions .ts-edit-button:not(:disabled)') : null;
     requestAnimationFrame(() => (trigger?.isConnected ? trigger : fallback)?.focus());
   });
 });
})();
