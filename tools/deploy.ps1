# 本机调试部署：构建 → 复制到 %LOCALAPPDATA%\SelectTranslate\app → 刷新桌面快捷方式
# 用法： pwsh -File tools/deploy.ps1           # 先构建再部署
#        pwsh -File tools/deploy.ps1 -NoBuild   # 直接用现有构建产物

param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\SelectTranslate.App\bin\Debug\net10.0-windows'
$dest = Join-Path $env:LOCALAPPDATA 'SelectTranslate\app'

if (-not $NoBuild) {
    Write-Host '构建 Debug …' -ForegroundColor Cyan
    & dotnet build (Join-Path $root 'SelectTranslate.slnx') -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败（$LASTEXITCODE）" }
}

if (-not (Test-Path (Join-Path $source 'SelectTranslate.exe'))) {
    throw "找不到构建产物：$source"
}

Get-Process SelectTranslate -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item "$source\*" $dest -Recurse -Force
Remove-Item (Join-Path $dest 'data') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $dest 'startup-error.log') -Force -ErrorAction SilentlyContinue

$exe = Join-Path $dest 'SelectTranslate.exe'
$desktop = [Environment]::GetFolderPath('Desktop')
$link = Join-Path $desktop 'SelectTranslate 划词译.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = '划词译：选中外文按中键翻译'
$shortcut.Save()

$hashMatch = (Get-FileHash (Join-Path $source 'SelectTranslate.dll')).Hash -eq (Get-FileHash (Join-Path $dest 'SelectTranslate.dll')).Hash

Write-Host ''
Write-Host "部署完成：$dest" -ForegroundColor Green
Write-Host "快捷方式：$link" -ForegroundColor Green
Write-Host "版本一致：$hashMatch" -ForegroundColor Green