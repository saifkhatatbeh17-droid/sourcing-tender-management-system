import * as T from '../lib/login-scene/three.module.js';

// Self-contained isometric procurement campus. Geometry is local, instanced where static, and illustrative.
export function createCityScene({ host, canvas, labels, stages, reduced, small, paused, onSelect, onInteract, onStateChange, onFallback }) {
    const renderer = new T.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'high-performance' });
    renderer.setPixelRatio(Math.min(devicePixelRatio, small.matches ? 1.5 : 2));
    renderer.outputColorSpace = T.SRGBColorSpace;
    renderer.toneMapping = T.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.2;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = T.PCFSoftShadowMap;
    renderer.setClearColor(0, 0);

    const scene = new T.Scene(), city = new T.Group(), moving = new T.Group();
    scene.add(city, moving);
    const camera = new T.OrthographicCamera(-40, 40, 28, -28, .1, 240);
    scene.add(new T.HemisphereLight('#f6fcff', '#7d9685', 2.15));
    const sun = new T.DirectionalLight('#fff3de', 3.15);
    sun.position.set(-22, 39, 25);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    Object.assign(sun.shadow.camera, { left: -44, right: 44, top: 40, bottom: -40, near: 1, far: 105 });
    sun.shadow.normalBias = .05;
    sun.shadow.bias = -.00015;
    sun.shadow.radius = 2.8;
    scene.add(sun);

    const palette = {
        plaster: '#f0efe2', stone: '#d3ded0', roof: '#1d5560', edge: '#0c252d', glass: '#8cb9ac',
        glassLight: '#b9ded6', road: '#647f7c', roadMark: '#e2e8d8', grass: '#9fbc8e', lawn: '#b2c99e',
        path: '#e7e8da', curb: '#f5f2e1', timber: '#a88058', leaf: '#6e9b66', leafLight: '#8ab576',
        trunk: '#76654e', copper: '#efb85c', copperDeep: '#d98a2b', water: '#6ab1b5', white: '#fcfaf2', ink: '#0a1f26'
    };
    const materials = Object.fromEntries(Object.entries(palette).map(([key, color]) =>
        [key, new T.MeshStandardMaterial({ color, roughness: .8 })]));
    materials.glass.roughness = .24;
    materials.glass.metalness = .22;
    materials.water.roughness = .16;
    materials.water.metalness = .14;
    materials.copper.roughness = .42;
    materials.copper.metalness = .18;

    const unitBox = new T.BoxGeometry(1, 1, 1), sphere = new T.IcosahedronGeometry(1, 1), cylinders = new Map();
    const pickables = [], buildings = [], anchors = [], routes = [], routeCasings = [], routeArrows = [];
    const clamp = (v, min, max) => Math.max(min, Math.min(max, v));

    let width = 1, height = 1, aspect = 1, selected = -1, hovered = -1, offerCount = 3;
    let inside = false, mode = 'journey', stopped = paused, time = 0, last = 0, frame = 0;
    let sliderPreview = 0;

    // Critically damped spring state for smooth, interruptible camera & route motion.
    let elevation = 72, velElevation = 0, targetElevation = 72;
    let angle = .36, velAngle = 0, targetAngle = .36;
    let zoom = 1, velZoom = 0, targetZoom = 1;
    let progress = 0, velProgress = 0, targetProgress = 0;
    const center = new T.Vector3(), velCenter = new T.Vector3(), targetCenter = new T.Vector3();

    function spring1D(x, v, target, omega, dt) {
        const f = 1 + 2 * dt * omega, oo = omega * omega, hoo = dt * oo, hhoo = dt * hoo;
        const inv = 1 / (f + hhoo);
        return [(f * x + dt * v + hhoo * target) * inv, (v + hoo * (target - x)) * inv];
    }

    function mesh(parent, geometry, material, x, y, z, sx = 1, sy = 1, sz = 1) {
        const item = new T.Mesh(geometry, material);
        item.position.set(x, y, z);
        item.scale.set(sx, sy, sz);
        item.castShadow = true;
        item.receiveShadow = true;
        parent.add(item);
        return item;
    }
    function box(parent, w, h, d, x, y, z, material = materials.plaster) {
        return mesh(parent, unitBox, material, x, y, z, w, h, d);
    }
    function cylinder(parent, radius, h, x, y, z, material = materials.roof, sides = 16) {
        if (!cylinders.has(sides)) cylinders.set(sides, new T.CylinderGeometry(1, 1, 1, sides));
        return mesh(parent, cylinders.get(sides), material, x, y, z, radius, h, radius);
    }
    function group(parent, x = 0, z = 0) {
        const g = new T.Group(); g.position.set(x, 0, z); parent.add(g); return g;
    }
    function tree(x, z, scale = 1, slim = false) {
        const g = group(city, x, z); g.scale.setScalar(scale);
        cylinder(g, .12, 1.6, 0, .8, 0, materials.trunk, 7);
        mesh(g, sphere, materials.leaf, 0, 2, 0, slim ? .48 : .85, slim ? 1.25 : .9, slim ? .48 : .8);
        if (!slim) mesh(g, sphere, materials.leafLight, -.28, 2.42, .1, .52, .6, .5);
    }
    function planter(x, z, w = 2.5, d = 1) {
        box(city, w, .35, d, x, .23, z, materials.curb);
        box(city, w - .18, .22, d - .16, x, .49, z, materials.leaf);
        for (let i = 0; i < 3; i++) cylinder(city, .13, .13, x - w * .28 + i * w * .28, .66, z, materials.copper, 7);
    }
    function bench(x, z, rotation = 0) {
        const g = group(city, x, z); g.rotation.y = rotation;
        for (let i = 0; i < 3; i++) box(g, 1.65, .09, .13, 0, .55, -.18 + i * .17, materials.timber);
        box(g, 1.65, .4, .09, 0, .8, -.25, materials.timber);
        for (const bx of [-.6, .6]) box(g, .09, .5, .4, bx, .26, 0, materials.edge);
    }
    function lamp(x, z) {
        cylinder(city, .07, 2.7, x, 1.35, z, materials.edge, 8);
        box(city, .58, .12, .42, x + .18, 2.7, z, materials.edge);
        box(city, .4, .04, .27, x + .18, 2.62, z, materials.white);
    }
    function texturePlane(parent, text, w, h, x, y, z, { ground = false, pale = false, dark = false, size = 48 } = {}) {
        const surface = document.createElement('canvas'); surface.width = 768; surface.height = 176;
        const ctx = surface.getContext('2d');
        ctx.fillStyle = pale ? palette.path : dark ? palette.edge : palette.roof;
        ctx.fillRect(0, 0, 768, 176);
        if (!pale) {
            ctx.strokeStyle = palette.copper;
            ctx.lineWidth = 8;
            ctx.strokeRect(4, 4, 760, 168);
        }
        ctx.font = `700 ${size}px Geist, Segoe UI, Arial`;
        ctx.fillStyle = pale ? palette.ink : palette.white;
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        ctx.fillText(text, 384, 90, 710);
        const texture = new T.CanvasTexture(surface); texture.colorSpace = T.SRGBColorSpace;
        texture.anisotropy = Math.min(4, renderer.capabilities.getMaxAnisotropy());
        const plane = new T.Mesh(new T.PlaneGeometry(w, h), new T.MeshBasicMaterial({ map: texture, toneMapped: false }));
        plane.position.set(x, y, z); if (ground) plane.rotation.x = -Math.PI / 2; parent.add(plane); return plane;
    }

    // Raised campus plinth with perimeter ring road and landscaped boulevards.
    box(city, 58, .9, 46, 0, -.65, 0, materials.stone);
    box(city, 57.7, .18, 45.7, 0, -.13, 0, materials.grass);
    box(city, 55, .12, 43, 0, .01, 0, materials.curb);
    box(city, 47, .05, 35, 0, .12, 0, materials.path);
    for (const z of [-19, 19]) {
        box(city, 52, .08, 3.2, 0, .1, z, materials.road);
        for (let x = -23; x <= 23; x += 3) box(city, 1.35, .015, .1, x, .15, z, materials.roadMark);
        box(city, 52, .09, .22, 0, .15, z + Math.sign(z) * 1.75, materials.curb);
    }
    for (const x of [-25, 25]) {
        box(city, 3.2, .08, 38, x, .1, 0, materials.road);
        for (let z = -16; z <= 16; z += 3) box(city, .1, .015, 1.35, x, .15, z, materials.roadMark);
    }
    for (const z of [-19, 19]) for (let i = 0; i < 6; i++) box(city, .24, .02, 2.8, -8.2 + i * .48, .16, z, materials.white);
    for (const x of [-27, 27]) for (let z = -19; z <= 19; z += 4.75) tree(x, z, .72, true);
    for (const z of [-21.4, 21.4]) for (let x = -22; x <= 22; x += 5.5) tree(x, z, .8);
    for (const x of [-8, 8]) {
        box(city, 2.8, .08, 26, x, .19, 0, materials.curb);
        box(city, 2.4, .05, 25.6, x, .25, 0, materials.lawn);
        for (const z of [-12, -5, 5, 12]) tree(x, z, .65, true);
    }
    for (const x of [-20.6, 20.6]) for (const z of [-14, 2, 13]) lamp(x, z);

    function station(i, w, h, d, { showFacadeSign = true } = {}) {
        const [x, z] = stages[i][4], g = group(city, x, z);
        box(g, w + 2, .16, d + 2, 0, .21, 0, materials.curb);
        box(g, w + 1.6, .06, d + 1.6, 0, .32, 0, materials.path);
        const bodyMaterial = materials.plaster.clone();
        box(g, w, h, d, 0, h / 2 + .4, 0, bodyMaterial);
        box(g, w + .3, .3, d + .3, 0, h + .5, 0, materials.roof);
        box(g, w + .04, .28, d + .04, 0, .55, 0, materials.stone);
        for (let y = 1.5; y < h - .55; y += 1.45) {
            for (let xx = -w / 2 + 1; xx < w / 2 - .5; xx += 1.25) for (const zz of [-d / 2 - .025, d / 2 + .025]) {
                box(g, .82, .83, .06, xx, y, zz, materials.edge);
                box(g, .67, .67, .075, xx, y + .035, zz + Math.sign(zz) * .025, materials.glass);
                box(g, .9, .08, .18, xx, y - .43, zz, materials.curb);
            }
            for (let zz = -d / 2 + 1; zz < d / 2 - .4; zz += 1.3)
                box(g, .07, .8, .76, w / 2 + .025, y, zz, materials.glass);
        }
        box(g, 1.55, 1.75, .08, 0, 1.28, d / 2 + .055, materials.glass);
        box(g, .08, 1.75, .12, 0, 1.28, d / 2 + .1, materials.edge);
        box(g, 2.6, .14, 1.15, 0, 2.22, d / 2 + .42, materials.roof);
        box(g, 2.3, .12, .8, 0, .38, d / 2 + .35, materials.stone);
        if (showFacadeSign) {
            // Mounted cleanly on the roof parapet frieze above any entrance canopy.
            box(g, Math.min(w - .7, 5.6), .72, .14, 0, h + .92, d / 2 - .12, materials.edge);
            texturePlane(g, `${String(i + 1).padStart(2, '0')} · ${stages[i][1].toUpperCase()}`, Math.min(w - .85, 5.45), .58, 0, h + .92, d / 2 - .04, { dark: true, size: 52 });
        }
        const hit = box(g, w + .8, h + 1.4, d + 1, 0, h / 2 + .7, 0,
            new T.MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false }));
        hit.userData.place = i; hit.castShadow = false; hit.receiveShadow = false; pickables.push(hit);

        const ring = new T.Mesh(new T.RingGeometry(Math.max(w, d) * .72, Math.max(w, d) * .72 + .2, 64),
            new T.MeshBasicMaterial({ color: palette.copper, transparent: true, opacity: 0, side: T.DoubleSide, depthWrite: false }));
        ring.rotation.x = -Math.PI / 2; ring.position.y = .4; g.add(ring);
        buildings.push({ material: bodyMaterial, ring });
        anchors.push(new T.Vector3(x, h + 1.35, z));
        planter(x - w / 2 + .5, z + d / 2 + 1.25, 1.8, .65);
        return g;
    }

    const department = station(0, 6.8, 4.6, 5.5);
    const tender = station(1, 6.4, 3.5, 5.3);
    const suppliers = station(2, 6.8, 2.7, 4.8);
    const lab = station(3, 7.6, 4.1, 5.5);
    const board = station(4, 7, 5.6, 5.6);
    const archive = station(5, 7.4, 3.2, 5.3, { showFacadeSign: false });

    // 01 Department: rooftop solar arrays.
    box(department, 4.4, .55, 3, 0, 5.3, -.6, materials.stone);
    for (let i = 0; i < 3; i++) {
        const panel = box(department, 1.15, .07, 1.5, -1.45 + i * 1.4, 5.65, -.5, materials.glass);
        panel.rotation.x = -.2;
        box(department, .04, .09, 1.5, -1.45 + i * 1.4, 5.7, -.5, materials.edge);
    }

    // 02 Publish tender: broadcast tower + mast.
    box(tender, 2.35, 3.1, 2.6, 1.35, 5.2, -.75, materials.plaster);
    box(tender, 2.65, .22, 2.9, 1.35, 6.85, -.75, materials.roof);
    for (const xx of [.7, 1.35, 2.0]) box(tender, .34, 1.85, .08, xx, 5.2, .57, materials.glass);
    cylinder(tender, .07, 2.5, -2.2, 4.9, -1.2, materials.edge, 8);
    const tenderFlag = box(tender, 1.05, .56, .05, -1.68, 5.75, -1.2, materials.copper);
    tenderFlag.castShadow = false;
    anchors[1].y = 7.3;

    // 03 Submit offers: supplier market pavilion.
    box(suppliers, 7.8, .22, 2.8, 0, 2.85, 3.05, materials.roof);
    for (const x of [-3.3, -.1, 3.3]) cylinder(suppliers, .12, 2.45, x, 1.55, 3.9, materials.stone, 8);
    for (const x of [-2.2, 0, 2.2]) {
        box(suppliers, 1.5, .9, .8, x, .85, 3.3, materials.timber);
        box(suppliers, 1.65, .1, .95, x, 1.35, 3.3, materials.curb);
    }

    // 04 Evaluate offers: glass laboratory annex + rooftop condensers.
    box(lab, 3.2, 2.65, 2.4, -2.2, 1.75, 3.4, materials.glass);
    box(lab, 3.5, .17, 2.6, -2.2, 3.16, 3.4, materials.roof);
    for (let i = 0; i < 4; i++) box(lab, .06, 2.5, .09, -3.6 + i * .9, 1.75, 4.63, materials.curb);
    for (const x of [-2, -.5, 1]) {
        cylinder(lab, .38, .65, x, 5.0, -.9, materials.stone);
        cylinder(lab, .2, .14, x, 5.4, -.9, materials.edge);
    }

    // 05 Approve selection: executive council hall + 4 live approval beacons.
    box(board, 4.7, .15, 3.1, 0, 6.0, -.2, materials.glassLight);
    box(board, 5, .12, .12, 0, 6.2, 1.35, materials.edge);
    for (const x of [-2.6, 2.6]) box(board, .28, 4.6, .32, x, 2.8, 3.05, materials.stone);
    box(board, 6.6, .23, 1.5, 0, 5.35, 3.15, materials.roof);

    // 06 Finalize award: colonnade archive + clean upper frieze sign.
    box(archive, 6, .5, 4.2, 0, 3.9, 0, materials.stone);
    box(archive, 5.5, .28, 3.6, 0, 4.3, 0, materials.roof);
    for (const x of [-2.6, -1.3, 0, 1.3, 2.6]) box(archive, .13, 2.6, .22, x, 1.85, 2.82, materials.stone);
    box(archive, 4.8, .72, .16, 0, 4.8, 1.72, materials.edge);
    texturePlane(archive, '06 · FINALIZE AWARD', 4.65, .58, 0, 4.8, 1.81, { dark: true, size: 52 });
    anchors[5].y = 5.55;

    for (const hit of pickables) {
        const stationGroup = hit.parent;
        stationGroup.updateWorldMatrix(true, true);
        const footprint = new T.Box3().setFromObject(stationGroup).expandByScalar(.12);
        hit.position.copy(footprint.getCenter(new T.Vector3()).sub(stationGroup.position));
        hit.scale.copy(footprint.getSize(new T.Vector3()));
    }

    // Central Tiryaq civic square & fountain.
    cylinder(city, 4.1, .12, 0, .26, 0, materials.curb, 64);
    cylinder(city, 3.7, .1, 0, .36, 0, materials.path, 64);
    cylinder(city, 2.05, .4, 0, .57, 0, materials.stone, 48);
    cylinder(city, 1.78, .06, 0, .8, 0, materials.water, 48);
    cylinder(city, .32, 1.35, 0, 1.4, 0, materials.roof, 12);
    const sculpture = box(moving, .92, .92, .92, 0, 2.3, 0, materials.copper);
    sculpture.rotation.set(.4, .6, .3);
    const ripples = [0, 1].map(() => {
        const ring = new T.Mesh(new T.RingGeometry(.45, .53, 40),
            new T.MeshBasicMaterial({ color: '#e9fbfb', transparent: true, opacity: .45, side: T.DoubleSide, depthWrite: false }));
        ring.rotation.x = -Math.PI / 2; ring.position.y = .85; moving.add(ring); return ring;
    });

    for (const [x, z, rotation] of [[-4.6, 1, Math.PI / 2], [4.6, -1, -Math.PI / 2], [-1.5, 4.5, 0], [1.5, -4.5, Math.PI]]) bench(x, z, rotation);
    planter(-4, -3, 2, .8); planter(4, 3, 2, .8);
    texturePlane(city, 'T I R Y A Q', 4.3, .9, -.5, .23, 6, { ground: true, pale: true, size: 68 });
    for (const [x, z] of [[-11, -14], [-11, 11], [11, -14], [11, 11]]) { bench(x, z); tree(x + 1.7, z - 1, .7); }

    // High-contrast paved corridors connecting all 6 stations cleanly without cutting buildings.
    const stops = [[-15, -5.2], [-15, 11], [0, 16.5], [-1, -4.9], [14, -4.9], [15, 11.3]];
    const waypointSets = [
        [stops[0], [-11, -5.2], [-11, 11], stops[1]],
        [stops[1], [-11, 11], [-11, 16.5], stops[2]],
        [stops[2], [-5.8, 16.5], [-5.8, -4.9], stops[3]],
        [stops[3], [5.8, -4.9], stops[4]],
        [stops[4], [10.4, -4.9], [10.4, 11.3], stops[5]]
    ];
    function roundedPath(points, y, radius = .75) {
        const vectors = points.map(([x, z]) => new T.Vector3(x, y, z)), path = new T.CurvePath();
        let from = vectors[0];
        for (let i = 1; i < vectors.length - 1; i++) {
            const corner = vectors[i], previous = vectors[i - 1], next = vectors[i + 1];
            const r = Math.min(radius, corner.distanceTo(previous) / 3, corner.distanceTo(next) / 3);
            const enter = corner.clone().add(previous.clone().sub(corner).normalize().multiplyScalar(r));
            const exit = corner.clone().add(next.clone().sub(corner).normalize().multiplyScalar(r));
            path.add(new T.LineCurve3(from, enter)); path.add(new T.QuadraticBezierCurve3(enter, corner, exit)); from = exit;
        }
        path.add(new T.LineCurve3(from, vectors.at(-1))); return path;
    }

    waypointSets.forEach(points => {
        const path = roundedPath(points, .31);
        const paving = new T.Mesh(new T.TubeGeometry(path, 90, .42, 6, false), materials.curb);
        paving.scale.y = .09; paving.position.y = .22; city.add(paving);

        // Dark recessed track underlay keeps active honey pulses high-contrast against pale stone.
        const casingMat = new T.MeshBasicMaterial({ color: palette.edge, transparent: true, opacity: .28 });
        const casing = new T.Mesh(new T.TubeGeometry(path, 90, .16, 6, false), casingMat);
        casing.scale.y = .22; casing.position.y = .235; city.add(casing);
        routeCasings.push(casingMat);

        const lineMaterial = new T.MeshBasicMaterial({ color: '#45736b', transparent: true, opacity: .82 });
        const line = new T.Mesh(new T.TubeGeometry(path, 90, .095, 6, false), lineMaterial);
        line.position.y = .03; city.add(line);
        routes.push({ path, material: lineMaterial });

        const point = path.getPointAt(.54), tangent = path.getTangentAt(.54);
        const arrow = new T.Mesh(new T.ConeGeometry(.26, .66, 4), new T.MeshBasicMaterial({ color: palette.roof }));
        arrow.position.copy(point); arrow.position.y = .37;
        arrow.quaternion.setFromUnitVectors(new T.Vector3(0, 1, 0), tangent);
        city.add(arrow); routeArrows.push(arrow);
    });

    stops.forEach(([x, z], i) => {
        cylinder(city, .76, .09, x, .31, z, materials.edge, 32);
        cylinder(city, .66, .11, x, .33, z, materials.roof, 32);
        texturePlane(city, String(i + 1).padStart(2, '0'), .86, .48, x, .4, z, { ground: true, size: 96 });
    });

    // Animated directional flow pulses streaming through corridors.
    const flowBeads = Array.from({ length: 15 }, (_, idx) => {
        const mat = new T.MeshBasicMaterial({ color: palette.copper, transparent: true, opacity: .9 });
        const bead = new T.Mesh(new T.SphereGeometry(.16, 10, 10), mat);
        bead.scale.set(.9, .45, 1.85);
        bead.userData.slot = idx;
        moving.add(bead);
        return bead;
    });

    // Stage-specific living micro-indicators (01 origin beacon, 02 tower waves, 04 4-scorer ring, 05 4-council votes, 06 archive crown).
    const originWave = new T.Mesh(new T.RingGeometry(.78, .94, 48),
        new T.MeshBasicMaterial({ color: palette.copper, transparent: true, opacity: 0, side: T.DoubleSide, depthWrite: false }));
    originWave.rotation.x = -Math.PI / 2; originWave.position.set(stops[0][0], .42, stops[0][1]); moving.add(originWave);

    const towerWaves = [0, 1].map(() => {
        const wave = new T.Mesh(new T.RingGeometry(.35, .48, 40),
            new T.MeshBasicMaterial({ color: palette.copper, transparent: true, opacity: 0, side: T.DoubleSide, depthWrite: false }));
        wave.rotation.x = -Math.PI / 2;
        wave.position.set(stages[1][4][0] - 2.2, 6.15, stages[1][4][1] - 1.2);
        moving.add(wave); return wave;
    });

    const evalNodes = Array.from({ length: 4 }, () => {
        const mat = new T.MeshStandardMaterial({ color: palette.copper, emissive: '#b5741e', emissiveIntensity: .3, roughness: .35 });
        const node = new T.Mesh(sphere, mat);
        node.scale.setScalar(.22);
        moving.add(node); return { mesh: node, mat };
    });

    const councilBeacons = [-1.5, -.5, .5, 1.5].map(dx => {
        const mat = new T.MeshStandardMaterial({ color: palette.copper, emissive: '#000000', emissiveIntensity: 0, roughness: .35 });
        const beacon = cylinder(moving, .19, .42, stages[4][4][0] + dx, 6.24, stages[4][4][1] - .15, mat, 14);
        const halo = new T.Mesh(new T.RingGeometry(.24, .36, 24),
            new T.MeshBasicMaterial({ color: palette.copper, transparent: true, opacity: 0, side: T.DoubleSide, depthWrite: false }));
        halo.rotation.x = -Math.PI / 2;
        halo.position.set(stages[4][4][0] + dx, 6.48, stages[4][4][1] - .15);
        moving.add(halo);
        return { beacon, mat, halo };
    });

    const archiveCrown = box(moving, .56, .56, .56, stages[5][4][0], 5.15, stages[5][4][1],
        new T.MeshStandardMaterial({ color: palette.copper, emissive: '#a96b18', emissiveIntensity: .25, roughness: .32 }));
    archiveCrown.rotation.set(.4, .5, .3);

    // Elevated procurement dossier courier (never clips the ground stop numbers).
    const packet = group(moving);
    const packetCard = group(packet);
    box(packetCard, .86, 1.06, .15, 0, 1.28, 0, materials.copper);
    box(packetCard, .72, .92, .17, 0, 1.28, 0, materials.white);
    box(packetCard, .52, .14, .19, 0, 1.54, 0, materials.roof);
    for (const y of [1.1, 1.28]) box(packetCard, .48, .055, .19, 0, y, 0, materials.copperDeep);
    const packetHalo = new T.Mesh(new T.RingGeometry(.82, .98, 40),
        new T.MeshBasicMaterial({ color: palette.copper, side: T.DoubleSide, transparent: true, opacity: .7, depthWrite: false }));
    packetHalo.rotation.x = -Math.PI / 2; packetHalo.position.y = .43; packet.add(packetHalo);

    const vans = [], offers = [], kiosks = [], kioskLanterns = [], people = [];
    for (let i = 0; i < 6; i++) {
        const van = group(moving);
        box(van, 1.65, .8, .9, -.15, .7, 0, i % 2 ? materials.white : materials.roof);
        box(van, .65, .65, .9, .95, .62, 0, materials.plaster);
        box(van, .035, .3, .7, 1.29, .77, 0, materials.glass);
        for (const x of [-.65, .92]) for (const z of [-.46, .46]) {
            const wheel = cylinder(van, .21, .12, x, .25, z, materials.edge, 10); wheel.rotation.x = Math.PI / 2;
        }
        vans.push(van);

        const kiosk = group(moving, -4.7 + (i % 3) * 4.7, i < 3 ? 7.6 : 15.5);
        box(kiosk, 1.9, .75, 1.4, 0, .6, 0, materials.timber);
        box(kiosk, 2.25, .14, 1.8, 0, 1.66, 0, materials.roof);
        for (const x of [-.85, .85]) cylinder(kiosk, .055, 1.4, x, .9, .55, materials.stone, 6);
        const lanternMat = new T.MeshStandardMaterial({ color: palette.copper, emissive: '#b5741e', emissiveIntensity: .22, roughness: .4 });
        const lantern = box(kiosk, .44, .12, .44, 0, 1.8, 0, lanternMat);
        kiosks.push(kiosk); kioskLanterns.push({ lantern, mat: lanternMat });

        const offer = group(moving);
        box(offer, .52, .64, .12, 0, .32, 0, materials.copper);
        box(offer, .38, .48, .14, 0, .32, 0, materials.white);
        offers.push(offer);
    }
    for (let i = 0; i < 16; i++) {
        const person = group(moving);
        cylinder(person, .12, .42, 0, .48, 0, i % 4 ? materials.roof : materials.copper, 7);
        mesh(person, sphere, materials.timber, 0, .82, 0, .14, .14, .14);
        for (const x of [-.065, .065]) box(person, .075, .27, .1, x, .15, 0, materials.edge);
        people.push(person);
    }
    const roadPath = roundedPath([[-25, 16], [-25, -19], [25, -19], [25, 19], [-25, 19], [-25, 16]], .2, 2.2);

    // Instance repeated static meshes so draw calls stay low and frame pacing stays locked.
    city.updateMatrixWorld(true);
    const batches = new Map(), originals = [];
    city.traverse(object => {
        if (!object.isMesh || object.userData.place !== undefined || object.material.transparent || object.material.map) return;
        const key = `${object.geometry.uuid}/${object.material.uuid}`;
        if (!batches.has(key)) batches.set(key, []);
        batches.get(key).push(object);
    });
    for (const items of batches.values()) {
        if (items.length < 3) continue;
        const batch = new T.InstancedMesh(items[0].geometry, items[0].material, items.length);
        items.forEach((item, i) => { batch.setMatrixAt(i, item.matrixWorld); originals.push(item); });
        batch.castShadow = true; batch.receiveShadow = true; batch.computeBoundingSphere(); city.add(batch);
    }
    originals.forEach(item => item.removeFromParent());
    renderer.shadowMap.autoUpdate = false;
    renderer.shadowMap.needsUpdate = true;
    moving.traverse(object => { object.castShadow = false; });

    // GPU-positioned 2D floating callout pills + curved SVG leaders + roof anchor dots.
    const svgNS = 'http://www.w3.org/2000/svg', leaders = document.createElementNS(svgNS, 'svg');
    leaders.classList.add('campus-map-leaders'); leaders.setAttribute('aria-hidden', 'true');
    const leaderItems = labels.map((button, idx) => {
        const labelText = button.querySelector('.campus-place-name')?.textContent?.trim() || stages[idx][1];
        button.setAttribute('aria-label', `Step ${idx + 1}: ${labelText}`);
        const path = document.createElementNS(svgNS, 'path');
        const dot = document.createElementNS(svgNS, 'circle');
        dot.setAttribute('r', '3.2');
        leaders.append(path, dot);
        return { path, dot, lastKey: '' };
    });
    host.querySelector('.campus-labels').prepend(leaders);

    // Exterior callout offsets per station keep roofs unobstructed in isometric view.
    const preferredOffsets = [
        [[-52, -26], [0, -34], [-72, -6], [48, -28]],
        [[-82, -10], [-58, -32], [-72, 26], [0, -34]],
        [[0, 74], [-66, 66], [-56, -24], [66, 66]],
        [[0, -36], [52, -30], [-52, -30], [0, -56]],
        [[62, -26], [0, -34], [76, 8], [-48, -30]],
        [[68, 68], [76, -18], [0, 74], [-58, 56]]
    ];
    const fallbackOffsets = [[0, -38], [-64, -22], [64, -22], [-64, 34], [64, 34], [0, 48]];

    let labelSizes = [];
    function measureLabels() {
        labelSizes = labels.map(button => {
            const pill = button.firstElementChild || button;
            const r = pill.getBoundingClientRect();
            return { w: r.width || 118, h: r.height || 38 };
        });
    }
    function resize() {
        width = Math.max(1, host.clientWidth); height = Math.max(1, host.clientHeight); aspect = width / height;
        renderer.setSize(width, height, false);
        leaders.setAttribute('viewBox', `0 0 ${width} ${height}`);
        measureLabels();
    }
    const observer = new ResizeObserver(resize); observer.observe(host); resize();

    const projection = new T.Vector3();
    function positionLabels() {
        const used = [], order = labels.map((_, i) => i).sort((a, b) => Number(b === selected) - Number(a === selected));
        order.forEach(i => {
            const point = projection.copy(anchors[i]).project(camera), button = labels[i], size = labelSizes[i], item = leaderItems[i];
            const anchorX = (point.x * .5 + .5) * width, anchorY = (-point.y * .5 + .5) * height;
            if (point.z < -1 || point.z > 1 || anchorX < -24 || anchorX > width + 24 || anchorY < -24 || anchorY > height + 24) {
                if (!button.hidden) {
                    button.hidden = true;
                    item.path.setAttribute('d', '');
                    item.dot.setAttribute('opacity', '0');
                    item.lastKey = 'hidden';
                }
                return;
            }
            const candidates = mobileCompact() ? [[0, -14], [-34, -12], [34, -12], [0, 36]] : [...preferredOffsets[i], ...fallbackOffsets];
            let best = null;
            for (let c = 0; c < candidates.length; c++) {
                const [dx, dy] = candidates[c];
                const x = clamp(anchorX + dx, size.w / 2 + 10, width - size.w / 2 - 10);
                const y = clamp(anchorY + dy, size.h + 10, height - 62);
                const boxLeft = x - size.w / 2, boxRight = x + size.w / 2, boxTop = y - size.h, boxBottom = y;
                const overlap = used.reduce((total, r) =>
                    total + Math.max(0, Math.min(boxRight + 6, r.right) - Math.max(boxLeft - 6, r.left))
                    * Math.max(0, Math.min(boxBottom + 6, r.bottom) - Math.max(boxTop - 6, r.top)), 0);
                const roofCover = Math.max(0, Math.min(boxRight, anchorX + 26) - Math.max(boxLeft, anchorX - 26))
                    * Math.max(0, Math.min(boxBottom, anchorY + 34) - Math.max(boxTop, anchorY - 4));
                const cost = overlap * 120 + (mobileCompact() ? 0 : roofCover * 2.4) + c * 6 + Math.hypot(x - (anchorX + dx), y - (anchorY + dy)) * .6;
                if (!best || cost < best.cost) best = { x, y, cost };
            }
            const { x, y } = best;
            button.hidden = false;
            used.push({ left: x - size.w / 2, right: x + size.w / 2, top: y - size.h, bottom: y });

            const attachX = clamp(anchorX, x - size.w / 2 + 10, x + size.w / 2 - 10);
            const attachY = anchorY < y - size.h / 2 ? y - size.h : y;
            const ctrlX = attachX + (anchorX - attachX) * .25;
            const ctrlY = anchorY;
            const activeState = i === selected ? 'sel' : i === hovered ? 'hov' : 'idle';
            const key = `${x.toFixed(1)},${y.toFixed(1)},${anchorX.toFixed(1)},${anchorY.toFixed(1)},${activeState}`;
            if (item.lastKey !== key) {
                item.lastKey = key;
                button.style.transform = `translate3d(${x.toFixed(1)}px, ${y.toFixed(1)}px, 0) translate(-50%, -100%)`;
                item.path.setAttribute('d', `M${attachX.toFixed(1)},${attachY.toFixed(1)} Q${ctrlX.toFixed(1)},${ctrlY.toFixed(1)} ${anchorX.toFixed(1)},${(anchorY + 4).toFixed(1)}`);
                item.dot.setAttribute('cx', anchorX.toFixed(1));
                item.dot.setAttribute('cy', (anchorY + 4).toFixed(1));
                item.dot.setAttribute('opacity', '1');
                item.path.classList.toggle('is-selected', i === selected);
                item.dot.classList.toggle('is-selected', i === selected);
            }
        });
    }
    function mobileCompact() { return small.matches; }

    function sampleRouteChain(u) {
        const scaled = clamp(u, 0, 1) * routes.length;
        const idx = Math.min(routes.length - 1, Math.floor(scaled));
        const local = clamp(scaled - idx, 0, 1);
        return {
            point: routes[idx].path.getPointAt(local),
            tangent: routes[idx].path.getTangentAt(local)
        };
    }

    function updateCamera() {
        camera.position.set(center.x + Math.sin(angle) * 70, elevation, center.z + Math.cos(angle) * 70);
        camera.lookAt(center.x, 0, center.z); camera.updateMatrixWorld();
        const half = (inside ? Math.max(11.5, 13.5 / aspect) : Math.max(28.5, 41 / aspect)) / zoom;
        camera.left = -half * aspect; camera.right = half * aspect; camera.top = half; camera.bottom = -half;
        camera.updateProjectionMatrix();
    }

    const offerStart = new T.Vector3(), offerEnd = new T.Vector3();
    function animate(now) {
        frame = requestAnimationFrame(animate);
        if (document.hidden) { last = now; return; }
        const dt = Math.min(.05, (now - (last || now)) / 1000); last = now;
        const motionOn = !stopped && !reduced.matches;
        if (motionOn) time += dt;
        if (sliderPreview > 0) sliderPreview = Math.max(0, sliderPreview - dt);

        if (reduced.matches) {
            elevation = targetElevation; angle = targetAngle; zoom = targetZoom;
            center.copy(targetCenter); progress = targetProgress;
            velElevation = velAngle = velZoom = velProgress = 0; velCenter.set(0, 0, 0);
        } else {
            const camOmega = down ? 22 : 9.5;
            [elevation, velElevation] = spring1D(elevation, velElevation, targetElevation, camOmega, dt);
            [angle, velAngle] = spring1D(angle, velAngle, targetAngle, camOmega, dt);
            [zoom, velZoom] = spring1D(zoom, velZoom, targetZoom, 11, dt);
            [progress, velProgress] = spring1D(progress, velProgress, targetProgress, 6.2, dt);
            let vx, vy, vz;
            [center.x, vx] = spring1D(center.x, velCenter.x, targetCenter.x, camOmega, dt);
            [center.y, vy] = spring1D(center.y, velCenter.y, targetCenter.y, camOmega, dt);
            [center.z, vz] = spring1D(center.z, velCenter.z, targetCenter.z, camOmega, dt);
            velCenter.set(vx, vy, vz);
        }
        updateCamera();

        // Building selection & hover glow.
        buildings.forEach((building, i) => {
            const active = i === selected, over = i === hovered;
            const sliderMarketGlow = i === 2 && sliderPreview > 0;
            building.material.emissive.set(active ? '#588c79' : over || sliderMarketGlow ? '#739588' : '#000000');
            building.material.emissiveIntensity = active ? .18 : over || sliderMarketGlow ? .1 : 0;
            building.ring.material.opacity = active ? .9 : over ? .42 : sliderMarketGlow ? .55 : 0;
            building.ring.scale.setScalar(active && motionOn ? 1 + Math.sin(time * 2.4) * .022 : 1);
        });

        // Route state styling: completed = deep teal, active incoming/outgoing = bright honey over dark casing.
        const activeLeg = selected > 0 ? selected - 1 : selected === 0 ? 0 : -1;
        routes.forEach((route, i) => {
            const done = selected > 0 && i < selected - 1;
            const currentLeg = i === activeLeg;
            route.material.color.set(currentLeg ? palette.copper : done ? palette.roof : '#4c7970');
            route.material.opacity = currentLeg ? 1 : done ? .95 : .68;
            routeCasings[i].opacity = currentLeg ? .72 : done ? .42 : .24;
            routeArrows[i].material.color.set(currentLeg ? palette.copper : done ? palette.roof : '#3d6962');
            routeArrows[i].scale.setScalar(currentLeg && motionOn ? 1 + Math.sin(time * 4) * .12 : 1);
        });

        // Animated flow beads along corridors (all corridors gently in overview; concentrated on active leg when selected).
        flowBeads.forEach((bead, idx) => {
            if (!motionOn) { bead.visible = false; return; }
            bead.visible = true;
            if (selected < 0) {
                const seg = idx % routes.length;
                const slotInSeg = Math.floor(idx / routes.length) / 3;
                const t = (time * .16 + slotInSeg + seg * .13) % 1;
                const pt = routes[seg].path.getPointAt(t), tan = routes[seg].path.getTangentAt(t);
                bead.position.copy(pt); bead.position.y = .37;
                bead.rotation.y = -Math.atan2(tan.z, tan.x);
                bead.material.opacity = Math.sin(t * Math.PI) * .65;
            } else {
                const seg = activeLeg;
                if (idx >= 5) { bead.visible = false; return; }
                const t = (time * .36 + idx / 5) % 1;
                const pt = routes[seg].path.getPointAt(t), tan = routes[seg].path.getTangentAt(t);
                bead.position.copy(pt); bead.position.y = .39;
                bead.rotation.y = -Math.atan2(tan.z, tan.x);
                bead.material.opacity = .25 + Math.sin(t * Math.PI) * .75;
            }
        });

        // Elevated dossier courier smoothly glides along the connected path and hovers offset from ground numbers.
        if (selected < 0) {
            packet.position.set(stops[0][0] + 1.35, 0, stops[0][1]);
            packetCard.rotation.y = motionOn ? Math.sin(time * 1.2) * .22 + .35 : .35;
        } else {
            const sample = sampleRouteChain(progress);
            const sideOffset = Math.abs(targetProgress - progress) < .015 ? 1.15 : 0;
            packet.position.set(sample.point.x + sideOffset * .75, 0, sample.point.z + sideOffset * .45);
            packetCard.rotation.y = -Math.atan2(sample.tangent.z, sample.tangent.x) + Math.PI / 2;
        }
        packetCard.position.y = motionOn ? Math.sin(time * 2.6) * .12 : 0;
        packetHalo.scale.setScalar(motionOn ? 1 + Math.sin(time * 2.6) * .08 : 1);
        packetHalo.material.opacity = selected < 0 ? .45 : .78;

        // Station 01 origin pulse.
        if ((selected <= 0) && motionOn) {
            const p = (time * .45) % 1;
            originWave.scale.setScalar(1 + p * 1.45);
            originWave.material.opacity = (1 - p) * .55;
        } else {
            originWave.material.opacity = 0;
        }

        // Station 02 tender broadcast waves.
        towerWaves.forEach((wave, idx) => {
            if (selected === 1 && motionOn) {
                const p = (time * .55 + idx * .5) % 1;
                wave.scale.setScalar(1 + p * 6.5);
                wave.material.opacity = (1 - p) * .65;
            } else {
                wave.material.opacity = 0;
            }
        });

        // Station 03 supplier vans, market stalls, and parabolic sealed-offer arcs (also triggered live by slider).
        const showOffers = selected === 2 || selected === 3 || sliderPreview > 0;
        for (let i = 0; i < 6; i++) {
            const activeSupplier = i < offerCount;
            vans[i].visible = kiosks[i].visible = activeSupplier;
            if (activeSupplier) {
                const t = (time * .014 + i / 6) % 1, location = roadPath.getPointAt(t), tangent = roadPath.getTangentAt(t);
                vans[i].position.copy(location); vans[i].rotation.y = -Math.atan2(tangent.z, tangent.x);
                kioskLanterns[i].mat.emissiveIntensity = (selected === 2 || sliderPreview > 0) ? .65 : .18;
            }
            offers[i].visible = activeSupplier && showOffers;
            if (offers[i].visible) {
                const phase = motionOn ? (time * .38 + i / offerCount) % 1 : (i + 1) / (offerCount + 1);
                if (selected === 3) {
                    offerStart.set(stops[2][0], .9, stops[2][1] - 2);
                    offerEnd.set(stages[3][4][0] - 1.2, 1.4, stages[3][4][1] + 2.6);
                } else {
                    offerStart.set(kiosks[i].position.x, 1.2, kiosks[i].position.z);
                    offerEnd.set(stages[2][4][0], 1.5, stages[2][4][1] + 1.4);
                }
                offers[i].position.lerpVectors(offerStart, offerEnd, phase);
                offers[i].position.y += Math.sin(phase * Math.PI) * 2.9;
                offers[i].rotation.y = phase * Math.PI;
            }
        }

        // Station 04 4-member evaluation committee orbit nodes.
        evalNodes.forEach((node, idx) => {
            const activeEval = selected === 3;
            const theta = (motionOn ? time * .9 : 0) + idx * (Math.PI / 2);
            const radius = activeEval ? 1.95 : 1.45;
            node.mesh.position.set(
                stages[3][4][0] + Math.cos(theta) * radius,
                activeEval ? 5.85 + (motionOn ? Math.sin(time * 2.5 + idx) * .14 : 0) : 5.25,
                stages[3][4][1] + Math.sin(theta) * radius * .75
            );
            node.mesh.scale.setScalar(activeEval ? .27 : .16);
            node.mat.emissiveIntensity = activeEval ? .75 : .15;
        });

        // Station 05 4 sequential CEO & Chiefs approval beacons.
        councilBeacons.forEach((b, idx) => {
            if (selected === 4) {
                const cycle = motionOn ? (time * .85) % 5 : 4.5;
                const lit = cycle >= idx + .35;
                b.mat.emissive.set(lit ? '#e59a28' : '#1b3a3f');
                b.mat.emissiveIntensity = lit ? .9 : .12;
                b.halo.material.opacity = lit ? .75 : .12;
                b.halo.scale.setScalar(lit && motionOn ? 1 + Math.sin(time * 3 + idx) * .12 : 1);
            } else if (selected === 5) {
                b.mat.emissive.set('#e59a28');
                b.mat.emissiveIntensity = .55;
                b.halo.material.opacity = .4;
                b.halo.scale.setScalar(1);
            } else {
                b.mat.emissive.set('#000000');
                b.mat.emissiveIntensity = 0;
                b.halo.material.opacity = 0;
            }
        });

        // Station 06 award crown & central Tiryaq sculpture.
        if (motionOn) {
            sculpture.rotation.y = .6 + time * .32;
            archiveCrown.rotation.y = .5 + time * (selected === 5 ? .9 : .25);
        }
        archiveCrown.scale.setScalar(selected === 5 ? .72 : .52);
        archiveCrown.material.emissiveIntensity = selected === 5 ? .85 : .2;

        // Fountain water ripples.
        ripples.forEach((ring, idx) => {
            if (!motionOn) { ring.material.opacity = .22; ring.scale.setScalar(1.6 + idx * .8); return; }
            const p = (time * .32 + idx * .5) % 1;
            ring.scale.setScalar(1 + p * 2.4);
            ring.material.opacity = (1 - p) * .42;
        });

        // Campus pedestrians walking between stations.
        people.forEach((person, i) => {
            const route = routes[i % routes.length].path, phase = (time * .022 + i * .17) % 1;
            person.position.copy(route.getPointAt(phase)); person.position.y = .33;
            const tangent = route.getTangentAt(phase); person.rotation.y = -Math.atan2(tangent.z, tangent.x) + Math.PI / 2;
        });

        positionLabels();
        renderer.render(scene, camera);
    }

    const ray = new T.Raycaster(), pointer = new T.Vector2(); let down = null;
    function pick(event) {
        const r = canvas.getBoundingClientRect();
        pointer.set((event.clientX - r.left) / r.width * 2 - 1, -(event.clientY - r.top) / r.height * 2 + 1);
        ray.setFromCamera(pointer, camera);
        return ray.intersectObjects(pickables)[0]?.object.userData.place;
    }
    canvas.addEventListener('pointerdown', e => {
        if (down || (e.button !== 0 && e.button !== 2)) return;
        const savedCenter = targetCenter.clone(), savedZoom = targetZoom;
        onInteract?.();
        targetCenter.copy(savedCenter); targetZoom = savedZoom;
        down = { id: e.pointerId, x: e.clientX, y: e.clientY, last: e.clientX, lastY: e.clientY, moved: false, pan: e.shiftKey || e.button === 2 };
        canvas.setPointerCapture(e.pointerId);
    });
    canvas.addEventListener('contextmenu', e => e.preventDefault());
    canvas.addEventListener('pointermove', e => {
        if (down && down.id === e.pointerId) {
            if (!down.moved) {
                if (Math.hypot(e.clientX - down.x, e.clientY - down.y) <= 5) return;
                down.moved = true;
                down.last = down.x; down.lastY = down.y;
            }
            if (down.moved) {
                const dx = e.clientX - down.last, dy = e.clientY - down.lastY;
                if (down.pan) {
                    const units = (camera.right - camera.left) / width;
                    const tilt = Math.max(.3, elevation / Math.hypot(70, elevation));
                    targetCenter.x = clamp(targetCenter.x - (Math.cos(angle) * dx - Math.sin(angle) * dy / tilt) * units, -35, 35);
                    targetCenter.z = clamp(targetCenter.z + (Math.sin(angle) * dx + Math.cos(angle) * dy / tilt) * units, -30, 30);
                } else {
                    targetAngle -= dx * .0038;
                    targetElevation = clamp(targetElevation + dy * .14, 34, 98);
                }
            }
            down.last = e.clientX; down.lastY = e.clientY;
        } else if (e.pointerType === 'mouse') {
            hovered = pick(e) ?? -1;
            canvas.style.cursor = hovered >= 0 ? 'pointer' : 'grab';
        }
    });
    canvas.addEventListener('pointerleave', () => { hovered = -1; });
    canvas.addEventListener('pointerup', e => {
        if (!down || down.id !== e.pointerId) return;
        if (!down.moved && !down.pan) { const i = pick(e); if (i !== undefined) onSelect(i); }
        down = null; if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    });
    canvas.addEventListener('pointercancel', () => { down = null; });
    canvas.addEventListener('lostpointercapture', () => { down = null; });
    canvas.addEventListener('wheel', e => {
        e.preventDefault();
        const savedCenter = targetCenter.clone(), savedZoom = targetZoom;
        onInteract?.();
        targetCenter.copy(savedCenter);
        const delta = e.deltaY * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? height : 1);
        targetZoom = clamp(savedZoom * Math.exp(-clamp(delta, -120, 120) * .0016), .78, 2.35);
    }, { passive: false });
    canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); cancelAnimationFrame(frame); onFallback(new Error('Graphics view interrupted')); });
    canvas.addEventListener('webglcontextrestored', () => window.location.reload());
    window.addEventListener('pagehide', () => cancelAnimationFrame(frame));
    window.addEventListener('pageshow', e => { if (e.persisted) { last = 0; frame = requestAnimationFrame(animate); } });

    frame = requestAnimationFrame(animate);

    function notifyState() { onStateChange?.({ inside, zoom: targetZoom, mode }); }

    return {
        select(i) {
            selected = i;
            const factor = inside ? .92 : .34;
            targetCenter.set(i < 0 ? 0 : stages[i][4][0] * factor, 0, i < 0 ? 0 : stages[i][4][1] * factor);
            targetZoom = inside ? 1.08 : i < 0 ? 1 : small.matches ? 1.38 : 1.15;
            measureLabels();
            notifyState();
        },
        enter(force) {
            inside = typeof force === 'boolean' ? force : !inside;
            targetElevation = inside ? 42 : 72;
            targetZoom = inside ? 1.08 : selected < 0 ? 1 : 1.15;
            if (!inside && selected < 0) targetCenter.set(0, 0, 0);
            else if (selected >= 0) {
                const factor = inside ? .92 : .34;
                targetCenter.set(stages[selected][4][0] * factor, 0, stages[selected][4][1] * factor);
            }
            notifyState();
            return inside;
        },
        isInside() { return inside; },
        follow(value) {
            if (!inside) return;
            const sample = sampleRouteChain(value);
            targetCenter.copy(sample.point);
        },
        progress(value) { targetProgress = clamp(value, 0, 1); },
        mode(value) { mode = value; notifyState(); },
        pause(value) { stopped = value; },
        suppliers(value, preview = true) { offerCount = value; if (preview) sliderPreview = 2.6; },
        zoom(direction) { targetZoom = clamp(targetZoom + direction * .22, .78, 2.35); notifyState(); },
        rotate(direction) { targetAngle += direction * Math.PI / 8; },
        reset() {
            inside = false; targetElevation = 72; targetAngle = .36; targetZoom = 1;
            targetCenter.set(0, 0, 0); targetProgress = 0;
            notifyState();
        }
    };
}
