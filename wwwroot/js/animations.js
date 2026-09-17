// Scroll Reveal Animation
const revealElements = document.querySelectorAll('.reveal');

const scrollReveal = () => {
    revealElements.forEach(el => {
        const elementTop = el.getBoundingClientRect().top;
        const revealPoint = 150;

        if (elementTop < window.innerHeight - revealPoint) {
            el.classList.add('active');
        }
    });
};

window.addEventListener('scroll', scrollReveal);
window.addEventListener('load', scrollReveal);

// ==========================================
// 2. Shrinking Header on Scroll
// ==========================================
const mainHeader = document.getElementById('mainHeader');

const handleHeaderShrink = () => {
    if (!mainHeader) return;
    if (window.scrollY > 40) {
        mainHeader.classList.add('scrolled');
    } else {
        mainHeader.classList.remove('scrolled');
    }
};

window.addEventListener('scroll', handleHeaderShrink, { passive: true });
handleHeaderShrink();

// ==========================================
// 3. Theme Switcher (Dropdown Select)
// ==========================================
const themeDropdown = document.getElementById('themeDropdown');
const themeDropdownTrigger = document.getElementById('themeDropdownTrigger');
const themeCurrentIcon = document.getElementById('themeCurrentIcon');
const themeCurrentName = document.getElementById('themeCurrentName');
const themeDropdownItems = document.querySelectorAll('.theme-dropdown-item');

const headerLogo = document.querySelector('.header-logo-img');
const footerLogo = document.querySelector('.footer-logo-img');
const favicon = document.querySelector('link[rel="icon"]');

const THEME_CLASSES = ['dark-theme', 'light-theme', 'red-theme', 'green-theme', 'water-theme', 'gold-theme'];

const THEME_META = {
    gold:  { name: 'Zlatý',  icon: '✨' },
    dark:  { name: 'Tmavý',  icon: '🌙' },
    light: { name: 'Světlý', icon: '☀️' },
    red:   { name: 'Plamen', icon: '🔥' },
    green: { name: 'Les',    icon: '🌲' },
    water: { name: 'Voda',   icon: '💧' }
};

const setAppTheme = (themeName) => {
    if (!THEME_META[themeName]) themeName = 'gold';

    // Remove all theme classes
    THEME_CLASSES.forEach(cls => document.documentElement.classList.remove(cls));
    
    // Add selected theme class
    document.documentElement.classList.add(`${themeName}-theme`);
    
    // Update logo source based on theme
    const isLight = themeName === 'light';
    const logoSrc = isLight ? 'img/PatriotiLogo.webp' : 'img/PatriotiLogoBlack.webp';
    
    if (headerLogo) headerLogo.src = logoSrc;
    if (footerLogo) footerLogo.src = logoSrc;
    if (favicon) favicon.href = logoSrc;
    
    // Update trigger button label & icon
    const meta = THEME_META[themeName];
    if (themeCurrentIcon) themeCurrentIcon.textContent = meta.icon;
    if (themeCurrentName) themeCurrentName.textContent = meta.name;

    // Update active class in dropdown menu
    themeDropdownItems.forEach(item => {
        if (item.getAttribute('data-theme') === themeName) {
            item.classList.add('active');
        } else {
            item.classList.remove('active');
        }
    });
};

// Toggle dropdown open/close
if (themeDropdownTrigger && themeDropdown) {
    themeDropdownTrigger.addEventListener('click', (e) => {
        e.stopPropagation();
        const isOpen = themeDropdown.classList.toggle('open');
        themeDropdownTrigger.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
    });

    // Close on item click
    themeDropdownItems.forEach(item => {
        item.addEventListener('click', (e) => {
            e.stopPropagation();
            const selectedTheme = item.getAttribute('data-theme');
            localStorage.setItem('patrioti_theme', selectedTheme);
            localStorage.setItem('theme', selectedTheme);
            setAppTheme(selectedTheme);
            themeDropdown.classList.remove('open');
            themeDropdownTrigger.setAttribute('aria-expanded', 'false');
        });
    });

    // Close on outside click
    document.addEventListener('click', (e) => {
        if (!themeDropdown.contains(e.target)) {
            themeDropdown.classList.remove('open');
            themeDropdownTrigger.setAttribute('aria-expanded', 'false');
        }
    });
}

// Initialize Theme on load - Gold is default
const savedTheme = localStorage.getItem('patrioti_theme') || localStorage.getItem('theme') || 'gold';
setAppTheme(savedTheme);

// ==========================================
// 4. Live Countdown to Event (25. 9. 2026 15:00)
// ==========================================
const eventDate = new Date('2026-09-25T15:00:00+02:00').getTime();
const cdDays = document.getElementById('cdDays');
const cdHours = document.getElementById('cdHours');
const cdMinutes = document.getElementById('cdMinutes');
const cdSeconds = document.getElementById('cdSeconds');

