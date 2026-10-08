import path from 'node:path';
import { expect, test } from '@playwright/test';

// The pitch detector of the singing exercises (wwwroot/js/core/pitch-detector.js)
// on seeded synthetic singers (fixtures/synthetic-voice.js): every voice type,
// vowel and habit (vibrato, slides, notes a little out of tune, breath, mains
// hum), scored as the exercises score them. It runs in the browser only, so
// the app needn't be running.

const detector = path.join(__dirname, '..', '..', '..', 'AcademiaAuditiva', 'wwwroot', 'js', 'core', 'pitch-detector.js');
const voice = path.join(__dirname, '..', 'fixtures', 'synthetic-voice.js');

type Task = 'note' | 'interval' | 'melody' | 'silence';
type Note = { midi: number; seconds: number };
type Case = { task: Task; notes: Note[]; options: Record<string, unknown> };
type Detected = { midi: number; start: number; duration: number };

function cases(count: number): Case[] {
  let state = 2024;
  const rnd = () => {
    state = (state * 1103515245 + 12345) % 2147483648;
    return state / 2147483648;
  };
  const pick = <T>(list: T[]) => list[Math.floor(rnd() * list.length)];
  const between = (a: number, b: number) => a + (b - a) * rnd();
  const voices = [[40, 62], [48, 69], [53, 74], [60, 81]]; // bass, tenor, alto, soprano
  const major = [0, 2, 4, 5, 7, 9, 11];
  const inMajor = (midi: number) => major.includes(((midi % 12) + 12) % 12);
  const list: Case[] = [];
  for (let i = 0; i < count; i++) {
    const [low, high] = pick(voices);
    const task = pick<Task>(['note', 'interval', 'melody', 'melody']);
    let notes: Note[];
    if (task === 'note') {
      notes = [{ midi: Math.round(between(low, high)), seconds: between(0.8, 2) }];
    } else if (task === 'interval') {
      const first = Math.round(between(low + 12, high - 12));
      const steps = Math.round(between(1, 12)) * (rnd() < 0.5 ? -1 : 1);
      notes = [{ midi: first, seconds: between(0.7, 1.4) }, { midi: first + steps, seconds: between(0.7, 1.4) }];
    } else {
      // A melody in C major, mostly by step, as the exercises' melodies are.
      const length = Math.round(between(4, 8));
      let midi = Math.round((low + high) / 2 + between(-4, 4));
      while (!inMajor(midi)) midi++;
      notes = [];
      for (let n = 0; n < length; n++) {
        notes.push({ midi, seconds: n === length - 1 ? between(0.8, 1.2) : between(0.35, 0.75) });
        let next: number;
        do {
          const step = rnd() < 0.7 ? pick([-2, -1, 1, 2]) : pick([-7, -5, -4, -3, 3, 4, 5, 7]);
          next = midi + step;
          while (!inMajor(next)) next += step > 0 ? 1 : -1;
        } while (next < low + 2 || next > high - 2 || next === midi);
        midi = next;
      }
    }
    list.push({
      task,
      notes,
      options: {
        seed: i + 1,
        vowel: pick(['a', 'e', 'i', 'o', 'u']),
        gain: Math.pow(10, between(-1.7, -0.3)),
        vibratoCents: between(10, 70),
        vibratoRate: between(4.5, 7),
        detuneCents: between(0, 30),
        driftCents: between(2, 15),
        legato: rnd() < 0.5,
        glideSeconds: between(0.03, 0.12),
        gapSeconds: between(0.05, 0.3),
        scoopCents: between(0, 150),
        scoopSeconds: between(0.03, 0.12),
        fallCents: rnd() < 0.4 ? between(50, 250) : 0,
        fallSeconds: between(0.05, 0.15),
        breath: between(0.005, 0.06),
        room: between(0.0003, 0.004),
        hum: rnd() < 0.2 ? between(0.002, 0.02) : 0
      }
    });
  }
  // Nobody singing: a quiet room, a noisy one, mains hum.
  for (let i = 0; i < 8; i++) {
    list.push({ task: 'silence', notes: [], options: { seed: 9000 + i, room: Math.pow(10, between(-3.3, -1.5)), hum: i % 3 === 0 ? between(0.005, 0.05) : 0 } });
  }
  return list;
}

