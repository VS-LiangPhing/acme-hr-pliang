"use client";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { Balance, createLeave, currentEmployeeId, decideLeave, getBalance, HalfDay, LeaveRequest, listLeave } from "@/lib/api";

const empty = { startDate: "", endDate: "", halfDay: "None" as HalfDay, reason: "" };

export default function Leave() {
  const me = currentEmployeeId();
  const [rows, setRows] = useState<LeaveRequest[]>([]);
  const [balance, setBalance] = useState<Balance | null>(null);
  const [form, setForm] = useState(empty);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    try {
      const [list, bal] = await Promise.all([listLeave(), getBalance(me)]);
      setRows(list);
      setBalance(bal);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [me]);

  useEffect(() => { load(); }, [load]);

  // The API checks every rule; the page only shows the API's answer.
  async function run(action: () => Promise<LeaveRequest>, done: (r: LeaveRequest) => string) {
    setBusy(true); setError(""); setNotice("");
    try {
      setNotice(done(await action()));
      await load();
      return true;
    } catch (e) {
      setError((e as Error).message);
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = { ...form, endDate: form.halfDay === "None" ? form.endDate : form.startDate };
    if (await run(() => createLeave(body), r => `Request ${r.id} created: ${r.days} day(s), ${r.status}.`)) setForm(empty);
  }

  const decide = (id: number, d: "approve" | "reject") =>
    run(() => decideLeave(id, d), r => `Request ${r.id} ${r.status.toLowerCase()}.`);

  return (
    <div className="card">
      <h2>Leave requests</h2>
      {loading && <p>Loading…</p>}
      {balance && (
        <p>
          <strong>Available: {balance.available} days</strong> (entitlement {balance.entitlement}, used {balance.used}, pending {balance.pending})
        </p>
      )}
      {error && <p className="error">{error}</p>}
      {notice && <p>{notice}</p>}

      <form onSubmit={submit}>
        <h3>New request</h3>
        <label>From <input type="date" required value={form.startDate} onChange={e => setForm({ ...form, startDate: e.target.value })} /></label>{" "}
        {form.halfDay === "None" && (
          <label>To <input type="date" required value={form.endDate} onChange={e => setForm({ ...form, endDate: e.target.value })} /></label>
        )}{" "}
        <label>Half day{" "}
          <select value={form.halfDay} onChange={e => setForm({ ...form, halfDay: e.target.value as HalfDay })}>
            <option value="None">No (full days)</option>
            <option value="AM">AM</option>
            <option value="PM">PM</option>
          </select>
        </label>{" "}
        <label>Reason <input required maxLength={500} value={form.reason} onChange={e => setForm({ ...form, reason: e.target.value })} /></label>{" "}
        <button type="submit" disabled={busy}>{busy ? "Saving…" : "Request leave"}</button>
      </form>

      <table>
        <thead><tr><th>ID</th><th>Employee</th><th>From</th><th>To</th><th>Half day</th><th>Days</th><th>Status</th><th>Reason</th><th></th></tr></thead>
        <tbody>
          {rows.map(r => (
            <tr key={r.id}>
              <td>{r.id}</td><td>{r.employeeId}</td><td>{r.startDate}</td><td>{r.endDate}</td><td>{r.halfDay}</td>
              <td>{r.days}</td><td>{r.status}</td><td>{r.reason}</td>
              <td>
                {r.status === "Pending" && r.employeeId !== me && (
                  <>
                    <button disabled={busy} onClick={() => decide(r.id, "approve")}>Approve</button>{" "}
                    <button disabled={busy} onClick={() => decide(r.id, "reject")}>Reject</button>
                  </>
                )}
              </td>
            </tr>
          ))}
          {!loading && rows.length === 0 && <tr><td colSpan={9}>No leave requests.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
