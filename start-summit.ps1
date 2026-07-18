# ▲ SUMMIT — sobe tudo com um clique: MySQL + API + Client
# Uso:  clique direito > Executar com PowerShell   (ou: powershell -File start-summit.ps1)

$root = $PSScriptRoot

# 1) MySQL
$up = (Test-NetConnection localhost -Port 3306 -WarningAction SilentlyContinue).TcpTestSucceeded
if (-not $up) {
    Write-Host "[1/3] Iniciando MySQL..." -ForegroundColor Yellow
    powershell -NoProfile -File "$root\database\start-mysql.ps1"
    Start-Sleep -Seconds 5
} else {
    Write-Host "[1/3] MySQL ja esta rodando" -ForegroundColor Green
}

# 2) API
$api = Get-Process Summit.Api -ErrorAction SilentlyContinue
if ($null -eq $api) {
    Write-Host "[2/3] Iniciando Summit API..." -ForegroundColor Yellow
    $exe = "$root\Summit.Api\bin\Debug\net8.0\Summit.Api.exe"
    if (-not (Test-Path $exe)) {
        Write-Host "      (buildando a API primeiro...)"
        dotnet build "$root\Summit.Api\Summit.Api.csproj" -v q | Out-Null
    }
    Start-Process -WindowStyle Hidden -WorkingDirectory "$root\Summit.Api" $exe
    Start-Sleep -Seconds 6
} else {
    Write-Host "[2/3] API ja esta rodando" -ForegroundColor Green
}

# 3) Client
Write-Host "[3/3] Abrindo o Summit..." -ForegroundColor Yellow
$client = "$root\bin\Debug\net8.0-windows\Summit.exe"
if (-not (Test-Path $client)) {
    Write-Host "      (buildando o client primeiro...)"
    dotnet build "$root\Summit.csproj" -v q | Out-Null
}
Start-Process $client
Write-Host "Summit no ar! ELEVATE YOUR GAME." -ForegroundColor Red
