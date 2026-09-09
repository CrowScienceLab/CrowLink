$ErrorActionPreference = 'Stop'
$api = 'https://api.github.com/repos/CrowScienceLab/CrowLink'
$commit = '692aa4b3aec8533b58355058e0d152a6cf065f03'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repoRoot 'artifacts'
$files = @('CrowLink-1.8.1-Setup-win-x64.exe','CrowLink-1.8.1-Setup-win-arm64.exe','CrowLink-1.8.1-Manual-KO.html','SHA256SUMS.txt')
foreach ($file in $files) { if (-not (Test-Path -LiteralPath (Join-Path $assetRoot $file))) { throw "Missing release asset: $file" } }
$env:GCM_INTERACTIVE = 'never'
$env:GIT_TERMINAL_PROMPT = '0'
$credentialLines = @('protocol=https','host=github.com','') | git credential fill
if ($LASTEXITCODE -ne 0) { throw 'Git authentication unavailable.' }
$credential = @{}
foreach ($line in $credentialLines) { if ($line -match '^([^=]+)=(.*)$') { $credential[$matches[1]]=$matches[2] } }
if (-not $credential['password']) { throw 'Git authentication unavailable.' }
$headers = @{Authorization='Bearer '+$credential['password'];'User-Agent'='CrowLink-release';Accept='application/vnd.github+json'}
$remote = Invoke-RestMethod -Uri "$api/git/refs/heads/main" -Headers $headers
if ($remote.object.sha -ne $commit) { throw 'Remote main changed; inspect before publishing.' }
$releases = @(Invoke-RestMethod -Uri "$api/releases" -Headers $headers)
if ($releases | Where-Object {$_.tag_name -eq 'v1.8.1'}) { throw 'Release already exists; inspect before retrying.' }
$notes = [IO.File]::ReadAllText((Join-Path $repoRoot 'docs\RELEASE-v1.8.1.md'))
$notes += "`n`n## 다운로드`n일반 Intel/AMD PC: win-x64.exe. Windows ARM PC: win-arm64.exe. 설치 파일은 .NET 런타임을 포함합니다. SHA256SUMS.txt로 무결성을 확인할 수 있습니다.`n"
$body = @{tag_name='v1.8.1';target_commitish=$commit;name='CrowLink 1.8.1';body=$notes;draft=$true;prerelease=$false} | ConvertTo-Json
$release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
Write-Output "Draft release created: $($release.id)"
foreach ($name in $files) {
 $path = Join-Path $assetRoot $name
 $url = ($release.upload_url -replace '\{.*$','') + '?name=' + [Uri]::EscapeDataString($name)
 $asset = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -ContentType 'application/octet-stream' -InFile $path -TimeoutSec 180
 $expected = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
 if ($asset.size -ne (Get-Item -LiteralPath $path).Length -or $asset.state -ne 'uploaded') { throw "Asset verification failed: $name" }
 if ($asset.digest -and $asset.digest -ne "sha256:$expected") { throw "Asset hash mismatch: $name" }
 Write-Output "Verified asset: $name ($($asset.size) bytes)"
}
$published = Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType 'application/json' -Body '{"draft":false,"make_latest":"true"}'
Write-Output "Published: $($published.html_url)"
