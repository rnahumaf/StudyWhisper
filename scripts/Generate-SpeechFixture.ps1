param([string]$OutputDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\fixtures'))
$ErrorActionPreference='Stop'
# Explicit output stream only. This script never opens capture or plays sound.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$voice=New-Object -ComObject SAPI.SpVoice
$token=New-Object -ComObject SAPI.SpObjectToken
$token.SetId('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens\MSTTS_V110_ptBR_DanielM','', $false)
$voice.Voice=$token
$items=@(
    @{name='pt-BR-question';text=('Qual '+[char]0xE9+' a diferen'+[char]0xE7+'a entre mitose e meiose?')},
    @{name='pt-BR-short-question';text=('Por qu'+[char]0xEA+'?')},
    @{name='pt-BR-comment';text=('Isso '+[char]0xE9+' interessante. Entendi a explica'+[char]0xE7+[char]0xE3+'o.')}
)
$provenance=@()
foreach($item in $items){
    $stream=New-Object -ComObject SAPI.SpFileStream
    $format=New-Object -ComObject SAPI.SpAudioFormat
    $format.Type=18 # SAFT16kHz16BitMono
    $stream.Format=$format
    $file=Join-Path $OutputDirectory ($item.name+'.wav')
    $stream.Open($file,3,$false)
    try{$voice.AudioOutputStream=$stream;[void]$voice.Speak($item.text,0)}finally{$stream.Close()}
    $provenance+=[pscustomobject]@{file=$item.name+'.wav';text=$item.text;generator='Windows SAPI TTS, OneCore Microsoft Daniel pt-BR';format='PCM16 mono 16000 Hz';sha256=(Get-FileHash -LiteralPath $file).Hash}
}
$provenance | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'provenance.json') -Encoding utf8
