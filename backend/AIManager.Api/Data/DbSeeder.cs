using AIManager.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Data;

/// <summary>
/// Seeds the dev team roster and the initial validation rules. Idempotent: only runs when the
/// Teams table is empty. During validation only Andrian is Active and DMs are redirected to the
/// operator (TestRecipientOverride); flip those to go live.
/// </summary>
public static class DbSeeder
{
    private const string Operator = "veaceslav.andreev@infigo.net";

    private static readonly (string Name, string Email, bool ActiveForTest)[] DevRoster =
    {
        ("Victor Bodarev", "victor.bodarev@infigo.net", false),
        ("Dorin Buraga", "dorin.buraga@infigo.net", false),
        ("George Vragalev", "george.vragalev@infigo.net", false),
        ("Vladislav Crucerescu", "vladislav.crucerescu@infigo.net", false),
        ("Artur Paraschiv", "artur.paraschiv@infigo.net", false),
        ("Andrian Gaidarji", "andrian.gaidarji@infigo.net", true), // <-- the single test subject
        ("Adrian Gherman", "adrian.gherman@infigo.net", false),
        ("Alina Ataman", "alina.ataman@infigo.net", false),
        ("Ion Tentiuc", "ion.tentiuc@infigo.net", false),
        ("Livia Coada", "livia.coada@infigo.net", false),
        ("Alex Olaras", "alex.olaras@infigo.net", false),
        ("Tatiana Gavrilita", "tatiana.gavrilita@infigo.net", false),
    };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Teams.AnyAsync(ct)) return;

        var team = new Team
        {
            Name = "Development",
            Timezone = "Europe/Chisinau",
            WorkingDays = "1,2,3,4", // Mon–Thu (no Fridays)
            Enabled = true,
            Members = DevRoster.Select(r => new TeamMember
            {
                DisplayName = r.Name,
                Email = r.Email,
                Active = r.ActiveForTest
            }).ToList()
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);

        db.ChaseRules.AddRange(
            new ChaseRule
            {
                TeamId = team.Id,
                Type = ChaseRuleType.TimeLog,
                Enabled = true,
                Cron = "0 10 * * 1-4",
                ThresholdHours = 5,
                Channel = ChaseChannel.TeamsDm,
                TestRecipientOverride = Operator, // redirect test DMs to the operator
                MessageTemplate = null            // uses the job's default template
            },
            new ChaseRule
            {
                TeamId = team.Id,
                Type = ChaseRuleType.TaskUpdate,
                Enabled = true,
                Cron = "0 9 * * 1-4",
                Channel = ChaseChannel.Report,
                TestRecipientOverride = Operator, // report goes to the operator
                ConfigJson = "{\"scopeJql\":\"project = \\\"Venture - Catfish Main Development\\\"\",\"lookbackDays\":2,\"maxIssues\":40}"
            });

        await db.SaveChangesAsync(ct);
    }

    private const string SupportDigestDefaultConfig =
        "{\"projectKey\":\"SUP\"," +
        "\"assigneeStatuses\":[\"Triage\",\"Assigned\",\"In Progress\",\"Needs assignee attention\",\"Support Backlog\"]," +
        "\"reporterStatuses\":[\"Customer Feedback\",\"Needs reporter attention\"]," +
        "\"reporterFilter\":[]," +
        "\"assigneeFilter\":[]," +
        "\"operatorEmail\":\"veaceslav.andreev@infigo.net\"," +
        "\"operatorName\":\"Veaceslav Andreev\"," +
        "\"maxPerPerson\":0," +
        "\"mention\":false}";

    /// <summary>
    /// Idempotently ensures a Support team + a (disabled) SupportDigest rule exist — runs even on an
    /// already-populated DB. Enable it after registering the destination channel.
    /// </summary>
    public static async Task EnsureSupportDigestAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.ChaseRules.AnyAsync(r => r.Type == ChaseRuleType.SupportDigest, ct)) return;

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Name == "Support", ct);
        if (team is null)
        {
            team = new Team { Name = "Support", Timezone = "Europe/Chisinau", WorkingDays = "1,2,3,4", Enabled = true };
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
        }

        db.ChaseRules.Add(new ChaseRule
        {
            TeamId = team.Id,
            Type = ChaseRuleType.SupportDigest,
            Enabled = false, // turn on once the destination channel is set
            Cron = "0 10 * * 1-4",
            Channel = ChaseChannel.Report,
            ConfigJson = SupportDigestDefaultConfig
        });
        await db.SaveChangesAsync(ct);
    }
}
