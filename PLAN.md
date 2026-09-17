# Team Chaser — AI compliance platform (time logging + ticket updates)

## Context
The dev team is inconsistent about (a) logging time in Jira daily and (b) leaving meaningful progress
updates on tickets. We want an "AI chaser" that nudges people, and — importantly — a **web app to see
the whole picture** (compliance, chase history) that will **grow** into more nudge types, escalation,
and other teams beyond dev.

An existing n8n workflow (`Dev - Loging time`, `Jo6HdUNVru0fSXyn`) already proves the core Jira logic:
weekend-aware "previous working day", per-dev worklog totals (including 0h people), and Teams delivery
via a Power Automate HTTP flow. We port that logic into this app; n8n is **not** used going forward.

### Decisions (locked via Q&A)
- **New standalone app** (this repo), built directly — no n8n interim step.
- Backend: **.NET 8 Web API**; DB: **Postgres + EF Core**; scheduler: **Hangfire**; Jira: **Atlassian.SDK**.
  Copy proven patterns from `D:\Repositories\Reporting` (see Reuse section).
- Frontend: **React + Vite + TypeScript** SPA.
- Jira data: **live queries each run**, persist **roster + rules + chase-event history** locally.
- Teams DM appears **as Veaceslav**, sent via the existing **Power Automate "Post as User"** HTTP flow.
- **Auth deferred** for MVP (internal network only); data model still designed for multi-team/escalation.
- Time-log threshold: **< 5h** for the previous working day. First live test: **Andrian Gaidarji only**.
- Task-update tracker: **report to Veaceslav only** (no team messages yet).
- LLM for update-quality judgement: **Anthropic Claude** via API (model configurable, default Sonnet).

---

## Target architecture
```
React+Vite SPA  --HTTP-->  .NET 8 Web API  -->  Postgres (roster, rules, chase history)
                                |
                                +- Hangfire recurring jobs (chaser schedules)
                                +- JiraService (Atlassian.SDK, live queries)
                                +- TeamsService (HttpClient -> Power Automate "post as me" flow)
                                +- UpdateJudgeService (Anthropic Claude API)
```
Repo: **`AIManager`** — GitHub `infigo-veaceslav/AIManager`, local `D:\Repositories\AIManager`
(currently empty). Layout: `/backend` (.NET solution) + `/frontend` (React) + `docker-compose.yml`
(api + postgres) + `README.md`.
> **First step:** the local `D:\Repositories\AIManager` is not yet connected to the GitHub repo (git
> there resolves to a parent `tooling` Bitbucket remote). Initialize it properly against
> `https://github.com/infigo-veaceslav/AIManager` before scaffolding.

## Data model (EF Core -> Postgres) — built for growth from day one
- **Team**(Id, Name, Timezone[default Europe/Chisinau], Enabled)
- **TeamMember**(Id, TeamId, DisplayName, Email, JiraAccountId, Active)
- **ChaseRule**(Id, TeamId, Type[`TimeLog`|`TaskUpdate`|future], Enabled, Cron, ThresholdHours,
  ConfigJson[JQL/params], MessageTemplate, Channel[`TeamsDM`|`Report`], TestRecipientOverride[nullable])
- **ChaseEvent**(Id, RuleId, MemberId?, TargetDate, Reason, DetailJson, Outcome[`Sent`|`Failed`|`Skipped`],
  MessageText, IssueKeys[], CreatedAt) — the "whole picture" history the dashboard renders
- *(future)* **EscalationState**(RuleId, MemberId, ConsecutiveMisses, LastEscalatedAt)

## Backend milestones
- **M0 Scaffold** — .NET 8 Web API solution; EF Core + Npgsql + migrations; Hangfire (Postgres storage
  + `/hangfire` UI); Serilog; `docker-compose.yml` (api + postgres:15); config for Jira creds, PA flow
  URL, Anthropic key. *(Copy shape from Reporting `Program.cs`, `Startup.cs`,
  `Reporting.Background/ServiceExtension.cs`, `docker-compose.yml`.)*
- **M1 JiraService** (Atlassian.SDK / REST, credentials = Development Agent):
  - `WorklogTotalsPerMember(teamId, date)` — port the n8n aggregation: JQL `worklogDate` by member
    `JiraAccountId`s, sum `timeSpentSeconds` in the team timezone, **include 0h members**.
  - `ActiveOrWorkloggedTasks(date)` — JQL `status = Active OR worklogDate = <prev working day>`, dev scope.
  - `IssueComments(key, since)`.
  - `PreviousWorkingDay(today, tz)` helper — port the JS in `Identify target day`.
