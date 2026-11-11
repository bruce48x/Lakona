# Deterministic source checks only; never execute documentation examples.
function Get-DocumentationParts {
    param([string]$Content)
    $text = [Collections.Generic.List[string]]::new()
    $blocks = [Collections.Generic.List[string]]::new()
    $block = [Collections.Generic.List[string]]::new()
    $fence = ''
    foreach ($line in ($Content -split "`n")) {
        if ($line -match '^ {0,3}(?<fence>`{3,}|~{3,})') {
            if (!$fence) { $fence = $Matches.fence; $block.Clear() }
            elseif ($Matches.fence[0] -eq $fence[0] -and $Matches.fence.Length -ge $fence.Length) {
                $blocks.Add(($block -join "`n").Trim())
                $fence = ''
            }
            $text.Add('')
        }
        elseif ($fence) { $block.Add($line.TrimEnd()); $text.Add('') }
        else { $text.Add($line) }
    }
    return @{ Text = $text -join "`n"; Blocks = $blocks }
}

function Get-DocumentationAnchors {
    param([string]$Text)
    $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $counts = @{}
    foreach ($heading in [regex]::Matches($Text, '(?m)^ {0,3}#{1,6}\s+(?<title>.+?)\s*#*\s*$')) {
        $title = $heading.Groups['title'].Value -replace '\[([^]]+)\]\([^)]*\)', '$1' -replace '<[^>]+>', ''
        $slug = [Net.WebUtility]::HtmlDecode($title).ToLowerInvariant() -replace '[^\p{L}\p{N}_\-\s]', '' -replace '\s', '-'
        $number = if ($counts.ContainsKey($slug)) { $counts[$slug] + 1 } else { 0 }
        $counts[$slug] = $number
        [void]$anchors.Add($(if ($number) { "$slug-$number" } else { $slug }))
    }
    foreach ($id in [regex]::Matches($Text, '(?i)\b(?:id|name)\s*=\s*["''](?<id>[^"'']+)["'']')) {
        [void]$anchors.Add($id.Groups['id'].Value)
    }
    return ,$anchors
}

function Test-DocumentationLinks {
    param([string]$Path, [string]$RepositoryRoot, [string]$Text)
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $Path).Replace('\', '/')
    # Inline links/images and reference definitions, outside fenced examples.
    $pattern = '\]\(\s*(?:<(?<target>[^>]+)>|(?<target>[^\s)]+))(?:\s+["''][^\r\n]*?["''])?\s*\)|(?m)^ {0,3}\[[^]]+\]:\s*(?:<(?<target>[^>]+)>|(?<target>\S+))'
    foreach ($link in [regex]::Matches($Text, $pattern)) {
        $target = $link.Groups['target'].Value
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:|^/') { continue }
        $parts = $target -split '#', 2
        $filePart = [uri]::UnescapeDataString(($parts[0] -split '\?', 2)[0])
        $resolved = if (!$filePart) { $Path } else { [IO.Path]::GetFullPath((Join-Path (Split-Path $Path) $filePart)) }
        if (!(Test-Path -LiteralPath $resolved) -and $relative.StartsWith('blog/content/') -and $filePart -notmatch '\.[a-zA-Z0-9]+$') {
            # Hugo pretty URLs resolve against the article URL, not its source directory.
            $articleUrl = 'https://docs.invalid/' + $relative.Substring('blog/content/'.Length) -replace '\.md$', '/'
            $route = [uri]::new([uri]$articleUrl, $filePart).AbsolutePath.Trim('/').TrimEnd('/')
            $contentPath = Join-Path $RepositoryRoot "blog/content/$route"
            foreach ($candidate in @("$contentPath.md", "$contentPath/_index.md", "$contentPath/index.md")) {
                if (Test-Path -LiteralPath $candidate) { $resolved = $candidate; break }
            }
        }
        if (!(Test-Path -LiteralPath $resolved)) {
            "$relative`: missing local link target '$target'"
            continue
        }
        if ($parts.Count -eq 2 -and $parts[1] -and [IO.Path]::GetExtension($resolved) -eq '.md') {
            $targetText = (Get-DocumentationParts (Get-Content -LiteralPath $resolved -Raw)).Text
            $anchors = Get-DocumentationAnchors $targetText
            if (!$anchors.Contains([uri]::UnescapeDataString($parts[1]))) { "$relative`: missing local anchor '$target'" }
        }
    }
}

