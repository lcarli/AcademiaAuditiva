import { expect, test, type Browser, type Page } from '@playwright/test';

const cultures = ['en-US', 'pt-BR', 'fr-CA'] as const;

test.beforeEach(async ({ page }) => {
  (page as Page & { _aaErrors?: string[] })._aaErrors = collectErrors(page, []);
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

test('an admin can lock, unlock and delete an account', async ({ page, baseURL, browser }) => {
  const errors = (page as Page & { _aaErrors?: string[] })._aaErrors!;
  const stamp = Date.now().toString(36);
  const email = `e2e-lock-${stamp}@example.test`;
  const password = `E2e!${stamp}Aa1`;
  await page.goto(`${baseURL}/Identity/Account/Register`, { waitUntil: 'networkidle' });
  await page.fill('#Input_FirstName', 'E2E');
  await page.fill('#Input_LastName', 'Locked');
  await page.fill('#Input_Email', email);
  await page.fill('#Input_Password', password);
  await page.fill('#Input_ConfirmPassword', password);
  await Promise.all([
    page.waitForURL(/RegisterConfirmation/),
    page.click('#registerSubmit')
  ]);
  await Promise.all([
    page.waitForURL(/ConfirmEmail/),
    page.click('#confirm-link')
  ]);

  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  const account = page.locator('tbody tr', { hasText: email });
  const showAccount = () => page.goto(`${baseURL}/Admin/Users?q=${encodeURIComponent(email)}`, { waitUntil: 'networkidle' });
  // Clicks one of the account's buttons, agrees to what it asks and returns the question.
  const clickAndConfirm = async (button: string) => {
    let question = '';
    page.once('dialog', dialog => { question = dialog.message(); void dialog.accept(); });
    await account.getByRole('button', { name: button, exact: true }).click();
    return question;
  };

  await showAccount();
  expect(await clickAndConfirm('Lock')).toContain(email);
  await expect(page.locator('.alert-success')).toContainText(`Account ${email} locked.`);
  await showAccount();
  await expect(account.getByText('locked', { exact: true })).toBeVisible();
  await expect(account.getByRole('button', { name: 'Lock', exact: true })).toHaveCount(0);
  await signInElsewhere(browser, errors, baseURL!, email, password, async owner => {
    await expect(owner).toHaveURL(/\/Identity\/Account\/Lockout/);
    await expect(owner.locator('main')).toContainText("This account is locked and can't sign in right now.");
    await expect(owner.locator('main a[href="mailto:contato@academiaauditiva.com"]')).toBeVisible();
  });

  expect(await clickAndConfirm('Unlock')).toContain(email);
  await expect(page.locator('.alert-success')).toContainText(`Account ${email} unlocked.`);
  await showAccount();
  await expect(account.getByText('locked', { exact: true })).toHaveCount(0);
  await expect(account.getByRole('button', { name: 'Lock', exact: true })).toBeVisible();
  await signInElsewhere(browser, errors, baseURL!, email, password, async owner => {
    await expect(owner).toHaveURL(/\/Dashboard/);
  });

  await Promise.all([
    page.waitForURL(/\/Admin\/Users\/Delete\//),
    account.getByRole('link', { name: 'Delete', exact: true }).click()
  ]);
  await expect(page.locator('main')).toContainText(email);
  await expect(page.locator('main .alert-danger')).toContainText("It can't be undone.");
  await expect(page.locator('main .alert-danger')).not.toContainText('classrooms and routines');
  await page.getByRole('button', { name: 'Delete account permanently' }).click();
  await expect(page.locator('.alert-success')).toContainText(`Account ${email} deleted.`);
  await showAccount();
  await expect(page.locator('tbody')).toContainText('No users match.');
  await signInElsewhere(browser, errors, baseURL!, email, password, async owner => {
    await expect(owner.locator('.validation-summary-errors')).toContainText('Invalid login attempt.');
  });
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
  await expect(filters.locator('#rangeStart')).toHaveAttribute('min', '3');
  await expect(filters.locator('#rangeEnd')).toHaveAttribute('max', '6');
  // The lowest octave starts at the violin's G string.
  await filters.locator('#rangeStart').fill('3');
  await expect(filters.locator('#rangeStartLabel')).toHaveText('G3');
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
  await expect(filters.locator('#rangeStart')).toHaveAttribute('min', '3');
});

test('the guitar plays the chords where on the neck the student picks', async ({ page, baseURL, context }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessChords`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  const filters = page.locator('#filtersModal');
  const piano = filters.locator('[data-instrument="Piano"]');
  const guitar = filters.locator('[data-instrument="Guitar"]');
  const positions = filters.locator('#positionFilter');
  const range = filters.locator('#rangeFilter');
  const open = filters.locator('[data-guitar-position="Open"]');
  const high = filters.locator('[data-guitar-position="High"]');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  // A violin plays one note at a time.
  await expect(filters.locator('[data-instrument="Violin"]')).toHaveCount(0);
  await expect(piano).toHaveAttribute('aria-pressed', 'true');
  await expect(positions).toBeHidden();
  await expect(range).toBeVisible();

  // On the guitar, where on the neck to play replaces the note range: the open chords first.
  await guitar.click();
  await expect(positions).toBeVisible();
  await expect(range).toBeHidden();
  await expect(open).toHaveAttribute('aria-pressed', 'true');
  await high.click();
  await expect(high).toHaveAttribute('aria-pressed', 'true');
  await expect(open).toHaveAttribute('aria-pressed', 'false');
  expect((await context.cookies()).find(cookie => cookie.name === 'guitarPosition')?.value).toBe('High');
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
  await page.click('#Play');
  const audio = await audioResponse;
  expect(audio.headers()['content-type']).toContain('audio/');
  expect((await audio.body()).length).toBeGreaterThan(1000);

  // The page is rendered with the position the cookie remembers, and the piano brings the range back.
  await page.reload({ waitUntil: 'networkidle' });
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(high).toHaveAttribute('aria-pressed', 'true');
  await expect(positions).toBeVisible();
  await expect(range).toBeHidden();
  await piano.click();
  await expect(positions).toBeHidden();
  await expect(range).toBeVisible();
});

test('an exercise that sets its own octave offers no octave range, only the instrument', async ({ page, baseURL, context }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(`${baseURL}/Exercise/CompleteChord`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  const filters = page.locator('#filtersModal');
  const guitar = filters.locator('[data-instrument="Guitar"]');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(filters.locator('select[name="ccOctave"]')).toBeVisible();
  await expect(filters.locator('#rangeFilter')).toHaveCount(0);
  await guitar.click();
  await expect(guitar).toHaveAttribute('aria-pressed', 'true');
  await expect(filters.locator('#positionFilter')).toHaveCount(0);
  expect((await context.cookies()).find(cookie => cookie.name === 'instrument')?.value).toBe('Guitar');
  expect(errors).toEqual([]);
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

test('guess the meter plays its beats as clicks or as bass and chords, and checks the meter', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessMeter?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const answers = page.locator('.aa-answer.guessAnswer:visible');
  await expect(answers).toHaveCount(3);
  expect(await answers.evaluateAll(buttons => buttons.map(button => (button as HTMLButtonElement).value)))
    .toEqual(['duple', 'triple', 'quadruple']);

  const filters = page.locator('#filtersModal');
  const dialog = page.locator('.swal2-popup');
  for (const level of ['clicks', 'accompaniment']) {
    await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
    await filters.locator('select[name="gmLevel"]').selectOption(level);
    await filters.locator('.btn-close').click();
    await expect(filters).toBeHidden();

    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters).toEqual({ gmLevel: level });
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    expect((await audio.body()).length).toBeGreaterThan(1000);

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    expect(shown.answer).toMatch(/^(duple|triple|quadruple)$/);
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();

    await page.locator(`.aa-answer:visible[value="${shown.answer}"]`).click();
    const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validateResponse).json();
    expect(result).toMatchObject({ success: true, free: true, isCorrect: true, answer: shown.answer });
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
  }
});

test('guess the rhythm draws four rhythms, plays one of them and checks the one picked', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessRhythmPattern?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const options = page.locator('#rhythmOptions .aa-rhythm-option');
  // Nothing to pick before a round plays.
  await expect(options).toHaveCount(0);
  await expect(page.locator('#rhythmPlaceholder')).toBeVisible();

  const filters = page.locator('#filtersModal');
  const dialog = page.locator('.swal2-popup');
  for (const level of ['1', '3', '4', '5', '6', '7', '8']) {
    await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
    await filters.locator('select[name="grpLevel"]').selectOption(level);
    await filters.locator('.btn-close').click();
    await expect(filters).toBeHidden();

    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters).toEqual({ grpLevel: level, grpTempo: '120' });
    const offered: string[] = (await play.json()).metadata.options;
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    expect((await audio.body()).length).toBeGreaterThan(1000);

    // Each rhythm is drawn on a staff and read out for screen readers, never as codes ("q|8|bar").
    await expect(options).toHaveCount(4);
    await expect(page.locator('#rhythmPlaceholder')).toBeHidden();
    expect(await options.evaluateAll(buttons => buttons.map(button => (button as HTMLButtonElement).value))).toEqual(offered);
    for (let i = 0; i < 4; i++) {
      await expect(options.nth(i).locator('svg .vf-stavenote').first()).toBeVisible();
      await expect(options.nth(i)).toHaveAttribute('aria-label', new RegExp(`^Rhythm ${i + 1}: [^|:]+$`));
    }

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    expect(offered).toContain(shown.answer);
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();

    const played = options.nth(offered.indexOf(shown.answer));
    await played.click();
    await expect(played).toHaveAttribute('aria-pressed', 'true');
    const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validateResponse).json();
    expect(result).toMatchObject({ success: true, free: true, isCorrect: true, answer: shown.answer });
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    // The rhythm played stays marked, and nothing more can be picked, until the next round.
    await expect(played).toHaveClass(/\bis-answer\b/);
    await expect(page.locator('#rhythmOptions .aa-rhythm-option:disabled')).toHaveCount(4);
  }
});

test('on a phone the rhythms to pick from stay readable, a bar to a line when they are long', async ({ page, baseURL }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessRhythmPattern?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const filters = page.locator('#filtersModal');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await filters.locator('select[name="grpLevel"]').selectOption('6');
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  // Sixteenths in 4/4 would shrink to a third of their size on one line.
  for (let round = 0; round < 3; round++) {
    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    await page.click('#Play');
    const metadata = (await (await playResponse).json()).metadata;
    await expect(page.locator('#rhythmOptions .aa-rhythm-option')).toHaveCount(4);
    const layout = await page.locator('#rhythmOptions').evaluate(list => ({
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      scales: [...list.querySelectorAll('.aa-rhythm-staff svg')]
        .map(svg => svg.getBoundingClientRect().width / Number(svg.getAttribute('width'))),
      lines: [...list.querySelectorAll('.aa-rhythm-staff')].map(staff => staff.querySelectorAll('svg').length),
    }));
    const label = `${metadata.timeSignature}: ${JSON.stringify(metadata.options)}`;
    expect(layout.overflow, label).toBe(0);
    for (const scale of layout.scales) expect(scale, label).toBeGreaterThanOrEqual(0.5);
    // The four are laid out alike: a line each, or a line per bar.
    expect(new Set(layout.lines).size, label).toBe(1);
    expect([1, metadata.numMeasures], label).toContain(layout.lines[0]);
  }
});

test('tap the rhythm takes the taps once the count-in has played again, and says what went wrong', async ({ page, baseURL }) => {
  test.setTimeout(90_000);
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/RhythmTap?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const pad = page.locator('#tapPad');
  const count = page.locator('#tapCount');
  const dialog = page.locator('.swal2-popup');
  // Nothing to tap before a round plays.
  await expect(pad).toBeDisabled();
  await expect(count).toHaveText('Taps: 0');

  const filters = page.locator('#filtersModal');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await filters.locator('select[name="rtLevel"]').selectOption('3');
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const playRound = async () => {
    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters).toEqual({ rtLevel: '3', rtTempo: '120' });
    const metadata = (await play.json()).metadata;
    // The page learns when to take the taps, never the rhythm.
    expect(Object.keys(metadata).sort()).toEqual(['level', 'numMeasures', 'tapsFrom', 'timeSignature']);
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    // A tap while the rhythm and the second count-in play counts for nothing.
    await expect(pad).toBeDisabled();
    await page.keyboard.press('Space');
    await expect(count).toHaveText('Taps: 0');
    await expect(pad).toBeEnabled({ timeout: (metadata.tapsFrom + 10) * 1000 });
  };

  await playRound();
  const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
  await page.locator('[data-aa-reveal]').click();
  const answer: string = (await (await revealResponse).json()).answer;
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();

  // A tap on the space bar where each note starts, at 120 quarter notes a minute.
  const beats: Record<string, number> = { w: 4, h: 2, q: 1 };
  const onsets: number[] = [];
  let at = 0;
  for (const value of answer.split('|').filter(value => value !== 'bar')) {
    if (!value.endsWith('r')) onsets.push(at * 500);
    at += beats[value.replace(/r$/, '')];
  }
  const start = Date.now() + 200;
  for (const onset of onsets) {
    await page.waitForTimeout(Math.max(0, start + onset - Date.now()));
    await page.keyboard.press('Space');
  }
  await expect(count).toHaveText(`Taps: ${onsets.length}`);
  let validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  let result = await (await validateResponse).json();
  expect(result, answer).toMatchObject({ success: true, free: true, isCorrect: true, answer });
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();
  await expect(pad).toBeDisabled();

  // One tap for a rhythm of three notes at least: the rhythm is drawn under how many it has.
  await playRound();
  await pad.click();
  await expect(count).toHaveText('Taps: 1');
  validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  result = await (await validateResponse).json();
  expect(result).toMatchObject({ isCorrect: false, detail: { taps: 1, deviations: null } });
  await expect(dialog).toContainText(`Taps: 1, but the rhythm has ${result.detail.notes} notes:`);
  await expect(dialog.locator('svg .vf-stavenote').first()).toBeVisible();
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();
});

test('rhythm dictation is written with note and rest symbols, without note names', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  const metadata = await openDictation(page, baseURL!, 'RhythmDictation', 'rdLevel');

  expect(metadata).toMatchObject({ durations: ['w', 'h', 'q', '8'], rests: true });
  await expectFigureButtons(page, ['w', 'h', 'q', '8', 'wr', 'hr', 'qr', '8r']);
  // A rhythm has no pitch: there are no note names, octaves or accidentals to pick.
  await expect(page.locator('#staffEditor :is([data-note-name], [data-octave], [data-accidental])')).toHaveCount(0);

  // Each click writes its symbol on the staff; the barlines come by themselves.
  const answer = await revealOnStaff(page);
  const [first, ...others] = answer.split('|').filter(token => token !== 'bar');
  await page.click(`#staffEditor [data-figure="${first}"]`);
  await expectUnfinishedNotChecked(page);
  for (const token of others) await page.click(`#staffEditor [data-figure="${token}"]`);
  // Every measure is full: no note value fits any more.
  await expect(page.locator('#staffEditor [data-figure]:not([disabled])')).toHaveCount(0);

  expect(await validateStaff(page)).toMatchObject({ success: true, free: true, isCorrect: true, answer });
  await page.click('.swal2-confirm');
});

// The rhythm levels past Advanced: dotted notes, sixteenths, syncopation and compound meter.
for (const [level, figures] of [
  ['5', ['h.', 'h', 'q.', 'q', '8', 'qr']],
  ['6', ['h', 'q', '8.', '8', '16', 'qr']],
  ['7', ['h', 'q', '8', 'qr', '8r']],
  ['8', ['h.', 'q.', 'q', '8', 'q.r']],
] as const) {
  test(`rhythm dictation level ${level} offers the figures it teaches, and is written with them`, async ({ page, baseURL }) => {
    await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
    const metadata = await openDictation(page, baseURL!, 'RhythmDictation', 'rdLevel', level);

    expect(metadata.durations).toEqual(figures.filter(figure => !figure.endsWith('r')));
    expect(metadata.restDurations).toEqual(figures.filter(figure => figure.endsWith('r')));
    expect(metadata.timeSignature).toMatch(level === '8' ? /^6\/8$/ : /^[234]\/4$/);
    await expectFigureButtons(page, [...figures]);

    const answer = await revealOnStaff(page);
    for (const token of answer.split('|').filter(token => token !== 'bar')) {
      await page.click(`#staffEditor [data-figure="${token}"]`);
    }
    await expect(page.locator('#staffEditor [data-figure]:not([disabled])')).toHaveCount(0);

    expect(await validateStaff(page)).toMatchObject({ success: true, free: true, isCorrect: true, answer });
    await page.click('.swal2-confirm');
  });
}

test('melodic dictation is written with note symbols and note names, and a wrong one shows the melody', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  const metadata = await openDictation(page, baseURL!, 'MelodicDictation', 'mdLevel');

  // A melody in 6/8 is written in the figures of compound meter.
  const compound = metadata.timeSignature === '6/8';
  const durations = compound ? ['h.', 'q.', 'q', '8'] : ['w', 'h', 'q', '8'];
  const rests = compound ? ['q.r'] : ['wr', 'hr', 'qr', '8r'];
  expect(metadata).toMatchObject({ durations, restDurations: rests, rests: true });
  expect(metadata.firstNote).toMatch(/^[A-G][#b]?\d$/);
  await expectFigureButtons(page, [...durations, ...rests]);
  await expect(page.locator('#staffEditor [data-note-name]')).toHaveText(['C', 'D', 'E', 'F', 'G', 'A', 'B']);

  const answer = await revealOnStaff(page);
  await writeMelody(page, answer);
  expect(await validateStaff(page)).toMatchObject({ success: true, free: true, isCorrect: true, answer });
  await page.click('.swal2-confirm');

  // A melody never climbs to octave 6, so B6s are always wrong.
  await playDictation(page);
  const editor = page.locator('#staffEditor');
  const up = editor.locator('[data-octave="up"]');
  for (let i = 0; i < 4 && await up.isEnabled(); i++) await up.click();
  await expect(editor.locator('[data-octave-status]')).toHaveText(/6$/);
  const b = editor.locator('[data-note-name="B"]');
  for (let i = 0; i < 40 && await b.isEnabled(); i++) await b.click();
  await expect(b).toBeDisabled();

  const wrong = await validateStaff(page);
  expect(wrong).toMatchObject({ success: true, isCorrect: false });
  const dialog = page.locator('.swal2-popup');
  await expect(dialog.locator('.aa-answer-caption')).toHaveText('The correct answer was:');
  await expect(dialog.locator('.aa-reveal-staff svg')).toBeVisible();
  await expect(dialog.locator('.aa-reveal-staff')).toHaveAttribute('aria-label', /^[A-G][#b]?\d (dotted )?(whole|half|quarter|eighth) note[,;]/);
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();
});

test('sight-singing is silent on a new melody and plays its starting note on the piano when asked', async ({ page, baseURL }) => {
  // Notes what the page does with sound: clips decoded, clips played and synthesized tones.
  await page.addInitScript(() => {
    const heard: string[] = [];
    (window as unknown as { aaHeard: string[] }).aaHeard = heard;
    const decode = BaseAudioContext.prototype.decodeAudioData;
    BaseAudioContext.prototype.decodeAudioData = function (this: BaseAudioContext, data: ArrayBuffer) {
      return decode.call(this, data).then(buffer => {
        heard.push('decoded');
        return buffer;
      });
    };
    const tone = BaseAudioContext.prototype.createOscillator;
    BaseAudioContext.prototype.createOscillator = function (this: BaseAudioContext) {
      heard.push('tone');
      return tone.call(this);
    };
    const start = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (this: AudioBufferSourceNode, ...args: Parameters<typeof start>) {
      heard.push('clip');
      return start.apply(this, args);
    };
  });
  const heard = () => page.evaluate(() => (window as unknown as { aaHeard: string[] }).aaHeard.slice());
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/SolfegeMelody`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  // A new melody is drawn and its starting note fetched, but nothing plays.
  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  const noteResponse = page.waitForResponse(response => response.url().includes('/audio/token/'));
  await page.click('#Generate');
  const play = await (await playResponse).json();
  expect(play.melody[0]).toMatchObject({ type: 'note' });
  expect(play.startingNoteToken).toMatch(/^[0-9a-f]{32}$/);
  const note = await noteResponse;
  expect(note.url()).toContain(play.startingNoteToken);
  expect(note.headers()['content-type']).toContain('audio/');
  expect((await note.body()).length).toBeGreaterThan(1000);
  await expect(page.locator('#output-sheet svg')).toBeVisible();
  await expect.poll(heard).toEqual(['decoded']);

  // Its button plays the note, once, as the piano clip: no synthesized tone.
  await page.click('#playStartingNote');
  await expect.poll(heard).toEqual(['decoded', 'clip']);
});

test('complete the chord is heard and written on the staff, above its root or whole', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  const editor = page.locator('#staffEditor');
  const noteheads = editor.locator('svg .vf-notehead');

  // The root is on the staff; the other notes stack above it, written in any order.
  const given = await openChord(page, baseURL!, { ccQuality: 'sevenths', ccAccidentals: 'any', ccRoot: 'given', ccOctave: '4' });
  expect(given).toMatchObject({ clef: 'treble', octave: 4, slots: 3 });
  const [root] = given.promptNotes;
  expect(root).toMatch(/^[A-G][#b]?4$/);
  await expect(page.locator('#staffPrompt')).toHaveText(`Listen to the chord and complete it above ${root}.`);
  await expect(noteheads).toHaveCount(1);

  const seventh = await revealOnStaff(page, 4);
  const upper = seventh.split('|').map(token => token.split(':')[0]);
  expect(upper).toHaveLength(3);
  for (const note of [...upper].reverse()) await writeNote(page, note);
  await expect(noteheads).toHaveCount(4);
  await expect(editor.locator('[data-note-name]:not([disabled])')).toHaveCount(0);
  expect(await validateStaff(page)).toMatchObject({ success: true, free: true, isCorrect: true, answer: seventh });
  await page.click('.swal2-confirm');

  // With the root hidden, the whole chord goes on an empty staff: the bass one for octave 3.
  const hidden = await openChord(page, baseURL!, { ccQuality: 'triads', ccAccidentals: 'none', ccRoot: 'hidden', ccOctave: '3' });
  expect(hidden).toMatchObject({ promptNotes: [], clef: 'bass', octave: 3, slots: 3 });
  await expect(page.locator('#staffPrompt')).toHaveText('Listen to the chord and write all of its notes on the staff, with the root in octave 3.');
  await expect(noteheads).toHaveCount(0);

  const triad = await revealOnStaff(page, 3);
  const [bottom, middle, top] = triad.split('|').map(token => token.split(':')[0]);
  const topTooHigh = top.replace(/\d$/, octave => String(Number(octave) + 1));
  await writeNote(page, topTooHigh);
  await writeNote(page, middle);
  await writeNote(page, bottom);
  // Undo takes back the note written last, wherever it sits in the chord.
  await editor.locator('[data-action="undo"]').click();
  await expect(noteheads).toHaveCount(2);
  await writeNote(page, bottom);
  // A click on a note of the chord selects it, to move it.
  await clickHighestNote(page);
  await expect(editor.locator('[data-octave-status]')).toHaveText(new RegExp(`${topTooHigh.slice(-1)}$`));
  await editor.locator('[data-octave="down"]').click();
  expect(await validateStaff(page)).toMatchObject({ success: true, free: true, isCorrect: true, answer: triad });
  await page.click('.swal2-confirm');
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

test('the header stays on top of wide pages and links land below it', async ({ page, baseURL }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(`${baseURL}/Home/Privacy`, { waitUntil: 'networkidle' });
  const header = page.locator('.aa-site-header');
  await page.evaluate(() => window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'instant' }));
  await expect(header).toBeInViewport({ ratio: 1 });

  await page.locator('.aa-side-sticky a[href="#cookies"]').click();
  await expect(page).toHaveURL(/#cookies$/);
  const [sectionTop, headerBottom] = await page.evaluate(() => [
    document.getElementById('cookies')!.getBoundingClientRect().top,
    document.querySelector('.aa-site-header')!.getBoundingClientRect().bottom
  ]);
  expect(sectionTop).toBeGreaterThanOrEqual(headerBottom);

  // On a phone it scrolls away: the open menu can be taller than the screen.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => window.scrollTo({ top: 1500, behavior: 'instant' }));
  await expect(header).not.toBeInViewport();
});

test('on a phone the header menus open over the page and stay on screen', async ({ page, baseURL }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 360, height: 740 });
  await page.goto(`${baseURL}/Identity/Account/Login`, { waitUntil: 'networkidle' });
  await expectHeaderMenuUsable(page, '.aa-lang-btn');

  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Admin`, { waitUntil: 'networkidle' });
  await expectHeaderMenuUsable(page, '.aa-lang-btn');
  await expectHeaderMenuUsable(page, '.aa-site-header .dropdown-toggle.aa-ghost-btn');
});

test('public pages never scroll sideways on phones, tablets or small laptops', async ({ page, baseURL }) => {
  const paths = [...cultures.map(c => `/?culture=${c}&ui-culture=${c}`), '/Exercise', '/Home/Privacy', '/Identity/Account/Login', '/Identity/Account/Register'];
  for (const path of paths) {
    await page.setViewportSize({ width: 360, height: 740 });
    await page.goto(`${baseURL}${path}`, { waitUntil: 'networkidle' });
    // Below 1200 px <main> fills the screen, so a row gutter wider than its padding sticks out.
    for (const width of [360, 768, 1024]) {
      await page.setViewportSize({ width, height: 740 });
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, `${path} at ${width} px`).toBe(0);
    }
  }
});

test('teacher and admin menus fit every label on one line and sit beside the page on wide screens', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  for (const culture of cultures) {
    for (const path of ['/Teacher', '/Admin', '/Admin/Users']) {
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto(`${baseURL}${path}?culture=${culture}&ui-culture=${culture}`, { waitUntil: 'networkidle' });
      for (const width of [390, 768, 992, 1200, 1280, 1400, 1920]) {
        await page.setViewportSize({ width, height: 900 });
        expect(await sideMenuLayout(page), `${path} ${culture} at ${width} px`).toEqual({ wrapped: [], overflow: 0, beside: width >= 992 });
      }
    }
  }

  // A table wider than the page scrolls inside it instead of pushing the page under the menu.
  await page.evaluate(() => {
    const cells = Array.from({ length: 30 }, (_, i) => `<td class="text-nowrap">Column ${i}</td>`).join('');
    document.querySelector('main aside')!.nextElementSibling!
      .insertAdjacentHTML('beforeend', `<div class="table-responsive"><table class="table"><tr>${cells}</tr></table></div>`);
  });
  for (const width of [992, 1920]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await sideMenuLayout(page), `wide table at ${width} px`).toEqual({ wrapped: [], overflow: 0, beside: true });
  }
});

test('each tour step brings its element out from under the header', async ({ page, baseURL }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 1024, height: 600 });
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Dashboard`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  await page.locator('[data-aa-tour-start]:visible').first().click();
  await expect(page.locator('.aa-tour')).toBeVisible();
  // The tour leaves out steps whose elements this screen doesn't show.
  const targets = await page.evaluate(() => {
    const steps: { target?: string }[] = JSON.parse(document.getElementById('aa-tour-data')!.textContent!).steps;
    const shown = (node: Element) => node.getClientRects().length > 0 && getComputedStyle(node).visibility !== 'hidden';
    return steps.filter(s => !s.target || [...document.querySelectorAll(s.target)].some(shown)).map(s => s.target ?? null);
  });

  let hidden = 0;
  for (let step = 1; step < targets.length; step++) {
    // Scrolls the next step's elements under the header before moving to it.
    hidden += await page.evaluate(target => {
      const header = document.querySelector('.aa-site-header')!;
      const nodes = target ? [...document.querySelectorAll(target)].filter(n => n.getClientRects().length && !header.contains(n)) : [];
      if (!nodes.length) return 0;
      const top = () => Math.min(...nodes.map(n => n.getBoundingClientRect().top));
      window.scrollBy({ top: top() - 10, behavior: 'instant' });
      return top() < header.getBoundingClientRect().bottom ? 1 : 0;
    }, targets[step]);
    await page.keyboard.press('ArrowRight');
    await expect(page.locator('.aa-tour-count')).toHaveText(`Step ${step + 1} of ${targets.length}`);

    const spot = await page.evaluate(target => new Promise<{ top: number; headerBottom: number; inHeader: boolean }>(resolve =>
      requestAnimationFrame(() => requestAnimationFrame(() => {
        const header = document.querySelector('.aa-site-header')!;
        const nodes = target ? [...document.querySelectorAll(target)].filter(n => n.getClientRects().length) : [];
        resolve({
          top: document.querySelector('.aa-tour-spot')!.getBoundingClientRect().top,
          headerBottom: header.getBoundingClientRect().bottom,
          inHeader: nodes.length > 0 && nodes.every(n => header.contains(n))
        });
      }))), targets[step]);
    if (targets[step] && !spot.inHeader) {
      expect(spot.top, `step ${step + 1} clears the header`).toBeGreaterThanOrEqual(spot.headerBottom - 0.5);
    }
  }
  expect(hidden, 'steps that started under the header').toBeGreaterThan(0);

  const seen = page.waitForResponse(response => response.url().includes('/Tutorial/Seen'));
  await page.keyboard.press('Escape');
  expect((await seen).status()).toBe(204);
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

// Opens one of the header's menus on a small screen: it must fit the screen and
// each item must be the element a tap on it reaches.
async function expectHeaderMenuUsable(page: Page, toggle: string) {
  const collapse = page.locator('#mainNavbar');
  if (!await collapse.evaluate(node => node.classList.contains('show'))) {
    await page.locator('.navbar-toggler').click();
    await expect(collapse).toHaveClass(/\bshow\b/);
  }
  await page.locator(toggle).click();
  const menu = page.locator('.aa-site-header .dropdown-menu.show');
  await expect(menu).toBeVisible();
  const { left, right, screen, items } = await menu.evaluate(node => {
    const box = node.getBoundingClientRect();
    return {
      left: box.left,
      right: box.right,
      screen: document.documentElement.clientWidth,
      items: [...node.querySelectorAll('.dropdown-item')].map(item => {
        const r = item.getBoundingClientRect();
        const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
        return { text: item.textContent!.trim(), onTop: !!hit && item.contains(hit) };
      })
    };
  });
  expect(left, `${toggle} menu starts on screen`).toBeGreaterThanOrEqual(0);
  expect(right, `${toggle} menu ends on screen`).toBeLessThanOrEqual(screen);
  expect(items.length).toBeGreaterThan(0);
  for (const item of items) expect(item.onTop, `"${item.text}" is on top`).toBe(true);
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();
}

// Where the teacher and admin areas put their menu (the <aside>) and the page after it,
// and which of its links are taller than a line.
async function sideMenuLayout(page: Page) {
  return page.evaluate(() => {
    const aside = document.querySelector('main aside')!;
    const menu = aside.getBoundingClientRect();
    const content = aside.nextElementSibling!.getBoundingClientRect();
    // A label too long for the menu breaks under its icon.
    const wrapped = [...aside.querySelectorAll<HTMLElement>('.nav-link')].filter(link => {
      const style = getComputedStyle(link);
      const height = link.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);
      return height > 1.5 * parseFloat(style.lineHeight);
    }).map(link => link.textContent!.trim());
    return {
      wrapped,
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      beside: Math.abs(content.top - menu.top) < 1 && content.left >= menu.right - 1
    };
  });
}

// "Db4" as the answer button that plays it ("C#"): the buttons name pitch classes in sharps.
function pitchClass(note: string) {
  const sharps: Record<string, string> = { Db: 'C#', Eb: 'D#', Gb: 'F#', Ab: 'G#', Bb: 'A#' };
  const name = note.replace(/\d+$/, '');
  return sharps[name] ?? name;
}

// Opens a dictation in free practice at a level (by default Advanced: every plain note value,
// and rests) and plays a round; returns what the round tells the staff editor.
async function openDictation(page: Page, baseURL: string, exercise: string, levelFilter: string, level = '4') {
  await page.goto(`${baseURL}/Exercise/${exercise}?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const filters = page.locator('#filtersModal');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await filters.locator(`select[name="${levelFilter}"]`).selectOption(level);
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();
  return playDictation(page);
}

async function playDictation(page: Page) {
  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Play');
  const play = await (await playResponse).json();
  await expect(page.locator('#staffEditor [data-figure]').first()).toBeVisible();
  return play.metadata;
}

// The note values are offered as their symbols, named for screen readers, never as codes ("w", "qr").
async function expectFigureButtons(page: Page, figures: string[]) {
  const buttons = await page.locator('#staffEditor [data-figure]').evaluateAll(nodes => nodes.map(node => ({
    figure: (node as HTMLElement).dataset.figure,
    label: node.getAttribute('aria-label'),
    text: node.textContent!.trim(),
    drawn: !!node.querySelector('svg path'),
  })));
  expect(buttons.map(button => button.figure)).toEqual(figures);
  for (const button of buttons) {
    expect(button.text, `${button.figure} shows no code`).toBe('');
    expect(button.drawn, `${button.figure} is drawn`).toBe(true);
    expect(button.label, `${button.figure} is named`).toMatch(/^(Whole|Half|Quarter|Eighth|Sixteenth|Dotted (half|quarter|eighth)) (note|rest)$/);
  }
}

// Free practice shows the round's answer on a staff, read out without codes;
// the notes of a chord (as many as chordOf) stack as one.
async function revealOnStaff(page: Page, chordOf?: number) {
  const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
  await page.click('[data-aa-reveal]');
  const shown = await (await revealResponse).json();
  expect(shown.success).toBe(true);
  const dialog = page.locator('.swal2-popup');
  await expect(dialog.locator('.aa-reveal-staff svg')).toBeVisible();
  await expect(dialog.locator('.aa-reveal-staff')).toHaveAttribute('aria-label', /^[^|:]+$/);
  if (chordOf) {
    await expect(dialog.locator('.aa-reveal-staff .vf-stavenote')).toHaveCount(1);
    await expect(dialog.locator('.aa-reveal-staff .vf-notehead')).toHaveCount(chordOf);
  }
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();
  return shown.answer as string;
}

// Writes a melody ("E4:q|rest:qr|bar|G4:h") with the editor's buttons: the note value,
// then the note. The barlines come by themselves.
async function writeMelody(page: Page, answer: string) {
  const editor = page.locator('#staffEditor');
  for (const token of answer.split('|')) {
    if (token === 'bar') continue;
    const [note, duration] = token.split(':');
    const figure = editor.locator(`[data-figure="${duration}"]`);
    if (note === 'rest') {
      await figure.click();
      continue;
    }
    if (await figure.getAttribute('aria-pressed') !== 'true') await figure.click();
    await expect(figure).toHaveAttribute('aria-pressed', 'true');
    await writeNote(page, note);
  }
}

// Writes a note ("Eb5") with the editor's buttons: its name, then its octave and accidental.
async function writeNote(page: Page, note: string) {
  const editor = page.locator('#staffEditor');
  const octaveStatus = editor.locator('[data-octave-status]');
  const [, name, accidental, octave] = /^([A-G])([#b]?)(\d)$/.exec(note)!;
  await editor.locator(`[data-note-name="${name}"]`).click();
  for (let i = 0; i < 4; i++) {
    const current = Number(/(\d+)\s*$/.exec(await octaveStatus.textContent() ?? '')?.[1]);
    if (current === Number(octave)) break;
    await editor.locator(`[data-octave="${current < Number(octave) ? 'up' : 'down'}"]`).click();
  }
  await expect(octaveStatus).toHaveText(new RegExp(`${octave}$`));
  if (accidental) await editor.locator(`[data-accidental="${accidental === '#' ? 'sharp' : 'flat'}"]`).click();
}

// Opens "complete the chord" in free practice with the chosen filters and plays a round:
// the chord is heard. Returns what the round tells the staff editor.
async function openChord(page: Page, baseURL: string, choices: Record<string, string>) {
  await page.goto(`${baseURL}/Exercise/CompleteChord?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const filters = page.locator('#filtersModal');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  for (const [group, option] of Object.entries(choices)) {
    await filters.locator(`select[name="${group}"]`).selectOption(option);
  }
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  const audioResponse = page.waitForResponse(response => response.url().includes('/audio/token/'));
  await page.click('#Play');
  const play = await (await playResponse).json();
  const audio = await audioResponse;
  expect(audio.url()).toContain(play.playToken);
  expect(audio.headers()['content-type']).toContain('audio/');
  await expect(page.locator('#staffEditor [data-note-name]').first()).toBeVisible();
  return play.metadata;
}

// Clicks the highest note of the chord on the editor's staff.
async function clickHighestNote(page: Page) {
  const centres = await page.locator('#staffEditor svg .vf-notehead').evaluateAll(nodes => nodes.map(node => {
    const box = node.getBoundingClientRect();
    return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
  }));
  const highest = centres.reduce((best, centre) => (centre.y < best.y ? centre : best));
  await page.mouse.click(highest.x, highest.y);
}

// A dictation is checked only once every measure is written.
async function expectUnfinishedNotChecked(page: Page) {
  let checked = false;
  const listener = (request: { url(): string }) => {
    if (request.url().includes('/Exercise/ValidateExercise')) checked = true;
  };
  page.on('request', listener);
  await page.click('#validateGuess');
  const dialog = page.locator('.swal2-popup');
  await expect(dialog.locator('.swal2-html-container')).toHaveText('Fill every measure before validating.');
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();
  page.off('request', listener);
  expect(checked, 'an unfinished dictation is not sent').toBe(false);
}

async function validateStaff(page: Page) {
  const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  return (await validateResponse).json();
}

// Notes what goes wrong on a page (script errors, console errors, failed
// requests) for the check after each test.
function collectErrors(page: Page, errors: string[]) {
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
  return errors;
}

// Signs in from a browser of its own, as the account's owner would, and runs
// the checks on where that left them.
async function signInElsewhere(browser: Browser, errors: string[], baseURL: string, email: string, password: string, check: (page: Page) => Promise<void>) {
  const context = await browser.newContext({ locale: 'en-US' });
  try {
    const page = await context.newPage();
    collectErrors(page, errors);
    await login(page, baseURL, email, password);
    await check(page);
    await page.waitForLoadState('networkidle');
  } finally {
    await context.close();
  }
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
