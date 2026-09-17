# AIManager

Internal "AI chaser" compliance platform. It nudges the dev team to (1) log time in Jira daily and
(2) keep meaningful progress updates on their tickets, and provides a web dashboard to see the whole
picture (compliance + chase history). Designed to grow into more nudge types, escalation, and other
teams.

See [`PLAN.md`](./PLAN.md) for the full architecture and roadmap.

## Stack
- **Backend:** .NET 8 Web API, EF Core + PostgreSQL, Hangfire (scheduling), Serilog.
- **Frontend:** React + Vite + TypeScript SPA.
- **Integrations:** Jira Cloud (REST v3), Microsoft Teams (via a Power Automate "post as user" HTTP
  flow), Anthropic Claude (ticket-update quality judgement).

## Layout
```
backend/          .NET 8 solution (AIManager.Api)
frontend/         React + Vite + TS SPA
docker-compose.yml  api + postgres for local dev
```

## Local development
1. `docker-compose up -d postgres` (or run the whole stack with `docker-compose up`).
2. Backend: `cd backend/AIManager.Api && dotnet run` — API on http://localhost:5080, Hangfire UI at
   `/hangfire`, Swagger at `/swagger`. Migrations apply and the dev team seeds automatically on boot.
3. Frontend: `cd frontend && npm install && npm run dev` — dashboard on http://localhost:5173
   (dev-proxied to the API).

## Configuration
Two ways to configure — the **Settings tab** in the dashboard is the primary one:

- **Settings tab (recommended):** enter Jira token, Power Automate DM URL, Anthropic key, model, and
  report recipient in the UI. Values are stored in the DB, take effect immediately (no restart), and
  override the config file. Secrets are shown only as *set / not set* and are never echoed back.
- **Config file / env (fallback):** any value left blank in the DB falls back to
  `appsettings.Development.json` (copy from `appsettings.example.json`) or the environment variables
  in `.env` (copy from `.env.example`). Both files are git-ignored.

| Key | Purpose |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | Postgres connection string |
| `Jira:Url` / `Jira:User` / `Jira:ApiToken` | Jira Cloud REST access (basic auth) |
| `Teams:PowerAutomateDmUrl` | Power Automate HTTP trigger that posts a 1:1 DM as the user |
| `Teams:ReportRecipient` | Who the task-update report is sent to |
| `Anthropic:ApiKey` / `Anthropic:Model` | Claude API for ticket-update judgement |

> **Security:** never commit real secrets. DB-stored settings, `appsettings.Development.json`, and
> `.env` are all kept out of git.
