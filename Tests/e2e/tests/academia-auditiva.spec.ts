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
