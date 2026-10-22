@echo off
rem Double-click on the HotelPOS server PC: loads the sample floor plan and menu into the local server.
rem Asks for the HotelPOS admin password. Safe to run again; existing data is kept.
title HotelPOS - load sample data
echo.
echo HotelPOS - sample floor plan and menu
echo =====================================
echo This adds 4 sections with 28 tables and a 82-item menu to the server on this PC.
echo Anything that already exists is kept. You can change everything later in Administration.
echo.
set /p HPUSER=HotelPOS admin user name [admin]:
if "%HPUSER%"=="" set HPUSER=admin
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0seed-sample-data.ps1" -ApiBaseUrl http://localhost:5000 -Username %HPUSER%
echo.
if errorlevel 1 (
  echo Something went wrong - take a photo of this window and send it to your HotelPOS contact.
) else (
  echo Finished. Open HotelPOS: the tables and the menu are there.
)
echo.
pause
