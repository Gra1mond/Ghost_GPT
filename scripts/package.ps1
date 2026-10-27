param([string]$PublishDirectory = 'artifacts/GhostGPT-win-x64')
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
Copy-Item README.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination $packageRoot
Copy-Item docs -Destination $packageRoot -Recurse -Force
$runtimeConfig = Get-Content (Join-Path $packageRoot 'GhostGPT.runtimeconfig.json') -Raw | ConvertFrom-Json
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $sourceRoot = Join-Path (Join-Path $nugetRoot $packageName) $framework.version
    foreach ($licenseName in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        $sourceFile = Join-Path $sourceRoot $licenseName
        if (Test-Path -LiteralPath $sourceFile) {
            Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $packageRoot "docs/licenses/$packageName-$licenseName.txt")
        }
    }
}
$archive = Join-Path (Split-Path $packageRoot) 'GhostGPT-win-x64.zip'
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -Force
Get-Item -LiteralPath $archive | Select-Object FullName, Length
