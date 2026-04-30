#powershell -ExecutionPolicy Bypass -File "d:\Git\Kof6Key\build.ps1"
$cscPath = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $cscPath)) {
    $cscPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

if (-not (Test-Path $cscPath)) {
    throw "csc.exe was not found."
}

$outputDir = Join-Path $PSScriptRoot "dist"
$outputExe = Join-Path $outputDir "kof6key.exe"
$configFile = Join-Path $PSScriptRoot "kof6key.ini"

# 检查并结束正在运行的 kof6key.exe 进程
$runningProcess = Get-Process -Name "kof6key" -ErrorAction SilentlyContinue
if ($runningProcess) {
    Write-Host "Found running kof6key.exe process, terminating..."
    try {
        # 尝试正常结束进程
        $runningProcess | Stop-Process -Force -ErrorAction Stop
        Write-Host "Successfully terminated kof6key.exe process"
    }
    catch {
        Write-Host "Failed to terminate kof6key.exe process: $_"
        # 尝试使用 taskkill 强制结束
        try {
            taskkill /F /IM kof6key.exe
            Write-Host "Successfully terminated kof6key.exe process using taskkill"
        }
        catch {
            Write-Host "Failed to terminate kof6key.exe using taskkill: $_"
        }
    }
    # 等待进程完全结束
    Start-Sleep -Milliseconds 500
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

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

if (Test-Path $configFile) {
    Copy-Item -Path $configFile -Destination (Join-Path $outputDir "kof6key.ini") -Force
}

Write-Host "Build completed: dist\\kof6key.exe"
