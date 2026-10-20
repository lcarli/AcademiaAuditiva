(() => {
  'use strict';
  const panel = document.getElementById('study-reminders');
  if (!panel) return;
  const form = document.getElementById('study-reminder-form');
  const time = document.getElementById('study-reminder-time');
  const zone = document.getElementById('study-reminder-zone');
  const enable = document.getElementById('enable-study-reminders');
  const disable = document.getElementById('disable-study-reminders');
  const disableAll = document.getElementById('disable-all-study-reminders');
  const status = document.getElementById('study-reminder-status');
  const text = panel.dataset;
  let registration;
  let subscribed = false;
  let busy = false;

  function show(message, error = false) {
    status.textContent = message;
    status.classList.toggle('text-danger', error);
  }

  function buttons() {
    enable.disabled = busy || !registration || !text.publicKey || Notification.permission === 'denied';
    disable.disabled = busy || !registration || !subscribed;
    disableAll.disabled = busy;
  }

  async function request(action, data) {
    const response = await fetch(`/StudyReminders/${action}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': document.querySelector('meta[name="request-verification-token"]').content
      },
      body: JSON.stringify(data)
    });
    if (!response.ok) {
      // A sign-in redirect may return HTML; never mistake it for a saved subscription.
      const body = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
      throw new Error(body?.error || text.error);
    }
    if (response.redirected) throw new Error(text.error);
    return response;
  }

  function applicationServerKey() {
    const encoded = text.publicKey.replace(/-/g, '+').replace(/_/g, '/');
    return Uint8Array.from(atob(encoded + '='.repeat((4 - encoded.length % 4) % 4)), c => c.charCodeAt(0));
  }

  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (busy || !registration || !form.reportValidity()) return;
    try {
      new Intl.DateTimeFormat('en', { timeZone: zone.value });
    } catch {
      show(text.invalid, true);
      return;
    }
    // This call must happen in the tap handler, before any await, for iOS.
    const permission = Notification.requestPermission();
    busy = true;
    buttons();
    try {
      if (await permission !== 'granted') {
        show(text.denied, true);
        return;
      }
      let subscription = await registration.pushManager.getSubscription();
      if (!subscription) {
        subscription = await registration.pushManager.subscribe({
          userVisibleOnly: true, applicationServerKey: applicationServerKey()
        });
      }
      subscribed = true;
      const json = subscription.toJSON();
      const [hours, minutes] = time.value.split(':').map(Number);
      await request('Subscribe', {
        endpoint: subscription.endpoint,
        p256dh: json.keys.p256dh,
        auth: json.keys.auth,
        timeZoneId: zone.value,
        minuteOfDay: hours * 60 + minutes
      });
      show(text.enabled);
    } catch (error) {
      show(error.message || text.error, true);
    } finally {
      busy = false;
      buttons();
    }
  });

  disable.addEventListener('click', async () => {
    if (busy || !registration) return;
    busy = true;
    buttons();
    try {
      const subscription = await registration.pushManager.getSubscription();
      if (subscription) {
        // Remove on the server first: if that fails, keep the browser subscription so it can retry.
        await request('Unsubscribe', { endpoint: subscription.endpoint });
        if (!await subscription.unsubscribe()) throw new Error(text.error);
      }
      subscribed = false;
      show(text.disabled);
    } catch (error) {
      show(error.message || text.error, true);
    } finally {
      busy = false;
      buttons();
    }
  });

  disableAll.addEventListener('click', async () => {
    if (busy) return;
    busy = true;
    buttons();
    try {
      await request('DisableAll', {});
      const subscription = registration && await registration.pushManager.getSubscription();
      if (subscription && !await subscription.unsubscribe()) throw new Error(text.error);
      subscribed = false;
      show(text.disabled);
    } catch (error) {
      show(error.message || text.error, true);
    } finally {
      busy = false;
      buttons();
    }
  });

  async function initialize() {
    disableAll.disabled = false;
    if (!window.isSecureContext || !('serviceWorker' in navigator) || !('PushManager' in window)
        || !('Notification' in window)) {
      show(text.unsupported);
      return;
    }
    try {
      zone.value = Intl.DateTimeFormat().resolvedOptions().timeZone;
      await window.aaServiceWorker;
      registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();
      subscribed = !!subscription;
      if (subscription) {
        const response = await request('Status', { endpoint: subscription.endpoint });
        const settings = await response.json();
        if (settings.enabled) {
          time.value = `${String(Math.floor(settings.minuteOfDay / 60)).padStart(2, '0')}:${String(settings.minuteOfDay % 60).padStart(2, '0')}`;
          zone.value = settings.timeZoneId;
        }
        show(settings.enabled ? text.enabled : text.disabled);
      } else {
        show(text.disabled);
      }
      if (!text.publicKey) show(text.unavailable);
      else if (Notification.permission === 'denied') show(text.denied, true);
      buttons();
    } catch {
      show(text.error, true);
    }
  }
  initialize();
})();
