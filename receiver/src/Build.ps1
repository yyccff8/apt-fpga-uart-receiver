$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $target=Join-Path $PSScriptRoot 'build'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:build/APT_Receiver.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll Protocol.cs Images.cs App.cs PreviewView.cs
    if($LASTEXITCODE -ne 0){throw 'Receiver build failed'}
    Write-Host ('Built: '+(Join-Path $target 'APT_Receiver.exe'))
} finally {Pop-Location}
