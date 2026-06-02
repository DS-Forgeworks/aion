@echo off
set DOTNET_ROOT=%USERPROFILE%\.dotnet
set PATH=%DOTNET_ROOT%;%PATH%
dotnet "%~dp0dist\Aion.Host.dll" %*
pause
