@echo off
if ..==.%1. (
set CONFIG=Debug
echo Assuming "Debug" configuration
) else (
set CONFIG=%1
)

nmake /f makefile8 Build=%CONFIG%
xcopy /I /S ..\Templates Templates
