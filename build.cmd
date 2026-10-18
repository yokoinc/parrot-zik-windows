@echo off
rem Compile ZikTray, son installateur et ZikProbe avec le compilateur C# livre avec Windows (.NET Framework 4).
set FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FX%\csc.exe
set WPF=-lib:"%FX%\WPF" -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll -r:System.Xaml.dll -r:System.Drawing.dll
cd /d "%~dp0"
"%CSC%" -nologo -nowarn:618 -codepage:65001 -target:winexe -win32icon:ZikTray.ico -out:ZikTray.exe %WPF% -r:System.Windows.Forms.dll -r:System.Xml.dll ZikTray.cs ZikClient.cs Theme.cs || exit /b 1
"%CSC%" -nologo -codepage:65001 -target:winexe -win32icon:ZikTray.ico -out:ZikTray-Setup.exe %WPF% -r:Microsoft.CSharp.dll -r:System.Core.dll -resource:ZikTray.exe,ZikTray.exe ZikSetup.cs Theme.cs || exit /b 1
"%CSC%" -nologo -out:ZikProbe.exe ZikProbe.cs || exit /b 1
