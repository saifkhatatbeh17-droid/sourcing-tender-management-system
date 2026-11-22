document.querySelectorAll('.notice-center').forEach(center => {
    const list = center.querySelector('.notice-center-list');
    if (!list) return;

    const items = [...list.children];
    const filters = [...center.querySelectorAll('[data-notice-filter]')];
    const empty = center.querySelector('[data-notice-empty]');
    const relativeTime = new Intl.RelativeTimeFormat('en', { numeric: 'auto', style: 'short' });

    center.parentElement.addEventListener('show.bs.dropdown', () => {
        for (const stamp of list.querySelectorAll('time')) {
            const minutes = Math.floor((Date.now() - Date.parse(stamp.dateTime)) / 60000);
            if (!Number.isFinite(minutes) || minutes < 0) continue;
            if (minutes < 1) stamp.textContent = 'Just now';
            else if (minutes < 60) stamp.textContent = relativeTime.format(-minutes, 'minute');
            else if (minutes < 1440) stamp.textContent = relativeTime.format(-Math.floor(minutes / 60), 'hour');
            else if (minutes < 10080) stamp.textContent = relativeTime.format(-Math.floor(minutes / 1440), 'day');
            else stamp.textContent = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(stamp.dateTime));
        }
    });

    for (const button of filters) {
        button.addEventListener('click', () => {
            const filter = button.dataset.noticeFilter;
            let visible = 0;
            for (const item of items) {
                const group = item.querySelector('[data-notice-group]')?.dataset.noticeGroup;
                const matches = filter === 'all' || (filter === 'unread' && item.classList.contains('is-unread')) || filter === group;
                item.hidden = !matches;
                if (matches) visible++;
            }
            for (const option of filters) option.setAttribute('aria-pressed', String(option === button));
            empty.hidden = visible > 0;
            list.scrollTop = 0;
        });
    }
});
