param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference = 'Stop'
$releaseRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\','/')
$prefix = $releaseRoot + [System.IO.Path]::DirectorySeparatorChar
$checked = 0
$missing = @()
# Check inline links/images and reference definitions. Web URLs and in-page anchors
# need no packaged file; local targets are resolved relative to their Markdown file.
$patterns = @('\[[^\]]*\]\((?<target><[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)', '(?m)^\s*\[[^\]]+\]:\s*(?<target><[^>]+>|\S+)')
foreach ($document in Get-ChildItem -LiteralPath $releaseRoot -Filter '*.md' -Recurse -File) {
    $content = Get-Content -LiteralPath $document.FullName -Raw -Encoding UTF8
    $content = [regex]::Replace($content, '(?ms)^\s*(```|~~~).*?^\s*\1[^\r\n]*$', '')
    foreach ($pattern in $patterns) {
        foreach ($link in [regex]::Matches($content, $pattern)) {
            $target = $link.Groups['target'].Value.Trim('<','>')
            if ($target -match '^(?:[a-z][a-z0-9+.-]*:|//|#)') { continue }
            $target = [Uri]::UnescapeDataString(($target -split '[#?]', 2)[0])
            if (-not $target) { continue }
            $checked++
            $resolved = [System.IO.Path]::GetFullPath((Join-Path $document.DirectoryName $target))
            if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $resolved)) {
                $missing += $document.FullName.Substring($prefix.Length) + ': ' + $target
            }
        }
    }
}
if ($missing.Count) { throw ("发行文档含缺失或越界的本地链接：`n" + ($missing -join "`n")) }
Write-Output "PASS  Release Markdown links ($checked local targets)"
