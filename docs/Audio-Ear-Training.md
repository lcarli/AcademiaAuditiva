# Audio Ear Training

## Status

Proposed implementation plan, written on 2026-10-08.

This document turns the audio-engineering training concept into incremental
work that fits the current Academia Auditiva architecture. GitHub issues
should track each implementation slice; this document owns the overall
direction, sequencing and acceptance criteria.

## Product goal

Academia Auditiva currently trains musical perception: notes, intervals,
chords, melody, rhythm and singing. Audio Ear Training adds a complementary
technical-listening pillar for audio engineers, producers, musicians and
students:

> listen -> diagnose -> decide -> act

The new pillar should teach users to recognize frequency balance, level,
dynamics, stereo position, phase, space, distortion and common mix problems.
It should reuse the existing practice loop:

> listen -> answer -> receive feedback -> track progress -> earn rewards

## Product structure

Audio Ear Training is a top-level training track, not another musical
exercise category. The intended hierarchy is:

```text
Academia Auditiva
|-- Music Ear Training
|   |-- Notes and intervals
|   |-- Melody
|   |-- Harmony
|   |-- Scales
|   `-- Rhythm
|-- Audio Ear Training
|   |-- Level
|   |-- Frequency and EQ
|   |-- Dynamics
|   |-- Stereo and phase
|   |-- Space and time
|   `-- Critical listening
`-- Mix Challenges
    |-- Vocals
    |-- Drums
    |-- Bass
    |-- Stereo
    `-- Full mix
```

The distinction matters because the current `ExerciseCategory` model and
`ExerciseCatalog.CategoryOrder` are flat. Adding EQ beside Harmony and Rhythm
would mix two different learning domains and would make it difficult to offer
separate landing pages, learning paths, placement and recommendations.

## Scope

### First release

The first public release is **Audio Ear Training: Foundations**, with three
exercises:

1. **Level Match** - compare two signals and identify which is louder or the
   approximate gain difference.
2. **Stereo Position** - identify where one source sits between left and
   right.
3. **Guess Frequency** - identify the center frequency of a peaking EQ boost.

These exercises deliberately establish the reusable DSP primitives needed by
the rest of the track: gain, pan and parametric EQ.

### Next exercises

After the first release is stable:

1. **Boost or Cut** - identify the direction and region of an EQ change.
2. **Guess the Q** - identify broad, medium and narrow EQ curves.
3. **Compression** - identify the processed signal at matched loudness.
4. **Attack and Release** - distinguish compressor timing.
5. **Reverb** - distinguish dry, light and heavy ambience.
6. **Find the Mix Problem** - diagnose one intentionally introduced problem.

### Not in the first release

- continuous EQ matching;
- compressor ratio or gain-reduction estimation;
- polarity, phase and mono compatibility;
- reverb type, decay and pre-delay estimation;
- noise, codec and aliasing identification;
- adaptive sessions;
- full interactive Mix Challenges;
- user-uploaded audio or a browser-based DAW.

These remain part of the long-term direction, but should not delay validating
the foundations.

## Design principles

- **One controlled variable.** A round should differ only in the property the
  exercise asks about.
- **Blind comparison.** The response must not reveal which clip is processed.
- **Loudness matching.** Exercises that are not about level must not be
  solvable by choosing the louder clip.
- **Immediate A/B.** Comparative exercises need low-latency switching between
  reference and processed clips.
- **Repeatable rounds.** The processing recipe and source asset must reproduce
  the same expected answer.
- **No answer leakage.** Source names, blob names, processing parameters and
  tokens sent to the browser must not disclose the answer.
- **Educational feedback.** Results should name the correct parameter and
  explain what to listen for.
- **Progressive difficulty.** Beginner rounds use large differences and few
  choices; advanced rounds use smaller differences, closer choices and more
  complex source material.
- **Safe playback.** Avoid unexpected level jumps and tell users to begin at a
  comfortable headphone or speaker level.
- **Localization.** Every user-facing string must exist in en-US, pt-BR and
  fr-CA.

## Current architecture and gaps

The existing exercise flow already provides most product infrastructure:

