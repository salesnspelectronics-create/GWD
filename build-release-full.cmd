@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title Generic Web Deployment Manager - Full Clean Release Build
set "PROJECT=src\DeploymentManager\DeploymentManager.csproj"
set "SOURCE_ROOT=%CD%"
set "DIST=dist"
set "CACHE_ROOT=%LOCALAPPDATA%\GenericWebDeploymentManager\SelfBuildCache"
set "NUGET_PACKAGES=%CACHE_ROOT%\NuGetPackages"
set "NUGET_HTTP_CACHE_PATH=%CACHE_ROOT%\NuGetHttpCache"
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "[xml]$x=Get-Content '.\Directory.Build.props'; $x.Project.PropertyGroup.DeploymentManagerVersion"`) do set "FULL_VERSION=%%V"
for /f "tokens=1 delims=." %%V in ("%FULL_VERSION%") do set "VERSION=%%V"
for /f "usebackq delims=" %%R in (`powershell -NoProfile -ExecutionPolicy Bypass -File ".\resolve-release-root.ps1" -Create -PathOnly`) do set "MANAGER_ROOT=%%R"
if not defined MANAGER_ROOT (echo [ERROR] Could not resolve Manager release/output folder from Settings. & pause & exit /b 1)
if not exist "%NUGET_PACKAGES%" mkdir "%NUGET_PACKAGES%" >nul 2>&1
if not exist "%NUGET_HTTP_CACHE_PATH%" mkdir "%NUGET_HTTP_CACHE_PATH%" >nul 2>&1
if not exist "%MANAGER_ROOT%" mkdir "%MANAGER_ROOT%" >nul 2>&1
echo [RELEASE] Resolved Settings output: %MANAGER_ROOT%

rem V60: never build directly from a potentially long incoming/versioned path.
rem Stage the complete source in a controlled short path to avoid legacy MAX_PATH failures.
set "BUILD_STAGE=%LOCALAPPDATA%\GWDM\Build\V%VERSION%"
if exist "%BUILD_STAGE%" rmdir /s /q "%BUILD_STAGE%"
mkdir "%BUILD_STAGE%" >nul 2>&1 || (echo [ERROR] Could not create short build staging folder: %BUILD_STAGE% & pause & exit /b 1)
robocopy "%SOURCE_ROOT%" "%BUILD_STAGE%" /E /XD .git bin obj dist /XF *.user *.suo >nul
if errorlevel 8 (echo [ERROR] Could not stage source into short build workspace. & pause & exit /b 1)
set "PROJECT=%BUILD_STAGE%\src\DeploymentManager\DeploymentManager.csproj"
set "DIST=%BUILD_STAGE%\dist"
echo [BUILD] Short staging workspace: %BUILD_STAGE%
where dotnet >nul 2>&1 || (echo [ERROR] .NET SDK was not found in PATH. & pause & exit /b 1)

echo [FULL] V%VERSION% output: %MANAGER_ROOT%
dotnet clean "%PROJECT%" -c Release --verbosity minimal || (pause & exit /b 1)
dotnet restore "%PROJECT%" -r win-x64 --packages "%NUGET_PACKAGES%" --force -p:NuGetAudit=false --verbosity minimal || (pause & exit /b 1)
if exist "%DIST%" rmdir /s /q "%DIST%"
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%DIST%" --verbosity minimal || (pause & exit /b 1)
set "BUILT_EXE=%DIST%\GenericWeb.DeploymentManager.exe"
copy /y "%BUILT_EXE%" "%MANAGER_ROOT%\DeploymentManager-V%VERSION%.exe" >nul || (echo [ERROR] Versioned EXE copy failed. & pause & exit /b 1)
copy /y "%BUILT_EXE%" "%MANAGER_ROOT%\DeploymentManager.exe" >nul || (echo [ERROR] Latest EXE copy failed. & pause & exit /b 1)
start "" "%MANAGER_ROOT%\DeploymentManager-V%VERSION%.exe"
echo Built: %BUILT_EXE%
echo Published: %MANAGER_ROOT%
exit /b 0