const updateCountdown = () => {
    if (!cdDays || !cdHours || !cdMinutes || !cdSeconds) return;

    const now = new Date().getTime();
    const distance = eventDate - now;

    if (distance <= 0) {
        cdDays.textContent = '00';
        cdHours.textContent = '00';
        cdMinutes.textContent = '00';
        cdSeconds.textContent = '00';
        const headerTitle = document.querySelector('.countdown-title');
        if (headerTitle) headerTitle.textContent = 'Setkání právě probíhá nebo skončilo!';
        return;
    }

    const days = Math.floor(distance / (1000 * 60 * 60 * 24));
    const hours = Math.floor((distance % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const minutes = Math.floor((distance % (1000 * 60 * 60)) / (1000 * 60));
    const seconds = Math.floor((distance % (1000 * 60)) / 1000);

    cdDays.textContent = String(days).padStart(2, '0');
    cdHours.textContent = String(hours).padStart(2, '0');
    cdMinutes.textContent = String(minutes).padStart(2, '0');
    cdSeconds.textContent = String(seconds).padStart(2, '0');
};

updateCountdown();
setInterval(updateCountdown, 1000);

// ==========================================
// 5. iCal (.ics) Download for Event
// ==========================================
const downloadIcsBtn = document.getElementById('downloadIcsBtn');

if (downloadIcsBtn) {
    downloadIcsBtn.addEventListener('click', (e) => {
        e.preventDefault();

        const icsContent = [
            'BEGIN:VCALENDAR',
            'VERSION:2.0',
            'PRODID:-//Patrioti Trutnov//Beseda 2026//CS',
            'CALSCALE:GREGORIAN',
            'METHOD:PUBLISH',
            'BEGIN:VEVENT',
            'UID:beseda-20260925-patrioti-trutnov@patriotitrutnov.cz',
            'DTSTAMP:20260917T180000Z',
            'DTSTART:20260925T130000Z',
            'DTEND:20260925T150000Z',
            'SUMMARY:Beseda s ob\u010dany - Patrioti Trutnov',
            'DESCRIPTION:Setk\u00e1n\u00ed s kandid\u00e1tem na sen\u00e1tora a p\u0159edstaven\u00ed volebn\u00edho programu Patrioti Trutnov (Kandid\u00e1tka \u010d. 1). Host\u00e9: Mgr. Bc. Kate\u0159ina Hurd\u00e1lkov\u00e1, JUDr. Jind\u0159ich Rajchl, Ing. Hynek Beran.',
            'LOCATION:Krakono\u0161ovo n\u00e1m\u011bst\u00ed, Trutnov',
            'STATUS:CONFIRMED',
            'END:VEVENT',
            'END:VCALENDAR'
        ].join('\r\n');

        const blob = new Blob([icsContent], { type: 'text/calendar;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'patrioti-trutnov-beseda-25-9-2026.ics';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    });
}

// ==========================================
// 6. FAQ Accordion Logic
// ==========================================
const faqQuestions = document.querySelectorAll('.faq-question');

faqQuestions.forEach(question => {
    question.addEventListener('click', () => {
        const item = question.closest('.faq-item');
        const isOpen = item.classList.contains('open');

        // Close other items
        document.querySelectorAll('.faq-item').forEach(otherItem => {
            if (otherItem !== item) {
                otherItem.classList.remove('open');
                const otherBtn = otherItem.querySelector('.faq-question');
                if (otherBtn) otherBtn.setAttribute('aria-expanded', 'false');
            }
        });

        // Toggle current item
        if (isOpen) {
            item.classList.remove('open');
            question.setAttribute('aria-expanded', 'false');
        } else {
            item.classList.add('open');
            question.setAttribute('aria-expanded', 'true');
        }
    });
});

// ==========================================
// 7. Form Submission (Lead Form + Volunteer Checkboxes)
// ==========================================
const leadForm = document.getElementById('leadForm');
const formStatus = document.getElementById('formStatus');

if (leadForm) {
    leadForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        
        const btn = leadForm.querySelector('button[type="submit"]');
        const originalText = btn.innerText;
        btn.innerText = 'Odesílám...';
        btn.disabled = true;

        const formData = new FormData(leadForm);
        const data = Object.fromEntries(formData.entries());

        // Collect volunteer checkboxes
        const selectedVolunteers = [];
        if (formData.get('support_banner')) selectedVolunteers.push('Plachta na plot/balkon zdarma');
        if (formData.get('support_leaflets')) selectedVolunteers.push('Roznos volebních novin/letáků');
        if (formData.get('support_newsletter')) selectedVolunteers.push('Odběr novinek');

        if (selectedVolunteers.length > 0) {
            const volunteerNote = `[ZÁJEM O ZAPOJENÍ: ${selectedVolunteers.join(', ')}]`;
            data.message = data.message ? `${data.message}\n\n${volunteerNote}` : volunteerNote;
        }

        try {
            const response = await fetch('api/leads', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });

            if (response.ok) {
                formStatus.innerText = 'Děkujeme! Vaše zpráva byla úspěšně odeslána. Brzy se vám ozveme.';
                formStatus.className = 'status-message success';
                leadForm.reset();
            } else {
                formStatus.innerText = 'Něco se nepovedlo. Zkuste to prosím znovu.';
                formStatus.className = 'status-message error';
            }
        } catch (err) {
            formStatus.innerText = 'Chyba při spojení se serverem.';
            formStatus.className = 'status-message error';
        } finally {
            btn.innerText = originalText;
            btn.disabled = false;
        }
    });
}

// ==========================================
// 8. Mobile Nav Toggle
// ==========================================
const navToggle = document.getElementById('navToggle');
const navMenu = document.getElementById('navMenu');

if (navToggle && navMenu) {
    navToggle.addEventListener('click', () => {
        navToggle.classList.toggle('active');
        navMenu.classList.toggle('active');
    });

    // Close menu when clicking a link
    const navLinks = navMenu.querySelectorAll('a');
    navLinks.forEach(link => {
        link.addEventListener('click', () => {
            navToggle.classList.remove('active');
            navMenu.classList.remove('active');
        });
    });

    // Close menu when clicking the logo or header volte badge
    const logoLink = document.querySelector('.logo');
    if (logoLink) {
        logoLink.addEventListener('click', () => {
            navToggle.classList.remove('active');
            navMenu.classList.remove('active');
        });
    }

    const volteBadgeLink = document.querySelector('.header-volte-badge');
    if (volteBadgeLink) {
        volteBadgeLink.addEventListener('click', () => {
            navToggle.classList.remove('active');
            navMenu.classList.remove('active');
        });
    }
}