- `SeedData.Exercises()` defines the exercise catalog;
- `ExerciseController.RequestPlay` creates a round and issues opaque audio
  tokens;
- `ExercisePlaybackPlanner` can return more than one clip for a round;
- `AudioMixerService` decodes sources, mixes them to PCM WAV, caches output in
  Blob Storage and prevents clipping;
- `IExerciseValidator` implementations validate answers;
- score snapshots, dashboards, routines, daily challenges, games, streaks and
  badges consume recorded answers.

The main gaps are:

1. the product has one flat category list and one musical learning path;
2. `ExercisePlaybackPlanner` is coupled to
   `MusicTheoryService.GenerateNoteForExercise`;
3. `MixInput` can schedule, trim and pitch-shift note samples, but cannot
   apply gain, pan, EQ, compression or reverb;
4. the source library contains instrument notes rather than curated speech,
   vocal, percussion and mix excerpts;
5. the generic response normally exposes one `playToken`; comparative
   exercises need named reference and processed tokens;
6. the dashboard and recommendation surfaces do not distinguish training
   tracks.

## Target architecture

### Training tracks

Introduce a stable track key:

```text
Music
Audio
Mix
```

An exercise belongs to one track and one category within that track. Prefer a
code-first catalog consistent with the current `ExerciseCatalog`; persist a
new field only where database relationships, teacher routines or reports need
it. Do not infer a track from category names.

**Implemented (slice 1).** `TrainingTracks` (`Services/TrainingTracks.cs`) is
an explicit, code-first table: each track lists its category names in display
order, a category belongs to exactly one track, and an exercise gets the track
of its category. No column or migration was added. `ExerciseCatalog` fails at
startup on a seeded category that no track lists, `CatalogExercise.Track`
exposes the track and `ExerciseCatalog.ByTrack` groups categories by track.
The Audio categories (`Level`, `FrequencyEq`, `Dynamics`, `StereoPhase`,
`SpaceTime`, `CriticalListening`) are declared and named in all cultures but
not seeded yet; Mix categories wait for Phase 3.

`/Exercise` keeps listing Music; `/Exercise?track=audio` (case-insensitive)
lists another track and an unknown track is a 404. The track selector only
shows when more than one track has exercises, or when an empty track is asked
for, in which case that track renders a localized empty state. The Audio tab
therefore appears by itself once the first Audio exercise is seeded.

The first change must preserve all current URLs, scores, routines, badges and
the Music learning path. Existing exercises default to the `Music` track.

Each track can then own:

- its landing page and category order;
- its learning path;
- track-specific progress summaries;
- eligibility rules for daily challenges, games and placement;
- future recommendations and achievements.

### Audio source catalog

Add a curated, code-owned catalog of training assets. Each asset needs:

- a stable key, never a storage URL;
- instrument or source type;
- tags such as vocal, speech, kick, snare, bass, guitar, piano or full mix;
- sample rate, channel count and duration;
- license, author and attribution requirements;
- loudness and peak measurements;
- suitability for each processor and difficulty;
- checksum or version so generated clips are invalidated when the source
  changes.

Start with a small set of repository-owned or explicitly licensed lossless
clips. Do not use arbitrary production recordings or assets without recorded
rights. Keep source masters out of the public web root and expose them only
through opaque audio tokens.

All sources used in one A/B pair must have the same sample rate and channel
layout. The ingestion process should reject invalid files rather than
silently converting or normalizing them differently.

As built in slice 2:

- `AcademiaAuditiva/Audio/Sources/sources.json` lists every source (key,
  description, kind, tags, uses, difficulties, origin, license and the
  measurements of its file) and `{key}.wav` sits next to it. The folder ships
  with the app outside `wwwroot`, like `Audio/Instruments`, so no URL reaches
  it; `LICENSE.txt` covers it.
- Every file is 16- or 24-bit integer PCM WAV at 44.1 kHz, mono or stereo,
  2 to 20 s long, peaking at or below -1 dBFS and between -36 and -14 LUFS
  (`AudioSourceRules`). The app refuses to start on a catalog that breaks a
  rule, and the `audio-sources` readiness check fails when a file is missing
  or its SHA-256 differs from the catalog's.
