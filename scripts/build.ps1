# Builds the finished program: dist\Pagjoro.exe (one self-contained file, no installer) and dist\Pagjoro.exe.sha256.
# Run from the repository folder:  powershell -ExecutionPolicy Bypass -File scripts\build.ps1
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

if (-not $SkipTests) {
    Write-Host 'Running the tests...'
    dotnet test Pagjoro.sln --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The tests failed, so nothing was built.' }
}

$publish = Join-Path $PWD 'publish'
if (Test-Path $publish) { Get-ChildItem $publish -Recurse -Force | Remove-Item -Force -Recurse }
Write-Host 'Publishing...'
dotnet publish src\Pagjoro.App\Pagjoro.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'The publish step failed.' }

$dist = Join-Path $PWD 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist 'Pagjoro.exe'
Copy-Item (Join-Path $publish 'Pagjoro.exe') $exe -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$exe.sha256" -Value "$hash  Pagjoro.exe" -Encoding ascii
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Built $exe ($size MB)"
Write-Host "SHA-256: $hash"
