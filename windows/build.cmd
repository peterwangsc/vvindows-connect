@echo off
setlocal
set MSBUILD="%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
if not exist %MSBUILD% set MSBUILD="%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe"
%MSBUILD% "%~dp0VindOS.Xr\VindOS.Xr.vcxproj" -restore -p:Configuration=Release -p:Platform=x64 -nologo -v:m || exit /b 1
dotnet build "%~dp0VindOS\VindOS.csproj" -c Release -nologo -v:m || exit /b 1
echo Built %~dp0VindOS\bin\Release\net10.0-windows\win-x64\vindOS.exe
