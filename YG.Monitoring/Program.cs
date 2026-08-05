

using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using YG.ADO;
using YG.LogProvider;
using YG.Monitoring.BGWorker;
using YG.SendMail;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

builder.Logging.ClearProviders();
builder.Logging.AddYGLogProvider(option =>
{
    option.BaseDirectory = AppContext.BaseDirectory;
    option.FolderName = config["YGLogging:folderName"] ?? "Log";
    option.MinimumLevel = LogLevel.Information;

    if (long.TryParse(config["YGLogging:maxSize"], out long maxFileSize))
        option.MaxSize = maxFileSize;

    if (int.TryParse(config["YGLogging:expiryDays"], out int expiryDays))
        option.ExpiryDays = expiryDays;
});

builder.Services.AddYGADO();

int.TryParse(config["YGSendEmail:Port"], out int port);

Dictionary<string, SecureSocketOptions> ssoption = new Dictionary<string, SecureSocketOptions>(StringComparer.OrdinalIgnoreCase)
{
    {"none", SecureSocketOptions.None },
    {"auto", SecureSocketOptions.Auto },    
    {"startTls", SecureSocketOptions.StartTls },
    {"SslOnConnect", SecureSocketOptions.SslOnConnect },
    {"StartTlsWhenAvailable", SecureSocketOptions.StartTlsWhenAvailable }
};

ssoption.TryGetValue(config["YGSendEmail:SecureSocketOption"], out SecureSocketOptions sso);

builder.Services.AddYGSendEmail(new SendMailOptions()
{
     Host = config["YGSendEmail:Host"],
     Username = config["YGSendEmail:Username"],
     Password = config["YGSendEmail:Password"],
     Port = port,
     SecureSocketOption = sso
});

builder.Services.AddHttpClient("MonitoringHttpClient", client =>
    {
        // Let the resilience pipeline control request timeouts.
        client.Timeout = Timeout.InfiniteTimeSpan;

        client.DefaultRequestHeaders.UserAgent.ParseAdd("YG.Monitoring/1.0");

        client.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
    })
    .AddStandardResilienceHandler();

builder.Services.Configure<BGWorkerOptions>(config.GetSection("BGWorker"));
builder.Services.AddHostedService<BGWorker>();


var host = builder.Build();

await host.RunAsync();