// Spellbound's service worker: only shows notifications and opens the game when one is tapped. It has no fetch
// handler on purpose, so it never sits between the page and its live connection.
self.addEventListener('push', event => {
    const message = event.data ? event.data.json() : {};
    event.waitUntil(self.registration.showNotification(message.title || 'Spellbound', {
        body: message.body || '',
        icon: '/icon-192.png',
        badge: '/icon-192.png',
        data: { url: message.url || '/' },
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = new URL(event.notification.data.url, self.location.origin).href;
    event.waitUntil(clients.matchAll({ type: 'window', includeUncontrolled: true }).then(windows => {
        const open = windows.find(w => w.url === url);
        return open ? open.focus() : clients.openWindow(url);
    }));
});
