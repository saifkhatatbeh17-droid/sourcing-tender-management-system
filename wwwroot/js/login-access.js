// Login page animations and the sign-in method buttons.
(() => {
    const identity = document.querySelector('.login-identity');
    const form = document.getElementById('password-login');
    const message = document.getElementById('access-message');
    if (!identity || !form) return;

    const emailInput = document.getElementById('Email');
    const passwordInput = document.getElementById('Password');
    const emailField = document.getElementById('field-email');
    const emailHint = document.getElementById('email-live-hint');
    const passwordHint = document.getElementById('password-live-hint');
    const typingRing = document.getElementById('typing-ring');
    const stateLabel = document.getElementById('logo-state-label');
    const energyLabel = document.getElementById('logo-energy-label');
    const panelReactionText = document.getElementById('panel-reaction-text');
    const core = document.getElementById('interactive-logo-core');
    const dots = Array.from(document.querySelectorAll('#interaction-dots span'));
    const pillars = Array.from(document.querySelectorAll('.pillar-pill'));
    const subcopy = document.getElementById('identity-subcopy');

    const messages = {
        idle: 'Ready to sign in',
        pending: 'Checking your sign-in request…',
        verified: 'Verified. Opening your workspace…',
        failed: 'Please review your details and try again'
    };

    const RING_CIRCUMFERENCE = 578;

    function updateProgressRing() {
        const emailVal = (emailInput?.value || '').trim();
        const passVal = passwordInput?.value || '';
        const emailValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailVal);

        if (emailField) {
            emailField.classList.toggle('is-valid-email', emailValid);
        }
        if (emailHint) {
            emailHint.textContent = emailValid ? 'Verified format ✓' : (emailVal.length > 0 ? 'Typing email…' : 'Workspace ID');
        }
        if (passwordHint) {
            passwordHint.textContent = passVal.length > 0 ? `${passVal.length} chars · Shielded` : 'Encrypted';
        }

        // Calculate combined completion score (0 to 1)
        const emailScore = Math.min(emailVal.length / 16, 0.5) + (emailValid ? 0.1 : 0);
        const passScore = Math.min(passVal.length / 10, 0.4);
        const totalRatio = Math.min(1, emailScore + passScore);

        if (typingRing) {
            const offset = RING_CIRCUMFERENCE - (totalRatio * RING_CIRCUMFERENCE);
            typingRing.style.strokeDashoffset = String(Math.round(offset));
        }

        if (dots.length) {
            const litCount = Math.round(totalRatio * dots.length);
            dots.forEach((dot, idx) => dot.classList.toggle('is-lit', idx < litCount));
        }
    }

    function setLabels(topText, bottomText, badgeText) {
        if (stateLabel && topText) stateLabel.textContent = topText;
        if (energyLabel && bottomText) energyLabel.textContent = bottomText;
        if (panelReactionText && badgeText) panelReactionText.textContent = badgeText;
    }

    function state(value) {
        identity.dataset.access = value;
        if (message) message.textContent = messages[value] || messages.idle;

        if (value === 'pending') {
            setLabels('Authenticating Session…', 'Verifying credentials with Tiryaq', 'Verifying…');
        } else if (value === 'verified') {
            setLabels('Access Granted ✓', 'Redirecting to your workspace', 'Verified ✓');
        } else if (value === 'failed') {
            setLabels('Attention Needed', 'Check your credentials and retry', 'Check Details');
        } else {
            setLabels('Interactive Brand Core', 'Move cursor or type to interact', 'Tiryaq Active');
        }
    }

    document.addEventListener('login-access', event => state(event.detail));

    // Input focus & typing reactions
    form.querySelectorAll('input:not([type="hidden"])').forEach(input => {
        input.addEventListener('focus', () => {
            const fieldName = input.id.toLowerCase();
            identity.dataset.focus = fieldName;
            if (fieldName === 'email') {
                setLabels('Tracking Workspace ID', 'Logo optical sensors following input', 'Reading Email…');
            } else if (fieldName === 'password') {
                const isShown = identity.dataset.passwordShown === 'true';
                setLabels(
                    isShown ? 'Password Visible (Peeking)' : 'Privacy Shield Active',
                    isShown ? 'Optical visor open while password shown' : 'Tiryaq core locked for password privacy',
                    isShown ? 'Visor Open' : 'Privacy Shield'
                );
            }
        });

        input.addEventListener('blur', () => {
            delete identity.dataset.focus;
            identity.style.setProperty('--eye-x', '0px');
            identity.style.setProperty('--eye-y', '0px');
            if (identity.dataset.access === 'idle' || !identity.dataset.access) {
                setLabels('Interactive Brand Core', 'Move cursor or type to interact', 'Tiryaq Active');
            }
        });

        input.addEventListener('input', () => {
            updateProgressRing();
            if (input.id.toLowerCase() === 'email') {
                // Move optical eyes across the capsule as user types email characters
                const len = input.value.length;
                const lookX = Math.max(-4.5, Math.min(4.5, (len % 18 - 9) * 0.45));
                identity.style.setProperty('--eye-x', `${lookX.toFixed(2)}px`);
                identity.style.setProperty('--eye-y', '1px');
            }
        });
    });

    // Biometric buttons hover interaction
    document.querySelectorAll('[data-biometric-hover]').forEach(btn => {
        const mode = btn.getAttribute('data-biometric-hover');
        btn.addEventListener('mouseenter', () => {
            identity.dataset.biometric = mode;
            if (mode === 'face') {
                setLabels('Face ID Biometric Ready', 'Camera similarity viewfinder active', 'Face ID Mode');
            } else if (mode === 'passkey') {
                setLabels('Passkey / Fingerprint Ready', 'WebAuthn hardware security key ready', 'Passkey Mode');
            }
        });
        btn.addEventListener('mouseleave', () => {
            delete identity.dataset.biometric;
            if (!identity.dataset.focus) {
                setLabels('Interactive Brand Core', 'Move cursor or type to interact', 'Tiryaq Active');
            }
        });
    });

    // 3D Magnetic Tilt & Cursor Tracking on the Identity Showcase
    identity.addEventListener('pointermove', event => {
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        const rect = identity.getBoundingClientRect();
        const x = ((event.clientX - rect.left) / rect.width) * 100;
        const y = ((event.clientY - rect.top) / rect.height) * 100;
        identity.style.setProperty('--cursor-x', `${x.toFixed(1)}%`);
        identity.style.setProperty('--cursor-y', `${y.toFixed(1)}%`);

        const normX = (event.clientX - rect.left) / rect.width - 0.5;
        const normY = (event.clientY - rect.top) / rect.height - 0.5;
        const tiltRx = (-normY * 16).toFixed(2);
        const tiltRy = (normX * 16).toFixed(2);
        identity.style.setProperty('--tilt-rx', `${tiltRx}deg`);
        identity.style.setProperty('--tilt-ry', `${tiltRy}deg`);

        if (!identity.dataset.focus) {
            const eyeX = (normX * 7).toFixed(2);
            const eyeY = (normY * 5).toFixed(2);
            identity.style.setProperty('--eye-x', `${eyeX}px`);
            identity.style.setProperty('--eye-y', `${eyeY}px`);
        }
    });

    identity.addEventListener('pointerleave', () => {
        identity.style.setProperty('--tilt-rx', '0deg');
        identity.style.setProperty('--tilt-ry', '0deg');
        if (!identity.dataset.focus) {
            identity.style.setProperty('--eye-x', '0px');
            identity.style.setProperty('--eye-y', '0px');
        }
    });

    // Interactive Pillar switcher & Logo click pulse
    function pulseCore() {
        if (!core) return;
        core.classList.remove('is-pulsing');
        void core.offsetWidth;
        core.classList.add('is-pulsing');
    }

    function activatePillar(index) {
        if (!pillars.length) { pulseCore(); return; }
        const target = pillars[index % pillars.length];
        pillars.forEach(p => p.classList.toggle('is-active', p === target));
        identity.dataset.mode = target.dataset.pillar || 'sourcing';
        if (subcopy && target.dataset.copy) {
            subcopy.textContent = target.dataset.copy;
        }
        if (core) {
            core.classList.remove('is-pulsing');
            void core.offsetWidth;
            core.classList.add('is-pulsing');
        }
    }

    pillars.forEach((pill, index) => {
        pill.addEventListener('click', () => activatePillar(index));
    });

    if (core) {
        let currentPillar = 0;
        core.addEventListener('click', () => {
            currentPillar = (currentPillar + 1) % Math.max(1, pillars.length);
            activatePillar(currentPillar);
        });
    }

    window.addEventListener('pageshow', () => {
        updateProgressRing();
        state(document.querySelector('.validation-summary-errors,.field-validation-error') ? 'failed' : 'idle');
    });

    form.addEventListener('submit', event => {
        if (event.defaultPrevented || event.submitter?.id === 'face-login-button') return;
        if (window.jQuery && !window.jQuery(form).valid()) return;
        state('pending');
    });
})();
