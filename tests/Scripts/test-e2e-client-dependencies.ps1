#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$target = Join-Path $repoRoot '.agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1'
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$null, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
# Import production functions without running scaffolding, builds or network I/O.
foreach ($definition in $ast.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}

function Assert-Equal($Expected, $Actual) {
    if ($Expected -cne $Actual) { throw "Expected '$Expected'; got '$Actual'." }
}

function Assert-DependencyError([scriptblock]$Action, [string]$Reason, [string]$Path) {
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    if (!$message -or !$message.Contains($Reason) -or !$message.Contains('Lakona.Game.Client') -or !$message.Contains($Path)) {
        throw "Expected '$Reason' with package and manifest path; got '$message'."
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('lakona-e2e-dependencies-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($engine in 'godot', 'unity', 'tuanjie') {
        $projectDir = Join-Path $tempRoot $engine
        New-Item -ItemType Directory -Force (Join-Path $projectDir 'Shared'), (Join-Path $projectDir 'Client/Assets'), (Join-Path $projectDir 'Server/App') | Out-Null
        '<Project />' | Set-Content (Join-Path $projectDir 'Shared/Shared.csproj')
        $serverPath = Join-Path $projectDir 'Server/App/Server.App.csproj'
        '<Project><ItemGroup><PackageReference Include="Lakona.Game.Server" Version="9.9.9" /></ItemGroup></Project>' | Set-Content $serverPath
        $isSdk = $engine -eq 'godot'
        $manifestPath = Join-Path $projectDir $(if ($isSdk) { 'Client/Client.csproj' } else { 'Client/Assets/packages.config' })
        $original = if ($isSdk) {
            @'
<Project Sdk="Godot.NET.Sdk/4.6.1"><ItemGroup>
  <PackageReference Include="Lakona.Game.Client" Version="1.2.3" />
  <PackageReference Include="Lakona.Rpc.Transport.WebSocket"><Version>2.3.4</Version></PackageReference>
  <PackageReference Include="Lakona.Rpc.Serializer.MemoryPack" Version="3.4.5-preview.1" />
</ItemGroup></Project>
'@
        } else {
            @'
<packages>
  <package id="Lakona.Game.Client" version="1.2.3" targetFramework="netstandard2.1" />
  <package id="Lakona.Rpc.Transport.WebSocket" version="2.3.4" />
  <package id="Lakona.Rpc.Serializer.MemoryPack" version="3.4.5-preview.1" />
</packages>
'@
        }
        $original | Set-Content $manifestPath
        $arguments = @{ ProjectDir=$projectDir; Engine=$engine; Transport='websocket'; Serializer='memorypack'; Port=20000; Feed='NuGetOrg'; RepoRoot=$repoRoot }
        foreach ($serverHasConflictingClient in $false, $true) {
            if ($serverHasConflictingClient) {
                # All server versions differ. No client package version may come from here.
                '<Project><ItemGroup><PackageReference Include="Lakona.Game.Client" Version="9.9.9" /><PackageReference Include="Lakona.Rpc.Transport.WebSocket" Version="9.9.9" /><PackageReference Include="Lakona.Rpc.Serializer.MemoryPack" Version="9.9.9" /></ItemGroup></Project>' | Set-Content $serverPath
            }
            $outputDir = New-E2EClient @arguments
            [xml]$generated = Get-Content (Join-Path $outputDir 'E2EVerification.csproj') -Raw
            $expected = @{ 'Lakona.Game.Client'='1.2.3'; 'Lakona.Rpc.Transport.WebSocket'='2.3.4'; 'Lakona.Rpc.Serializer.MemoryPack'='3.4.5-preview.1' }
            foreach ($id in $expected.Keys) {
                $reference = $generated.SelectSingleNode("/Project/ItemGroup/PackageReference[@Include='$id']")
                Assert-Equal $expected[$id] $reference.GetAttribute('Version')
            }
            [xml]$config = Get-Content (Join-Path $outputDir 'NuGet.config') -Raw
            Assert-Equal 'https://api.nuget.org/v3/index.json' $config.configuration.packageSources.add.value
        }

        $read = { Get-ClientPackageVersion -ProjectDir $projectDir -Engine $engine -PackageId 'Lakona.Game.Client' }
        $closing = if ($isSdk) { '</ItemGroup>' } else { '</packages>' }
        $duplicate = if ($isSdk) { '<PackageReference Include="lakona.game.client" Version="1.2.3" />' } else { '<package id="lakona.game.client" version="1.2.3" />' }
        $original.Replace($closing, $duplicate + $closing) | Set-Content $manifestPath
        Assert-Equal '1.2.3' (& $read)
        $original.Replace($closing, $duplicate.Replace('1.2.3', '4.5.6') + $closing) | Set-Content $manifestPath
        Assert-DependencyError $read 'conflicting versions' $manifestPath
        $original.Replace('Lakona.Game.Client', 'Other.Package') | Set-Content $manifestPath
        Assert-DependencyError { New-E2EClient @arguments } 'missing from' $manifestPath
        $original.Replace('1.2.3', '   ') | Set-Content $manifestPath
        Assert-DependencyError $read 'empty version' $manifestPath
        $original.Replace($(if ($isSdk) { 'Version="1.2.3"' } else { 'version="1.2.3"' }), '') | Set-Content $manifestPath
        Assert-DependencyError $read 'no version' $manifestPath
        foreach ($nonLiteral in '$(ClientVersion)', '1.*', '[1.0,2.0)') {
            $original.Replace('1.2.3', $nonLiteral) | Set-Content $manifestPath
            Assert-DependencyError $read 'literal version' $manifestPath
        }
        if ($isSdk) {
            $original.Replace('<Project ', '<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003" ') | Set-Content $manifestPath
            Assert-Equal '1.2.3' (& $read)
            $original.Replace('<PackageReference Include="Lakona.Game.Client" Version="1.2.3" />', '<PackageReference Include="Lakona.Game.Client" Version="1.2.3"><Version>2.0.0</Version></PackageReference>') | Set-Content $manifestPath
            Assert-DependencyError $read 'conflicting versions' $manifestPath
        }
        Remove-Item -LiteralPath $manifestPath
        Assert-DependencyError $read 'file missing' $manifestPath
        Write-Host "$engine client dependency cases: PASS"
    }
    Write-Host 'E2E client dependency contract: PASS'
}
finally {
    # Delete only this test's unique, verified temporary directory.
    $resolved = [System.IO.Path]::GetFullPath($tempRoot)
    $parent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($parent, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe fixture cleanup: $resolved" }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
