[CmdletBinding()]
param([string]$Archive,[string]$Version,[switch]$InitializeKey,[string]$KeyDirectory=(Join-Path $env:LOCALAPPDATA 'LocalControl-ReleaseSigning'))
$ErrorActionPreference='Stop'
New-Item $KeyDirectory -ItemType Directory -Force | Out-Null
$privateFile=Join-Path $KeyDirectory 'publisher-key.dpapi'
$key=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::NamedCurves.nistP256)
if(Test-Path $privateFile){$protected=[IO.File]::ReadAllBytes($privateFile);$bytes=[Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);$consumed=0;$key.ImportPkcs8PrivateKey($bytes,[ref]$consumed)}
else{$protected=[Security.Cryptography.ProtectedData]::Protect($key.ExportPkcs8PrivateKey(),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);[IO.File]::WriteAllBytes($privateFile,$protected)}
$projectRoot=Split-Path $PSScriptRoot -Parent
New-Item "$projectRoot/build-config" -ItemType Directory -Force | Out-Null
[IO.File]::WriteAllText("$projectRoot/build-config/publisher-public.pem",$key.ExportSubjectPublicKeyInfoPem())
if($InitializeKey){$key.Dispose();Write-Host 'Publisher public key initialized. Build the first package now.';return}
if(-not $Archive -or -not $Version){throw 'Specify Archive and Version, or InitializeKey.'}
$file=Get-Item $Archive
$hash=(Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
[version]$parsedVersion=$null
if(-not [version]::TryParse($Version,[ref]$parsedVersion)){throw 'Use a numeric version, for example 1.0.1'}
$canonical="LocalControl-Update-v1`n$Version`n$($file.Name)`n$hash`n$($file.Length)`n"
$signature=$key.SignData([Text.Encoding]::UTF8.GetBytes($canonical),[Security.Cryptography.HashAlgorithmName]::SHA256)
$manifest=@{Manifest=@{Version=$Version;Archive=$file.Name;Sha256=$hash;Length=$file.Length};Signature=[Convert]::ToBase64String($signature)}
$manifest | ConvertTo-Json -Depth 5 | Set-Content ($file.FullName+'.update.json') -Encoding utf8
$key.Dispose()
Write-Host 'Signed manifest created. Keep the DPAPI private key out of GitHub. Include the public key in the initial trusted build.'
