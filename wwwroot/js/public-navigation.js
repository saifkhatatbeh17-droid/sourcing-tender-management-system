(() => {
 const navbar = document.querySelector('.public-page-nav');
 if (!navbar) return;
 let previous = 0;
 new ResizeObserver(() => {
  const height = Math.ceil(navbar.getBoundingClientRect().height);
  if (height === previous) return;
  previous = height;
  document.documentElement.style.setProperty('--public-nav-height', `${height}px`);
  window.dispatchEvent(new Event('publicnavresize'));
 }).observe(navbar);
})();
