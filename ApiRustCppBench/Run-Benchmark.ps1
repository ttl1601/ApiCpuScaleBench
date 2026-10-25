[CmdletBinding()]
param([string]$Ccu="100,1000,5000", [int]$Seconds=20, [int]$Warmup=5, [int]$Repetitions=3)
$ErrorActionPreference="Stop"
Push-Location -LiteralPath $PSScriptRoot
$job=$null
try {
    function Compose([string[]]$Arguments) {
        & docker compose @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Docker compose failed: $LASTEXITCODE" }
    }
    New-Item -ItemType Directory -Force validation | Out-Null
    Compose @("build", "cpp", "rust")
    Compose @("--profile", "tools", "build", "loadgen")
    Compose @("up", "-d", "cpp", "rust")
    Compose @("run", "--rm", "loadgen", "smoke")
    $ids=@(& docker compose ps -q cpp rust)
    & docker inspect @ids | Set-Content validation/pre-inspect.json
    & docker compose exec -T cpp cat /app/packages.txt /app/compiler.txt | Set-Content validation/cpp-build-packages.txt
    & docker info | Set-Content validation/docker-info.txt
    $job=Start-Job -ArgumentList $PSScriptRoot -ScriptBlock {
        param($root)
        Set-Location -LiteralPath $root
        while ($true) {
            $timestamp=[DateTimeOffset]::UtcNow.ToString("O")
            & docker stats --no-stream --format '{{json .}}' api-rust-cpp-bench-cpp-1 api-rust-cpp-bench-rust-1 2>$null |
                ForEach-Object { '{"timestamp":"'+$timestamp+'","stats":'+$_+'}' } | Add-Content validation/resources.jsonl
            Start-Sleep -Seconds 5
        }
    }
    Compose @("run", "--rm", "loadgen", "run", "--ccu", $Ccu, "--seconds", "$Seconds", "--warmup", "$Warmup", "--repetitions", "$Repetitions")
} finally {
    if ($job) { Stop-Job $job; Remove-Job $job }
    & docker compose stop cpp rust
    $ids=@(& docker compose ps -a -q cpp rust)
    if ($ids.Count) { & docker inspect @ids | Set-Content validation/post-inspect.json }
    Pop-Location
}
