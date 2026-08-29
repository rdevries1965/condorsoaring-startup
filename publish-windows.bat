@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK is niet gevonden. Installeer dit eerst via https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
dotnet restore GoZCCondorLauncher.csproj
if errorlevel 1 goto :failed

dotnet build GoZCCondorLauncher.csproj -c Release --no-restore
if errorlevel 1 goto :failed

dotnet run --project tests\GoZCCondorLauncher.Tests.csproj -c Release
if errorlevel 1 goto :failed

dotnet publish GoZCCondorLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
  goto :failed
)
echo.
echo Gereed. De app staat in bin\Release\net8.0-windows\win-x64\publish
pause
exit /b 0

:failed
echo.
echo Bouwen, testen of publiceren is mislukt.
pause
exit /b 1