- The mixer reads a source through `AudioSourceLibrary` as the input
  `source:{key}`; only listed keys resolve, never a path. The source's
  SHA-256 is part of the mix hash, so a replaced recording never reuses old
  mixes, while every other mix keeps its name.
- `dotnet run --no-cache scripts/audio-sources.cs generate` synthesizes the
  repository's own sources from a fixed seed (pink noise, a drum loop, a bass
  line, synth chords and a stereo mix of the three, all MIT); `ingest <key>
  <file.wav>` adds a recording obtained elsewhere once its entry, license and
  source URL are in the catalog; `measure` rewrites the measurements. Every
  source is set to -23 LUFS, or lower when its peak would pass -1.5 dBFS.
- Loudness is the integrated loudness of ITU-R BS.1770-4 (`Loudness`), with
  its K-weighting derived for any sample rate as libebur128 does.

### Processing plans

Keep musical scheduling and technical audio processing separate. Extend the
audio pipeline with a declarative processing plan instead of adding
exercise-specific branches to `AudioMixerService`.

An illustrative contract is:

```csharp
public sealed record AudioProcessingPlan(
    string SourceKey,
    IReadOnlyList<AudioProcessor> Processors);

public abstract record AudioProcessor;
public sealed record GainProcessor(double Decibels) : AudioProcessor;
public sealed record PanProcessor(double Position) : AudioProcessor;
public sealed record PeakingEqProcessor(
    double FrequencyHz,
    double GainDb,
    double Q) : AudioProcessor;
```

The exact types may change during implementation, but the following invariants
must remain:

- processor order is explicit;
- parameters are validated before decoding or allocating large buffers;
- processing is deterministic;
- the plan, source version and processing-engine version are part of the
  output hash;
- generated output uses the existing short-lived Blob Storage and token flow;
- the expected answer remains server-side;
- no broad fallback returns unprocessed audio when processing fails.

The first processor implementation should operate on the decoded interleaved
floating-point PCM already used by `AudioMixerService`:

- gain converts dB to a linear multiplier;
- constant-power pan controls stereo balance without a center loudness jump;
- peaking EQ uses a tested biquad filter with stable parameter limits;
- the final peak guard remains a safety mechanism, not loudness matching.

Loudness matching should be an explicit step with a documented target and
tolerance. Peak normalization alone is not an acceptable substitute.

### Round generation

Create a technical-listening round generator separate from
`MusicTheoryService`. It should:

1. select an allowed source for the exercise and difficulty;
2. choose processing parameters from the exercise's difficulty profile;
3. create reference and processed plans;
4. randomize presentation order where appropriate;
5. return the expected answer and safe, non-answer metadata;
6. let the existing controller mix, tokenize and cache the clips.

Use the application's cryptographically secure random source for answer
selection where the current exercise generators do so. Tests should be able
to supply deterministic choices without making production rounds predictable.

### A/B playback

Generalize the existing multiple-plan response instead of creating
exercise-specific endpoints. A comparative round should return:

```json
{
  "roundId": "...",
  "clips": [
    { "key": "A", "token": "..." },
    { "key": "B", "token": "..." }
  ]
}
```

The keys identify controls, not processing state. The browser must not receive
fields such as `reference`, `processed`, frequency, gain or pan.

The client should preload both clips and provide:

- Play A and Play B;
- keyboard shortcuts;
- immediate switching;
- replay without creating a new round;
- one shared loading and error state;
- accessible labels and focus behavior.

Browser playback must not apply the tested DSP: the server produces the
canonical clips so browsers hear the same round and cannot inspect client-side
processor settings.

## Exercise specifications

### Level Match

**Question:** Which signal is louder, or what is the approximate difference?

| Difficulty | Gain differences | Answer shape |
|---|---|---|
| Beginner | 6, 9 or 12 dB | A, B or same |
| Intermediate | 2, 3, 4 or 6 dB | A/B plus difference |
| Advanced | 0.5, 1, 1.5 or 2 dB | A/B plus difference |

