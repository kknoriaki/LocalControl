$ErrorActionPreference='Stop'
$client='{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
function RuntimePresent {
    foreach($root in @('HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\','HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\','HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\')){
        $value=(Get-ItemProperty ($root+$client) -Name pv -ErrorAction SilentlyContinue).pv
        if($value -and $value -ne '0.0.0.0'){return $true}
    }
    return $false
}
if(RuntimePresent){exit 0}
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('LocalControl-WebView2-'+[guid]::NewGuid().ToString('N'))
New-Item $temporary -ItemType Directory | Out-Null
try{
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $file=Join-Path $temporary 'MicrosoftEdgeWebview2Setup.exe'
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $file -UseBasicParsing
    if((Get-Item $file).Length -gt 32MB){throw 'Unexpected prerequisite download size'}
    $signature=Get-AuthenticodeSignature $file
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'WebView2 installer is not signed by Microsoft'}
    $process=Start-Process $file -ArgumentList '/silent','/install' -Wait -PassThru
    if($process.ExitCode -ne 0 -or -not (RuntimePresent)){throw 'WebView2 installation failed'}
    exit 0
}catch{Write-Error 'Unable to install verified Microsoft WebView2 Runtime. Install it from the official Microsoft website and run Setup again.';exit 1}
finally{Remove-Item $temporary -Recurse -Force -ErrorAction SilentlyContinue}
