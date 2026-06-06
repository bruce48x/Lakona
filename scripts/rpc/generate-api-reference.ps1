param(
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0",
    [string]$OutputPath = "blog/content/reference/api.md"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projects = @(
    @{
        Name = "Lakona.Rpc.Core"
        Xml = Join-Path $root "src/Lakona.Rpc.Core/bin/$Configuration/$Framework/Lakona.Rpc.Core.xml"
    },
    @{
        Name = "Lakona.Rpc.Client"
        Xml = Join-Path $root "src/Lakona.Rpc.Client/bin/$Configuration/$Framework/Lakona.Rpc.Client.xml"
    },
    @{
        Name = "Lakona.Rpc.Server"
        Xml = Join-Path $root "src/Lakona.Rpc.Server/bin/$Configuration/$Framework/Lakona.Rpc.Server.xml"
    }
)

function Normalize-DocText {
    param($Node)

    if ($null -eq $Node) {
        return ""
    }

    if ($Node -is [System.Xml.XmlNode]) {
        $text = $Node.InnerXml
    }
    else {
        $text = [string]$Node
    }
    $text = [regex]::Replace($text, '<see cref="([^"]+)"\s*/>', {
        param($match)
        return "``$(Format-Cref $match.Groups[1].Value)``"
    })
    $text = [regex]::Replace($text, '<see langword="([^"]+)"\s*/>', '`$1`')
    $text = [regex]::Replace($text, '<typeparamref name="([^"]+)"\s*/>', '`$1`')
    $text = [regex]::Replace($text, '<paramref name="([^"]+)"\s*/>', '`$1`')
    $text = [regex]::Replace($text, '<c>(.*?)</c>', '`$1`')
    $text = [regex]::Replace($text, '<[^>]+>', '')
    $text = [System.Net.WebUtility]::HtmlDecode($text)
    $text = [regex]::Replace($text, '\s+', ' ')
    return $text.Trim()
}

function Format-Cref {
    param([string]$Cref)

    if ([string]::IsNullOrWhiteSpace($Cref)) {
        return ""
    }

    $value = $Cref -replace '^[A-Z]:', ''
    $value = $value -replace '([A-Za-z0-9_])``\d+(?=\()', '$1'
    $value = $value -replace '([A-Za-z0-9_])``\d+$', '$1'
    $value = $value -replace '``(\d+)', 'T$1'
    $value = $value -replace '`(\d+)', ''
    $value = $value -replace '\{', '<'
    $value = $value -replace '\}', '>'
    return $value
}

function Get-MemberKind {
    param([string]$Name)

    switch ($Name.Substring(0, 2)) {
        "T:" { return "Type" }
        "M:" { return "Method" }
        "P:" { return "Property" }
        "E:" { return "Event" }
        "F:" { return "Field" }
        default { return "Member" }
    }
}

function Get-MemberDisplayName {
    param([string]$Name)

    $display = $Name.Substring(2)
    $display = $display -replace '#ctor', 'constructor'
    $display = $display -replace '([A-Za-z0-9_])``\d+(?=\()', '$1'
    $display = $display -replace '([A-Za-z0-9_])``\d+$', '$1'
    $display = $display -replace '``(\d+)', 'T$1'
    $display = $display -replace '`(\d+)', ''
    $display = $display -replace '\{', '<'
    $display = $display -replace '\}', '>'
    return $display
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("---")
$lines.Add("title: API Reference")
$lines.Add("date: 2026-05-11T00:00:00+08:00")
$lines.Add("---")
$lines.Add("")
$lines.Add("This page is generated from C# XML documentation comments. Update the source comments first, then rerun ``scripts/generate-api-reference.ps1``.")
$lines.Add("")

foreach ($project in $projects) {
    if (!(Test-Path $project.Xml)) {
        throw "Missing XML documentation file: $($project.Xml). Build the project first."
    }

    [xml]$doc = Get-Content -Raw $project.Xml
    $members = @($doc.doc.members.member) | Sort-Object { $_.name }

    $lines.Add("## $($project.Name)")
    $lines.Add("")

    foreach ($member in $members) {
        $summary = Normalize-DocText $member.summary
        $remarks = Normalize-DocText $member.remarks

        if ([string]::IsNullOrWhiteSpace($summary) -and [string]::IsNullOrWhiteSpace($remarks)) {
            continue
        }

        $kind = Get-MemberKind $member.name
        $display = Get-MemberDisplayName $member.name
        $lines.Add("### $kind ``$display``")
        $lines.Add("")

        if (![string]::IsNullOrWhiteSpace($summary)) {
            $lines.Add($summary)
            $lines.Add("")
        }

        if (![string]::IsNullOrWhiteSpace($remarks)) {
            $lines.Add("Remarks: $remarks")
            $lines.Add("")
        }

        $params = @($member.SelectNodes("param"))
        if ($params.Count -gt 0) {
            $lines.Add("Parameters:")
            foreach ($param in $params) {
                $paramName = $param.Attributes["name"].Value
                $paramText = Normalize-DocText $param
                $lines.Add("- ``$paramName``: $paramText")
            }
            $lines.Add("")
        }

        $typeParams = @($member.SelectNodes("typeparam"))
        if ($typeParams.Count -gt 0) {
            $lines.Add("Type parameters:")
            foreach ($typeParam in $typeParams) {
                $typeParamName = $typeParam.Attributes["name"].Value
                $typeParamText = Normalize-DocText $typeParam
                $lines.Add("- ``$typeParamName``: $typeParamText")
            }
            $lines.Add("")
        }

        $returns = Normalize-DocText $member.returns
        if (![string]::IsNullOrWhiteSpace($returns)) {
            $lines.Add("Returns: $returns")
            $lines.Add("")
        }

        $exceptions = @($member.SelectNodes("exception"))
        if ($exceptions.Count -gt 0) {
            $lines.Add("Exceptions:")
            foreach ($exception in $exceptions) {
                $cref = Format-Cref $exception.Attributes["cref"].Value
                $exceptionText = Normalize-DocText $exception
                $lines.Add("- ``$cref``: $exceptionText")
            }
            $lines.Add("")
        }
    }
}

$outFullPath = Join-Path $root $OutputPath
$outDir = Split-Path -Parent $outFullPath
if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

Set-Content -Path $outFullPath -Value ($lines -join "`n") -Encoding utf8
Write-Host "Generated $OutputPath"
