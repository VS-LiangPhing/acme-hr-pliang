// Pure helpers for the leave calendar page. No fetching here: data comes in from lib/api.ts.
import type { Employee, LeaveRequest } from "./api";

export type CalendarEntry = { leaveId: number; name: string; status: "Approved" | "Pending"; half: "AM" | "PM" | null };
export type CalendarDay = { date: string; day: number; inMonth: boolean; weekend: boolean; entries: CalendarEntry[] };

const pad = (n: number) => String(n).padStart(2, "0");

/** "YYYY-MM-DD" for a UTC date (month is 0-based in Date, written 1-based). */
export function dateKey(d: Date): string {
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

/** Move a (year, 0-based month) pair by delta months. */
export function shiftMonth(year: number, month: number, delta: number): { year: number; month: number } {
  const total = year * 12 + month + delta;
  return { year: Math.floor(total / 12), month: ((total % 12) + 12) % 12 };
}

export function monthLabel(year: number, month: number): string {
  return new Date(Date.UTC(year, month, 1)).toLocaleString("en-GB", { month: "long", year: "numeric", timeZone: "UTC" });
}

/**
 * Weeks (Mon–Sun) covering the month. Each day lists employees on leave that day.
 * Approved and Pending are shown; Rejected is not. Leave is working days only, so weekends stay empty.
 */
export function buildCalendar(year: number, month: number, leave: LeaveRequest[], employees: Employee[]): CalendarDay[][] {
  const names = new Map(employees.map(e => [e.id, e.name]));
  const shown = leave.filter(l => l.status === "Approved" || l.status === "Pending");

  const first = new Date(Date.UTC(year, month, 1));
  const offset = (first.getUTCDay() + 6) % 7; // Monday = 0
  const cursor = new Date(Date.UTC(year, month, 1 - offset));

  const weeks: CalendarDay[][] = [];
  do {
    const week: CalendarDay[] = [];
    for (let i = 0; i < 7; i++) {
      const key = dateKey(cursor);
      const weekend = i >= 5;
      const entries: CalendarEntry[] = weekend ? [] : shown
        .filter(l => l.startDate.slice(0, 10) <= key && key <= l.endDate.slice(0, 10))
        .map(l => ({
          leaveId: l.id,
          name: names.get(l.employeeId) ?? `Employee #${l.employeeId}`,
          status: l.status as "Approved" | "Pending",
          half: l.halfDay === "None" ? null : l.halfDay,
        }))
        .sort((a, b) => (a.status === b.status ? a.name.localeCompare(b.name) : a.status === "Approved" ? -1 : 1));
      week.push({ date: key, day: cursor.getUTCDate(), inMonth: cursor.getUTCMonth() === month, weekend, entries });
      cursor.setUTCDate(cursor.getUTCDate() + 1);
    }
    weeks.push(week);
  } while (cursor.getUTCMonth() === month);
  return weeks;
}
