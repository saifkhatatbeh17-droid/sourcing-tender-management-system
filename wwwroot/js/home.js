(() => {
    const header = document.querySelector('.landing-header');
    const hero = document.querySelector('.landing-hero');
    if (header) {
        const updateHeader = () => {
            const isScrolled = header.classList.contains('is-scrolled');
            // Keep the current appearance through small scroll reversals near the top.
            header.classList.toggle('is-scrolled', window.scrollY > (isScrolled ? 12 : 36));
            if (hero) header.classList.toggle('is-past-hero', hero.getBoundingClientRect().bottom <= 1);
        };
        updateHeader();
        window.addEventListener('scroll', updateHeader, { passive: true });
        window.addEventListener('resize', updateHeader);
        window.addEventListener('pageshow', updateHeader);
    }

    const video = document.getElementById('landing-video');
    const button = document.querySelector('.landing-video-toggle');
    if (!video || !button) return;

    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const play = () => {
        if (!video.hasAttribute('src')) video.src = video.dataset.src;
        video.muted = true;
        video.play().catch(() => {});
    };

    const updateButton = () => {
        button.textContent = video.paused ? 'Play background video' : 'Pause background video';
    };

    button.hidden = false;
    video.addEventListener('play', updateButton);
    video.addEventListener('pause', updateButton);
    video.addEventListener('error', () => { button.hidden = true; });
    button.addEventListener('click', () => video.paused ? play() : video.pause());
    motion.addEventListener('change', event => {
        if (event.matches) video.pause();
    });
    if (!motion.matches) play();
})();
