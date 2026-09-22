using System.Net.Http.Headers;
using System.Text;
using AIManager.Api.Configuration;
using AIManager.Api.Data;
using AIManager.Api.Jobs;
using AIManager.Api.Services;
using AIManager.Api.Services.Ai;
using AIManager.Api.Services.Jira;
using AIManager.Api.Services.Settings;
using AIManager.Api.Services.Teams;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

// ---- Options ----
builder.Services.Configure<JiraOptions>(builder.Configuration.GetSection(JiraOptions.SectionName));
builder.Services.Configure<TeamsOptions>(builder.Configuration.GetSection(TeamsOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=aimanager;Username=aimanager;Password=aimanager";

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));

// ---- Settings (DB overrides appsettings/env at runtime) ----
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<AIManager.Api.Services.Compliance.IComplianceSnapshotStore, AIManager.Api.Services.Compliance.ComplianceSnapshotStore>();

// ---- External services (typed HttpClients; URL/auth resolved per request from settings) ----
builder.Services.AddHttpClient<IJiraService, JiraService>();
builder.Services.AddHttpClient<ITeamsService, TeamsService>();
builder.Services.AddHttpClient<IUpdateJudge, AnthropicUpdateJudge>(c => c.Timeout = TimeSpan.FromSeconds(60));

// ---- Jobs ----
builder.Services.AddScoped<TimeLogChaserJob>();
builder.Services.AddScoped<TaskUpdateTrackerJob>();
builder.Services.AddScoped<SupportDigestJob>();

// ---- Hangfire ----
builder.Services.AddHangfire(cfg => cfg
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer(o => o.ServerName = "aimanager-worker");

// ---- Web ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5173", "http://localhost:4173")
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ---- Migrate + seed ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
    await DbSeeder.EnsureSupportDigestAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    IgnoreAntiforgeryToken = true,
    Authorization = new List<Hangfire.Dashboard.IDashboardAuthorizationFilter>()
});

RegisterRecurringJobs(app.Services);

app.MapControllers();
app.Run();

// ---- Recurring schedules (each rule's ;-separated crons, in the team timezone) ----
static void RegisterRecurringJobs(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var manager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    // All rules (not only enabled) so disabled rules' stale schedules are cleaned up.
    foreach (var rule in db.ChaseRules.Include(r => r.Team).ToList())
        RecurringJobs.Sync(manager, rule);
}