function Test-DocumentationExamples {
    param([string]$Path, [string]$RepositoryRoot, $Blocks)
    $relative = [IO.Path]::GetRelativePath($RepositoryRoot, $Path).Replace('\', '/')
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($block in $Blocks) {
        if (@($block -split "`n" | Where-Object { $_.Trim() }).Count -ge 8 -and !$seen.Add($block)) {
            "$relative`: repeated identical code block (at least eight non-empty lines)"
        }
        foreach ($command in [regex]::Matches($block, '(?<![\w./\\-])(?<script>(?:\.[\\/])?(?:\.agents[\\/][\w./\\-]+[\\/])?scripts[\\/][\w./\\-]+\.ps1)(?:[ \t]+-Sample[ \t]+["'']?(?<sample>[\w.-]+)["'']?)?')) {
            $scriptPath = Join-Path $RepositoryRoot $command.Groups['script'].Value.Replace('\', '/')
            if (!(Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
                "$relative`: missing script in example '$($command.Groups['script'].Value)'"
                continue
            }
            if ($command.Groups['sample'].Success) {
                $tokens = $null; $errors = $null
                $ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
                $parameter = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Sample' }
                $validateSet = $parameter.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' }
                $values = @($validateSet.PositionalArguments | ForEach-Object { $_.Value })
                if (!$values.Count -or $command.Groups['sample'].Value -cnotin $values) {
                    "$relative`: unsupported -Sample '$($command.Groups['sample'].Value)' for '$($command.Groups['script'].Value)'"
                }
            }
        }
    }
}

function Test-DocumentationChangelog {
    param([string]$Content)
    $dates = [Collections.Generic.HashSet[string]]::new()
    $previous = '9999-12-31'
    foreach ($section in [regex]::Matches($Content, '(?ms)^## (?<heading>[^\r\n]+)\r?\n(?<body>.*?)(?=^## |\z)')) {
        $heading = $section.Groups['heading'].Value
        if ($heading -notmatch '^(?<date>\d{4}-\d{2}-\d{2}) — \S') { "CHANGELOG.md: invalid milestone heading '$heading'"; continue }
        $date = $Matches.date
        $parsedDate = [datetime]::MinValue
        if (![datetime]::TryParseExact($date, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedDate)) { "CHANGELOG.md: invalid date '$date'" }
        if (!$dates.Add($date)) { "CHANGELOG.md: duplicate milestone date '$date'" }
        if ($date -gt $previous) { "CHANGELOG.md: milestones must be newest first ($date)" }
        $previous = $date
        $body = $section.Groups['body'].Value
        $count = [regex]::Matches($body, '(?m)^- ').Count
        if ($count -lt 1 -or $count -gt 3) { "CHANGELOG.md: $date has $count bullets; expected one to three" }
        if ($body -match '(?m)^\*\*Key release:\*\*') { "CHANGELOG.md: $date must use 'Key releases'" }
        foreach ($release in [regex]::Matches($body, '(?ms)^\*\*Key releases:\*\*(?<list>.*?)(?=\r?\n\s*\r?\n|\z)')) {
            $list = $release.Groups['list'].Value
            $entries = [regex]::Matches($list, '`(?<entry>[^`]+)`')
            $rest = $list -replace '`[^`]+`', '' -replace '\band\b|[\s,.]', ''
            if (!$entries.Count -or $rest) { "CHANGELOG.md: $date must list explicit package IDs and versions in backticks" }
            foreach ($entry in $entries) {
                if ($entry.Groups['entry'].Value -cnotmatch '^Lakona(?:\.[A-Za-z0-9_-]+)+ \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
                    "CHANGELOG.md: $date has invalid package ID/version '$($entry.Groups['entry'].Value)'"
                }
            }
        }
    }
}
