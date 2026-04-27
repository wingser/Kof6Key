$cscPath = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    $cscPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

if (-not (Test-Path $cscPath)) {
    throw "csc.exe was not found."
}

$outputDir = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$outputExe = Join-Path $outputDir "kof6key.exe"

& $cscPath `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /debug:pdbonly `
    /out:$outputExe `
    /win32icon:6key.ico `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    GlobalKeyboardHook.cs `
    GlobalKeyboardHookEventArgs.cs `
    Main.cs `
    Main.Designer.cs `
    Program.cs

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

Write-Host "Build completed: dist\\kof6key.exe"