- **M2 TeamsService** — `SendDmAsMe(recipientEmail, htmlMessage)` -> HttpClient POST `{recipient, message}`
  to the Power Automate flow URL. Records success/failure.
- **M3 Time-log chaser job** (Hangfire recurring, **daily 10:00 Europe/Chisinau**):
  for each enabled `TimeLog` rule -> compute totals for prev working day -> members `< ThresholdHours`
  -> render message from template -> `SendDmAsMe` (honoring `TestRecipientOverride`) -> write `ChaseEvent`.
  **First test:** seed one Team (dev) + Andrian as the only active member (or a rule scoped to him),
  threshold 5h, `TestRecipientOverride = veaceslav.andreev@infigo.net` so the first DMs land on Veaceslav.
- **M4 Task-update tracker job** (daily): `ActiveOrWorkloggedTasks` -> fetch recent comments ->
  `UpdateJudgeService` (Claude) returns
  `{key, assignee, hasWorkDone, hasRemaining, hasEstimate, missing[], verdict}`
  -> persist findings -> compile report -> deliver to Veaceslav (Teams DM to self). Report-only.
- **M5 API for SPA** — endpoints: teams/members CRUD, rules list+toggle, chase-event history (filter by
  date/member/type), task-update findings, **"run now"** trigger per rule (for manual validation).

## Frontend milestones
- **F0** — React + Vite + TS scaffold, typed API client, app layout, table/UI lib (shadcn or MUI).
- **F1 Dashboard** — views: **Today's compliance** (per-member hours vs threshold), **Chase history**
  (who/when/why/outcome), **Task-update findings** (tickets missing work-done/remaining/estimate),
  **Rules** (enable/disable, thresholds, test-recipient), **Run now** buttons. No login (internal MVP);
  layout structured so Azure AD SSO drops in later.

## External setup (Veaceslav — needed to go live, not to build)
1. **Power Automate DM flow**: `When an HTTP request is received` (JSON `{recipient, message}`) ->
   `Post message in a chat or channel`, **Post as = User**, **Post in = Group chat**, Recipient =
   `recipient`, Message = `message`. Provide the trigger URL for app config.
2. **Jira API token** for the app (Development Agent user).
3. **Anthropic API key**.

## Reuse (copy patterns from Reporting)
- Hangfire config + recurring-job registration: `reporting\Reporting.Background\ServiceExtension.cs`
- EF Core DbContext / entity conventions: `reporting\Reporting.Domain\ReportingContext.cs`
- Jira client wrapper + config schema: `reporting\Services\Jira.Services\*`,
  `reporting\Infrastructure\Infigo.Configuration\JiraConfiguration.cs`
- Container setup: `reporting\Dockerfile`, `reporting\docker-compose.yml`
- App bootstrap: `reporting\Reporting.Presentation\Program.cs`, `Startup.cs`
- Worklog aggregation + prev-working-day logic to port: n8n workflow `Jo6HdUNVru0fSXyn`
  (nodes "Aggregate hours per user" and "Identify target day").

## Verification (end-to-end)
1. `docker-compose up` (api + postgres); run EF migrations; `npm run dev` for the SPA.
2. Seed dev team + Andrian, one `TimeLog` rule (threshold 5h, `TestRecipientOverride` = Veaceslav).
3. Hit **"run now"** on the rule (or trigger the Hangfire job): confirm a DM arrives to Veaceslav with
   correct name/date/hours, and a `ChaseEvent` row is written and shows in the dashboard.
4. Run the task-update job manually on a small date window; eyeball findings vs a few real tickets,
   tune the Claude prompt to cut false "missing" flags; confirm the report DM to Veaceslav.
5. Once wording/accuracy approved: remove `TestRecipientOverride`, broaden the rule to the whole dev
   team, enable the Hangfire schedules.

## Out of scope (designed-for, built later)
- Azure AD SSO + role-based views. Escalation (repeat-offender tracking, lead/manager CC, weekly
  summaries). Additional nudge types (stand-up, PR review, missing estimates). Rollout to CSM/Support.

## Cleanup
- Delete the throwaway n8n scaffold `[Teams] Dev - Log time chaser DM (TEST: Andrian)`
  (`TDvbYWbaMA2TiKAR`, currently inactive) — superseded by this app.
