# Archived documents

These documents record earlier stages of Academia Auditiva. They are kept for
history and are **no longer maintained**, so parts of them no longer match the
code. The current documentation is listed in the
[main README](../../readme.md#-documentation).

| Document | What it was | Where to look now |
|---|---|---|
| [Arquitetura.md](Arquitetura.md) (PT) | Early architecture proposal (2025) | [docs/Architecture.md](../Architecture.md) |
| [MapaDoProjeto.md](MapaDoProjeto.md) (PT) | Early feature map (2025) | [Features](../../readme.md#-features) in the main README |
| [Scrum_Backlog_AcademiaAuditiva.md](Scrum_Backlog_AcademiaAuditiva.md) (PT) | Scrum backlog (2025) | [docs/Backlog.md](../Backlog.md) and GitHub issues |
| [Insights.md](Insights.md) (PT) | Ideas for player insights (2025) | Partly built: XP, levels, streaks and badges (#72), learning path (#73) |
| [SMOKE_TEST.md](SMOKE_TEST.md) (PT) | Manual smoke test of the v2 layout and translations, with the bugs it found | Automated tests; status below |
| [Erro1.jpeg](Erro1.jpeg) to [Erro4.jpeg](Erro4.jpeg) | Screenshots of Erro1 to Erro4 in SMOKE_TEST.md | |

## What happened to the SMOKE_TEST.md findings

Every bug and improvement written down in [SMOKE_TEST.md](SMOKE_TEST.md):

| Finding | Status | Fixed in |
|---|---|---|
| Main navigation not translated in every language (section 1) | Fixed | #39 (`ae83e85`) |
| Logout lands on the login page, not the landing page (section 3) | Fixed | #39 (`ae83e85`) |
| Weak spots list shows untranslated exercise names (section 4) | Fixed | #39 (`ae83e85`), #82 |
| After creating a classroom or routine, its page (Members, Pending invites, …) is in English (section 6) | Fixed | #39 (`ae83e85`) |
| Teacher subviews with hard-coded English (known bugs) | Fixed | #39 (`ae83e85`), #56 |
| Answer buttons such as `Iguais`/`Diferentes` always in Portuguese (known bugs) | Fixed | #56 |
| Admin › Users: filter and role buttons always in English | Fixed | #39 (`ae83e85`), #56 |
| Register: the first and last name placeholders in English | Fixed | #39 (`ae83e85`) |
| Account pages (`Identity/Account/Manage`) unstyled and in English | Fixed | #39 (`e557975`), #53 |
| Erro1: chart category names with accents garbled in fr-CA and pt-BR | Fixed | #39 (`ae83e85`) |
| Erro2: recommendations and most-missed exercises always in Portuguese | Fixed | #39 (`ae83e85`), #82 |
| Erro3: the exercise subtitle stays in Portuguese in en-US and fr-CA | Fixed | #39 (`ae83e85`) |
| Erro4: the success toast is shown twice and never closes | Fixed | #39 (`ae83e85`), #64 |
| Erro4: toasts, the invite form and the invite e-mail in English | Fixed | #39 (`ae83e85`), #56 |
| Erro4: pending invites and member lists without margins | Fixed | #39 (`ae83e85`), #53 |
| Erro4: the routine pages have the same problems (toasts, English text, layout) | Fixed | #39 (`ae83e85`), #53, #56, #64, #71, #82 |
| Improvement: lock, unlock and delete users in Admin | Done | #80 |
| Improvement: HTML e-mail templates (confirmation, password reset, …) | Done | #92 |

Before archiving, sections 1 to 8 were run again in en-US, pt-BR and fr-CA
with an automated browser script. It found two more bugs:

| Finding | Status | Fixed in |
|---|---|---|
| Changing the two-factor authentication settings signs the user out a minute later | Fixed | #81 |
| Exercise identifiers (such as `HigherOrLower`) on the dashboard, the history and the teacher's student page; untranslated routine form and profile labels | Fixed | #82 |

With #82, every check passes. The audio checks of section 0 are automated:
`FreePracticeTests` (only `roundId` and `playToken` are sent, and a round
expires once answered), `AudioControllerTests` (`Cache-Control: no-store`) and
the Playwright suite (32-character hex round and play tokens).
