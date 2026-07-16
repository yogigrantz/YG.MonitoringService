namespace YG.Monitoring.DTOs;

public sealed class MonitorActionOption
{
    public string Name { get; set; } = string.Empty;
    public int RunIntervalInSeconds { get; set; } = 300;

    public string ConnectionString { get; set; } = string.Empty;

    public string SqlCommand { get; set; } = string.Empty;

    public string[] EmailRecipients { get; set; } = [];

    public string? EmailSubject { get; set; }
    public int ResilienceNbrOfRetries { get; set; } = 3;
    public int ResilienceWaitInMs { get; set; } = 800;
}