Requirements:

- randomly place the changed version in A or B;
- include equal-level control rounds only if the UI offers "same";
- prevent clipping after positive gain;
- use several source types by Intermediate;
- explain the relationship between dB and perceived level in feedback.

### Stereo Position

**Question:** Where is the source in the stereo field?

Beginner positions:

```text
Left, Center, Right
```

Intermediate positions:

```text
L75, L25, Center, R25, R75
```

Advanced positions:

```text
L75, L50, L25, Center, R25, R50, R75
```

Requirements:

- define and test the pan convention (`-1` left, `0` center, `+1` right);
- use mono sources rendered to stereo so the position is controlled;
- preserve comparable perceived level across positions;
- do not claim reliable results on a mono output device;
- explain that this trains level-based pan, not every form of spatial
  localization.

### Guess Frequency

**Question:** Which center frequency received the EQ boost?

Initial frequency sets:

```text
Beginner:     100 Hz, 500 Hz, 1 kHz, 5 kHz, 10 kHz
Intermediate: 125 Hz, 250 Hz, 500 Hz, 1 kHz, 2 kHz, 4 kHz, 8 kHz
Advanced:     selected adjacent ISO third-octave bands
```

The first version uses a fixed positive gain and Q within each difficulty so
frequency is the only changing variable. Gain and Q become variable only
after the user has exercises that train them separately.

Requirements:

- offer reference and processed clips;
- keep gain and Q fixed for a selected difficulty;
- ensure the source has useful energy around every offered frequency;
- exclude bands too near Nyquist for the source sample rate;
- loudness-match the processed clip without erasing the intended spectral
  difference;
- return feedback with the frequency, broad perceptual region and a source-
  appropriate listening cue.

## Delivery plan

Each numbered slice should normally be one GitHub issue and one pull request.
Do not build all slices on one long-running branch.

### 1. Add the training-track foundation

**Work**

- add the Music, Audio and Mix track keys;
- assign every current exercise to Music without changing its behavior;
- make `ExerciseCatalog` group by track and category;
- add a track selector or separate route while keeping existing URLs valid;
- prepare separate learning-path catalogs, with the current path remaining
  the Music path;
- define explicit inclusion rules for games, daily challenges, placement and
  track-wide badges.

**Done when**

- all existing pages, scores, routines and tests behave as before;
- Music is selected for every existing exercise;
- the Audio track can render an empty state without fake exercises;
- localized track names exist in all three cultures;
- tests prevent an exercise from having an unknown track.

**Status:** implemented. The inclusion rules are Music-only for now: the
learning path (the current path is the Music path), the daily challenge pool,
the game modes (`GameModes.Playable`, which also gates placement) and the
Explorer and All-Rounder badges skip exercises of other tracks
(`TrainingTracks.IsMusic`). Slice 9 revisits them.

### 2. Add the licensed audio-source catalog

**Work**

- define source metadata and validation;
- select the initial mono and stereo clips;
- document license and attribution for every asset;
- add an ingestion or export script that produces the runtime format;
- add source versioning to generated-audio cache keys.

**Done when**

- CI can verify every catalog entry has a file, metadata and license;
- invalid sample rates, channel layouts, duration or clipping fail clearly;
- production can read sources without exposing their storage address;
- replacing a source cannot reuse output generated from its old bytes.

**Status:** done. It resolves decision 2: the source masters are committed
and ship inside the app image, so development, CI and production read the
same files with no storage step; Git history is their backup. It settles the
measurement half of decision 3 (BS.1770 integrated loudness); slice 7 still
chooses the tolerance for matched clips.

### 3. Introduce the processing-plan pipeline

**Work**

- separate source decoding, processing, encoding and storage concerns;
- add validated gain and constant-power pan processors;
- include processing plans in cache hashes;
- preserve the current note-mixing and pitch-shifting behavior;
- keep the audio endpoint's per-response random gain (`ClipVariation`,
  between -2 dB and 0 dB) from changing the level difference between A and
  B: give both clips of a round the same gain, or none;
- record structured diagnostics without logging answer parameters at a level
  exposed to users.

