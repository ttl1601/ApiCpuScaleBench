[CmdletBinding()]
param([string]$Ccu="100,1000,5000", [int]$Seconds=20, [int]$Warmup=5, [int]$Repetitions=3)
$ErrorActionPreference="Stop"
Push-Location -LiteralPath $PSScriptRoot
try {
    function Invoke-Compose([string[]]$Arguments) {
        & docker compose @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Docker compose failed: $LASTEXITCODE" }
    }
    Invoke-Compose @("build")
    Invoke-Compose @("--profile", "tools", "build", "loadgen")
    Invoke-Compose @("up", "-d", "dotnet", "rust")
    Invoke-Compose @("run", "--rm", "loadgen", "smoke")
    Invoke-Compose @("run", "--rm", "loadgen", "run", "--ccu", $Ccu, "--seconds", "$Seconds", "--warmup", "$Warmup", "--repetitions", "$Repetitions")
} finally { Pop-Location }
