# Backlog

What is left to do on Academia Auditiva, in priority order. Written on
2026-10-02, when `master` was at `07d6776` and production ran `c44fafb`. Line
numbers refer to `07d6776`, except in item 2 (`ef42bfa`) and in the items added
on 2026-10-06: item 3 (the merge of #113), item 4 (`47f0a6d`) and the
[exercises](#exercises) (`21f9dff`). Each item says why it matters, where to
look, what to do and when it is done. New ideas go to GitHub issues.

## Where things stand

The modernization plan is done (#47 to #83):

- **Platform**: .NET 10 with central package management, private networking
  for Key Vault, SQL and Blob storage, custom domains, OpenTelemetry, and CI
  with real SQL Server and Playwright (#47, #48, #51, #52, #59).
- **Look and languages**: the visual identity, eight illustrations on the
  home page, and full en-US, pt-BR and fr-CA (#53, #56, #70, #82, #101).
- **Exercises**: 24 exercises on piano, guitar and violin, plus Explore and
  free practice (#58, #60, #75 to #78). The server times each answer, and the
  dashboard works out every figure from the answers themselves, so it shows
  real practice time and real error counts (#99, #100). Compare 2 melodies
  plays short melodies in one key, as many notes as the student picks, and
  every round that expects "different" leaves out a note (#120).
  GuessFunction plays a cadence in its key before the chord, and in minor
  keys takes V and vii° from the harmonic minor (#122). GuessDegree plays
  the same cadence, then a note, and asks for its degree in the key: 1 to 7,
  or all twelve notes at the chromatic level (#123). GuessProgression plays
  the cadence before a chord progression and asks for its name or, at the
  dictation level, the degree of each chord after the tonic (#124). The
  dictations count in on the piano and play at the tempo the student picks,
  and RhythmDictation has four more levels: dotted notes, sixteenths,
  syncopation and 6/8 in dotted quarters, which melodies in 6/8 use too
  (#125). GuessMeter plays twelve beats, as clicks or as an oom-pah of bass
  and chords, and asks whether they go in two, three or four (#126).
  GuessRhythmPattern plays a two-bar rhythm at a dictation level and asks
  which of four written rhythms, that differ in one or two beats, it was
  (#127). RhythmTap plays such a rhythm between two count-ins, and the
  student taps it back on a pad or the space bar. The server matches the
  taps to the notes at the tempo and from the start that fit them best, so
  a steady delay of the headphones doesn't count, and a single tap off time
  is the only note marked wrong (#128). GuessQuality plays the five seventh
  chords, and sus2, sus4, 6 and add9, in chord types the student picks; the
  learning path asks for the triads and the sevenths (#129).
- **Engagement**: XP, levels, streaks, 18 badges with their own medal art (15
  more wait for theirs), the learning path, the tutorial and the daily
  challenge (#72 to #74, #94, #97).
- **Accounts**: privacy policy, data export and deletion, admin lock, unlock
  and delete, and lockout after failed sign-ins with a rate limit on the
  account forms (#57, #65, #80, #81, #93).
- **Classrooms**: a routine works like a test. Each item takes exactly the
  questions the teacher set, with the teacher's filters, one at a time; only
  the answers given in the routine count, and the due date closes it unless
  the teacher accepts late work (#111). Once assigned, its exercises can't
  change, nor can a student's adjustments to an item they have started; the
  teacher duplicates the routine to change a copy (#112). Teachers get reports
  per routine, per class and per student, made only from the answers given in
  their own routines; a student's other practice stays private (#113).

E-mail is [on](#e-mail): production sends through Resend (#89), and every
e-mail has the site's layout and a plain-text version (#92). There are no open
pull requests and no open CodeQL or Dependabot alerts. The open issues are
#16, #31 and the two in [Classrooms and teachers](#classrooms-and-teachers)
(#108 and #109).

## How we work

- One pull request per item, on a branch off `master`; a git worktree keeps
  the main checkout free. Details are in [CONTRIBUTING.md](../CONTRIBUTING.md).
- CI must pass before merging: build and tests (including real SQL Server
  through Testcontainers), Playwright E2E, CodeQL, Trivy, Gitleaks and Bicep
  validation. Then squash-merge and delete the branch.
- CD deploys only when `AcademiaAuditiva/**`, `infra/**`, `Dockerfile`,
  `Directory.Build.props`, `Directory.Packages.props`, `global.json` or
  `.github/workflows/cd.yml` change. After a deploy,
  `https://academiaauditiva.com/health/ready` must report the merge commit as
  its `version`.
- Run the app with [Run-Locally.md](Run-Locally.md); audio needs Azurite
  (`scripts/local-audio.ps1`).
- Every user-facing text exists in en-US, pt-BR and fr-CA. It goes in the three
  `.resx` files, except the privacy policy, which has one view per culture.
- Maintained docs are in English, except `Pedagogia-Exercicios.md` and
  `FiltrosPorExercicio.md`.
- Never commit secrets, tenant IDs or subscription IDs. If your NuGet feed
  lacks a pinned version, pin it locally and leave that change out of commits.

## Next up

### 1. Settle the Essentia.js license (AGPL-3.0)

**Why.** Sight-singing (`SolfegeMelody`) detects the sung pitch with
Essentia.js. Its files carry the AGPL-3.0 notice, while the app is MIT. The
AGPL attaches source-sharing conditions to distributing the code, and serving
these files to browsers may count. The repo does not yet say which upstream
release the files are or where its source is. This is not legal advice; if in
doubt, ask someone qualified.

**Where.**

- `AcademiaAuditiva/wwwroot/js/dist/`: `essentia.js-core.min.js`,
  `essentia-wasm.web.js` and `essentia-wasm.web.wasm`, 2.15 MB together. They
  came in with `c5009e9` and carry no version number.
- Loaded only by `AcademiaAuditiva/Views/Exercise/SolfegeMelody.cshtml:72-73`.
- Used in `AcademiaAuditiva/wwwroot/js/Exercises/SolfegeMelody.js:224-303`
  (`PitchMelodia`, `PitchContourSegmentation`).

**What.** Pick one:

- **Keep it.** Identify the upstream release, put its license text and a link
  to that release's source next to the files, and link both from the readme.
- **Replace it** with a permissively licensed pitch detector, such as a small
  YIN or McLeod implementation of our own. That also takes 2.15 MB off the
  page.

**Done when.** Either the notice and source link are in place, or Essentia.js is
gone and sight-singing still recognizes sung notes. Either way, the readme's
third-party table matches.

### 2. Create the contato@ inbox

**Why.** `contato@academiaauditiva.com` is the only address the site gives for
reaching us, but the domain has no MX record, so mail to it bounces.

- The privacy policy sends every privacy request there and promises an answer
  within the time limits set by law.
- The Lockout page sends locked-out users there. Since #93, wrong passwords and
  authenticator codes lock accounts too, for 15 minutes; the page also offers a
  password reset, which ends that lockout but not an admin's lock.

**Where.**

- `AcademiaAuditiva/Views/Home/Privacy.cshtml:37`, `:208` and `:226`, and the
  same lines of `Privacy.pt-BR.cshtml` and `Privacy.fr-CA.cshtml`.
- `AcademiaAuditiva/Areas/Identity/Pages/Account/Lockout.cshtml`, on the
  `Lockout.Contact` line.
- `Tests/AcademiaAuditiva.IntegrationTests/PrivacyPageTests.cs:28` and
  `Tests/e2e/tests/academia-auditiva.spec.ts:95` check the address.
- DNS: the `academiaauditiva.com` zone is in another tenant, and the apex has
  no MX or TXT record. `infra/scripts/configure-dns.ps1` writes only the
  custom-domain records (`A/@`, `CNAME/www` and their `asuid` TXT records); no
  script holds the Resend records.

**What.**

- **Owner:** choose where the mail goes. Either:
  - a mailbox at an e-mail host (for example Microsoft 365, Google Workspace or
    Zoho Mail), which can also reply as `contato@`;
  - or forwarding to an inbox you already read (for example ImprovMX or Forward
    Email). Gmail can then reply as `contato@` through "Send mail as", with
    Resend's SMTP server and a Resend key of its own.
- Add the host's MX records at the apex, and the verification record it asks
  for. If it sends as `contato@`, add its SPF and DKIM records too: without
  them its mail fails DMARC, and once the policy is `quarantine` (see
  [E-mail](#e-mail)) that mail goes to spam.
- Leave the Resend records (`send`, `rsend`, `resend._domainkey`) and `_dmarc`
  as they are.
- If you pick another address, change the views and tests above.

**Done when.** Mail from an outside address reaches the inbox. If the inbox
replies as `contato@`, its replies pass DMARC.

## Classrooms and teachers

The owner's notes of 2026-10-06, one issue each. The first two are done:
routines that work like a test (#106, in #111 and #112), and teacher reports
made only from the answers given in routines (#107, in #113). #111 tied
answers to routines, which item 4 builds on; item 3 can go at any time.

### 3. Assign to a class or chosen students, and e-mail them (#108)

**Why.** Assigning a routine saves the assignment and shows a toast, and
nobody hears about it until they open My Training
(`AcademiaAuditiva/Areas/Teacher/Controllers/RoutinesController.cs:365-418`).
The form offers a whole class or one student
(`AcademiaAuditiva/Areas/Teacher/Views/Routines/Assign.cshtml:16-36`), so
giving a routine to three students takes three assignments, with three
reports.

**Rules agreed with the owner.** A routine goes to the whole class by default;
the teacher can instead tick some of its students. The students it goes to get
an e-mail.

**What.**

- The form: pick a class, then "Whole class" or tick students. One assignment
  holds the ticked students (a join table); existing one-student assignments
  keep working.
- A student who joins the class later still gets its whole-class routines, as
  today (`AcademiaAuditiva/Services/Routines/RoutineRounds.cs:124-129`), but no
  e-mail.
- The reports (#113) take a class assignment's students from the class's
  current members, and a one-student assignment's from that student
  (`AcademiaAuditiva/Areas/Teacher/Services/RoutineReports.cs:36-38`, `:65-73`
  and `:104-107`). An assignment to ticked students must count only those of
  them still in the class.
- `EmailComposer.RoutineAssignedAsync`, like `ClassroomInviteAsync`
  (`AcademiaAuditiva/Services/Email/EmailComposer.cs:40`): the teacher, the
  routine, the due date and a button to My Training. Texts in the three `.resx`
  files.
- E-mails use the culture of the request that sends them
  (`EmailComposer.cs:12`), which here is the teacher's. Save each user's
  language when they sign up and when they switch, and write each student's
  e-mail in theirs.
- Send from a background queue after the response, so a class of 30 doesn't
  hold up the page. A failed send never undoes the assignment.
- Students can turn these e-mails off on their account page, and the e-mail's
  footer says how.
- Add these e-mails to the privacy policy's list (`Privacy.cshtml:82`, and the
  same line in pt-BR and fr-CA).
- Each assignment sends one e-mail per student, so check the Resend plan's
  quota first (see [Watch](#watch)): when it runs out, sign-ups stop getting
  their confirmation e-mail.

**Done when.** Assigning to a class, or to ticked students, e-mails exactly
those students, each in their own language, and the reports count exactly
them; a failed send still saves the assignment; integration tests cover who
gets the e-mail.

### 4. Notifications on the site (#109)

**Why.** The site has no notifications: a student learns about a routine only
on My Training, and a teacher learns about progress only in the reports.

**Proposal.**

- A `Notification` table: the user, the kind, the routine, assignment or class
  it's about, and when it was created and read. The texts come from the `.resx`
  files when shown, so they follow the reader's language.
- A bell with the unread count in the top bar
  (`AcademiaAuditiva/Views/Shared/_Layout.cshtml` and
  `AcademiaAuditiva/Areas/Teacher/Views/Shared/_TeacherLayout.cshtml`), and a
  page that lists them. Opening one marks it read. The count updates when a
  page loads; no real-time push at first.
- First events. For students: a routine is assigned, or is due tomorrow and
  unfinished. For teachers: a student finished a routine, or accepted an
  invite.
- Item 3's e-mail and this notification come from the same event, so build the
  notifier once.
- Notifications go into the data export and the account deletion
  (`AcademiaAuditiva/Services/PersonalDataService.cs:136` and `:49`), and are
  deleted after 90 days.

**Decide first.** Which events, and whether "due tomorrow" is worked out when
the page loads or needs a daily job.

**Done when.** The bell shows the chosen events in the three languages, and
they appear in the data export.

## Exercises

From the brainstorm of 2026-10-06. There are 24 exercises, four of them on
rhythm (RhythmDictation, GuessMeter, GuessRhythmPattern and RhythmTap), but
the Games and Misc categories have none
(`AcademiaAuditiva/Data/SeedData.cs:34-35`). GuessFunction (#122),
GuessDegree (#123) and GuessProgression (#124) play a cadence to set the key
before the question. IntervalMelodico could play it too: its melody, 8 to 31
notes, has to set the key on its own (`MusicTheoryService.cs:1127`), and
GuessDegree could later ask for two or three notes in a row. RhythmDictation
counts in and has levels up to 6/8 (#125), GuessMeter asks for the meter
(#126), GuessRhythmPattern for the rhythm (#127), and RhythmTap has the
student tap one back (#128).

| Item | Kind | Effort | Needs |
| --- | --- | --- | --- |
| 5. The top note | New exercise | Small | – |
| 6. Harmonic intervals | New filter | Small | – |
| 7. Which note changed? | New exercise | Small | – |
| 8. Singing exercises | New exercises | Medium | Item 1 |
| 9. In tune or not? | New exercise | Medium | – |
| 10. Game modes | Games category | Medium to large | – |

**Adding an exercise.** A new exercise needs all of this, and tests that go
through every seeded exercise check much of it:

- Its row in `SeedData.cs`. Seeding adds the missing exercises, by name, and
  updates the others (`SeedData.cs:959-979`), so it reaches production with
  the deploy.
- A case in `MusicTheoryService.GenerateNoteForExercise`, and one in
  `ExercisePlaybackPlanner.Plan`, which throws on an exercise it doesn't know
  (`AcademiaAuditiva/Services/Audio/ExercisePlaybackPlanner.cs:186-188`).
- An `IExerciseValidator`, registered in `AcademiaAuditiva/Program.cs:230-248`.
- `Views/Exercise/<Name>.cshtml` and `wwwroot/js/Exercises/<Name>.js`.
- In the three `.resx` files: its name
  (`Tests/AcademiaAuditiva.IntegrationTests/LocalizedNamesTests.cs:31-55`),
  and `Exercise.<Name>.Instructions` and its tips, which otherwise fall back to
  the seed's Portuguese
  (`AcademiaAuditiva/Views/Exercise/_ExerciseInstructions.cshtml:4-16`).
- Exactly one step in
  `AcademiaAuditiva/Services/LearningPath/LearningPathCatalog.cs`
  (`Tests/AcademiaAuditiva.UnitTests/LearningPathServiceTests.cs:17-23`).
- An entry in `MusicTheoryService.UsesNoteRange`
  (`AcademiaAuditiva/Services/MusicTheoryService.cs:183-192`), only if its
  rounds follow the octave range
  (`Tests/AcademiaAuditiva.UnitTests/NoteRangeFilterTests.cs:92`).

Each new exercise, unless it needs a microphone, also raises the bar for three
badges that stay hidden until their art is ready (#16): `explorer` asks for
every exercise
(`AcademiaAuditiva/Services/Gamification/BadgeRules.cs:80`), `total_mastery`
for the whole learning path (`:103`), and `all_rounder` for 10 answers in
every category that has an exercise (`:109` and `:330-333`), so the first game
adds a category. Badges already earned are kept (`BadgeRules.cs:39`).

**Left out on purpose.** Reading drills without sound, since this is an
ear-training site; the frequency and mixing module
(`docs/archive/MapaDoProjeto.md:150`); a melodic contour exercise
(`docs/Pedagogia-Exercicios.md:45`), since HigherOrLower and the dictations
cover it between them; and ties and triplets in the dictations (#125), which
would each need a duration label of their own in the generator, the staff
editor and the renderer.

### 5. The top note of a chord

**Why.** GuessInversion asks which note is in the bass. Nothing asks which one
is on top (the root, the third or the fifth), the note that carries a melody
over chords. The other half of this item, the seventh, sus, 6 and add9 chords
in GuessQuality, shipped in #129.

**What.** The top note, as a mode of GuessInversion or an exercise of its own:
the root stays in the bass while the top changes, in open or four-part
voicings. With the close triads GuessInversion plays
(`MusicTheoryService.cs:922-967`), the top note follows from the inversion, so
it needs voicings of its own.

**Done when.** The top note can be asked, and its texts exist in the three
languages.

### 6. Harmonic intervals

**Why.** GuessInterval and GuessFullInterval always play one note after the
other (`ExercisePlaybackPlanner.cs:131-139`). Two notes at once, a harmonic
interval, is what students hear in two-part music and inside chords.
`docs/FiltrosPorExercicio.md:39` planned the filter.

**What.** A filter on both: melodic as today, harmonic, or either at random.
Harmonic plays both notes at the same time, in the octaves written, as
`WrittenChord` does (`ExercisePlaybackPlanner.cs:244`). `Chord` won't do: on
the guitar it strums a shape of four to six strings, in octaves of its own,
which changes the interval. The direction filter doesn't apply to harmonic
intervals. HigherOrLower shares the planner case and stays melodic.

**Done when.** Both exercises play harmonic intervals when chosen, a planner
test checks the start times, and the new texts exist in the three languages.

### 7. New exercise: which note changed?

**Why.** Compare 2 melodies asks only whether two melodies differ: the second
one leaves out a note or not (#120). The next step is to say where and how:
which note changed, and whether it went up or down
(`docs/Pedagogia-Exercicios.md:31`, `docs/archive/MapaDoProjeto.md:20`).

**What.** The melodies of Compare 2 melodies (`GenerateComparisonMelody` in
`MusicTheoryService.cs`), with one note moved by a step or a leap. The
student picks its position (buttons 1 to n) and up or down. It could be a
level of Compare 2 melodies instead, but its answer buttons are different.

**Done when.** It follows the list above.

### 8. Singing exercises

**Needs [item 1](#1-settle-the-essentiajs-license-agpl-30).** They would
detect the sung pitch as sight-singing does (`detectNotes` in
`AcademiaAuditiva/wwwroot/js/Exercises/SolfegeMelody.js:266`), so settle
Essentia.js first. If it is replaced, the new detector serves them all.

**What.**

- Sing the note you hear.
- Sing an interval above or below a given note
  (`docs/FiltrosPorExercicio.md:128`, `docs/archive/MapaDoProjeto.md:25`).
- Sing back a short melody (`docs/archive/MapaDoProjeto.md:39`).

Microphone exercises stay out of the learning path, the daily challenge, and
the badges that ask for every exercise or category. Add each one to
`MicrophoneExercises` (`AcademiaAuditiva/Services/MicrophoneExercises.cs:9`),
which the daily challenge and those badges check
(`AcademiaAuditiva/Services/DailyChallenge/DailyChallengeRules.cs:118`,
`BadgeRules.cs:315` and `:331`). The learning path just has no step for them,
and its test names SolfegeMelody (`LearningPathServiceTests.cs:22`); make the
test use `MicrophoneExercises`.

**Done when.** Each one follows the list above, except the learning-path step,
and recognizes sung notes as reliably as sight-singing does.

### 9. New exercise: in tune or not?

**Why.** Violinists and singers tune by ear, and nothing trains it: every
sample is in tune, one per semitone.

**What.** A reference note, then the same note in tune, sharp or flat. The
levels set how far off: 50, then 25, then 10 cents. The mixer needs to shift a
sample's pitch by resampling it as it mixes (`AudioMixerService.cs:161-167`),
with a new field on `MixInput`.

**Done when.** It follows the list above, and a mixer test checks the shifted
pitch.

### 10. Game modes

**Why.** The Games and Misc categories are empty. Modes that reuse the rounds
of existing exercises add variety without new music code.

**What.**

- **Sprint:** as many right answers as possible in 60 seconds
  (`docs/Pedagogia-Exercicios.md:63`).
- **Sudden death:** rounds until the first mistake
  (`docs/archive/MapaDoProjeto.md:96`).
- **Weak spots:** rounds built from what the student misses most, from
  `ScoreSnapshots` and their recorded filters (#96;
  `docs/Pedagogia-Exercicios.md:65`).
- **Placement test:** a short mixed test that suggests where to start on the
  learning path (`docs/archive/MapaDoProjeto.md:88`).

**Decide first.** Whether they count for XP, streaks and badges, and whether
they are exercises, which then follow the list above and bring the Games
category into `all_rounder`, or pages of their own.

**Done when.** The chosen modes are live in the three languages.

## E-mail

**Today.** Production sends e-mail through Resend's SMTP server (#89). The five
`Smtp--*` Key Vault secrets hold the settings (see the
[inventory](Security.md#secret-inventory-production)). The password is a Resend
API key with sending access to `academiaauditiva.com` only.

- DNS: DKIM (`resend._domainkey`) and the `send` and `rsend` CNAMEs (SPF and
  bounces) are verified in Resend. DMARC is `p=none`.
- Sign-up no longer shows the confirmation link on screen. Forgot password,
  resend confirmation, change e-mail and teacher invites send real mail.
- A failed send never breaks a page. The account pages answer as usual
  (`EmailSenderExtensions.TrySendEmailAsync`), the account e-mail page says the
  e-mail could not be sent, and an invite shows its link.
- Register, forgot password and resend confirmation share the per-IP limit on
  the account forms (#93). It slows a flood from one address but doesn't cap
  how much mail goes out in a day, so keep an eye on the quota (see
  [Watch](#watch)).
- Every e-mail has one layout (#92): the logo, a button, the link written out,
  and a footer that says why it was sent. `EmailComposer` renders
  `Services/Email/EmailLayout.razor` with `HtmlRenderer` and writes the same
  texts as the plain-text version, in the culture of the page that sends it.
  A new e-mail gets a method there and its texts in the three `.resx` files.
- Without the settings (locally and in CI), the app skips sending and shows the
  confirmation link, as before.

**Still to do.**

- The `contato@` inbox ([item 2](#2-create-the-contato-inbox)).
- Add a DMARC report address (`rua=`), and once the reports are clean, move
  from `p=none` to `quarantine`.

**Done when.** DMARC reports arrive and the policy is `quarantine`.

## Later, or needs a decision

### #16: the art of the hidden badges

#72 shipped 18 badges, and #94 replaced their icons with medal art. Fifteen
more have their rules, tests and texts in the three languages, but stay hidden
(`IsAvailable: false` in
`AcademiaAuditiva/Services/Gamification/BadgeCatalog.cs`) until their art is
ready: the eight seeded with #72, whose rules now use the recorded filters
(#96), the daily challenge (#97) and the learning path, and seven new ones
(longer streaks, 100 sessions, a perfect session, all categories, night owl
and early bird). [badges.md](../badges.md#hidden-badges) lists their rules and
has an art prompt for each.

**What to do.** For each badge, generate its art, export it with
`python scripts/export-badge-art.py <key> <png>` and remove its
`IsAvailable: false`. Players who already meet a released badge's rule get it
with their next answer. When all of them are out, raise
`BadgeRules.BadgeCollectorThreshold` from 15 to 20 and change the 15 in the
`Badge.badge_collector.Description` texts (3 resx files), its seed row and its
prompt in badges.md.

**Done when.** No badge is hidden and the collector badge asks for 20 others.
The owner approves any artwork.

### #31: Microsoft Entra External ID

The issue proposes letting Entra handle identity. Today ASP.NET Core Identity
handles local accounts, two-factor, the admin lock, and data export and
deletion.

- **Gains.** Moving would bring hosted e-mail verification, MFA and social
  sign-in.
- **Costs.** It would touch `Areas/Identity`, `LockoutAwareSignInManager`, the
  admin users page and the personal-data code. Existing accounts would need a
  migration plan.

Wait for the owner's decision before starting.

## Owner-only actions

These are outside the repo:

- Change the bootstrap admin password in production, turn on two-factor for
  that account, and delete any local copy of the initial password.
- Review who holds the Admin role.
- Confirm in the Meta console that the Facebook App Secret was reset: an older
  one is in the public git history (see the
  [runbook](Security.md#rotate-facebook-appsecret)).
- Choose and create the `contato@` inbox, and add its DNS records (see
  [item 2](#2-create-the-contato-inbox)).

## Watch

- The weekly Gitleaks run (Mondays at 06:00 UTC) scans the whole history.
  - Its scheduled runs in June and July 2026 failed on password hashes in the
    2025 migrations.
  - `.gitleaksignore` covers them now, and a local full-history scan was clean.
  - If it fails again, look at the new finding before allowlisting its
    fingerprint.
- Merge Dependabot pull requests through the same cycle.
- Resend → Emails: bounces and spam complaints hurt the domain's reputation.
  Once the plan's sending quota runs out, sign-ups stop getting their
  confirmation e-mail.
