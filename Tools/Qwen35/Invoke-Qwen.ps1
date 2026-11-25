param(
    [ValidateSet('Test','Compile','Export','Bake','Rebuild','Validate','Smoke','Preview','Motion','Console','Observation','Localization','Cleanup','Audit','BuildPC','BuildAndroid','BuildAll','VerifyAndroid','VerifyIOS','NativeTest','Restore','Setup','Reference','Summary','SourceCheck','SourcePackage')]
    [string]$Action='Test',
    [string]$DataRoot='',
    [string]$UnityPath='',
    [ValidateSet('cpu','cuda')][string]$Device='cpu'
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Qwen-Paths.ps1')
$projectRoot=Get-QwenProjectRoot
$DataRoot=Resolve-QwenDataRoot $DataRoot $projectRoot
$pythonActions=@('Test','Export','Reference','Summary','SourceCheck','SourcePackage')
if($Action -in $pythonActions){$pythonPath=Find-QwenPython $DataRoot}
else{$UnityPath=Find-QwenUnity $UnityPath $projectRoot}
$env:QWEN_DATA_ROOT=$DataRoot
$env:TEMP=Join-Path $DataRoot 'tmp'
$env:TMP=$env:TEMP
$env:HF_HUB_OFFLINE='1'
$env:PYTHONDONTWRITEBYTECODE='1'
$env:OPENCV_IO_ENABLE_OPENEXR='1'
New-Item -ItemType Directory -Force -Path $env:TEMP,(Join-Path $DataRoot 'logs') | Out-Null

function Invoke-Unity([string]$Target,[string]$Method,[string]$Marker,[bool]$Quit=$false) {
    $step=$Method.Split('.')[-1]
    $log=Join-Path $DataRoot "logs/pipeline-$Action-$Target-$step.log"
    $arguments=@('-batchmode','-buildTarget',$Target,'-projectPath',('"'+$projectRoot+'"'),'-executeMethod',$Method,'-logFile',('"'+$log+'"'))
    if($Quit){$arguments+='-quit'}
    $job=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $job.WaitForExit()
    if($job.ExitCode -ne 0){throw "Unity failed ($($job.ExitCode)); see $log"}
    if(!(Select-String -LiteralPath $log -SimpleMatch $Marker -Quiet)){throw "Missing completion marker $Marker; see $log"}
    Write-Output "PASS $Method; $log"
}

Push-Location -LiteralPath $projectRoot
try {
    switch($Action) {
        'Test' {
            & $pythonPath -m unittest discover -s Tools/Qwen35 -p 'test_*.py'
            if($LASTEXITCODE -ne 0){throw 'Preparation tests failed'}
        }
        'Export' {
            & $pythonPath Tools/Qwen35/prepare_model.py --model (Join-Path $DataRoot 'models/Qwen3.5-0.8B') --output (Join-Path $DataRoot 'exports/Qwen35-Full-Q4') --device $Device
            if($LASTEXITCODE -ne 0){throw 'Full model export failed'}
        }
        'Bake' {
            $source=Join-Path $DataRoot 'exports/Qwen35-Full-Q4'
            if(!(Test-Path -LiteralPath (Join-Path $source 'export-manifest.json'))){throw "Missing full export: $source"}
            Copy-Item -LiteralPath $source -Destination 'Assets/QwenWorld/Models/' -Recurse -Force
            Invoke-Unity Win64 QwenWorld.Editor.QwenModelPipeline.BakeFullAndValidate QWEN_IMPLEMENTATION_VALIDATION_COMPLETE
        }
        'Setup' { Invoke-Unity Win64 QwenWorld.Editor.QwenSourceSetup.InitializeBatch QWEN_SOURCE_CHECKOUT_READY }
        'Reference' {
            & $pythonPath -B Tools/Qwen35/world_reference.py --device $Device
            if($LASTEXITCODE -ne 0){throw 'World reference generation failed'}
        }
        'Summary' {
            & $pythonPath -B Tools/Qwen35/summarize_validation.py
            if($LASTEXITCODE -ne 0){throw 'Summary generation failed'}
        }
        'SourceCheck' {
            & $pythonPath -B Tools/Qwen35/source_audit.py
            if($LASTEXITCODE -ne 0){throw 'Source audit failed'}
        }
        'SourcePackage' {
            & $pythonPath -B Tools/Qwen35/source_audit.py --package
            if($LASTEXITCODE -ne 0){throw 'Source packaging failed'}
        }
        'Compile' { Invoke-Unity Win64 QwenWorld.Editor.QwenModelPipeline.Compile QWEN_UDON_COMPILE_FINISHED $true }
        'Rebuild' { Invoke-Unity Win64 QwenWorld.Editor.QwenSceneInstaller.InstallBatch QWEN_COMPLETE_INSTALL_FINISHED }
        'Validate' { Invoke-Unity Win64 QwenWorld.Editor.QwenIntegrationValidation.ValidateBatch QWEN_FULL_PROJECT_VALIDATION_PASSED }
        'Smoke' { Invoke-Unity Win64 QwenWorld.Editor.QwenModelPipeline.ValidateGpuAndWorldBatch QWEN_IMPLEMENTATION_VALIDATION_COMPLETE }
        'Preview' { Invoke-Unity Win64 QwenWorld.Editor.QwenFloorValidation.ApplyAndPreviewBatch QWEN_HEX_FLOOR_PASSED }
        'Motion' { Invoke-Unity Win64 QwenWorld.Editor.QwenMotionValidation.ValidateBatch QWEN_MOTION_VALIDATION_PASSED }
        'Console' { Invoke-Unity Win64 QwenWorld.Editor.QwenConsoleValidation.ValidateBatch QWEN_CONSOLE_LAYOUT_PASSED }
        'Observation' { Invoke-Unity Win64 QwenWorld.Editor.QwenObservationValidation.ValidateBatch QWEN_OBSERVATION_VALIDATION_PASSED }
        'Localization' { Invoke-Unity Win64 QwenWorld.Editor.QwenLocalizationValidation.ApplyBatch QWEN_LOCALIZATION_PASSED }
        'Cleanup' { Invoke-Unity Win64 QwenWorld.Editor.QwenProjectMaintenance.CleanupBatch QWEN_CLEANUP_PASSED }
        'Audit' { Invoke-Unity Win64 QwenWorld.Editor.QwenProjectAudit.ValidateBatch QWEN_PROJECT_AUDIT_PASSED }
        'BuildPC' { Invoke-Unity Win64 QwenWorld.Editor.QwenWorldBuild.BuildPC QWEN_BUNDLE_READY }
        'BuildAndroid' { Invoke-Unity Android QwenWorld.Editor.QwenWorldBuild.BuildAndroid QWEN_BUNDLE_READY }
        'VerifyAndroid' { Invoke-Unity Android QwenWorld.Editor.QwenWorldBuild.ValidateAndroidFromMainBatch QWEN_DIRECT_ANDROID_BUILD_PASSED }
        'VerifyIOS' { Invoke-Unity iOS QwenWorld.Editor.QwenWorldBuild.ValidateIOSFromMainBatch QWEN_DIRECT_IOS_BUILD_PASSED }
        'NativeTest' { Invoke-Unity Win64 QwenWorld.Editor.QwenWorldBuild.BuildAndTest QWEN_BUNDLE_READY }
        'Restore' { Invoke-Unity Win64 QwenWorld.Editor.QwenWorldBuild.RestoreMainScene QWEN_WORKSPACE_RESTORED }
        'BuildAll' {
            Invoke-Unity Win64 QwenWorld.Editor.QwenWorldBuild.BuildPC QWEN_BUNDLE_READY
            Invoke-Unity Android QwenWorld.Editor.QwenWorldBuild.BuildAndroid QWEN_BUNDLE_READY
            Invoke-Unity Win64 QwenWorld.Editor.QwenWorldBuild.RestoreMainScene QWEN_WORKSPACE_RESTORED
        }
    }
} finally { Pop-Location }
