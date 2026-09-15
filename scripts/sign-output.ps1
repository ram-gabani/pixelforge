param(
    [Parameter(Mandatory=$true, ValueFromRemainingArguments=$true)]
    [string[]]$Files
)

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -match 'PixelForge' } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=PixelForgeDev' -CertStoreLocation Cert:\CurrentUser\My
}

foreach ($file in $Files) {
    if (Test-Path $file) {
        Set-AuthenticodeSignature -Certificate $cert -FilePath $file | Out-Null
    }
}

