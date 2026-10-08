import path from 'node:path';
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

test('the home page counts every exercise it lists, by category, each linking to its page', async ({ page, baseURL }) => {
  await page.goto(`${baseURL}/?culture=en-US&ui-culture=en-US`, { waitUntil: 'networkidle' });

  const title = await page.locator('#aa-ex-title').textContent();
  const count = Number(title!.match(/^(\d+) ways to train your ear$/)![1]);
  expect(count).toBeGreaterThan(29);
  await expect(page.locator('.aa-feature-title').first()).toContainText(`${count} exercises`);
  await expect(page.locator('.aa-ex-group-title')).toHaveText(['Ear Training', 'Melody', 'Harmony', 'Scales', 'Rhythm']);
  const items = page.locator('.aa-ex-group .aa-ex-item');
  await expect(items).toHaveCount(count);
  const links = await items.evaluateAll(links => links.map(link => link.getAttribute('href')));
  expect(new Set(links).size, 'each exercise is listed once').toBe(count);
  for (const group of await page.locator('.aa-ex-group').all()) {
    await expect(group.locator('.aa-ex-item').first()).toBeVisible();
  }

  await expect(page.locator('.aa-ex-item', { hasText: 'In tune or not?' })).toHaveAttribute('href', '/Exercise/GuessTuning');
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

test('guess the quality shows the chords of the chord type picked, plays one of them and checks it', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessQuality?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const answers = page.locator('.aa-answer.guessAnswer:visible');
  const shownAnswers = () => answers.evaluateAll(buttons => buttons.map(button => (button as HTMLButtonElement).value));
  expect(await shownAnswers()).toEqual(['major', 'minor']);

  const filters = page.locator('#filtersModal');
  const dialog = page.locator('.swal2-popup');
  const pickChordType = async (chordGroup: string) => {
    await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
    await filters.locator('select[name="chordGroup"]').selectOption(chordGroup);
    await filters.locator('.btn-close').click();
    await expect(filters).toBeHidden();
  };
  const chordTypes: [string, string[]][] = [
    ['sevenths', ['major7', 'dominant7', 'minor7', 'halfDiminished', 'diminished7']],
    ['susAdded', ['sus2', 'sus4', 'major6', 'add9']],
  ];
  for (const [chordGroup, qualities] of chordTypes) {
    await pickChordType(chordGroup);
    expect(await shownAnswers()).toEqual(qualities);

    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters.chordGroup).toBe(chordGroup);
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    expect((await audio.body()).length).toBeGreaterThan(1000);

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    expect(qualities).toContain(shown.answer);
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

  // An answer the new chord type hides can't stay picked.
  await page.locator('.aa-answer:visible[value="add9"]').click();
  await expect(page.locator('.aa-answer.selected')).toHaveCount(1);
  await pickChordType('both');
  expect(await shownAnswers()).toEqual(['major', 'minor']);
  await expect(page.locator('.aa-answer.selected')).toHaveCount(0);
});

test('guess the top note plays the chord as written, on the guitar too, and checks the note on top', async ({ page, baseURL, context }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessTopNote?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const topNotes = ['topRoot', 'topThird', 'topFifth'];
  const answers = page.locator('.aa-answer.guessAnswer:visible');
  expect(await answers.evaluateAll(buttons => buttons.map(button => (button as HTMLButtonElement).value))).toEqual(topNotes);
  await expect(answers).toHaveText(['Root', 'Third', 'Fifth']);

  // The guitar sounds the written notes, so no position on the neck replaces the octave range.
  const filters = page.locator('#filtersModal');
  const guitar = filters.locator('[data-instrument="Guitar"]');
  const quality = filters.locator('select[name="tnQuality"]');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(filters.locator('[data-instrument="Violin"]')).toHaveCount(0);
  await expect(filters.locator('#positionFilter')).toHaveCount(0);
  await expect(quality.locator('option')).toHaveText(['Majors', 'Minors', 'Majors and Minors']);
  await expect(quality).toHaveValue('major');
  await guitar.click();
  await expect(guitar).toHaveAttribute('aria-pressed', 'true');
  await expect(filters.locator('#rangeFilter')).toBeVisible();
  await expect(filters.locator('#rangeStart')).toHaveAttribute('min', '2');
  await expect(filters.locator('#rangeEnd')).toHaveAttribute('max', '5');
  expect((await context.cookies()).find(cookie => cookie.name === 'instrument')?.value).toBe('Guitar');
  await quality.selectOption('minor');
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const dialog = page.locator('.swal2-popup');
  const playAndReveal = async () => {
    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters.tnQuality).toBe('minor');
    // Nothing but the round and its audio: the page can't tell which note is on top.
    expect(Object.keys(await play.json()).sort()).toEqual(['playToken', 'roundId']);
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    expect((await audio.body()).length).toBeGreaterThan(1000);

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    expect(topNotes).toContain(shown.answer);
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    return shown.answer as string;
  };
  const answer = async (guess: string) => {
    await page.locator(`.aa-answer:visible[value="${guess}"]`).click();
    const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validateResponse).json();
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    return result;
  };

  const first = await playAndReveal();
  expect(await answer(first)).toMatchObject({ success: true, free: true, isCorrect: true, answer: first });
  const second = await playAndReveal();
  const wrong = topNotes.find(note => note !== second)!;
  expect(await answer(wrong)).toMatchObject({ success: true, free: true, isCorrect: false, answer: second });
});

