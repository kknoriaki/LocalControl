[CmdletBinding()]
param([switch]$Installer)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseVersion=([xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if($releaseVersion -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version'}
Push-Location $projectRoot
try {
    if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'Build on Windows 11 x64 with PowerShell and .NET 10 SDK.' }
    if ((Get-Process -Name LocalControl -ErrorAction SilentlyContinue)) { throw 'Exit LocalControl from tray before building.' }
    Push-Location 'src/LocalControl.Web'
    try {
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Web build failed' }
    } finally { Pop-Location }
    dotnet restore LocalControl.slnx
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed' }
    dotnet build LocalControl.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
    dotnet run --project tests/LocalControl.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Security/API checks failed' }
    $publishDirectory = Join-Path $projectRoot 'artifacts/win-x64'
    if (Test-Path $publishDirectory) { Remove-Item $publishDirectory -Recurse -Force }
    dotnet publish src/LocalControl.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
    $updaterDirectory = Join-Path $projectRoot 'artifacts/updater'
    if (Test-Path $updaterDirectory) { Remove-Item $updaterDirectory -Recurse -Force }
    dotnet publish src/LocalControl.Updater -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $updaterDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed' }
    Copy-Item "$updaterDirectory/*" -Destination $publishDirectory -Recurse -Force
    if (Test-Path 'build-config/publisher-public.pem') { Copy-Item 'build-config/publisher-public.pem' -Destination $publishDirectory }
    if (-not (Test-Path "$publishDirectory/wwwroot/index.html")) { throw 'Published web bundle missing' }
    Copy-Item README.md, THIRD_PARTY_NOTICES.md -Destination $publishDirectory
    Copy-Item LICENSES -Destination $publishDirectory -Recurse
    Copy-Item docs/RELEASE-CHECKLIST.ru.md -Destination $publishDirectory
    Copy-Item scripts/firewall.ps1 -Destination $publishDirectory
    Copy-Item scripts/Install-WebView2.ps1, scripts/Stop-App.ps1, scripts/Prepare-Install.ps1 -Destination $publishDirectory
    $ownedFiles = @(Get-ChildItem $publishDirectory -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/') })
    $ownedFiles += 'owned-files.json'
    ConvertTo-Json -InputObject $ownedFiles | Set-Content "$publishDirectory/owned-files.json" -Encoding utf8
    $uninstallLines = [System.Collections.Generic.List[string]]::new()
    Get-ChildItem $publishDirectory -Recurse -File | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('/', '\')
        if ($relative.Contains('"') -or $relative.Contains('$')) { throw 'Invalid publish filename' }
        $uninstallLines.Add('Delete "$INSTDIR\' + $relative + '"')
    }
    $uninstallLines.Add('Delete "$INSTDIR\Uninstall.exe"')
    Get-ChildItem $publishDirectory -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('/', '\')
        $uninstallLines.Add('RMDir "$INSTDIR\' + $relative + '"')
    }
    $uninstallLines.Add('RMDir "$INSTDIR"')
    $uninstallLines | Set-Content "$publishDirectory/uninstall-files.nsh" -Encoding utf8
    $zip = Join-Path $projectRoot "artifacts/LocalControl-$releaseVersion-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Get-ChildItem $publishDirectory | Where-Object Name -ne 'uninstall-files.nsh').FullName -DestinationPath $zip
    if ($Installer) {
        $compiler = Get-Command makensis.exe -ErrorAction SilentlyContinue
        if ($compiler) { $nsis = $compiler.Source }
        else { $nsis = Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe' }
        if (-not (Test-Path $nsis)) { throw 'Install NSIS 3.x to build Setup.' }
        & $nsis /V3 /INPUTCHARSET UTF8 "/DAPP_VERSION=$releaseVersion" "/DPUBLISH_DIR=$publishDirectory" "/DOUT_FILE=$projectRoot/artifacts/LocalControl-$releaseVersion-Setup.exe" installer/LocalControl.nsi
        if ($LASTEXITCODE -ne 0) { throw 'NSIS build failed' }
    }
    Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.exe','.zip' } | ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)" | Set-Content -Path ($_.FullName + '.sha256') -Encoding ascii
    }
    Write-Host 'LocalControl 0.3.1 packages produced. Run installed-app, update and uninstall checks before publishing.'
} finally { Pop-Location }
