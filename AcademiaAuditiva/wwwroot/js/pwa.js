(() => {
  'use strict';
  if (!window.isSecureContext || !('serviceWorker' in navigator)) return;
  window.aaServiceWorker = navigator.serviceWorker.register('/service-worker.js', { updateViaCache: 'none' });
  window.aaServiceWorker.catch(error => console.warn('PWA registration failed:', error.name));
})();
