@echo off
cd /d D:\Code\work\CDiskManager\CDiskManager\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64

if exist _pri_src rd /s /q _pri_src
md _pri_src
md _pri_src\Views

copy /y App.xbf _pri_src\
copy /y MainWindow.xbf _pri_src\
copy /y Views\CleanupPage.xbf _pri_src\Views\
copy /y Views\DashboardPage.xbf _pri_src\Views\
copy /y Views\DiskScanPage.xbf _pri_src\Views\
copy /y Views\DuplicateFilesPage.xbf _pri_src\Views\
copy /y Views\LargeFilesPage.xbf _pri_src\Views\
copy /y Views\PartitionAdvicePage.xbf _pri_src\Views\

"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makepri.exe" createconfig /cf _pri_src\priconfig.xml /dq en-US /pv 10.0.0 /o
"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makepri.exe" new /cf _pri_src\priconfig.xml /pr _pri_src /in CDiskManager /of resources.pri /o

echo EXIT: %ERRORLEVEL%
rd /s /q _pri_src
