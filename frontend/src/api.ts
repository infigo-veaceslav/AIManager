// Typed client for the AIManager API. Paths are relative and dev-proxied to the .NET backend.

export interface Member {
  id: number
  displayName: string
  email: string
  jiraAccountId: string | null
  active: boolean
}

export interface Team {
  id: number
  name: string
  timezone: string
  enabled: boolean
  members: Member[]
}

export interface Rule {
  id: number
  teamId: number
  teamName: string
  type: 'TimeLog' | 'TaskUpdate'
  enabled: boolean
  cron: string
  thresholdHours: number | null
  messageTemplate: string | null
  channel: 'TeamsDm' | 'Report'
  testRecipientOverride: string | null
  configJson: string | null
}

export interface ChaseEvent {
  id: number
  ruleId: number
  type: string
  memberId: number | null
  memberName: string | null
  targetDate: string
  reason: string
  outcome: 'Sent' | 'Failed' | 'Skipped'
  deliveredTo: string | null
  messageText: string | null
  detailJson: string | null
  createdAtUtc: string
}

export interface Finding {
  id: number
  targetDate: string
  issueKey: string
  summary: string | null
  assignee: string | null
  assigneeEmail: string | null
  status: string | null
  hasWorkDone: boolean
  hasRemaining: boolean
  hasEstimate: boolean
  isComplete: boolean
  missing: string | null
  verdict: string | null
  createdAtUtc: string
}

export interface ComplianceRow {
  memberId: number
  displayName: string
  email: string
  hours: number
  ok: boolean
}

export interface ComplianceResult {
  teamId: number
  teamName: string
  targetDate: string
  threshold: number
  rows: ComplianceRow[]
}

export interface SettingsDto {
  jira: { url: string; user: string; apiTokenSet: boolean }
  teams: { powerAutomateDmUrlSet: boolean; reportRecipient: string }
  anthropic: { apiKeySet: boolean; baseUrl: string; model: string; maxTokens: number }
}

export interface SettingsUpdate {
  jiraUrl?: string
  jiraUser?: string
  jiraApiToken?: string
  teamsPowerAutomateDmUrl?: string
  teamsReportRecipient?: string
  anthropicApiKey?: string
  anthropicBaseUrl?: string
  anthropicModel?: string
  anthropicMaxTokens?: number
}

async function http<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return text ? (JSON.parse(text) as T) : (undefined as T)
}

export const api = {
  getTeams: () => http<Team[]>('/api/teams'),
  getRules: () => http<Rule[]>('/api/rules'),
  updateRule: (id: number, body: Partial<Rule>) =>
    http<void>(`/api/rules/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  runRule: (id: number) => http<{ jobId: string }>(`/api/rules/${id}/run`, { method: 'POST' }),
  updateMember: (id: number, body: { displayName?: string; active?: boolean; jiraAccountId?: string }) =>
    http<void>(`/api/members/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  getChaseEvents: (params: { ruleId?: number; take?: number } = {}) => {
    const q = new URLSearchParams()
    if (params.ruleId != null) q.set('ruleId', String(params.ruleId))
    q.set('take', String(params.take ?? 200))
    return http<ChaseEvent[]>(`/api/chase-events?${q.toString()}`)
  },
  getFindings: (onlyIncomplete = false) =>
    http<Finding[]>(`/api/findings?onlyIncomplete=${onlyIncomplete}`),
  getCompliance: (teamId?: number) =>
    http<ComplianceResult>(`/api/compliance${teamId ? `?teamId=${teamId}` : ''}`),
  getSettings: () => http<SettingsDto>('/api/settings'),
  updateSettings: (body: SettingsUpdate) =>
    http<void>('/api/settings', { method: 'PUT', body: JSON.stringify(body) }),
}
