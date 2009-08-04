cscript /e:jscript /nologo ..\..\common\bin\CleanReg.js

if exist *.dll del /Q *.dll
if exist *.pdb del /Q *.pdb
if exist *.exe del /Q *.exe
