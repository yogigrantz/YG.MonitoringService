using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YG.ADO;
using YG.Monitoring.DTOs;
using YG.SendMail;

namespace YG.Monitoring.BGWorker;

public class BGWorker : BackgroundService
{
    private readonly BGWorkerOptions _options;
    private readonly IConfiguration _config;
    private readonly ILogger<BGWorker> _logger;
    private readonly IDBSPToolFactory _dbFactory;
    private readonly ISendMail _email;
    private int _connTimeout = 30;
    private int _sqlCmdTimeout = 30;

    public BGWorker(IOptions<BGWorkerOptions> options, IConfiguration config, ILogger<BGWorker> logger, IDBSPToolFactory dbFactory, ISendMail email)
    {
        this._options = options.Value;
        this._config = config;
        this._logger = logger;
        this._dbFactory = dbFactory;
        this._email = email;
        if (int.TryParse(_config["DBConnTimeout"], out int connTimeout))
            _connTimeout = connTimeout;

        if (int.TryParse(_config["SQLCmdTimeout"], out int sqlcmdTimeout))
            _sqlCmdTimeout = sqlcmdTimeout;

    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var monitorTasks = _options.MonitorActionOptions
       .Select(action => RunMonitorLoopAsync(action, stoppingToken))
       .ToArray();

        await Task.WhenAll(monitorTasks);
    }

    private async Task RunMonitorLoopAsync(
    MonitorActionOption action,
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

    private async Task RunMonitorAsync(
    MonitorActionOption action,
    CancellationToken cancellationToken)
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

            var email = new EmailDTO
            {
                To = action.EmailRecipients,
                Subject = action.EmailSubject ?? "Alert from YG.Monitoring",
                Body = message,
                IsHtml = false
            };

            string result = await _email.SendAsync(email);

            _logger.LogInformation(
                "Database monitor {MonitorName} email result: {EmailResult}",
                action.Name,
                result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Let BackgroundService stop gracefully.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database monitor '{MonitorName}' failed.", action.Name);
        }
    }

    private static string FormatTable(DataTable table, MonitorActionOption action)
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
