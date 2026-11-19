"use client";
import { useEffect, useMemo, useState } from "react";
import { api, Employee, LeaveRequest } from "@/lib/api";
import { buildCalendar, monthLabel, shiftMonth } from "@/lib/calendar";

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

export default function Calendar() {
  const today = new Date();
  const [{ year, month }, setYm] = useState({ year: today.getFullYear(), month: today.getMonth() });
  const [leave, setLeave] = useState<LeaveRequest[] | null>(null);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [error, setError] = useState("");

  useEffect(() => {
    Promise.all([api<LeaveRequest[]>("/leave"), api<Employee[]>("/employees")])
      .then(([l, e]) => { setLeave(l); setEmployees(e); })
      .catch(e => setError((e as Error).message));
  }, []);

  const weeks = useMemo(() => buildCalendar(year, month, leave ?? [], employees), [year, month, leave, employees]);
  const pad = (n: number) => String(n).padStart(2, "0");
  const todayKey = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;
  const go = (delta: number) => setYm(shiftMonth(year, month, delta));

  return (
    <div className="card">
      <div className="cal-head">
        <button type="button" onClick={() => go(-1)} aria-label="Previous month">‹ Prev</button>
        <h2>{monthLabel(year, month)}</h2>
        <button type="button" onClick={() => go(1)} aria-label="Next month">Next ›</button>
      </div>
      {error && <p className="error">{error}</p>}
      {!error && leave === null && <p>Loading…</p>}
      {leave !== null && (
        <>
          <table className="cal">
            <thead><tr>{WEEKDAYS.map(d => <th key={d}>{d}</th>)}</tr></thead>
            <tbody>
              {weeks.map(w => (
                <tr key={w[0].date}>
                  {w.map(d => (
                    <td key={d.date} className={[d.inMonth ? "" : "out", d.weekend ? "weekend" : "", d.date === todayKey ? "today" : ""].join(" ").trim()}>
                      <div className="cal-day">{d.day}</div>
                      {d.entries.map(e => (
                        <div key={e.leaveId} className={e.status === "Pending" ? "cal-entry pending" : "cal-entry"} title={e.status}>
                          {e.name}{e.half && ` (${e.half})`}
                        </div>
                      ))}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          <p className="hint">Approved leave in dark text, <span className="pending">pending in grey</span>.</p>
        </>
      )}
    </div>
  );
}
