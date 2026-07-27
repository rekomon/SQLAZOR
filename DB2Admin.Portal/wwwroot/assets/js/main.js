let isDark = true;

window.CheckThemeMode = () => {
	try {
		var theme = document.documentElement.getAttribute('data-theme');
		
		if (theme === 'dark') {
			isDark = true;
			return true;
			
		}
		else {
			isDark = false;
			return false;
		}
	} catch  {
		
	}

}

window.InitParticles = () => {
	const container = document.getElementById('particles');
	for (let i = 0; i < 100; i++) {
		const p = document.createElement('div');
		p.className = 'particle';
		p.style.left = Math.random() * 100 + '%';
		p.style.top = Math.random() * 100 + '%';
		p.style.animationDuration = (15 + Math.random() * 50) + 's';
		p.style.animationDelay = (Math.random() * 50) + 's';
		p.style.width = (2 + Math.random() * 4) + 'px';
		p.style.height = p.style.width;
		container.appendChild(p);
	}
}

window.toggleTheme = () => {
	isDark = !isDark;
	document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');

	// Update SlimScroll bars (if they exist)
	const cyan = getComputedStyle(document.documentElement).getPropertyValue('--cu-neon-cyan').trim();
	document.querySelectorAll('.slimScrollBar').forEach(bar => {
		bar.style.background = cyan || '#00f0ff';
		bar.style.boxShadow = '0 0 12px ' + (cyan || '#00f0ff');
	});
	CheckThemeMode();
}

window.animateCounters = () => {
	document.querySelectorAll('.stat-value[data-target]').forEach(el => {
		const target = parseFloat(el.dataset.target.replace(/,/g, ''));
		const isCurrency = el.textContent.includes('$');
		const isPercent = el.textContent.includes('%');
		let current = 0;
		const steps = 55;
		const increment = target / steps;
		let count = 0;
		const timer = setInterval(() => {
			count++;
			current += increment;
			if (count >= steps) {
				current = target;
				clearInterval(timer);
			}
			let display = Math.floor(current).toLocaleString();
			if (isCurrency) display = '$' + display;
			if (isPercent) display = current.toFixed(2) + '%';
			el.textContent = display;
		}, 20);
	});
}

window.InitTabsandPills = () => {
	const testTab = document.querySelector('[data-bs-toggle="tab"]');
	if (testTab && typeof bootstrap !== 'undefined') {
		// Bootstrap JS is available, use it
		console.log('Bootstrap tabs/pills active.');
	}
	else {
		// Fallback: manually handle clicks
		console.warn('Bootstrap JS not detected – using manual tab/pill fallback.');
		document.querySelectorAll('[data-bs-toggle="tab"], [data-bs-toggle="pill"]').forEach(trigger => {
			trigger.addEventListener('click', function (e) {
				e.preventDefault();
				const targetId = this.getAttribute('data-bs-target');
				if (!targetId) return;
				const targetPane = document.querySelector(targetId);
				if (!targetPane) return;
				// Deactivate all tabs/pills in the same parent
				const parent = this.closest('.nav');
				if (parent) {
					parent.querySelectorAll('.nav-link').forEach(link => link.classList.remove(
						'active'));
					parent.querySelectorAll('.nav-link').forEach(link => {
						const paneId = link.getAttribute('data-bs-target');
						if (paneId) {
							const pane = document.querySelector(paneId);
							if (pane) pane.classList.remove('show', 'active');
						}
					});
				}
				// Activate this one
				this.classList.add('active');
				targetPane.classList.add('show', 'active');
			});
		});
		// Ensure the initial active tab/pill is shown
		document.querySelectorAll('.nav-link.active').forEach(activeLink => {
			const targetId = activeLink.getAttribute('data-bs-target');
			if (targetId) {
				const pane = document.querySelector(targetId);
				if (pane) pane.classList.add('show', 'active');
			}
		});
	}
}


window.InitNavActive = () => {
	document.querySelectorAll('.navbar .nav-link').forEach(link => {
		link.addEventListener('click', function (e) {
			document.querySelectorAll('.navbar .nav-link').forEach(l => l.classList.remove('active'));
			this.classList.add('active');
		});
	});
}