test('in tune or not plays the note twice, the second time the level\'s cents out of tune, and checks the answer', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessTuning?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const tunings = ['inTune', 'sharp', 'flat'];
  const answers = page.locator('.aa-answer.guessAnswer:visible');
  expect(await answers.evaluateAll(buttons => buttons.map(button => (button as HTMLButtonElement).value))).toEqual(tunings);
  await expect(answers).toHaveText(['In tune', 'Sharp', 'Flat']);

  const filters = page.locator('#filtersModal');
  const level = filters.locator('select[name="gtLevel"]');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(level.locator('option')).toHaveText(['50 cents (a quarter tone)', '25 cents', '10 cents', '5 cents']);
  await expect(level).toHaveValue('50');
  await expect(filters.locator('#rangeFilter')).toBeVisible();
  await filters.locator('[data-instrument="Piano"]').click();
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const dialog = page.locator('.swal2-popup');
  const playAndReveal = async () => {
    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters.gtLevel).toBe('50');
    // Nothing but the round and its audio: the page can't tell whether the note is out of tune.
    expect(Object.keys(await play.json()).sort()).toEqual(['playToken', 'roundId']);
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    const cents = await centsBetweenTheNotes(page, await audio.body());

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    expect(tunings).toContain(shown.answer);
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    return { answer: shown.answer as string, cents };
  };
  const answer = async (guess: string) => {
    await page.locator(`.aa-answer:visible[value="${guess}"]`).click();
    const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validateResponse).json();
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    return result;
  };

  // What the student hears: the second note 50 cents above or below the first, or the same note.
  const heard = new Set<string>();
  for (let round = 0; round < 8 && heard.size < 2; round++) {
    const { answer: tuning, cents } = await playAndReveal();
    const expected = { inTune: 0, sharp: 50, flat: -50 }[tuning]!;
    expect(Math.abs(cents - expected), `the ${tuning} note sounds ${cents.toFixed(1)} cents away`).toBeLessThan(5);
    heard.add(tuning);
    if (round === 0) {
      expect(await answer(tuning)).toMatchObject({ success: true, free: true, isCorrect: true, answer: tuning });
    } else {
      const wrong = tunings.find(t => t !== tuning)!;
      expect(await answer(wrong)).toMatchObject({ success: true, free: true, isCorrect: false, answer: tuning });
    }
  }
  expect(heard.size, 'the rounds are not all the same answer').toBeGreaterThan(1);
});

