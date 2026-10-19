'use strict';

// No fetch handler or cache: authenticated pages, answers and audio always use the network.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
  let message = {};
  try {
    const data = event.data ? event.data.json() : {};
    if (data && typeof data === 'object') message = data;
  } catch (error) {
    console.warn('Invalid push payload:', error.name);
  }
  event.waitUntil(self.registration.showNotification(typeof message.title === 'string' ? message.title : 'Academia Auditiva', {
    body: typeof message.body === 'string' ? message.body : '',
    icon: '/img/pwa/icon-192.png',
    badge: '/img/pwa/icon-192.png',
    tag: typeof message.tag === 'string' ? message.tag : 'study-reminder',
    data: { url: '/Dashboard' }
  }));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  event.waitUntil((async () => {
    const target = new URL('/Dashboard', self.location.origin).href;
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of windows) {
      if (new URL(client.url).origin === self.location.origin) {
        await client.navigate(target);
        return client.focus();
      }
    }
    return self.clients.openWindow(target);
  })());
});
