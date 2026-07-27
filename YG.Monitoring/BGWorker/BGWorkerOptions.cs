using System.Collections.Generic;
using YG.Monitoring.DTOs;

namespace YG.Monitoring.BGWorker;

public sealed class BGWorkerOptions
{
    public SqlMonitorOption[] SqlMonitorOptions { get; set; } = [];

    public HttpMonitorOption[] HttpMonitorOptions { get; set; } = [];
}