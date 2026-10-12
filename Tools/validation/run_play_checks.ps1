param([string]$UnityPath, [switch]$PauseReturnOnly, [switch]$CatFlightOnly, [switch]$PresentationOnly, [switch]$LoadingOnly, [switch]$MinimalEditor, [switch]$CompactGameplayOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if (!$UnityPath) {
    $taskVersionText = Get-Content -LiteralPath (Join-Path $taskRoot 'ProjectSettings/ProjectVersion.txt') -Raw
    $taskVersion = [regex]::Match($taskVersionText, 'm_EditorVersion: (\S+)').Groups[1].Value
    $UnityPath = Join-Path 'C:/Program Files/Unity/Hub/Editor' "$taskVersion/Editor/Unity.exe"
}
if (!(Test-Path -LiteralPath $UnityPath)) { throw 'Укажите путь к Unity.exe через -UnityPath.' }

# Уникальная копия проекта сохраняет исходные сцены, редактор и игровой прогресс пользователя.
$taskProject = Join-Path ([IO.Path]::GetTempPath()) ('BPGChecks/PlayChecks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskProject | Out-Null
foreach ($taskFolder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $taskFolder) -Destination (Join-Path $taskProject $taskFolder) -Recurse
}
$taskSettingsPath = Join-Path $taskProject 'ProjectSettings/ProjectSettings.asset'
$taskSettings = [IO.File]::ReadAllText($taskSettingsPath)
$taskSettings = [regex]::Replace($taskSettings, '(?m)^  companyName:.*$', '  companyName: BPGChecks')
$taskSettings = [regex]::Replace($taskSettings, '(?m)^  productName:.*$', '  productName: RefactoringValidation')
[IO.File]::WriteAllText($taskSettingsPath, $taskSettings, [Text.UTF8Encoding]::new($false))

# Уже загруженные пакеты берутся из локального кэша, чтобы не скачивать их повторно.
$taskManifestPath = Join-Path $taskProject 'Packages/manifest.json'
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
if ($MinimalEditor) {
    # Редакторский AI не нужен игровым проверкам и может исчерпать память при перезагрузке домена.
    $taskManifest.dependencies.PSObject.Properties.Remove('com.unity.ai.assistant')
    $taskManifest.dependencies.PSObject.Properties.Remove('com.unity.ai.inference')
}
$taskCache = Join-Path $taskRoot 'Library/PackageCache'
if (Test-Path -LiteralPath $taskCache) {
    foreach ($taskDependency in $taskManifest.dependencies.PSObject.Properties) {
        $taskPackage = Get-ChildItem -LiteralPath $taskCache -Directory -Filter ($taskDependency.Name + '@*') |
            Select-Object -First 1
        if ($taskPackage) { $taskDependency.Value = 'file:' + $taskPackage.FullName.Replace('\', '/') }
    }
}
[IO.File]::WriteAllText($taskManifestPath, ($taskManifest | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ValidationRunner.cs') -Destination (Join-Path $taskProject 'Assets/ValidationRunner.cs')
New-Item -ItemType Directory -Path (Join-Path $taskProject 'Assets/Editor') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PlayChecksBootstrap.cs') -Destination (Join-Path $taskProject 'Assets/Editor/PlayChecksBootstrap.cs')
$taskLog = Join-Path $taskProject 'unity.log'
Write-Output ('Проверки запускаются в отдельном проекте: ' + $taskProject)
$taskUnityArguments = @(
    '-batchmode', '-projectPath', ('"' + $taskProject + '"'),
    '-executeMethod', 'PlayChecksBootstrap.Run', '-screen-width', '1920', '-screen-height', '1080',
    '-logFile', ('"' + $taskLog + '"')
)
if ($MinimalEditor) { $taskUnityArguments += @('-force-d3d11', '--burst-disable-compilation') }
if ($PauseReturnOnly) { $taskUnityArguments += '-bpgPauseReturnOnly' }
if ($CatFlightOnly) { $taskUnityArguments += '-bpgCatFlightOnly' }
if ($PresentationOnly) { $taskUnityArguments += '-bpgPresentationOnly' }
if ($CompactGameplayOnly) { $taskUnityArguments += '-bpgCompactGameplayOnly' }
if ($LoadingOnly) { $taskUnityArguments += '-bpgLoadingOnly' }
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskUnityArguments -WindowStyle Hidden -Wait -PassThru
$taskMarker = Join-Path $taskProject 'play-checks.done'
if ($taskProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $taskMarker) -or
    (Get-Content -LiteralPath $taskMarker -Raw) -ne 'PASS') {
    throw ('Проверки не прошли. Подробности: ' + $taskLog)
}
Get-Content -LiteralPath (Join-Path $taskProject 'play-checks.txt')
Write-Output ('Рендеры интерфейса, замеры и журнал сохранены в ' + $taskProject)
