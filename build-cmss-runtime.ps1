$ErrorActionPreference = 'Stop'

$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspace = Split-Path -Parent $project
$source = Join-Path $workspace 'analysis\cmss-full\drivers\CMSS\client'
$scanner = Join-Path $workspace 'analysis\tools\PeDependencyScanner.exe'
$runtime = Join-Path $project 'dist\cmss-runtime'
$runtimeClient = Join-Path $runtime 'client'
$support = Join-Path $workspace 'analysis\cmss-support\drivers\CMSS'
$supportRedirect = Join-Path $support 'redirect'
$entry = Join-Path $source 'uSmartView_VDI_Client.exe'

if (-not (Test-Path -LiteralPath $entry)) { throw "CMSS source client not found: $entry" }
if (-not (Test-Path -LiteralPath $scanner)) { throw "PE dependency scanner not found: $scanner" }

$distRoot = [System.IO.Path]::GetFullPath((Join-Path $project 'dist'))
$runtimeFull = [System.IO.Path]::GetFullPath($runtime)
if (-not $runtimeFull.StartsWith($distRoot + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path -Leaf $runtimeFull) -ne 'cmss-runtime') {
    throw "Refusing to rebuild unexpected runtime path: $runtimeFull"
}
if (Test-Path -LiteralPath $runtimeFull) { Remove-Item -LiteralPath $runtimeFull -Recurse -Force }
New-Item -ItemType Directory -Force -Path $runtimeClient | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $runtime 'log') | Out-Null
$copied = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$pluginDirectories = @(
    'platforms',
    'styles',
    'imageformats',
    'iconengines',
    'audio',
    'mediaservice',
    'bearer',
    'data',
    'guide',
    'sqldrivers',
    'win32',
    'win64',
    'Winsock'
)

$scanEntries = [System.Collections.Generic.List[string]]::new()
$scanEntries.Add($entry)
$serviceScanNames = @(
    'uSmartViewServiceAgent.exe',
    'usmartviewservice.dll',
    'iClassProxy.dll',
    'vdconn.dll',
    'EncryptDll.dll',
    'libcag.dll'
)
foreach ($name in $serviceScanNames) {
    $serviceEntry = Join-Path $source $name
    if (Test-Path -LiteralPath $serviceEntry) { $scanEntries.Add($serviceEntry) }
}
foreach ($name in $pluginDirectories) {
    $directory = Join-Path $source $name
    if (Test-Path -LiteralPath $directory) {
        Get-ChildItem -LiteralPath $directory -Recurse -File -Filter '*.dll' | ForEach-Object { $scanEntries.Add($_.FullName) }
    }
}
foreach ($scanEntry in $scanEntries) {
    $scanLines = & $scanner $source $scanEntry
    if ($LASTEXITCODE -ne 0) { throw "PE dependency scan failed for ${scanEntry}: $LASTEXITCODE" }
    foreach ($line in $scanLines) {
        if (-not $line.StartsWith("FILE`t")) { continue }
        $parts = $line -split "`t"
        if ($parts.Count -lt 2) { continue }
        $relative = $parts[1]
        if (-not $copied.Add($relative)) { continue }
        $from = Join-Path $source $relative
        $to = Join-Path $runtimeClient $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
    }
}

foreach ($name in $pluginDirectories) {
    $from = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $from)) { continue }
    $to = Join-Path $runtimeClient $name
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Copy-Item -Path (Join-Path $from '*') -Destination $to -Recurse -Force
}

$resourceNames = @(
    'desktop_switch.xml',
    'ErrorCodeDictionary.xml',
    'login_logo_right.png',
    'login_logo_rightdefault.png',
    'Microsoft.VC90.MFC.manifest',
    'rsa_pub.txt',
    'systeminfo.txt',
    'userAscriptionInfo.xml',
    'uSmartView.ico',
    'vdi_audio.wav',
    'VERSION',
    'vpn_CAG_new.xml',
    'vpn_CAG_ZTE.xml',
    'ztencr'
)
foreach ($name in $resourceNames) {
    $from = Join-Path $source $name
    if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $runtimeClient $name) -Force }
}

# The renderer resolves these modules with LoadLibrary at runtime, so they do not
# appear in the normal PE import closure scanned above.
$dynamicRuntimeNames = @(
    'vdconn.dll',
    'BasicFunc.dll',
    'iClassProxy.dll',
    'libcag.dll',
    'usbRedirectCheck.dll',
    'serialMsgLib.dll',
    'netdetect.dll',
    'TipTranslator.dll',
    'EncryptDll.dll',
    'libvdisk.dll',
    'usbMsgLib.dll',
    'usmartviewservice.dll',
    'uSmartViewServiceAgent.exe',
    'IntelligentQA.exe',
    'UapAgent.exe'
)
$dynamicCopied = 0
foreach ($name in $dynamicRuntimeNames) {
    $from = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $from)) {
        Write-Warning "Dynamic CMSS dependency not found: $name"
        continue
    }
    Copy-Item -LiteralPath $from -Destination (Join-Path $runtimeClient $name) -Force
    $dynamicCopied++
}

if (Test-Path -LiteralPath (Join-Path $supportRedirect 'clipboard')) {
    $redirectTarget = Join-Path $runtime 'redirect'
    New-Item -ItemType Directory -Force -Path $redirectTarget | Out-Null
    Copy-Item -LiteralPath (Join-Path $supportRedirect 'clipboard') -Destination $redirectTarget -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $project 'assets\cmsszte-public.pem') -Destination (Join-Path $runtimeClient 'cmsszte-public.pem') -Force
if (Test-Path -LiteralPath (Join-Path $support 'config')) {
    Copy-Item -LiteralPath (Join-Path $support 'config') -Destination $runtime -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $support 'updateinfo.ini')) {
    Copy-Item -LiteralPath (Join-Path $support 'updateinfo.ini') -Destination (Join-Path $runtime 'updateinfo.ini') -Force
}

$files = Get-ChildItem -LiteralPath $runtime -Recurse -File
$bytes = ($files | Measure-Object Length -Sum).Sum
$manifest = $files | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($runtime.Length).TrimStart('\')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash`t$($_.Length)`t$relative"
}
$manifest | Set-Content -LiteralPath (Join-Path $runtime 'runtime-manifest.sha256') -Encoding UTF8

[pscustomobject]@{
    Runtime = $runtime
    Files = $files.Count
    StaticClosureFiles = $copied.Count
    DynamicRuntimeFiles = $dynamicCopied
    Bytes = $bytes
    MiB = [math]::Round($bytes / 1MB, 2)
} | Format-List
