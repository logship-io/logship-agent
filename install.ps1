#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Config,
    [switch] $Start
)

$ErrorActionPreference = 'Stop'
$repository = 'https://github.com/logship-io/logship-agent'
$serviceName = 'logship.agent'
$configDir = Join-Path $env:ProgramData 'Logship Agent'
$programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$binDir = Join-Path $programFiles 'Logship Agent'
$settings = Join-Path $configDir 'appsettings.json'
$binary = Join-Path $binDir 'Logship.Agent.ConsoleHost.exe'
$serviceCommand = '"{0}" --contentRoot "{1}"' -f $binary, $configDir

function Set-PrivateDirectory([string] $Path, [System.Security.AccessControl.FileSystemRights] $ServiceAccess) {
    $directory = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($directory -and (!$directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint))) {
        throw "Expected a regular directory at $Path"
    }
    $administrators = [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    if ($directory) {
        $owner = (Get-Acl -LiteralPath $Path).GetOwner([System.Security.Principal.SecurityIdentifier])
        $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
        if ($owner -ne $administrators -and $owner.Value -ne 'S-1-5-18' -and $owner -ne $currentUser) {
            throw "Unexpected owner of $Path"
        }
    } else {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($administrators)
    foreach ($sid in 'S-1-5-32-544', 'S-1-5-18', 'S-1-5-19') {
        $identity = [System.Security.Principal.SecurityIdentifier]::new($sid)
        $rights = if ($sid -eq 'S-1-5-19') { $ServiceAccess } else { 'FullControl' }
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

foreach ($managedPath in $binDir, $binary, $settings) {
    $item = Get-Item -LiteralPath $managedPath -Force -ErrorAction SilentlyContinue
    if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected a regular installation path at $managedPath"
    }
}

$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$runtime = switch ($architecture) {
    'AMD64' { 'win-x64' }
    'ARM64' { 'win-arm64' }
    default { throw "Unsupported architecture: $architecture. Use Windows x64 or arm64." }
}
if ($Config) {
    $source = Get-Item -LiteralPath $Config
    if ($source.PSIsContainer) { throw 'Specify a configuration file.' }
    $Config = $source.FullName
}
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}
if (!$Version) {
    $Version = (Invoke-RestMethod 'https://api.github.com/repos/logship-io/logship-agent/releases/latest').tag_name
}
if ($Version -cnotmatch '^v[0-9][a-zA-Z0-9._-]*$') { throw "Invalid release tag: $Version" }

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($service -and ($service.PathName -ne $serviceCommand -or !(Test-Path -LiteralPath $binary -PathType Leaf))) {
    throw "The existing $serviceName service is outside this installer's layout."
}
if ($service -and $service.State -notin 'Running', 'Stopped') {
    throw "Wait for $serviceName to reach Running or Stopped, then retry."
}
$wasRunning = $service -and $service.State -eq 'Running'
if ($wasRunning -and $service.StartMode -eq 'Disabled') {
    throw "Stop the disabled $serviceName service before upgrading it."
}
$startRequested = $Start -or (!$service -and $Config)
$archiveName = "LogshipAgent-$runtime.zip"
$stage = Join-Path $binDir ('.install-' + [guid]::NewGuid().ToString('N'))
$configStage = Join-Path $configDir ('.install-' + [guid]::NewGuid().ToString('N'))
$stagedExe = Join-Path $stage 'Logship.Agent.ConsoleHost.exe'
$stagedConfig = Join-Path $configStage 'appsettings.json'
$previousExe = Join-Path $stage 'previous-agent.exe'
$previousConfig = Join-Path $configStage 'previous-settings.json'
$binaryChanged = $configChanged = $serviceCreated = $startupChanged = $serviceTouched = $false
$rollbackFailed = $false

try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $archive = Join-Path $stage $archiveName
    $assetUrl = "$repository/releases/download/$Version/$archiveName"
    Invoke-WebRequest -UseBasicParsing -Uri $assetUrl -OutFile $archive
    Invoke-WebRequest -UseBasicParsing -Uri "$assetUrl.sha256" -OutFile "$archive.sha256"
    $expected = ((Get-Content -LiteralPath "$archive.sha256" -TotalCount 1).Trim() -split '\s+')[0]
    if ($expected -notmatch '^[0-9a-f]{64}$' -or (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne $expected) {
        throw 'Release archive checksum verification failed.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $stage

    Set-PrivateDirectory $configDir ReadAndExecute
    Set-PrivateDirectory (Join-Path $configDir 'logship-agent-local-storage') Modify
    New-Item -ItemType Directory -Path $configStage | Out-Null
    $configSource = if ($Config) { $Config } elseif (Test-Path -LiteralPath $settings) { $settings } else { Join-Path $stage 'appsettings.json' }
    Copy-Item -LiteralPath $configSource -Destination $stagedConfig
    $validateArgs = @('--contentRoot', $configStage, '--validate-config=true')
    if (!$startRequested -and !$wasRunning) {
        $validateArgs += '--Output:endpoint=http://localhost:5000', '--Output:account=00000000-0000-0000-0000-000000000001'
    }
    & $stagedExe @validateArgs
    if ($LASTEXITCODE -ne 0) { throw 'The staged agent could not validate the configuration.' }

    if ($wasRunning) {
        $serviceTouched = $true
        Stop-Service -Name $serviceName
    }
    if (Test-Path -LiteralPath $binary) {
        [IO.File]::Replace($stagedExe, $binary, $previousExe)
    } else {
        [IO.File]::Move($stagedExe, $binary)
    }
    $binaryChanged = $true
    if ($Config -or !(Test-Path -LiteralPath $settings)) {
        if (Test-Path -LiteralPath $settings) {
            [IO.File]::Replace($stagedConfig, $settings, $previousConfig)
        } else {
            [IO.File]::Move($stagedConfig, $settings)
        }
        $configChanged = $true
    }
    if (!$service) {
        New-Service -Name $serviceName -BinaryPathName $serviceCommand -DisplayName 'Logship Agent' -StartupType Manual | Out-Null
        $serviceCreated = $true
        & sc.exe config $serviceName obj= 'NT AUTHORITY\LocalService'
        if ($LASTEXITCODE -ne 0) { throw 'Could not set the Logship service account.' }
    }
    if (![Diagnostics.EventLog]::SourceExists('Logship.Agent.ConsoleHost')) {
        [Diagnostics.EventLog]::CreateEventSource('Logship.Agent.ConsoleHost', 'Application')
    }
    if ($startRequested -and (!$service -or $service.StartMode -ne 'Auto')) {
        Set-Service -Name $serviceName -StartupType Automatic
        $startupChanged = $true
    }
    if ($startRequested -or $wasRunning) {
        $serviceTouched = $true
        Start-Service -Name $serviceName
    }
} catch {
    $installationError = $_
    try {
        if ($serviceTouched -and (Get-Service -Name $serviceName).Status -ne 'Stopped') {
            Stop-Service -Name $serviceName
        }
        if ($binaryChanged) {
            if (Test-Path -LiteralPath $previousExe) { Move-Item -Force -LiteralPath $previousExe -Destination $binary }
            else { Remove-Item -LiteralPath $binary }
        }
        if ($configChanged) {
            if (Test-Path -LiteralPath $previousConfig) { Move-Item -Force -LiteralPath $previousConfig -Destination $settings }
            else { Remove-Item -LiteralPath $settings }
        }
        if ($serviceCreated) {
            & sc.exe delete $serviceName
            if ($LASTEXITCODE -ne 0) { throw 'Could not remove the newly registered service.' }
        } elseif ($startupChanged) {
            Set-Service -Name $serviceName -StartupType $service.StartMode
        }
        if ($wasRunning -and (Get-Service -Name $serviceName).Status -ne 'Running') { Start-Service -Name $serviceName }
    } catch {
        $rollbackFailed = $true
        Write-Warning "Rollback failed. Staged files are in '$stage' and '$configStage'. $($_.Exception.Message)"
    }
    throw $installationError
} finally {
    if (!$rollbackFailed) {
        foreach ($directory in $stage, $configStage) {
            if (Test-Path -LiteralPath $directory -PathType Container) {
                Remove-Item -LiteralPath $directory -Recurse -Force
            }
        }
    }
}

Write-Output "Installed Logship Agent $Version ($runtime). Configuration: $settings"
if (!$startRequested -and !$wasRunning) {
    Write-Output 'Set the backend and enrollment settings in the configuration, then rerun the installer with -Start.'
}
