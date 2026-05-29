@echo off
cd /d D:\Code\work\CDiskManager\CDiskManager\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64

if exist _pri_staging rd /s /q _pri_staging
md _pri_staging
md _pri_staging\Views

copy /y App.xbf _pri_staging\
copy /y MainWindow.xbf _pri_staging\
if exist Views\*.xbf copy /y Views\*.xbf _pri_staging\Views\

REM Create priconfig - use createconfig first then modify
"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makepri.exe" createconfig /cf _pri_staging\priconfig.xml /dq en-US /pv 10.0.0 /o

REM Remove the packaging/isDeploymentMergeable nodes from the config
powershell -Command "(Get-Content '_pri_staging\priconfig.xml') -replace 'isDeploymentMergeable=\"true\"', 'isDeploymentMergeable=\"false\"' | Set-Content '_pri_staging\priconfig.xml'"

REM Create PRI with auto-merge
"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makepri.exe" new /cf _pri_staging\priconfig.xml /pr _pri_staging /in CDiskManager /of resources.pri /o
echo EXIT: %ERRORLEVEL%

rd /s /q _pri_staging