window.InitSlimScroll = () => {
	if ($.fn.slimScroll) {
		// Apply to main wrapper
		$('#mainScrollWrapper').slimScroll({
			height: '100%',
			size: '5px',
			color: '#00f0ff',
			alwaysVisible: false,
			distance: '4px',
			railVisible: true,
			railColor: 'rgba(0,240,255,0.05)',
			railOpacity: 1,
			railBorderRadius: '4px',
			railWidth: '5px',
			allowPageScroll: false,
			wheelStep: 10,
			touchScrollStep: 20,
		});

		$('.slimscroll').slimScroll({
			height: '160px',
			size: '5px',
			color: '#00f0ff',
			alwaysVisible: false,
			distance: '2px',
			railVisible: true,
			railColor: 'rgba(0,240,255,0.05)',
			railOpacity: 1,
			railBorderRadius: '4px',
			railWidth: '5px',
			allowPageScroll: false,
			wheelStep: 10,
			touchScrollStep: 20,
		});


		setTimeout(() => {
			const cyan = getComputedStyle(document.documentElement).getPropertyValue('--cu-neon-cyan')
				.trim() || '#00f0ff';
			document.querySelectorAll('.slimScrollBar').forEach(bar => {
				bar.style.background = cyan;
				bar.style.borderRadius = '4px';
				bar.style.width = '5px';
				bar.style.boxShadow = '0 0 12px ' + cyan;
				bar.style.opacity = '0.8';
			});
			document.querySelectorAll('.slimScrollRail').forEach(rail => {
				rail.style.background = 'rgba(0,240,255,0.05)';
				rail.style.borderRadius = '4px';
				rail.style.width = '5px';
			});
		}, 100);


	} else {
	
		const wrapper = document.getElementById('mainScrollWrapper');
		wrapper.classList.add('native-scroll');
		try {
			const scroll = document.getElementsByClassName('slimscroll');
			if (scroll) {
				scroll.classList.add('native-scroll');
				scroll.style.height = '160px';
			}
		} catch { }
		$('.slimScrollBar, .slimScrollRail').remove();
	}

	if ($.fn.slimScroll) {
		$(window).resize(function () {
			$('#mainScrollWrapper').slimScroll({ destroy: true });
			$('#mainScrollWrapper').slimScroll({
				height: '100%',
				size: '5px',
				color: '#00f0ff',
				alwaysVisible: false,
				distance: '4px',
				railVisible: true,
				railColor: 'rgba(0,240,255,0.05)',
				railOpacity: 1,
				railBorderRadius: '4px',
				railWidth: '5px',
				allowPageScroll: false,
				wheelStep: 10,
				touchScrollStep: 20,
			});
			setTimeout(() => {
				const cyan = getComputedStyle(document.documentElement).getPropertyValue(
					'--cu-neon-cyan').trim() || '#00f0ff';
				document.querySelectorAll('.slimScrollBar').forEach(bar => {
					bar.style.background = cyan;
					bar.style.boxShadow = '0 0 12px ' + cyan;
				});
			}, 50);
		});
	}
}


window.initialize = () => {
	CheckThemeMode();
	InitParticles();
	animateCounters();
	InitTabsandPills();
	InitNavActive();
	InitSlimScroll();
	HighlightActiveNavScroll();
}

window.HighlightActiveNavScroll = () => {
	const sections = document.querySelectorAll('section[id]');
	const navLinks = document.querySelectorAll('.navbar .nav-link:not(.btn)');

	window.addEventListener('scroll', () => {
		let current = '';
		const scrollPos = window.scrollY + 120;
		sections.forEach(section => {
			const sectionTop = section.offsetTop;
			const sectionHeight = section.offsetHeight;
			if (scrollPos >= sectionTop && scrollPos < sectionTop + sectionHeight) {
				current = section.getAttribute('id');
			}
		});
		navLinks.forEach(link => {
			link.classList.remove('active');
			if (link.getAttribute('href') === '#' + current) {
				link.classList.add('active');
			}
		});
	});
};

/* Downloads*/

window.downloadFileFromStream = async (fileName, contentStreamReference) => {
	const arrayBuffer = await contentStreamReference.arrayBuffer();
	const blob = new Blob([arrayBuffer]);
	const url = URL.createObjectURL(blob);
	const anchorElement = document.createElement('a');
	anchorElement.href = url;
	anchorElement.download = fileName ?? '';
	anchorElement.click();
	anchorElement.remove();
	URL.revokeObjectURL(url);
}

setTimeout(animateCounters, 400);



