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
 const picker = document.getElementById('tender-picker');
 picker?.addEventListener('shown.bs.modal', () => document.getElementById('tender-search')?.focus());
 const confirmation = document.getElementById('compliance-confirm');
 let pendingInput;
 if (confirmation) {
   const form = confirmation.querySelector('form');
   document.querySelectorAll('.ow-row-decision input[type="radio"]').forEach(input => {
     input.addEventListener('change', () => {
       const row = input.closest('.os-offer-row');
       pendingInput = input;
       const group = input.closest('.ow-decision');
       group.classList.remove('ow-decision-missing');
       group.querySelector('.ow-decision-hint').classList.add('visually-hidden');
       input.checked = false;
       const compliant = input.value === 'Compliant';
       form.elements.id.value = row.dataset.offerId;
       form.elements.decision.value = input.value;
       confirmation.querySelector('#compliance-confirm-title').textContent = compliant ? 'Mark offer as compliant?' : 'Mark offer as non-compliant?';
       confirmation.querySelector('.oc-offer').textContent = `${row.dataset.supplier} · Offer #${row.dataset.offerId}`;
       confirmation.querySelector('#compliance-confirm-help').textContent = compliant ? 'This confirms that the offer meets the requested requirements. It will be eligible for committee review.' : 'This confirms that the offer does not meet the requested requirements. It will be excluded from committee review.';
       const submit = confirmation.querySelector('[data-confirm-compliance]');
       submit.textContent = compliant ? 'Confirm compliant' : 'Confirm non-compliant';
       submit.disabled = false;
       submit.classList.toggle('oc-negative', !compliant);
       bootstrap.Modal.getOrCreateInstance(confirmation).show();
     });
   });
   confirmation.addEventListener('shown.bs.modal', () => confirmation.querySelector('[data-bs-dismiss="modal"]').focus());
   confirmation.addEventListener('hide.bs.modal', () => { if (confirmation.contains(document.activeElement)) document.activeElement.blur(); });
   confirmation.addEventListener('hidden.bs.modal', () => { pendingInput?.focus(); });
   form.addEventListener('submit', () => {
     const submit = confirmation.querySelector('[data-confirm-compliance]');
     submit.disabled = true;
     submit.textContent = 'Saving decision…';
   });
 }
 window.addEventListener('submit', event => {
   if (!(event.target instanceof HTMLFormElement) || !event.target.action.endsWith('/ReviewAndSend')) return;
   const missing = [...document.querySelectorAll('.ow-decision')].find(group => group.querySelector('input'));
   if (!missing) return;
   event.preventDefault();event.stopImmediatePropagation();
   const rowDetails = missing.closest('.os-offer-row')?.querySelector('.os-offer-details');
   if (rowDetails) rowDetails.open = true;
   missing.classList.add('ow-decision-missing');
   missing.querySelector('.ow-decision-hint').classList.remove('visually-hidden');
   missing.querySelector('.ow-decision-hint').textContent = 'Decision required before sending.';
   missing.querySelector('input').focus();
 }, true);
})();
