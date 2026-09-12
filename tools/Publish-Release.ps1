param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$Commit
)
$ErrorActionPreference = 'Stop'
$api = 'https://api.github.com/repos/CrowScienceLab/CrowLink'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repoRoot 'artifacts'
$assets = @(
    @{ Name = "CrowLink-$Version-Setup-win-x64.exe"; Path = Join-Path $assetRoot "CrowLink-$Version-Setup-win-x64.exe" },
    @{ Name = "CrowLink-$Version-Manual-KO.html"; Path = Join-Path $repoRoot "docs\CrowLink-$Version-Manual-KO.html" },
    @{ Name = "SHA256SUMS-$Version.txt"; Path = Join-Path $assetRoot "SHA256SUMS-$Version.txt" }
)
foreach ($entry in $assets) {
    if (-not (Test-Path -LiteralPath $entry.Path)) { throw "Missing release asset: $($entry.Name)" }
}
$env:GCM_INTERACTIVE = 'never'
$env:GIT_TERMINAL_PROMPT = '0'
$credentialLines = @('protocol=https','host=github.com','') | git credential fill
if ($LASTEXITCODE -ne 0) { throw 'Git authentication unavailable.' }
$credential = @{}
foreach ($line in $credentialLines) { if ($line -match '^([^=]+)=(.*)$') { $credential[$matches[1]]=$matches[2] } }
if (-not $credential['password']) { throw 'Git authentication unavailable.' }
$headers = @{Authorization='Bearer '+$credential['password'];'User-Agent'='CrowLink-release';Accept='application/vnd.github+json'}
$remote = Invoke-RestMethod -Uri "$api/git/refs/heads/main" -Headers $headers
if ($remote.object.sha -ne $Commit) { throw 'Remote main changed; inspect before publishing.' }
$releases = @(Invoke-RestMethod -Uri "$api/releases" -Headers $headers)
if ($releases | Where-Object {$_.tag_name -eq "v$Version"}) { throw "Release v$Version already exists." }
$notes = [IO.File]::ReadAllText((Join-Path $repoRoot "docs\RELEASE-v$Version.md"))
$body = @{tag_name="v$Version";target_commitish=$Commit;name="CrowLink $Version";body=$notes;draft=$true;prerelease=$false} | ConvertTo-Json
$release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
Write-Output "Draft release created: $($release.id)"
foreach ($entry in $assets) {
    $name = $entry.Name
    $path = $entry.Path
    $url = ($release.upload_url -replace '\{.*$','') + '?name=' + [Uri]::EscapeDataString($name)
    $asset = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -ContentType 'application/octet-stream' -InFile $path -TimeoutSec 180
    $expected = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.size -ne (Get-Item -LiteralPath $path).Length -or $asset.state -ne 'uploaded') { throw "Asset verification failed: $name" }
    if ($asset.digest -and $asset.digest -ne "sha256:$expected") { throw "Asset hash mismatch: $name" }
    Write-Output "Verified asset: $name ($($asset.size) bytes)"
}
$published = Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'
Write-Output "Published: $($published.html_url)"
