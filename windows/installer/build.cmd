@echo off
setlocal
set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%~1
if not exist "%ISCC%" (
  echo Pass the path to ISCC.exe ^(Inno Setup 6^) as the first argument.
  exit /b 1
)
call "%~dp0..\build.cmd" || exit /b 1
dotnet publish "%~dp0..\VindOS\VindOS.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0..\VindOS\bin\publish" -nologo -v:q || exit /b 1
"%ISCC%" "%~dp0vindOS.iss" || exit /b 1
echo Installer in %~dp0out
