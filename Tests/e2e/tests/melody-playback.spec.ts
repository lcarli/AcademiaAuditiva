import path from 'node:path';
import { expect, test } from '@playwright/test';

for (const exercise of ['GuessChangedNote', 'GuessMissingNote']) {
  test(`${exercise} cancels queued melodies on validation, reset and newer playback`, async ({ page, baseURL }) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.clock.install();
    await page.clock.pauseAt(new Date());
    await page.route('**/melody-playback-fixture', route => route.fulfill({
      contentType: 'text/html',
      body: `<!doctype html><html><body>
        <input id="exerciseId" value="1"><input id="melodyLength" value="7">
        <button id="Play">Play</button><button id="Replay">Replay</button>
        <button id="Melody1">First</button><button id="Melody2">Second</button>
        <button id="choosePosition" class="guessPosition" value="1">Position</button>
        <button id="chooseDirection" class="guessDirection" value="up">Direction</button>
        <button id="chooseAnswer" class="guessAnswer" value="same">Answer</button>
        <button id="validateGuess">Validate</button>
        <script>
          window.fixturePlayed = [];
          window.AcademiaAuditiva = { init() {} };
          window.AudioEngine = {
            setupWaveform() {}, stop() {},
            playToken(token) {
              if (typeof token !== 'string' || !token) throw new Error('Invalid audio token');
              window.fixturePlayed.push(token);
              return Promise.resolve();
            }
          };
          window.AAi18n = {
            localizer: () => ({}), serverError: () => false,
            result() {}, incomplete() {}, noAudio() {}
          };
          let round = 0;
          window.AAPractice = {
            setAnswerView() {}, onReset(handler) { window.fixtureReset = handler; },
            play() {
              round += 1;
              return Promise.resolve({
                roundId: 'round-' + round, notes: 7,
                melody1Token: round + '-a', melody2Token: round + '-b'
              });
            },
            validate() {
              return new Promise(resolve => {
                window.fixtureFinishValidate = () => resolve({ success: true, isCorrect: true });
              });
            }
          };
        </script>
        <script src="/js/Exercises/${exercise}.js"></script>
      </body></html>`,
    }));
    await page.route(`**/js/Exercises/${exercise}.js`, route => route.fulfill({
      path: path.resolve('..', '..', 'AcademiaAuditiva', 'wwwroot', 'js', 'Exercises', `${exercise}.js`),
      contentType: 'text/javascript',
    }));
    await page.goto(`${baseURL}/melody-playback-fixture`);
    const played = () => page.evaluate(() =>
      (window as typeof window & { fixturePlayed: string[] }).fixturePlayed);

    await page.locator('#Play').click();
    await expect.poll(played).toEqual(['1-a']);
    if (exercise === 'GuessChangedNote') {
      await page.locator('#choosePosition').click();
      await page.locator('#chooseDirection').click();
    } else {
      await page.locator('#chooseAnswer').click();
    }
    await page.locator('#validateGuess').click();
    await page.clock.runFor(1_100);
    expect(await played()).toEqual(['1-a']);
    await page.evaluate(() =>
      (window as typeof window & { fixtureFinishValidate: () => void }).fixtureFinishValidate());

    await page.locator('#Play').click();
    await expect.poll(played).toEqual(['1-a', '2-a']);
    await page.evaluate(() =>
      (window as typeof window & { fixtureReset: () => void }).fixtureReset());
    await page.clock.runFor(1_100);
    expect(await played()).toEqual(['1-a', '2-a']);

    await page.locator('#Play').click();
    await expect.poll(played).toEqual(['1-a', '2-a', '3-a']);
    await page.locator('#Replay').click();
    await page.clock.runFor(1_100);
    expect(await played()).toEqual(['1-a', '2-a', '3-a', '3-a', '3-b']);

    await page.locator('#Play').click();
    await expect.poll(played).toEqual(['1-a', '2-a', '3-a', '3-a', '3-b', '4-a']);
    await page.locator('#Melody2').click();
    await page.clock.runFor(1_100);
    expect(await played()).toEqual(['1-a', '2-a', '3-a', '3-a', '3-b', '4-a', '4-b']);

    await page.locator('#Play').click();
    await expect.poll(played).toContain('5-a');
    await page.locator('#Play').click();
    await page.clock.runFor(1_100);
    expect((await played()).slice(-3)).toEqual(['5-a', '6-a', '6-b']);
    expect(errors).toEqual([]);
  });
}
