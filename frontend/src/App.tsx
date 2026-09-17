import { useCallback, useEffect, useState } from 'react'
import './App.css'
import {
  api,
  type ChaseEvent,
  type ComplianceResult,
  type Finding,
  type Rule,
  type SettingsDto,
  type SettingsUpdate,
  type Team,
} from './api'

type Tab = 'compliance' | 'history' | 'findings' | 'rules' | 'roster' | 'settings'

const TABS: { key: Tab; label: string }[] = [
  { key: 'compliance', label: 'Compliance' },
  { key: 'findings', label: 'Task updates' },
  { key: 'history', label: 'Chase history' },
  { key: 'rules', label: 'Rules' },
  { key: 'roster', label: 'Roster' },
  { key: 'settings', label: 'Settings' },
]

export default function App() {
  const [tab, setTab] = useState<Tab>('compliance')
  return (
    <div className="app">
      <header>
        <h1>AIManager</h1>
        <span className="subtitle">Team compliance chaser</span>
        <a className="hangfire-link" href="/hangfire" target="_blank" rel="noreferrer">
          Hangfire ↗
        </a>
      </header>
      <nav className="tabs">
        {TABS.map((t) => (
          <button key={t.key} className={tab === t.key ? 'tab active' : 'tab'} onClick={() => setTab(t.key)}>
            {t.label}
          </button>
        ))}
      </nav>
      <main>
        {tab === 'compliance' && <CompliancePanel />}
        {tab === 'findings' && <FindingsPanel />}
        {tab === 'history' && <HistoryPanel />}
        {tab === 'rules' && <RulesPanel />}
        {tab === 'roster' && <RosterPanel />}
        {tab === 'settings' && <SettingsPanel />}
      </main>
    </div>
  )
}