const pitchClass = (midi: number) => ((midi % 12) + 12) % 12;

function withoutRepeats<T>(list: T[]) {
  return list.filter((value, i) => i === 0 || value !== list[i - 1]);
}

function editDistance(a: number[], b: number[]) {
  const d = Array.from({ length: a.length + 1 }, (_, i) => [i, ...Array<number>(b.length).fill(0)]);
  for (let j = 1; j <= b.length; j++) d[0][j] = j;
  for (let i = 1; i <= a.length; i++) {
    for (let j = 1; j <= b.length; j++) {
      d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
    }
  }
  return d[a.length][b.length];
}

function longest(notes: Detected[], count: number) {
  return notes.map((note, index) => ({ note, index }))
    .sort((a, b) => b.note.duration - a.note.duration)
    .slice(0, count)
    .sort((a, b) => a.index - b.index)
    .map(entry => entry.note.midi);
}

// Whether the exercise would take what was found as the notes sung: the
// longest note (SingNote), the two longest (SingInterval), or the melody with
// at most one note wrong, missing or extra (SingMelody, SolfegeMelody).
function heard(task: Task, sung: Note[], found: Detected[]) {
  const want = sung.map(note => note.midi);
  switch (task) {
    case 'silence':
      return found.length === 0;
    case 'note':
      return found.length > 0 && pitchClass(longest(found, 1)[0]) === pitchClass(want[0]);
    case 'interval': {
      const two = longest(found, 2);
      return two.length === 2 && pitchClass(two[0]) === pitchClass(want[0]) && two[1] - two[0] === want[1] - want[0];
    }
    case 'melody': {
      const melody = withoutRepeats(want.map(pitchClass));
      return editDistance(withoutRepeats(found.map(note => pitchClass(note.midi))), melody) <= (melody.length >= 4 ? 1 : 0);
    }
  }
}

test('the pitch detector hears what synthetic singers sing, and nothing when nobody sings', async ({ page }) => {
  test.setTimeout(180_000);
  await page.addScriptTag({ path: detector });
  await page.addScriptTag({ path: voice });

  const list = cases(120);
  const found = await page.evaluate(async list => {
    type Voice = { sing(notes: unknown[], options: object): { samples: Float32Array; sampleRate: number }; wav(samples: Float32Array, rate: number): Blob };
    const w = window as unknown as { SyntheticVoice: Voice; PitchDetector: { detect(blob: Blob): Promise<Detected[]> } };
    const out: Detected[][] = [];
    for (const c of list) {
      const sung = w.SyntheticVoice.sing(c.notes, c.options);
      out.push(await w.PitchDetector.detect(w.SyntheticVoice.wav(sung.samples, sung.sampleRate)));
    }
    return out;
  }, list);

  const missed: Record<Task, string[]> = { note: [], interval: [], melody: [], silence: [] };
  const counts: Record<Task, number> = { note: 0, interval: 0, melody: 0, silence: 0 };
  list.forEach((c, i) => {
    counts[c.task]++;
    if (!heard(c.task, c.notes, found[i])) {
      missed[c.task].push(`case ${i}: sang ${c.notes.map(n => n.midi).join(' ')}, heard ${found[i].map(n => n.midi).join(' ') || 'nothing'}`);
    }
  });

  // Every task is in the sample.
  for (const task of Object.keys(counts) as Task[]) expect(counts[task], task).toBeGreaterThan(5);
  expect(missed.silence).toEqual([]);
  expect(missed.note).toEqual([]);
  expect(missed.interval).toEqual([]);
  // A melody may lose a note to a slide or a breath now and then.
  expect(missed.melody.length, missed.melody.join('\n')).toBeLessThanOrEqual(Math.floor(counts.melody * 0.03));
});
