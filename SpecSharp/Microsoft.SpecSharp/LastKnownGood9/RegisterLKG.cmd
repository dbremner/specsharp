@echo off

set REGASM=regasm
if not "%1"=="" set REGASM=%1

%REGASM% /nologo /codebase Microsoft.VisualStudio.Package.dll
%REGASM% /nologo /codebase Microsoft.SpecSharp.dll
%REGASM% /nologo /codebase ContractsPropertyPane.dll
rem devenv /setup