**Done when**

- unit tests verify gain and pan numerically with generated PCM signals;
- output is deterministic across repeated runs;
- invalid NaN, infinity, out-of-range pan and unsafe gain are rejected;
- current `AudioMixerService` tests still pass;
- the same plan reuses a cached blob and a changed parameter does not.

### 4. Add generic A/B rounds and controls

**Work**

- generalize controller responses for named multiple clips;
- add a reusable A/B Razor partial and JavaScript controller;
- preload both clips and switch without overlapping playback;
- retain opaque token authorization and replay behavior;
- add keyboard and screen-reader support.

**Done when**

- the response never identifies the processed clip;
- A and B can be switched repeatedly within the same round;
- stale, foreign-user and expired tokens still fail;
- Playwright covers mouse, keyboard, loading, replay and error behavior;
- existing one-clip and two-melody exercises are unchanged.

### 5. Ship Level Match

**Work**

- add the generator, validator, seed rows, views, scripts and resources;
- seed the Audio categories into existing databases: `SeedData` only inserts
  `ExerciseCategories()` into an empty table and exercises reference a
  category by its 1-based position in that list, so seeding must add missing
  categories by name and resolve ids by name;
- implement difficulty profiles and source selection;
- record the gain difference and source type in answer filter metadata where
  appropriate for weak-spots reporting;
- add the first Audio learning-path unit.

**Done when**

- every configured gain difference is generated and accepted correctly;
- A/B placement is balanced over deterministic test samples;
- positive gain cannot create clipped output;
- feedback names the correct signal and difference;
- dashboards, routines and ordinary practice record answers correctly.

### 6. Ship Stereo Position

**Work**

- add its generator, validator, UI and resources;
- add its Audio learning-path step;
- provide an output-device limitation note;
- store the tested pan position in answer metadata.

**Done when**

- numerical tests verify left, center and right channel gains;
- all configured positions can be generated and validated;
- center and edge positions stay within the loudness tolerance;
- the exercise works through practice and teacher routines.

### 7. Add parametric EQ and loudness matching

**Work**

- implement and test peaking biquad EQ;
- define safe frequency, gain and Q bounds;
- add the explicit loudness-matching stage;
- measure performance and memory use with the longest allowed source;
- version the processor output independently of exercise content.

**Done when**

- synthetic-signal tests measure the expected center-frequency gain and curve;
- invalid or unstable filter parameters are rejected;
- matched clips meet the documented loudness tolerance;
- processing stays within the request-time and memory budgets;
- Linux container output is equivalent within tolerance to test output.

### 8. Ship Guess Frequency

**Work**

- add its generator, validator, A/B UI and resources;
- define beginner, intermediate and advanced frequency profiles;
- restrict each source to frequencies it can meaningfully represent;
- add educational frequency-region feedback;
- complete the Foundations Audio learning-path unit.

**Done when**

- every offered answer can be generated from more than one source;
- the target band is measurably changed and other bands remain within the
  filter's expected response;
- loudness is not a reliable answer cue;
- all three exercises contribute to Audio-track progress;
- integration and Playwright tests cover a complete Audio practice session.

### 9. Integrate progress and engagement

**Work**

- show progress by track and Audio category;
- decide which Audio exercises enter daily challenges and game modes;
- keep Music placement independent from Audio progress;
- add track-aware weak-spots analysis;
- add only achievements whose art, rules and localization are ready.

**Done when**

- Music percentages do not change merely because Audio exercises were added;
- reports can distinguish Music and Audio answers;
- routines can intentionally include exercises from either track;
- track-wide badges do not silently become harder for existing users;
- personal-data export and deletion include any new stored fields.

### 10. Expand the track

Implement, in order:

1. Boost or Cut;
2. Guess the Q;
3. Compression;
4. Attack and Release;
5. Reverb;
6. Find the Mix Problem.

Each exercise must reuse shared processors and A/B controls. Do not add a
processor solely inside one exercise generator.

## Testing strategy

### DSP unit tests

Generate small known PCM signals in memory and assert measurable behavior:

