using System.Net.Http;

namespace YG.Monitoring.DTOs;

public class HttpMonitorOption : MonitorOption
{
    public string Url { get; set; } = string.Empty;

    public HttpMethod Method { get; set; } = HttpMethod.Get;

    public int ExpectedStatusCode { get; set; }
}
