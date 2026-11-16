import * as T from '../lib/login-scene/three.module.js';

// A small, self-contained city. All geometry is local and all activity is illustrative.
export function createCityScene({ host, canvas, labels, stages, reduced, small, paused, onSelect, onInteract, onFallback }) {
    const renderer = new T.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'low-power' });
    renderer.setPixelRatio(Math.min(devicePixelRatio, small.matches ? 1.5 : 1.75));
    renderer.outputColorSpace = T.SRGBColorSpace;
    renderer.toneMapping = T.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.22;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = T.PCFSoftShadowMap;
    renderer.setClearColor(0, 0);

    const scene = new T.Scene(), city = new T.Group(), moving = new T.Group();
    scene.add(city, moving);
    const camera = new T.OrthographicCamera(-40, 40, 28, -28, .1, 220);
    scene.add(new T.HemisphereLight('#f4fcff', '#7f9783', 2.1));
    const sun = new T.DirectionalLight('#fff2dc', 3.2);
    sun.position.set(-22, 38, 25);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    Object.assign(sun.shadow.camera, { left: -43, right: 43, top: 40, bottom: -40, near: 1, far: 100 });
    sun.shadow.normalBias = .055;
    sun.shadow.bias = -.00015;
    sun.shadow.radius = 3;
    scene.add(sun);

    const palette = {
        plaster: '#eeeede', stone: '#d2ddcd', roof: '#1d5560', edge: '#0c252d', glass: '#8db8a8',
        glassLight: '#b2d7cf', road: '#65807d', roadMark: '#e0e6d4', grass: '#a0bd8c', lawn: '#b1c89c',
        path: '#e6e7d7', curb: '#f5f1dd', timber: '#a77f57', leaf: '#709c66', leafLight: '#8bb675',
        trunk: '#77664e', copper: '#efb85c', water: '#6bafb2', white: '#fcfaf0', ink: '#0a1f26'
    };
    const materials = Object.fromEntries(Object.entries(palette).map(([key, color]) =>
        [key, new T.MeshStandardMaterial({ color, roughness: .82 })]));
    materials.glass.roughness = .25;
    materials.glass.metalness = .2;
    materials.water.roughness = .18;
    materials.water.metalness = .12;
    const unitBox = new T.BoxGeometry(1, 1, 1), sphere = new T.IcosahedronGeometry(1, 1), cylinders = new Map();
    const pickables = [], buildings = [], anchors = [], routes = [], routeArrows = [];
    const clamp = (v, min, max) => Math.max(min, Math.min(max, v));
    let width = 1, height = 1, aspect = 1, selected = -1, hovered = -1, offerCount = 3;
    let inside = false;
    let mode = 'journey', stopped = paused, time = 0, last = 0, frame = 0;
    let elevation = 72, targetElevation = 72;
    let angle = .36, targetAngle = .36, zoom = 1, targetZoom = 1, progress = 0, targetProgress = 0;
    const center = new T.Vector3(), targetCenter = new T.Vector3();

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
        for (const x of [-.6, .6]) box(g, .09, .5, .4, x, .26, 0, materials.edge);
    }
    function lamp(x, z) {
        cylinder(city, .07, 2.7, x, 1.35, z, materials.edge, 8);
        box(city, .58, .12, .42, x + .18, 2.7, z, materials.edge);
        box(city, .4, .04, .27, x + .18, 2.62, z, materials.white);
    }
    function texturePlane(parent, text, w, h, x, y, z, { ground = false, pale = false, size = 48 } = {}) {
        const surface = document.createElement('canvas'); surface.width = 768; surface.height = 160;
        const ctx = surface.getContext('2d');
        ctx.fillStyle = pale ? palette.path : palette.roof; ctx.fillRect(0, 0, 768, 160);
        ctx.font = `600 ${size}px Segoe UI, Arial`; ctx.fillStyle = pale ? palette.ink : palette.white;
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText(text, 384, 83, 720);
        const texture = new T.CanvasTexture(surface); texture.colorSpace = T.SRGBColorSpace;
        texture.anisotropy = Math.min(4, renderer.capabilities.getMaxAnisotropy());
        const plane = new T.Mesh(new T.PlaneGeometry(w, h), new T.MeshBasicMaterial({ map: texture, toneMapped: false }));
        plane.position.set(x, y, z); if (ground) plane.rotation.x = -Math.PI / 2; parent.add(plane); return plane;
    }

    // A raised, bordered city block with a continuous ring road and a separate pedestrian spine.
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
    // Garden strips give the city breathing space and keep the route separate from the roads.
    for (const x of [-27, 27]) for (let z = -19; z <= 19; z += 4.75) tree(x, z, .72, true);
    for (const z of [-21.4, 21.4]) for (let x = -22; x <= 22; x += 5.5) tree(x, z, .8);
    for (const x of [-8, 8]) {
        box(city, 2.8, .08, 26, x, .19, 0, materials.curb);
        box(city, 2.4, .05, 25.6, x, .25, 0, materials.lawn);
        for (const z of [-12, -5, 5, 12]) tree(x, z, .65, true);
    }
    for (const x of [-20.6, 20.6]) for (const z of [-14, 2, 13]) lamp(x, z);

    function station(i, w, h, d) {
        const [x, z] = stages[i][4], g = group(city, x, z);
        box(g, w + 2, .16, d + 2, 0, .21, 0, materials.curb);
        box(g, w + 1.6, .06, d + 1.6, 0, .32, 0, materials.path);
        const bodyMaterial = materials.plaster.clone();
        box(g, w, h, d, 0, h / 2 + .4, 0, bodyMaterial);
        box(g, w + .26, .28, d + .26, 0, h + .48, 0, materials.roof);
        box(g, w + .04, .28, d + .04, 0, .55, 0, materials.stone);
        // Window mullions and recesses keep the facades legible at a distance.
        for (let y = 1.5; y < h; y += 1.45) {
            for (let xx = -w / 2 + 1; xx < w / 2 - .5; xx += 1.25) for (const zz of [-d / 2 - .025, d / 2 + .025]) {
                box(g, .82, .83, .06, xx, y, zz, materials.edge);
                box(g, .67, .67, .075, xx, y + .035, zz + Math.sign(zz) * .025, materials.glass);
                box(g, .9, .08, .18, xx, y - .43, zz, materials.curb);
            }
            for (let zz = -d / 2 + 1; zz < d / 2 - .4; zz += 1.3)
                box(g, .07, .8, .76, w / 2 + .025, y, zz, materials.glass);
        }
        box(g, 1.55, 1.9, .08, 0, 1.35, d / 2 + .055, materials.glass);
        box(g, .08, 1.9, .12, 0, 1.35, d / 2 + .1, materials.edge);
        box(g, 2.7, .15, 1.3, 0, 2.5, d / 2 + .45, materials.roof);
        box(g, 2.3, .12, .8, 0, .38, d / 2 + .35, materials.stone);
        texturePlane(g, stages[i][1].toUpperCase(), Math.min(w - .5, 5.8), .58, 0, h - .05, d / 2 + .09);
        // The proxy remains separate from static geometry, so roofs and wings are easy to select.
        const hit = box(g, w + .8, h + 1, d + 1, 0, h / 2 + .6, 0,
            new T.MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false }));
        hit.userData.place = i; hit.castShadow = false; hit.receiveShadow = false; pickables.push(hit);
        const ring = new T.Mesh(new T.RingGeometry(Math.max(w, d) * .71, Math.max(w, d) * .71 + .12, 64),
            new T.MeshBasicMaterial({ color: palette.copper, transparent: true, opacity: 0, side: T.DoubleSide, depthWrite: false }));
        ring.rotation.x = -Math.PI / 2; ring.position.y = .39; g.add(ring);
        buildings.push({ material: bodyMaterial, ring }); anchors.push(new T.Vector3(x, h + 1.6, z));
        planter(x - w / 2 + .5, z + d / 2 + 1.25, 1.8, .65);
        return g;
    }
    const department = station(0, 6.8, 4.6, 5.5);
    const tender = station(1, 6.4, 3.3, 5.3);
    const suppliers = station(2, 6.8, 2.4, 4.8);
    const lab = station(3, 7.6, 4, 5.5);
    const board = station(4, 7, 5.6, 5.6);
    const archive = station(5, 7.4, 3, 5.3);

    // Six silhouettes: administration, publishing tower, market hall, glass lab, council, archive.
    box(department, 4.4, .55, 3, 0, 5.3, -.5, materials.stone);
    for (let i = 0; i < 3; i++) {
        const panel = box(department, 1.15, .07, 1.5, -1.45 + i * 1.4, 5.65, -.4, materials.glass);
        panel.rotation.x = -.2;
        box(department, .04, .09, 1.5, -1.45 + i * 1.4, 5.7, -.4, materials.edge);
    }
    box(tender, 2.3, 3.2, 2.6, 1.3, 5, -.6, materials.plaster);
    box(tender, 2.6, .2, 2.9, 1.3, 6.7, -.6, materials.roof);
    for (const xx of [.65, 1.35, 2.05]) box(tender, .36, 2.1, .08, xx, 5.15, .75, materials.glass);
    texturePlane(tender, '02', 1.1, .65, 1.3, 6, .77, { size: 90 });
    cylinder(tender, .06, 2.2, -2.3, 4.6, -1.3, materials.edge, 8);
    box(tender, 1.1, .6, .04, -1.8, 5.3, -1.3, materials.copper);
    anchors[1].y = 7.4;
    box(suppliers, 7.8, .22, 3, 0, 2.9, 3, materials.roof);
    for (const x of [-3.3, -.1, 3.3]) cylinder(suppliers, .12, 2.5, x, 1.55, 3.9, materials.stone, 8);
    for (const x of [-2.2, 0, 2.2]) {
        box(suppliers, 1.5, .9, .8, x, .85, 3.3, materials.timber);
        box(suppliers, 1.65, .1, .95, x, 1.35, 3.3, materials.curb);
    }
    box(lab, 3.2, 2.65, 2.4, -2.2, 1.75, 3.4, materials.glass);
    box(lab, 3.5, .17, 2.6, -2.2, 3.16, 3.4, materials.roof);
    for (let i = 0; i < 4; i++) box(lab, .06, 2.5, .09, -3.6 + i * .9, 1.75, 4.63, materials.curb);
    for (const x of [-2, -.5, 1]) {
        cylinder(lab, .38, .65, x, 4.9, -.8, materials.stone);
        cylinder(lab, .2, .14, x, 5.3, -.8, materials.edge);
    }
    box(board, 4.7, .15, 3.1, 0, 5.9, .25, materials.glassLight);
    box(board, 5, .12, .12, 0, 6.1, 1.8, materials.edge);
    for (const x of [-2.6, 2.6]) box(board, .28, 4.6, .32, x, 2.8, 3.05, materials.stone);
    box(board, 6.6, .23, 1.7, 0, 5.5, 3.2, materials.roof);
    for (const x of [-1.5, -.5, .5, 1.5]) cylinder(board, .16, .36, x, 6.15, .2, materials.copper, 12);
    box(archive, 6, .45, 4.2, 0, 3.7, 0, materials.stone);
    box(archive, 5.4, .25, 3.5, 0, 4.05, 0, materials.roof);
    for (const x of [-2.6, -1.3, 0, 1.3, 2.6]) box(archive, .12, 2.5, .2, x, 1.8, 2.8, materials.stone);
    texturePlane(archive, '06  /  AWARD', 3, .65, 0, 3.75, 2.16);
    // Include the publishing tower and glass annex in each building's selectable footprint.
    for (const hit of pickables) {
        const stationGroup = hit.parent;
        stationGroup.updateWorldMatrix(true, true);
        const footprint = new T.Box3().setFromObject(stationGroup).expandByScalar(.08);
        hit.position.copy(footprint.getCenter(new T.Vector3()).sub(stationGroup.position));
        hit.scale.copy(footprint.getSize(new T.Vector3()));
    }

    // A quiet central square, with a fountain and seating rather than more buildings.
    cylinder(city, 4.1, .12, 0, .26, 0, materials.curb, 64);
    cylinder(city, 3.7, .1, 0, .36, 0, materials.path, 64);
    cylinder(city, 2.05, .4, 0, .57, 0, materials.stone, 48);
    cylinder(city, 1.78, .06, 0, .8, 0, materials.water, 48);
    cylinder(city, .32, 1.35, 0, 1.4, 0, materials.roof, 12);
    const sculpture = box(city, .9, .9, .9, 0, 2.25, 0, materials.copper); sculpture.rotation.set(.4, .6, .3);
    for (const [x, z, rotation] of [[-4.6, 1, Math.PI / 2], [4.6, -1, -Math.PI / 2], [-1.5, 4.5, 0], [1.5, -4.5, Math.PI]]) bench(x, z, rotation);
    planter(-4, -3, 2, .8); planter(4, 3, 2, .8);
    texturePlane(city, 'T I R Y A Q', 4.3, .9, -.5, .23, 6, { ground: true, pale: true, size: 68 });
    for (const [x, z] of [[-11, -14], [-11, 11], [11, -14], [11, 11]]) { bench(x, z); tree(x + 1.7, z - 1, .7); }

    // Rounded, right-angle routes follow paved corridors; they never cut through a building.
    const stops = [[-15, -5.2], [-15, 11], [0, 16.5], [-1, -4.9], [14, -4.9], [15, 11.3]];
    const waypointSets = [
        [stops[0], [-11, -5.2], [-11, 11], stops[1]],
        [stops[1], [-11, 11], [-11, 16.5], stops[2]],
        [stops[2], [-5.8, 16.5], [-5.8, -4.9], stops[3]],
        [stops[3], [5.8, -4.9], stops[4]],
        [stops[4], [10.4, -4.9], [10.4, 11.3], stops[5]]
    ];
    function roundedPath(points, y, radius = .7) {
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
    waypointSets.forEach((points, index) => {
        const path = roundedPath(points, .31);
        const paving = new T.Mesh(new T.TubeGeometry(path, 90, .34, 5, false), materials.curb);
        paving.scale.y = .1; paving.position.y = .22; city.add(paving);
        const lineMaterial = new T.MeshBasicMaterial({ color: '#547e72', transparent: true, opacity: .78 });
        const line = new T.Mesh(new T.TubeGeometry(path, 90, .08, 5, false), lineMaterial); city.add(line);
        routes.push({ path, material: lineMaterial });
        const point = path.getPointAt(.56), tangent = path.getTangentAt(.56);
        const arrow = new T.Mesh(new T.ConeGeometry(.22, .58, 3), new T.MeshBasicMaterial({ color: palette.roof }));
        arrow.position.copy(point); arrow.position.y = .35;
        arrow.quaternion.setFromUnitVectors(new T.Vector3(0, 1, 0), tangent); city.add(arrow); routeArrows.push(arrow);
    });
    stops.forEach(([x, z], i) => {
        cylinder(city, .65, .1, x, .32, z, materials.roof, 32);
        texturePlane(city, String(i + 1).padStart(2, '0'), .85, .45, x, .38, z, { ground: true, size: 95 });
    });

    const packet = group(moving);
    box(packet, .72, .92, .12, 0, .6, 0, materials.copper);
    for (const y of [.4, .6, .8]) box(packet, .42, .06, .14, 0, y, .02, materials.white);
    const packetHalo = new T.Mesh(new T.RingGeometry(.5, .66, 32), new T.MeshBasicMaterial({ color: palette.copper, side: T.DoubleSide, transparent: true, opacity: .55 }));
    packetHalo.rotation.x = -Math.PI / 2; packet.add(packetHalo);
    const vans = [], offers = [], kiosks = [], people = [];
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
        box(kiosk, 2.25, .12, 1.8, 0, 1.65, 0, materials.roof);
        for (const x of [-.85, .85]) cylinder(kiosk, .055, 1.4, x, .9, .55, materials.stone, 6);
        kiosks.push(kiosk);
        const offer = group(moving); box(offer, .4, .55, .09, 0, .3, 0, materials.glassLight); offers.push(offer);
    }
    for (let i = 0; i < 16; i++) {
        const person = group(moving);
        cylinder(person, .12, .42, 0, .48, 0, i % 4 ? materials.roof : materials.copper, 7);
        mesh(person, sphere, materials.timber, 0, .82, 0, .14, .14, .14);
        for (const x of [-.065, .065]) box(person, .075, .27, .1, x, .15, 0, materials.edge);
        people.push(person);
    }
    const roadPath = roundedPath([[-25, 16], [-25, -19], [25, -19], [25, 19], [-25, 19], [-25, 16]], .2, 2.2);

    // Repeated facade, landscape and street geometry is instanced to keep draw calls modest.
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
    // Animated figures do not invalidate the static architectural shadow map.
    moving.traverse(object => { object.castShadow = false; });

    const svgNS = 'http://www.w3.org/2000/svg', leaders = document.createElementNS(svgNS, 'svg');
    leaders.classList.add('campus-map-leaders'); leaders.setAttribute('aria-hidden', 'true');
    const leaderLines = labels.map(button => {
        const name = document.createElement('span'); name.className = 'campus-place-name';
        name.textContent = button.lastChild.textContent; button.lastChild.replaceWith(name);
        button.setAttribute('aria-label', `Step ${Number(button.dataset.campusPlace) + 1}: ${name.textContent}`);
        const line = document.createElementNS(svgNS, 'path'); leaders.append(line); return line;
    });
    host.querySelector('.campus-labels').prepend(leaders);
    let labelSizes = [];
    function measureLabels() { labelSizes = labels.map(button => { const r = button.getBoundingClientRect(); return { w: r.width || 48, h: r.height || 44 }; }); }
    function resize() {
        width = Math.max(1, host.clientWidth); height = Math.max(1, host.clientHeight); aspect = width / height;
        renderer.setSize(width, height, false); leaders.setAttribute('viewBox', `0 0 ${width} ${height}`); measureLabels();
    }
    const observer = new ResizeObserver(resize); observer.observe(host); resize();
    const projection = new T.Vector3();
    function positionLabels() {
        const used = [], order = labels.map((_, i) => i).sort((a, b) => Number(b === selected) - Number(a === selected));
        order.forEach(i => {
            const point = projection.copy(anchors[i]).project(camera), button = labels[i], size = labelSizes[i];
            const anchorX = (point.x * .5 + .5) * width, anchorY = (-point.y * .5 + .5) * height;
            if (point.z < -1 || point.z > 1 || anchorX < -20 || anchorX > width + 20 || anchorY < -20 || anchorY > height + 20) {
                button.hidden = true; leaderLines[i].setAttribute('d', ''); return;
            }
            const offsets = [[0, -8], [0, -40], [-40, -8], [40, -8], [0, size.h + 14], [-45, 35], [45, 35], [-65, -35], [65, -35], [0, 80]];
            let best = null;
            for (const [dx, dy] of offsets) {
                const x = clamp(anchorX + dx, size.w / 2 + 8, width - size.w / 2 - 8);
                const y = clamp(anchorY + dy, size.h + 8, height - 65);
                const overlap = used.reduce((total, r) => total + Math.max(0, Math.min(x + size.w / 2 + 4, r.right) - Math.max(x - size.w / 2 - 4, r.left)) * Math.max(0, Math.min(y + 4, r.bottom) - Math.max(y - size.h - 4, r.top)), 0);
                const cost = overlap * 100 + Math.hypot(x - anchorX, y - anchorY + 8);
                if (!best || cost < best.cost) best = { x, y, cost };
            }
            const { x, y } = best;
            button.hidden = false; button.style.left = `${x}px`; button.style.top = `${y}px`;
            used.push({ left: x - size.w / 2, right: x + size.w / 2, top: y - size.h, bottom: y });
            leaderLines[i].setAttribute('d', `M${x},${y} L${anchorX},${anchorY + 5}`);
            leaderLines[i].classList.toggle('is-selected', i === selected);
        });
    }

    // Stable framing keeps manual orbit from changing the zoom.
    function updateCamera() {
        camera.position.set(center.x + Math.sin(angle) * 70, elevation, center.z + Math.cos(angle) * 70);
        camera.lookAt(center.x, 0, center.z); camera.updateMatrixWorld();
        const half = (inside ? Math.max(11, 13 / aspect) : Math.max(29, 42 / aspect)) / zoom;
        camera.left = -half * aspect; camera.right = half * aspect; camera.top = half; camera.bottom = -half;
        camera.updateProjectionMatrix();
    }
    function animate(now) {
        frame = requestAnimationFrame(animate);
        if (document.hidden) { last = now; return; }
        const dt = Math.min(.05, (now - (last || now)) / 1000); last = now;
        if (!stopped && !reduced.matches) time += dt;
        const blend = reduced.matches ? 1 : 1 - Math.exp(-dt * (down ? 18 : 8));
        elevation += (targetElevation - elevation) * blend;
        angle += (targetAngle - angle) * blend; zoom += (targetZoom - zoom) * blend;
        center.lerp(targetCenter, blend); progress += (targetProgress - progress) * blend; updateCamera();
        buildings.forEach((building, i) => {
            const active = i === selected, over = i === hovered;
            building.material.emissive.set(active ? '#65977e' : over ? '#7d9b8b' : '#000000');
            building.material.emissiveIntensity = active ? .16 : over ? .08 : 0;
            building.ring.material.opacity = active ? .85 : over ? .4 : 0;
            building.ring.scale.setScalar(active && !stopped && !reduced.matches ? 1 + Math.sin(time * 2) * .018 : 1);
        });
        routes.forEach((route, i) => {
            route.material.color.set(i < selected ? palette.roof : i === selected ? palette.copper : '#547e72');
            route.material.opacity = i <= selected ? .95 : .78;
            routeArrows[i].material.color.set(i < selected ? palette.roof : i === selected ? palette.copper : '#547e72');
        });
        if (selected < 0) packet.position.set(stops[0][0], .45, stops[0][1]);
        else {
            const position = clamp(progress, 0, 1) * 5, index = Math.min(4, Math.floor(position));
            packet.position.copy(routes[index].path.getPointAt(position - index)); packet.position.y = .5;
        }
        packet.position.y += stopped || reduced.matches ? 0 : Math.sin(time * 2) * .05;
        for (let i = 0; i < 6; i++) {
            vans[i].visible = kiosks[i].visible = i < offerCount;
            const t = (time * .012 + i / 6) % 1, location = roadPath.getPointAt(t), tangent = roadPath.getTangentAt(t);
            vans[i].position.copy(location); vans[i].rotation.y = -Math.atan2(tangent.z, tangent.x);
            offers[i].visible = i < offerCount && (selected === 2 || selected === 3);
            const phase = (time * .12 + i / offerCount) % 1;
            offers[i].position.lerpVectors(new T.Vector3(kiosks[i].position.x, .6, kiosks[i].position.z), new T.Vector3(-1, .6, -4.9), phase);
            offers[i].position.y += Math.sin(phase * Math.PI) * 2.5;
        }
        people.forEach((person, i) => {
            const route = routes[i % routes.length].path, phase = (time * .022 + i * .17) % 1;
            person.position.copy(route.getPointAt(phase)); person.position.y = .33;
            const tangent = route.getTangentAt(phase); person.rotation.y = -Math.atan2(tangent.z, tangent.x) + Math.PI / 2;
        });
        positionLabels(); renderer.render(scene, camera);
    }

    const ray = new T.Raycaster(), pointer = new T.Vector2(); let down = null;
    function pick(event) {
        const r = canvas.getBoundingClientRect(); pointer.set((event.clientX - r.left) / r.width * 2 - 1, -(event.clientY - r.top) / r.height * 2 + 1);
        ray.setFromCamera(pointer, camera); return ray.intersectObjects(pickables)[0]?.object.userData.place;
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
                    targetAngle -= dx * .0035;
                    targetElevation = clamp(targetElevation + dy * .14, 35, 100);
                }
            }
            down.last = e.clientX; down.lastY = e.clientY;
        } else if (e.pointerType === 'mouse') {
            hovered = pick(e) ?? -1; canvas.style.cursor = hovered >= 0 ? 'pointer' : 'grab';
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
    canvas.addEventListener('wheel', e => { e.preventDefault(); const savedCenter = targetCenter.clone(), savedZoom = targetZoom; onInteract?.(); targetCenter.copy(savedCenter); const delta = e.deltaY * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? height : 1); targetZoom = clamp(savedZoom * Math.exp(-clamp(delta, -120, 120) * .0015), .8, 2.2); }, { passive: false });
    canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); cancelAnimationFrame(frame); onFallback(new Error('Graphics view interrupted')); });
    canvas.addEventListener('webglcontextrestored', () => window.location.reload());
    window.addEventListener('pagehide', () => cancelAnimationFrame(frame));
    window.addEventListener('pageshow', e => { if (e.persisted) { last = 0; frame = requestAnimationFrame(animate); } });
    frame = requestAnimationFrame(animate);
    return {
        select(i) { selected = i; targetCenter.set(i < 0 ? 0 : stages[i][4][0] * (inside ? 1 : .35), 0, i < 0 ? 0 : stages[i][4][1] * (inside ? 1 : .35)); targetZoom = inside || i < 0 ? 1 : small.matches ? 1.45 : 1.14; measureLabels(); },
        enter() { inside = true; targetElevation = 40; targetZoom = 1; },
        follow(value) { if (!inside) return; const p = clamp(value, 0, 1) * 5, i = Math.min(4, Math.floor(p)); targetCenter.copy(routes[i].path.getPointAt(p - i)); },
        progress(value) { targetProgress = value; }, mode(value) { mode = value; }, pause(value) { stopped = value; },
        suppliers(value) { offerCount = value; }, zoom(direction) { targetZoom = clamp(targetZoom + direction * .2, .8, 2.2); },
        rotate(direction) { targetAngle += direction * Math.PI / 8; },
        reset() { inside = false; targetElevation = 72; targetAngle = .36; targetZoom = 1; targetCenter.set(0, 0, 0); targetProgress = 0; }
    };
}
