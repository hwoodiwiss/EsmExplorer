#Requires -Version 7.0
<#
.SYNOPSIS
Creates a SHA-256 manifest from original files or an independent extraction.
.DESCRIPTION
Preserve archive-relative directories beneath OriginalFilesRoot. Do not use this
parser's extraction output to establish expected results. OutputPath must be a new
file outside the input tree, in an existing directory.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OriginalFilesRoot,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($OriginalFilesRoot)
$target = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Original-files directory does not exist: $root"
}
if (-not (Test-Path -LiteralPath ([System.IO.Path]::GetDirectoryName($target)) -PathType Container)) {
    throw 'The manifest output directory must already exist.'
}
if (Test-Path -LiteralPath $target) {
    throw "Output already exists: $target"
}
$relative = [System.IO.Path]::GetRelativePath($root, $target)
$parentPrefix = '..' + [System.IO.Path]::DirectorySeparatorChar
if (-not [System.IO.Path]::IsPathRooted($relative) -and
    $relative -ne '..' -and -not $relative.StartsWith($parentPrefix, [System.StringComparison]::Ordinal)) {
    throw 'Place the manifest outside the original-files directory.'
}

$lines = [System.Collections.Generic.List[string]]::new()
$paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Read-OriginalDirectory {
    param([string]$Directory)

    if (([System.IO.File]::GetAttributes($Directory) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Symbolic links/reparse points are not fixture originals: $Directory"
    }
    [string[]]$entries = [System.IO.Directory]::GetFileSystemEntries($Directory)
    [System.Array]::Sort($entries, [System.StringComparer]::Ordinal)
    foreach ($entry in $entries) {
        $attributes = [System.IO.File]::GetAttributes($entry)
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Symbolic links/reparse points are not fixture originals: $entry"
        }
        if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
            Read-OriginalDirectory -Directory $entry
        }
        else {
            $archivePath = [System.IO.Path]::GetRelativePath($root, $entry).Replace([System.IO.Path]::DirectorySeparatorChar, '/')
            if ($archivePath -match '[\t\r\n\\]' -or -not $paths.Add($archivePath)) {
                throw "Ambiguous manifest path: $archivePath"
            }
            $stream = [System.IO.File]::OpenRead($entry)
            $hasher = [System.Security.Cryptography.SHA256]::Create()
            try {
                # ComputeHash(Stream) bounds memory independently of input file size.
                $hash = [System.BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '')
                $lines.Add("$hash`t$archivePath")
            }
            finally {
                $hasher.Dispose()
                $stream.Dispose()
            }
        }
    }
}

Read-OriginalDirectory -Directory $root
if ($lines.Count -eq 0) {
    throw 'No original files found.'
}

# CreateNew prevents overwriting even if another process creates the path after validation.
$output = [System.IO.FileStream]::new($target, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
try {
    $writer = [System.IO.StreamWriter]::new($output, [System.Text.UTF8Encoding]::new($false))
    try {
        $writer.NewLine = "`n"
        foreach ($line in $lines) {
            $writer.WriteLine($line)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $output.Dispose()
}
Write-Output "Created $target with $($lines.Count) independently sourced file hashes."
