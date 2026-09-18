// ==========================================================================
// Patrioti Trutnov - Administration JavaScript Logic
// ==========================================================================

window.getApiUrl = function(endpoint) {
    var clean = endpoint.startsWith('/') ? endpoint.substring(1) : endpoint;
    var path = window.location.pathname;
    var base = '/';
    if (path.includes('/patriotitrutnov')) {
        base = '/patriotitrutnov/';
    }
    return base + clean;
};

document.addEventListener('DOMContentLoaded', () => {
    // Elements
    const loginView = document.getElementById('loginView');
    const dashboardView = document.getElementById('dashboardView');
    const adminNavActions = document.getElementById('adminNavActions');
    const adminUsernameDisplay = document.getElementById('adminUsernameDisplay');
    const loginForm = document.getElementById('loginForm');
    const loginError = document.getElementById('loginError');
    const loginBtn = document.getElementById('loginBtn');
    const adminLogoutBtn = document.getElementById('adminLogoutBtn');

    const emailListContainer = document.getElementById('emailListContainer');
    const addEmailForm = document.getElementById('addEmailForm');
    const newEmailInput = document.getElementById('newEmailInput');
    const saveSettingsBtn = document.getElementById('saveSettingsBtn');
    const settingsSuccess = document.getElementById('settingsSuccess');
    const settingsError = document.getElementById('settingsError');

    const leadsTableBody = document.getElementById('leadsTableBody');
    const leadsEmptyState = document.getElementById('leadsEmptyState');
    const refreshLeadsBtn = document.getElementById('refreshLeadsBtn');
    const leadsCountBadge = document.getElementById('leadsCountBadge');

    let currentEmails = [];

    // Tab Navigation
    const tabBtns = document.querySelectorAll('.admin-tab-btn');
    tabBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            tabBtns.forEach(b => b.classList.remove('active'));
            document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));

            btn.classList.add('active');
            const targetId = btn.getAttribute('data-tab');
            const targetPane = document.getElementById(targetId);
            if (targetPane) targetPane.classList.add('active');

            if (targetId === 'leadsTab') {
                loadLeads();
            }
        });
    });

    // Check existing session
    checkSession();

    // Login Form Submit
    if (loginForm) {
        loginForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const username = document.getElementById('adminUser').value.trim();
            const password = document.getElementById('adminPass').value;

            if (loginError) loginError.style.display = 'none';
            if (loginBtn) {
                loginBtn.disabled = true;
                loginBtn.textContent = 'Ověřuji...';
            }

            try {
                const res = await fetch(getApiUrl('api/admin/login'), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ username, password })
                });

                const data = await res.json();
                if (res.ok && data.success) {
                    localStorage.setItem('patrioti_admin_token', data.token);
                    localStorage.setItem('patrioti_admin_user', data.username);
                    showDashboard(data.username);
                } else {
                    if (loginError) {
                        loginError.textContent = data.message || 'Neplatné přihlašovací údaje.';
                        loginError.style.display = 'block';
                    }
                }
            } catch (err) {
                if (loginError) {
                    loginError.textContent = 'Chyba připojení k serveru.';
                    loginError.style.display = 'block';
                }
            } finally {
                if (loginBtn) {
                    loginBtn.disabled = false;
                    loginBtn.textContent = 'Přihlásit se';
                }
            }
        });
    }

    // Logout
    if (adminLogoutBtn) {
        adminLogoutBtn.addEventListener('click', () => {
            localStorage.removeItem('patrioti_admin_token');
            localStorage.removeItem('patrioti_admin_user');
            localStorage.removeItem('patrioti_token');
            localStorage.removeItem('patrioti_user');
            localStorage.removeItem('patrioti_role');
            window.location.href = 'login.html';
        });
    }

    // Session verification
    async function checkSession() {
        const token = localStorage.getItem('patrioti_admin_token') || localStorage.getItem('patrioti_token');
        const user = localStorage.getItem('patrioti_admin_user') || localStorage.getItem('patrioti_user');

        if (!token) {
            window.location.href = 'login.html';
            return;
        }

        try {
            const res = await fetch(getApiUrl('api/auth/check'), {
                headers: { 'Authorization': 'Bearer ' + token }
            });

            if (res.ok) {
                const data = await res.json();
                if (data.valid) {
                    if (data.role === 'admin') {
                        showDashboard(data.username || user || 'Admin');
                        return;
                    } else if (data.role === 'client') {
                        window.location.href = 'client.html';
                        return;
                    }
                }
            }
        } catch (e) { }

        // Invalid or expired token
        localStorage.removeItem('patrioti_admin_token');
        localStorage.removeItem('patrioti_admin_user');
        localStorage.removeItem('patrioti_token');
        localStorage.removeItem('patrioti_user');
        localStorage.removeItem('patrioti_role');
        window.location.href = 'login.html';
    }

    function showLogin() {
        window.location.href = 'login.html';
    }

    function showDashboard(username) {
        if (loginView) loginView.style.display = 'none';
        if (dashboardView) dashboardView.style.display = 'block';
        if (adminNavActions) adminNavActions.style.display = 'flex';
        if (adminUsernameDisplay) adminUsernameDisplay.textContent = `Přihlášen: ${username}`;

        loadSettings();
    }

    // Settings logic
    async function loadSettings() {
        const token = localStorage.getItem('patrioti_admin_token') || localStorage.getItem('patrioti_token');
        if (!token) return;

        try {
            const res = await fetch(getApiUrl('api/admin/settings'), {
                headers: { 'Authorization': 'Bearer ' + token }
            });

            if (res.ok) {
                const data = await res.json();
                currentEmails = data.notification_emails || ['info@patriotitrutnov.cz'];
                renderEmailList();
            }
        } catch (e) {
            console.error('Chyba při načítání nastavení:', e);
        }
    }

    function renderEmailList() {
        if (!emailListContainer) return;
        emailListContainer.innerHTML = '';

        if (currentEmails.length === 0) {
            emailListContainer.innerHTML = `
                <div style="padding: 16px; background: rgba(239, 68, 68, 0.1); border: 1px dashed rgba(239, 68, 68, 0.4); border-radius: 8px; color: #fca5a5; font-size: 0.9rem;">
                    ⚠️ Není zadána žádná adresa. Zprávy budou odesílány na výchozí adresu <strong>info@patriotitrutnov.cz</strong>.
                </div>`;
            return;
        }

        currentEmails.forEach((email, index) => {
            const row = document.createElement('div');
            row.className = 'email-item';
            row.innerHTML = `
                <div class="email-item-address">
                    <span>✉️</span>
                    <span>${escapeHtml(email)}</span>
                    <span class="email-item-badge">Příjemce</span>
                </div>
                <button type="button" class="btn-delete-email" data-index="${index}">🗑️ Odebrat</button>
            `;
            emailListContainer.appendChild(row);
        });

        // Attach delete events
        emailListContainer.querySelectorAll('.btn-delete-email').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.getAttribute('data-index'), 10);
                if (!isNaN(idx) && idx >= 0 && idx < currentEmails.length) {
                    currentEmails.splice(idx, 1);
                    renderEmailList();
                }
            });
        });
    }

    // Add email
    if (addEmailForm) {
        addEmailForm.addEventListener('submit', (e) => {
            e.preventDefault();
            const email = newEmailInput.value.trim().toLowerCase();
            if (!email || !email.includes('@') || !email.includes('.')) {
                alert('Zadejte platnou e-mailovou adresu.');
                return;
            }

            if (currentEmails.includes(email)) {
                alert('Tato e-mailová adresa již v seznamu existuje.');
                return;
            }

            currentEmails.push(email);
            renderEmailList();
            newEmailInput.value = '';
            newEmailInput.focus();
        });
    }

    // Save settings
    if (saveSettingsBtn) {
        saveSettingsBtn.addEventListener('click', async () => {
            const token = localStorage.getItem('patrioti_admin_token') || localStorage.getItem('patrioti_token');
            if (!token) return;

            if (settingsSuccess) settingsSuccess.style.display = 'none';
            if (settingsError) settingsError.style.display = 'none';

            saveSettingsBtn.disabled = true;
            saveSettingsBtn.textContent = '⏳ Ukládám...';

            try {
                const res = await fetch(getApiUrl('api/admin/settings'), {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': 'Bearer ' + token
                    },
                    body: JSON.stringify({ notificationEmails: currentEmails })
                });

                const data = await res.json();
                if (res.ok && data.success) {
                    currentEmails = data.notification_emails;
                    renderEmailList();
                    if (settingsSuccess) {
                        settingsSuccess.textContent = '✅ Nastavení příjemců e-mailů bylo úspěšně uloženo.';
                        settingsSuccess.style.display = 'block';
                        setTimeout(() => {
                            if (settingsSuccess) settingsSuccess.style.display = 'none';
                        }, 5000);
                    }
                } else {
                    if (settingsError) {
                        settingsError.textContent = data.message || 'Nepodařilo se uložit nastavení.';
                        settingsError.style.display = 'block';
                    }
                }
            } catch (err) {
                if (settingsError) {
                    settingsError.textContent = 'Chyba spojení se serverem při ukládání.';
                    settingsError.style.display = 'block';
                }
            } finally {
                saveSettingsBtn.disabled = false;
                saveSettingsBtn.textContent = '💾 Uložit nastavení';
            }
        });
    }

    // Leads logic
    async function loadLeads() {
        const token = localStorage.getItem('patrioti_admin_token') || localStorage.getItem('patrioti_token');
        if (!token) return;

        if (refreshLeadsBtn) refreshLeadsBtn.textContent = '⏳ Načítám...';

        try {
            const res = await fetch(getApiUrl('api/admin/leads'), {
                headers: { 'Authorization': 'Bearer ' + token }
            });

            if (res.ok) {
                const list = await res.json();
                renderLeads(list);
            }
        } catch (e) {
            console.error('Chyba při načítání zpráv:', e);
        } finally {
            if (refreshLeadsBtn) refreshLeadsBtn.textContent = '🔄 Obnovit zprávy';
        }
    }

    if (refreshLeadsBtn) {
        refreshLeadsBtn.addEventListener('click', loadLeads);
    }

    function renderLeads(leads) {
        if (!leadsTableBody) return;
        leadsTableBody.innerHTML = '';

        if (leadsCountBadge) {
            leadsCountBadge.textContent = leads ? leads.length : 0;
            leadsCountBadge.style.display = 'inline-block';
        }

        if (!leads || leads.length === 0) {
            if (leadsEmptyState) leadsEmptyState.style.display = 'block';
            return;
        }

        if (leadsEmptyState) leadsEmptyState.style.display = 'none';

        leads.forEach(lead => {
            const tr = document.createElement('tr');
            
            let formattedDate = '-';
            if (lead.createdAt) {
                try {
                    const d = new Date(lead.createdAt);
                    formattedDate = d.toLocaleString('cs-CZ', {
                        day: '2-digit', month: '2-digit', year: 'numeric',
                        hour: '2-digit', minute: '2-digit'
                    });
                } catch { }
            }

            tr.innerHTML = `
                <td class="lead-date">${formattedDate}</td>
                <td class="lead-name">${escapeHtml(lead.fullName || '')}</td>
                <td class="lead-email"><a href="mailto:${escapeHtml(lead.email || '')}?subject=Re:%20Patrioti%20Trutnov">${escapeHtml(lead.email || '')}</a></td>
                <td>${lead.phone ? `<a href="tel:${escapeHtml(lead.phone)}" style="color: #cbd5e1; text-decoration: none;">${escapeHtml(lead.phone)}</a>` : '<span style="color:#64748b;">-</span>'}</td>
                <td class="lead-topic">${escapeHtml(lead.topic || '-')}</td>
                <td class="lead-msg">${escapeHtml(lead.message || '-')}</td>
                <td>
                    <a href="mailto:${escapeHtml(lead.email || '')}?subject=Re:%20Patrioti%20Trutnov" class="btn-admin-link" style="padding: 4px 8px; font-size: 0.75rem;">
                        ✉️ Odpovědět
                    </a>
                </td>
            `;
            leadsTableBody.appendChild(tr);
        });
    }

    function escapeHtml(str) {
        if (!str) return '';
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }
});
