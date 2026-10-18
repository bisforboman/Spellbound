// Turning notifications on and off. Plain buttons call these directly (not through the server), because browsers only
// ask for permission inside the tap itself. Works with the forms on the Account page (data-push attributes).
(() => {
    const status = text => document.querySelectorAll('[data-push-status]').forEach(e => { e.textContent = text; });
    const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    const key = base64 => {
        const raw = atob((base64 + '='.repeat((4 - base64.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/'));
        return Uint8Array.from(raw, c => c.charCodeAt(0));
    };
    const post = async (url, fields) => {
        const form = new FormData(document.getElementById('push-form'));
        Object.entries(fields).forEach(([name, value]) => form.set(name, value));
        const response = await fetch(url, { method: 'POST', body: form, credentials: 'same-origin' });
        if (!response.ok) throw new Error(await response.text());
    };

    async function turnOn() {
        if (!supported()) {
            status(/iPhone|iPad/.test(navigator.userAgent)
                ? 'On iPhone and iPad, add Spellbound to your home screen first (Share, then Add to Home Screen), and turn notifications on from there.'
                : "This browser can't show notifications.");
            return;
        }
        if (await Notification.requestPermission() !== 'granted') {
            status('Notifications are blocked for this site. Allow them in the browser\'s settings, then try again.');
            return;
        }
        try {
            const registration = await navigator.serviceWorker.register('/sw.js');
            await navigator.serviceWorker.ready;
            const serverKey = await (await fetch('/Account/Push/Key', { credentials: 'same-origin' })).text();
            const subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key(serverKey) });
            const json = subscription.toJSON();
            await post('/Account/Push', { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth });
            status('Notifications are on for this device.');
        } catch (error) {
            status('Notifications couldn\'t be turned on here: ' + error.message);
        }
    }

    async function turnOff() {
        const registration = supported() && await navigator.serviceWorker.getRegistration('/sw.js');
        const subscription = registration && await registration.pushManager.getSubscription();
        if (subscription) {
            await post('/Account/Push/Remove', { endpoint: subscription.endpoint });
            await subscription.unsubscribe();
        }
        status('Notifications are off for this device.');
    }

    async function show() {
        if (!document.querySelector('[data-push-status]')) return;
        if (!supported()) { status('Not available in this browser.'); return; }
        const registration = await navigator.serviceWorker.getRegistration('/sw.js');
        const subscription = registration && await registration.pushManager.getSubscription();
        status(subscription && Notification.permission === 'granted' ? 'Notifications are on for this device.' : 'Notifications are off for this device.');
    }

    document.addEventListener('click', event => {
        const button = event.target.closest('[data-push]');
        if (!button) return;
        event.preventDefault();
        (button.dataset.push === 'on' ? turnOn : turnOff)();
    });
    document.addEventListener('DOMContentLoaded', show);
    if (window.Blazor) Blazor.addEventListener?.('enhancedload', show);
})();
