namespace YG.Monitoring.DTOs;

public abstract class MonitorOption
{
    public string Name { get; set; } = string.Empty;
    public int RunIntervalInSeconds { get; set; } = 300;


    public string[] EmailRecipients { get; set; } = [];

    public string? EmailSubject { get; set; }
}