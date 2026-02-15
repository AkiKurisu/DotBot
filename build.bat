@echo off

if exist "Release" (
    rmdir /s /q "Release"
)
mkdir "Release"

cd DotBot

echo.
echo =====================================
echo Extracting version number...
echo =====================================
echo.

REM Set default version in case extraction fails
set VERSION=0.0.0

REM Extract version using PowerShell for better reliability
for /f "delims=" %%i in ('powershell -Command "(Select-Xml -Path 'DotBot.csproj' -XPath '//Version').Node.InnerText"') do set VERSION=%%i

REM If PowerShell method failed, try manual parsing
if "%VERSION%"=="0.0.0" (
    echo Trying alternative version extraction method...
    for /f "tokens=2 delims=>" %%a in ('findstr /C:"<Version>" DotBot.csproj 2^>nul') do (
        for /f "tokens=1 delims=<" %%b in ("%%a") do set VERSION=%%b
    )
)

REM Remove any whitespace
for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo Version found: %VERSION%

echo.
echo =====================================
echo  Building DotBot...
echo =====================================
echo.

call dotnet publish /p:PublishProfile=ReleaseProfile

if %ERRORLEVEL% neq 0 (
    echo Build DotBot failed with exit code %ERRORLEVEL%.
    goto :failure
)

goto :success

:failure
echo.
echo Installation failed. Please try again.
echo.
pause
exit /b 1

:success
echo.
echo =====================================
echo  Build completed successfully!
echo =====================================
echo.

cd ..

echo.
echo =====================================
echo  Packaging...
echo =====================================
echo.

REM Copy helper scripts to CLI output directory
copy /Y "Scripts\install_to_path.ps1" "Release\DotBot\install_to_path.ps1"
copy /Y "Scripts\dotbot.bat" "Release\DotBot\dotbot.bat"
copy /Y "Scripts\dotbot.ps1" "Release\DotBot\dotbot.ps1"

REM Create zip for DotBot
echo Creating DotBot.zip...
powershell -Command "Compress-Archive -Path 'Release\DotBot\*' -DestinationPath 'Release\DotBot_v%VERSION%.zip' -Force"

echo.
echo =====================================
echo  Packaging completed!
echo =====================================
echo  - DotBot.zip created
echo =====================================
echo.
pause 