- gain in dB converts to the expected amplitude ratio;
- pan produces the expected left and right gains;
- EQ reaches the requested center gain within tolerance;
- processors reject invalid parameters;
- processor order changes output and participates in the cache key;
- matched loudness meets the agreed tolerance;
- output never contains NaN or infinity.

Do not rely only on snapshots of encoded WAV bytes: numerical tests should
describe the audio property being protected.

### Application unit tests

- every seeded exercise has a track, category, validator and playback plan;
- every Audio learning-path step references a seeded Audio exercise;
- generators cover every configured difficulty option;
- validators reject malformed or out-of-profile answers;
- safe metadata never contains expected-answer fields.

### Integration tests

- Audio exercises seed and localize correctly;
- rounds create the expected number of opaque tokens;
- token ownership, expiry and single-round validation remain enforced;
- scores and filter metadata are recorded;
- routines can assign and complete Audio exercises;
- account export and deletion handle any new data.

### End-to-end tests

- select the Audio track and start each Foundations exercise;
- switch A/B, replay, answer and receive feedback;
- use keyboard controls;
- verify no horizontal overflow at supported viewports;
- verify a complete Audio learning-path step;
- test one error response without showing success-shaped UI.

### Listening quality review

Automated tests cannot prove that examples are pedagogically useful. Before
releasing an exercise, perform a documented listening review with:

- headphones and speakers;
- at least two source types;
- all difficulty extremes;
- mono playback where relevant;
- checks for clicks, clipping, unintended cues and abrupt level changes.

## Operational requirements

- Generated variants remain short-lived cached blobs, not permanent assets.
- Source masters have backup and documented ownership.
- Blob lifecycle policies must not delete source assets.
- Processing failures log the exercise, source key and processor type, but not
  secrets or user-identifying information.
- Metrics should distinguish source loading, DSP time, encoding, upload and
  cache reuse.
- Set explicit maximum source duration, sample rate and channel count before
  allocating output buffers.
- Reassess Container App CPU and memory after EQ and again before compression
  or reverb.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Adding exercises changes existing badges and completion percentages | Make progress and badge rules track-aware before the first public Audio exercise |
| Loudness reveals the processed answer | Require explicit matching and numerical tolerance tests |
| Different browsers produce different DSP | Process canonical exercise audio on the server |
| Source material is unlicensed or later removed | Keep a reviewed source catalog with license and checksum |
| DSP increases request latency and compute cost | Cache by complete plan, cap source duration and measure each processor |
| A/B switching has audible gaps | Preload both clips and use one reusable playback controller |
| Technical parameters leak through JSON or filenames | Send only opaque tokens and whitelisted metadata |
| One large refactor destabilizes musical exercises | Deliver the foundation in isolated PRs with regression tests |
| Advanced users memorize a small source set | Grow the catalog and rotate sources before adding adaptive training |

## Decisions required before implementation

Resolve these in the first GitHub issue:

1. ~~whether `TrainingTrack` is persisted on `Exercise` or supplied by a
   code-first catalog with a migration only when relational queries require
   it;~~ **Resolved in slice 1:** code-first `TrainingTracks` table keyed by
   category, no migration;
2. where licensed source masters live and how they reach development, CI and
   production;
3. the loudness measurement and tolerance used for matched A/B clips;
4. whether the first release is visible as soon as Level Match ships or only
   after all three Foundations exercises are ready (slice 1 default: the
   Audio tab appears as soon as an Audio exercise is seeded);
5. which current engagement features include Audio exercises at launch
   (slice 1 default: none; daily challenge, games, placement and the
   all-exercises badges stay Music-only until slice 9).

## First milestone

The first milestone is complete when a production user can:

1. select Audio Ear Training separately from Music Ear Training;
2. complete Level Match, Stereo Position and Guess Frequency;
3. switch reference and processed audio without answer leakage;
4. receive localized educational feedback;
5. see Audio progress without changing existing Music progress;
6. use the exercises in a teacher routine;
7. replay deterministic, unclipped and correctly matched audio;
8. pass the repository's unit, integration, real-SQL and Playwright suites.
