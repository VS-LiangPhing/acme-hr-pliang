// Run with: npm test  (Node's built-in test runner, types stripped natively)
import { test } from "node:test";
import assert from "node:assert/strict";
import { buildCalendar, shiftMonth, monthLabel } from "./calendar.ts";
import type { Employee, LeaveRequest } from "./api";

const emp = (id: number, name: string): Employee => ({ id, name, email: `${id}@acme.test`, department: "Ops", managerId: null, annualEntitlement: 14 });
const leave = (p: Partial<LeaveRequest> & Pick<LeaveRequest, "id" | "employeeId" | "startDate" | "endDate">): LeaveRequest =>
  ({ halfDay: "None", days: 1, status: "Approved", reason: "", ...p });

const employees = [emp(1, "Aisyah"), emp(2, "Ben"), emp(3, "Chen")];
const day = (weeks: ReturnType<typeof buildCalendar>, date: string) => weeks.flat().find(d => d.date === date)!;

test("grid starts on Monday and covers the whole month in full weeks", () => {
  // November 2026: 1st is a Sunday, 30th is a Monday.
  const weeks = buildCalendar(2026, 10, [], employees);
  assert.equal(weeks[0][0].date, "2026-10-26");
  assert.equal(weeks[0][6].date, "2026-11-01");
  assert.equal(weeks.at(-1)![0].date, "2026-11-30");
  assert.equal(weeks.at(-1)![6].date, "2026-12-06");
  assert.ok(weeks.every(w => w.length === 7));
  assert.equal(weeks.flat().filter(d => d.inMonth).length, 30);
});

test("a month starting on Monday has no leading days from the previous month", () => {
  const weeks = buildCalendar(2026, 5, [], employees); // June 2026 starts on a Monday
  assert.equal(weeks[0][0].date, "2026-06-01");
  assert.ok(weeks[0][0].inMonth);
});

test("approved leave appears on each working day it covers, not on the weekend", () => {
  const weeks = buildCalendar(2026, 10, [leave({ id: 1, employeeId: 1, startDate: "2026-11-05", endDate: "2026-11-10" })], employees);
  for (const d of ["2026-11-05", "2026-11-06", "2026-11-09", "2026-11-10"])
    assert.deepEqual(day(weeks, d).entries, [{ leaveId: 1, name: "Aisyah", status: "Approved", half: null }], d);
  assert.equal(day(weeks, "2026-11-07").entries.length, 0); // Saturday
  assert.equal(day(weeks, "2026-11-08").entries.length, 0); // Sunday
  assert.equal(day(weeks, "2026-11-04").entries.length, 0);
  assert.equal(day(weeks, "2026-11-11").entries.length, 0);
});

test("pending is shown with its status, rejected is hidden, approved sorts first", () => {
  const weeks = buildCalendar(2026, 10, [
    leave({ id: 1, employeeId: 1, startDate: "2026-11-12", endDate: "2026-11-12", status: "Pending" }),
    leave({ id: 2, employeeId: 3, startDate: "2026-11-12", endDate: "2026-11-12", status: "Approved" }),
    leave({ id: 3, employeeId: 2, startDate: "2026-11-12", endDate: "2026-11-12", status: "Rejected" }),
  ], employees);
  assert.deepEqual(day(weeks, "2026-11-12").entries.map(e => [e.name, e.status]), [["Chen", "Approved"], ["Aisyah", "Pending"]]);
});

test("half days carry AM or PM", () => {
  const weeks = buildCalendar(2026, 10, [
    leave({ id: 1, employeeId: 1, startDate: "2026-11-13", endDate: "2026-11-13", halfDay: "AM", days: 0.5 }),
    leave({ id: 2, employeeId: 2, startDate: "2026-11-13", endDate: "2026-11-13", halfDay: "PM", days: 0.5 }),
  ], employees);
  assert.deepEqual(day(weeks, "2026-11-13").entries.map(e => e.half), ["AM", "PM"]);
});

test("leave spanning a month boundary shows in both months", () => {
  const l = [leave({ id: 1, employeeId: 2, startDate: "2026-11-30", endDate: "2026-12-02" })];
  assert.equal(day(buildCalendar(2026, 10, l, employees), "2026-11-30").entries.length, 1);
  assert.equal(day(buildCalendar(2026, 11, l, employees), "2026-12-01").entries.length, 1);
});

test("unknown employee falls back to the id", () => {
  const weeks = buildCalendar(2026, 10, [leave({ id: 1, employeeId: 99, startDate: "2026-11-16", endDate: "2026-11-16" })], employees);
  assert.equal(day(weeks, "2026-11-16").entries[0].name, "Employee #99");
});

test("date strings with a time part still match", () => {
  const weeks = buildCalendar(2026, 10, [leave({ id: 1, employeeId: 1, startDate: "2026-11-17T00:00:00", endDate: "2026-11-17T00:00:00" })], employees);
  assert.equal(day(weeks, "2026-11-17").entries.length, 1);
});

test("shiftMonth wraps across years", () => {
  assert.deepEqual(shiftMonth(2026, 0, -1), { year: 2025, month: 11 });
  assert.deepEqual(shiftMonth(2026, 11, 1), { year: 2027, month: 0 });
  assert.deepEqual(shiftMonth(2026, 10, 0), { year: 2026, month: 10 });
});

test("monthLabel", () => {
  assert.equal(monthLabel(2026, 10), "November 2026");
});
