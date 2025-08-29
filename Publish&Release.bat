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

REM 使用简化的发布命令（参考能用的版本）
dotnet publish -c Release -r win-x64 /p:SelfContained=true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false --output ./publish-!timestamp!

if !errorlevel! neq 0 (
    echo 发布失败！
    pause
    exit /b 1
)

echo.
echo 发布成功！
echo 输出目录: %CD%\publish-!timestamp!
echo 可执行文件: %CD%\publish-!timestamp!\%project_name%.exe
echo.

REM 创建最终打包目录
set "package_dir=%project_name%_Package_!timestamp!"
if exist "!package_dir!" rmdir /s /q "!package_dir!"
mkdir "!package_dir!"

echo 准备打包文件...

REM 复制发布的文件
xcopy "publish-!timestamp!\*" "!package_dir!\" /E /I /Y

REM 复制 Examples 文件夹（如果存在）
if exist "Examples" (
    echo 复制 Examples 文件夹...
    xcopy "Examples" "!package_dir!\Examples\" /E /I /Y
) else (
    echo 警告：未找到 Examples 文件夹，跳过复制
)

REM 创建 ZIP 文件
set "zip_name=%project_name%_!version!.zip"
echo 创建 ZIP 文件: !zip_name!

REM 删除已存在的 ZIP 文件
if exist "!zip_name!" (
    echo 删除现有 ZIP 文件...
    del "!zip_name!"
)

powershell -Command "Compress-Archive -Path '!package_dir!\*' -DestinationPath '!zip_name!' -Force"

if exist "!zip_name!" (
    echo.
    echo 打包完成！
    echo ZIP 文件: %CD%\!zip_name!
    echo.
    
    REM 自动清理临时文件
    echo 清理临时文件...
    rmdir /s /q "!package_dir!"
    rmdir /s /q "publish-!timestamp!"
    echo 临时文件已清理
    
    echo.
    echo 构建和打包成功完成！
    
) else (
    echo ZIP 文件创建失败
)

pause
