(() => {
    'use strict';
    const page = document.querySelector('.journey-video-page');
    const video = page?.querySelector('video');
    if (!video) return;
    const buttons = [...page.querySelectorAll('[data-journey-seek]')];
    const cards = [...page.querySelectorAll('[data-journey-stage]')];
    const status = page.querySelector('[data-journey-current]');
    let pendingSeek = null, current = -1;
    function update() {
        if (!video.currentTime && video.paused && !video.ended) {
            cards.forEach(card => card.classList.remove('is-playing'));
            buttons.forEach(button => button.removeAttribute('aria-current'));
            current = -1;
            status.textContent = 'Start the walkthrough, or choose a step below.';
            return;
        }
        let index = 0;
        buttons.forEach((button, i) => { if (video.currentTime >= Number(button.dataset.journeySeek)) index = i; });
        if (index !== current || video.ended) {
            current = index;
            cards.forEach((card, i) => card.classList.toggle('is-playing', i === index));
            buttons.forEach((button, i) => { if (i === index) button.setAttribute('aria-current', 'step'); else button.removeAttribute('aria-current'); });
            status.textContent = video.ended ? 'Journey complete. Replay any step below.' : `Step ${index + 1} of 6 · ${cards[index].querySelector('h3').textContent}`;
        }
    }
    function seek(time) {
        if (video.readyState === 0) { pendingSeek = time; video.load(); return; }
        video.currentTime = time;
        video.play().catch(() => { status.textContent = 'Press Play to watch this step.'; });
        video.scrollIntoView({ behavior:matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block:'center' });
        update();
    }
    buttons.forEach(button => {
        button.hidden = false;
        button.addEventListener('click', () => seek(Number(button.dataset.journeySeek)));
    });
    video.addEventListener('loadedmetadata', () => { if (pendingSeek !== null) { const time = pendingSeek; pendingSeek = null; seek(time); } });
    ['timeupdate', 'seeked', 'play', 'ended'].forEach(event => video.addEventListener(event, update));
    video.addEventListener('error', () => {
        buttons.forEach(button => button.hidden = true);
        status.textContent = 'Video unavailable. You can still read the steps and transcript below.';
    });
})();
