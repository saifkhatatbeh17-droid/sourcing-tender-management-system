/* A single SVG world. Buildings, their work, streets and the request share coordinates. */
(function(){
'use strict';
const page=document.querySelector('[data-campus]');
if(!page||!window.TiryaqCityScenes)return;
const $=s=>page.querySelector(s),$$=s=>[...page.querySelectorAll(s)];
const svg=$('[data-city-svg]'),world=$('[data-city-world]'),viewport=$('[data-city-viewport]');
const library=window.TiryaqCityScenes,NS='http://www.w3.org/2000/svg';
const reduced=matchMedia('(prefers-reduced-motion: reduce)'),small=matchMedia('(max-width:900px)');
const names=['Pharmacy Department','Tender office','Supplier courtyard','Evaluation lab','Decision boardroom','Award archive'];
const titles=['A request starts here.','The request becomes a tender.','Suppliers prepare their offers.','Four reviewers. Shared criteria.','A recommendation becomes a decision.','Awarded, with the whole trail kept.'];
const pitch=1470,offset=110,roomY=170,worldWidth=8890,transferDuration=3;
let scenes=[],suppliers=3,position=0,total=0,playing=false,followCamera=true,overview=false;
let frame=0,last=0,active=-1,beat=-1,drag=null,suppressClick=false,scrollPending=0;
const camera={x:710,y:520,w:1600},target={...camera};
function element(tag,attrs,content){const el=document.createElementNS(NS,tag);for(const [k,v]of Object.entries(attrs||{}))el.setAttribute(k,v);if(content!=null)el.textContent=content;return el;}
function tree(x,y,scale=1){return `<g transform="translate(${x},${y}) scale(${scale})"><rect x="-5" y="-48" width="10" height="54" rx="4" fill="#927d61"/><circle cy="-75" r="32" fill="#9cc1a2"/><circle cx="-18" cy="-55" r="25" fill="#83af91"/><circle cx="20" cy="-58" r="26" fill="#aacdb0"/></g>`;}
function cityBackdrop(){
 let s='<rect x="-1500" y="-1500" width="13000" height="4000" fill="#edf4f1"/>';
 // A distant skyline and a continuous ground plane establish one shared city.
 for(let i=0;i<36;i++){const x=i*270-120,h=100+(i%4)*26;s+=`<rect x="${x}" y="${160-h}" width="190" height="${h+630}" rx="6" fill="#dce9e2"/>`;for(let w=0;w<3;w++)s+=`<rect x="${x+20+w*55}" y="${185-h}" width="25" height="32" rx="3" fill="#eaf3ed"/>`;}
 s+='<rect x="-1000" y="850" width="11000" height="600" fill="#d7e5d9"/><rect x="-1000" y="885" width="11000" height="46" fill="#e4ece4"/><path d="M-1000 930H10000" stroke="#b9cdbf" stroke-width="4"/><rect x="-1000" y="935" width="11000" height="170" fill="#7d9690"/><path d="M-1000 1020H10000" stroke="#dce8df" stroke-width="4" stroke-dasharray="30 28"/><rect x="-1000" y="1105" width="11000" height="90" fill="#e4ece4"/>';
 for(let i=0;i<6;i++){const x=offset+i*pitch;s+=`<path d="M${x+1140} 860V937" stroke="#c1d4c5" stroke-width="55"/><path d="M${x+1140} 860V937" stroke="#f7fbf6" stroke-width="3" stroke-dasharray="7 10"/>`;s+=tree(x+1300,887,1.15)+tree(x+1350,1165,.85);s+=`<g transform="translate(${x+1290},865)"><rect width="70" height="10" rx="3" fill="#927d61"/><path d="M8 10v20m54-20v20" stroke="#315c60" stroke-width="6"/></g><text class="city-road-label" x="${x+210}" y="1067">${i===0?'ONE REQUEST · ONE CONNECTED JOURNEY':names[i].toUpperCase()}</text>`;}
 return s;
}
function build(){
 scenes.forEach(s=>s.engine.stop());scenes=[];world.innerHTML=cityBackdrop();let start=0;
 for(let i=0;i<6;i++){
  const x=offset+i*pitch,group=element('g',{'data-city-building':i});
  group.innerHTML=`<rect x="${x-17}" y="${roomY-15}" width="1234" height="700" fill="#afc8b9"/><path d="M${x-35} ${roomY-14}V${roomY-65}L${x+600} ${roomY-103}L${x+1235} ${roomY-65}V${roomY-14}Z" fill="#315c60"/><rect x="${x-28}" y="${roomY-23}" width="1256" height="14" fill="#6d9988"/><text class="city-roof-label" x="${x+600}" y="${roomY-47}" text-anchor="middle">${String(i+1).padStart(2,'0')}  ${names[i]}</text><rect x="${x-25}" y="${roomY+675}" width="1250" height="18" fill="#b5cbbb"/>`;
  const art=element('g',{transform:`translate(${x},${roomY})`,class:'city-world-room'}),def=library.create(i,suppliers);
  art.innerHTML=def.html;const room=art.querySelector('svg');room.setAttribute('width','1200');room.setAttribute('height','675');
  room.setAttribute('aria-hidden','true');
  // The roof labels belong to the city, so the cutaway doesn't repeat them.
  const roomHeading=[...room.querySelectorAll('text')].find(t=>t.getAttribute('y')==='46');if(roomHeading)roomHeading.remove();
  const engine=library.engine(),starts=[];let duration=0;
  def.beats.forEach(b=>{starts.push(duration);duration+=b.dur;});
  def.build(engine,0,sel=>art.querySelector(sel));engine.setDuration(duration);
  // One world document is used throughout; local artwork never duplicates it.
  art.querySelectorAll('#s1-doc,#s2-doc,#s5-doc,#s6-doc').forEach(el=>el.setAttribute('visibility','hidden'));
  const hit=element('rect',{x:x-30,y:roomY-110,width:1260,height:80,rx:8,class:'city-building-hit',tabindex:0,role:'button','aria-label':`Visit ${names[i]}`});
  hit.addEventListener('click',()=>{if(!suppressClick)select(i);});hit.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();select(i);}});
  group.append(art,hit);world.append(group);scenes.push({x,art,engine,def,duration,starts,start,end:start+duration});start+=duration+(i<5?transferDuration:0);
 }
 total=start;
 const trail=element('path',{fill:'none',stroke:'#ee8d54','stroke-width':4,'stroke-dasharray':'7 12',opacity:.6,'data-city-path':''});world.append(trail);
 const packet=element('g',{class:'city-request','data-city-document':'','aria-hidden':'true'});
 packet.innerHTML='<circle class="city-request-glow" cy="3" r="65"/><rect x="-36" y="-44" width="72" height="89" rx="6" fill="#fff7ed" stroke="#ee8d54" stroke-width="4"/><path d="M10-44H36V-18Z" fill="#ee8d54"/><path d="M-21-18H4M-21-3H21M-21 11H21" stroke="#ee8d54" stroke-width="5" stroke-linecap="round"/><rect x="-55" y="48" width="110" height="28" rx="6" fill="#315c60"/><text y="68" text-anchor="middle" fill="#fff" font-size="18" font-weight="700">PR-2041</text>';
 world.append(packet);active=-1;beat=-1;render(true);
}
function locate(t){for(let i=0;i<6;i++){const s=scenes[i];if(t<=s.end)return{i,t:Math.max(0,t-s.start),travel:false};if(i<5&&t<scenes[i+1].start)return{i,t:t-s.end,travel:true};}return{i:5,t:scenes[5].duration,travel:false};}
function currentBeat(s,t){for(let i=s.starts.length-1;i>=0;i--)if(t>=s.starts[i])return i;return 0;}
function smooth(t){t=Math.max(0,Math.min(1,t));return t*t*(3-2*t);}
function lerp(a,b,t){return a+(b-a)*t;}
function pathPoint(points,t){const v=Math.min(.99999,Math.max(0,t))*(points.length-1),i=Math.floor(v),p=smooth(v-i);return{x:lerp(points[i][0],points[i+1][0],p),y:lerp(points[i][1],points[i+1][1],p)};}
function documentPoint(state){const s=scenes[state.i],x=s.x;if(state.travel){const next=scenes[state.i+1];return pathPoint([[x+1140,900],[x+1290,978],[next.x-45,978],[next.x+55,900],[next.x+330,roomY+420]],state.t/transferDuration);}const f=state.t/s.duration;const desks=[500,510,690,510,490,620];return pathPoint([[x+70,roomY+420],[x+desks[state.i],roomY+415],[x+desks[state.i],roomY+415],[x+1140,900]],f);}
function render(force=false){
 const state=locate(position),s=scenes[state.i],b=currentBeat(s,state.travel?s.duration:state.t);
 if(force||state.i!==active){for(let i=0;i<6;i++){const sc=scenes[i];sc.engine.seek(i<state.i?sc.duration:i===state.i?Math.min(sc.duration,state.travel?sc.duration:state.t):0);}}else{s.engine.seek(Math.min(s.duration,state.travel?s.duration:state.t));}
 if(force||state.i!==active||b!==beat){active=state.i;beat=b;$('[data-city-title]').textContent=titles[active];$('[data-city-chapter]').textContent=`${String(active+1).padStart(2,'0')} · ${names[active]} · sample PR-2041`;$('[data-city-caption]').textContent=state.travel?`PR-2041 travels along the street to the ${names[active+1].toLowerCase()}.`:s.def.beats[b].caption;$('[data-city-progress]').textContent=`${String(active+1).padStart(2,'0')} · ${b+1} / ${s.starts.length}`;$('[data-city-status]').textContent=$('[data-city-caption]').textContent;}
 if(state.travel)$('[data-city-caption]').textContent=`PR-2041 travels along the street to the ${names[active+1].toLowerCase()}.`;
 const point=documentPoint(state);$('[data-city-document]').setAttribute('transform',`translate(${point.x} ${point.y})`);
 $('[data-city-path]').setAttribute('d',`M${offset+1140} 978H${Math.max(offset+1140,point.x)}`);
 $$('[data-city-step]').forEach((button,i)=>{button.toggleAttribute('aria-current',false);if(i===state.i)button.setAttribute('aria-current','step');const completed=position>=scenes[i].start+[14.8,14,18.5,19.5,17,.2][i];button.classList.toggle('is-done',completed);button.querySelector('[data-city-tick]').textContent=completed?'✓':String(i+1).padStart(2,'0');});
 $('[data-city-scrub]').value=String(Math.round(position/total*1000));
 if(followCamera&&!overview){const focus=[[330,850,1000,850,1100],[480,930,850,955,650],[620,620,620,620,800,1055],[240,240,805,815,815,560],[150,460,870,820,880],[620,620,350,600]];target.x=state.travel?point.x:s.x+(small.matches?focus[state.i][b]:600);target.y=small.matches?540:560;target.w=small.matches?600:Math.max(1550,fit().aspect*1030);}
 updatePlay();
}
function updatePlay(){$('[data-city-play]').textContent=reduced.matches?'Next step':playing?'Pause journey':'Play journey';$('[data-city-play]').setAttribute('aria-pressed',String(playing));}
function select(i){playing=false;overview=false;followCamera=true;position=scenes[i].start+(reduced.matches?scenes[i].starts[1]-.1:0);render(true);}
function step(direction){playing=false;overview=false;followCamera=true;const state=locate(position),s=scenes[state.i],b=currentBeat(s,state.t);let i=state.i,k=b+direction;if(i===0&&k<0){position=0;render(true);return;}if(i===5&&k>=s.starts.length){position=total;render(true);return;}if(k<0){i=Math.max(0,i-1);k=scenes[i].starts.length-1;}if(k>=s.starts.length){i=Math.min(5,i+1);k=0;}const sc=scenes[i];position=sc.start+(reduced.matches?(sc.starts[k+1]||sc.duration)-.1:sc.starts[k]+.01);render(true);}
function fit(){const box=viewport.getBoundingClientRect(),aspect=Math.max(.25,box.width/Math.max(1,box.height));return{aspect,w:box.width,h:box.height};}
function applyCamera(dt,instant=false){const view=fit(),blend=instant||reduced.matches?1:1-Math.exp(-dt*5);camera.x=lerp(camera.x,target.x,blend);camera.y=lerp(camera.y,target.y,blend);camera.w=lerp(camera.w,target.w,blend);const height=camera.w/view.aspect;svg.setAttribute('viewBox',`${camera.x-camera.w/2} ${camera.y-height/2} ${camera.w} ${height}`);}
function loop(now){frame=requestAnimationFrame(loop);const dt=Math.max(0,Math.min(.05,(now-(last||now))/1000));last=now;if(document.hidden)return;if(playing&&!reduced.matches){position=Math.min(total,position+dt);if(position>=total)playing=false;render();}applyCamera(dt);}
$$('[data-city-step]').forEach((button,i)=>button.addEventListener('click',()=>select(i)));
$('[data-city-play]').addEventListener('click',()=>{if(reduced.matches){step(1);return;}if(position>=total)position=0;playing=!playing;overview=false;followCamera=true;render();});
$('[data-city-replay]').addEventListener('click',()=>{position=0;overview=false;followCamera=true;playing=!reduced.matches;render(true);});
$('[data-city-previous]').addEventListener('click',()=>step(-1));$('[data-city-next]').addEventListener('click',()=>step(1));
$('[data-city-scrub]').addEventListener('input',e=>{position=Number(e.target.value)/1000*total;playing=false;overview=false;followCamera=true;render(true);});
$('[data-city-suppliers]').addEventListener('input',e=>{const state=locate(position),fraction=state.t/scenes[state.i].duration;suppliers=Number(e.target.value);$('[data-city-count]').value=String(suppliers);build();position=scenes[state.i].start+fraction*scenes[state.i].duration;render(true);});
$('[data-city-overview]').addEventListener('click',()=>{playing=false;overview=true;followCamera=false;target.x=worldWidth/2;target.y=520;target.w=worldWidth+250;updatePlay();});
$('[data-city-follow]').addEventListener('click',()=>{overview=false;followCamera=true;render(true);});
$$('[data-city-zoom]').forEach(button=>button.addEventListener('click',()=>{followCamera=false;overview=false;target.w=Math.max(480,Math.min(worldWidth+250,target.w*(Number(button.dataset.cityZoom)>0?.8:1.25)));}));
viewport.addEventListener('pointerdown',e=>{if(e.target.closest('button'))return;drag={x:e.clientX,y:e.clientY,startX:e.clientX,startY:e.clientY,moved:false};});
viewport.addEventListener('pointermove',e=>{if(!drag)return;const dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(Math.hypot(e.clientX-drag.startX,e.clientY-drag.startY)>5){if(!drag.moved)viewport.setPointerCapture(e.pointerId);drag.moved=true;playing=false;followCamera=false;overview=false;viewport.classList.add('is-dragging');const ratio=camera.w/Math.max(1,fit().w);target.x=Math.max(-100,Math.min(worldWidth,target.x-dx*ratio));target.y=Math.max(300,Math.min(850,target.y-dy*ratio));camera.x=target.x;camera.y=target.y;updatePlay();}drag.x=e.clientX;drag.y=e.clientY;});
function release(e){if(drag){suppressClick=drag.moved;drag=null;viewport.classList.remove('is-dragging');if(viewport.hasPointerCapture(e.pointerId))viewport.releasePointerCapture(e.pointerId);setTimeout(()=>suppressClick=false,0);}}
viewport.addEventListener('pointerup',release);viewport.addEventListener('pointercancel',release);
viewport.addEventListener('keydown',e=>{if(e.target.closest('button'))return;if(e.key==='ArrowRight'){e.preventDefault();step(1);}if(e.key==='ArrowLeft'){e.preventDefault();step(-1);}if(e.key===' '){e.preventDefault();$('[data-city-play]').click();}if(e.key==='+'||e.key==='=')$('[data-city-zoom="1"]').click();if(e.key==='-')$('[data-city-zoom="-1"]').click();});
function scrollFollow(){scrollPending=0;if(small.matches||drag)return;const sc=$('[data-city-scroll]'),top=sc.getBoundingClientRect().top+window.scrollY-70,range=Math.max(1,sc.offsetHeight-$('.city-stage').offsetHeight);position=Math.max(0,Math.min(1,(window.scrollY-top)/range))*total;playing=false;overview=false;followCamera=true;render(true);}
window.addEventListener('scroll',()=>{if(!scrollPending)scrollPending=requestAnimationFrame(scrollFollow);},{passive:true});
reduced.addEventListener('change',()=>{if(reduced.matches)playing=false;render(true);applyCamera(1,true);});
window.addEventListener('pagehide',()=>{cancelAnimationFrame(frame);cancelAnimationFrame(scrollPending);scenes.forEach(s=>s.engine.stop());});
window.addEventListener('pageshow',e=>{if(e.persisted){last=0;frame=requestAnimationFrame(loop);}});
window.addEventListener('resize',()=>{if(followCamera)render(true);});
build();applyCamera(1,true);frame=requestAnimationFrame(loop);
})();
