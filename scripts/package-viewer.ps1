param(
    [string]$Version = '0.1.0-alpha',
    [string]$OutputDirectory = 'artifacts/viewer'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+-alpha(?:\.\d+)?$') { throw 'An explicit alpha version is required.' }
$root = Split-Path $PSScriptRoot -Parent
$destination = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (Test-Path $destination) { throw "Output already exists: $destination" }
New-Item -ItemType Directory $destination | Out-Null
$cacheOutput = & dotnet nuget locals global-packages --list
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate NuGet package cache.' }
$packageCache = ($cacheOutput -replace '^global-packages:\s*', '').Trim()
function Copy-DotNetNotices([string]$Id, [string]$PackageVersion, [string]$PackageDirectory, [string[]]$RequiredFiles) {
    $idLower = $Id.ToLowerInvariant()
    if ($idLower -notin @('microsoft.netcore.app.host.win-x64', 'microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64') -or $PackageVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Unexpected runtime notice package.'
    }
    $sourceUrl = "https://api.nuget.org/v3-flatcontainer/$idLower/$PackageVersion/$idLower.$PackageVersion.nupkg"
    $cached = Join-Path $packageCache "$idLower/$PackageVersion"
    $notices = Join-Path $PackageDirectory "third-party-licenses/$idLower"
    New-Item -ItemType Directory $notices -Force | Out-Null
    $missing = @($RequiredFiles | Where-Object { !(Test-Path (Join-Path $cached $_)) })
    if ($missing.Count -eq 0) {
        foreach ($name in $RequiredFiles) { Copy-Item (Join-Path $cached $name) (Join-Path $notices $name) }
    } else {
        # SDK-installed apphost packs may not be in the NuGet cache. Fetch the
        # exact official package for notices; never substitute another runtime/version.
        $archive = Join-Path $destination "$idLower.$PackageVersion.nupkg"
        if (!(Test-Path $archive)) { Invoke-WebRequest -Uri $sourceUrl -OutFile $archive }
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($name in $RequiredFiles) {
                $entry = @($zip.Entries | Where-Object { $_.FullName -ieq $name })
                if ($entry.Count -ne 1) { throw "Missing notice $name in $Id $PackageVersion" }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0], (Join-Path $notices $name), $true)
            }
        } finally { $zip.Dispose() }
    }
    "Package: $Id`nVersion: $PackageVersion`nSource: $sourceUrl" | Set-Content (Join-Path $notices 'SOURCE.txt') -Encoding utf8
    Write-Host "Included third-party notices: $Id $PackageVersion"
}
foreach ($kind in @('framework-dependent', 'self-contained')) {
    $package = Join-Path $destination "ManagedBlf.Viewer-$Version-win-x64-$kind"
    $selfContained = if ($kind -eq 'self-contained') { 'true' } else { 'false' }
    & dotnet publish (Join-Path $root 'samples/ManagedBlf.Viewer/ManagedBlf.Viewer.csproj') -c Release -r win-x64 --self-contained $selfContained -p:Version=$Version -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Viewer publish failed.' }
    $hostJson = & dotnet msbuild (Join-Path $root 'samples/ManagedBlf.Viewer/ManagedBlf.Viewer.csproj') -target:ProcessFrameworkReferences -getItem:AppHostPack -p:RuntimeIdentifier=win-x64 -p:SelfContained=$selfContained -verbosity:quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the published apphost pack.' }
    $hostPacks = @(($hostJson | ConvertFrom-Json).Items.AppHostPack | Where-Object { $_.RuntimeIdentifier -eq 'win-x64' })
    if ($hostPacks.Count -ne 1) { throw 'Expected exactly one Windows x64 apphost pack.' }
    $hostId = $hostPacks[0].NuGetPackageId
    $hostVersion = $hostPacks[0].NuGetPackageVersion
    if (!$hostId -or !$hostVersion) {
        # An SDK-installed apphost exposes PackageDirectory instead of NuGet metadata.
        $packDirectory = ([string]$hostPacks[0].PackageDirectory) -replace '\\', '/'
        if ($packDirectory -notmatch '/(?<id>Microsoft\.NETCore\.App\.Host\.win-x64)/(?<version>\d+\.\d+\.\d+)$') {
            throw 'Cannot identify the SDK-installed Windows apphost version.'
        }
        $hostId = $Matches.id
        $hostVersion = $Matches.version
    }
    Copy-DotNetNotices $hostId $hostVersion $package @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')
    if ($kind -eq 'self-contained') {
        $config = Get-Content (Join-Path $package 'ManagedBlf.Viewer.runtimeconfig.json') -Raw | ConvertFrom-Json
        $frameworks = @($config.runtimeOptions.includedFrameworks)
        foreach ($required in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
            $framework = @($frameworks | Where-Object { $_.name -eq $required })
            if ($framework.Count -ne 1) { throw "Missing included runtime $required" }
            if ($required -eq 'Microsoft.NETCore.App') {
                Copy-DotNetNotices 'Microsoft.NETCore.App.Runtime.win-x64' $framework[0].version $package @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')
            } else {
                Copy-DotNetNotices 'Microsoft.WindowsDesktop.App.Runtime.win-x64' $framework[0].version $package @('LICENSE')
            }
        }
    }
    Copy-Item (Join-Path $root 'LICENSE') $package
    Copy-Item (Join-Path $root 'docs/VIEWER-DOWNLOADS.md') (Join-Path $package 'README.md')
    Copy-Item (Join-Path $root 'docs/MANUAL-CHECKS.md') $package
    $exe = Join-Path $package 'ManagedBlf.Viewer.exe'
    $demo = Join-Path $destination "demo-$kind.blf"
    $generation = Start-Process $exe -ArgumentList @('--save-demo', ('"' + $demo + '"')) -Wait -PassThru
    if ($generation.ExitCode -ne 0 -or !(Test-Path $demo) -or (Get-Item $demo).Length -lt 144) { throw 'Packaged demo generation failed.' }
    # Verify create-new semantics, including byte preservation on attempted overwrite.
    $hash = (Get-FileHash $demo -Algorithm SHA256).Hash
    $overwrite = Start-Process $exe -ArgumentList @('--save-demo', ('"' + $demo + '"')) -Wait -PassThru
    if ($overwrite.ExitCode -eq 0 -or (Get-FileHash $demo -Algorithm SHA256).Hash -ne $hash) { throw 'Demo overwrite protection failed.' }
    $process = Start-Process $exe -PassThru
    try {
        Start-Sleep -Seconds 5
        $process.Refresh()
        if ($process.HasExited) { throw 'Viewer exited during startup smoke check.' }
        if ($process.MainWindowHandle -eq 0) { throw 'Viewer did not create a window.' }
        Write-Host "Startup smoke passed: $kind; demo bytes $((Get-Item $demo).Length)"
    } finally {
        if (!$process.HasExited) {
            $null = $process.CloseMainWindow()
            if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
        }
    }
    $files = Get-ChildItem $package -Recurse -File
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($package, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|private|\.git)([\\/]|$)' -or $file.Name -match '^(binlog\.dll|.*\.(blf|cs|user|suo|key|pem))$') {
            throw "Unexpected package file: $relative"
        }
    }
    if (!(Test-Path (Join-Path $package 'LICENSE'))) { throw 'Missing license.' }
    Compress-Archive -Path "$package/*" -DestinationPath "$package.zip"
}
Get-ChildItem $destination -Filter '*.zip' | ForEach-Object {
    $hash = Get-FileHash $_.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content (Join-Path $destination 'SHA256SUMS.txt') -Encoding ascii
