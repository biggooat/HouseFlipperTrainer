@echo off
chcp 65001 >nul
rem Сборка трейнера под вашу версию House Flipper.
rem Нужен .NET SDK 8: https://dotnet.microsoft.com/download
rem Использование:  build.bat "C:\Program Files (x86)\Steam\steamapps\common\House Flipper"
rem Без аргумента — сборка по эталонным Unity-библиотекам (обычно тоже работает).

set GAMEDIR=%~1
if "%GAMEDIR%"=="" (
  dotnet build src\Injector\Injector.csproj -c Release -o out
) else (
  dotnet build src\Injector\Injector.csproj -c Release -o out -p:GameDir="%GAMEDIR%"
)
if errorlevel 1 ( echo СБОРКА НЕ УДАЛАСЬ & pause & exit /b 1 )
echo.
echo Готово: out\HouseFlipperTrainer.exe
pause
