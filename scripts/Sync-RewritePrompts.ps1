param([string]$SourceRoot=(Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\LMDocs'),[switch]$Verify)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$target=Join-Path $repo 'src\StudyWhisper.Core\Prompts\lmdocs-rewrite.json'
$ids=@('global_natural_writing','rewrite_base','rewrite_mode_proofread','rewrite_style_default','rewrite_ai_patterns_skill')
$parts=@()
foreach($id in $ids){
    $file=Join-Path $SourceRoot ('prompts\sysPrompts\'+$id+'.sys.md')
    $bytes=[IO.File]::ReadAllBytes($file);$sourceHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    $text=[Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n","`n");$body=[regex]::Replace($text,'\A---\n.*?\n---\n','',[Text.RegularExpressions.RegexOptions]::Singleline).Trim()
    if(-not $body){throw ('Canonical prompt is empty: '+$id)}
    $parts+=[pscustomobject]@{Id=$id;Source=('prompts/sysPrompts/'+$id+'.sys.md');SourceSHA256=$sourceHash;Body=$body;BodySHA256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($body)))}
}
$instruction=($parts.Body -join "`n`n")
$handler='worker/settings-sync/src/handlers/rewriteHandler.ts'
$bundle=[ordered]@{SourceProject='LMDocs';Composition='global_natural_writing + rewrite_base + proofread + default + removeAiPatterns';CompositionSource=$handler;CompositionSourceSHA256=(Get-FileHash -LiteralPath (Join-Path $SourceRoot $handler) -Algorithm SHA256).Hash;InstructionSHA256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($instruction)));Parts=$parts}
if($Verify){$saved=Get-Content -Raw -LiteralPath $target|ConvertFrom-Json;if($saved.InstructionSHA256 -ne $bundle.InstructionSHA256-or$saved.CompositionSourceSHA256 -ne $bundle.CompositionSourceSHA256){throw 'Canonical LMDocs prompts or composition changed. Review and run Sync-RewritePrompts.ps1 explicitly.'};Write-Output ('Canonical prompt bundle verified: '+$bundle.InstructionSHA256);return}
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
[IO.File]::WriteAllText($target,($bundle|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output ('Imported exact canonical prompt bodies: '+$bundle.InstructionSHA256)