test('which note changed plays two melodies, offers a button for each note and checks the note and where it went', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessChangedNote?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const positions = page.locator('.aa-answer.guessPosition:visible');
  const shownPositions = () => positions.evaluateAll(buttons => buttons.map(button => (button as HTMLButtonElement).value));
  // Four notes to start with, the shortest length, as Compare 2 melodies.
  expect(await shownPositions()).toEqual(['1', '2', '3', '4']);
  await expect(page.locator('.aa-answer.guessDirection:visible')).toHaveText(['Up', 'Down']);

  // A button for each note of the length picked.
  const filters = page.locator('#filtersModal');
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await filters.locator('select[name="melodyLength"]').selectOption('7');
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();
  expect(await shownPositions()).toEqual(['1', '2', '3', '4', '5', '6', '7']);

  const dialog = page.locator('.swal2-popup');
  const playAndReveal = async () => {
    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    expect(JSON.parse(play.request().postData() ?? '{}').filters.melodyLength).toBe('7');
    // The two melodies and how many notes they have: the page can't tell which note changed.
    const round = await play.json();
    expect(Object.keys(round).sort()).toEqual(['melody1Token', 'melody2Token', 'notes', 'roundId']);
    expect(round.notes).toBe(7);
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    expect((await audio.body()).length).toBeGreaterThan(1000);

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    expect(shown.answer).toMatch(/^[1-7]\|(up|down)$/);
    const [note, moved] = shown.answer.split('|');
    await expect(dialog.locator('.swal2-html-container')).toHaveText(`Note ${note}, which went ${moved}`);
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    return shown.answer as string;
  };
  const answer = async (guess: string) => {
    const [note, moved] = guess.split('|');
    await page.locator(`.aa-answer.guessPosition:visible[value="${note}"]`).click();
    await page.locator(`.aa-answer.guessDirection:visible[value="${moved}"]`).click();
    const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validateResponse).json();
    await expect(dialog).toBeVisible();
    return result;
  };
  const close = async () => {
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
  };

  const first = await playAndReveal();
  expect(await answer(first)).toMatchObject({ success: true, free: true, isCorrect: true, answer: first });
  await close();
  const second = await playAndReveal();
  const [note, moved] = second.split('|');
  const otherWay = moved === 'up' ? 'down' : 'up';
  expect(await answer(`${note}|${otherWay}`)).toMatchObject({ success: true, free: true, isCorrect: false, answer: second });
  await expect(dialog.locator('.swal2-html-container')).toContainText(`The correct answer was note ${note}, which went ${moved}.`);
  await close();
  await expect(page.locator('.aa-answer.selected')).toHaveCount(0);
});

test('the full interval plays its notes together when asked, with no direction, and checks the interval', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessFullInterval?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  await expect(page.locator('body')).toContainText('Played together, seconds and sevenths sound harsh');

  const filters = page.locator('#filtersModal');
  const mode = filters.locator('select[name="intervalMode"]');
  const direction = filters.locator('select[name="intervalDirection"]');
  const pickMode = async (value: string) => {
    await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
    await mode.selectOption(value);
    // Two notes played together go neither up nor down.
    if (value === 'harmonic') await expect(direction).toBeDisabled();
    else await expect(direction).toBeEnabled();
    await filters.locator('.btn-close').click();
    await expect(filters).toBeHidden();
  };
  await page.locator('[data-bs-target="#filtersModal"]:visible').first().click();
  await expect(mode.locator('option')).toHaveText(
    ['Melodic (one note after the other)', 'Harmonic (both notes together)', 'Both, at random']);
  await expect(mode).toHaveValue('melodic');
  await expect(direction).toBeEnabled();
  await filters.locator('.btn-close').click();
  await expect(filters).toBeHidden();

  const dialog = page.locator('.swal2-popup');
  const playAndCheck = async () => {
    const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
    const audioResponse = page.waitForResponse(response => response.url().includes('/audio/') && response.status() === 200);
    await page.click('#Play');
    const play = await playResponse;
    const sent = JSON.parse(play.request().postData() ?? '{}').filters;
    expect(Object.keys(await play.json()).sort()).toEqual(['playToken', 'roundId']);
    const audio = await audioResponse;
    expect(audio.headers()['content-type']).toContain('audio/');
    expect((await audio.body()).length).toBeGreaterThan(1000);

    const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
    await page.locator('[data-aa-reveal]').click();
    const shown = await (await revealResponse).json();
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();

    await page.locator(`.aa-answer:visible[value="${shown.answer}"]`).click();
    const validateResponse = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validateResponse).json();
    expect(result).toMatchObject({ success: true, free: true, isCorrect: true, answer: shown.answer });
    await page.click('.swal2-confirm');
    await expect(dialog).toBeHidden();
    return sent;
  };

  await pickMode('harmonic');
  expect(await playAndCheck()).toEqual({ keySelect: 'C', intervalMode: 'harmonic' });
  await pickMode('both');
  expect(await playAndCheck()).toEqual({ keySelect: 'C', intervalMode: 'both', intervalDirection: 'asc' });
  await pickMode('melodic');
  expect(await playAndCheck()).toEqual({ keySelect: 'C', intervalMode: 'melodic', intervalDirection: 'asc' });
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

