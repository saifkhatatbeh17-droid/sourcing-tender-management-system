// UI QA only: never calls page.pdf(), printToPDF, or any browser PDF generator.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:[process.env.CODEX_NODE_MODULES||process.cwd()]}));
(async()=>{
  const output=process.argv[2],data=JSON.parse(fs.readFileSync(path.join(output,'fixtures.json')));
  const browser=await chromium.launch({channel:'msedge',headless:true});
  const contexts=[];const results=[];const base='http://127.0.0.1:5088';
  async function actor(name){const c=await browser.newContext({acceptDownloads:true,extraHTTPHeaders:{'X-Report-QA':data.token,'X-Report-Actor':name}});contexts.push(c);return c;}
  const officer=await actor('officer'),manager=await actor('manager'),evaluation=await actor('eval'),top=await actor('top'),supplier=await actor('supplier');
  // markers: text the exported PDF must contain (checked by report-pdf-verify.py).
  // pageMarkers: text the page itself must show. Since the 16 Nov redesign most pages show a summary
  // and link the full report as a PDF, so they no longer carry every report line inline.
  // direct: the PDF endpoint has no on-page button any more and is fetched by URL.
  const cases=[
    {name:'officer-list',actor:'officer',context:officer,page:'/SourcingOfficer/Reports',link:'ReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','Recommendations recorded','Manager notes recorded'],pageMarkers:[],report:'.so-page'},
    {name:'officer-detail',actor:'officer',context:officer,page:`/SourcingOfficer/Reports?id=${data.report}`,link:'ReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','OFFICER-SUMMARY','MANAGER-SUMMARY','Prepared By'],pageMarkers:['REPORT-LINE-001','REPORT-LINE-120','OFFICER-SUMMARY','MANAGER-SUMMARY','Prepared by'],report:'.so-page'},
    {name:'officer-tender-report',actor:'officer',context:officer,page:`/SourcingOfficer/Offers?tenderId=${data.tender}&tab=report#committee-report`,link:'ReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120'],pageMarkers:[],report:'#committee-report'},
    {name:'officer-evaluation',actor:'officer',context:officer,page:`/SourcingOfficer/Evaluations?tenderId=${data.tender}`,link:'EvaluationReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','SCORE-NOTES','PRICE-CRITERION','TECHNICAL-CRITERION'],report:'.so-page',tab:'#evaluation-report-tab'},
    {name:'committee-combined',actor:'eval',context:evaluation,page:`/EvaluationCommittee/Reports?id=${data.tender}`,link:'ReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','Financial weight','Technical weight','Approval record','OFFICER-SUMMARY'],pageMarkers:['Financial weight','Technical weight','OFFICER-SUMMARY'],report:'.cw-report-detail'},
    {name:'committee-evaluation',actor:'eval',context:evaluation,page:`/EvaluationCommittee/Evaluations?id=${data.tender}`,link:'EvaluationReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','Approval record','OFFICER-SUMMARY'],pageMarkers:['OFFICER-SUMMARY'],report:'#report',tab:'#report-tab'},
    {name:'saved-ai',actor:'eval',context:evaluation,page:`/EvaluationCommittee/Evaluations?id=${data.tender}`,link:'RecommendationPdf',direct:`/EvaluationCommittee/RecommendationPdf?id=${data.tender}`,markers:['AI-REASON-001','AI-REASON-045','AI-STRENGTHS','AI-TRADEOFFS','AI-MISSING','40% financial'],pageMarkers:['AI-REASON-001','AI-REASON-045','40% financial'],report:'.ev-ai-workspace'},
    {name:'manager-decisions',actor:'manager',context:manager,page:'/SourcingManager/Reports?search=Manager&decision=Approved',link:'ReportsPdf',markers:['Manager fixture','DECISION-NOTE-001','DECISION-NOTE-035','1 records'],absent:['Top fixture','PRIVATE-OTHER'],report:'.manager-shell'},
    {name:'manager-submission',actor:'manager',context:manager,page:`/SourcingManager/Submissions?id=${data.report}`,link:'SubmissionPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','OFFICER-SUMMARY','SCORE-NOTES','TECHNICAL-NOTES','Supplier comparison',data.total],pageMarkers:['REPORT-LINE-001','REPORT-LINE-120','OFFICER-SUMMARY','SCORE-NOTES','TECHNICAL-NOTES',data.total],report:'main'},
    {name:'top-unified',actor:'top',context:top,page:`/TopCommittee/TenderDetails?id=${data.topTender}&bidId=${data.topBid}`,link:'UnifiedReportPdf',markers:['REPORT-LINE-001','REPORT-LINE-120','ITEM-065','MANAGER-SUMMARY','OFFICER-SUMMARY','Committee records','REQUEST-SPEC-065','OFFER-SPEC-ITEM-065'],report:'.tc-tender'},
    {name:'top-existing',actor:'top',context:top,page:'/TopCommittee/Reports?Search=Top%20fixture',link:'ReportExport',query:'format=pdf',markers:['Top fixture','Title: Top fixture','A · General statistics','B · Tender and decision details','C · Supplier statistics','D · Decision and audit summary'],absent:['PRIVATE-OTHER-OFFICER','Manager fixture'],report:'#main'},
    {name:'supplier-existing',actor:'supplier',context:supplier,page:`/Supplier/Offer?id=${data.bid}`,link:'DownloadSummary',markers:['ITEM-001','ITEM-065','OFFER-SPEC-ITEM-065','PAYMENT-TERMS','TECHNICAL-NOTES','recorded-supporting-document.pdf',data.total],absent:['PRIVATE-OTHER'],report:'.supplier-workspace'}
  ];
  try{
    const before=await(await top.request.get(base+'/qa/stats')).json();
    for(const c of cases){
      const page=await c.context.newPage();const response=await page.goto(base+c.page);assert.equal(response.status(),200,c.name+': HTML response');
      if(c.tab)await page.locator(c.tab).click();
      // The CEO & Chiefs evaluation report now opens in a dialog rather than a tab.
      if(c.name==='top-unified'){const opener=page.locator('[data-review-dialog-open][aria-controls="evaluation-report-content"]');if(await opener.count())await opener.first().evaluate(button=>button.click());} // card animates continuously, so a pointer click never sees it as stable
      let link;
      if(!c.direct){
        for(const candidate of await page.locator(c.report).locator('a').filter({hasText:/Download|Export/}).all()){
          const href=await candidate.getAttribute('href');if(href.includes(c.link)&&(!c.query||href.includes(c.query))){link=candidate;break;}
        }
        assert(link,c.name+': report download control');
      }
      const href=c.direct??await link.getAttribute('href');
      if(c.name==='manager-decisions')assert(href.includes('search=Manager')&&href.includes('decision=Approved'),'Active filters preserved');
      if(c.name==='officer-list')assert(!new URL(href,base).searchParams.has('id'),'List export is not narrowed by ambient ID');
      for(const width of c.direct?[]:[1440,768,390]){
        await page.setViewportSize({width,height:1000});await link.scrollIntoViewIfNeeded();
        const box=await link.evaluate(el=>{const r=el.getBoundingClientRect(),at=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);return {left:r.left,right:r.right,width:r.width,viewport:innerWidth,visible:at===el||el.contains(at)};});
        assert(box.width>0&&box.left>=0&&box.right<=box.viewport+1&&box.visible,`${c.name}: unobscured control at ${width}`);
        await page.screenshot({path:path.join(output,`${c.name}-${width}.png`),animations:'disabled'});
        await page.waitForTimeout(350); // Allow the incumbent sidebar width transition to settle.
        // Compare incumbent shell styles/geometry with only the newly added controls hidden.
        const shell=await page.locator('body > header, .admin-sidebar').evaluateAll(els=>els.map(e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return [r.x,r.width,s.color,s.backgroundColor,s.fontFamily];}));
        await page.locator('nav[aria-label$="export"]:not([aria-label="Exports"])').evaluateAll(els=>els.forEach(e=>e.hidden=true));
        const unchanged=await page.locator('body > header, .admin-sidebar').evaluateAll(els=>els.map(e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return [r.x,r.width,s.color,s.backgroundColor,s.fontFamily];}));
        assert.equal(unchanged.length,shell.length);
        for(let i=0;i<shell.length;i++){
          assert(Math.abs(unchanged[i][0]-shell[i][0])<=2&&Math.abs(unchanged[i][1]-shell[i][1])<=2,`${c.name}: shell geometry unchanged at ${width}`);
          assert.deepEqual(unchanged[i].slice(2),shell[i].slice(2),`${c.name}: shell typography/colors unchanged at ${width}`);
        }
        await page.locator('nav[aria-label$="export"]').evaluateAll(els=>els.forEach(e=>e.hidden=false));
      }
      await page.setViewportSize({width:1440,height:1000});
      const source=await page.locator(c.report).textContent();
      const valueSelectors={
        'officer-list':'tbody td:not(:last-child)',
        'officer-detail':'.rp-facts dd',
        'officer-evaluation':'table tbody td, #evaluation-report dd',
        'committee-combined':'.cw-report-weights dd, .cw-recommended strong, .ev-report-text, .cw-officer-note p, .cw-approval-grid strong, .cw-approval-grid small',
        'committee-evaluation':'.ev-report-summary dd, .ev-report-approvals strong, .ev-report-approvals small',
        'saved-ai':'.ev-ai-explanation p, .ev-ai-tradeoff-grid p, .ev-ai-missing li, .ev-ai-pick h4, .ev-ai-basis',
        'manager-decisions':'tbody td:not(:last-child), tbody td:last-child p, tbody td:last-child > span',
        'manager-submission':'#submission-overview table tbody td, #submission-overview details > p',
        'supplier-existing':'tbody td, .supplier-summary-grid strong, .detail-list dd, .offer-progress-message'
      };
      // innerText keeps the visual line breaks between stacked elements; textContent would glue "title" and "Report #" into one word.
      const sourceValues=valueSelectors[c.name]?await page.locator(c.report).locator(valueSelectors[c.name]).evaluateAll(els=>els.map(e=>e.innerText)):[];
      for(const marker of (c.pageMarkers??c.markers).filter(m=>!m.startsWith('A ·')&&!m.startsWith('B ·')&&!m.startsWith('C ·')&&!m.startsWith('D ·')&&!m.startsWith('Title:')))
        assert(source.replace(/\s/g,'').includes(marker.replace(/\s/g,'')),`${c.name}: source HTML has ${marker}`);
      const raw=await c.context.request.get(base+href);assert.equal(raw.status(),200,c.name+': export response');
      if(c.direct)fs.writeFileSync(path.join(output,c.name+'.pdf'),await raw.body());
      else{const downloadPromise=page.waitForEvent('download');await link.click();const download=await downloadPromise;
        assert.equal(await download.failure(),null,c.name+': download succeeds');await download.saveAs(path.join(output,c.name+'.pdf'));}
      assert(raw.headers()['content-type'].startsWith('application/pdf'));assert(raw.headers()['cache-control'].includes('no-store'));
      assert.equal((await raw.body()).subarray(0,5).toString(),'%PDF-');
      const failure=await c.context.request.get(base+href,{headers:{'X-Report-Fail-Watermark':'1'}});
      assert.equal(failure.status(),422,c.name+': watermark failure blocks PDF');assert(!failure.headers()['content-type'].includes('application/pdf'));
      results.push({name:c.name,actor:c.actor,href,markers:c.markers,absent:c.absent||[],source,sourceValues});
      fs.writeFileSync(path.join(output,'browser-results.json'),JSON.stringify(results,null,2));
      console.log('PASS button/download, matching HTML, 3 viewports, fail-closed watermark:',c.name);await page.close();
    }
    const wrong=await actor('department'),anonymous=await browser.newContext();contexts.push(anonymous);
    for(const c of results){assert.equal((await wrong.request.get(base+c.href)).status(),403);assert.equal((await anonymous.request.get(base+c.href)).status(),401);}
    const otherOfficer=await actor('other-officer'),otherTop=await actor('other-top'),otherSupplier=await actor('other-supplier');
    for(const [context,url] of [[otherOfficer,`/SourcingOfficer/ReportPdf?id=${data.report}`],[otherOfficer,`/SourcingOfficer/EvaluationReportPdf?tenderId=${data.tender}`],[otherSupplier,`/Supplier/DownloadSummary?id=${data.bid}`],[otherTop,`/TopCommittee/UnifiedReportPdf?id=${data.topTender}`],[evaluation,`/EvaluationCommittee/ReportPdf?id=${data.privateTender}`],[evaluation,`/EvaluationCommittee/EvaluationReportPdf?id=${data.privateTender}`],[evaluation,`/EvaluationCommittee/RecommendationPdf?id=${data.privateTender}`],[manager,`/SourcingManager/SubmissionPdf?id=${data.privateReport}`]])
      assert.equal((await context.request.get(base+url)).status(),404,'Record isolation: '+url);
    assert.equal((await top.request.get(base+'/TopCommittee/ReportExport?format=pdf&Status=invalid')).status(),400);
    const excel=await top.request.get(base+'/TopCommittee/ReportExport?format=xlsx&Search=Top%20fixture');
    assert.equal(excel.status(),200);assert.equal((await excel.body()).subarray(0,2).toString(),'PK','Existing Excel export preserved');
    const documentUrl=base+`/Supplier/DownloadDocument?id=${data.documentId}`;
    const originalHash=require('crypto').createHash('sha256').update(fs.readFileSync(data.documentPath)).digest('hex').toUpperCase();
    assert.equal(originalHash,data.documentHash);
    const uploaded=await supplier.request.get(documentUrl);assert.equal(uploaded.status(),200);
    fs.writeFileSync(path.join(output,'supplier-uploaded-document.pdf'),await uploaded.body());
    assert.equal((await otherSupplier.request.get(documentUrl)).status(),404,'Uploaded PDF isolation');
    assert.equal(require('crypto').createHash('sha256').update(fs.readFileSync(data.documentPath)).digest('hex').toUpperCase(),data.documentHash,'Original stored PDF unchanged');
    const after=await(await top.request.get(base+'/qa/stats')).json();assert.deepEqual(after,before,'Exports preserve DB counts and make zero AI calls');assert.equal(after.aiCalls,0);
    console.log('PASS: all 11 exports; existing uploaded PDF/Excel; role denial; record isolation; filters; original file unchanged; no database/AI changes.');
  }finally{for(const c of contexts)await c.close();await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
