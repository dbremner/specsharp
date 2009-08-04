@echo off
set REGASM=c:\Windows\Microsoft.NET\Framework\v2.0.50727\RegAsm.exe
%REGASM% /nologo /codebase Microsoft.VisualStudio.Package.dll
%REGASM% /nologo /codebase Microsoft.SpecSharp.dll
%REGASM% /nologo /codebase ContractsPropertyPane.dll
rem devenv /setup
