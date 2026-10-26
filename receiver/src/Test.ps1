$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    New-Item -ItemType Directory -Force 'build' | Out-Null
    & $compiler /nologo /target:exe /codepage:65001 /out:build/ProtocolTests.exe /r:System.Drawing.dll Protocol.cs Images.cs Tests.cs PreviewTests.cs
    if($LASTEXITCODE -ne 0){throw 'Protocol tests compile failed'}
    & './build/ProtocolTests.exe' '../verification/rtl_packet.hex' '../verification/preview_packet.hex' '../verification/preview_full.hex'
    if($LASTEXITCODE -ne 0){throw 'Protocol tests failed'}
    & $compiler /nologo /target:exe /main:QualityTests /codepage:65001 /out:build/QualityTests.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll Protocol.cs Images.cs App.cs PreviewView.cs QualityTests.cs
    if($LASTEXITCODE -ne 0){throw 'Quality tests compile failed'}
    & './build/QualityTests.exe' '../verification/quality_packet.hex' '../verification/hq_packet.hex' '../verification/metadata.txt' 'build/overlay.png' 'build/layout.png'
    if($LASTEXITCODE -ne 0){throw 'Quality tests failed'}
} finally {Pop-Location}
