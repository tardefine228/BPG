param()
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$taskProjectPath = Join-Path $taskRoot 'Assembly-CSharp.csproj'
if (!(Test-Path -LiteralPath $taskProjectPath)) {
    throw 'Сначала Unity должна сгенерировать Assembly-CSharp.csproj для этого проекта.'
}
$taskProject = [System.Xml.XmlDocument]::new()
$taskXml = Get-Content -LiteralPath $taskProjectPath -Raw
# Unity может записать неэкранированный амперсанд в пути Cats&Guns.
$taskXml = [regex]::Replace($taskXml, '&(?!amp;|lt;|gt;|quot;|apos;|#\d+;|#x[0-9a-fA-F]+;)', '&amp;')
$taskProject.LoadXml($taskXml)
$taskSdk = Get-ChildItem -LiteralPath 'C:/Program Files/dotnet/sdk' -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$taskCompiler = Join-Path $taskSdk.FullName 'Roslyn/bincore/csc.dll'
$taskTemp = Join-Path ([System.IO.Path]::GetTempPath()) ('BPG_ComicFX_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTemp | Out-Null
$taskResponse = Join-Path $taskTemp 'compile.rsp'
$taskOutput = Join-Path $taskTemp 'BPG_GameScripts.dll'
$taskArguments = [System.Collections.Generic.List[string]]::new()
foreach ($taskFlag in @('/nostdlib+', '/target:library', '/langversion:9.0', '/utf8output', '/warn:4', '/nowarn:0649')) {
    $taskArguments.Add($taskFlag)
}
$taskArguments.Add('/out:"' + $taskOutput + '"')
$taskArguments.Add('/define:ENABLE_INPUT_SYSTEM;UNITY_6000_0_OR_NEWER')
foreach ($taskReference in $taskProject.Project.ItemGroup.Reference) {
    if (!$taskReference.HintPath) { continue }
    $taskReferencePath = [string]$taskReference.HintPath
    if (![System.IO.Path]::IsPathRooted($taskReferencePath)) {
        $taskReferencePath = Join-Path $taskRoot $taskReferencePath
    }
    if (Test-Path -LiteralPath $taskReferencePath) {
        $taskArguments.Add('/reference:"' + $taskReferencePath + '"')
    }
}
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Assets/Scripts') -Filter '*.cs' -Recurse | ForEach-Object {
    $taskArguments.Add('"' + $_.FullName + '"')
}
[System.IO.File]::WriteAllLines($taskResponse, $taskArguments, [System.Text.UTF8Encoding]::new($false))
& dotnet $taskCompiler /noconfig ('@' + $taskResponse)
if ($LASTEXITCODE -ne 0) { throw ('Проверка компиляции не прошла. Ответ компилятору: ' + $taskResponse) }
Write-Output 'Текущие игровые скрипты Assets/Scripts скомпилированы без ошибок.'
Write-Output ('Временная сборка: ' + $taskOutput)
