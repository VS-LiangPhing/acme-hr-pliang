---
name: endpoint-change
description: Use whenever the user asks to create, add, build or scaffold an API endpoint (or route, action, API call) in src/Api, including via /new-endpoint or a feature spec. First checks whether an existing endpoint can be reused or enhanced instead, then creates or updates it, then logs the change with its date in docs/endpoint.md.
---

# Endpoint change: reuse first, then build, then log

Follow these three steps in order. Do not skip step 1 even when the request says "new endpoint".

## 1. Look for an endpoint to reuse or enhance

List every existing endpoint before designing anything:

- Grep `src/Api/Controllers/` for `Route(` and `Http(Get|Post|Put|Patch|Delete)` to get verb + path for each action.
- Also check `src/Api/Auth/AuthSetup.cs` (`MapGroup("/auth")`, Identity endpoints) and any `Map*` calls in `src/Api/Program.cs`.
- Read `docs/endpoint.md` for the history of why endpoints exist.
- Check callers in `web/lib/api.ts` and in `src/Api/Helpdesk` tools, so you know who depends on each endpoint.

Then decide, and state the decision to the user in one or two lines with the reason:

| Situation | Decision |
|---|---|
| Same resource and verb, and the need is met by an extra **optional** query parameter, filter, or extra response field | **Enhance** the existing endpoint |
| Same resource, but a distinct question (e.g. a sub-view like `/holidays/weekends` vs `/holidays`) | **New action on the existing controller and service** |
| A different resource or a different access rule | **New endpoint** (new controller and service if no fitting one exists) |
| Enhancing would change existing behaviour for current callers (rename, remove, required param, different default, different access) | Do **not** enhance silently. Ask the user, or create a new endpoint |

If two options are equally reasonable, ask the user before writing code.

## 2. Create or update

Follow the project rules in CLAUDE.md and `.claude/rules/api-controllers.md`:

- Write the tests first (red), then the code (green).
- Keep the controller thin. Logic goes in a service in `src/Api/Services/`, registered in `Program.cs` if new. Errors are returned as `Problem(...)` / `ValidationProblem(...)`.
- Every endpoint requires a signed-in user. Never add `[AllowAnonymous]` without asking.
- Take the acting employee from `User.EmployeeId()`, never from the body or the query. Check access with `AccessService` and return 403 when it says no.
- Tests in `src/Api.Tests` cover the happy path and validation errors (400). Also include a 401 test for an anonymous caller. If the endpoint touches employee data, add a test that another employee gets 403.
- When enhancing: keep the existing tests passing unchanged. They prove current callers still work. Add new tests for the new behaviour.
- If `web/lib/api.ts` calls the endpoint and the response shape changed, update it.
- Run `dotnet build` (no new warnings) and `dotnet test`. Report which tests ran and the result.

## 3. Log the change in docs/endpoint.md

Get today's date from the system (`Get-Date -Format yyyy-MM-dd` in PowerShell, `date +%F` in bash). Never guess it.

Create `docs/endpoint.md` if it is missing, with the heading `# Endpoint change log` and the line `Newest first. Maintained by the endpoint-change skill.` Then add the new entry **at the top**, directly under that line:

```markdown
## YYYY-MM-DD — <VERB> <path>
- **Change:** New | Enhanced | New action on existing controller
- **Why:** <the requirement, one line>
- **Reuse check:** <which existing endpoints were considered and why they were reused or not>
- **Details:** <params, response shape, access rule, validation>
- **Breaking:** No | Yes — <what callers must change>
- **Files:** <controller, service, tests, other>
- **Tests:** <test class names; N passed / M total>
```

Write one entry per endpoint touched. Only log changes that were actually made and tested. If the work stops early, log nothing.
