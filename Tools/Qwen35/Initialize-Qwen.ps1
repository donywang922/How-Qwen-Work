param(
    [string]$DataRoot='',
    [string]$Python='python',
    [string]$FromExport='',
    [ValidateSet('cpu','cu128')][string]$TorchBackend='cpu',
    [switch]$SkipInstall,
    [switch]$Offline
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Qwen-Paths.ps1')
$projectRoot=Get-QwenProjectRoot
$DataRoot=Resolve-QwenDataRoot $DataRoot $projectRoot
$env:QWEN_DATA_ROOT=$DataRoot
$env:PYTHONDONTWRITEBYTECODE='1'
$env:HF_HUB_OFFLINE='1'
$env:OPENCV_IO_ENABLE_OPENEXR='1'

function Run-Python([string]$Executable,[string[]]$Arguments) {
    & $Executable @Arguments
    if($LASTEXITCODE -ne 0){throw "Python preparation failed: $($Arguments[0])"}
}

if($FromExport){
    $pythonPath=if($Python -eq 'python'){Find-QwenPython $DataRoot}else{$Python}
    Run-Python $pythonPath @('-B',(Join-Path $PSScriptRoot 'restore_model.py'),'--export',$FromExport,'--project',$projectRoot)
} else {
    $venv=Join-Path $DataRoot '.venv'
    $pythonPath=Join-Path $venv 'Scripts/python.exe'
    if($env:OS -ne 'Windows_NT'){$pythonPath=Join-Path $venv 'bin/python'}
    if(!(Test-Path -LiteralPath $pythonPath)){
        Run-Python $Python @('-m','venv',$venv)
    }
    if(!$SkipInstall){
        if($Offline){throw 'Offline mode requires an existing environment and -SkipInstall.'}
        $lock=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'sources.lock.json') -Raw | ConvertFrom-Json
        Run-Python $pythonPath @('-m','pip','install',"torch==$($lock.torch)","torchvision==$($lock.torchvision)",'--index-url',"https://download.pytorch.org/whl/$TorchBackend")
        Run-Python $pythonPath @('-m','pip','install','-r',(Join-Path $PSScriptRoot 'requirements.txt'))
    }
    $downloadArgs=@('-B',(Join-Path $PSScriptRoot 'download_model.py'))
    $vendorArgs=@('-B',(Join-Path $PSScriptRoot 'download_vendor.py'))
    if($Offline){$downloadArgs+='--offline';$vendorArgs+='--offline'}
    Run-Python $pythonPath $downloadArgs
    Run-Python $pythonPath $vendorArgs
    $exportRoot=Join-Path $DataRoot 'exports/Qwen35-Full-Q4'
    $device=if($TorchBackend -eq 'cpu'){'cpu'}else{'cuda'}
    Run-Python $pythonPath @('-B',(Join-Path $PSScriptRoot 'prepare_model.py'),'--output',$exportRoot,'--device',$device)
    Run-Python $pythonPath @('-B',(Join-Path $PSScriptRoot 'restore_model.py'),'--export',$exportRoot,'--project',$projectRoot)
}
Write-Output 'Prepared model payloads. Restore the VRChat SDK with Creator Companion, then run Invoke-Qwen.ps1 Setup or use Qwen World > Setup > Initialize source checkout.'
