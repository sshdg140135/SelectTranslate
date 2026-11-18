# 构建 → 单文件自包含 → 打包 zip → 生成 SHA256
# 用法： pwsh -File tools/publish.ps1
#        pwsh -File tools/publish.ps1 -ReadyToRun   # 冷启动更快，体积更大

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$ReadyToRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$csproj = Join-Path $root 'src\SelectTranslate.App\SelectTranslate.App.csproj'
$version = ([regex]'<Version>([^<]+)</Version>').Match((Get-Content $csproj -Raw)).Groups[1].Value
if (-not $version) { throw "无法从 $csproj 读取版本号" }

$name = "SelectTranslate-v$version-$Runtime"
$staging = Join-Path $root "artifacts\$name"
$dist = Join-Path $root 'dist'
$zip = Join-Path $dist "$name.zip"

Write-Host "版本：$version  运行时：$Runtime" -ForegroundColor Cyan

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }

$publishArgs = @(
    'publish', (Join-Path $root 'src\SelectTranslate.App'),
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-o', $staging
)

if ($ReadyToRun) { $publishArgs += '-p:PublishReadyToRun=true' }

Write-Host "dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（$LASTEXITCODE）" }

Copy-Item (Join-Path $root 'README.md') (Join-Path $staging 'README.md') -Force
Get-ChildItem $staging -Filter '*.pdb' | Remove-Item -Force

Write-Host "打包 $zip" -ForegroundColor Cyan
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
"$hash  $name.zip" | Set-Content (Join-Path $dist "$name.zip.sha256") -Encoding ASCII

Write-Host ''
Write-Host "完成：$zip" -ForegroundColor Green
Write-Host "大小：$sizeMb MB" -ForegroundColor Green
Write-Host "SHA256：$hash" -ForegroundColor Green
Write-Host "校验文件：$zip.sha256" -ForegroundColor Green