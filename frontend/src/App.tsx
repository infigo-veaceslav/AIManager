import { useCallback, useEffect, useState } from 'react'
import './App.css'
import {
  api,
  type Channel,
  type ChaseEvent,
  type ComplianceResult,
  type Finding,
  type Rule,
  type SettingsDto,
  type SettingsUpdate,
  type Team,
  type User,
} from './api'

type Tab = 'compliance' | 'history' | 'findings' | 'rules' | 'roster' | 'channels' | 'users' | 'settings'

const TABS: { key: Tab; label: string }[] = [
  { key: 'compliance', label: 'Compliance' },
  { key: 'findings', label: 'Task updates' },
  { key: 'history', label: 'Chase history' },
  { key: 'rules', label: 'Rules' },
  { key: 'roster', label: 'Roster' },
  { key: 'channels', label: 'Channels' },
  { key: 'users', label: 'Users' },
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
        {tab === 'channels' && <ChannelsPanel />}
        {tab === 'users' && <UsersPanel />}
        {tab === 'settings' && <SettingsPanel />}
      </main>
    </div>
  )
}

// Format an ISO/UTC timestamp in the browser's local timezone as "YYYY-MM-DD HH:mm".
function fmt(iso: string) {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
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
  const { data, error, reload } = useAsync<ComplianceResult>(() => api.getCompliance())
  const [resyncing, setResyncing] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const resync = async () => {
    setResyncing(true); setMsg(null)
    try { await api.resyncCompliance(); await reload() }
    catch { setMsg('Resync failed — Jira may be throttling/unavailable. Try again shortly.') }
    finally { setResyncing(false) }
  }
  return (
    <section>
      <div className="panel-head">
        <h2>Compliance — previous working day</h2>
        <button onClick={resync} disabled={resyncing}>{resyncing ? 'Resyncing…' : 'Resync from Jira'}</button>
      </div>
      {error && <p className="error">{error}</p>}
      {msg && <p className="error">{msg}</p>}
      {data && (
        <>
          <p className="muted">
            {data.teamName} · {data.targetDate} · threshold {data.threshold}h ·{' '}
            {data.lastSyncedAt
              ? `synced ${fmt(data.lastSyncedAt)}`
              : 'never synced — click Resync to load'}{' '}
            · showing all members ({data.rows.filter((r) => r.active).length} in chase scope)
          </p>
          <table>
            <thead><tr><th>Member</th><th>Hours</th><th>Status</th><th>Chase scope</th></tr></thead>
            <tbody>
              {data.rows.map((r) => (
                <tr key={r.memberId} className={r.active ? '' : 'row-muted'}>
                  <td>{r.displayName}</td>
                  <td>{r.hours.toFixed(2)}h</td>
                  <td>{r.ok ? <span className="ok">OK</span> : <span className="bad">Under</span>}</td>
                  <td>{r.active ? <span className="ok">✓ chased</span> : <span className="muted">not chased</span>}</td>
                </tr>
              ))}
              {data.rows.length === 0 && <tr><td colSpan={4} className="muted">No members.</td></tr>}
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
          <tr><th>When</th><th>Type</th><th>Trigger</th><th>Target</th><th>Who</th><th>Reason</th><th>Outcome</th><th>Delivered to</th></tr>
        </thead>
        <tbody>
          {(data ?? []).map((e) => (
            <tr key={e.id}>
              <td>{fmt(e.createdAtUtc)}</td>
              <td>{e.type}</td>
              <td><span className={e.trigger === 'Manual' ? 'info' : 'muted'}>{e.trigger}</span></td>
              <td>{e.targetDate}</td>
              <td>{e.memberName ?? '—'}</td>
              <td>{e.reason}</td>
              <td><span className={e.outcome === 'Sent' ? 'ok' : e.outcome === 'Failed' ? 'bad' : 'muted'}>{e.outcome}</span></td>
              <td>{e.deliveredTo ?? '—'}</td>
            </tr>
          ))}
          {data && data.length === 0 && <tr><td colSpan={8} className="muted">No chase events yet.</td></tr>}
        </tbody>
      </table>
    </section>
  )
}

function UserMultiSelect({ label, users, selected, onChange, disabled }: {
  label: string
  users: User[]
  selected: string[]
  onChange: (emails: string[]) => void
  disabled?: boolean
}) {
  const [q, setQ] = useState('')
  const sel = new Set(selected.map((e) => e.toLowerCase()))
  const active = users.filter((u) => u.active)
  const filtered = q ? active.filter((u) => `${u.displayName} ${u.email}`.toLowerCase().includes(q.toLowerCase())) : active
  const toggle = (email: string) => {
    const e = email.toLowerCase()
    onChange(sel.has(e) ? selected.filter((x) => x.toLowerCase() !== e) : [...selected, email])
  }
  return (
    <details className="multiselect">
      <summary>{label}: {selected.length ? `${selected.length} selected` : 'all'}</summary>
      <div className="ms-panel">
        <input className="ms-search" placeholder="search…" value={q} onChange={(e) => setQ(e.target.value)} />
        <div className="ms-list">
          {filtered.map((u) => (
            <label key={u.id} className="ms-item">
              <input type="checkbox" checked={sel.has(u.email.toLowerCase())} disabled={disabled} onChange={() => toggle(u.email)} />
              <span>{u.displayName} <span className="muted">{u.email}</span></span>
            </label>
          ))}
          {active.length === 0 && <p className="muted">No users yet — add some in the Users tab.</p>}
        </div>
        {selected.length > 0 && (
          <button type="button" className="link-btn" disabled={disabled} onClick={() => onChange([])}>Clear</button>
        )}
      </div>
    </details>
  )
}

function UsersPanel() {
  const { data, reload } = useAsync<User[]>(() => api.getUsers())
  const cfg = useAsync<{ projectKeys: string; boardIds: string }>(() => api.getUserSyncConfig())
  const [projectKeys, setProjectKeys] = useState('')
  const [boardIds, setBoardIds] = useState('')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  useEffect(() => {
    if (cfg.data) { setProjectKeys(cfg.data.projectKeys); setBoardIds(cfg.data.boardIds) }
  }, [cfg.data])

  const saveCfg = async () => { try { await api.putUserSyncConfig({ projectKeys, boardIds }) } catch { /* ignore */ } }
  const add = async () => {
    if (!name.trim() || !email.trim()) { setMsg('Name and email are required.'); return }
    setBusy(true); setMsg(null)
    try { await api.createUser({ displayName: name.trim(), email: email.trim() }); setName(''); setEmail(''); await reload() }
    catch (e) { setMsg(String(e)) } finally { setBusy(false) }
  }
  const toggle = async (id: number, active: boolean) => { await api.updateUser(id, { active }); await reload() }
  const remove = async (id: number) => { setBusy(true); try { await api.deleteUser(id); await reload() } finally { setBusy(false) } }
  const sync = async () => {
    setBusy(true); setMsg(null)
    try { await saveCfg(); const r = await api.syncUsers(); setMsg(`Synced from Jira: +${r.added} new, ${r.updated} updated, ${r.total} total.`); await reload() }
    catch { setMsg('Sync failed — Jira may be throttling/unavailable.') } finally { setBusy(false) }
  }
  const importRoster = async () => {
    setBusy(true); setMsg(null)
    try { const r = await api.importRoster(); setMsg(`Imported roster: +${r.added} new, ${r.updated} updated, ${r.total} total.`); await reload() }
    catch (e) { setMsg(String(e)) } finally { setBusy(false) }
  }

  return (
    <section>
      <div className="panel-head"><h2>Users ({data?.length ?? 0})</h2></div>
      <p className="muted">Master directory of people the platform knows — used to pick reporters/assignees for rules. Independent of the dev Roster.</p>
      {msg && <p className="info">{msg}</p>}

      <div className="rule-card">
        <div className="rule-title"><strong>Populate</strong></div>
        <div className="rule-fields">
          <label>Sync project keys<input value={projectKeys} onChange={(e) => setProjectKeys(e.target.value)} onBlur={saveCfg} placeholder="SUP,VENTURE" /></label>
          <label>Sync board ids<input value={boardIds} onChange={(e) => setBoardIds(e.target.value)} onBlur={saveCfg} placeholder="e.g. the DR board id" /></label>
        </div>
        <button disabled={busy} onClick={sync}>{busy ? 'Working…' : 'Sync from Jira'}</button>
        <button className="link-btn" disabled={busy} onClick={importRoster}>Import dev roster</button>
      </div>

      <table>
        <thead><tr><th>Name</th><th>Email</th><th>Jira</th><th>Source</th><th>Active</th><th></th></tr></thead>
        <tbody>
          {(data ?? []).map((u) => (
            <tr key={u.id} className={u.active ? '' : 'row-muted'}>
              <td>{u.displayName}</td>
              <td>{u.email}</td>
              <td className="muted">{u.jiraAccountId ? '✓' : '—'}</td>
              <td className="muted">{u.source}</td>
              <td><input type="checkbox" checked={u.active} disabled={busy} onChange={(e) => toggle(u.id, e.target.checked)} /></td>
              <td><button className="link-btn" disabled={busy} onClick={() => remove(u.id)}>Delete</button></td>
            </tr>
          ))}
          {data && data.length === 0 && <tr><td colSpan={6} className="muted">No users yet — Sync from Jira or Import dev roster.</td></tr>}
        </tbody>
      </table>

      <div className="rule-card">
        <div className="rule-title"><strong>Add user</strong></div>
        <div className="rule-fields">
          <label>Name<input value={name} onChange={(e) => setName(e.target.value)} /></label>
          <label>Email<input value={email} onChange={(e) => setEmail(e.target.value)} /></label>
        </div>
        <button disabled={busy} onClick={add}>Add user</button>
      </div>
    </section>
  )
}

function SupportConfig({ rule, save, busy, users }: {
  rule: Rule
  save: (id: number, body: Partial<Rule>) => void
  busy: boolean
  users: User[]
}) {
  let cfg: Record<string, unknown> = {}
  try { cfg = rule.configJson ? JSON.parse(rule.configJson) : {} } catch { cfg = {} }
  const emails = (a: unknown) => (Array.isArray(a) ? (a as string[]) : [])
  const patch = (p: Record<string, unknown>) => save(rule.id, { configJson: JSON.stringify({ ...cfg, ...p }) })

  return (
    <div className="rule-fields">
      <UserMultiSelect label="Reporters to cover" users={users} selected={emails(cfg.reporterFilter)} disabled={busy}
        onChange={(v) => patch({ reporterFilter: v })} />
      <UserMultiSelect label="Assignees to cover" users={users} selected={emails(cfg.assigneeFilter)} disabled={busy}
        onChange={(v) => patch({ assigneeFilter: v })} />
      <label>Max per person (0 = no cap)
        <input type="number" defaultValue={Number(cfg.maxPerPerson ?? 0)} disabled={busy}
          onBlur={(e) => { if (Number(e.target.value) !== Number(cfg.maxPerPerson ?? 0)) patch({ maxPerPerson: Number(e.target.value) }) }} />
      </label>
      <label className="check">
        <input type="checkbox" checked={!!cfg.mention} disabled={busy}
          onChange={(e) => patch({ mention: e.target.checked })} />
        @mention people (needs the mentions n8n workflow)
      </label>
    </div>
  )
}

function RulesPanel() {
  const { data, error, reload } = useAsync<Rule[]>(() => api.getRules())
  const channels = useAsync<Channel[]>(() => api.getChannels())
  const users = useAsync<User[]>(() => api.getUsers())
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
            {r.type === 'TimeLog' && (
              <label>Delivery
                <select value={r.deliveryMode} disabled={busy === r.id}
                  onChange={(e) => save(r.id, { deliveryMode: e.target.value as Rule['deliveryMode'] })}>
                  <option value="PerPersonDm">Per-person DM</option>
                  <option value="ChannelSummary">Channel summary</option>
                  <option value="Both">Both</option>
                </select>
              </label>
            )}
            <label>{r.type === 'TaskUpdate' ? 'Report to channel' : r.type === 'SupportDigest' ? 'Post to channel' : 'Summary channel'}
              <select value={r.destinationChannelId ?? 0} disabled={busy === r.id}
                onChange={(e) => save(r.id, { destinationChannelId: Number(e.target.value) })}>
                <option value={0}>{r.type === 'TaskUpdate' ? '(DM to operator)' : '(none)'}</option>
                {(channels.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            </label>
          </div>
          {r.type === 'SupportDigest' && <SupportConfig rule={r} save={save} busy={busy === r.id} users={users.data ?? []} />}
          {r.type === 'TaskUpdate' && (
            <label className="config-editor">Config (JSON)
              <textarea defaultValue={r.configJson ?? ''} rows={7} spellCheck={false}
                onBlur={(e) => {
                  const val = e.target.value.trim()
                  if (val === (r.configJson ?? '')) return
                  if (val) { try { JSON.parse(val) } catch { setMsg('Invalid JSON — not saved'); return } }
                  save(r.id, { configJson: val })
                }} />
            </label>
          )}
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

function ChannelsPanel() {
  const { data, reload } = useAsync<Channel[]>(() => api.getChannels())
  const [name, setName] = useState('')
  const [teamId, setTeamId] = useState('')
  const [channelId, setChannelId] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  const add = async () => {
    if (!name || !teamId || !channelId) { setMsg('Name, Team ID and Channel ID are all required.'); return }
    setBusy(true); setMsg(null)
    try {
      await api.createChannel({ name, teamId, channelId })
      setName(''); setTeamId(''); setChannelId(''); await reload()
    } catch (e) { setMsg(String(e)) } finally { setBusy(false) }
  }
  const remove = async (id: number) => {
    setBusy(true)
    try { await api.deleteChannel(id); await reload() } finally { setBusy(false) }
  }

  return (
    <section>
      <div className="panel-head"><h2>Channels</h2></div>
      <p className="muted">
        Register Teams channels here, then pick one as a rule's summary/report destination. Get the
        Team ID &amp; Channel ID from the channel's ••• → <b>Get link to channel</b> — the URL contains
        <code>groupId=&lt;Team ID&gt;</code> and the channel id after <code>/channel/</code>.
      </p>
      {msg && <p className="error">{msg}</p>}
      <table>
        <thead><tr><th>Name</th><th>Team ID</th><th>Channel ID</th><th></th></tr></thead>
        <tbody>
          {(data ?? []).map((c) => (
            <tr key={c.id}>
              <td>{c.name}</td>
              <td className="muted">{c.teamId}</td>
              <td className="muted">{c.channelId}</td>
              <td><button className="link-btn" disabled={busy} onClick={() => remove(c.id)}>Delete</button></td>
            </tr>
          ))}
          {data && data.length === 0 && <tr><td colSpan={4} className="muted">No channels registered yet.</td></tr>}
        </tbody>
      </table>
      <div className="rule-card">
        <div className="rule-title"><strong>Add channel</strong></div>
        <div className="rule-fields">
          <label>Name<input value={name} onChange={(e) => setName(e.target.value)} placeholder="Dev — Standup" /></label>
          <label>Team ID (groupId)<input value={teamId} onChange={(e) => setTeamId(e.target.value)} /></label>
          <label>Channel ID<input value={channelId} onChange={(e) => setChannelId(e.target.value)} /></label>
        </div>
        <button disabled={busy} onClick={add}>Add channel</button>
      </div>
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
          <label>Power Automate Teams flow URL ({secretBadge(data.teams.powerAutomateDmUrlSet)})
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
