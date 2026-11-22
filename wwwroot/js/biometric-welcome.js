// Success moment for Face ID / fingerprint sign-in. Resolves when the redirect may proceed.
(() => {
    const ICONS = {
        face: 'M8 3H5a2 2 0 0 0-2 2v3m13-5h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3m13-5v3a2 2 0 0 1-2 2h-3M8 9v1m8-1v1m-4-1v4m-3 3c1.5 1.5 4.5 1.5 6 0',
        fingerprint: 'M3 12a9 9 0 0 1 18 0v2M6 16v-4a6 6 0 0 1 12 0v3c0 2 .5 3.5 1.5 5M9 21c1-2 1-4 1-6v-3a2 2 0 0 1 4 0v3c0 2.5-.4 5-1.5 7M6 19c.6-1 .8-3 .8-4m9.7 5.5-.5 1'
    };
    const LABEL = { face: 'Face recognised', fingerprint: 'Fingerprint matched' };
    const hex = (r) => Array.from({ length: 6 }, (_, i) => {
        const a = Math.PI / 3 * i - Math.PI / 2;
        return `${(r * Math.cos(a)).toFixed(2)},${(r * Math.sin(a)).toFixed(2)}`;
    }).join(' ');

    window.tiryaqBiometricWelcome = (kind, origin) => {
        if (matchMedia('(prefers-reduced-motion: reduce)').matches) return new Promise(r => setTimeout(r, 300));
        const box = origin?.getBoundingClientRect();
        const x = box ? box.left + box.width / 2 : innerWidth / 2;
        const y = box ? box.top + box.height / 2 : innerHeight / 2;
        const shards = Array.from({ length: 14 }, (_, i) => {
            const a = (Math.PI * 2 / 14) * i + Math.random() * .3, d = 120 + Math.random() * 90;
            return `<polygon class="bw-shard" points="${hex(5 + Math.random() * 5)}" style="--dx:${(Math.cos(a) * d).toFixed(0)}px;--dy:${(Math.sin(a) * d).toFixed(0)}px;--d:${(Math.random() * .15).toFixed(2)}s"/>`;
        }).join('');
        const el = document.createElement('div');
        el.className = 'bw';
        el.setAttribute('role', 'status');
        el.style.setProperty('--x', x + 'px');
        el.style.setProperty('--y', y + 'px');
        el.innerHTML = `
<svg class="bw-mark" viewBox="-120 -120 240 240" aria-hidden="true">
  <polygon class="bw-ring" points="${hex(92)}"/>
  <polygon class="bw-ring bw-ring2" points="${hex(108)}"/>
  <g class="bw-shards">${shards}</g>
  <g class="bw-icon" transform="translate(-60 -60) scale(5)"><path d="${ICONS[kind] || ICONS.fingerprint}" pathLength="1"/></g>
  <rect class="bw-scan" x="-70" y="-3" width="140" height="6" rx="3"/>
  <path class="bw-check" d="M-30 2 L-8 24 L34 -22" pathLength="1"/>
</svg>
<p class="bw-kicker">${LABEL[kind] || 'Identity verified'}</p>
<h2 class="bw-title">Welcome back</h2>
<p class="bw-sub">Opening your workspace</p>`;
        document.body.appendChild(el);
        requestAnimationFrame(() => el.classList.add('is-on'));
        return new Promise(r => setTimeout(r, 2300));
    };
})();
