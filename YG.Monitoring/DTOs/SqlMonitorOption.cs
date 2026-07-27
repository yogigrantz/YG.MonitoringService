namespace YG.Monitoring.DTOs;

public class SqlMonitorOption : MonitorOption
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SqlCommand { get; set; } = string.Empty;
    public int ResilienceNbrOfRetries { get; set; } = 3;
    public int ResilienceWaitInMs { get; set; } = 800;
}
