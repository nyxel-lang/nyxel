#requires -Version 7
<#
.SYNOPSIS
  Create a new Architecture Decision Record from docs/decisions/template.md.
.EXAMPLE
  ./tools/new-adr.ps1 "job-system-design"
#>
param([Parameter(Mandatory)][string]$Slug)

$dir = Join-Path $PSScriptRoot "../docs/decisions"
# -Filter goes to the Win32 API, which only understands * and ? -- character classes there match nothing and
# every ADR would be numbered 0001. Filter in PowerShell instead.
$existing = Get-ChildItem $dir -Filter "*.md" | Where-Object { $_.Name -match '^\d{4}-' } | Sort-Object Name
$next = if ($existing) { [int]($existing[-1].Name.Substring(0, 4)) + 1 } else { 1 }
$name = "{0:D4}-{1}.md" -f $next, $Slug
$target = Join-Path $dir $name
(Get-Content (Join-Path $dir "template.md") -Raw) `
    -replace "ADR-XXXX", ("ADR-{0:D4}" -f $next) `
    -replace "YYYY-MM-DD", (Get-Date -Format "yyyy-MM-dd") |
    Set-Content $target -Encoding utf8NoBOM
Write-Host "Created $target"