function useAsync<T>(loader: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const reload = useCallback(() => {
    setLoading(true)
    setError(null)
    loader()
      .then(setData)
      .catch((e) => setError(String(e)))
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  useEffect(reload, [reload])
  return { data, error, loading, reload }
}

function Bool({ v }: { v: boolean }) {
  return <span className={v ? 'ok' : 'bad'}>{v ? '✓' : '✗'}</span>
}

function CompliancePanel() {
  const { data, error, loading, reload } = useAsync<ComplianceResult>(() => api.getCompliance())
  return (
    <section>
      <div className="panel-head">
        <h2>Compliance — previous working day</h2>
        <button onClick={reload} disabled={loading}>{loading ? 'Loading…' : 'Refresh'}</button>
      </div>
      {error && <p className="error">{error}</p>}
      {data && (
        <>
          <p className="muted">
            {data.teamName} · {data.targetDate} · threshold {data.threshold}h · live from Jira
          </p>
          <table>
            <thead><tr><th>Member</th><th>Hours</th><th>Status</th></tr></thead>
            <tbody>
              {data.rows.map((r) => (
                <tr key={r.memberId}>
                  <td>{r.displayName}</td>
                  <td>{r.hours.toFixed(2)}h</td>
                  <td>{r.ok ? <span className="ok">OK</span> : <span className="bad">Under</span>}</td>
                </tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={3} className="muted">No active members.</td></tr>}
            </tbody>
          </table>
        </>
      )}
    </section>
  )
}

function FindingsPanel() {
  const [onlyIncomplete, setOnlyIncomplete] = useState(false)
  const { data, error, loading, reload } = useAsync<Finding[]>(
    () => api.getFindings(onlyIncomplete), [onlyIncomplete],
  )
  return (
    <section>
      <div className="panel-head">
        <h2>Task-update findings</h2>
        <label className="check">
          <input type="checkbox" checked={onlyIncomplete} onChange={(e) => setOnlyIncomplete(e.target.checked)} />
          only missing an update
        </label>
        <button onClick={reload} disabled={loading}>{loading ? 'Loading…' : 'Refresh'}</button>
      </div>
      {error && <p className="error">{error}</p>}
      <table>
        <thead>
          <tr><th>Ticket</th><th>Status</th><th>Assignee</th><th>Work done</th><th>Remaining</th><th>Estimate</th><th>Verdict</th></tr>
        </thead>
        <tbody>
          {(data ?? []).map((f) => (
            <tr key={f.id} className={f.isComplete ? '' : 'row-warn'}>
              <td><a href={`https://infigosoftware.atlassian.net/browse/${f.issueKey}`} target="_blank" rel="noreferrer">{f.issueKey}</a></td>
              <td>{f.status}</td>
              <td>{f.assignee ?? '—'}</td>
              <td><Bool v={f.hasWorkDone} /></td>
              <td><Bool v={f.hasRemaining} /></td>
              <td><Bool v={f.hasEstimate} /></td>
              <td className="verdict">{f.verdict}</td>
            </tr>
          ))}
          {data && data.length === 0 && <tr><td colSpan={7} className="muted">No findings yet — run the Task-update rule.</td></tr>}
        </tbody>
      </table>
    </section>
  )
}

function HistoryPanel() {
  const { data, error, loading, reload } = useAsync<ChaseEvent[]>(() => api.getChaseEvents({ take: 200 }))
  return (
    <section>
      <div className="panel-head">
        <h2>Chase history</h2>
        <button onClick={reload} disabled={loading}>{loading ? 'Loading…' : 'Refresh'}</button>
      </div>
      {error && <p className="error">{error}</p>}
      <table>
        <thead>
          <tr><th>When (UTC)</th><th>Type</th><th>Target</th><th>Who</th><th>Reason</th><th>Outcome</th><th>Delivered to</th></tr>
        </thead>
        <tbody>
          {(data ?? []).map((e) => (
            <tr key={e.id}>
              <td>{new Date(e.createdAtUtc).toISOString().replace('T', ' ').slice(0, 16)}</td>
              <td>{e.type}</td>
              <td>{e.targetDate}</td>
              <td>{e.memberName ?? '—'}</td>
              <td>{e.reason}</td>
              <td><span className={e.outcome === 'Sent' ? 'ok' : e.outcome === 'Failed' ? 'bad' : 'muted'}>{e.outcome}</span></td>
              <td>{e.deliveredTo ?? '—'}</td>
            </tr>
          ))}
          {data && data.length === 0 && <tr><td colSpan={7} className="muted">No chase events yet.</td></tr>}
        </tbody>
      </table>
    </section>
  )
}

function RulesPanel() {
  const { data, error, reload } = useAsync<Rule[]>(() => api.getRules())
  const [busy, setBusy] = useState<number | null>(null)
  const [msg, setMsg] = useState<string | null>(null)

  const save = async (id: number, body: Partial<Rule>) => {
    setBusy(id); setMsg(null)
    try { await api.updateRule(id, body); await reload() } catch (e) { setMsg(String(e)) } finally { setBusy(null) }
  }
  const run = async (id: number) => {
    setBusy(id); setMsg(null)
    try { const r = await api.runRule(id); setMsg(`Enqueued job ${r.jobId}. Refresh history/findings in a moment.`) }
    catch (e) { setMsg(String(e)) } finally { setBusy(null) }
  }

  return (
    <section>
      <div className="panel-head"><h2>Rules</h2></div>
      {error && <p className="error">{error}</p>}
      {msg && <p className="info">{msg}</p>}
      {(data ?? []).map((r) => (
        <div key={r.id} className="rule-card">
          <div className="rule-title">
            <strong>{r.type}</strong> · {r.teamName} · <code>{r.channel}</code>
            <label className="check">
              <input type="checkbox" checked={r.enabled} disabled={busy === r.id}
                onChange={(e) => save(r.id, { enabled: e.target.checked })} />
              enabled
            </label>
          </div>
          <div className="rule-fields">
            <label>Cron<input defaultValue={r.cron} onBlur={(e) => e.target.value !== r.cron && save(r.id, { cron: e.target.value })} /></label>
            {r.type === 'TimeLog' && (
              <label>Threshold h
                <input type="number" step="0.5" defaultValue={r.thresholdHours ?? 5}
                  onBlur={(e) => save(r.id, { thresholdHours: Number(e.target.value) })} />
              </label>
            )}
            <label>Test recipient (blank = go live)
              <input defaultValue={r.testRecipientOverride ?? ''} placeholder="(none)"
                onBlur={(e) => e.target.value !== (r.testRecipientOverride ?? '') && save(r.id, { testRecipientOverride: e.target.value })} />
            </label>
          </div>
          <button onClick={() => run(r.id)} disabled={busy === r.id}>Run now</button>
        </div>
      ))}
    </section>
  )
}

function RosterPanel() {
  const { data, error, reload } = useAsync<Team[]>(() => api.getTeams())
  const [busy, setBusy] = useState<number | null>(null)
  const toggle = async (id: number, active: boolean) => {
    setBusy(id)
    try { await api.updateMember(id, { active }); await reload() } finally { setBusy(null) }
  }
  return (
    <section>
      <div className="panel-head"><h2>Roster</h2></div>
      {error && <p className="error">{error}</p>}
      <p className="muted">Only <b>active</b> members are chased. Activate the whole team to go live.</p>
      {(data ?? []).map((t) => (
        <table key={t.id}>
          <thead><tr><th>{t.name} ({t.timezone})</th><th>Email</th><th>Jira acct</th><th>Active</th></tr></thead>
          <tbody>
            {t.members.map((m) => (
              <tr key={m.id}>
                <td>{m.displayName}</td>
                <td>{m.email}</td>
                <td className="muted">{m.jiraAccountId ? '✓' : '—'}</td>
                <td><input type="checkbox" checked={m.active} disabled={busy === m.id} onChange={(e) => toggle(m.id, e.target.checked)} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      ))}
    </section>
  )
}

function SettingsPanel() {
  const { data, reload } = useAsync<SettingsDto>(() => api.getSettings())
  const [jiraUrl, setJiraUrl] = useState('')
  const [jiraUser, setJiraUser] = useState('')
  const [jiraToken, setJiraToken] = useState('')
  const [teamsUrl, setTeamsUrl] = useState('')
  const [reportRecipient, setReportRecipient] = useState('')
  const [anthKey, setAnthKey] = useState('')
  const [anthBase, setAnthBase] = useState('')
  const [anthModel, setAnthModel] = useState('')
  const [anthMaxTokens, setAnthMaxTokens] = useState('')
  const [msg, setMsg] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Sync editable non-secret fields from the loaded effective settings.
  useEffect(() => {
    if (!data) return
    setJiraUrl(data.jira.url)
    setJiraUser(data.jira.user)
    setReportRecipient(data.teams.reportRecipient)
    setAnthBase(data.anthropic.baseUrl)
    setAnthModel(data.anthropic.model)
    setAnthMaxTokens(String(data.anthropic.maxTokens))
  }, [data])

  if (!data) return <section><p className="muted">Loading…</p></section>

  const apply = async (update: SettingsUpdate, note: string) => {
    setBusy(true); setMsg(null)
    try { await api.updateSettings(update); setJiraToken(''); setAnthKey(''); setTeamsUrl(''); await reload(); setMsg(note) }
    catch (e) { setMsg(String(e)) } finally { setBusy(false) }
  }

  const saveAll = () => {
    const u: SettingsUpdate = {}
    if (jiraUrl !== data.jira.url) u.jiraUrl = jiraUrl
    if (jiraUser !== data.jira.user) u.jiraUser = jiraUser
    if (jiraToken) u.jiraApiToken = jiraToken
    if (teamsUrl) u.teamsPowerAutomateDmUrl = teamsUrl
    if (reportRecipient !== data.teams.reportRecipient) u.teamsReportRecipient = reportRecipient
    if (anthKey) u.anthropicApiKey = anthKey
    if (anthBase !== data.anthropic.baseUrl) u.anthropicBaseUrl = anthBase
    if (anthModel !== data.anthropic.model) u.anthropicModel = anthModel
    if (anthMaxTokens && Number(anthMaxTokens) !== data.anthropic.maxTokens) u.anthropicMaxTokens = Number(anthMaxTokens)
    if (Object.keys(u).length === 0) { setMsg('Nothing changed.'); return }
    apply(u, 'Saved. Changes take effect immediately.')
  }

  const secretBadge = (set: boolean) =>
    <span className={set ? 'ok' : 'bad'}>{set ? 'set' : 'not set'}</span>

  return (
    <section>
      <div className="panel-head"><h2>Settings</h2></div>
      <p className="muted">Values entered here override <code>appsettings</code>/env. Leave a secret blank to keep the current value; use <b>Clear</b> to remove an override and fall back to config.</p>
      {msg && <p className="info">{msg}</p>}

      <div className="rule-card">
        <div className="rule-title"><strong>Jira</strong></div>
        <div className="rule-fields">
          <label>Base URL<input type="text" value={jiraUrl} onChange={(e) => setJiraUrl(e.target.value)} /></label>
          <label>User (email)<input type="text" value={jiraUser} onChange={(e) => setJiraUser(e.target.value)} /></label>
          <label>API token ({secretBadge(data.jira.apiTokenSet)})
            <input type="password" value={jiraToken} placeholder="•••••• (unchanged)" onChange={(e) => setJiraToken(e.target.value)} />
          </label>
          <button className="link-btn" disabled={busy} onClick={() => apply({ jiraApiToken: '' }, 'Jira token override cleared.')}>Clear token</button>
        </div>
      </div>

      <div className="rule-card">
        <div className="rule-title"><strong>Teams</strong></div>
        <div className="rule-fields">
          <label>Power Automate DM URL ({secretBadge(data.teams.powerAutomateDmUrlSet)})
            <input type="password" value={teamsUrl} placeholder="•••••• (unchanged)" onChange={(e) => setTeamsUrl(e.target.value)} />
          </label>
          <button className="link-btn" disabled={busy} onClick={() => apply({ teamsPowerAutomateDmUrl: '' }, 'Teams URL override cleared.')}>Clear URL</button>
          <label>Report recipient<input type="text" value={reportRecipient} onChange={(e) => setReportRecipient(e.target.value)} /></label>
        </div>
      </div>

      <div className="rule-card">
        <div className="rule-title"><strong>Anthropic</strong></div>
        <div className="rule-fields">
          <label>API key ({secretBadge(data.anthropic.apiKeySet)})
            <input type="password" value={anthKey} placeholder="•••••• (unchanged)" onChange={(e) => setAnthKey(e.target.value)} />
          </label>
          <button className="link-btn" disabled={busy} onClick={() => apply({ anthropicApiKey: '' }, 'Anthropic key override cleared.')}>Clear key</button>
          <label>Base URL<input type="text" value={anthBase} onChange={(e) => setAnthBase(e.target.value)} /></label>
          <label>Model<input type="text" value={anthModel} onChange={(e) => setAnthModel(e.target.value)} /></label>
          <label>Max tokens<input type="number" value={anthMaxTokens} onChange={(e) => setAnthMaxTokens(e.target.value)} /></label>
        </div>
      </div>

      <button disabled={busy} onClick={saveAll}>{busy ? 'Saving…' : 'Save changes'}</button>
    </section>
  )
}
