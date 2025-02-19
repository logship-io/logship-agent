param (
    [CmdletBinding()]
    [Parameter(Mandatory = $false)]$FilePath = "Packages.props",
    [Parameter(Mandatory = $false)]$Update = [UpdateType]::None,
    [Parameter(Mandatory = $false)]$Force = $false
)

<#
Define an enum parameter for update types
#>
enum UpdateType {
    None
    Update
    UpdataMinor
    UpdatePrerelease
    UpdatePrereleaseMinor
}

<#
Read the xml packages files
#>
$content = Get-Content $FilePath
$xml = [xml]$content

$itemgroup = $xml.project.ItemGroup
$orderedNodes = $itemgroup.PackageReference | Sort-Object -Property Update
#$orderedNodes
$itemgroup.RemoveAll()
foreach ($item in $orderedNodes) {
    $packageName = $item.Update
    $packageVersion = $item.Version

    if ($Update -eq [UpdateType]::None) {
        $itemgroup.AppendChild($item) > $null
        continue
    }

    if (([UpdateType]::UpdatePrerelease, [UpdateType]::UpdatePrereleaseMinor) -contains $Update) {
        $content = dotnet package search $packageName --exact-match --prerelease --format json | ConvertFrom-Json
    }
    else {
        $content = dotnet package search $packageName --exact-match --format json | ConvertFrom-Json
    }
    

    foreach ($result in $content.searchResult) {
        if ($result.sourceName -ne "nuget.org") {
            continue
        }

        $package = $result.packages | Select-Object -Last 1
        if (-not $package) {
            continue
        }

        if ($packageVersion -eq $package.version) {
            Write-Verbose "Package $packageName is up to date at version $packageVersion"
            continue
        }

        $oldVersionPrefix = [int]::Parse($packageVersion.Split('.')[0])
        $newVersionPrefix = [int]::Parse($package.version.Split('.')[0])

        $isMajorDowngrade = $newVersionPrefix -lt $oldVersionPrefix
        $isMajorUpgrade = $newVersionPrefix -gt $oldVersionPrefix

        if (-not $Force -and $isMajorDowngrade) {
            Write-Output "Major downgrade detected for package $packageName from $packageVersion to $($package.version). Skipping."
            continue;
        }

        if (-not $Force -and ($Update -eq [UpdateType]::UpdateMinor -or $Update -eq [UpdateType]::UpdatePrereleaseMinor) -and $isMajorUpgrade) {
            Write-Output "Major upgrade detected for package $packageName from $packageVersion to $($package.version). Skipping."
            continue
        }

        Write-Output "Updating package $packageName from $packageVersion to $($package.version)"
        $item.Version = $package.version
    }

    $itemgroup.AppendChild($item) > $null
}

<#
Write the xml file
#>
#$xml.OuterXml

$stringWriter = [System.IO.StringWriter]::new()
$xmlWriter = [System.Xml.XmlTextWriter]::new($stringWriter)
$xmlWriter.Formatting = "indented"
$xmlWriter.Indentation = 2
$xml.WriteContentTo($xmlWriter) > $null
$xmlWriter.Flush()
$stringWriter.Flush()
Set-Content $FilePath -Value $stringWriter.ToString()
