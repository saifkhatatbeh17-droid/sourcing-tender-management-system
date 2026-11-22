// Team page accordion reel: one panel open at a time (hover on desktop, tap or keyboard everywhere).
(() => {
    const reel = document.querySelector('[data-team-reel]');
    if (!reel) return;

    const panels = [...reel.querySelectorAll('[data-team-panel]')];
    const triggers = panels.map(panel => panel.querySelector('.team-panel-trigger'));
    const details = panels.map(panel => panel.querySelector('.team-panel-detail'));
    const canHover = matchMedia('(hover: hover) and (pointer: fine)');
    let active = Math.max(0, panels.findIndex(panel => panel.classList.contains('is-active')));
    let hoverTimer = 0;

    function activate(index, focus = false) {
        if (index !== active) {
            panels.forEach((panel, i) => {
                const open = i === index;
                panel.classList.toggle('is-active', open);
                triggers[i].setAttribute('aria-expanded', String(open));
                details[i].inert = !open;
            });
            active = index;
        }
        if (focus) triggers[index].focus();
    }

    panels.forEach((panel, i) => {
        details[i].inert = i !== active;
        triggers[i].addEventListener('click', () => activate(i));
        panel.addEventListener('pointerenter', () => {
            if (!canHover.matches) return;
            clearTimeout(hoverTimer);
            hoverTimer = setTimeout(() => activate(i), 90);
        });
    });
    reel.addEventListener('pointerleave', () => clearTimeout(hoverTimer));

    const steps = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };
    reel.addEventListener('keydown', event => {
        const from = triggers.indexOf(event.target);
        if (from < 0) return;
        let to;
        if (event.key in steps) to = (from + steps[event.key] + panels.length) % panels.length;
        else if (event.key === 'Home') to = 0;
        else if (event.key === 'End') to = panels.length - 1;
        else return;
        event.preventDefault();
        activate(to, true);
    });
})();
