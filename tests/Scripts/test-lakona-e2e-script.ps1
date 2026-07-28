#Requires -Version 5.1

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "../..")
$target = Join-Path $repoRoot ".agents/skills/lakona-e2e-testing/scripts/run-e2e.ps1"
$content = Get-Content -Raw -LiteralPath $target

$functionMatch = [regex]::Match(
    $content,
    '(?s)function Set-GeneratedServerPort \{.*?(?=\r?\nfunction Test-PortAvailable)')

if (-not $functionMatch.Success) {
    throw "Could not find Set-GeneratedServerPort in $target"
}

Invoke-Expression $functionMatch.Value

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lakona-e2e-script-test-" + [guid]::NewGuid().ToString("N"))
$appDir = Join-Path $tempRoot "Server/App"
$appSettings = Join-Path $appDir "appsettings.json"

try {
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    @'
{
  "Lakona": {
    "Management": {
      "Http": {
        "Port": 20080
      }
    },
    "Endpoints": [
      {
        "Transport": "websocket",
        "Port": 20000
      }
    ]
  }
}
'@ | Set-Content -LiteralPath $appSettings -Encoding UTF8

    Set-GeneratedServerPort -ProjectDir $tempRoot -Port 20137

    $config = Get-Content -Raw -LiteralPath $appSettings | ConvertFrom-Json
    if ($config.Lakona.Management.Http.Port -ne 20080) {
        throw "Management HTTP port must remain unchanged."
    }

    if ($config.Lakona.Endpoints[0].Port -ne 20137) {
        throw "RPC endpoint port was not updated."
    }

    if ($content -notmatch '\$serverText -match "Lakona server started successfully"') {
        throw "Server readiness must recognize the transport-neutral Lakona startup signal."
    }

    Write-Host "Lakona scaffold E2E script contract: PASS"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
