# Backlog

What is left to do on Academia Auditiva, in priority order. Written on
2026-10-02, when `master` was at `07d6776` and production ran `c44fafb`; line
numbers refer to `07d6776`. Each item says why it matters, where to look, what
to do and when it is done. New ideas go to GitHub issues.

## Where things stand

The modernization plan is done (#47 to #83):

- **Platform**: .NET 10 with central package management, private networking
  for Key Vault, SQL and Blob storage, custom domains, OpenTelemetry, and CI
  with real SQL Server and Playwright (#47, #48, #51, #52, #59).
- **Look and languages**: the visual identity, and full en-US, pt-BR and fr-CA
  (#53, #56, #70, #82).
- **Exercises**: 19 exercises on piano, guitar and violin, plus Explore and
  free practice (#58, #60, #75 to #78).
- **Engagement**: XP, levels, streaks, 18 badges, the learning path and the
  tutorial (#72 to #74).
- **Accounts**: privacy policy, data export and deletion, and admin lock,
  unlock and delete (#57, #65, #80, #81).

E-mail is [on hold](#on-hold-e-mail). There are no open pull requests and no
open CodeQL or Dependabot alerts. The open issues are #16 and #31.

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

### 1. Lock accounts after failed sign-ins and rate-limit the account pages

**Why.** Wrong passwords never count toward lockout: `Login.cshtml.cs:120`
calls `PasswordSignInAsync(..., lockoutOnFailure: false)`. No rate-limit policy
covers the account pages either. `docs/Security.md:14` says lockout protects
against brute force, which is not true yet.

**Where.**

- `AcademiaAuditiva/Areas/Identity/Pages/Account/Login.cshtml.cs:120`. Also
  check `LoginWith2fa` and `LoginWithRecoveryCode`.
- `AcademiaAuditiva/Program.cs:79`: Identity options. Lockout runs on the
  defaults (5 failures, 5 minutes).
- `AcademiaAuditiva/Program.cs:390-440`: the rate-limit policies
  (`RequestPlay`, `AudioToken`, `Explore`).
- `AcademiaAuditiva/Services/LockoutAwareSignInManager.cs`, and
  `AcademiaAuditiva/Areas/Admin/Controllers/UsersController.cs:58`, `:163-168`
  and `:188`.

**What.**

- Count failures (`lockoutOnFailure: true`) and set `options.Lockout`
  explicitly.
- Add a per-client-IP policy for the POSTs of Login, LoginWith2fa,
  LoginWithRecoveryCode, Register, ForgotPassword and ResendEmailConfirmation.
  - Make the limit configurable. The integration tests sign in through the
    real form, and every test-server request lands in the same partition. The
    Playwright suite also signs in several times from one IP.
  - Limits live in each replica's memory (production runs up to 3 replicas).
    The lockout, stored in the database, is the real cap.
- Settle how temporary lockouts and the admin lock interact:
  - An admin lock sets `LockoutEnd` to `DateTimeOffset.MaxValue`
    (`UsersController.cs:168`). A temporary lockout would also show as locked
    in **Admin › Users** (`UsersController.cs:58`), and Unlock clears both
    (`UsersController.cs:188`).
  - `LockoutAwareSignInManager` signs a locked account out of all its sessions
    within a minute. With lockout on, five wrong guesses by anyone would end
    the real user's sessions. Consider applying that only to admin locks.
  - Accounts with `LockoutEnabled = false` never lock
    (`UsersController.cs:163`). Check the existing users.
- Keep the `LockoutAwareSignInManager` overrides and the `AdminUsersTests`
  that pin them.
- Correct `docs/Security.md:14`.

**Done when.** Integration tests show that repeated wrong passwords lead to the
Lockout page and that the limit returns 429. `AdminUsersTests` still pass, and
`docs/Security.md` matches the code.

### 2. Show invite links while e-mail is off

**Why.** Without SMTP, the e-mail sender logs a warning and returns
(`AcademiaAuditiva/Services/EmailSender.cs:25-34`). The teacher still sees
"invite sent" (`AcademiaAuditiva/Areas/Teacher/Controllers/MembersController.cs:134`)
although nothing went out. The pending-invite list
(`AcademiaAuditiva/Areas/Teacher/Views/Classrooms/Details.cshtml:65-94`) has no
link to copy, so the student cannot join. The link only appears when sending
throws (`MembersController.cs:127-131`).

**What.**

- When SMTP is not configured, say so and show the accept link with a copy
  button, both after inviting and in the pending list.
  `RegisterConfirmation.cshtml.cs:76-80` already checks whether SMTP is
  configured.
- Keep the e-mail path as it is.
- Add the new texts in the three cultures.

**Done when.** Without SMTP, a teacher copies the link and the student joins.
Integration tests cover both modes.

### 3. Fix the inflated practice time

**Why.** Each exercise script sets `exerciseStartTime` once, when the page
loads (for example `AcademiaAuditiva/wwwroot/js/Exercises/GuessNote.js:14`).
Every answer then sends the time since page load (`GuessNote.js:66`), so the
tenth answer on a page reports the whole visit. The dashboard's total time adds
these values up (`AcademiaAuditiva/Controllers/DashboardController.cs:65`).
`AcademiaAuditiva/Services/UserReportService.cs:292`, `:506`, `:513`, `:521`
and `:530` average or sum the same field.

**What.**

- Time each round. Choose one way:
  - restart the clock when a round starts, in the 14 scripts that set
    `exerciseStartTime`;
  - or compute the time on the server, from when `RequestPlay` issued the
    round, and stop trusting the client value
    (`AcademiaAuditiva/Controllers/ExerciseController.cs:324`, `:334`, `:376`).
- Cap a single answer, for example at 5 minutes.
- Decide what to do with existing rows; capping them in the queries would do.

**Done when.** Tests show per-round times, and the dashboard total matches real
practice time.

### 4. Settle the Essentia.js license (AGPL-3.0)

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

## On hold: e-mail

The owner put e-mail on hold while the site is a test site.

**Today, without SMTP.** The sender (`AcademiaAuditiva/Services/EmailSender.cs`,
registered at `Program.cs:208`) skips sending when `Smtp:Host`, `Smtp:User` or
`Smtp:Password` is missing. The `Smtp--*` Key Vault secrets are placeholders.
As a result:

- Registration shows the confirmation link on screen
  (`AcademiaAuditiva/Areas/Identity/Pages/Account/RegisterConfirmation.cshtml.cs:76-80`),
  so nobody proves they own the address. This stops by itself once SMTP is
  configured.
- Forgot password, resend confirmation and change e-mail send nothing, so
  users cannot reset a forgotten password.
- Teacher invites go nowhere (see [item 2](#2-show-invite-links-while-e-mail-is-off)).
- `contato@academiaauditiva.com` has no MX record, so mail to it bounces. It is
  shown on the Lockout page and in the privacy policy.
- `docs/Security.md:11` relies on e-mail: confirmation, and the forgot-password
  reset of the bootstrap admin.

**When it resumes.**

- Pick a provider: Azure Communication Services Email or an SMTP service. The
  current sender speaks SMTP (MailKit). Store the settings with
  `infra/scripts/seed-keyvault.ps1`.
- DNS: SPF, DKIM and DMARC for the sending domain, plus MX or forwarding for
  `contato@`. The zone lives in another tenant; `infra/scripts/configure-dns.ps1`
  updates it.
- HTML templates for confirmation, password reset and invites, in the three
  cultures.
- External sign-in also sends a confirmation e-mail (`RequireConfirmedAccount`,
  `Program.cs:79`).

**Done when.** Confirmation, reset and invite e-mails reach a test inbox in
each culture, SPF and DKIM pass, and `contato@` receives mail.

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

### #16: the remaining badges and their artwork

#72 shipped 18 badges as icon medals. Eight more are seeded but hidden
(`IsAvailable: false` in
`AcademiaAuditiva/Services/Gamification/BadgeCatalog.cs:82-89`). Nothing in the
app can award them yet:

| Badge key | Seeded rule (`AcademiaAuditiva/Data/SeedData.cs`) | Missing feature |
|---|---|---|
| `explorer` | Used every filter once (:979) | Track the filters each player uses, or reward the Explore page (#76) instead |
| `filter_ninja` | Custom filter combinations in 5 sessions (:980) | The same filter tracking |
| `daily_challenge_complete` | Finished all of the day's exercises (:983) | A daily challenge |
| `total_mastery` | 100% on an exercise with every filter (:989) | The same filter tracking |
| `mission_addict` | Finished 10 mixed challenges (:995) | Missions |
| `speedster` | 90% correct in a speed test (:996) | A speed test mode |
| `mystery_listener` | Got an "impossible" question right in a fully random mode (:997) | A fully random mode |
| `impossible_melody` | Got an altered melody with a hidden rest right (:998) | That melody variant |

The issue also asks for custom artwork instead of the icons.

**Done when.** Each badge has a rule, tests and texts in the three cultures, and
its `IsAvailable: false` is gone. The owner approves any artwork.

### #31: Microsoft Entra External ID

The issue proposes letting Entra handle identity. Today ASP.NET Core Identity
handles local accounts, two-factor, the admin lock, and data export and
deletion.

- **Gains.** Moving would bring hosted e-mail verification, MFA and social
  sign-in, which covers much of the e-mail and Facebook work.
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
- Set up the `contato@` mailbox, together with e-mail.

## Watch

- The weekly Gitleaks run (Mondays at 06:00 UTC) scans the whole history.
  - Its scheduled runs in June and July 2026 failed on password hashes in the
    2025 migrations.
  - `.gitleaksignore` covers them now, and a local full-history scan was clean.
  - If it fails again, look at the new finding before allowlisting its
    fingerprint.
- Merge Dependabot pull requests through the same cycle.
