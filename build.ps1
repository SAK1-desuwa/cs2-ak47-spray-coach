# 重新编译 AK47压枪提醒器.exe
# 不需要 .NET SDK / Visual Studio：直接用 Windows 自带的 C# 编译器（.NET Framework 4.x）
# 用法：右键 -> 使用 PowerShell 运行，或在终端执行  .\build.ps1

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "找不到 csc.exe（需要 .NET Framework 4.x，Windows 自带）" }

$src = Join-Path $here "src\Ak47SprayCoach.cs"
$out = Join-Path $here "AK47压枪提醒器.exe"
$ico = Join-Path $here "app.ico"

$compilerArgs = @("/nologo", "/target:winexe", "/platform:x64", "/optimize+", "/codepage:65001")
if (Test-Path $ico) { $compilerArgs += "/win32icon:$ico" }
$compilerArgs += "/out:$out"
$compilerArgs += $src

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
Write-Host ("编译完成: {0}  ({1:N0} 字节)" -f $out, (Get-Item $out).Length) -ForegroundColor Green

# ---- 自动跑一遍数据自检 ----
$report = Join-Path $here "self-test.txt"
Remove-Item $report -Force -ErrorAction SilentlyContinue
& $out --selftest $report | Out-Null

$deadline = (Get-Date).AddSeconds(10)
while (-not (Test-Path $report) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }

if (Test-Path $report) {
    Get-Content $report | Write-Host
} else {
    Write-Host "自检报告没生成（不影响 exe 使用）" -ForegroundColor Yellow
}
