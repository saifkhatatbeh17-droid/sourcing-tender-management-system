const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync('wwwroot/js/face-login-inline.js', 'utf8');
function setup(mode = 'match') {
    const events = {}, modalEvents = {}, calls = [], states = [];
    let stopped = 0, redirected = null, shown = false, resolveCamera, captureTask;
    const preview = { dataset:{} }, snapshot = { hidden:true, getContext:()=>({drawImage(){}}) };
    const password = { value: 'test-only-password' }, error = { hidden: true }, feedback = {}, video = { videoWidth: 640, videoHeight: 480, play: async () => {} };
    const form = { reportValidity: () => mode !== 'invalid', querySelector: () => password, addEventListener: (k,v) => events[k] = v };
    const button = { form, formAction: 'http://localhost/Account/LoginWithFaceId' };
    const element = { addEventListener: (k,v) => modalEvents[k] = v };
    const host = { dataset: { faceApiUrl:'/lib/face-api/face-api.js',modelUrl:'/lib/face-api/model',modelIdentifier:'test-model' } };
    const camera = { getTracks: () => [{ stop: () => stopped++ }] };
    const modal = { show() { shown = true; captureTask = modalEvents['shown.bs.modal']?.(); }, hide() { let prevented=false; modalEvents['hide.bs.modal']?.({preventDefault(){prevented=true;}}); if(!prevented) shown=false; } };
    const faces = mode === 'bad-descriptor' ? [{ descriptor:[NaN], detection:{box:{width:400,height:400}} }] : [{ descriptor:Array(128).fill(.01),detection:{box:{width:400,height:400}} }];
    const api = { nets:Object.fromEntries(['tinyFaceDetector','faceLandmark68Net','faceRecognitionNet'].map(k=>[k,{loadFromUri:async()=>{}}])), TinyFaceDetectorOptions:function(){}, detectAllFaces:()=>({withFaceLandmarks:()=>({withFaceDescriptors:async()=>faces})}) };
    const location = { href:'http://localhost/Account/Login',origin:'http://localhost',assign:url=>redirected=url };
    const ids={'face-login-button':button,'face-login-host':host,'face-signin-modal':element,'face-login-error':error,'face-signin-video':video,'face-signin-feedback':feedback,'face-signin-preview':preview,'face-signin-snapshot':snapshot};
    const context={window:{bootstrap:{Modal:{getOrCreateInstance:()=>modal}},faceapi:api,isSecureContext:true,addEventListener(){}},faceapi:api,bootstrap:{Modal:{getOrCreateInstance:()=>modal}},document:{getElementById:k=>ids[k],dispatchEvent:e=>states.push(e.detail),addEventListener(){}},location,URL,CustomEvent:class{constructor(type,arg){this.detail=arg.detail;}},FormData:class{constructor(){this.values=[];}append(k,v){this.values.push([k,v]);}},navigator:{mediaDevices:{getUserMedia:async()=>{if(mode==='denied'){const e=new Error();e.name='NotAllowedError';throw e;} if(mode==='cancel')return await new Promise(r=>resolveCamera=r);return camera;}}},fetch:async(url,options)=>{calls.push({url,options});return{ok:mode==='match',status:mode==='match'?200:400,json:async()=>mode==='match'?{redirectUrl:'/Account/GoToDashboard'}:{message:'Password and face must match.',code:mode==='mismatch'?'face_not_matched':undefined}};},setTimeout,Date};
    vm.runInNewContext(source,context);
    function submit(face=true){let prevented=false;events.submit({submitter:face?button:{},preventDefault(){prevented=true;}});return prevented;}
    return {submit,start:()=>captureTask,cancel:()=>modal.hide(),resolveCamera:()=>resolveCamera(camera),calls,password,error,states,preview,snapshot,feedback,get shown(){return shown;},get stopped(){return stopped;},get redirected(){return redirected;}};
}
(async()=>{
    const normal=setup();assert.equal(normal.submit(false),false);assert.equal(normal.calls.length,0);
    const invalid=setup('invalid');invalid.submit();assert.equal(invalid.shown,false);
    const match=setup();assert(match.submit());assert(match.shown);assert.equal(match.calls.length,0,'No password-only request before capture');await match.start();assert.equal(match.calls.length,1);assert.equal(match.calls[0].options.body.values.filter(([k])=>k.startsWith('Descriptor[')).length,128);assert(match.redirected.endsWith('/Account/GoToDashboard'));assert.equal(match.preview.dataset.state,'matched');assert.equal(match.snapshot.hidden,false);assert.equal(match.password.value,'');assert(match.stopped>0);
    for(const mode of ['mismatch','wrong-password','denied','bad-descriptor']){const test=setup(mode);test.submit();await test.start();assert.equal(test.redirected,null);assert.equal(test.password.value,'');assert.equal(test.preview.dataset.state,'failed');assert(!test.states.includes('verified'));if(mode==='mismatch')assert.equal(test.feedback.textContent,'Face not matched');if(mode==='wrong-password')assert.equal(test.feedback.textContent,'Sign-in not verified');if(mode==='denied'||mode==='bad-descriptor')assert.equal(test.calls.length,0);}
    const cancel=setup('cancel');cancel.submit();const pending=cancel.start();for(let i=0;i<8;i++)await new Promise(setImmediate);cancel.cancel();cancel.resolveCamera();await pending;assert.equal(cancel.calls.length,0);assert.equal(cancel.password.value,'');assert(cancel.stopped>0,'Late permission response also stops camera');
    console.log('Face login client checks passed: normal login, invalid fields, no premature auth, both-factor payload, matching redirect, mismatch, camera denial, invalid descriptor and cancellation.');
})().catch(e=>{console.error(e);process.exitCode=1;});
