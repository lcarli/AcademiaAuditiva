# Backlog

What is left to do on Academia Auditiva, in priority order. Written on
2026-10-02, when `master` was at `07d6776` and production ran `c44fafb`; line
numbers refer to `07d6776` (`ef42bfa` in item 3, `47f0a6d` in items 4 and 6,
added on 2026-10-06, and the merge of #112 in item 5). Each item says why it
matters, where to look, what to do and when it is done. New ideas go to GitHub
issues.

## Where things stand

The modernization plan is done (#47 to #83):

- **Platform**: .NET 10 with central package management, private networking
  for Key Vault, SQL and Blob storage, custom domains, OpenTelemetry, and CI
  with real SQL Server and Playwright (#47, #48, #51, #52, #59).
- **Look and languages**: the visual identity, eight illustrations on the
  home page, and full en-US, pt-BR and fr-CA (#53, #56, #70, #82, #101).
- **Exercises**: 19 exercises on piano, guitar and violin, plus Explore and
  free practice (#58, #60, #75 to #78). The server times each answer, and the
  dashboard works out every figure from the answers themselves, so it shows
  real practice time and real error counts (#99, #100).
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
  teacher duplicates the routine to change a copy (#112).

E-mail is [on](#e-mail): production sends through Resend (#89), and every
e-mail has the site's layout and a plain-text version (#92). There are no open
pull requests and no open CodeQL or Dependabot alerts. The open issues are
#16, #31 and the three in [Classrooms and teachers](#classrooms-and-teachers)
(#107 to #109).

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

### 1. Show invite links while e-mail is off

**Lower priority since #89.** Production now e-mails invites, and when sending
fails the teacher already sees the link. This only matters where e-mail is off:
locally, in CI, or if production turns it off again.

**Why.** Without SMTP, the e-mail sender logs a warning and returns
(`AcademiaAuditiva/Services/EmailSender.cs:25-34`). The teacher still sees
"invite sent" (`AcademiaAuditiva/Areas/Teacher/Controllers/MembersController.cs:134`)
although nothing went out. The pending-invite list
(`AcademiaAuditiva/Areas/Teacher/Views/Classrooms/Details.cshtml:65-94`) has no
link to copy, so the student cannot join. The link only appears when sending
throws (`MembersController.cs:127-131`).

**What.**

- When e-mail is off, say so and show the accept link with a copy
  button, both after inviting and in the pending list.
  `SmtpOptions.IsConfigured` tells whether e-mail is on;
  `RegisterConfirmation.cshtml.cs` already uses it.
- Keep the e-mail path as it is.
- Add the new texts in the three cultures.

**Done when.** Without SMTP, a teacher copies the link and the student joins.
Integration tests cover both modes.

### 2. Settle the Essentia.js license (AGPL-3.0)

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
- Used in `AcademiaAuditiva/wwwroot/js/Exercises/SolfegeMelody.js:244-323`
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

### 3. Create the contato@ inbox

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

The owner's notes of 2026-10-06, one issue each. The first, routines that work
like a test (#106), is done (#111, #112). #111 tied answers to routines, which
items 4 and 6 build on; item 5 stands alone and can go at any time.

### 4. Teacher reports from routines only (#107)

**Why.** The teacher needs reports per routine (overall and per student), per
class (overall and per student) and a page per student. Today those pages read
all of a student's practice:

- `DashboardController.Classroom` sums `ScoreAggregates` and counts
  `ScoreSnapshots` for every member
  (`AcademiaAuditiva/Areas/Teacher/Controllers/DashboardController.cs:42-53`),
  and `Student` shows accuracy per exercise from all of them (`:105-110`). A
  student in two teachers' classes shows each teacher everything.
- The privacy policy says so (`AcademiaAuditiva/Views/Home/Privacy.cshtml:94`,
  and the same line of `Privacy.pt-BR.cshtml` and `Privacy.fr-CA.cshtml`).
- There's no report per routine, and no tests cover these pages.

**Rule agreed with the owner.** A teacher sees only the answers given in the
routines they assigned: never the student's practice outside them, free or
not, and never another teacher's routines.

**What.** Build on the answers #111 ties to routines (`ScoreSnapshot`'s
`RoutineAssignmentId` and `RoutineItemId`), and drop the queries on
`ScoreAggregates` and on all of a student's `ScoreSnapshots`.

- **Routine** (one assignment): how many students finished, are under way,
  haven't started or are late, and accuracy and time per answer for each item.
  Per student: progress and accuracy per item, when they finished, and whether
  it was late.
- **Class:** every routine assigned to the class or to some of its students,
  with completion and accuracy. Per student: routines finished out of those
  assigned, accuracy and last routine activity.
- **Student:** their routines from this teacher, item by item. Nothing from
  other teachers or from practice.
- A student removed from a class drops out of its reports.
- Update "Your teachers" in the privacy policy, in the three cultures, and the
  home page's "Reports" text (`Home.Roles.Teacher3.Desc`, "by class, by student
  and by exercise").
- Stopgap, if this item has to wait: remove the practice figures from both
  pages first, in a small pull request of their own.

**Done when.** Every figure a teacher sees comes from their own routines, the
privacy policy says so in the three languages, and integration tests show that
a teacher doesn't see practice outside routines, another teacher's routines or
students outside their classes.

### 5. Assign to a class or chosen students, and e-mail them (#108)

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
those students, each in their own language; a failed send still saves the
assignment; integration tests cover who gets the e-mail.

### 6. Notifications on the site (#109)

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
- Item 5's e-mail and this notification come from the same event, so build the
  notifier once.
- Notifications go into the data export and the account deletion
  (`AcademiaAuditiva/Services/PersonalDataService.cs:136` and `:49`), and are
  deleted after 90 days.

**Decide first.** Which events, and whether "due tomorrow" is worked out when
the page loads or needs a daily job.

**Done when.** The bell shows the chosen events in the three languages, and
they appear in the data export.

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

- The `contato@` inbox ([item 3](#3-create-the-contato-inbox)).
- Add a DMARC report address (`rua=`), and once the reports are clean, move
  from `p=none` to `quarantine`.

**Done when.** DMARC reports arrive and the policy is `quarantine`.

## Later, or needs a decision

### Facebook sign-in (off in production)

**Today.** Facebook sign-in is registered only when `Facebook:AppId` and
`Facebook:AppSecret` are both set (`AcademiaAuditiva/Program.cs:252-263`). In
production the `Facebook--AppId` Key Vault secret is a disabled placeholder, so
no Facebook button appears.

**Before turning it on.**

- **Owner:**
  - An older App Secret is in the public git history. `docs/Security.md` lists
    it as rotated; confirm that in the Meta console
    ([runbook](Security.md#rotate-facebook-appsecret)).
  - Set the OAuth redirect URIs (`https://academiaauditiva.com/signin-facebook`,
    plus the `www` host).
  - Store both values with `seed-keyvault.ps1`.
- **Code:**
  - `AccessDeniedPath = "/AccessDeniedPathInfo"` (`Program.cs:261`) is a 404.
    Send the user back to the login page instead.
  - Handle `OnRemoteFailure` (a cancel or a Meta error) with a localized
    message.

**Done when.** A test Facebook account signs in on production, and cancelling
returns to the login page with a message.

If #31 goes ahead, Entra External ID could provide social sign-in instead.

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
  sign-in, which covers much of the Facebook work.
- **Costs.** It would touch `Areas/Identity`, `LockoutAwareSignInManager`, the
  admin users page and the personal-data code. Existing accounts would need a
  migration plan.

Wait for the owner's decision before starting.

## Owner-only actions

These are outside the repo:

- Change the bootstrap admin password in production, turn on two-factor for
  that account, and delete any local copy of the initial password.
- Review who holds the Admin role.
- Confirm the Facebook App Secret was reset (see
  [Facebook sign-in](#facebook-sign-in-off-in-production)).
- Choose and create the `contato@` inbox, and add its DNS records (see
  [item 3](#3-create-the-contato-inbox)).

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
