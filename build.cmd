@echo off
rem Compile ZikTray et ZikProbe avec le compilateur C# livre avec Windows (.NET Framework 4).
set FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FX%\csc.exe
cd /d "%~dp0"
"%CSC%" -nologo -nowarn:618 -codepage:65001 -target:winexe -win32icon:ZikTray.ico -out:ZikTray.exe -lib:"%FX%\WPF" -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll -r:System.Xaml.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Xml.dll ZikTray.cs ZikClient.cs || exit /b 1
"%CSC%" -nologo -out:ZikProbe.exe ZikProbe.cs || exit /b 1
