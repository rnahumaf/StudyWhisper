param([switch]$Ui)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_HOME=Join-Path $repo 'artifacts\dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:NUGET_PACKAGES=Join-Path $repo 'artifacts\packages'
$env:APPDATA=Join-Path $repo 'artifacts\appdata'
dotnet restore (Join-Path $repo 'tests\StudyWhisper.Tests') --configfile (Join-Path $repo 'NuGet.Config') -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Restore failed'}
dotnet run --project (Join-Path $repo 'tests\StudyWhisper.Tests') --configuration Release --no-restore
if($LASTEXITCODE -ne 0){throw 'Tests failed'}
dotnet restore (Join-Path $repo 'tests\StudyWhisper.AudioAudit') --configfile (Join-Path $repo 'NuGet.Config') -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Audit restore failed'}
dotnet run --project (Join-Path $repo 'tests\StudyWhisper.AudioAudit') --configuration Release --no-restore -- (Join-Path $repo 'artifacts\audio-triage-audit.json') (Join-Path $repo 'tests\fixtures')
if($LASTEXITCODE -ne 0){throw 'Audit failed'}
if($Ui){
    dotnet restore (Join-Path $repo 'src\StudyWhisper.App') --configfile (Join-Path $repo 'NuGet.Config') -p:NuGetAudit=false
    if($LASTEXITCODE -ne 0){throw 'Restore failed'}
    dotnet build (Join-Path $repo 'src\StudyWhisper.App') -c Release --no-restore
    if($LASTEXITCODE -ne 0){throw 'Build failed'}
    $app=Join-Path $repo 'src\StudyWhisper.App\bin\Release\net8.0-windows\StudyWhisper.exe'
    $output=Join-Path $repo 'artifacts\ui-smoke'
    $process=Start-Process -FilePath $app -ArgumentList @('--ui-smoke','--output',('"'+$output+'"')) -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(30000)){throw 'UI smoke timeout'}
    if($process.ExitCode -ne 0){throw 'UI smoke process failed'}
    $checks=Get-Content -Raw (Join-Path $output 'checks.json') | ConvertFrom-Json
    if($checks | Where-Object {-not $_.passed}){throw 'UI smoke failed'}
    $checks | Format-Table name,passed
}
