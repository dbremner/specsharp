@echo off
set TO=%1
set PDBTO=%TO:~0,-4%.pdb

REM This script copies from temp files to real files and installs the new dll, we assume CopyBitsToTemp has already been run so we know copying here will succeed

if exist %TO% regasm /nologo /silent /u %TO%  
if ERRORLEVEL 1 echo ERROR uninstalling old %TO% from the Registry

copy /y /v %TO%.temp %TO% > nul
if ERRORLEVEL 1 (
  echo ERROR moving %TO%.temp to %TO%
  exit 1
)

del /Q %TO%.temp > nul
if ERRORLEVEL 1 (
  echo ERROR deleting temp file %TO%.temp
  exit 1
)

if exist %PDBTO%.temp copy /y /v %PDBTO%.temp %PDBTO% > nul
if ERRORLEVEL 1 (
  echo ERROR moving %PDBTO%.temp to %PDBTO%
  exit 1
)

if exist %PDBTO%.temp del /Q %PDBTO%.temp > nul
if ERRORLEVEL 1 (
  echo ERROR deleting temp file %PDBTO%.temp
  exit 1
)

regasm /nologo /codebase %TO%
if ERRORLEVEL 1 echo ERROR installing %TO% into the Registry

