<#
.SYNOPSIS
    Rebuilds the local demo environment from scratch and prints the resulting queue shape.

.DESCRIPTION
    Development helper. Stops any running API, drops the local NexaOps database, rebuilds the
    solution, starts the API (which migrates and seeds), then signs in and prints the service
    desk dashboard so the seeded data can be checked at a glance.

    Every figure printed is fetched from the running API, not computed by this script.

.PARAMETER SkipBuild
    Reuse the existing build output instead of rebuilding.

.PARAMETER KeepDatabase
    Leave the existing database in place; useful for restarting without losing local changes.
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

$logDir = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'api-local.log'

Write-Host '==> Stopping any running API' -ForegroundColor Cyan
Get-Process -Name NexaOps.Api -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

if (-not $KeepDatabase) {
    Write-Host '==> Dropping local database' -ForegroundColor Cyan
    $drop = "IF DB_ID('NexaOps') IS NOT NULL BEGIN ALTER DATABASE NexaOps SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE NexaOps; END"
    & sqlcmd -S "(localdb)\MSSQLLocalDB" -Q $drop | Out-Null
}

if (-not $SkipBuild) {
    Write-Host '==> Building' -ForegroundColor Cyan
    $env:DOTNET_NOLOGO = '1'
    $build = dotnet build NexaOps.slnx -v q --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $build | Select-String -Pattern 'error' | Select-Object -First 10
        throw 'Build failed.'
    }
    Write-Host '    build succeeded'
}

Write-Host '==> Starting API (migrates and seeds)' -ForegroundColor Cyan
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', 'src/NexaOps.Api/NexaOps.Api.csproj', '--no-launch-profile', '--no-build', '--urls', 'http://localhost:5266') `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" -WindowStyle Hidden | Out-Null

$ready = $false
foreach ($attempt in 1..120) {
    Start-Sleep -Seconds 2
    try {
        $probe = Invoke-WebRequest -Uri 'http://localhost:5266/health/live' -TimeoutSec 3 -UseBasicParsing
        if ($probe.StatusCode -eq 200) { $ready = $true; Write-Host "    ready after ~$($attempt * 2)s"; break }
    } catch { }
}

if (-not $ready) {
    Get-Content $log -Tail 25
    Get-Content "$log.err" -Tail 25 -ErrorAction SilentlyContinue
    throw 'API did not become ready.'
}

Write-Host '==> Signing in as the service desk manager' -ForegroundColor Cyan
$signIn = Invoke-RestMethod -Uri 'http://localhost:5266/api/v1/auth/sign-in' -Method Post `
    -ContentType 'application/json' `
    -Body (@{ email = 'arun.mehta@acmetech.example.in'; password = 'NexaOps#Demo2026' } | ConvertTo-Json)

$headers = @{ Authorization = "Bearer $($signIn.accessToken)" }
$summary = Invoke-RestMethod -Uri 'http://localhost:5266/api/v1/incidents/summary' -Headers $headers
$all = Invoke-RestMethod -Uri 'http://localhost:5266/api/v1/incidents?pageSize=1' -Headers $headers

$breachPct = if ($summary.openIncidents -gt 0) {
    [math]::Round($summary.breachedOpen * 100 / $summary.openIncidents)
} else { 0 }

Write-Host ''
Write-Host 'SERVICE DESK - Acme Technologies India' -ForegroundColor Green
Write-Host ("  total incidents     {0}" -f $all.totalCount)
Write-Host ("  open                {0}" -f $summary.openIncidents)
Write-Host ("  breached (open)     {0}  ({1}% of open)" -f $summary.breachedOpen, $breachPct)
Write-Host ("  P1 / P2 open        {0} / {1}" -f $summary.criticalOpen, $summary.highOpen)
Write-Host ("  due within 2h       {0}" -f $summary.dueWithinTwoHours)
Write-Host ("  unassigned (mine)   {0}" -f $summary.unassignedInMyGroups)
Write-Host ("  created today       {0}    resolved today {1}" -f $summary.createdToday, $summary.resolvedToday)
Write-Host ("  by status           {0}" -f (($summary.openByStatus | ForEach-Object { "$($_.status)=$($_.count)" }) -join ' '))
Write-Host ("  by priority         {0}" -f (($summary.openByPriority | ForEach-Object { "$($_.priority)=$($_.count)" }) -join ' '))
Write-Host ("  workload            {0}" -f (($summary.teamWorkload | ForEach-Object { "$($_.displayName.Split(' ')[0]) $($_.openCount)/$($_.breachedCount)b" }) -join '  '))
Write-Host ''
Write-Host "API log: $log"
