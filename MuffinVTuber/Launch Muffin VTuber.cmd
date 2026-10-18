@echo off
rem Starts Muffin VTuber. If a newer build is waiting in Builds\Win64-next (built while the app was
rem open), it is moved into Builds\Win64 first, so this always runs the newest version.
rem If the app is already open, it does nothing (two copies would fight over the camera).
setlocal
set "HERE=%~dp0"
set "LIVE=%HERE%Builds\Win64"
set "NEXT=%HERE%Builds\Win64-next"
tasklist /FI "IMAGENAME eq MuffinMotionPrototype.exe" | find /I "MuffinMotionPrototype.exe" >nul
if not errorlevel 1 exit /b 0
if exist "%NEXT%\MuffinMotionPrototype.exe" (
  robocopy "%NEXT%" "%LIVE%" /E /NFL /NDL /NJH /NJS /NP >nul
  rd /s /q "%NEXT%"
)
start "" "%LIVE%\MuffinMotionPrototype.exe"
