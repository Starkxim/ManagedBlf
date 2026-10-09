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
foreach ($kind in @('framework-dependent', 'self-contained')) {
    $package = Join-Path $destination "ManagedBlf.Viewer-$Version-win-x64-$kind"
    $selfContained = if ($kind -eq 'self-contained') { 'true' } else { 'false' }
    & dotnet publish (Join-Path $root 'samples/ManagedBlf.Viewer/ManagedBlf.Viewer.csproj') -c Release -r win-x64 --self-contained $selfContained -p:Version=$Version -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Viewer publish failed.' }
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
