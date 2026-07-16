using System.Collections.Generic;
using YG.Monitoring.DTOs;

namespace YG.Monitoring.BGWorker;

public class BGWorkerOptions
{
    public int IntervalInSeconds { get; set; } = 60;
    public List<MonitorActionOption> MonitorActionOptions { get; set; } = [];
}