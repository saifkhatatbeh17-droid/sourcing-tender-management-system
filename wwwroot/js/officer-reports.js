// Sourcing Officer reports page: search and open reports.
(() => {
 const search=document.getElementById('report-search'),filter=document.getElementById('report-filter');
 if(!search||!filter)return;
 const rows=[...document.querySelectorAll('.rp-report-row')];
 function update(){let count=0;const query=search.value.trim().toLocaleLowerCase();rows.forEach(row=>{row.hidden=!row.dataset.search.toLocaleLowerCase().includes(query)||(filter.value!=='all'&&row.dataset.filter!==filter.value);if(!row.hidden)count++;});document.querySelector('.rp-results').textContent=`${count} ${count===1?'report':'reports'}`;const empty=document.querySelector('.rp-no-matches');if(empty)empty.hidden=count!==0;}
 search.addEventListener('input',update);filter.addEventListener('change',update);
})();

(() => {
 const dialog=document.getElementById('report-review-modal');
 if(!dialog)return;
 const content=dialog.querySelector('.modal-content');
 let request,trigger;
 const modal=bootstrap.Modal.getOrCreateInstance(dialog);
 dialog.addEventListener('hidden.bs.modal',()=>{request?.abort();trigger?.focus();});
 dialog.addEventListener('hide.bs.modal',()=>{if(dialog.contains(document.activeElement))document.activeElement.blur();});
 document.querySelectorAll('[data-report-review-url]').forEach(button=>button.addEventListener('click',async()=>{
  trigger=button;request?.abort();request=new AbortController();
  content.innerHTML='<div class="modal-header"><h2 class="modal-title" id="report-review-title">Review evaluation report</h2><button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close report review"></button></div><div class="modal-body" role="status" aria-live="polite">Loading report…</div>';
  modal.show();
  try {
   const response=await fetch(button.dataset.reportReviewUrl,{signal:request.signal,headers:{'X-Requested-With':'XMLHttpRequest'}});
   if(!response.ok||response.redirected)throw new Error('Report unavailable');
   content.innerHTML=await response.text();
   const form=content.querySelector('.rv-confirm'),actions=content.querySelector('.rv-actions');
   content.querySelectorAll('[data-report-decision]').forEach(choice=>choice.addEventListener('click',()=>{
    const decision=choice.dataset.reportDecision;
    const approve=decision==='Approve',reject=decision==='Reject';
    form.elements.decision.value=decision;
    form.querySelector('#rv-confirm-title').textContent=approve?'Forward this recommendation?':reject?'Reject report and cancel tender?':'Return report to the committee?';
    form.querySelector('#rv-confirm-help').textContent=approve?'The approved report and recommended offer will be sent unchanged to the sourcing manager.':reject?'This cancels the tender. Record a clear reason before confirming.':'Explain what needs to change. The committee must review the report and record four fresh approvals.';
    const reason=form.elements.reason;
    reason.required=!approve;reason.disabled=approve;
    form.querySelector('.rv-reason').hidden=approve;
    const submit=form.querySelector('[data-report-confirm]');
    submit.textContent=approve?'Confirm & forward':reject?'Reject & cancel tender':'Send edit request';
    submit.className=reject?'btn btn-danger':'btn btn-primary';
    actions.hidden=true;form.hidden=false;
    form.querySelector('#rv-confirm-title').focus();
   }));
   form?.querySelector('[data-report-back]').addEventListener('click',()=>{
    const decision=form.elements.decision.value;form.hidden=true;actions.hidden=false;
    actions.querySelector(`[data-report-decision="${decision}"]`).focus();
   });
   form?.addEventListener('submit',()=>{
    const submit=form.querySelector('[data-report-confirm]');submit.disabled=true;submit.textContent='Saving decision…';
   });
  }catch(error){
   if(error.name==='AbortError')return;
   content.querySelector('.modal-body').innerHTML='<div role="alert"><h3>Couldn’t load this report</h3><p>Close this window and try again. If it keeps happening, refresh the report list.</p></div>';
  }
 }));
})();
