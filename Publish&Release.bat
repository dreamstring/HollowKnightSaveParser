@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
echo 开始构建项目...

REM 获取当前时间戳
for /f "tokens=2 delims==" %%a in ('wmic OS Get localdatetime /value') do set "dt=%%a"
set "YY=%dt:~2,2%" & set "YYYY=%dt:~0,4%" & set "MM=%dt:~4,2%" & set "DD=%dt:~6,2%"
set "HH=%dt:~8,2%" & set "Min=%dt:~10,2%" & set "Sec=%dt:~12,2%"
set "timestamp=%YYYY%%MM%%DD%_%HH%%Min%%Sec%"

REM 自动检测项目文件
set "project_file="
set "project_name="
for %%f in (*.csproj) do (
    set "project_file=%%f"
    set "project_name=%%~nf"
    goto :found_project
)

:found_project
if "%project_file%"=="" (
    echo 错误：未找到 .csproj 文件！
    pause
    exit /b 1
)

echo 找到项目文件: %project_file%
echo 项目名称: %project_name%

REM 读取版本信息
set "version=1.0.0"
if exist "%project_file%" (
    echo 读取项目版本信息...
    for /f "tokens=*" %%i in ('findstr "<Version>" "%project_file%"') do (
        set "line=%%i"
        for /f "tokens=2 delims=<>" %%j in ("!line!") do (
            set "version=%%j"
        )
    )
)

echo 项目版本: !version!

REM 恢复项目依赖
echo 恢复项目依赖...
dotnet restore
if !errorlevel! neq 0 (
    echo 依赖恢复失败！
    pause
    exit /b 1
)

REM 清理项目
echo 清理项目...
dotnet clean -c Release

REM 删除构建目录
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj

echo 开始发布单文件...

REM 创建临时发布目录
set "temp_publish=temp_publish_!timestamp!"
if exist "!temp_publish!" rmdir /s /q "!temp_publish!"

REM 发布单文件到临时目录
dotnet publish -c Release -r win-x64 /p:SelfContained=true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false --output ./!temp_publish!

if !errorlevel! neq 0 (
    echo 发布失败！
    pause
    exit /b 1
)

REM 设置最终的 exe 文件名（包含版本号）
set "final_exe=%project_name%_v!version!.exe"

REM 删除已存在的同名 exe 文件
if exist "!final_exe!" (
    echo 删除现有文件: !final_exe!
    del "!final_exe!"
)

REM 复制生成的 exe 到项目根目录
if exist "!temp_publish!\%project_name%.exe" (
    echo 复制可执行文件到项目根目录...
    copy "!temp_publish!\%project_name%.exe" "!final_exe!"
    
    echo.
    echo 发布成功！
    echo 可执行文件: %CD%\!final_exe!
    echo.
    
    REM 清理临时发布目录
    echo 清理临时文件...
    rmdir /s /q "!temp_publish!"
    echo 临时文件已清理
    
    echo.
    echo 构建完成！单文件 exe 已生成在项目根目录！
    
) else (
    echo 错误：未找到生成的 exe 文件
    rmdir /s /q "!temp_publish!"
    pause
    exit /b 1
)

pause
