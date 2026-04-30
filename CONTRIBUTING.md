# Contributing to Academia Auditiva

Thanks for taking the time to contribute! This document describes the
conventions used in this repository.

## Branching

- `main` — protected, always deployable.
- `feat/*`, `fix/*`, `chore/*`, `docs/*` — short-lived feature branches.
- One PR per feature; squash-merge into `main`.

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

## Testing

- Add or update tests for any behavior change in `Tests/UnitTests` or
  `Tests/IntegrationTests`.
- Run locally before opening a PR:
  ```powershell
  dotnet build
  dotnet test
  ```
- New EF entities require a migration: `dotnet ef migrations add <Name> --project AcademiaAuditiva`.

## Security review

Any change that touches authentication, authorization, secrets, file
uploads, SQL queries, or external integrations should call out the
security implications in the PR description. The CODEOWNERS for those
paths will gate merging.

## Pull requests

- Title: same format as the squash-commit subject.
- Body must answer: **what**, **why**, **how to verify**.
- Link the relevant todo from the modernization plan when applicable.
- CI must pass: build, tests, gitleaks, CodeQL.

## Reporting issues

Bugs and feature requests go in GitHub Issues. Security reports do
**not** — see [docs/Security.md](docs/Security.md).
