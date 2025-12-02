# Azure Functions Deployment Guide

## 🚀 Production Deployment Steps

### Prerequisites
- Azure account with active subscription
- Azure CLI installed
- .NET 8 SDK installed
- Azure Functions Core Tools v4

---

## 📝 **Step 1: Create Azure Resources**

### 1.1 Login to Azure
```bash
az login
```

### 1.2 Set Your Subscription (if you have multiple)
```bash
# List subscriptions
az account list --output table

# Set active subscription
az account set --subscription "YOUR_SUBSCRIPTION_ID"
```

### 1.3 Create Resource Group
```bash
az group create \
  --name sareed-functions-rg \
  --location eastus
```

**Options for location:**
- `eastus` (US East)
- `westeurope` (West Europe)
- `southeastasia` (Southeast Asia)
- Choose closest to your database!

### 1.4 Create Storage Account
```bash
az storage account create \
  --name sareedstorageaccount \
  --resource-group sareed-functions-rg \
  --location eastus \
  --sku Standard_LRS
```

**Note:** Storage account name must be:
- 3-24 characters
- Lowercase letters and numbers only
- Globally unique

### 1.5 Create Function App
```bash
az functionapp create \
  --resource-group sareed-functions-rg \
  --consumption-plan-location eastus \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --name sareed-background-jobs \
  --storage-account sareedstorageaccount \
  --os-type Windows
```

**Important:**
- `--name` must be globally unique (try: `sareed-bg-jobs-{yourname}`)
- Function URL will be: `https://sareed-background-jobs.azurewebsites.net`

---

## 🔧 **Step 2: Configure Application Settings**

### 2.1 Set Connection String
```bash
az functionapp config appsettings set \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --settings "ConnectionStrings__Default=YOUR_PRODUCTION_SQL_CONNECTION_STRING"
```

**Replace with your Azure SQL Database connection string:**
```
Server=tcp:your-server.database.windows.net,1433;Database=SardDatabase;User ID=your-admin;Password=your-password;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;
```

### 2.2 Verify Settings
```bash
az functionapp config appsettings list \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --output table
```

---

## 📦 **Step 3: Build and Deploy**

### Option A: Deploy via Azure Functions Core Tools (Recommended)

#### 3.1 Navigate to BackgroundJobs folder
```bash
cd C:\Users\sadap\source\repos\Sareed-novels-backend\BackgroundJobs
```

#### 3.2 Build the project
```bash
dotnet build --configuration Release
```

#### 3.3 Publish to Azure
```bash
func azure functionapp publish sareed-background-jobs
```

**Expected Output:**
```
Getting site publishing info...
Uploading package...
Upload completed successfully.
Deployment completed successfully.

Functions in sareed-background-jobs:
    DailyChapterUnlock - [timerTrigger]
    RankingRecalculation - [timerTrigger]
    TestDailyUnlock - [httpTrigger]
    TestRankingRecalculation - [httpTrigger]
```

### Option B: Deploy via Visual Studio

1. **Right-click** on `BackgroundJobs` project
2. Select **Publish**
3. Choose **Azure**
4. Select **Azure Function App (Windows)**
5. Sign in to your Azure account
6. Select `sareed-background-jobs`
7. Click **Publish**

---

## ✅ **Step 4: Verify Deployment**

### 4.1 Check Function App Status
```bash
az functionapp show \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --query "state" \
  --output tsv
```

**Expected:** `Running`

### 4.2 List Functions
```bash
az functionapp function list \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --output table
```

### 4.3 Test HTTP Triggers (Optional)
```bash
# Get function URL
az functionapp function show \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --function-name TestRankingRecalculation \
  --query "invokeUrlTemplate" \
  --output tsv

# Test via PowerShell
Invoke-WebRequest -Uri "https://sareed-background-jobs.azurewebsites.net/api/TestRankingRecalculation" -Method POST
```

---

## 📊 **Step 5: Monitor Functions**

### 5.1 Enable Application Insights (Recommended)
```bash
# Create Application Insights
az monitor app-insights component create \
  --app sareed-functions-insights \
  --location eastus \
  --resource-group sareed-functions-rg

# Get Instrumentation Key
az monitor app-insights component show \
  --app sareed-functions-insights \
  --resource-group sareed-functions-rg \
  --query "instrumentationKey" \
  --output tsv

# Link to Function App
az functionapp config appsettings set \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --settings "APPINSIGHTS_INSTRUMENTATIONKEY=YOUR_INSTRUMENTATION_KEY"
```

