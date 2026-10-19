import { createECDH } from 'node:crypto';
import { runInNewContext } from 'node:vm';
import { expect, test, type Page } from '@playwright/test';

const settingsPath = '/Identity/Account/Manage/Notifications';

test('the installed app manifest and service worker load without caching private pages', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/manifest.webmanifest');
  const manifest = await (await page.request.get('/manifest.webmanifest')).json();
  expect(manifest.display).toBe('standalone');
  expect(manifest.start_url).toBe('/Dashboard?source=pwa');
  const registration = await page.evaluate(async () => {
    const ready = await navigator.serviceWorker.ready;
    return { scope: ready.scope, script: ready.active?.scriptURL };
  });
  expect(registration.script).toMatch(/\/service-worker\.js$/);
  expect(registration.scope).toBe(new URL('/', page.url()).href);
  expect(await page.evaluate(() => caches.keys())).toEqual([]);
});

test('daily reminder settings save local time, restore it and opt out', async ({ page, baseURL }) => {
  await login(page, baseURL!);
  await mockPush(page);
  let saved: { endpoint: string; minuteOfDay: number; timeZoneId: string } | null = null;
  await page.route('**/StudyReminders/*', async route => {
    expect(route.request().headers()['requestverificationtoken']).toBeTruthy();
    const action = new URL(route.request().url()).pathname.split('/').at(-1);
    if (action === 'Subscribe') saved = route.request().postDataJSON();
    if (action === 'Unsubscribe' || action === 'DisableAll') saved = null;
    await route.fulfill({
      json: action === 'Status'
        ? { enabled: !!saved, minuteOfDay: saved?.minuteOfDay, timeZoneId: saved?.timeZoneId }
        : {}
    });
  });
  await page.goto(`${settingsPath}?culture=en-US&ui-culture=en-US`);
  await expect(page.locator('#enable-study-reminders')).toBeEnabled();
  await page.locator('#study-reminder-time').fill('20:15');
  await page.locator('#study-reminder-zone').fill('America/Toronto');
  await page.locator('#enable-study-reminders').click();
  await expect(page.locator('#study-reminder-status')).toHaveText('Daily reminder saved for this device.');
  expect(saved).toMatchObject({ minuteOfDay: 1215, timeZoneId: 'America/Toronto' });
  expect(await page.evaluate(() => sessionStorage.getItem('mock-permission-on-tap'))).toBe('true');
  await page.reload();
  await expect(page.locator('#study-reminder-time')).toHaveValue('20:15');
  await expect(page.locator('#study-reminder-zone')).toHaveValue('America/Toronto');
  await page.locator('#disable-study-reminders').click();
  await expect(page.locator('#study-reminder-status')).toHaveText('Daily reminders are off for this device.');
  expect(saved).toBeNull();
});

test('denied notification permission does not save a subscription', async ({ page, baseURL }) => {
  await login(page, baseURL!);
  await mockPush(page, 'denied');
  await page.goto(`${settingsPath}?culture=en-US&ui-culture=en-US`);
  await expect(page.locator('#study-reminder-status')).toContainText('Notifications are blocked');
  await expect(page.locator('#enable-study-reminders')).toBeDisabled();
  await expect(page.locator('#disable-all-study-reminders')).toBeEnabled();
});

test('a failed settings save is visible and retryable', async ({ page, baseURL }) => {
  await login(page, baseURL!);
  await mockPush(page);
  await page.route('**/StudyReminders/Subscribe', route => route.fulfill({
    status: 503, json: { error: 'Could not save this reminder.' }
  }));
  await page.goto(`${settingsPath}?culture=en-US&ui-culture=en-US`);
  await expect(page.locator('#enable-study-reminders')).toBeEnabled();
  await page.locator('#enable-study-reminders').click();
  await expect(page.locator('#study-reminder-status')).toHaveText('Could not save this reminder.');
  await expect(page.locator('#enable-study-reminders')).toBeEnabled();
});

