/* Read-only authenticated route, responsive, accessibility and interaction QA. Credentials via environment only. */
const fs=require('fs'),path=require('path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'C:/Users/user/AppData/Local/CodexTools/playwright/cli/node_modules/playwright');
const root=process.cwd(),out=path.join(root,'output/qa/browser');fs.mkdirSync(out,{recursive:true});
const base='https://localhost:7130',accounts=JSON.parse(process.env.TIRYAQ_QA_ACCOUNTS||'[]');
const axe=path.join(process.env.TEMP,'tiryaq-a11y/node_modules/axe-core/axe.min.js');
const groups=[['Admin',['Dashboard','Users','Departments','Suppliers','SupplierApplications','ContactMessages']],['Department',['Dashboard','Requests','Create','Clarifications']],['SourcingOfficer',['Dashboard','Requests','Offers','Reports','Create','Clarifications']],['EvaluationCommittee',['Dashboard','Evaluations','Reports','Clarifications']],['SourcingManager',['Dashboard','Submissions','ChosenOffers','Reports','Clarifications']],['TopCommittee',['Dashboard','Decisions','History','Reports','Clarifications','Notifications']],['Supplier',['Dashboard','Invitations','Offers','Profile','Clarifications','Notifications','History']]];
function controller(role){return role==='Sourcing Officer'?'SourcingOfficer':role==='Sourcing Manager'?'SourcingManager':role.startsWith('Evaluation:')?'EvaluationCommittee':role.startsWith('Top Committee:')?'TopCommittee':role;}
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});const results=[];const sampled=new Set();
 for(const account of accounts){
  const ctrl=controller(account.role),deep=!sampled.has(ctrl);sampled.add(ctrl);
  const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1440,height:1000}});const page=await context.newPage();
  await page.goto(base+'/Account/Login');await page.getByRole('textbox',{name:'Email',exact:true}).fill(account.email);await page.getByRole('textbox',{name:'Password',exact:true}).fill(account.password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(u=>!u.pathname.includes('/Account/Login'));
  const queue=deep?groups.find(g=>g[0]===ctrl)[1].map(a=>`/${ctrl}/${a}`):[`/${ctrl}/Dashboard`,`/${ctrl}/${ctrl==='TopCommittee'?'Decisions':'Evaluations'}`];
  const seen=new Set();let detailCount=0;
  while(queue.length){
   const route=queue.shift();if(seen.has(route))continue;seen.add(route);
   const errors=[],resources=[];const onError=e=>errors.push(e.message),onResponse=r=>{if(r.status()>=400&&r.url().startsWith(base)&&r.request().resourceType()!=='document')resources.push({url:new URL(r.url()).pathname,status:r.status()});};page.on('pageerror',onError);page.on('response',onResponse);
   const record={role:account.role,route};
   try{
    const response=await page.goto(base+route);await page.waitForLoadState('networkidle');
    record.status=response.status();record.final=new URL(page.url()).pathname+new URL(page.url()).search;record.title=await page.title();
    record.headings=await page.locator('main h1').allTextContents();
    if(deep){
     const hrefs=await page.locator('main a[href]').evaluateAll(els=>els.map(e=>e.getAttribute('href')));
     for(const href of hrefs){if(detailCount>=7)break;if(!href||!href.startsWith('/'+ctrl+'/')||/Pdf|Export|Download|Document|Print|Logout|Mark|Delete/i.test(href)||seen.has(href)||queue.includes(href))continue;if(/\/(Details|Invitation|Offer|CreateOffer|TenderDetails|SupplierDetails|ContactMessageDetails)\b/.test(href)||/[?&](id|tenderId|invitationId|rejectedTenderId)=/.test(href)){queue.push(href);detailCount++;}}
     if(fs.existsSync(axe)){await page.addScriptTag({path:axe});record.a11y=await page.evaluate(async()=>{const r=await axe.run(document.querySelector('main')||document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}});return r.violations.map(v=>({id:v.id,impact:v.impact,description:v.help,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary})).slice(0,5),count:v.nodes.length}));});}
     // Search disclosures and safe Bootstrap modals are opened without submitting actions.
     // Some GET creation routes intentionally open a modal on the workspace.
     const autoModal=page.locator('.modal.show').first();
     if(await autoModal.count()){
      await page.waitForFunction(()=>[...document.querySelectorAll('.modal.show')].every(m=>!bootstrap.Modal.getInstance(m)?._isTransitioning));
      await page.keyboard.press('Escape');await autoModal.waitFor({state:'hidden'});
     }
     const targets=await page.locator('main [data-bs-toggle="modal"]').evaluateAll(es=>es.filter(e=>!e.disabled).map(e=>e.getAttribute('data-bs-target')).filter(Boolean));
     record.modals=[];
     for(const target of [...new Set(targets)].slice(0,3)){
      const trigger=page.locator(`main [data-bs-target="${target}"]`).first();
      if(!await trigger.isVisible())continue;
      await trigger.click({timeout:5000});const modal=page.locator(target);await modal.waitFor({state:'visible'});await page.waitForFunction(selector=>!bootstrap.Modal.getInstance(document.querySelector(selector))?._isTransitioning,target);
      record.modals.push({target,visible:true,overflow:await modal.evaluate(el=>el.scrollWidth>el.clientWidth+1)});await page.keyboard.press('Escape');await modal.waitFor({state:'hidden'});
     }
     await page.setViewportSize({width:390,height:844});await page.waitForFunction(()=>!document.getAnimations().some(a=>a.playState==='running'));
     record.mobile=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth,overflow:document.documentElement.scrollWidth>innerWidth+1}));
     await page.evaluate(()=>document.documentElement.dataset.theme='dark');
     await page.addScriptTag({path:axe});record.darkA11y=await page.evaluate(async()=>{const r=await axe.run(document.querySelector('main')||document,{runOnly:{type:'rule',values:['color-contrast']}});return r.violations.map(v=>({id:v.id,count:v.nodes.length,nodes:v.nodes.slice(0,3).map(n=>({target:n.target,summary:n.failureSummary}))}));});
     if(seen.size<=2){await page.screenshot({path:path.join(out,ctrl+'-'+seen.size+'-mobile-dark.png'),fullPage:true});await page.evaluate(()=>document.documentElement.dataset.theme='light');await page.setViewportSize({width:1440,height:1000});await page.waitForFunction(()=>!document.getAnimations().some(a=>a.playState==='running'));await page.screenshot({path:path.join(out,ctrl+'-'+seen.size+'-desktop.png'),fullPage:true});}
     await page.setViewportSize({width:1440,height:1000});await page.evaluate(()=>document.documentElement.dataset.theme='light');
    }
    record.errors=errors;record.resources=resources;
   }catch(error){record.failure=error.message;}
   finally{page.off('pageerror',onError);page.off('response',onResponse);results.push(record);fs.writeFileSync(path.join(out,'results.json'),JSON.stringify(results,null,2));}
  }
  await context.close();console.log('QA_ROLE '+account.role+' '+seen.size+' routes');
 }
 await browser.close();const summary={pages:results.length,failures:results.filter(r=>r.failure||r.status>=400||r.errors?.length||r.mobile?.overflow||r.resources?.length).map(r=>({role:r.role,route:r.route,status:r.status,failure:r.failure,errors:r.errors,mobile:r.mobile,resources:r.resources})),a11y:results.filter(r=>r.a11y?.length||r.darkA11y?.length).map(r=>({role:r.role,route:r.route,light:r.a11y,dark:r.darkA11y}))};fs.writeFileSync(path.join(out,'summary.json'),JSON.stringify(summary,null,2));console.log(JSON.stringify({pages:summary.pages,failures:summary.failures.length,a11yPages:summary.a11y.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
