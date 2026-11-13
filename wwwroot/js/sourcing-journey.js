document.querySelectorAll('.guide-journey').forEach(journey => {
    const scroller = journey.querySelector('.guide-journey-scroll');
    const track = journey.querySelector('.guide-journey-track');
    const controls = journey.querySelector('.guide-journey-controls');
    const [previous, next] = controls.querySelectorAll('button');

    const updateControls = () => {
        const end = scroller.scrollWidth - scroller.clientWidth;
        controls.hidden = end <= 1;
        previous.disabled = scroller.scrollLeft <= 1;
        next.disabled = scroller.scrollLeft >= end - 1;
    };

    controls.addEventListener('click', event => {
        const button = event.target.closest('button[data-direction]');
        if (!button) return;
        const [first, second] = track.children;
        scroller.scrollBy({ left: (second.offsetLeft - first.offsetLeft) * Number(button.dataset.direction) });
    });
    scroller.addEventListener('scroll', updateControls, { passive: true });
    window.addEventListener('resize', updateControls);
    updateControls();

    if ('IntersectionObserver' in window) {
        const observer = new IntersectionObserver(([entry]) => {
            track.classList.toggle('is-visible', entry.isIntersecting);
        }, { rootMargin: '0px 0px -12% 0px' });
        observer.observe(scroller);
    }
});
