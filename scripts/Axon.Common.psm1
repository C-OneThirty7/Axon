Set-StrictMode -Version Latest

function Test-AxonBundleChecksums {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$BundleRoot,
        [Parameter(Mandatory)][string]$ManifestPath,
        [ValidateSet("Strict", "Warn", "Skip")][string]$Mode = "Strict"
    )

    if ($Mode -eq "Skip") {
        Write-Warning "Axon bundle checksum validation was explicitly skipped."
        return $true
    }

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        $message = "Axon checksum manifest is missing: $ManifestPath"
        if ($Mode -eq "Strict") { throw $message }
        Write-Warning $message
        return $false
    }

    $resolvedRoot = [IO.Path]::GetFullPath($BundleRoot)
    $bundlePrefix = $resolvedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $problems = [Collections.Generic.List[string]]::new()
    $checked = 0

    foreach ($rawLine in Get-Content -LiteralPath $ManifestPath) {
        $line = ([string]$rawLine).TrimStart([char]0xFEFF).Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) { continue }
        if ($line -notmatch '^([a-fA-F0-9]{64})\s+\*?(.+?)\s*$') {
            $problems.Add("Invalid manifest line: $line")
            continue
        }

        $expected = $Matches[1].ToUpperInvariant()
        $relative = $Matches[2] -replace '[\\/]', [string][IO.Path]::DirectorySeparatorChar
        $target = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $relative))
        if (-not $target.StartsWith($bundlePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $problems.Add("Manifest target escapes the bundle: $relative")
            continue
        }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
            $problems.Add("Missing payload: $relative")
            continue
        }

        $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($actual -ne $expected) {
            $problems.Add("Hash mismatch: $relative")
            continue
        }
        $checked++
    }

    if ($checked -eq 0 -and $problems.Count -eq 0) {
        $problems.Add("The checksum manifest contains no payload entries.")
    }
    if ($problems.Count -gt 0) {
        $message = "Checksum validation found $($problems.Count) problem(s):`n - " + ($problems -join "`n - ")
        if ($Mode -eq "Strict") { throw $message }
        Write-Warning $message
        return $false
    }

    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $installerPaths = @(
            (Join-Path $resolvedRoot "installers\Docker Desktop Installer.exe")
        )
        $installerPaths += @(Get-ChildItem -LiteralPath (Join-Path $resolvedRoot "installers") `
            -Filter "*x64*.msi" -File -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName)
        foreach ($installerPath in $installerPaths | Select-Object -Unique) {
            if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { continue }
            $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
            if ($signature.Status -in @("HashMismatch", "NotSigned")) {
                $message = "Installer signature validation failed for $(Split-Path -Leaf $installerPath): $($signature.Status)"
                if ($Mode -eq "Strict") { throw $message }
                Write-Warning $message
            } elseif ($signature.Status -ne "Valid") {
                Write-Warning "Windows could not fully validate the installer signature for $(Split-Path -Leaf $installerPath): $($signature.Status). The SHA-256 payload check passed."
            }
        }
    }

    Write-Host "Axon bundle checksum validation passed for $checked files."
    return $true
}

Export-ModuleMember -Function Test-AxonBundleChecksums
