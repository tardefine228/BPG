param([string]$WarmProject)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$taskVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $taskRoot 'ProjectSettings/ProjectVersion.txt') -Raw), 'm_EditorVersion: (\S+)').Groups[1].Value
$taskUnity = Join-Path 'C:/Program Files/Unity/Hub/Editor' "$taskVersion/Editor/Unity.exe"
if (!(Test-Path -LiteralPath $taskUnity)) { throw 'Unity.exe not found.' }
$taskTempRoot = Join-Path ([IO.Path]::GetTempPath()) 'BPGChecks'
if ($WarmProject) {
    $taskProject = (Resolve-Path -LiteralPath $WarmProject).Path
    if (!$taskProject.StartsWith(([IO.Path]::GetFullPath($taskTempRoot).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Warm project must be an isolated copy inside Temp/BPGChecks.'
    }
    $taskExisting = Get-Content -LiteralPath (Join-Path $taskProject 'ProjectSettings/ProjectSettings.asset') -Raw
    if ($taskExisting -notmatch '(?m)^  companyName: BPGChecks\r?$') { throw 'Not an isolated check project.' }
} else {
    $taskProject = Join-Path $taskTempRoot ('StartupMenu-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $taskProject -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Packages') -Destination (Join-Path $taskProject 'Packages') -Recurse
    $taskManifestPath = Join-Path $taskProject 'Packages/manifest.json'
    $taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
    $taskManifest.dependencies.PSObject.Properties.Remove('com.unity.ai.assistant')
    $taskManifest.dependencies.PSObject.Properties.Remove('com.unity.ai.inference')
    foreach ($taskDependency in $taskManifest.dependencies.PSObject.Properties) {
        $taskPackage = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Library/PackageCache') -Directory -Filter ($taskDependency.Name + '@*') | Select-Object -First 1
        if ($taskPackage) { $taskDependency.Value = 'file:' + $taskPackage.FullName.Replace('\', '/') }
    }
    [IO.File]::WriteAllText($taskManifestPath, ($taskManifest | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
}
foreach ($taskFolder in @('Assets', 'ProjectSettings')) {
    & robocopy (Join-Path $taskRoot $taskFolder) (Join-Path $taskProject $taskFolder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -gt 7) { throw ('Failed to copy ' + $taskFolder) }
}
$taskSettingsPath = Join-Path $taskProject 'ProjectSettings/ProjectSettings.asset'
$taskSettings = Get-Content -LiteralPath $taskSettingsPath -Raw
$taskSettings = [regex]::Replace($taskSettings, '(?m)^  companyName:.*$', '  companyName: BPGChecks')
$taskSettings = [regex]::Replace($taskSettings, '(?m)^  productName:.*$', '  productName: RefactoringValidation')
[IO.File]::WriteAllText($taskSettingsPath, $taskSettings, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StartupMenuChecks.cs') -Destination (Join-Path $taskProject 'Assets/StartupMenuCheckRunner.cs') -Force
$taskLog = Join-Path $taskProject 'startup-menu.log'
$taskArgs = @('-batchmode', '-projectPath', ('"' + $taskProject + '"'), '-executeMethod', 'StartupMenuChecks.Run',
    '-screen-width', '1920', '-screen-height', '1080', '-force-d3d11', '--burst-disable-compilation', '-logFile', ('"' + $taskLog + '"'))
Write-Output ('Checking Boot/MainMenu in: ' + $taskProject)
$taskProcess = Start-Process -FilePath $taskUnity -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -Wait
$taskMarker = Join-Path $taskProject 'startup-menu-checks.done'
if ($taskProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $taskMarker) -or (Get-Content -LiteralPath $taskMarker -Raw) -ne 'PASS') {
    throw ('Checks failed. See ' + $taskLog)
}
Get-Content -LiteralPath (Join-Path $taskProject 'startup-menu-checks.txt')
