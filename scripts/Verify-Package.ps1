param([string]$Version='0.1.6')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version'}
$repo=Split-Path -Parent $PSScriptRoot
$artifacts=Join-Path $repo 'artifacts'
$app=Join-Path $artifacts ('publish-'+$Version+'\StudyWhisper.exe')
if(-not(Test-Path -LiteralPath $app)){throw 'Build the package first'}
$env:DOTNET_ROOT=Join-Path $artifacts 'unavailable-runtime'
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $artifacts ('package-extract-'+$Version)
$env:APPDATA=Join-Path $artifacts 'appdata'
$results=@()
foreach($mode in @('dependency','audio','ui')){
    $output=Join-Path $artifacts ('package-'+$mode+'-smoke-'+$Version)
    $process=Start-Process -FilePath $app -ArgumentList @('--'+$mode+'-smoke','--output',('"'+$output+'"')) -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(30000)){throw ('Package '+$mode+' timeout')}
    if($process.ExitCode -ne 0){throw ('Package '+$mode+' failed')}
    $checkFile=if($mode -eq 'ui'){'checks.json'}else{$mode+'-checks.json'}
    $checks=Get-Content -Raw -LiteralPath (Join-Path $output $checkFile) | ConvertFrom-Json
    if($checks | Where-Object {-not $_.passed}){throw ('Package '+$mode+' checks failed')}
    $results+=[pscustomobject]@{Mode=$mode;Passed=@($checks).Count;Output=$output}
}
$results | ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath (Join-Path $artifacts ($Version+'-package-checks.json'))
$results | Format-Table Mode,Passed
