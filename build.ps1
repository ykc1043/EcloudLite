param(
    [switch]$SkipOfficialRuntime
)

$ErrorActionPreference = 'Stop'

$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $project 'src'
$dist = Join-Path $project 'dist'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found: $csc"
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$files = Get-ChildItem -LiteralPath $source -Filter '*.cs' -File -Recurse | Select-Object -ExpandProperty FullName
$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Web.Extensions.dll',
    'System.Security.dll'
)
$referenceArgs = $references | ForEach-Object { '/reference:' + $_ }

& $csc /nologo /codepage:65001 /optimize+ /platform:anycpu /target:winexe `
    /main:EcloudLite.Program `
    ('/win32manifest:' + (Join-Path $project 'app.manifest')) `
    ('/out:' + (Join-Path $dist 'EcloudLite.exe')) `
    $referenceArgs $files
if ($LASTEXITCODE -ne 0) { throw "Application build failed: $LASTEXITCODE" }

& $csc /nologo /codepage:65001 /optimize+ /platform:anycpu /target:exe `
    /main:EcloudLite.SelfTestProgram `
    ('/out:' + (Join-Path $dist 'EcloudLite.SelfTest.exe')) `
    $referenceArgs $files
if ($LASTEXITCODE -ne 0) { throw "Self-test build failed: $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $project 'assets\cmsszte-public.pem') -Destination (Join-Path $dist 'cmsszte-public.pem') -Force
if ($SkipOfficialRuntime) {
    Write-Host 'Skipping proprietary CMSS runtime assembly by request.'
}
else {
    $officialRuntimeSource = Join-Path (Split-Path -Parent $project) 'analysis\cmss-full\drivers\CMSS\client\uSmartView_VDI_Client.exe'
    if (Test-Path -LiteralPath $officialRuntimeSource) {
        & (Join-Path $project 'build-cmss-runtime.ps1')
        if ($LASTEXITCODE -ne 0) { throw "CMSS runtime build failed: $LASTEXITCODE" }
    }
    else {
        Write-Warning 'Official CMSS source files are unavailable; the open-source executables were built without assembling cmss-runtime.'
    }
}

Get-ChildItem -LiteralPath $dist -File | Select-Object Name, Length, LastWriteTime