test('unsupported browsers explain installation and can still turn off all devices', async ({ page, baseURL }) => {
  await login(page, baseURL!);
  await mockPush(page);
  await page.addInitScript(() => { Reflect.deleteProperty(window, 'PushManager'); });
  let disabledAll = false;
  await page.route('**/StudyReminders/DisableAll', async route => {
    disabledAll = true;
    await route.fulfill({ json: {} });
  });
  await page.goto(`${settingsPath}?culture=en-US&ui-culture=en-US`);
  await expect(page.locator('#study-reminder-status')).toContainText('Push is not available');
  await expect(page.locator('#study-reminders')).toContainText('Add to Home Screen');
  await page.locator('#disable-all-study-reminders').click();
  await expect(page.locator('#study-reminder-status')).toHaveText('Daily reminders are off for this device.');
  expect(disabledAll).toBe(true);
});

test('push events always show a notification and clicks only navigate to the dashboard', async ({ request }) => {
  const script = await (await request.get('/service-worker.js')).text();
  const listeners: Record<string, (event: unknown) => void> = {};
  const shown: { title: string; options: { body: string; data: { url: string } } }[] = [];
  const navigated: string[] = [];
  let focused = false;
  let closed = false;
  let pending: Promise<unknown> = Promise.resolve();
  runInNewContext(script, {
    URL,
    console: { warn: () => {} },
    self: {
      location: { origin: 'https://academiaauditiva.test' },
      addEventListener: (type: string, listener: (event: unknown) => void) => { listeners[type] = listener; },
      registration: {
        showNotification: async (title: string, options: { body: string; data: { url: string } }) => { shown.push({ title, options }); }
      },
      clients: {
        matchAll: async () => [{
          url: 'https://academiaauditiva.test/Exercise',
          navigate: async (url: string) => { navigated.push(url); },
          focus: async () => { focused = true; }
        }]
      }
    }
  });
  for (const payload of [{ title: 'Study', body: 'Practice today', url: 'https://evil.test' }, null]) {
    listeners.push({ data: { json: () => payload }, waitUntil: (work: Promise<unknown>) => { pending = work; } });
    await pending;
  }
  expect(shown.map(message => message.title)).toEqual(['Study', 'Academia Auditiva']);
  expect(shown[0].options.data.url).toBe('/Dashboard');
  listeners.notificationclick({
    notification: { close: () => { closed = true; } },
    waitUntil: (work: Promise<unknown>) => { pending = work; }
  });
  await pending;
  expect(navigated).toEqual(['https://academiaauditiva.test/Dashboard']);
  expect(focused).toBe(true);
  expect(closed).toBe(true);
});

async function login(page: Page, baseURL: string) {
  await page.goto(`${baseURL}/Identity/Account/Login?culture=en-US&ui-culture=en-US`);
  await page.locator('#Input_Email').fill(process.env.AA_EMAIL!);
  await page.locator('#Input_Password').fill(process.env.AA_PASSWORD!);
  await page.locator('#login-submit').click();
  await expect(page).not.toHaveURL(/\/Identity\/Account\/Login/);
}

async function mockPush(page: Page, permission: NotificationPermission = 'default') {
  const key = createECDH('prime256v1');
  key.generateKeys();
  const publicKey = key.getPublicKey().toString('base64url');
  await page.route(`**${settingsPath}*`, async route => {
    const response = await route.fetch();
    const html = (await response.text()).replace(/data-public-key="[^"]*"/, `data-public-key="${publicKey}"`);
    await route.fulfill({ response, body: html });
  });
  await page.addInitScript(({ permission, publicKey }) => {
    let subscribed = sessionStorage.getItem('mock-subscription') === 'true';
    const subscription = {
      endpoint: 'https://web.push.apple.com/browser-test',
      toJSON: () => ({ keys: { p256dh: publicKey, auth: 'AAAAAAAAAAAAAAAAAAAAAA' } }),
      unsubscribe: async () => {
        subscribed = false;
        sessionStorage.removeItem('mock-subscription');
        return true;
      }
    };
    const registration = {
      pushManager: {
        getSubscription: async () => subscribed ? subscription : null,
        subscribe: async () => {
          subscribed = true;
          sessionStorage.setItem('mock-subscription', 'true');
          return subscription;
        }
      }
    };
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: { register: async () => registration, ready: Promise.resolve(registration) }
    });
    Object.defineProperty(window, 'PushManager', { configurable: true, value: class {} });
    Object.defineProperty(window, 'Notification', {
      configurable: true,
      value: class {
        static permission = permission;
        static requestPermission() {
          sessionStorage.setItem('mock-permission-on-tap', String(navigator.userActivation.isActive));
          return Promise.resolve('granted');
        }
      }
    });
  }, { permission, publicKey });
}
