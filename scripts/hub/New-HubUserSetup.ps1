[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$ManifestPrivateKeyPath,
    [Parameter(Mandatory)][string]$SignScript,
    [Parameter(Mandatory)][string]$PublisherThumbprint
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows setup must be built on Windows.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable three-part Hub version is required.' }
$signer = (Resolve-Path -LiteralPath $SignScript).Path
$privateKey = (Resolve-Path -LiteralPath $ManifestPrivateKeyPath).Path
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputRoot).Path
$work = Join-Path $output ('build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText($privateKey))
    $publicKey = Join-Path $work 'release-public.pem'
    [IO.File]::WriteAllText($publicKey, $rsa.ExportSubjectPublicKeyInfoPem())
    $payload = Join-Path $work 'payload'
    $updater = Join-Path $work 'updater'
    $setup = Join-Path $work 'setup'
    function Publish-App([string]$Project, [string]$Destination, [string[]]$ExtraArguments = @()) {
        & dotnet publish $Project -c Release -r win-x64 --self-contained true -o $Destination `
            "-p:Version=$Version" "-p:HubReleasePublicKey=$publicKey" -p:TreatWarningsAsErrors=true `
            -p:ILLinkTreatWarningsAsErrors=true -p:DebugType=None -p:DebugSymbols=false @ExtraArguments
        if ($LASTEXITCODE -ne 0) { throw "NativeAOT publishing failed: $Project" }
    }
    function Sign-And-Verify([string]$Path) {
        # A fixed script path receives a structured argument. No interpolated shell
        # command, private key, or signing credential is written to release logs.
        & $signer -FilePath $Path
        if (-not $?) { throw 'The signing provider failed.' }
        $signature = Get-AuthenticodeSignature -LiteralPath $Path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $PublisherThumbprint) {
            throw "The artifact has no valid signature from the expected publisher: $([IO.Path]::GetFileName($Path))"
        }
    }
    Publish-App 'src/Lakona.Hub/Lakona.Hub.csproj' $payload
    Publish-App 'src/Lakona.Hub.Updater/Lakona.Hub.Updater.csproj' $updater
    Get-ChildItem -LiteralPath $updater -File | Where-Object Extension -ne '.pdb' | ForEach-Object {
        if ($_.Name -eq 'Lakona.Hub.Updater.exe' -or -not (Test-Path -LiteralPath (Join-Path $payload $_.Name))) {
            Copy-Item -LiteralPath $_.FullName -Destination $payload
        }
    }
    Get-ChildItem -LiteralPath $payload -Recurse -Filter '*.exe' | ForEach-Object { Sign-And-Verify $_.FullName }
    $files = [ordered]@{}
    Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/')
        $files[$relative] = [ordered]@{ Size = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $manifest = [ordered]@{ Protocol = 1; Version = $Version; Platform = 'win-x64'; Files = $files } | ConvertTo-Json -Depth 8
    $bytes = [Text.Encoding]::UTF8.GetBytes($manifest)
    [IO.File]::WriteAllBytes((Join-Path $payload '.lakona-package.json'), $bytes)
    [IO.File]::WriteAllBytes((Join-Path $payload '.lakona-package.sig'), $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss))
    $archive = Join-Path $work "lakona-hub-$Version-win-x64-user.zip"
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    Publish-App 'src/Lakona.Hub.Setup/Lakona.Hub.Setup.csproj' $setup @("-p:HubSetupPayload=$archive")
    $setupExecutable = Join-Path $setup 'Lakona.Hub.Setup.exe'
    Sign-And-Verify $setupExecutable
    Copy-Item -LiteralPath $archive -Destination $output
    Copy-Item -LiteralPath $setupExecutable -Destination (Join-Path $output 'Lakona.Hub.Setup.exe')
    Write-Host 'Signed offline setup and update payload created. Complete the Windows release-candidate tests before distribution.'
}
finally {
    $rsa.Dispose()
    # Work is a freshly-created direct child of the resolved output directory.
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($work)) -ne $output) { throw 'Invalid build cleanup path.' }
    Remove-Item -LiteralPath $work -Recurse -Force
}
