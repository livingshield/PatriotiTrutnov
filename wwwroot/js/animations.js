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
