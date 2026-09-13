param(
    [Parameter(Mandatory)]
    [string]$PackagePath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$package = Get-Item $PackagePath
$project = Join-Path $PSScriptRoot 'src\GptImagePlayground'
[xml]$manifest = Get-Content (Join-Path $project 'Package.appxmanifest') -Raw
$assets = Get-Content (Join-Path $project 'obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$output = Join-Path $project "bin\x64\$Configuration\net9.0-windows10.0.19041.0\win-x64"
$deps = Get-Content (Join-Path $output 'GptImagePlayground.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$kind = if ($CertificateThumbprint) { 'signed' } else { 'unsigned' }
$name = "GptImagePlayground_$($manifest.Package.Identity.Version)_x64_$kind"
$directory = Join-Path $PSScriptRoot "artifacts\distribution\$name"
if (Test-Path $directory) {
    throw "Distribution folder already exists: $directory. Move it aside before exporting again."
}
$null = New-Item $directory -ItemType Directory
Copy-Item $package.FullName $directory
foreach ($file in @('LICENSE', 'README.md', 'README-en.md')) {
    Copy-Item (Join-Path $PSScriptRoot $file) $directory
}

# Include the resolved dependency graph and implicit runtime packs, not just direct references.
$packages = @{}
foreach ($entry in $assets.libraries.GetEnumerator()) {
    if ($entry.Value.type -eq 'package') { $packages[$entry.Key] = $entry.Value.path }
}
foreach ($key in $deps.libraries.Keys) {
    if ($key.StartsWith('runtimepack.')) {
        $id = $key.Substring('runtimepack.'.Length)
        $packages[$id] = $id.ToLowerInvariant()
    }
}
$noticeRoot = Join-Path $directory 'ThirdPartyNotices'
$null = New-Item $noticeRoot -ItemType Directory
$inventory = [System.Collections.Generic.List[object]]::new()
$mit = (Get-Content (Join-Path $PSScriptRoot 'LICENSE') -Raw)
$mit = $mit.Substring($mit.IndexOf('Permission is hereby granted'))
foreach ($entry in $packages.GetEnumerator() | Sort-Object Key) {
    $source = $null
    foreach ($root in $assets.packageFolders.Keys) {
        $candidate = Join-Path $root ($entry.Value.Replace('/', '\'))
        if (Test-Path $candidate -PathType Container) { $source = $candidate; break }
    }
    if (-not $source) { throw "Restored package not found: $($entry.Key)" }
    $specFile = @(Get-ChildItem $source -Filter '*.nuspec' -File)
    if ($specFile.Count -ne 1) { throw "Expected one nuspec for $($entry.Key)" }
    [xml]$spec = Get-Content $specFile[0].FullName -Raw
    $metadata = $spec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $copyright = $metadata.SelectSingleNode("*[local-name()='copyright']")
    $licenseUrl = $metadata.SelectSingleNode("*[local-name()='licenseUrl']")
    $destination = Join-Path $noticeRoot ($entry.Key.Replace('/', '\'))
    $null = New-Item $destination -ItemType Directory -Force
    $noticeFiles = @(Get-ChildItem $source -File -Recurse | Where-Object {
        $_.Name -match '(?i)(license|licence|notice|copying)' -and $_.Extension -notin @('.dll', '.exe')
    })
    foreach ($file in $noticeFiles) {
        $target = Join-Path $destination ([IO.Path]::GetRelativePath($source, $file.FullName))
        $null = New-Item (Split-Path $target) -ItemType Directory -Force
        Copy-Item $file.FullName $target
    }
    if ($null -ne $license -and $license.GetAttribute('type') -eq 'expression') {
        if ($license.InnerText -ne 'MIT') { throw "Review license expression for $($entry.Key): $($license.InnerText)" }
        if ($null -eq $copyright) { throw "Missing MIT copyright for $($entry.Key)" }
        "MIT License`n`n$($copyright.InnerText)`n`n$mit" |
            Set-Content (Join-Path $destination 'NUGET-LICENSE.txt') -Encoding utf8
    } elseif ($noticeFiles.Count -eq 0) {
        if ($null -eq $licenseUrl -or $licenseUrl.InnerText -ne 'https://aka.ms/WinSDKLicenseURL') {
            throw "No license text available for $($entry.Key)"
        }
        # SDK projection/runtime references use the same SDK terms shipped with the MSIX build tools.
        $sdk = @($packages.Keys | Where-Object { $_ -like 'Microsoft.Windows.SDK.BuildTools.MSIX/*' })
        if ($sdk.Count -ne 1) { throw 'Cannot resolve Windows SDK license.' }
        $sdkLicense = $null
        foreach ($root in $assets.packageFolders.Keys) {
            $candidate = Join-Path $root "$($packages[$sdk[0]].Replace('/', '\'))\sdk_license.txt"
            if (Test-Path $candidate) { $sdkLicense = $candidate; break }
        }
        if (-not $sdkLicense) { throw 'Windows SDK license file is missing.' }
        Copy-Item $sdkLicense (Join-Path $destination 'sdk_license.txt')
    }
    $inventory.Add([ordered]@{
        Package = $entry.Key
        License = if ($null -ne $license) { $license.InnerText } else { $licenseUrl.InnerText }
        Copyright = if ($null -ne $copyright) { $copyright.InnerText } else { '' }
        NuGet = "https://www.nuget.org/packages/$($entry.Key)"
    })
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $noticeRoot 'packages.json') -Encoding utf8

$distributedPackage = Join-Path $directory $package.Name
if ($CertificateThumbprint) {
    $certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.Subject -ne $manifest.Package.Identity.Publisher -or
        $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
        throw 'Signing certificate must be valid, have a private key, and match the package Publisher.'
    }
    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse |
        Where-Object FullName -Match '\\x64\\signtool.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signTool) { throw 'Windows SDK x64 signtool.exe is required.' }
    & $signTool.FullName sign /fd SHA256 /sha1 $CertificateThumbprint $distributedPackage
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    $signature = Get-AuthenticodeSignature $distributedPackage
    if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
        throw 'MSIX signer does not match the requested certificate.'
    }
    # PowerShell maps an untrusted root to UnknownError. Inspect the native status instead of accepting arbitrary errors.
    if (-not ('DistributionSignature' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DistributionSignature
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr Handle, Subject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint Ui, Revocation, Choice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr State, Url;
        public uint Flags, Context;
        public IntPtr Settings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    public static uint Verify(string path)
    {
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(file, memory, false);
            var data = new TrustData {
                Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1,
                File = memory, Flags = 0x1000
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        }
        finally
        {
            Marshal.DestroyStructure<FileInfo>(memory);
            Marshal.FreeHGlobal(memory);
        }
    }
}
'@
    }
    $trust = [DistributionSignature]::Verify($distributedPackage)
    if ($trust -eq 0x800B0109L) {
        Write-Warning 'Signature root is not trusted (CERT_E_UNTRUSTEDROOT). Recipients must independently verify and trust the publisher before installing.'
    } elseif ($trust -ne 0) {
        throw ('MSIX signature verification failed: 0x{0:X8}' -f $trust)
    }
    $null = Export-Certificate -Cert $certificate -FilePath (Join-Path $directory 'Publisher.cer')
    Write-Host "Signature status: $($signature.Status). Self-signed certificates require explicit recipient trust."
}

$hash = (Get-FileHash $distributedPackage -Algorithm SHA256).Hash
"$hash  $($package.Name)" | Set-Content (Join-Path $directory 'SHA256SUMS.txt') -Encoding ascii
$zip = "$directory.zip"
Compress-Archive -Path "$directory\*" -DestinationPath $zip -Force
Get-FileHash $zip -Algorithm SHA256 | Format-List
Write-Host "Distribution bundle: $zip"
Write-Host 'Distribute the complete bundle with its license notices. Signing does not imply public certificate trust.'
