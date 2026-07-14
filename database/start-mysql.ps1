# Inicia o MySQL local do Summit (nao precisa de admin).
# O data dir foi inicializado com usuario root SEM senha (apenas dev local!).

$mysqld  = "C:\Program Files\MySQL\MySQL Server 8.4\bin\mysqld.exe"
$dataDir = "$env:LOCALAPPDATA\SummitMySQL\data"

$up = (Test-NetConnection localhost -Port 3306 -WarningAction SilentlyContinue).TcpTestSucceeded
if ($up) { Write-Host "MySQL ja esta rodando em localhost:3306"; exit }

if (-not (Test-Path $mysqld))  { Write-Error "mysqld.exe nao encontrado em $mysqld"; exit 1 }
if (-not (Test-Path $dataDir)) {
    Write-Host "Inicializando data dir em $dataDir..."
    & $mysqld --initialize-insecure --datadir="$dataDir"
}

Start-Process -WindowStyle Hidden $mysqld -ArgumentList "--datadir=`"$dataDir`"", "--port=3306", "--console"
Write-Host "MySQL iniciando em localhost:3306 (root, sem senha)"
