@echo off
REM ============================================================================
REM  Sale math unit tests.
REM
REM  Compiles the REAL StoreSettings.vb together with the test module, so a pass
REM  means the shipped code is correct -- not a copy of it that happens to agree.
REM  No database and no MySQL connector are needed: the tests set the VAT
REM  configuration through StoreSettings.ConfigureForTest.
REM
REM  Uses the Roslyn vbc that ships with Visual Studio, not the .NET Framework
REM  one in C:\Windows: the sale-math helpers return tuples, which the old
REM  compiler cannot parse.
REM ============================================================================
setlocal
set VBC=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\vbc.exe
set ROOT=%~dp0..
set OUT=%~dp0bin

if not exist "%VBC%" (
    echo ERROR: Roslyn vbc.exe not found at:
    echo   %VBC%
    echo Adjust VBC at the top of this script to match your Visual Studio install.
    exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"

REM  StoreSettings.vb carries an Imports for the MySQL connector, so the
REM  reference is needed to compile it even though no connection is opened.
"%VBC%" /nologo /target:exe /out:"%OUT%\SaleMathTests.exe" ^
    /reference:"%ROOT%\MySql.Data.dll" ^
    /reference:"%ROOT%\System.Configuration.ConfigurationManager.dll" ^
    /main:SaleMathTests ^
    "%ROOT%\Modules\StoreSettings.vb" ^
    "%ROOT%\Modules\Db.vb" ^
    "%~dp0SaleMathTests.vb"

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)

REM  StoreSettings only touches the connector if something asks for a setting
REM  before ConfigureForTest runs, but the CLR resolves the reference on first
REM  use of the type either way -- so the DLL has to sit beside the test exe.
copy /y "%ROOT%\MySql.Data.dll" "%OUT%\" >nul

"%OUT%\SaleMathTests.exe"
exit /b %errorlevel%
