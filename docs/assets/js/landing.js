(function () {
  'use strict';

  var root = document.documentElement;
  if (!root.getAttribute('data-theme')) {
    root.setAttribute('data-theme', 'dark');
  }

  // Drifting particle field (same behaviour as the Blazor app's initialize()).
  var field = document.getElementById('particles');
  if (field && field.childElementCount === 0) {
    for (var i = 0; i < 200; i++) {
      var p = document.createElement('div');
      p.className = 'particle';
      p.style.left = Math.random() * 100 + '%';
      p.style.top = Math.random() * 100 + '%';
      p.style.animationDuration = (15 + Math.random() * 50) + 's';
      p.style.animationDelay = (Math.random() * 50) + 's';
      p.style.width = (2 + Math.random() * 4) + 'px';
      p.style.height = p.style.width;
      field.appendChild(p);
    }
  }

  var themeBtn = document.querySelector('.theme-btn');
  if (themeBtn) {
    themeBtn.setAttribute('aria-label', 'Toggle light and dark theme');
    themeBtn.addEventListener('click', function () {
      var next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
      root.setAttribute('data-theme', next);
      themeBtn.setAttribute('title', next === 'light' ? 'Switch to dark theme' : 'Switch to light theme');
    });
  }

  // Bootstrap's collapse plugin is not part of this static export.
  var toggler = document.querySelector('.navbar-toggler');
  var nav = document.getElementById('mainNav');
  if (toggler && nav) {
    toggler.addEventListener('click', function () {
      var open = nav.classList.toggle('show');
      toggler.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    nav.addEventListener('click', function (event) {
      if (event.target.closest('a')) {
        nav.classList.remove('show');
        toggler.setAttribute('aria-expanded', 'false');
      }
    });
  }

  var sections = Array.prototype.slice.call(document.querySelectorAll('section[id]'));
  var links = Array.prototype.slice.call(document.querySelectorAll('#mainNav .nav-link'));
  function highlight() {
    var probe = window.scrollY + 120;
    var current = '';
    sections.forEach(function (section) {
      if (probe >= section.offsetTop && probe < section.offsetTop + section.offsetHeight) {
        current = section.id;
      }
    });
    links.forEach(function (link) {
      link.classList.toggle('active', link.getAttribute('href') === '#' + current);
    });
  }
  window.addEventListener('scroll', highlight, { passive: true });
  highlight();
})();
