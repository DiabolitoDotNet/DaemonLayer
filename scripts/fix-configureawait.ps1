# Script to add .ConfigureAwait(false) to all await statements in .cs files
# This fixes CA2007 warnings across the codebase

$files = Get-ChildItem -Path "src" -Filter "*.cs" -Recurse
$totalFixed = 0

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $original = $content

    # Pattern: await <expression>; or await <expression>)
    # Add ConfigureAwait(false) before semicolon or closing paren if not already present
    $content = $content -replace '(await\s+[^;)]+?)(\s*[;)])', '$1.ConfigureAwait(false)$2'

    # Remove double ConfigureAwait if already exists
    $content = $content -replace '\.ConfigureAwait\(false\)\.ConfigureAwait\(false\)', '.ConfigureAwait(false)'

    if ($content -ne $original) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        $totalFixed++
        Write-Host "Fixed: $($file.FullName)" -ForegroundColor Green
    }
}

Write-Host "`nTotal files fixed: $totalFixed" -ForegroundColor Cyan
