param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+(\.\d+)?([A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version,

    [switch]$Publish,

    [string]$Proxy = 'http://127.0.0.1:10809'
)

$ErrorActionPreference = 'Stop'

$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $project 'dist'
$tag = 'v' + $Version
$stage = Join-Path $dist ('release-' + $tag)
$archive = Join-Path $dist ('EcloudLite-' + $tag + '-win-net48.zip')
$notes = Join-Path $stage 'RELEASE_NOTES.md'
$appInfoText = Get-Content -LiteralPath (Join-Path $project 'src\Infrastructure\AppInfo.cs') -Raw
if ($appInfoText -notmatch 'LiteVersion\s*=\s*"([^"]+)"') {
    throw 'Unable to read LiteVersion from AppInfo.cs'
}
if ($Matches[1] -ne $Version) {
    throw "Release version $Version does not match AppInfo.LiteVersion $($Matches[1])"
}

function Require-Success([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

Push-Location $project
try {
    & (Join-Path $project 'build.ps1') -SkipOfficialRuntime
    Require-Success 'Open-source build'

    & (Join-Path $dist 'EcloudLite.SelfTest.exe')
    Require-Success 'Self-test'

    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stage | Out-Null

    $packageFiles = @(
        (Join-Path $dist 'EcloudLite.exe'),
        (Join-Path $dist 'EcloudLite.SelfTest.exe'),
        (Join-Path $dist 'cmsszte-public.pem'),
        (Join-Path $project 'clear-logs.bat'),
        (Join-Path $project 'README.md'),
        (Join-Path $project 'LICENSE'),
        (Join-Path $project 'DISCLAIMER.md'),
        (Join-Path $project 'THIRD_PARTY_NOTICES.md')
    )
    foreach ($file in $packageFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Release input is missing: $file"
        }
        Copy-Item -LiteralPath $file -Destination $stage -Force
    }

    $releaseNotes = @(
        "# EcloudLite $tag",
        '',
        'Public preview built from the open-source repository. This package does not contain the official CMSS runtime, official installers, account data, tokens, verification codes, logs, or local settings.',
        '',
        'Without the official runtime, login, desktop listing, and local session management remain available. On first run, the built-in setup wizard can locally extract the minimum CMSS runtime from an official Windows installer selected by the user.',
        '',
        'This build includes single-device CMSSZTE Path B keepalive with detailed redacted logs. Long-duration effectiveness still requires validation on an authorized cloud desktop; production_claim remains false.',
        '',
        'This project is not an official China Mobile client. Read DISCLAIMER.md and THIRD_PARTY_NOTICES.md before use.'
    )
    Set-Content -LiteralPath $notes -Encoding UTF8 -Value $releaseNotes

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal -Force
    $hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII -Value ($hash.Hash + '  ' + (Split-Path -Leaf $archive))

    Write-Host "Release package: $archive"
    Write-Host "SHA-256: $($hash.Hash)"

    if ($Publish) {
        $dirty = & git status --porcelain
        Require-Success 'Git status check'
        if ($dirty) {
            throw 'The Git worktree is not clean. Commit or stash changes before publishing.'
        }
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw 'GitHub CLI is required for -Publish. Install gh and run gh auth login first.'
        }

        $env:HTTP_PROXY = $Proxy
        $env:HTTPS_PROXY = $Proxy

        & git rev-parse -q --verify ('refs/tags/' + $tag) 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            & git tag -a $tag -m "EcloudLite $tag"
            Require-Success 'Tag creation'
        }

        & git push origin $tag
        Require-Success 'Tag push'

        & gh release view $tag --repo ykc1043/EcloudLite *> $null
        if ($LASTEXITCODE -eq 0) {
            throw "GitHub Release $tag already exists. Refusing to overwrite it."
        }

        & gh release create $tag $archive ($archive + '.sha256') `
            --repo ykc1043/EcloudLite `
            --title "EcloudLite $tag" `
            --notes-file $notes
        Require-Success 'GitHub Release creation'
    }
}
finally {
    Pop-Location
}