test('sight-singing hears the melody sung into the microphone and checks it', async ({ page, baseURL }) => {
  test.setTimeout(90_000);
  await fakeMicrophone(page);
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/SolfegeMelody`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Generate');
  const melody: { type: string; note: string }[] = (await (await playResponse).json()).melody;
  const notes = melody.filter(item => item.type === 'note').map(item => noteMidi(item.note));
  await expect(page.locator('#output-sheet svg')).toBeVisible();

  const { guess, result } = await singAndValidate(page, notes.map(midi => ({ midi, seconds: 0.6 })));
  // A one-bar melody may be a single whole note, and repeated notes are heard as one.
  expect(guess).toMatch(/^[A-G]#?\d(\|[A-G]#?\d)*$/);
  expect(result).toMatchObject({ success: true, isCorrect: true });
  await expect(page.locator('.swal2-popup .swal2-title')).toHaveText('Correct!');
});

test('sing the note shows the level while recording, then hears the note in any octave and how in tune it was', async ({ page, baseURL }) => {
  test.setTimeout(90_000);
  await fakeMicrophone(page);
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/SingNote?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const dialog = page.locator('.swal2-popup');

  // Nothing to sing before a round, and nothing to check before singing.
  await page.click('#recordAudio');
  await expect(dialog.locator('.swal2-title')).toHaveText('No audio loaded');
  await page.click('.swal2-confirm');
  const { play, answer } = await playAndReveal(page);
  expect(Object.keys(play).sort()).toEqual(['playToken', 'roundId']);
  expect(answer).toMatch(/^[A-G]#?\d$/);
  await page.click('#validateGuess');
  await expect(dialog).toContainText('Record yourself singing first.');
  await page.click('.swal2-confirm');

  // Sung where a voice sings it, which may be another octave.
  const sung = singable(noteMidi(answer));
  const meter = page.locator('#micMeter');
  await expect(meter).toBeHidden();
  const { guess, result } = await singAndValidate(page, [{ midi: sung, seconds: 1.5 }], async () => {
    await expect(meter).toBeVisible();
    await expect.poll(() => meter.locator('.aa-mic-meter-level').evaluate(level =>
      Number(/scaleX\(([\d.]+)\)/.exec((level as HTMLElement).style.transform)?.[1] ?? 0))).toBeGreaterThan(0.3);
  });
  await expect(meter).toBeHidden();
  expect(guess).toBe(sharpName(sung));
  expect(result).toMatchObject({ success: true, free: true, isCorrect: true, answer });
  await expect(dialog.locator('.swal2-title')).toHaveText('Correct!');
  await expect(dialog.locator('.swal2-html-container')).toHaveText(`We heard ${noteLabel(sung)}.You sang it in tune.`);
  await page.click('.swal2-confirm');

  // A note sung a tone too high is wrong, and the answer says which note was asked for.
  const next = await playAndReveal(page);
  const wrong = singable(noteMidi(next.answer)) + 2;
  const second = await singAndValidate(page, [{ midi: wrong, seconds: 1.5 }]);
  expect(second.result).toMatchObject({ success: true, free: true, isCorrect: false, answer: next.answer });
  await expect(dialog.locator('.swal2-title')).toHaveText('Wrong!');
  await expect(dialog.locator('.swal2-html-container')).toHaveText(
    `The correct answer was ${noteLabel(noteMidi(next.answer))}.We heard ${noteLabel(wrong)}.`);
  await page.click('.swal2-confirm');
  await expect(page.locator('#aaFreeCorrect')).toHaveText('1');
  await expect(page.locator('#aaFreeWrong')).toHaveText('1');
});

test('sing the interval shows the interval to sing but not its note, and checks the two notes sung', async ({ page, baseURL }) => {
  test.setTimeout(90_000);
  await fakeMicrophone(page);
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/SingInterval?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const dialog = page.locator('.swal2-popup');
  const prompt = page.locator('#singIntervalPrompt');
  await expect(prompt).toBeEmpty();

  // Easy, ascending by default: the round names the interval and its direction only.
  const names: Record<string, string> = { '2M': 'Major 2nd', '3M': 'Major 3rd', '4J': 'Perfect 4th', '5J': 'Perfect 5th', '8J': 'Perfect 8th' };
  const steps: Record<string, number> = { '2M': 2, '3M': 4, '4J': 5, '5J': 7, '8J': 12 };
  const { play, answer } = await playAndReveal(page, ' → ');
  expect(Object.keys(play).sort()).toEqual(['direction', 'interval', 'playToken', 'roundId']);
  expect(play.direction).toBe('asc');
  expect(Object.keys(names)).toContain(play.interval);
  await expect(prompt).toHaveText(`${names[play.interval]}Ascending`);
  await expect(prompt.locator('.bi-arrow-up')).toBeVisible();
  const [first, second] = answer.split('|').map(noteMidi);
  expect(second - first).toBe(steps[play.interval]);

  // One note is not an interval: the round stays to sing again.
  const start = singable(first);
  await record(page, [{ midi: start, seconds: 1.2 }]);
  await page.click('#validateGuess');
  await expect(dialog).toContainText('We heard only one note.');
  await page.click('.swal2-confirm');
  await expect(prompt).not.toBeEmpty();

  // A semitone short is wrong, and the answer says which interval was sung.
  const short = await singAndValidate(page, [{ midi: start, seconds: 1.1 }, { midi: start + steps[play.interval] - 1, seconds: 1.1 }]);
  expect(short.guess).toBe(`${sharpName(start)}|${sharpName(start + steps[play.interval] - 1)}`);
  expect(short.result).toMatchObject({ success: true, free: true, isCorrect: false, answer });
  const semitoneShort: Record<string, string> = { '2M': 'Minor 2nd', '3M': 'Minor 3rd', '4J': 'Major 3rd', '5J': 'Augmented 4th', '8J': 'Major 7th' };
  const sungName = semitoneShort[play.interval as string];
  await expect(dialog.locator('.swal2-html-container')).toContainText(`Interval you sang: ${sungName}, ascending.`);
  await page.click('.swal2-confirm');
  await expect(prompt).toBeEmpty();

  // Sung right, in another octave if need be.
  const next = await playAndReveal(page, ' → ');
  const [low, high] = next.answer.split('|').map(noteMidi);
  const from = singable(low);
  const right = await singAndValidate(page, [{ midi: from, seconds: 1.1 }, { midi: from + high - low, seconds: 1.1 }]);
  expect(right.result).toMatchObject({ success: true, free: true, isCorrect: true, answer: next.answer });
  await expect(dialog.locator('.swal2-title')).toHaveText('Correct!');
  await expect(dialog.locator('.swal2-html-container')).toHaveText(`We heard ${noteLabel(from)} → ${noteLabel(from + high - low)}.`);
});

test('sing the melody plays the melody and checks the one sung back', async ({ page, baseURL }) => {
  test.setTimeout(90_000);
  await fakeMicrophone(page);
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/SingMelody?practice=free`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);

  const { play, answer } = await playAndReveal(page);
  expect(Object.keys(play).sort()).toEqual(['playToken', 'roundId']);
  const melody = answer.split('|').map(noteMidi);
  expect(melody).toHaveLength(4);
  const { result } = await singAndValidate(page, melody.map(midi => ({ midi, seconds: 0.6 })));
  expect(result).toMatchObject({ success: true, free: true, isCorrect: true, answer });
  await expect(page.locator('.swal2-popup .swal2-title')).toHaveText('Correct!');
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

test('the games hub offers each game, fits small screens and opens the exercise in the game picked', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.locator('#mainNavbar a[href="/Games"]').click();
  await expect(page).toHaveURL(/\/Games$/);
  await expect(page.locator('h1')).toHaveText('Games');
  await expect(page.locator('#mainNavbar a[href="/Games"]')).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('[data-game-mode] h2')).toHaveText(['60-second sprint', 'Sudden death', 'Weak spots', 'Placement test']);

  for (const width of [360, 768, 1024]) {
    await page.setViewportSize({ width, height: 740 });
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `/Games at ${width} px`).toBe(0);
  }
  await page.setViewportSize({ width: 1280, height: 720 });

  const survival = page.locator('section[data-game-mode="survival"]');
  await expect(survival.locator('option[value="SingNote"]'), 'a sung exercise is no game').toHaveCount(0);
  await survival.locator('select[name="exercise"]').selectOption('GuessNote');
  await survival.getByRole('button', { name: 'Play' }).click();
  await expect(page).toHaveURL(/\/Exercise\/GuessNote\?game=survival$/);
  await expect(page.locator('#aaGameName')).toHaveText('Sudden death');
  await expect(page.locator('#aaFreePractice')).toHaveCount(0);
});

