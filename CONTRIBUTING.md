# Contributing to Academia Auditiva

Thanks for taking the time to contribute! This document describes the
conventions used in this repository.

## Branching

- `master` — protected, always deployable.
- `feat/*`, `fix/*`, `chore/*`, `docs/*` — short-lived feature branches.
- One PR per feature; squash-merge into `master`.

## Commits

We follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <subject>

<body>

<trailer>
```

- **type**: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `sec`,
  `build`, `ci`, `perf`, `style`.
- **scope** *(optional)*: the affected module — `identity`, `obs`,
  `infra`, `ui`, etc.
- **subject**: imperative, lowercase, no period, ≤ 72 chars.
- **trailer**: when AI assistance is used, include
  `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.

Examples:
- `feat(teacher): add classroom CRUD`
- `sec: remove SMTP password from source`
- `fix(score): clamp BestScore to non-negative`

## Code style

- `.editorconfig` is the source of truth — your editor will pick it up.
- Prefer constructor injection over service-locator patterns.
- Public APIs receive XML docs only when behavior is non-obvious.
- Localize new user-facing strings via `IStringLocalizer<SharedResources>`;
  do not hardcode pt-BR / en-US text in views or controllers.

## Dependencies

- The .NET SDK is pinned in `global.json`; shared build settings (target
  framework, nullable, implicit usings) live in `Directory.Build.props`.
- NuGet versions are managed centrally in `Directory.Packages.props`: add a
  `<PackageVersion>` there and a versionless `<PackageReference>` in the
  `.csproj`.
- Dependabot opens grouped weekly PRs for NuGet packages and GitHub Actions;
  `.github/dependabot.yml` lists the versions we deliberately hold back.

## Testing

- Add or update tests for any behavior change in `Tests/UnitTests` or
  `Tests/IntegrationTests`.
- Run locally before opening a PR:
  ```powershell
  dotnet build
  dotnet test
  ```
- Model changes require a migration: run `dotnet tool restore` once (it
  installs the `dotnet-ef` version pinned in `dotnet-tools.json`), then
  `dotnet ef migrations add <Name> --project AcademiaAuditiva`.
  `MigrationsTests` (integration tests) fails when the model and the
  migrations drift apart.

## Continuous integration

CI runs once per PR update and on pushes to `master`, not again on feature
branch pushes. Superseded PR runs are cancelled, and .NET jobs share a NuGet
package cache. The always-running scope check selects validation by impact:

| Changes | Validation |
|---|---|
| Root contribution/license documents, Markdown and non-runtime illustrations under `docs/` | Scope-policy tests and Gitleaks; no application build, browser suite or image scan |
| PRs changing only the home landing view, its WebP illustrations, `HomePageTests`, and existing resource files whose changes affect only `Home.*` keys | Release build; home, catalog and localization integration tests; four browser tests tagged `@landing`; image scan and CodeQL |
| Everything else, mixed functional changes, or an empty/uncertain diff | Full build, xUnit suite, production-like Linux DSP checks, complete Playwright suite, image scan and CodeQL |

Bicep validation runs for infrastructure or CI-policy changes. Changes to
the CI workflows or shared test/browser helpers are never treated as a
landing-only change. Resource additions/removals, renames, multi-commit
pushes and PR merge bases are handled conservatively. Arbitrary "extra
files" are not exempt: an audio source, shared stylesheet or script can
change application behavior.

Deployable pushes to `master` retain the full validation before production:
a landing-only merge must not inadvertently publish an earlier functional
change whose full CI is still running. Documentation-only pushes stay light.
Master runs are not cancelled by newer documentation-only commits.

The classifier and exact-commit deployment guard are tested with
`python scripts\test_ci_scope.py`. Landing browser coverage runs with
`npm test -- --grep @landing` from `Tests/e2e`.

Automatic CD consumes the successful `master` push CI's `ci-impact`
artifact instead of running CI a second time. It verifies the artifact's
SHA, checks out that exact tested commit, and deploys only when the original
app/infra/build-file deployment paths changed. Documentation/test-only
merges do not deploy or displace pending app deployments. Automatic CD
rejects old CI reruns when newer runtime changes exist on `master`, while
intervening documentation-only commits do not invalidate a tested app image.
Manual CD still runs the full CI first. Do not replace
job-level selection with top-level workflow path exclusions: required
checks must still report their outcome for lightweight PRs.

## Security review

Any change that touches authentication, authorization, secrets, file
uploads, SQL queries, or external integrations should call out the
security implications in the PR description. The CODEOWNERS for those
paths will gate merging.

Gitleaks scans Git history on every PR, `master` push and weekly run, using
the pinned open-source CLI (no commercial action license or PR comments).
Findings that were already in the history are baselined in
`.gitleaksignore` by fingerprint; never add new entries for a real secret:
rotate it instead.

## Pull requests

- Title: same format as the squash-commit subject.
- Body must answer: **what**, **why**, **how to verify**.
- Link the relevant todo from the modernization plan when applicable.
- CI and the applicable checks for the selected scope must pass, including
  Gitleaks and CodeQL when code is affected.

## Reporting issues

Bugs and feature requests go in GitHub Issues. Security reports do
**not** — see [docs/Security.md](docs/Security.md).
