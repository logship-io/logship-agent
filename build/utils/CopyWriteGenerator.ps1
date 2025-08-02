param (
    [CmdletBinding()]
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory
)

function Get-CopyrightText {
    param (
        [string]$fileName
    )
    return @"
// <copyright file="{fileName}" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

"@.Replace("{fileName}", $fileName)
}

function Generate-CopyrightFiles {
    param (
        [CmdletBinding()]
        [Parameter(Mandatory = $true)]
        [string]$directory
    )

    $files = Get-ChildItem -Path $directory -Filter "*.cs" -Recurse

    foreach ($file in $files) {
        $fileName = $file.Name

        Write-Verbose "Processing file: $fileName"

        # Check if the file already contains a copyright comment
        $content = Get-Content -Path $file.FullName -Raw
        if ($content -match "// <copyright") {
            Write-Verbose "Skipping file: $fileName (already contains copyright)"
            continue
        }

        Write-Information "Writing file: $fileName"
        $copyrightText = Get-CopyrightText -fileName $fileName
        Set-Content -Path $file.FullName -Value ($copyrightText + "`n" + $content) -Force
    }
}

# Main script execution

Generate-CopyrightFiles -directory $InputDirectory
