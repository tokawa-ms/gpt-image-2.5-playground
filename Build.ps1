param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$WinUIAnalyzerPath,
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet test tests\GptImagePlayground.Tests\GptImagePlayground.Tests.csproj -c $Configuration --logger 'trx;LogFileName=contracts.trx' --results-directory artifacts\tests -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }

    $packageDirectory = Join-Path $PSScriptRoot 'artifacts\msix\'
    $manifest = [xml](Get-Content 'src\GptImagePlayground\Package.appxmanifest' -Raw)
    $packageVersion = $manifest.Package.Identity.Version
    $arguments = @(
        'build', 'src\GptImagePlayground\GptImagePlayground.csproj',
        '-c', $Configuration, '-p:Platform=x64',
        '-p:GenerateAppxPackageOnBuild=true', '-p:AppxPackageSigningEnabled=false',
        "-p:AppxPackageDir=$packageDirectory", '-p:UapAppxPackageBuildMode=SideloadOnly', '-v:minimal'
    )
    if ($WinUIAnalyzerPath) {
        $arguments += "-p:WinUIAnalyzerPath=$((Resolve-Path $WinUIAnalyzerPath).Path)"
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'WinUI/MSIX build failed.' }

    $package = Join-Path $packageDirectory ('GptImagePlayground_{0}_x64_Test\GptImagePlayground_{0}_x64.msix' -f $packageVersion)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        foreach ($required in @('AppxManifest.xml', 'appsettings.json', 'coreclr.dll', 'Microsoft.ui.xaml.dll', 'GptImagePlayground.runtimeconfig.json', 'ja/GptImagePlayground.Core.resources.dll')) {
            if (-not $archive.GetEntry($required)) { throw "Package missing: $required" }
        }
        $outputDirectory = Join-Path $PSScriptRoot "src\GptImagePlayground\bin\x64\$Configuration\net9.0-windows10.0.19041.0\win-x64"
        foreach ($asset in Get-ChildItem 'src\GptImagePlayground\Assets' -File | Where-Object Extension -In @('.png', '.ico', '.svg')) {
            $relativePath = "Assets/$($asset.Name)"
            $installedAsset = Join-Path $outputDirectory "Assets\$($asset.Name)"
            $entry = $archive.GetEntry($relativePath)
            if (-not (Test-Path $installedAsset) -or -not $entry) {
                throw "Branding asset missing from development output or MSIX: $relativePath"
            }
            $expectedHash = (Get-FileHash $asset.FullName -Algorithm SHA256).Hash
            if ((Get-FileHash $installedAsset -Algorithm SHA256).Hash -ne $expectedHash) {
                throw "Development branding asset differs from source: $relativePath"
            }
            $stream = $entry.Open()
            try { $packagedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($packagedHash -ne $expectedHash) { throw "Packaged branding asset differs from source: $relativePath" }
        }
        $reader = [IO.StreamReader]::new($archive.GetEntry('GptImagePlayground.runtimeconfig.json').Open())
        try { $runtime = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if (-not ($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -like '9.*' })) {
            throw 'The package does not declare its self-contained .NET 9 runtime.'
        }
    }
    finally { $archive.Dispose() }
    Get-FileHash $package -Algorithm SHA256 | Format-List
    Write-Host 'Verified unsigned x64 MSIX. Signing/trust is required for distribution.'
    & "$PSScriptRoot\Export-Distribution.ps1" -PackagePath $package -Configuration $Configuration -CertificateThumbprint $CertificateThumbprint
}
finally { Pop-Location }
