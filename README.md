# YG.MonitoringService

**YG.DatabaseMonitor** is a lightweight C#/.NET 8 background monitoring application that continuously checks for data integrity and business rule violations across one or more databases. Each monitoring rule is independently configurable with its own SQL query, connection string, execution interval, resiliency settings, and notification recipients.

When a query returns one or more records, the application logs the results and automatically sends email notifications using **YG.SendEmail**, helping developers identify and resolve issues before they impact users.

### Features

* Monitor one or more SQL Server databases
* Multiple independently scheduled monitoring rules
* Configurable SQL-based validation and business rule checks
* Per-monitor execution intervals
* Per-monitor resiliency settings (retry count and retry delay)
* Email notifications when issues are detected
* Structured logging through `ILogger`
* Built on .NET 8 Worker Service and dependency injection
* Resilient database access using **YG.ADO**

Sample appsettings.json:
            {
              "YGLogging": {
                "folderName": "Log",
                "maxSize": 50000,
                "expiryDays": 3
              },
              "BGWorker": {
                "SqlMonitorOptions": [
                  {
                    "Name": "Checking 0 sequencenbr",
                    "RunIntervalInSeconds": 30,
                    "ConnectionString": "yourconnectionstring",
                    "SqlCommand": "yoursqlstatement",
                    "EmailRecipients": [
                      "yxgrantz@yukonhospitals.ca",
                      "csharpxprt@gmail.com"
                    ],
                    "EmailSubject": "TableA Check for SequenceNbr = 0",
                    "ResilienceNbrOfRetries": 5,
                    "ResilienceWaitInMs": 1000
                  }
                ],
                "HttpMonitorOptions": [
                  {
                    "Name": "Keep App Warm",
                    "RunIntervalInSeconds": 300,
                    "Url": "yourURL",
                    "ExpectedStatusCode": 200,
                    "EmailRecipients": [
                      "yogitester1000@mailinator.com"
                    ],
                    "EmailSubject": "App Health Check Failed"
                  }
                ]
              },
              "YGSendEmail": {
                "Host": "yourhost",
                "Username": "yourUsername",
                "Password": "yourpwd",
                "Port": 465,
                "SecureSocketOption": "Auto",
                "SenderEmail": ""
              },
              "DBConnTimeout": 60,
              "SQLCmdTimeout": 60
            }