@echo off
chcp 65001 >nul
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:DeskFence.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll src\*.cs
if errorlevel 1 (echo 编译失败，把上面的报错发给 Claude) else (echo 编译成功：DeskFence.exe)
pause
