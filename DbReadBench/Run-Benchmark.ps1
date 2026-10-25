[CmdletBinding()]
param(
    [int]$Rows = 100000,
    [int]$PayloadBytes = 256,
    [int]$Duration = 60,
    [int]$Warmup = 15,
    [int]$Cooldown = 5,
    [int]$Repetitions = 3,
    [string]$Ccu = "100,1000,5000,10000",
    [ValidateRange(1, 10000)][int]$MaxInflight = 256,
    [string]$Database = "both",
    [switch]$SkipSeed
)
$ErrorActionPreference = "Stop"
Push-Location -LiteralPath $PSScriptRoot
try {
    function Invoke-Compose {
        param([string[]]$ComposeArgs)
        & docker compose @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed (exit $LASTEXITCODE)." }
    }
    $databases = if ($Database -eq "both") { @("postgres", "scylla") } else { @($Database.ToLowerInvariant().Split(',') | Select-Object -Unique) }
    if (@($databases | Where-Object { $_ -notin @("postgres", "scylla", "mongodb", "cassandra", "redis") }).Count -gt 0) {
        throw "Unknown database. Use both or comma-separated postgres,mongodb,cassandra,scylla,redis."
    }
    if ($MaxInflight -gt 256 -and @($databases | Where-Object { $_ -notin @("cassandra", "scylla", "redis") }).Count -gt 0) {
        throw "MaxInflight > 256 is supported only for Redis/Cassandra/Scylla."
    }
    Invoke-Compose -ComposeArgs (@("up", "-d", "--wait", "--wait-timeout", "300") + $databases)
    Invoke-Compose -ComposeArgs @("build", "benchmark")
    $dataset = @("--rows", "$Rows", "--payload-bytes", "$PayloadBytes")
    if (-not $SkipSeed) {
        Invoke-Compose -ComposeArgs (@("run", "--rm", "benchmark", "seed", "--database", $Database) + $dataset)
    }
    Invoke-Compose -ComposeArgs (@("run", "--rm", "benchmark", "run", "--output", "/results",
        "--ccu", $Ccu, "--duration", "$Duration", "--warmup", "$Warmup", "--cooldown", "$Cooldown",
        "--repetitions", "$Repetitions", "--max-inflight", "$MaxInflight", "--database", $Database) + $dataset)
    Write-Host "Reports: $PSScriptRoot/results. Stop services with: docker compose down"
}
finally {
    Pop-Location
}
