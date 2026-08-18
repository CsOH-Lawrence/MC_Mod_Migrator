@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo .NET Framework compiler was not found.
  pause
  exit /b 1
)
if not exist "..\release" mkdir "..\release"
"%CSC%" /nologo /target:winexe /out:"..\release\MC Mod Migrator.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll McModMigrator.cs
copy /y "..\Background.jpg" "..\release\Background.jpg" >nul
if errorlevel 1 pause
