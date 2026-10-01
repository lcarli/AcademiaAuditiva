import { expect, test, type Page } from '@playwright/test';

const cultures = ['en-US', 'pt-BR', 'fr-CA'] as const;

test.beforeEach(async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(`pageerror: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console: ${message.text()}`);
  });
  page.on('response', response => {
    const url = response.url();
    if (response.status() >= 400 && !url.includes('/favicon')) {
      errors.push(`HTTP ${response.status()} ${url}`);
    }
  });
  (page as Page & { _aaErrors?: string[] })._aaErrors = errors;
});

test.afterEach(async ({ page }) => {
  const errors = (page as Page & { _aaErrors?: string[] })._aaErrors ?? [];
  expect([...new Set(errors)]).toEqual([]);
});

test('home, catalog and privacy load in every supported culture', async ({ page, baseURL }) => {
  for (const culture of cultures) {
    for (const path of ['/', '/Exercise', '/Home/Privacy']) {
      const response = await page.goto(`${baseURL}${path}?culture=${culture}&ui-culture=${culture}`, { waitUntil: 'networkidle' });
      expect(response?.status(), `${path} ${culture}`).toBe(200);
      await expect(page.locator('body')).toBeVisible();
    }
  }
});

test('health endpoints expose the running app version', async ({ request }) => {
  for (const path of ['/health/live', '/health/ready']) {
    const response = await request.get(path);
    expect(response.status(), path).toBe(200);
    const body = await response.json();
    expect(body.status).toBeTruthy();
    expect(body.version).toBeTruthy();
    if (process.env.E2E_EXPECT_VERSION) {
      expect(body.version).toBe(process.env.E2E_EXPECT_VERSION);
      expect(response.headers()['x-app-version']).toBe(process.env.E2E_EXPECT_VERSION);
    }
  }
});

test('register page works and bootstrapped admin can log in', async ({ page, baseURL }) => {
  const stamp = Date.now().toString(36);
  await page.goto(`${baseURL}/Identity/Account/Register`, { waitUntil: 'networkidle' });
  await page.fill('#Input_FirstName', 'E2E');
  await page.fill('#Input_LastName', 'User');
  await page.fill('#Input_Email', `e2e-${stamp}@example.test`);
  await page.fill('#Input_Password', `E2e!${stamp}Aa1`);
  await page.fill('#Input_ConfirmPassword', `E2e!${stamp}Aa1`);
  await Promise.all([
    page.waitForURL(/RegisterConfirmation/),
    page.click('#registerSubmit')
  ]);

  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await expect(page).not.toHaveURL(/\/Identity\/Account\/Login/);
});

test('one real-audio GuessNote round returns playable audio and validates', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessNote`, { waitUntil: 'networkidle' });

  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay') && response.status() === 200);
  const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
  await page.click('#Play');
  const play = await playResponse;
  const playJson = await play.json();
  expect(playJson.roundId).toMatch(/^[0-9a-f]{32}$/);
  expect(playJson.playToken).toMatch(/^[0-9a-f]{32}$/);

  const audio = await audioResponse;
  expect(audio.headers()['content-type']).toContain('audio/');
  expect((await audio.body()).length).toBeGreaterThan(1000);

  await page.locator('.aa-answer:visible').first().click();
  const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  const validate = await validateResponse;
  expect(validate.status()).toBe(200);
  const result = await validate.json();
  expect(result.success).toBe(true);
});

async function login(page: Page, baseURL: string, email: string, password: string) {
  expect(email, 'AA_EMAIL').toBeTruthy();
  expect(password, 'AA_PASSWORD').toBeTruthy();
  await page.goto(`${baseURL}/Identity/Account/Login`, { waitUntil: 'networkidle' });
  await page.fill('#Input_Email', email);
  await page.fill('#Input_Password', password);
  await Promise.all([
    page.waitForLoadState('networkidle'),
    page.click('#login-submit')
  ]);
}
