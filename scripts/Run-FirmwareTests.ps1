param([switch]$SkipFull)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $repoRoot 'build/rtl-tests'
New-Item -ItemType Directory -Force $testRoot | Out-Null
$rtl=@(Get-ChildItem (Join-Path $repoRoot 'firmware/rtl') -Filter '*.v' | ForEach-Object FullName)
$tbRoot=Join-Path $repoRoot 'firmware/tb'
function Run-Case([string]$Name,[string]$Bench,[string]$Top,[string[]]$Extra){
    $caseRoot=Join-Path $testRoot $Name
    New-Item -ItemType Directory -Force $caseRoot | Out-Null
    Push-Location $caseRoot
    try {
        & iverilog -g2012 -s $Top @Extra -o test.vvp (Join-Path $tbRoot $Bench) @rtl
        if($LASTEXITCODE -ne 0){throw "Compile failed: $Name"}
        & vvp test.vvp | Tee-Object -FilePath simulation.log
        if($LASTEXITCODE -ne 0){throw "Simulation failed: $Name"}
    } finally {Pop-Location}
}
& iverilog -g2012 -s apt_transport -o (Join-Path $testRoot 'compile.vvp') @rtl
if($LASTEXITCODE -ne 0){throw 'Transport compile failed'}
Run-Case 'commands' 'tb_commands.v' 'tb_commands' @()
Run-Case 'quality' 'tb_image_sender.v' 'tb_image_sender' @('-Ptb_image_sender.PROFILE=2')
Run-Case 'hq' 'tb_image_sender.v' 'tb_image_sender' @('-Ptb_image_sender.PROFILE=1')
Run-Case 'legacy' 'tb_image_sender.v' 'tb_image_sender' @('-Ptb_image_sender.PROFILE=0')
Run-Case 'metadata' 'tb_metadata.v' 'tb_metadata' @()
Run-Case 'metadata_legacy' 'tb_metadata_legacy.v' 'tb_metadata' @()
Run-Case 'nominal' 'tb_nominal_uart.v' 'tb_image_sender' @()
if(!$SkipFull){Run-Case 'full' 'tb_quality_full.v' 'tb_image_sender' @()}
$checkArgs=@((Join-Path $PSScriptRoot 'verify_rtl.py'),$testRoot)
if($SkipFull){$checkArgs+='--skip-full'}
& python @checkArgs
if($LASTEXITCODE -ne 0){throw 'RTL output comparison failed'}
