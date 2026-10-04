param([string]$Compiler='C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$artifacts=Join-Path $repo 'artifacts'
$publish=Join-Path $artifacts 'publish-0.1.6'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'
$env:DOTNET_CLI_HOME=Join-Path $artifacts 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:NUGET_PACKAGES=Join-Path $artifacts 'packages'
$env:APPDATA=Join-Path $artifacts 'appdata'
$project=Join-Path $repo 'src\StudyWhisper.App\StudyWhisper.App.csproj'
dotnet restore $project -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:NuGetAudit=false --configfile (Join-Path $repo 'NuGet.Config')
if($LASTEXITCODE -ne 0){throw 'Restore failed'}
dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
$notices=Join-Path $publish 'ThirdPartyNotices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'ThirdPartyNotices') -Destination $publish -Recurse -Force
foreach($package in @('markdig','microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64','microsoft.ml.onnxruntime','microsoft.ml.onnxruntime.managed')){
    $packageRoot=Join-Path $env:NUGET_PACKAGES $package
    $version=Get-ChildItem -LiteralPath $packageRoot -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
    foreach($file in Get-ChildItem -LiteralPath $version.FullName -File | Where-Object {$_.Name -match 'LICENSE|NOTICE'}){
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $notices ($package+'-'+$file.Name))
    }
}
if(-not(Test-Path -LiteralPath $Compiler)){throw 'Install Inno Setup 6 and pass -Compiler path'}
& $Compiler ('/DPublishDir='+$publish) ('/DArtifactDir='+$artifacts) (Join-Path $repo 'installer\StudyWhisper.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $artifacts 'StudyWhisper-0.1.6-Setup-x64.exe'
(Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash | Set-Content -Encoding ascii ($setup+'.sha256')
Write-Output $setup
