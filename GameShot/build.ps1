param([switch]$Test, [switch]$TestLibrary)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.buildhome'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
# 输出为单文件程序；目标机器需要 .NET 10 Desktop Runtime，本机已安装。
$taskProject = Join-Path $PSScriptRoot 'GameShot.csproj'
$taskOutput = Join-Path $PSScriptRoot 'release'
dotnet publish $taskProject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:UseSharedCompilation=false -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw '编译失败，请检查上方错误。' }
Copy-Item -LiteralPath (Join-Path $taskOutput 'GameShot.exe') -Destination (Join-Path $PSScriptRoot 'GameShot.exe')
if ($Test -or $TestLibrary) {
    $taskApp = Join-Path $taskOutput 'GameShot.exe'
    $taskResult = Join-Path $PSScriptRoot 'tests\results'
    $taskTestMode = if ($Test) { '--self-test' } else { '--library-test' }
    $taskReport = if ($Test) { 'test-report.txt' } else { 'library-test-report.txt' }
    $taskTestProcess = Start-Process -FilePath $taskApp -ArgumentList @($taskTestMode, ('"' + $taskResult + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $taskResult $taskReport)
    if ($taskTestProcess.ExitCode -ne 0) { throw '集成测试失败。' }
}
