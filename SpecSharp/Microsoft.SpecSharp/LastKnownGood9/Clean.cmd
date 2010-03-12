@echo off

rem set REGASM=c:\Windows\Microsoft.NET\Framework\v2.0.50727\RegAsm.exe
set REGASM=regasm
if not "%1"=="" set REGASM=%1

rem unregister old binaries
%REGASM% /nologo /silent /u Microsoft.VisualStudio.Package.dll
%REGASM% /nologo /silent /u Microsoft.SpecSharp.dll
%REGASM% /nologo /silent /u ContractsPropertyPane.dll
rem del bin\LastKnownGood.dll
rem del bin\LastKnownGood.pdb
