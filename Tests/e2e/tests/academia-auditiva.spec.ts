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
  await closeTourIfStarted(page);

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

test('the chosen instrument plays the round and is remembered', async ({ page, baseURL, context }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessNote`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  const filters = page.locator('#filtersModal');
  const violin = filters.locator('[data-instrument="Violin"]');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(filters.locator('[data-instrument="Piano"]')).toHaveAttribute('aria-pressed', 'true');
  await violin.click();
  await expect(violin).toHaveAttribute('aria-pressed', 'true');
  await expect(filters.locator('[data-instrument="Piano"]')).toHaveAttribute('aria-pressed', 'false');
  await expect(filters.locator('#rangeStart')).toHaveAttribute('min', '4');
  await expect(filters.locator('#rangeEnd')).toHaveAttribute('max', '6');
  expect((await context.cookies()).find(cookie => cookie.name === 'instrument')?.value).toBe('Violin');
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
  await page.click('#Play');
  const audio = await audioResponse;
  expect(audio.headers()['content-type']).toContain('audio/');
  expect((await audio.body()).length).toBeGreaterThan(1000);

  // The page is rendered with the instrument the cookie remembers.
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(violin).toHaveAttribute('aria-pressed', 'true');
  await expect(filters.locator('#rangeStart')).toHaveAttribute('min', '4');
});

test('free practice shows the answer and checks it without scoring', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessNote`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  const scoredCorrect = await page.locator('#correctCount').textContent();
  const banner = page.locator('#aaFreeNote');
  await expect(banner).toBeHidden();
  await page.locator('#aaFreePractice').check();
  await expect(banner).toBeVisible();
  await expect(page).toHaveURL(/[?&]practice=free(&|$)/);

  const reveal = page.locator('[data-aa-reveal]');
  await expect(reveal).toBeHidden();
  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Play');
  const play = await playResponse;
  expect(JSON.parse(play.request().postData() ?? '{}').free).toBe(true);
  expect((await play.json()).roundId).toMatch(/^[0-9a-f]{32}$/);

  await expect(reveal).toBeVisible();
  const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
  await reveal.click();
  const shown = await (await revealResponse).json();
  expect(shown.success).toBe(true);
  expect(shown.answer).toMatch(/^[A-G][#b]?\d$/);
  const dialog = page.locator('.swal2-popup');
  await expect(dialog.locator('.swal2-title')).toHaveText('Answer');
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();

  await page.locator(`.aa-answer:visible[value="${pitchClass(shown.answer)}"]`).click();
  const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  const result = await (await validateResponse).json();
  expect(result).toMatchObject({ success: true, free: true, isCorrect: true, answer: shown.answer });
  await expect(dialog.locator('.aa-free-footer')).toBeVisible();
  await page.click('.swal2-confirm');

  await expect(page.locator('#aaFreeCorrect')).toHaveText('1');
  await expect(page.locator('#correctCount')).toHaveText(scoredCorrect ?? '0');
  await expect(reveal).toBeHidden();
});

test('explore plays the chosen chord and shows its notes', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Explore`, { waitUntil: 'networkidle' });

  const chordResponse = page.waitForResponse(response =>
    response.url().includes('/Explore/Play') && (response.request().postData() ?? '').includes('"kind":"chord"'));
  await page.click('#explore-tab-chord');
  const chord = await chordResponse;
  expect(chord.status()).toBe(200);
  const sound = await chord.json();
  expect(sound.token).toMatch(/^[0-9a-f]{32}$/);
  expect(sound).toMatchObject({ root: 'C', notes: ['C4', 'E4', 'G4'], simultaneous: true });

  await expect(page.locator('#exploreName')).toHaveText('C Major');
  await expect(page.locator('#exploreNotes li')).toHaveText(['C4', 'E4', 'G4']);
  await expect(page.locator('#exploreStaff svg')).toBeVisible();

  // The chord's token is reused, so Play goes straight to the audio.
  const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
  await page.click('#Play');
  const audio = await audioResponse;
  expect(audio.url()).toContain(sound.token);
  expect(audio.headers()['content-type']).toContain('audio/');
  expect((await audio.body()).length).toBeGreaterThan(1000);
});

test('dashboard tour starts until closed and can be replayed to the end', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Dashboard`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  await page.reload({ waitUntil: 'networkidle' });
  const tour = page.locator('.aa-tour');
  await expect(tour).toHaveCount(0);

  await page.locator('[data-aa-tour-start]:visible').first().click();
  const dialog = page.getByRole('dialog', { name: 'Welcome to Academia Auditiva' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('.aa-tour-count')).toHaveText(/^Step 1 of \d+$/);

  const seen = page.waitForRequest(request => request.url().includes('/Tutorial/Seen'));
  for (let step = 1; await tour.count() > 0; step++) {
    expect(step, 'the tour ends').toBeLessThan(10);
    await page.locator('.aa-tour-pop .btn-primary').click();
  }
  expect((await seen).postData()).toContain('finished=true');
});

// A page's guided tour starts over it on the first visit; close it as a student
// would (Esc) and wait until the server remembers it.
async function closeTourIfStarted(page: Page) {
  const data = page.locator('#aa-tour-data');
  if (await data.count() === 0 || !JSON.parse(await data.textContent() ?? '{}').autoStart) return;

  await expect(page.locator('.aa-tour')).toBeVisible();
  const seen = page.waitForResponse(response => response.url().includes('/Tutorial/Seen'));
  await page.keyboard.press('Escape');
  expect((await seen).status()).toBe(204);
  await expect(page.locator('.aa-tour')).toHaveCount(0);
}

// "Db4" as the answer button that plays it ("C#"): the buttons name pitch classes in sharps.
function pitchClass(note: string) {
  const sharps: Record<string, string> = { Db: 'C#', Eb: 'D#', Gb: 'F#', Ab: 'G#', Bb: 'A#' };
  const name = note.replace(/\d+$/, '');
  return sharps[name] ?? name;
}

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
