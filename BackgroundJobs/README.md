# Azure Functions - Background Jobs

This project contains timer-triggered Azure Functions for scheduled background tasks.

## Functions

### 1. **RankingRecalculationFunction**
- **Schedule**: Every 6 hours (00:00, 06:00, 12:00, 18:00 UTC)
- **Purpose**: Recalculates all novel rankings (TopRated, Trending, New, AllTimeGreats, TrendingNow)
- **CRON**: `0 0 */6 * * *`

### 2. **DailyChapterUnlockFunction**
- **Schedule**: Daily at midnight (00:00 UTC)
- **Purpose**: Unlocks 1 privilege chapter per day for all enabled novels
- **CRON**: `0 0 0 * * *`

## Local Development

### Prerequisites
1. [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
2. [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (Azure Storage Emulator)
3. .NET 10 SDK

### Setup

1. **Update `local.settings.json`** with your connection strings:
```json
{
  "Values": {
    "ConnectionStrings:Default": "YOUR_SQL_SERVER_CONNECTION_STRING",
    "OpenSearch:Url": "YOUR_OPENSEARCH_URL",
    ...
  }
}
```

2. **Start Azurite** (for local storage emulation):
```bash
azurite
```

3. **Run Functions Locally**:
```bash
cd BackgroundJobs
func start
```

### Test Manually (Trigger Function)
```bash
# Trigger ranking recalculation
func azure functionapp publish <YOUR_FUNCTION_APP_NAME> --functions RankingRecalculation

# Trigger daily unlock
func azure functionapp publish <YOUR_FUNCTION_APP_NAME> --functions DailyChapterUnlock
```

## Deployment to Azure

### 1. **Create Azure Function App** (Portal or CLI)

**Using Azure CLI**:
```bash
# Login
az login

# Create Resource Group
az group create --name sareed-functions-rg --location eastus

# Create Storage Account
az storage account create \
  --name sareedstorageaccount \
  --resource-group sareed-functions-rg \
  --location eastus \
  --sku Standard_LRS

# Create Function App
az functionapp create \
  --resource-group sareed-functions-rg \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4 \
  --name sareed-background-jobs \
  --storage-account sareedstorageaccount
```

### 2. **Configure App Settings**

```bash
# Set connection string
az functionapp config appsettings set \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --settings "ConnectionStrings__Default=YOUR_SQL_SERVER_CONNECTION_STRING"

# Set OpenSearch settings
az functionapp config appsettings set \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --settings \
    "OpenSearch__Url=YOUR_OPENSEARCH_URL" \
    "OpenSearch__Username=YOUR_USERNAME" \
    "OpenSearch__Password=YOUR_PASSWORD" \
    "OpenSearch__NovelIndexName=sareed-novels"
```

### 3. **Deploy Functions**

**Using VS Code**:
1. Install "Azure Functions" extension
2. Right-click on `BackgroundJobs` folder → "Deploy to Function App"
3. Select your subscription and function app

**Using Azure Functions Core Tools**:
```bash
cd BackgroundJobs
func azure functionapp publish sareed-background-jobs
```

**Using GitHub Actions** (CI/CD):
See `deploy-functions.yml` workflow file (create if needed)

### 4. **Monitor Functions**

**View Logs in Portal**:
1. Go to Azure Portal → Your Function App
2. Navigate to "Functions" → Select function → "Monitor"

**View Logs in Real-Time**:
```bash
func azure functionapp logstream sareed-background-jobs
```

**Application Insights**:
- Enable Application Insights in Azure Portal
- View detailed telemetry, failures, and performance

## CRON Expression Guide

```
┌───────────── second (0-59)
│ ┌───────────── minute (0-59)
│ │ ┌───────────── hour (0-23)
│ │ │ ┌───────────── day of month (1-31)
│ │ │ │ ┌───────────── month (1-12)
│ │ │ │ │ ┌───────────── day of week (0-6)
│ │ │ │ │ │
* * * * * *
```

### Examples:
- `0 0 */6 * * *` - Every 6 hours
- `0 0 0 * * *` - Daily at midnight
- `0 30 9 * * *` - Every day at 9:30 AM
- `0 0 */1 * * *` - Every hour
- `0 */15 * * * *` - Every 15 minutes

## Timezone Configuration

By default, CRON expressions use UTC. To use a different timezone:

```bash
az functionapp config appsettings set \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --settings "WEBSITE_TIME_ZONE=Egypt Standard Time"
```

Common timezones:
- `UTC` (default)
- `Egypt Standard Time` (Cairo)
- `US/Eastern`
- `Europe/London`

## Cost Optimization

**Consumption Plan** (Pay-per-execution):
- **Free Grant**: 1 million executions/month + 400,000 GB-s
- **Estimated Cost** for your 2 functions:
  - Ranking: 4 times/day × 30 days = 120 executions/month
  - Daily Unlock: 1 time/day × 30 days = 30 executions/month
  - **Total**: ~150 executions/month (**FREE TIER**)

## Troubleshooting

### Function Not Triggering
1. Check CRON expression: https://crontab.guru/
2. Verify timezone settings
3. Check function app logs for errors

### Connection Issues
1. Verify connection strings in App Settings
2. Ensure SQL Server firewall allows Azure services
3. Check OpenSearch endpoint accessibility

### Performance Issues
1. Monitor execution time in Application Insights
2. Consider increasing timeout (default: 5 minutes)
3. Optimize database queries if needed

## Security Best Practices

1. **Use Azure Key Vault** for connection strings:
```bash
az keyvault secret set \
  --vault-name your-keyvault \
  --name "ConnectionString" \
  --value "YOUR_CONNECTION_STRING"
```

2. **Enable Managed Identity**
3. **Restrict network access** (if using Premium plan)
4. **Use Application Insights** for monitoring

## Support

For issues or questions:
- Azure Functions Docs: https://learn.microsoft.com/en-us/azure/azure-functions/
- CRON Helper: https://crontab.guru/
