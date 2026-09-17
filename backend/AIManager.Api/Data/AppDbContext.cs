using AIManager.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<ChaseRule> ChaseRules => Set<ChaseRule>();
    public DbSet<ChaseEvent> ChaseEvents => Set<ChaseEvent>();
    public DbSet<TaskUpdateFinding> TaskUpdateFindings => Set<TaskUpdateFinding>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Team>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Timezone).IsRequired().HasMaxLength(100);
        });

        b.Entity<TeamMember>(e =>
        {
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Email).IsRequired().HasMaxLength(320);
            e.HasIndex(x => x.Email);
            e.Property(x => x.JiraAccountId).HasMaxLength(128);
            e.HasOne(x => x.Team).WithMany(t => t.Members).HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChaseRule>(e =>
        {
            e.Property(x => x.Cron).IsRequired().HasMaxLength(100);
            e.Property(x => x.MessageTemplate).HasColumnType("text");
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
            e.Property(x => x.TestRecipientOverride).HasMaxLength(320);
            e.HasOne(x => x.Team).WithMany(t => t.Rules).HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChaseEvent>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DetailJson).HasColumnType("jsonb");
            e.Property(x => x.MessageText).HasColumnType("text");
            e.Property(x => x.DeliveredTo).HasMaxLength(320);
            e.HasIndex(x => new { x.RuleId, x.TargetDate });
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AppSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.JiraUrl).HasMaxLength(500);
            e.Property(x => x.JiraUser).HasMaxLength(320);
            e.Property(x => x.JiraApiToken).HasColumnType("text");
            e.Property(x => x.TeamsPowerAutomateDmUrl).HasColumnType("text");
            e.Property(x => x.TeamsReportRecipient).HasMaxLength(320);
            e.Property(x => x.AnthropicApiKey).HasColumnType("text");
            e.Property(x => x.AnthropicBaseUrl).HasMaxLength(500);
            e.Property(x => x.AnthropicModel).HasMaxLength(100);
        });

        b.Entity<TaskUpdateFinding>(e =>
        {
            e.Ignore(x => x.IsComplete);
            e.Property(x => x.IssueKey).IsRequired().HasMaxLength(50);
            e.Property(x => x.Summary).HasColumnType("text");
            e.Property(x => x.Verdict).HasColumnType("text");
            e.Property(x => x.Missing).HasMaxLength(200);
            e.HasIndex(x => new { x.TargetDate, x.IssueKey });
        });
    }
}
