const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync('wwwroot/js/ai-chat.js', 'utf8');
const control = () => ({value:'', files:[], hidden:false, disabled:false, events:{}, addEventListener(k, fn){this.events[k]=fn;}});
const file = control(), name = control(), remove = control(), options = control(), role = control(), help = control();
const mode = control(), modeOptions = control();mode.value='accounts';
role.value='SourcingOfficer';
Object.defineProperty(file,'value',{set(value){if(value==='')this.files=[];},get(){return '';}});
const selectors={'[data-chat-attachment]':file,'[data-chat-file-name]':name,'[data-chat-remove-file]':remove,'[data-chat-import-options]':options,'[data-chat-import-role]':role,'[data-chat-file-help]':help};
selectors['[data-chat-excel-mode]']=mode;selectors['[data-chat-excel-mode-options]']=modeOptions;
const requests=[], responses=[];
const form={action:'/AiChat/Ask',elements:{__RequestVerificationToken:{value:'test-token'}},events:{},querySelector:s=>selectors[s],addEventListener(k,fn){this.events[k]=fn;}};
const question={value:'',required:true,focus(){}}, send={}, label={}, status={dataset:{}}, saved=[];
let closed=0;
const context=vm.createContext({form,questionInput:question,send,sendLabel:label,status,clearButton:{},chat:{dataset:{workspace:'Admin'}},
 window:{clearTimeout(){},setTimeout(){},location:{pathname:'/Admin/Dashboard',search:'',assign(){}}},
 FormData:class {constructor(){this.values=new Map();}append(k,v){this.values.set(k,v);}get(k){return this.values.get(k);}},
 closeVoice(){closed++;},scrollToLatest(){},setThinkingState(){},listenAgain(){},
 renderEntry(entry,save){if(save)saved.push(entry);},saveEntry:e=>saved.push(e),persistEntries(){},
 fetch:async(url,init)=>{requests.push({url,...init});return {ok:true,status:200,redirected:false,json:async()=>responses.shift()};},
 document:{},console,Number,Array,String,TypeError});
vm.runInContext('let pending=false,failedQuestion="",voiceEnabled=false,listenTimer,stopSpeech; const storedEntries=[],historyLimit=12;',context);
vm.runInContext(source.slice(source.indexOf('    const attachmentInput ='), source.indexOf('    const storageKey =')),context);
vm.runInContext(source.slice(source.indexOf('    function updateComposer()'),source.indexOf('    questionInput.addEventListener("input"')),context);
vm.runInContext(source.slice(source.indexOf('    form.addEventListener("submit"'),source.lastIndexOf('})();')),context);
(async()=>{
 file.files=[{name:'accounts.xlsx',size:1000}];file.events.change();
 assert.equal(question.required,false,'file-only requests must pass native form validation');
 assert.equal(send.disabled,false);assert.equal(options.hidden,false);assert(closed>0,'voice is stopped before import');
 responses.push({answer:'Preview ready',previewReady:true,importRows:[]});
 await form.events.submit({preventDefault(){}});
 assert.equal(requests[0].url,'/AiChat/Attachment');assert.equal(requests[0].body.get('confirmImport'),'false');
 assert.equal(label.textContent,'Confirm account creation');assert.equal(file.files.length,1);
 responses.push({answer:'Created',mutated:true,previewReady:false});
 await form.events.submit({preventDefault(){}});
 assert.equal(requests[1].body.get('confirmImport'),'true');assert.equal(file.files.length,0);assert.equal(question.required,true);
 file.files=[{name:'screen.png',size:1000}];file.events.change();assert.equal(options.hidden,true);
 responses.push({answer:'Image explanation',previewReady:false});
 await form.events.submit({preventDefault(){}});
 assert.equal(requests[2].body.get('confirmImport'),'false');assert.equal(requests[2].body.get('file').name,'screen.png');
 question.value='Show my tenders';context.questionInput.value=question.value;
 responses.push({answer:'Your tenders'});await form.events.submit({preventDefault(){}});
 assert.equal(requests[3].url,'/AiChat/Ask');assert.equal(requests[3].headers['Content-Type'],'application/json');
 file.files=[{name:'quotation.pdf',size:1000}];file.events.change();assert.equal(options.hidden,true);
 responses.push({answer:'PDF explanation',previewReady:false});await form.events.submit({preventDefault(){}});
 assert.equal(requests[4].url,'/AiChat/Attachment');assert.match(requests[4].body.get('question'),/الملف/);
 mode.value='analyze';file.files=[{name:'offers.xlsx',size:1000}];file.events.change();assert.equal(options.hidden,true);
 responses.push({answer:'Excel analysis',previewReady:false,mutated:false});await form.events.submit({preventDefault(){}});
 assert.equal(requests[5].body.get('intent'),'analyze');assert.equal(requests[5].body.get('confirmImport'),'false');assert.match(requests[5].body.get('question'),/حلّل/);
 file.files=[{name:'huge.png',size:6*1024*1024}];file.events.change();assert.equal(file.files.length,0);assert.match(status.textContent,/5 MB/);
 assert(!requests.some(r=>r.body?.get?.('password')),'passwords are not separate chat fields');
 console.log('PASS: attachment composer native validity, explicit preview/confirmation, voice isolation, image upload, normal text fallback and size validation.');
})().catch(e=>{console.error(e);process.exitCode=1;});
