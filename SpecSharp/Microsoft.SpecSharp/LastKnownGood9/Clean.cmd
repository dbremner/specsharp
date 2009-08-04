@echo off
rem unregister old binaries
regasm /nologo /silent /u Microsoft.VisualStudio.Package.dll
regasm /nologo /silent /u Microsoft.SpecSharp.dll
regasm /nologo /silent /u ContractsPropertyPane.dll
rem del bin\LastKnownGood.dll
rem del bin\LastKnownGood.pdb
