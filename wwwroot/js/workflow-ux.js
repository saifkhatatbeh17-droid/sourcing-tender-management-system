(() => {
 'use strict';
 document.querySelectorAll('[data-qr-open]').forEach(trigger=>{const dialog=document.getElementById(trigger.dataset.qrOpen);if(!dialog)return;trigger.addEventListener('click',()=>dialog.showModal());dialog.querySelector('[data-qr-close]').addEventListener('click',()=>dialog.close());dialog.addEventListener('close',()=>trigger.focus());dialog.addEventListener('click',event=>{const r=dialog.getBoundingClientRect();if(event.target===dialog&&(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom))dialog.close();});});
 function flash(message){
  if(message.dataset.flashReady)return;
  message.dataset.flashReady='true';
  message.classList.add('persistent-feedback');
  const dismiss=document.createElement('button');
  dismiss.type='button';dismiss.className='feedback-dismiss';
  dismiss.setAttribute('aria-label','Dismiss message');dismiss.textContent='×';
  dismiss.addEventListener('click',()=>{const main=document.querySelector('main');message.remove();if(main){if(!main.hasAttribute('tabindex'))main.setAttribute('tabindex','-1');main.focus({preventScroll:true});}});
  message.append(dismiss);
 }
 document.querySelectorAll('[data-flash-message],.action-success,[data-admin-feedback]').forEach(flash);
 // Admin actions can replace their feedback markup without reloading.
 new MutationObserver(records=>records.forEach(record=>record.addedNodes.forEach(node=>{if(!(node instanceof Element))return;if(node.matches('[data-flash-message],.action-success,[data-admin-feedback]'))flash(node);node.querySelectorAll('[data-flash-message],.action-success,[data-admin-feedback]').forEach(flash);}))).observe(document.querySelector('main')||document.body,{childList:true,subtree:true});
})();
