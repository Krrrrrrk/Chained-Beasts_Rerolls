[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Chained Beasts',
    [switch]$Package, [switch]$Deploy, [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$version = '0.1.1'
$nativeHash = 'ba3ef02fa87da4c41bb27d7610539c9578132204f0fb5713833e2ef93415ce29'
$names = @('ChainedBeasts.Rerolls.dll','ChainedBeasts.Rerolls.Core.dll')
$output = Join-Path $repoRoot 'src\ChainedBeasts.Rerolls\bin\Release\net6.0'
$interop = Join-Path $GameDir 'BepInEx\interop\Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $interop)) {throw 'BepInEx-generated runtime interop assemblies are required.'}
if ((Get-FileHash -LiteralPath (Join-Path $GameDir 'GameAssembly.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $nativeHash) {throw 'Unsupported native game build. Re-inspect before building/deploying.'}
function SourceHash {
    $files = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src'),(Join-Path $repoRoot 'tests') -Recurse -File |
        Where-Object {$_.Extension -in @('.cs','.csproj','.alpha','.props') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'})
    $files += Get-Item -LiteralPath $PSCommandPath
    $buffer = [IO.MemoryStream]::new(); $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($file in $files | Sort-Object FullName) {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $header = [Text.Encoding]::UTF8.GetBytes([IO.Path]::GetRelativePath($repoRoot,$file.FullName).Replace('\','/')+[char]0+$bytes.Length+[char]0)
            $buffer.Write($header,0,$header.Length);$buffer.Write($bytes,0,$bytes.Length)
        }
        $buffer.Position=0;return [Convert]::ToHexString($sha.ComputeHash($buffer)).ToLowerInvariant()
    } finally {$buffer.Dispose();$sha.Dispose()}
}
$artifact = Join-Path $repoRoot "artifacts\$version"
New-Item -ItemType Directory -Path $artifact -Force | Out-Null
if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot 'src\ChainedBeasts.Rerolls\ChainedBeasts.Rerolls.csproj') -c Release --nologo "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) {throw 'Plugin build failed.'}
    $core = @(& dotnet run --project (Join-Path $repoRoot 'tests\CoreChecks\CoreChecks.csproj') -c Release -- (Join-Path $output $names[0]) $interop 2>&1)
    Write-Output $core;if ($LASTEXITCODE -ne 0) {throw 'Core/binding/artifact checks failed.'}
    $integration = @(& dotnet run --project (Join-Path $repoRoot 'tests\IntegrationChecks\IntegrationChecks.csproj') -c Release 2>&1)
    Write-Output $integration;if ($LASTEXITCODE -ne 0) {throw 'Service/transport/store checks failed.'}
    $offers = @(& dotnet run --project (Join-Path $repoRoot 'tests\NativeOfferChecks\NativeOfferChecks.csproj') -c Release 2>&1)
    Write-Output $offers;if ($LASTEXITCODE -ne 0) {throw 'Native-offer adapter checks failed.'}
    $presentation = @(& dotnet run --project (Join-Path $repoRoot 'tests\PresentationChecks\PresentationChecks.csproj') -c Release -- (Join-Path $artifact 'layout') 2>&1)
    Write-Output $presentation;if ($LASTEXITCODE -ne 0) {throw 'Presentation checks failed.'}
}
$files = @($names | ForEach-Object {
    $path=Join-Path $output $_
    if ([Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString() -ne "$version.0") {throw 'Unexpected assembly version.'}
    [ordered]@{file=$_;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$recordPath=Join-Path $artifact 'build-checks.json';$sourceHash=SourceHash
if (-not $SkipBuild) {
    [ordered]@{version=$version;sourceSha256=$sourceHash;nativeSha256=$nativeHash;interopSha256=(Get-FileHash -LiteralPath $interop -Algorithm SHA256).Hash.ToLowerInvariant();
        runtimeVerified=$false;files=$files;core=$core;integration=$integration;offers=$offers;presentation=$presentation} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $recordPath -Encoding utf8
} elseif ($Package -or $Deploy) {
    if (-not(Test-Path -LiteralPath $recordPath)) {throw 'No successful check record. Build without -SkipBuild first.'}
    $record=Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if ($record.sourceSha256 -ne $sourceHash) {throw 'Source changed since checks; rebuild.'}
    foreach ($file in $files) {if(@($record.files | Where-Object {$_.file -eq $file.file -and $_.sha256 -eq $file.sha256}).Count -ne 1){throw 'Unchecked output; rebuild.'}}
}
if ($Package) {
    $stage=Join-Path $artifact ('stage-'+[Guid]::NewGuid().ToString('N'))
    $plugins=Join-Path $stage 'BepInEx\plugins\ChainedBeasts.Rerolls';New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    foreach ($file in $names) {Copy-Item -LiteralPath (Join-Path $output $file) -Destination $plugins}
    foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) {Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $stage}
    $files | ForEach-Object {"$($_.sha256)  BepInEx/plugins/ChainedBeasts.Rerolls/$($_.file)"} | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8
    [ordered]@{version=$version;game='1.0.2';steamBuild='26231777';nativeSha256=$nativeHash;runtimeVerified=$false;singlePlayerManualTested=($version -eq '0.1.1');multiplayerRuntimeTested=$false;files=$files} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'BUILD_INFO.json') -Encoding utf8
    $temporary=Join-Path $artifact ([Guid]::NewGuid().ToString('N')+'.zip');[IO.Compression.ZipFile]::CreateFromDirectory($stage,$temporary)
    $zip=Join-Path $artifact "ChainedBeasts.Rerolls-$version.zip";Move-Item -LiteralPath $temporary -Destination $zip -Force
    [ordered]@{version=$version;archive=(Split-Path $zip -Leaf);sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant();runtimeVerified=$false;singlePlayerManualTested=($version -eq '0.1.1');multiplayerRuntimeTested=$false;publishedRelease=$false;files=$files} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifact 'package.json') -Encoding utf8
    Write-Output "Package: $zip"
}
if ($Deploy) {
    if (Get-Process -Name ChainedBeasts -ErrorAction SilentlyContinue) {throw 'Game is running; nothing deployed. Close it first.'}
    if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'ChainedBeasts.exe'))) {throw 'Invalid game directory.'}
    $root=Join-Path $GameDir 'BepInEx\plugins';$target=Join-Path $root 'ChainedBeasts.Rerolls'
    foreach($dll in Get-ChildItem -LiteralPath $root -Filter '*.dll' -File -Recurse) {
        try {$id=[Reflection.AssemblyName]::GetAssemblyName($dll.FullName).Name+'.dll'}catch{continue}
        if($id -in $names -and $dll.FullName -ne (Join-Path $target $id)){throw "Duplicate rerolls DLL: $($dll.FullName)"}
    }
    $others=@(Get-ChildItem -LiteralPath $root -Filter '*.dll' -File -Recurse | Where-Object {-not $_.FullName.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)} |
        ForEach-Object {[ordered]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
    $backup=Join-Path $repoRoot ('backups\deploy-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'));New-Item -ItemType Directory -Path $backup | Out-Null
    if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'Plugin') -Recurse}
    $config=Join-Path $GameDir 'BepInEx\config\local.chainedbeasts.rerolls.cfg'
    if(Test-Path -LiteralPath $config){Copy-Item -LiteralPath $config -Destination $backup}
    $ledger=Join-Path $GameDir 'BepInEx\config\ChainedBeasts.Rerolls'
    if(Test-Path -LiteralPath $ledger){Copy-Item -LiteralPath $ledger -Destination (Join-Path $backup 'Ledger') -Recurse}
    $data=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Featherweight Games\ChainedBeasts'
    if(Test-Path -LiteralPath $data){Copy-Item -LiteralPath $data -Destination (Join-Path $backup 'UserData') -Recurse}
    if(Get-Process -Name ChainedBeasts -ErrorAction SilentlyContinue){throw 'Game started during backup; nothing deployed.'}
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach($file in $files){Copy-Item -LiteralPath (Join-Path $output $file.file) -Destination $target -Force;if((Get-FileHash -LiteralPath (Join-Path $target $file.file) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw 'Deployment hash mismatch.'}}
    foreach($file in $others){if((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -ne $file.sha256){throw 'Another plugin changed during deployment.'}}
    [ordered]@{version=$version;installed=$target;backup=$backup;runtimeVerified=$false;otherPluginsUnchanged=$true;files=$files} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifact 'deployment.json') -Encoding utf8
    Write-Output "Deployed rerolls only. Rollback: $backup"
}
