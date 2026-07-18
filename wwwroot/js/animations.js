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

// Form Submission
const leadForm = document.getElementById('leadForm');
const formStatus = document.getElementById('formStatus');

if (leadForm) {
    leadForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        
        const btn = leadForm.querySelector('button');
        const originalText = btn.innerText;
        btn.innerText = 'Sending...';
        btn.disabled = true;

        const formData = new FormData(leadForm);
        const data = Object.fromEntries(formData.entries());

        try {
            const response = await fetch('/api/leads', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });

            if (response.ok) {
                formStatus.innerText = 'Thank you! Your message has been sent.';
                formStatus.className = 'status-message success';
                leadForm.reset();
            } else {
                formStatus.innerText = 'Something went wrong. Please try again.';
                formStatus.className = 'status-message error';
            }
        } catch (err) {
            formStatus.innerText = 'Error connecting to server.';
            formStatus.className = 'status-message error';
        } finally {
            btn.innerText = originalText;
            btn.disabled = false;
        }
    });
}

// Theme Toggle Logic
const themeBtns = document.querySelectorAll('.theme-btn');
const headerLogo = document.querySelector('.header-logo-img');
const footerLogo = document.querySelector('.footer-logo-img');
const favicon = document.querySelector('link[rel="icon"]');

const setAppTheme = (themeName) => {
    // Remove all theme classes
    document.documentElement.classList.remove('light-theme', 'red-theme', 'green-theme');
    
    // Add selected theme class
    if (themeName !== 'dark') {
        document.documentElement.classList.add(`${themeName}-theme`);
    }
    
    // Update logo source based on theme
    const isLight = themeName === 'light';
    const logoSrc = isLight ? 'img/PatriotiLogo.png' : 'img/PatriotiLogoBlack.png';
    
    if (headerLogo) headerLogo.src = logoSrc;
    if (footerLogo) footerLogo.src = logoSrc;
    if (favicon) favicon.href = logoSrc;
    
    // Update active button state
    themeBtns.forEach(btn => {
        if (btn.getAttribute('data-theme') === themeName) {
            btn.classList.add('active');
        } else {
            btn.classList.remove('active');
        }
    });
};

// Initialize Theme on load
const savedTheme = localStorage.getItem('theme') || 'dark';
setAppTheme(savedTheme);

themeBtns.forEach(btn => {
    btn.addEventListener('click', () => {
        const selectedTheme = btn.getAttribute('data-theme');
        localStorage.setItem('theme', selectedTheme);
        setAppTheme(selectedTheme);
    });
});

// Mobile Nav Toggle
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
}
