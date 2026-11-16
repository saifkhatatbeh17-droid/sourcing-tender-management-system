/* Login-only decoration. Never reads input values, submits forms or controls navigation. */
const host = document.getElementById('pharma-scene');
document.querySelectorAll('#login-stage .login-panel > *').forEach((element,index)=>element.style.setProperty('--i',index));
if (host) start().catch(() => { host.dataset.render = 'fallback'; });

async function start() {
    const T = await import('../lib/login-scene/three.module.js');
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const mobile = matchMedia('(max-width: 760px)');
    const renderer = new T.WebGLRenderer({ antialias:true, alpha:true, powerPreference:'low-power' });
    renderer.setPixelRatio(Math.min(devicePixelRatio, mobile.matches ? 1.25 : 1.75));
    renderer.outputColorSpace = T.SRGBColorSpace;
    renderer.toneMapping = T.ACESFilmicToneMapping;
    renderer.toneMappingExposure = .96;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = T.VSMShadowMap;
    host.appendChild(renderer.domElement);
    const scene = new T.Scene();
    const camera = new T.PerspectiveCamera(33, 1, .1, 60);
    camera.position.set(0, 1.5, 10.4);
    camera.lookAt(0, .25, 0);

    // Procedural photographic softboxes produce broad, believable surface reflections.
    const studio = new T.Scene();
    studio.background = new T.Color('#667773');
    function softbox(x,y,z,w,h,intensity) {
        const box = new T.Mesh(new T.PlaneGeometry(w,h),new T.MeshBasicMaterial({color:new T.Color(intensity,intensity,intensity),side:T.DoubleSide}));
        box.position.set(x,y,z); box.lookAt(0,0,0); studio.add(box);
    }
    softbox(-3,3,3,2,6,5); softbox(4,1,1,1.5,5,3); softbox(0,5,-2,5,2,4);
    const warm=new T.Mesh(new T.PlaneGeometry(1,4),new T.MeshBasicMaterial({color:new T.Color('#ff6b4a').multiplyScalar(1.6),side:T.DoubleSide}));warm.position.set(3,-1,-3);warm.lookAt(0,0,0);studio.add(warm);
    const pmrem = new T.PMREMGenerator(renderer);
    const environment = pmrem.fromScene(studio,.08);
    scene.environment = environment.texture;
    pmrem.dispose();
    studio.traverse(o => { o.geometry?.dispose(); o.material?.dispose(); });
    scene.add(new T.HemisphereLight('#f5fff2','#193936',.9));
    const key = new T.DirectionalLight('#fff8ef',3.2);
    key.position.set(-3,5,5); key.castShadow = true;
    key.shadow.mapSize.set(1024,1024); key.shadow.camera.left=-4; key.shadow.camera.right=4;
    key.shadow.camera.top=5; key.shadow.camera.bottom=-4; key.shadow.normalBias=.035;
    key.shadow.radius=5; key.shadow.blurSamples=8; scene.add(key);
    const rim = new T.DirectionalLight('#c4ded9',2); rim.position.set(4,2,-3); scene.add(rim);
    const fill=new T.DirectionalLight('#f6fff9',.65); fill.position.set(2,0,5); scene.add(fill);
    const reaction = new T.PointLight('#00a3a6',0,8,2); reaction.position.set(-1,1,2.4); scene.add(reaction);

    const product = new T.Group();
    product.rotation.set(.05,-.13,-.16);
    scene.add(product);
    // A microscopic molded-HDPE grain breaks up perfect computer-smooth highlights.
    const grainCanvas=document.createElement('canvas'); grainCanvas.width=256; grainCanvas.height=256;
    const grainContext=grainCanvas.getContext('2d'),grainPixels=grainContext.createImageData(256,256);
    let noiseSeed=8191;
    for(let i=0;i<grainPixels.data.length;i+=4) {
        noiseSeed=(noiseSeed*1664525+1013904223)>>>0;
        const v=115+(noiseSeed>>>27);grainPixels.data.set([v,v,v,255],i);
    }
    grainContext.putImageData(grainPixels,0,0);
    const grain=new T.CanvasTexture(grainCanvas);grain.wrapS=grain.wrapT=T.RepeatWrapping;grain.repeat.set(8,8);
    const plastic = new T.MeshPhysicalMaterial({color:'#eef1e6',roughness:.26,metalness:0,clearcoat:.45,clearcoatRoughness:.18,sheen:1,sheenColor:new T.Color('#cfe9e2'),sheenRoughness:.5,envMapIntensity:1.1,bumpMap:grain,bumpScale:.005});
    const teal = new T.MeshPhysicalMaterial({color:'#305b5c',roughness:.28,metalness:.08,clearcoat:.55,envMapIntensity:.9});
    const orange = new T.MeshStandardMaterial({color:'#ff6b4a',roughness:.48});
    function lathe(points,material,parent=product) {
        const mesh=new T.Mesh(new T.LatheGeometry(points.map(p=>new T.Vector2(...p)),128),material);
        mesh.castShadow=true; mesh.receiveShadow=true; parent.add(mesh); return mesh;
    }
    // Continuous outer wall, rolled rim, inner neck and inner cavity: a genuinely open vessel.
    lathe([[0,-1.7],[.63,-1.7],[.72,-1.695],[.785,-1.675],[.835,-1.64],[.866,-1.59],[.878,-1.53],[.88,-1.46],[.88,.66],[.877,.75],[.865,.82],[.845,.88],[.815,.94],[.774,.993],[.723,1.043],[.67,1.085],[.622,1.124],[.585,1.163],[.567,1.205],[.56,1.25],[.56,1.56],[.555,1.593],[.54,1.613],[.52,1.62],[.48,1.62]],plastic);
    lathe([[.48,1.62],[.46,1.58],[.46,1.26],[.51,1.13],[.67,.96],[.76,.75],[.76,-1.46],[.67,-1.56],[0,-1.56]],new T.MeshStandardMaterial({color:'#9eafa0',roughness:.8,side:T.DoubleSide}));
    // Neck screw threads remain visible beneath the lifted cap.
    for(let i=0;i<3;i++) {
        const thread=new T.Mesh(new T.TorusGeometry(.563,.018,8,96),plastic);
        thread.rotation.x=Math.PI/2; thread.position.y=1.29+i*.095; product.add(thread);
    }
    lathe([[.873,-1.48],[.887,-1.47],[.887,-1.44],[.873,-1.43]],plastic);
    const cap = new T.Group(); product.add(cap);
    lathe([[0,-.25],[.56,-.25],[.62,-.23],[.64,-.18],[.64,.2],[.635,.25],[.61,.29],[.56,.31],[.5,.31],[.46,.295],[.4,.285],[0,.285]],teal,cap);
    const ribGeometry=new T.CapsuleGeometry(.012,.34,3,5);
    const ribs=new T.InstancedMesh(ribGeometry,teal,100), ribTransform=new T.Object3D();
    for(let i=0;i<100;i++) {
        const a=i/100*Math.PI*2;
        ribTransform.position.set(Math.sin(a)*.641,.005,Math.cos(a)*.641);
        ribTransform.updateMatrix(); ribs.setMatrixAt(i,ribTransform.matrix);
    }
    cap.add(ribs);
    const arc=new T.Mesh(new T.TorusGeometry(.3,.012,8,48,Math.PI*1.5),orange);arc.rotation.x=Math.PI/2;arc.position.y=.294;cap.add(arc);
    const head=new T.Mesh(new T.ConeGeometry(.05,.12,12),orange);head.rotation.z=-Math.PI/2;head.position.set(.05,.294,-.3);cap.add(head);
    lathe([[.61,-.25],[.645,-.24],[.645,-.205],[.61,-.2]],orange,cap);
    cap.position.y=1.53;

    // A high resolution wrap label, authored here to keep all assets within the Login scope.
    const labelCanvas=document.createElement('canvas'); labelCanvas.width=2048; labelCanvas.height=1024;
    const ctx=labelCanvas.getContext('2d');
    ctx.fillStyle='#f2f1e7'; ctx.fillRect(0,0,2048,1024);
    ctx.fillStyle='#305b5c'; ctx.fillRect(0,0,2048,14); ctx.fillRect(0,900,2048,124);
    ctx.textAlign='center'; ctx.fillStyle='#234b49';
    ctx.font='600 114px Segoe UI, sans-serif'; ctx.fillText('TIRYAQ',1024,402);
    ctx.fillStyle='#ff6b4a'; ctx.fillRect(986,450,76,8);
    ctx.fillStyle='#234b49'; ctx.font='500 32px Segoe UI, sans-serif';
    ctx.letterSpacing='3px'; ctx.fillText('SOURCING & TENDER',1024,535); ctx.fillText('MANAGEMENT',1024,585);
    ctx.fillStyle='#91a69b'; ctx.fillRect(880,675,288,2);
    ctx.font='400 24px Segoe UI, sans-serif'; ctx.fillStyle='#506f68'; ctx.fillText('ترياق',1024,760);
    ctx.fillStyle='#edf3e9';ctx.font='400 21px Segoe UI, sans-serif';ctx.fillText('T I R Y A Q',1024,973);
    ctx.fillStyle='#ff6b4a';ctx.fillRect(0,886,2048,8);
    ctx.fillStyle='#c5d1c7';[[360,800],[300,826],[330,852]].forEach(([w,y])=>ctx.fillRect(1024-w/2,y,w,6));
    ctx.fillStyle='#305b5c';ctx.fillRect(994,108,60,24);ctx.fillRect(1012,90,24,60);ctx.fillStyle='#ff6b4a';ctx.beginPath();ctx.arc(1024,120,7,0,7);ctx.fill();
    const texture=new T.CanvasTexture(labelCanvas); texture.colorSpace=T.SRGBColorSpace; texture.anisotropy=Math.min(renderer.capabilities.getMaxAnisotropy(),8);
    const labelMaterial=new T.MeshPhysicalMaterial({map:texture,roughness:.58,clearcoat:.1,bumpMap:grain,bumpScale:.002});
    const label=new T.Mesh(new T.CylinderGeometry(.886,.886,1.65,128,1,true),labelMaterial);
    label.position.y=-.3; label.rotation.y=Math.PI; product.add(label);
    [.525,-1.125].forEach(y=>{const e=new T.Mesh(new T.TorusGeometry(.887,.006,6,128),new T.MeshStandardMaterial({color:'#d9dccd',roughness:.6}));e.rotation.x=Math.PI/2;e.position.y=y;product.add(e);});
    const brand=document.querySelector('.login-brand img');
    await brand.decode().catch(()=>{});
    if(brand.naturalWidth) { // Finish the real label before the first visible frame.
        ctx.fillStyle='#f2f1e7'; ctx.fillRect(745,215,558,214);
        ctx.drawImage(brand,774,247,500,167); texture.needsUpdate=true;
    }

    const scanMaterial=new T.MeshBasicMaterial({color:'#b8e4d6',transparent:true,opacity:0,depthWrite:false});
    const scan=new T.Mesh(new T.CylinderGeometry(.895,.895,.035,128,1,true),scanMaterial); product.add(scan);
    const ringMaterial=new T.MeshBasicMaterial({color:'#9ec7bc',transparent:true,opacity:0,depthWrite:false});
    const ring=new T.Mesh(new T.TorusGeometry(1.3,.008,8,128),ringMaterial);
    ring.rotation.x=Math.PI/2; ring.position.y=-1.65; product.add(ring);

    // Biconvex caplets: curved faces, a rounded compression edge and recessed score.
    const tabletShape=new T.Shape();
    tabletShape.absarc(0,.14,.14,0,Math.PI,false); tabletShape.absarc(0,-.14,.14,Math.PI,Math.PI*2,false);
    tabletShape.closePath();
    const outline=tabletShape.getSpacedPoints(96),vertices=[],uvs=[],indices=[];
    const layers=32,segments=96;
    for(let j=0;j<=layers;j++) {
        const a=j/layers*Math.PI,scale=Math.pow(Math.sin(a),.65);
        for(let i=0;i<=segments;i++) {
            const p=outline[i],x=p.x*scale,y=p.y*scale;
            const groove=Math.exp(-Math.pow(y/.008,2))*.005*Math.pow(Math.abs(Math.cos(a)),3);
            const z=Math.cos(a)*.064-Math.sign(Math.cos(a))*groove;
            vertices.push(x,y,z);uvs.push(x/.28+.5,y/.56+.5);
            if(j<layers&&i<segments) {const v=j*(segments+1)+i;indices.push(v,v+segments+1,v+1,v+1,v+segments+1,v+segments+2);}
        }
    }
    const tabletGeo=new T.BufferGeometry();tabletGeo.setAttribute('position',new T.Float32BufferAttribute(vertices,3));
    tabletGeo.setAttribute('uv',new T.Float32BufferAttribute(uvs,2));tabletGeo.setIndex(indices);tabletGeo.computeVertexNormals();
    const normals=tabletGeo.getAttribute('normal'),seamNormal=new T.Vector3();
    for(let j=0;j<=layers;j++) {
        const first=j*(segments+1),last=first+segments;
        seamNormal.fromBufferAttribute(normals,first).add(new T.Vector3().fromBufferAttribute(normals,last)).normalize();
        normals.setXYZ(first,seamNormal.x,seamNormal.y,seamNormal.z);normals.setXYZ(last,seamNormal.x,seamNormal.y,seamNormal.z);
        if(j===0||j===layers) for(let i=0;i<=segments;i++) normals.setXYZ(first+i,0,0,j===0?1:-1);
    }
    const tabletSurface=document.createElement('canvas');tabletSurface.width=256;tabletSurface.height=512;
    const powder=tabletSurface.getContext('2d');powder.fillStyle='#808080';powder.fillRect(0,0,256,512);
    for(let i=0;i<7000;i++) {
        noiseSeed=(noiseSeed*1664525+1013904223)>>>0;
        powder.fillStyle=i%2?'#898989':'#777777';powder.fillRect(noiseSeed%256,(noiseSeed>>>8)%512,1,1);
    }
    powder.textAlign='center';powder.fillStyle='#454545';powder.font='600 52px Segoe UI, sans-serif';
    powder.fillText('TQ',128,168);
    const tabletTexture=new T.CanvasTexture(tabletSurface);
    const tabletMat=new T.MeshPhysicalMaterial({color:'#f4f0e3',roughness:.57,clearcoat:.08,bumpMap:tabletTexture,bumpScale:.0035});
    const tabletEdgeMaterial=new T.MeshStandardMaterial({color:'#d5d7c7',roughness:.8});
    const edgeCurve=new T.CatmullRomCurve3(outline.slice(0,-1).map(p=>new T.Vector3(p.x,p.y,0)),true);
    const edgeGeometry=new T.TubeGeometry(edgeCurve,96,.0018,4,true);
    const glowCv=document.createElement('canvas');glowCv.width=glowCv.height=64;
    const gc=glowCv.getContext('2d'),gg=gc.createRadialGradient(32,32,0,32,32,32);
    gg.addColorStop(0,'rgba(100,244,220,.9)');gg.addColorStop(.4,'rgba(24,197,200,.28)');gg.addColorStop(1,'rgba(24,197,200,0)');gc.fillStyle=gg;gc.fillRect(0,0,64,64);
    const glowTex=new T.CanvasTexture(glowCv);
    function capsule(col) {
        const pts=[[0,0],[.13,0],[.13,.15]];
        for(let k=1;k<=16;k++){const a=k/16*Math.PI/2;pts.push([Math.cos(a)*.13,.15+Math.sin(a)*.13]);}
        const geo=new T.LatheGeometry(pts.map(q=>new T.Vector2(...q)),48);
        const mk=c=>new T.MeshPhysicalMaterial({color:c,roughness:.16,clearcoat:1,clearcoatRoughness:.06,envMapIntensity:1.2});
        const a=new T.Mesh(geo,mk(col)),b=new T.Mesh(geo,mk('#f4f0e3'));b.scale.y=-1;a.castShadow=b.castShadow=true;
        const band=new T.Mesh(new T.TorusGeometry(.131,.007,6,48),mk('#f4f0e3'));band.rotation.x=Math.PI/2;
        return [a,b,band];
    }
    const roundPts=[];for(let k=0;k<=24;k++){const a=k/24*Math.PI;roundPts.push(new T.Vector2(.2*Math.pow(Math.sin(a),.6),Math.cos(a)*.055));}
    const roundGeo=new T.LatheGeometry(roundPts.reverse(),64);roundGeo.rotateX(Math.PI/2);
    const tablets=[];
    const positions=[[-1.64,1.55,.25],[1.55,1.14,-.3],[-1.65,-.2,.55],[1.63,-.65,.45],[-1.15,-1.8,1.1],[1.13,2.15,-.65],[.18,-2.05,.6]];
    positions.forEach((p,i)=>{
        const g=new T.Group(), kind=i%4;
        if(kind===0){const body=new T.Mesh(tabletGeo,tabletMat);body.castShadow=true;g.add(body);g.add(new T.Mesh(edgeGeometry,tabletEdgeMaterial));}
        else if(kind===3){const r=new T.Mesh(roundGeo,tabletMat);r.castShadow=true;g.add(r);const sc=new T.Mesh(new T.BoxGeometry(.24,.012,.018),tabletEdgeMaterial);sc.position.z=.05;g.add(sc);}
        else capsule(kind===1?'#1fa6a3':'#ff6b4a').forEach(m=>g.add(m));
        const glow=new T.Sprite(new T.SpriteMaterial({map:glowTex,blending:T.AdditiveBlending,transparent:true,opacity:0,depthWrite:false}));glow.scale.setScalar(.95);g.add(glow);
        g.position.fromArray(p); g.rotation.set(i*.45,i*.7,-.6+i*.8); product.add(g);
        tablets.push({mesh:g,home:new T.Vector3(...p),rotation:g.rotation.clone(),glow,flight:null,consumed:false,seed:i*1.7});
    });
    const shCv=document.createElement('canvas');shCv.width=shCv.height=128;const sc2=shCv.getContext('2d'),sg=sc2.createRadialGradient(64,64,0,64,64,64);
    sg.addColorStop(0,'rgba(3,22,24,.55)');sg.addColorStop(.6,'rgba(3,22,24,.18)');sg.addColorStop(1,'rgba(3,22,24,0)');sc2.fillStyle=sg;sc2.fillRect(0,0,128,128);
    const blob=new T.Mesh(new T.PlaneGeometry(3.6,3.6),new T.MeshBasicMaterial({map:new T.CanvasTexture(shCv),transparent:true,depthWrite:false}));blob.rotation.x=-Math.PI/2;blob.position.y=-2.48;scene.add(blob);
    const ground=new T.Mesh(new T.PlaneGeometry(200,200),new T.ShadowMaterial({opacity:.13}));
    ground.rotation.x=-Math.PI/2; ground.position.y=-2.5; ground.receiveShadow=true; scene.add(ground);

    let focus='',access='idle',cameraActive=false,intersecting=true,disposed=false;
    let gulp=0,capOpen=0,last=0,time=0,frame=0,pulse=0,lastBatch=-10,failedAt=-10,queuedBatch=false;
    const form=document.getElementById('password-login');
    const email=document.getElementById('Email'),password=document.getElementById('Password');
    const modal=document.getElementById('face-signin-modal');
    function setAccess(next) {
        if(next==='failed'&&access!=='failed') {
            failedAt=time;queuedBatch=false;
            if(!reduced.matches) tablets.slice(0,3).forEach((tablet,index)=>{
                tablet.flight=null;tablet.consumed=false;tablet.entered=false;
                tablet.ejection={start:time+.65+index*.38,duration:1.55};
                tablet.mesh.visible=false;tablet.mesh.position.set(0,1.25,0);
            });
        }
        access=next;
        requestFrame();
    }
    function requestFrame() { if(!frame&&!disposed&&!document.hidden&&intersecting&&!cameraActive) frame=requestAnimationFrame(render); }
    function setFocus(name) { focus=name; requestFrame(); }
    email.addEventListener('focus',()=>setFocus('email'));
    password.addEventListener('focus',()=>setFocus('password'));
    [email,password].forEach(input=>input.addEventListener('blur',()=>setFocus('')));
    email.addEventListener('input',()=>{ pulse=1; requestFrame(); });
    function releaseBatch() {
        lastBatch=time; pulse=.6;
        const available=tablets.filter(t=>!t.flight&&!t.ejection&&!t.consumed).sort(()=>Math.random()-.5).slice(0,1+Math.floor(Math.random()*2));
        available.forEach((tablet,i)=> { tablet.flight={start:time+i*.28,from:tablet.mesh.position.clone(),duration:1.8+Math.random()*.25}; });
        queuedBatch=false;
    }
    password.addEventListener('input',()=>{
        // Throttled, randomized batches are independent of content and character count.
        if(reduced.matches||access==='pending'||time-lastBatch<.6) return;
        queuedBatch=true;
        if(capOpen>.9) releaseBatch();
        requestFrame();
    });
    document.addEventListener('login-access',event=>{
        if(!['idle','pending','verified','failed'].includes(event.detail)) return;
        setAccess(event.detail);pulse=1;
    });
    form.addEventListener('submit',event=>{
        if(event.defaultPrevented||event.submitter?.id==='face-login-button'||!form.checkValidity()) return;
        if(window.jQuery&&!window.jQuery(form).valid()) return;
        access='pending'; requestFrame();
    });
    modal.addEventListener('show.bs.modal',()=>{cameraActive=true;host.dataset.paused='camera';cancelAnimationFrame(frame);frame=0;});
    modal.addEventListener('hidden.bs.modal',()=>{cameraActive=false;delete host.dataset.paused;requestFrame();});
    function resize() {
        const {width,height}=host.getBoundingClientRect(); if(!width||!height) return;
        renderer.setSize(width,height,false); camera.aspect=width/height;
        camera.position.z=mobile.matches?11.6:Math.max(9.8,6.1/camera.aspect);
        camera.updateProjectionMatrix(); requestFrame();
    }
    const resizeObserver=new ResizeObserver(resize); resizeObserver.observe(host);
    const intersection=new IntersectionObserver(entries=>{intersecting=entries[0].isIntersecting;requestFrame();});intersection.observe(host);
    const themeObserver=new MutationObserver(requestFrame);themeObserver.observe(document.documentElement,{attributes:true,attributeFilter:['data-theme']});
    document.addEventListener('visibilitychange',()=>{if(document.hidden){cancelAnimationFrame(frame);frame=0;}else requestFrame();});
    reduced.addEventListener('change',requestFrame); mobile.addEventListener('change',resize);
    window.addEventListener('pageshow',()=>{
        setAccess(document.querySelector('.validation-summary-errors,.field-validation-error')?'failed':'idle');
    });
    renderer.domElement.addEventListener('webglcontextlost',event=>{event.preventDefault();cancelAnimationFrame(frame);frame=0;host.classList.remove('scene-ready');host.dataset.render='fallback';});
    renderer.domElement.addEventListener('webglcontextrestored',()=>{host.dataset.render='webgl';requestFrame();});

    const smooth=(current,target,dt)=>T.MathUtils.lerp(current,target,1-Math.exp(-dt*5));
    function render(now) {
        frame=0;
        if(disposed||document.hidden||!intersecting||cameraActive) return;
        const dt=Math.min((now-last)/1000||.016,.05);last=now;time+=dt;
        const before=[capOpen,product.rotation.x,product.rotation.y,product.rotation.z,product.position.y,reaction.intensity];
        const still=reduced.matches, moving=access==='pending'||access==='verified';
        const failureAge=time-failedAt;
        const failureGlow=access==='failed'?(still?.22:Math.max(0,1-failureAge/3.5)):0;
        // Finish any pill descent before resealing; local-space paths stay within the neck radius.
        const flying=tablets.some(t=>t.flight||t.ejection);
        capOpen=still?0:smooth(capOpen,(focus==='password'&&!moving)||flying?1:0,dt);
        if(queuedBatch&&capOpen>.9&&!moving&&!still) releaseBatch();
        const lift=T.MathUtils.smoothstep(capOpen,.12,1);
        cap.position.set(lift*1.08,1.53+lift*1.22,-lift*1.2);
        cap.rotation.set(0,capOpen*1.1,-capOpen*.12);
        const failureSway=access==='failed'&&!still?Math.sin(failureAge*24)*Math.exp(-failureAge*2.8)*.045:0;
        product.rotation.z=smooth(product.rotation.z,still?-.13:moving?-.04:-.16,dt)+failureSway;
        product.position.x=failureSway*.8;
        product.rotation.x=smooth(product.rotation.x,still?.08:focus==='password'?.12:.05,dt);
        product.rotation.y=smooth(product.rotation.y,still?-.12:focus==='email'?.12:focus==='password'?-.2:-.13,dt);
        gulp=still?0:Math.max(0,gulp-dt*2.4);product.scale.setScalar(1+Math.sin(gulp*Math.PI)*.02);
        product.position.y=smooth(product.position.y,(moving?.08:0)+(still?0:Math.sin(time*.9)*.035),dt);
        pulse=Math.max(0,pulse-dt*.7);
        reaction.color.set(failureGlow>0?'#e94747':'#00a3a6');
        reaction.intensity=smooth(reaction.intensity,failureGlow>0?failureGlow*5:focus==='email'?1.3+pulse*1.5:focus==='password'?.6+pulse*2.2:pulse,dt);
        plastic.emissive.set('#b42b32');plastic.emissiveIntensity=failureGlow*.085;
        labelMaterial.emissive.set(failureGlow>0?'#ba3434':'#305b5c');labelMaterial.emissiveIntensity=failureGlow>0?failureGlow*.17:(focus==='email'?.13+pulse*.12:0)+gulp*.22;
        scanMaterial.opacity=!still&&moving?.24:0; scan.position.y=-1.4+(time*.7%1)*2.4;
        ringMaterial.opacity=!still&&moving?.12*(.5+.5*Math.sin(time*2)):0;
        ring.scale.setScalar(1+(time*.22%1)*.3);
        tablets.forEach(tablet=>{
            const {mesh,home,flight,seed}=tablet;
            if(tablet.ejection) {
                const elapsed=time-tablet.ejection.start;
                mesh.visible=elapsed>=0;
                if(elapsed<0) return;
                const u=T.MathUtils.clamp(elapsed/tablet.ejection.duration,0,1);
                if(u<.35) {
                    // Leave vertically through the neck before curving outside the shoulder.
                    mesh.position.set(0,1.25+u/.35*1.2,0);mesh.rotation.set(0,0,0);
                } else {
                    const t=(u-.35)/.65,s=t*t*(3-2*t);
                    const curve=new T.CubicBezierCurve3(new T.Vector3(0,2.45,0),new T.Vector3(0,3.1,.3),new T.Vector3(home.x*1.15,2.9,Math.max(.6,home.z)),home);
                    mesh.position.copy(curve.getPoint(s));
                    mesh.rotation.set(tablet.rotation.x*s,tablet.rotation.y*s,tablet.rotation.z*s);
                }
                tablet.glow.material.opacity=.22*Math.sin(u*Math.PI);
                if(u===1) { tablet.ejection=null;tablet.glow.material.opacity=0; }
                return;
            }
            if(tablet.consumed) return;
            if(flight) {
                const u=T.MathUtils.clamp((time-flight.start)/flight.duration,0,1);
                const outside=new T.Vector3(flight.from.x<0?-1.7:1.7,flight.from.y,1.1);
                const above=outside.clone(); above.y=2.65;
                if(u<.2) {
                    const t=u/.2,s=t*t*(3-2*t);
                    // First move radially clear of the bottle, including tablets below its base.
                    mesh.position.lerpVectors(flight.from,outside,s);
                } else if(u<.52) {
                    const t=(u-.2)/.32,s=t*t*(3-2*t);
                    mesh.position.lerpVectors(outside,above,s);
                } else if(u<.8) {
                    const t=(u-.52)/.28,s=t*t*(3-2*t);
                    const curve=new T.CubicBezierCurve3(above,new T.Vector3(outside.x,3.25,1.1),new T.Vector3(0,3.15,0),new T.Vector3(0,2.05,0));
                    mesh.position.copy(curve.getPoint(s));
                    mesh.rotation.set(tablet.rotation.x*(1-s),tablet.rotation.y*(1-s),tablet.rotation.z*(1-s));
                } else {
                    const t=(u-.8)/.2;mesh.position.set(0,2.05-t*2.6,0);mesh.rotation.set(0,0,0);
                }
                if(u>.8&&!tablet.entered){tablet.entered=true;gulp=1;pulse=1;}
                tablet.glow.material.opacity=Math.min(1,u*6)*.7;mesh.visible=u<.94;
                if(u===1) { tablet.flight=null;tablet.consumed=true;mesh.visible=false; }
            } else {
                mesh.position.set(home.x,home.y+(still?0:Math.sin(time*1.1+seed)*.05),home.z);
                mesh.rotation.set(tablet.rotation.x+(still?0:Math.sin(time*.8+seed)*.07),tablet.rotation.y+(still?0:Math.cos(time*.7+seed)*.07),tablet.rotation.z);
            }
        });
        renderer.render(scene,camera); host.classList.add('scene-ready'); host.dataset.render='webgl';
        const after=[capOpen,product.rotation.x,product.rotation.y,product.rotation.z,product.position.y,reaction.intensity];
        const settling=after.some((value,i)=>Math.abs(value-before[i])>.00002);
        if(!still) requestFrame();
    }
    setAccess(document.querySelector('.validation-summary-errors,.field-validation-error')?'failed':'idle');
    resize();
    window.addEventListener('pagehide',event=>{
        if(event.persisted) return;
        disposed=true;cancelAnimationFrame(frame);resizeObserver.disconnect();intersection.disconnect();themeObserver.disconnect();
        const geometries=new Set(),materials=new Set(),maps=new Set();scene.traverse(object=>{if(object.geometry)geometries.add(object.geometry);if(object.material)materials.add(object.material);});
        geometries.forEach(g=>g.dispose());materials.forEach(m=>{if(m.map) maps.add(m.map);m.dispose();});maps.forEach(map=>map.dispose());texture.dispose();tabletTexture.dispose();grain.dispose();environment.dispose();renderer.dispose();
    });
}
