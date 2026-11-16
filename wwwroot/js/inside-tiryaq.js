import { createCityScene } from './tiryaq-city-scene.js';
const page=document.querySelector('[data-campus]');
if(page)boot();
function boot(){
 const stages=[
  ['Start with a clear need.','Request needs','The department lists the items, quantities, specifications, and required date. The purchase request gives the sourcing team a shared starting point.',[['Department','responsible team'],['Purchase request','handoff record']],[-15,-9]],
  ['Publish a clear tender.','Publish tender','The sourcing officer reviews the request, sets tender requirements and a closing date, then invites approved suppliers in the relevant category.',[['Sourcing officer','responsible role'],['Published tender','handoff record']],[-15,7]],
  ['Bring supplier offers together.','Submit offers','Invited suppliers review the tender and submit item prices, delivery terms, tax, and supporting documents before the deadline.',[['Suppliers','responsible team'],['3','sample offers']],[0,12]],
  ['Compare offers fairly.','Evaluate offers','The officer checks compliance. Four committee members score eligible offers using agreed technical and financial weights. The officer sends the recommendation to the manager.',[['4 reviewers','evaluation committee'],['Recommendation','handoff record']],[-1,-9]],
  ['Review the selection.','Approve selection','The sourcing manager reviews the recommendation and prepares a separate summary. CEO & Chiefs review the referral, with four member approvals needed to confirm the selection.',[['Sourcing manager','review and referral'],['4 approvals','CEO & Chiefs']],[14,-9]],
  ['Record the outcome.','Finalize award','After the selection is approved, the sourcing manager approves the shared report. The officer finalizes the award and closes the tender. Relevant teams and suppliers can follow the recorded outcome.',[['Recorded award','process outcome'],['Connected records','request to decision']],[15,7]]
 ];
 const $=s=>page.querySelector(s),$$=s=>[...page.querySelectorAll(s)],clamp=(n,a,b)=>Math.max(a,Math.min(b,n));
 const host=$('[data-campus-world]'),canvas=$('canvas'),scroll=$('[data-campus-scroll]'),labels=$$('[data-campus-place]'),buttons=$$('[data-campus-step]');
 const reduced=matchMedia('(prefers-reduced-motion: reduce)'),small=matchMedia('(max-width:900px)');
 let mode='journey',active=-1,count=3,paused=reduced.matches,api=null;
 page.dataset.mode=mode;
 let playing=false,elapsed=0,playLast=0,playFrame=0;
 const autoplay=$('[data-campus-autoplay]');
 function stopTour(){playing=false;cancelAnimationFrame(playFrame);autoplay.textContent='Autoplay process';autoplay.setAttribute('aria-pressed','false');}
 function tickTour(now){
  if(!playing)return;
  const dt=playLast?Math.min(now-playLast,100):0;playLast=now;
  if(!document.hidden&&!paused){
   elapsed+=dt;
   const stage=Math.min(5,Math.floor(elapsed/7000)),phase=(elapsed%7000)/7000;
   if(active!==stage)card(stage,true);
   const progress=Math.min(1,(stage+Math.max(0,(phase-.55)/.45))/5);
   api?.progress(progress);api?.follow(progress);
   if(elapsed>=42000){stopTour();return;}
  }
  playFrame=requestAnimationFrame(tickTour);
 }
 autoplay.addEventListener('click',()=>{
  if(playing){stopTour();return;}
  setMode('explore');api?.enter();paused=false;motionState();elapsed=0;playLast=0;card(0,true);api?.progress(0);
  playing=true;autoplay.textContent='Stop autoplay';autoplay.setAttribute('aria-pressed','true');playFrame=requestAnimationFrame(tickTour);
 });
 $('[data-campus-enter]').addEventListener('click',()=>{stopTour();setMode('explore');api?.enter();go(active<0?0:active);});
 page.addEventListener('click',e=>{if(e.target.closest('[data-campus-step],[data-campus-place],[data-campus-back],[data-campus-start],[data-campus-overview],[data-campus-replay],[data-campus-mode],[data-campus-rotate],[data-campus-zoom]'))stopTour();},true);
 canvas.addEventListener('pointerdown',stopTour);canvas.addEventListener('keydown',stopTour);canvas.addEventListener('wheel',stopTour,{passive:true});
 window.addEventListener('pagehide',stopTour);
 function fact(value,label){const span=document.createElement('span'),strong=document.createElement('strong');strong.textContent=value;span.append(strong,document.createTextNode(label));return span;}
 function card(index,announce=false){
  active=index;const s=stages[index];
  if(s){$('#campus-title').textContent=s[0];$('[data-campus-chapter]').textContent=`${String(index+1).padStart(2,'0')} / ${s[1]}`;$('[data-campus-description]').textContent=s[2];$('[data-campus-facts]').replaceChildren(...s[3].map(([v,l])=>fact(index===2&&l==='sample offers'?String(count):v,l)));$('[data-campus-start]').textContent=index===5?'Back to overview ↗':`Next: ${stages[index+1][1]}`;}
  else{$('#campus-title').replaceChildren('One request.',document.createElement('br'),'Six clear steps.');$('[data-campus-chapter]').textContent='From request to award';$('[data-campus-description]').textContent='See how a purchase request moves through Tiryaq. Six connected stops bring the right people into each decision.';$('[data-campus-facts]').replaceChildren(fact('6','connected stages'),fact('1','shared record'));$('[data-campus-start]').textContent='Begin the journey →';}
  $('[data-campus-hint]').textContent=mode==='explore'?'Drag to rotate and tilt. Shift-drag to move. Scroll over the city to zoom.':small.matches?'Choose a stage below, or use Next to follow the request.':'Drag the city to explore, or choose a numbered stop.';
  $('[data-campus-back]').disabled=index<0;page.style.setProperty('--journey-progress',String(index<0?0:index*20));$('[data-campus-progress]').textContent=index<0?'Six steps. One connected process.':`Step ${index+1} of 6 · ${s[1]}`;
  buttons.forEach((b,i)=>i===index?b.setAttribute('aria-current','step'):b.removeAttribute('aria-current'));labels.forEach((b,i)=>i===index?b.setAttribute('aria-current','step'):b.removeAttribute('aria-current'));
  if(announce)$('[data-campus-status]').textContent=s?`Step ${index+1} of 6. ${s[1]}. ${s[2]}`:'Campus overview';api?.select(index);
 }
 function setMode(value){if(mode===value)return;mode=value;page.dataset.mode=mode;$$('[data-campus-mode]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.campusMode===mode)));api?.mode(mode);card(active);}
 function go(index){index=clamp(index,-1,5);card(index,true);api?.progress(index<0?0:index/5);}
 function overview(){setMode('explore');go(-1);api?.reset();}
 $('[data-campus-back]').addEventListener('click',()=>go(active-1));
 $('[data-campus-start]').addEventListener('click',()=>active<5?go(active+1):overview());buttons.forEach((b,i)=>b.addEventListener('click',()=>go(i)));labels.forEach((b,i)=>b.addEventListener('click',()=>{setMode('explore');go(i);}));
 $$('[data-campus-mode]').forEach(b=>b.addEventListener('click',()=>{setMode(b.dataset.campusMode);if(mode==='journey')go(-1);}));
 $('[data-campus-overview]').addEventListener('click',overview);$('[data-campus-replay]').addEventListener('click',()=>{setMode('journey');go(-1);api?.reset();if(small.matches)page.scrollIntoView({behavior:reduced.matches?'instant':'smooth'});});
 $$('[data-campus-rotate]').forEach(b=>b.addEventListener('click',()=>{setMode('explore');api?.rotate(Number(b.dataset.campusRotate));}));
 canvas.addEventListener('keydown',e=>{if(!['ArrowLeft','ArrowRight','+','=','-'].includes(e.key))return;e.preventDefault();setMode('explore');if(e.key==='ArrowLeft'||e.key==='ArrowRight')api?.rotate(e.key==='ArrowLeft'?-1:1);else api?.zoom(e.key==='-'?-1:1);});
 $$('[data-campus-zoom]').forEach(b=>b.addEventListener('click',()=>api?.zoom(Number(b.dataset.campusZoom))));
 const motion=$('[data-campus-motion]');function motionState(){motion.textContent=paused?'Resume motion':'Pause motion';motion.setAttribute('aria-pressed',String(paused));api?.pause(paused);}
 motion.addEventListener('click',()=>{paused=!paused;motionState();});reduced.addEventListener('change',()=>{paused=reduced.matches;motionState();});
 $('#campus-suppliers').addEventListener('input',e=>{count=Number(e.target.value);$('#campus-supplier-count').value=String(count);api?.suppliers(count);if(active===2)card(2);});
 small.addEventListener('change',()=>card(active));
 function follow(){}
 function fallback(error){console.warn('Campus view unavailable',error);page.dataset.render='fallback';$('[data-campus-loading]').hidden=true;$('[data-campus-fallback]').hidden=false;labels.forEach(b=>b.hidden=true);}
 try{api=scene();motionState();card(-1);follow();$('[data-campus-loading]').hidden=true;}catch(e){fallback(e);}

 function scene(){
  return createCityScene({host,canvas,labels,stages,reduced,small,paused,onInteract(){stopTour();setMode('explore');},onSelect(index){setMode('explore');go(index);},onFallback:fallback});
 }
}
