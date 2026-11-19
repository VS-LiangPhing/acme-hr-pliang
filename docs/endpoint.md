# Endpoint change log
Newest first. Maintained by the endpoint-change skill.

## 2026-11-19 — GET /leave/team-summary
- **Change:** New action on existing controller
- **Why:** A manager wants the total number of leave days taken by all their subordinates.
- **Reuse check:** `GET /leave` lists individual requests and, for a manager, also includes the manager's own leave. `GET /employees/{id}/balance` covers one employee at a time. Neither gives a team total, and enhancing them would mix a different question and access rule into them. So a new action was added on `LeaveController`, with the logic in `LeaveService` next to the existing balance maths.
- **Details:** Query `year` (optional, 2000–2100; counts only the working days of a request that fall inside that year). Returns `{ managerId, year, directReports, daysTaken }`. "Subordinates" means direct reports (same as `AccessService`). "Taken" means Approved leave only (same as `used` in the balance); pending and rejected leave are excluded. Half days count 0.5. The manager comes from the token, and any `managerId` in the query is ignored. Managers only (`AccessService.CanSeeTeamSummary`): 403 for others, 400 for a year out of range, 401 when not signed in.
- **Breaking:** No
- **Files:** `src/Api/Controllers/CoreControllers.cs`, `src/Api/Services/LeaveService.cs`, `src/Api/Auth/AccessService.cs`, `src/Api.Tests/TeamLeaveSummaryTests.cs`
- **Tests:** `TeamLeaveSummaryServiceTests`, `TeamLeaveSummaryApiTests`; 121 passed / 121 total

## 2026-11-19 — GET /me
- **Change:** Enhanced
- **Why:** Show the signed-in user's manager's name with their personal info.
- **Reuse check:** `GET /me` already returns the signed-in employee's record and roles, so it was extended with one field instead of adding a new endpoint. `GET /employees/{id}` was not used: it would need a second call and takes an id from the URL.
- **Details:** New response field `managerName` (string, or null when the employee has no manager or the manager id points to no one). All existing fields are unchanged. Who is asking still comes from the token only, and any `employeeId` in the query is ignored. Logic moved from the controller into the new `ProfileService`. 401 when not signed in, 404 if the sign-in has no employee record.
- **Breaking:** No (additive field). `Employee` in `web/lib/api.ts` gets an optional `managerName`.
- **Files:** `src/Api/Controllers/MeController.cs`, `src/Api/Services/ProfileService.cs`, `src/Api/Program.cs`, `web/lib/api.ts`, `src/Api.Tests/MeTests.cs`
- **Tests:** `ProfileServiceTests`, `MeApiTests`, existing `AuthApiTests` unchanged; 109 passed / 109 total

## 2026-11-19 — GET /holidays/weekends
- **Change:** New
- **Why:** List the public holidays in a state and year that fall on a Saturday or Sunday, with the POL-13 replacement day for Sundays.
- **Reuse check:** No existing holiday endpoint or data. `/leave`, `/employees` and `/policies` are different resources, so a new `HolidaysController` was created.
- **Details:** Query `state` (required; SEL, KUL or PNG, case-insensitive) and `year` (optional, defaults to the current year, 2000–2100). Returns `[{ date, name, dayOfWeek, replacementDate }]`. `replacementDate` is the next working day that is not itself a holiday, and is set only for Sundays. Synthetic fixed-date holidays only: Hari Raya, Chinese New Year and Deepavali are not modelled. Any signed-in user may call it (no employee data). 400 for a missing or unknown state or a year out of range. 401 when not signed in.
- **Breaking:** No
- **Files:** `src/Api/Controllers/HolidaysController.cs`, `src/Api/Services/HolidayService.cs`, `src/Api/Program.cs`, `src/Api.Tests/WeekendHolidaysTests.cs`
- **Tests:** `HolidayServiceTests`, `HolidaysApiTests`; 101 passed / 101 total
