using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YG.ADO;
using YG.Monitoring.DTOs;
using YG.SendMail;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace YG.Monitoring.BGWorker;

public class BGWorker : BackgroundService
{
    private readonly BGWorkerOptions _options;
    private readonly IConfiguration _config;
    private readonly ILogger<BGWorker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDBSPToolFactory _dbFactory;
    private readonly ISendMail _email;
    private int _connTimeout = 30;
    private int _sqlCmdTimeout = 30;

    public BGWorker(IOptions<BGWorkerOptions> options, IConfiguration config, ILogger<BGWorker> logger, IHttpClientFactory httpClientFactory, IDBSPToolFactory dbFactory, ISendMail email)
    {
        this._options = options.Value;
        this._config = config;
        this._logger = logger;
        this._httpClientFactory = httpClientFactory;
        this._dbFactory = dbFactory;
        this._email = email;
        if (int.TryParse(_config["DBConnTimeout"], out int connTimeout))
            _connTimeout = connTimeout;

        if (int.TryParse(_config["SQLCmdTimeout"], out int sqlcmdTimeout))
            _sqlCmdTimeout = sqlcmdTimeout;

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IEnumerable<MonitorOption> monitorOptions = _options.SqlMonitorOptions
                                    .Cast<MonitorOption>()
                                    .Concat(_options.HttpMonitorOptions);

        Task[] monitorTasks = monitorOptions
                        .Select(option =>
                            RunMonitorLoopAsync(option, stoppingToken))
                        .ToArray();

        await Task.WhenAll(monitorTasks);
    }

    private async Task RunMonitorLoopAsync(
    MonitorOption action,
    CancellationToken stoppingToken)
    {
        if (action.RunIntervalInSeconds <= 0)
        {
            _logger.LogError(
                "Monitor {MonitorName} has an invalid interval of {Interval} seconds.",
                action.Name,
                action.RunIntervalInSeconds);

            return;
        }

        _logger.LogInformation(
            "Starting monitor {MonitorName}. Interval: {Interval} seconds.",
            action.Name,
            action.RunIntervalInSeconds);

        try
        {
            // Run immediately when the application starts.
            await RunMonitorAsync(action, stoppingToken);

            using var timer = new PeriodicTimer(
                TimeSpan.FromSeconds(action.RunIntervalInSeconds));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunMonitorAsync(action, stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Monitor {MonitorName} is stopping.",
                action.Name);
        }
    }

    private async Task RunMonitorAsync(MonitorOption option, CancellationToken stoppingToken)
    {
        switch (option)
        {
            case SqlMonitorOption sqlOption:
                await RunSqlMonitorAsync(sqlOption, stoppingToken);
                break;

            case HttpMonitorOption httpOption:
                await RunHttpMonitorAsync(httpOption, stoppingToken);
                break;

            default:
                _logger.LogError("Monitor {MonitorName} has unsupported option type {OptionType}.", option.Name,option.GetType().Name);
                break;
        }
    }

    private async Task RunHttpMonitorAsync(HttpMonitorOption action, CancellationToken cancellationToken)
    {
        using HttpClient client = _httpClientFactory.CreateClient("MonitoringHttpClient");
        string message = "";
        try
        {
            using HttpResponseMessage response = await client.GetAsync(action.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if ((int)response.StatusCode != action.ExpectedStatusCode)
            {

                message = $"Monitor '{action.Name}' expected HTTP {action.ExpectedStatusCode}, but received {(int)response.StatusCode} {response.StatusCode}.";
                _logger.LogError(message);

                string result = await SendEmailAsync(action, message);

                _logger.LogInformation("Http monitor {MonitorName} email result: {EmailResult}", action.Name, result);

            }
            else
                _logger.LogInformation("HTTP monitor {MonitorName} succeeded with status {StatusCode}.", action.Name, response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Let BackgroundService stop gracefully.
            throw;
        }
        catch (Exception ex)
        {
            string msg = $"Http monitor '{action.Name}' failed ({action.Url}). Please check log.\r\n{ex.StackTrace}";
            string result = await SendEmailAsync(action, msg);
            _logger.LogError(ex, msg);
        }
    }

    private async Task RunSqlMonitorAsync(SqlMonitorOption action, CancellationToken cancellationToken)
    {
        try
        {
            IDBSPTool db = _dbFactory.Create(
                action.ConnectionString,
                connectionTimeout: _connTimeout,
                commandTimeout: _sqlCmdTimeout,
                resiliencyRetries: action.ResilienceNbrOfRetries,
                resiliencyWaitMs: action.ResilienceWaitInMs);

            using DataSet dataSet = await db.GetDataSetAsync(
                action.SqlCommand,
                isQueryText: true,
                cancellationToken);

            if (dataSet.Tables.Count == 0 || dataSet.Tables[0].Rows.Count == 0)
            {
                _logger.LogDebug(
                    "Database monitor {MonitorName} found no issues.",
                    action.Name);

                return;
            }

            string message = FormatTable(dataSet.Tables[0], action);

            _logger.LogWarning(
                "Database monitor {MonitorName} found {RowCount} issue(s).{NewLine}{Results}",
                action.Name,
                dataSet.Tables[0].Rows.Count,
                Environment.NewLine,
                message);

            string result = await SendEmailAsync(action, message);

            _logger.LogInformation("Database monitor {MonitorName} email result: {EmailResult}", action.Name, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Let BackgroundService stop gracefully.
            throw;
        }
        catch (Exception ex)
        {
            string msg = $"Database monitor '{action.Name}' failed. Please check log.\r\n{ex.StackTrace}";
            string result = await SendEmailAsync(action, msg);
            _logger.LogError(ex, msg);
        }
    }

    private async Task<string> SendEmailAsync(MonitorOption action, string message)
    {
        var email = new EmailDTO
        {
            To = action.EmailRecipients,
            Subject = action.EmailSubject ?? "Alert from YG.Monitoring",
            Body = message,
            IsHtml = false
        };

        if (!string.IsNullOrEmpty(_config["YGSendEmail:SenderEmail"]))
            email.SenderEmail = _config["YGSendEmail:SenderEmail"];

        string result = await _email.SendAsync(email);
        return result;
    }

    private static string FormatTable(DataTable table, MonitorOption action)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Issues found from YG Monitoring Service:\r\n");

        sb.AppendLine($"Monitor: {action.Name}");
        sb.AppendLine($"Detected: {DateTimeOffset.Now}");
        sb.AppendLine($"Rows found: {table.Rows.Count}");

        sb.AppendLine(
            string.Join(
                " | ",
                table.Columns
                    .Cast<DataColumn>()
                    .Select(column => column.ColumnName)));

        foreach (DataRow row in table.Rows)
        {
            IEnumerable<string> values = table.Columns
                .Cast<DataColumn>()
                .Select(column =>
                    row[column] == DBNull.Value
                        ? "(null)"
                        : row[column]?.ToString() ?? string.Empty);

            sb.AppendLine(string.Join(" | ", values));
        }

        return sb.ToString();
    }

}
