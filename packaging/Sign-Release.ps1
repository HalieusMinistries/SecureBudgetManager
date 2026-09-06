param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

# Certificate-dependent signing. This is the only remaining signing step.
# Provide a code-signing certificate in the CurrentUser\My store, then run:
#   powershell -File packaging\Sign-Release.ps1 -Path <exe-or-installer>
#
# Example when a PFX is available:
#   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f certificate.pfx /p <password> $Path

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) {
    Write-Host "signtool.exe was not found. Signing is skipped. Install the Windows SDK signing tools to sign builds."
    return
}

$certs = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue)
if ($certs.Count -eq 0) {
    Write-Host "No code-signing certificate is available. Signing is the sole external certificate blocker."
    return
}

& $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /sha1 $certs[0].Thumbprint $Path
if ($LASTEXITCODE -ne 0) {
    throw "Signing failed for $Path"
}
