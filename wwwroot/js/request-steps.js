// Department request form: splits it into steps with next/previous buttons.
(() => {
 'use strict';
 const form=document.getElementById('purchase-request-form');if(!form)return;
 const steps=[...form.querySelectorAll('[data-request-step]')],markers=[...form.querySelectorAll('[data-request-target]')];
 if(steps.length!==2)return;
 const back=form.querySelector('[data-request-previous]'),next=form.querySelector('[data-request-next]'),final=form.querySelector('[data-request-final]');let current=0;form.noValidate=true;
 function show(index,focus=true){current=index;steps.forEach((step,i)=>step.hidden=i!==index);markers.forEach((m,i)=>{if(i===index)m.setAttribute('aria-current','step');else m.removeAttribute('aria-current');m.classList.toggle('is-complete',i<index);m.querySelector('.rq-step-number').textContent=i<index?'✓':String(i+1);m.querySelector('[data-step-state]').textContent=i<index?'Completed':i===index?'Current step':'Up next';});back.hidden=index===0;next.hidden=index===1;final.hidden=index!==1;form.querySelector('#request-step-status').textContent=`Step ${index+1} of 2 · ${index===0?'Request details':'Requested items'}`;if(focus){const title=steps[index].querySelector('h2');title.tabIndex=-1;title.focus();}}
 function invalidIn(step){return [...step.querySelectorAll('input,select,textarea')].find(el=>el.willValidate&&!el.validity.valid);}
 function reveal(field){const index=steps.findIndex(step=>step.contains(field));if(index>=0)show(index,false);field.reportValidity();requestAnimationFrame(()=>field.focus());return false;}
 next.addEventListener('click',()=>{const invalid=invalidIn(steps[0]);if(invalid)return reveal(invalid);show(1);});back.addEventListener('click',()=>show(0));
 markers.forEach((m,index)=>m.addEventListener('click',()=>{if(index>current){const invalid=invalidIn(steps[current]);if(invalid)return reveal(invalid);}show(index);}));
 form.tiryaqValidate=()=>{for(const step of steps){const invalid=invalidIn(step);if(invalid)return reveal(invalid);}if(current===0){show(1);return false;}return true;};
 form.addEventListener('submit',event=>{if(!form.tiryaqValidate()){event.preventDefault();event.stopImmediatePropagation();}},true);
 form.querySelector('#request-errors').addEventListener('click',event=>{const link=event.target.closest('a[href^="#"]');if(!link)return;const target=document.getElementById(link.hash.slice(1));const index=steps.findIndex(step=>step.contains(target));if(index>=0)show(index,false);},true);
 const serverError=form.querySelector('.input-validation-error');const errorStep=serverError?steps.findIndex(step=>step.contains(serverError)):0;show(Math.max(0,errorStep),false);
})();