### 5.2 View Logs in Real-Time
```bash
az webapp log tail \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg
```

### 5.3 View Logs in Azure Portal
1. Go to: https://portal.azure.com
2. Navigate to **Function App** → `sareed-background-jobs`
3. Click **Functions** → Select function (e.g., `RankingRecalculation`)
4. Click **Monitor** → View executions

---

## 🔒 **Step 6: Remove Test Functions (Production)**

After deployment, **disable test HTTP triggers** for security:

```bash
az functionapp config appsettings set \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --settings "AzureWebJobsDisabledFunctions=TestRankingRecalculation,TestDailyUnlock"
```

Or **delete the test file** before deployment:
```bash
# Remove test triggers file
rm BackgroundJobs/Functions/TestTriggersFunction.cs

# Rebuild and redeploy
dotnet build --configuration Release
func azure functionapp publish sareed-background-jobs
```

---

## 🎯 **Step 7: Verify Cron Jobs Are Running**

### 7.1 Check Next Execution Time
```bash
az functionapp function show \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg \
  --function-name RankingRecalculation \
  --query "config.bindings" \
  --output json
```

### 7.2 Monitor in Azure Portal
1. Go to **Function App** → `sareed-background-jobs`
2. Click **Functions** → `RankingRecalculation`
3. Click **Monitor** tab
4. Check execution history

**Expected Schedule:**
- **RankingRecalculation**: Every 6 hours (00:00, 06:00, 12:00, 18:00 UTC)
- **DailyChapterUnlock**: Daily at 00:00 UTC (midnight)

---

## 💰 **Cost Estimation**

**Consumption Plan Pricing:**
- **Free Tier**: 1 million executions/month + 400,000 GB-s
- **Your Usage**: ~150 executions/month (2 functions × ~2.5 executions/day × 30 days)
- **Expected Cost**: **$0/month** (within free tier)

---

## 🔄 **Step 8: Update Deployment (Future Changes)**

When you make changes to functions:

```bash
cd BackgroundJobs
dotnet build --configuration Release
func azure functionapp publish sareed-background-jobs
```

---

## 🛠️ **Troubleshooting**

### Function Not Triggering
```bash
# Check logs
az webapp log tail --name sareed-background-jobs --resource-group sareed-functions-rg

# Verify connection string
az functionapp config connection-string list \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg
```

### Database Connection Issues
1. **Check Azure SQL Firewall**:
   - Go to Azure Portal → SQL Server
   - Click **Firewalls and virtual networks**
   - Add rule: **Allow Azure services** = ON

2. **Test Connection String**:
```bash
# Via Azure Cloud Shell
sqlcmd -S your-server.database.windows.net -U your-admin -P your-password -d SardDatabase -Q "SELECT 1"
```

### Function App Not Starting
```bash
# Restart function app
az functionapp restart \
  --name sareed-background-jobs \
  --resource-group sareed-functions-rg
```

---

## 📚 **Useful Commands**

```bash
# View all functions
az functionapp function list --name sareed-background-jobs --resource-group sareed-functions-rg --output table

# Get function app URL
az functionapp show --name sareed-background-jobs --resource-group sareed-functions-rg --query "defaultHostName" --output tsv

# Delete function app (cleanup)
az functionapp delete --name sareed-background-jobs --resource-group sareed-functions-rg

# Delete resource group (cleanup everything)
az group delete --name sareed-functions-rg --yes
```

---

## ✅ **Deployment Checklist**

- [ ] Azure CLI installed and logged in
- [ ] Resource group created
- [ ] Storage account created
- [ ] Function app created
- [ ] Connection string configured
- [ ] Functions built successfully
- [ ] Functions deployed to Azure
- [ ] Test HTTP triggers work (optional)
- [ ] Test functions removed/disabled for production
- [ ] Application Insights enabled (optional)
- [ ] Logs verified in Azure Portal
- [ ] Cron schedules confirmed
- [ ] Database firewall allows Azure services

---

## 🎉 **Success!**

Your Azure Functions are now running in production! 🚀

- Ranking recalculation runs every 6 hours
- Daily chapter unlock runs at midnight UTC
- Monitor via Azure Portal or Application Insights
