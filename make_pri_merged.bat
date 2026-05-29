@echo off
cd /d D:\Code\work\CDiskManager\CDiskManager\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64

REM Delete old resources.pri
del /q resources.pri 2>/dev/null

REM Copy framework PRI as the base resources.pri (it was originally resources.pri in the MSIX)
copy /y "Microsoft.UI.Xaml.Controls.pri" resources.pri

echo EXIT: %ERRORLEVEL%