test('sudden death plays round after round until the first wrong answer, then shows the streak', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessNote?game=survival`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const bar = page.locator('#aaGame');
  const text = bar.locator('[data-aa-game-text]');
  await expect(text).toHaveText('One mistake ends the game.');
  await expect(bar.locator('[data-aa-game-timer]')).toHaveCount(0);

  const started = page.waitForResponse(response => response.url().includes('/Games/Start'));
  let played = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Play');
  const runId = (await (await started).json()).game.runId;
  for (let streak = 0; streak < 40; streak++) {
    const play = await played;
    expect(JSON.parse(play.request().postData() ?? '{}')).toMatchObject({ gameRunId: runId, free: false });
    expect(Object.keys(await play.json()).sort()).toEqual(['game', 'playToken', 'roundId']);
    await expect(bar.locator('[data-aa-game-stop]')).toBeVisible();

    await page.locator('.aa-answer:visible').first().click();
    const validated = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
    await page.click('#validateGuess');
    const result = await (await validated).json();
    expect(result.game).toMatchObject({ runId, answered: streak + 1, score: result.isCorrect ? streak + 1 : streak });
    if (result.isCorrect) {
      // The next round plays by itself once the answer dialog closes.
      played = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
      await expect(text).toHaveText(`Streak: ${streak + 1}`);
      continue;
    }

    expect(result.game.over).toBe(true);
    await expect(page.locator('.swal2-confirm')).toHaveText('See the result');
    await page.click('.swal2-confirm');
    const end = page.locator('.aa-game-end-popup');
    await expect(end.locator('.swal2-title')).toHaveText('Game over');
    await expect(end.locator('.aa-game-end-text')).toHaveText(`Streak: ${streak} in a row.`);
    await expect(end.locator('.swal2-confirm')).toHaveText('Play again');
    await expect(end.locator('.swal2-cancel')).toHaveText('Back to games');
    await expect(bar.locator('[data-aa-game-stop]')).toBeHidden();
    return;
  }
  throw new Error('Forty right answers in a row picking the first note: the answers are not checked.');
});

test('a sprint runs its clock from the first Play, and stopping it ends it with the answers given', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Exercise/GuessNote?game=sprint`, { waitUntil: 'networkidle' });
  await closeTourIfStarted(page);
  const bar = page.locator('#aaGame');
  const timer = bar.locator('[data-aa-game-timer]');
  await expect(timer).toHaveText('1:00');
  await expect(bar.locator('[data-aa-game-text]')).toHaveText('The clock starts with the first round.');

  const started = page.waitForResponse(response => response.url().includes('/Games/Start'));
  let played = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Play');
  const runId = (await (await started).json()).game.runId;
  expect(JSON.parse((await played).request().postData() ?? '{}').gameRunId).toBe(runId);
  await expect(timer).toHaveText(/^0:5\d$/);

  played = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.locator('.aa-answer:visible').first().click();
  const validated = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  const result = await (await validated).json();
  const score = result.isCorrect ? 1 : 0;
  expect(result.game).toMatchObject({ runId, score, answered: 1, over: false });
  await expect(bar.locator('[data-aa-game-text]')).toHaveText(`Right answers: ${score}`);
  // Right or wrong, the next round follows by itself.
  expect(JSON.parse((await played).request().postData() ?? '{}').gameRunId).toBe(runId);

  const finished = page.waitForResponse(response => response.url().includes('/Games/Finish'));
  await bar.locator('[data-aa-game-stop]').click();
  expect((await (await finished).json()).game).toMatchObject({ runId, over: true, secondsLeft: 0, answered: 1 });
  const end = page.locator('.aa-game-end-popup');
  await expect(end.locator('.swal2-title')).toHaveText('Sprint over');
  await expect(end.locator('.aa-game-end-text')).toHaveText(`${score} right out of 1 answered.`);
  await expect(timer).toHaveText('0:00');
  await expect(bar.locator('[data-aa-game-stop]')).toBeHidden();
});

