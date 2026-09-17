namespace AIManager.Api.Domain;

/// <summary>The kind of compliance a rule chases. New nudge types get added here.</summary>
public enum ChaseRuleType
{
    /// <summary>Chase people who logged too few hours for the previous working day.</summary>
    TimeLog = 0,

    /// <summary>Check tickets for a proper progress update (work done / remaining / estimate).</summary>
    TaskUpdate = 1
}

/// <summary>Where a rule delivers its output.</summary>
public enum ChaseChannel
{
    /// <summary>1:1 Teams DM (via the Power Automate "post as me" flow).</summary>
    TeamsDm = 0,

    /// <summary>A single report to the operator (no messages to the team).</summary>
    Report = 1
}

/// <summary>The result of an individual chase attempt, recorded for the dashboard.</summary>
public enum ChaseOutcome
{
    Sent = 0,
    Failed = 1,
    Skipped = 2
}
