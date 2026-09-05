param(
    [string] $PackageRoot = ".",
    [string] $UiPackageRoot = ""
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path -LiteralPath $PackageRoot).Path
if ([string]::IsNullOrWhiteSpace($UiPackageRoot)) {
    $UiPackageRoot = Join-Path (Split-Path -Parent $root) "com.wolfyvr.threadlight.ui"
}
$uiRoot = (Resolve-Path -LiteralPath $UiPackageRoot).Path
$manifest = Get-Content -LiteralPath (Join-Path $uiRoot "package.json") -Raw | ConvertFrom-Json
if ($manifest.name -ne "com.wolfyvr.threadlight.ui" -or $manifest.version -notmatch '^1\.\d+\.\d+$') {
    throw "The bundle requires a compatible ThreadLight UI 1.x package."
}
$entries = @("package.json", "package.json.meta", "README.md", "README.md.meta",
    "LICENSE.md", "LICENSE.md.meta", "Editor.meta",
    "Threadlight Wordmark.png", "Threadlight Wordmark.png.meta",
    "Threadlight Compact Mark.png", "Threadlight Compact Mark.png.meta")
$editorRoot = Join-Path $uiRoot "Editor"
$entries += @(Get-ChildItem -LiteralPath $editorRoot -Recurse -File | ForEach-Object {
    $_.FullName.Substring($uiRoot.Length + 1).Replace('\', '/')
})
$output = Join-Path $root "Distribution~/ThreadlightUI.bytes"
Add-Type -AssemblyName System.IO.Compression
$buffer = [System.IO.MemoryStream]::new()
$archive = [System.IO.Compression.ZipArchive]::new($buffer, [System.IO.Compression.ZipArchiveMode]::Create, $true)
try {
    foreach ($relative in ($entries | Sort-Object -Unique)) {
        $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $input = [System.IO.File]::OpenRead((Join-Path $uiRoot $relative))
        $target = $entry.Open()
        try { $input.CopyTo($target) }
        finally { $target.Dispose(); $input.Dispose() }
    }
}
finally { $archive.Dispose() }
try { [System.IO.File]::WriteAllBytes($output, $buffer.ToArray()) }
finally { $buffer.Dispose() }
Write-Host "Generated ThreadLight UI $($manifest.version) snapshot: $output"
Get-FileHash -LiteralPath $output -Algorithm SHA256