test('the placement test starts from the games hub on its first exercise and asks its questions in turn', async ({ page, baseURL }) => {
  await login(page, baseURL!, process.env.AA_EMAIL!, process.env.AA_PASSWORD!);
  await page.goto(`${baseURL}/Games`, { waitUntil: 'networkidle' });
  await page.locator('section[data-game-mode="placement"] form button[type="submit"]').click();
  await expect(page).toHaveURL(/\/Exercise\/GuessInterval\?.*game=placement&run=\d+$/);
  const runId = Number(new URL(page.url()).searchParams.get('run'));
  await page.waitForLoadState('networkidle');
  await closeTourIfStarted(page);

  const bar = page.locator('#aaGame');
  const text = bar.locator('[data-aa-game-text]');
  await expect(page.locator('#aaGameName')).toHaveText('Placement test');
  await expect(text).toHaveText('Exercise 1 of 6, question 1 of 3: Guess Interval');
  await expect(bar.locator('[data-aa-game-stop]')).toHaveCount(0);

  let played = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Play');
  expect(JSON.parse((await played).request().postData() ?? '{}')).toMatchObject({ gameRunId: runId, free: false });

  played = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.locator('.aa-answer:visible').first().click();
  const validated = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  expect((await (await validated).json()).game).toMatchObject({ runId, answered: 1, over: false });
  await expect(text).toHaveText('Exercise 1 of 6, question 2 of 3: Guess Interval');
  expect(JSON.parse((await played).request().postData() ?? '{}').gameRunId).toBe(runId);

  await page.goto(`${baseURL}/Games`, { waitUntil: 'networkidle' });
  const placement = page.locator('section[data-game-mode="placement"]');
  await expect(placement.getByRole('link', { name: 'Continue the test' })).toHaveAttribute('href', new RegExp(`game=placement&run=${runId}$`));
  await expect(placement.getByRole('button', { name: 'Start over' })).toBeVisible();
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

// How many cents the note that starts at 2 s sounds above the one that starts at 0 s, measured
// on the round's audio: the period of each, by autocorrelation, half a second into the note.
async function centsBetweenTheNotes(page: Page, mp3: Buffer) {
  return page.evaluate(async base64 => {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const audio = await new OfflineAudioContext(1, 1, 44100).decodeAudioData(bytes.buffer);
    const samples = audio.getChannelData(0);
    const rate = audio.sampleRate;
    const window = (from: number) => samples.subarray(Math.round(from * rate), Math.round((from + 0.5) * rate));
    const correlation = (x: Float32Array, lag: number) => {
      let sum = 0, a = 0, b = 0;
      for (let i = 0; i + lag < x.length; i++) {
        sum += x[i] * x[i + lag];
        a += x[i] * x[i];
        b += x[i + lag] * x[i + lag];
      }
      return sum / Math.sqrt(a * b);
    };
    const period = (x: Float32Array, min: number, max: number) => {
      let best = min, bestValue = -Infinity;
      for (let lag = min; lag <= max; lag++) {
        const value = correlation(x, lag);
        if (value > bestValue) [best, bestValue] = [lag, value];
      }
      const [before, after] = [correlation(x, best - 1), correlation(x, best + 1)];
      return best + (before - after) / (2 * (before - 2 * bestValue + after));
    };
    // From 30 Hz to 1 kHz; the second note is within a semitone of the first.
    const first = period(window(0.3), Math.floor(rate / 1000), Math.ceil(rate / 30));
    const second = period(window(2.3), Math.floor(first * 0.94), Math.ceil(first * 1.06));
    return 1200 * Math.log2(first / second);
  }, mp3.toString('base64'));
}

type Sung = { midi: number; seconds: number };

// A student's microphone: getUserMedia gives the page the synthetic singer of
// fixtures/synthetic-voice.js singing window.aaSing's notes in tune, from the
// moment the page opens it; window.aaSung turns true once they are sung.
async function fakeMicrophone(page: Page) {
  await page.addInitScript({ path: path.join(__dirname, '..', 'fixtures', 'synthetic-voice.js') });
  await page.addInitScript(() => {
    type Voice = { sing(notes: Sung[], options: object): { samples: Float32Array; sampleRate: number } };
    const w = window as unknown as { aaSing: Sung[]; aaSung: boolean; SyntheticVoice: Voice };
    w.aaSing = [];
    w.aaSung = false;
    navigator.mediaDevices.getUserMedia = async () => {
      const context = new AudioContext();
      const { samples, sampleRate } = w.SyntheticVoice.sing(w.aaSing, { seed: 7, sampleRate: context.sampleRate, detuneCents: 0, driftCents: 0 });
      const buffer = context.createBuffer(1, samples.length, sampleRate);
      buffer.copyToChannel(samples, 0);
      const source = context.createBufferSource();
      source.buffer = buffer;
      const microphone = context.createMediaStreamDestination();
      source.connect(microphone);
      w.aaSung = false;
      source.onended = () => { w.aaSung = true; };
      source.start();
      return microphone.stream;
    };
  });
}

// "C#4" as its MIDI number, 61.
function noteMidi(note: string) {
  const naturals: Record<string, number> = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
  const match = /^([A-G])([#b]?)(-?\d+)$/.exec(note);
  expect(match, note).toBeTruthy();
  const shift = match![2] === '#' ? 1 : match![2] === 'b' ? -1 : 0;
  return (Number(match![3]) + 1) * 12 + naturals[match![1]] + shift;
}

const sharpNames = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B'];

// 61 as the singing pages send it: "C#4".
function sharpName(midi: number) {
  return sharpNames[midi % 12] + (Math.floor(midi / 12) - 1);
}

// 61 as the English pages show it, without its octave: "C♯".
function noteLabel(midi: number) {
  return sharpNames[midi % 12].replace('#', '♯');
}

// The note moved by octaves to E3..D#4, where a voice sings it and the octave above it.
function singable(midi: number) {
  while (midi >= 64) midi -= 12;
  while (midi < 52) midi += 12;
  return midi;
}

// Plays a free practice round and reveals its answer, which the page names
// without octaves, joined as given; returns the round and the answer.
async function playAndReveal(page: Page, join = ' ') {
  const playResponse = page.waitForResponse(response => response.url().includes('/Exercise/RequestPlay'));
  await page.click('#Play');
  const play = await (await playResponse).json();
  const revealResponse = page.waitForResponse(response => response.url().includes('/Exercise/RevealAnswer'));
  await page.locator('[data-aa-reveal]').click();
  const answer: string = (await (await revealResponse).json()).answer;
  const dialog = page.locator('.swal2-popup');
  await expect(dialog.locator('.swal2-html-container')).toHaveText(answer.split('|').map(note => noteLabel(noteMidi(note))).join(join));
  await page.click('.swal2-confirm');
  await expect(dialog).toBeHidden();
  return { play, answer };
}

// Records the student singing the notes; whileRecording runs as they sing.
async function record(page: Page, notes: Sung[], whileRecording?: () => Promise<void>) {
  await page.evaluate(notes => { (window as unknown as { aaSing: Sung[] }).aaSing = notes; }, notes);
  await page.click('#recordAudio');
  await expect(page.locator('#recordAudio')).toHaveAttribute('aria-pressed', 'true');
  await whileRecording?.();
  await page.waitForFunction(() => (window as unknown as { aaSung: boolean }).aaSung);
}

// Sings the notes and checks them: returns the note names sent and the server's answer.
async function singAndValidate(page: Page, notes: Sung[], whileRecording?: () => Promise<void>) {
  await record(page, notes, whileRecording);
  const validated = page.waitForResponse(response => response.url().includes('/Exercise/ValidateExercise'));
  await page.click('#validateGuess');
  const response = await validated;
  const guess: string = JSON.parse(response.request().postData() ?? '{}').userGuess;
  return { guess, result: await response.json() };
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
