# Shared path resolution for PowerShell 5.1+; importing this file has no side effects.
function Get-QwenProjectRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
}

function Get-QwenLocalConfig([string]$ProjectRoot) {
    $file=Join-Path $ProjectRoot '.qwen-local.json'
    if(Test-Path -LiteralPath $file){ return Get-Content -LiteralPath $file -Raw | ConvertFrom-Json }
    return $null
}

function Resolve-QwenDataRoot([string]$Explicit,[string]$ProjectRoot) {
    $config=Get-QwenLocalConfig $ProjectRoot
    $value=$Explicit
    if(!$value){$value=$env:QWEN_DATA_ROOT}
    if(!$value -and $config){$value=$config.dataRoot}
    if(!$value){$value='.qwen-work'}
    if(![IO.Path]::IsPathRooted($value)){$value=Join-Path $ProjectRoot $value}
    return [IO.Path]::GetFullPath($value)
}

function Find-QwenUnity([string]$Explicit,[string]$ProjectRoot) {
    $config=Get-QwenLocalConfig $ProjectRoot
    $value=$Explicit
    if(!$value){$value=$env:QWEN_UNITY_PATH}
    if(!$value -and $config){$value=$config.unityPath}
    if($value){
        if(!(Test-Path -LiteralPath $value -PathType Leaf)){throw "Unity executable not found: $value"}
        return $value
    }
    $version=((Get-Content -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -split ': ')[1]
    $candidates=@()
    if($env:ProgramFiles){$candidates+=Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"}
    $candidates+="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
    $command=Get-Command Unity -ErrorAction SilentlyContinue
    if($command){$candidates+=$command.Source}
    foreach($candidate in $candidates){if(Test-Path -LiteralPath $candidate -PathType Leaf){return $candidate}}
    throw "Unity $version not found. Pass -UnityPath or set QWEN_UNITY_PATH."
}

function Find-QwenPython([string]$DataRoot) {
    foreach($relative in @('.venv/Scripts/python.exe','.venv/bin/python')){
        $candidate=Join-Path $DataRoot $relative
        if(Test-Path -LiteralPath $candidate -PathType Leaf){return $candidate}
    }
    $command=Get-Command python -ErrorAction SilentlyContinue
    if($command){return $command.Source}
    throw 'Python is not installed. Run Initialize-Qwen.ps1 with Python 3.12 available.'
}
