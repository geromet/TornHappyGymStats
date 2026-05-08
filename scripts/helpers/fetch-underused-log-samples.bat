@echo off
setlocal ENABLEDELAYEDEXPANSION

REM Fetch Torn log samples for selected underused log types into a SINGLE redacted JSON file.
REM Usage:
REM   scripts\helpers\fetch-underused-log-samples.bat
REM   scripts\helpers\fetch-underused-log-samples.bat --api-key your_key --limit 100 --out .\torn-log-samples.json

set "API_KEY="
set "LIMIT=100"
set "OUT_FILE=.\torn-log-samples.json"
set "BASE_URL=https://api.torn.com/v2/user/log"

:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--api-key" (
  set "API_KEY=%~2"
  shift
  shift
  goto parse_args
)
if /I "%~1"=="--limit" (
  set "LIMIT=%~2"
  shift
  shift
  goto parse_args
)
if /I "%~1"=="--out" (
  set "OUT_FILE=%~2"
  shift
  shift
  goto parse_args
)
if /I "%~1"=="-h" goto usage
if /I "%~1"=="--help" goto usage

echo Unknown argument: %~1
goto usage

:args_done
if "%API_KEY%"=="" set /p API_KEY=Enter Torn API key: 
if "%API_KEY%"=="" (
  echo ERROR: Missing API key.
  exit /b 2
)

for %%I in ("%OUT_FILE%") do set "OUT_DIR=%%~dpI"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

set "TMP_ENTRIES=%TEMP%\torn_entries_%RANDOM%_%RANDOM%.json"
> "%TMP_ENTRIES%" echo []

echo Building single output file: %OUT_FILE%

call :fetchOne 5915 "Property kick" personal
call :fetchOne 5916 "Property kick receive" personal
call :fetchOne 5963 "Education complete" personal
call :fetchOne 2051 "Item finish book" personal
call :fetchOne 2052 "Item finish book strength increase" personal
call :fetchOne 2053 "Item finish book speed increase" personal
call :fetchOne 2054 "Item finish book defense increase" personal
call :fetchOne 2055 "Item finish book dexterity increase" personal
call :fetchOne 2056 "Item finish book working stats increase" personal
call :fetchOne 2057 "Item finish book list capacity increase" personal
call :fetchOne 2058 "Item finish book merit reset" personal
call :fetchOne 2059 "Item finish book drug addiction removal" personal
call :fetchOne 2120 "Item use parachute" personal
call :fetchOne 2130 "Item use skateboard" personal
call :fetchOne 2140 "Item use boxing gloves" personal
call :fetchOne 2150 "Item use dumbbells" personal
call :fetchOne 6215 "Job promote" company
call :fetchOne 6217 "Job fired" company
call :fetchOne 6260 "Company quit" company
call :fetchOne 6261 "Company fire send" company
call :fetchOne 6262 "Company fire receive" company
call :fetchOne 6267 "Company rank change send" company
call :fetchOne 6268 "Company rank change receive" company
call :fetchOne 6760 "Faction tree upgrade set" faction
call :fetchOne 6761 "Faction tree upgrade unset" faction
call :fetchOne 6762 "Faction tree upgrade restore" faction
call :fetchOne 6763 "Faction tree upgrade unset entire branch" faction
call :fetchOne 6764 "Faction tree upgrade restore entire branch" faction
call :fetchOne 6765 "Faction tree branch select" faction
call :fetchOne 6766 "Faction tree war mode" faction
call :fetchOne 6767 "Faction tree optimize" faction
call :fetchOne 6800 "Faction create" faction
call :fetchOne 6830 "Faction change leader" faction
call :fetchOne 6831 "Faction change leader receive" faction
call :fetchOne 6832 "Faction change leader auto receive" faction
call :fetchOne 6833 "Faction change leader auto remove" faction
call :fetchOne 6835 "Faction change coleader" faction
call :fetchOne 6836 "Faction change coleader noone (legacy)" faction
call :fetchOne 6837 "Faction change coleader remove" faction
call :fetchOne 6838 "Faction change coleader receive" faction

powershell -NoProfile -Command "$entries = Get-Content -Raw '%TMP_ENTRIES%' | ConvertFrom-Json; $out = [ordered]@{ generatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); limit = [int]'%LIMIT%'; source = 'torn-v2-user-log'; entries = $entries }; $out | ConvertTo-Json -Depth 100 | Set-Content -NoNewline '%OUT_FILE%'"

del "%TMP_ENTRIES%" >nul 2>nul

echo Done: %OUT_FILE%
powershell -NoProfile -Command "$j=Get-Content -Raw '%OUT_FILE%' | ConvertFrom-Json; $j.entries | ForEach-Object { Write-Output ('log=' + $_.logTypeId + ' scope=' + $_.scope + ' status=' + $_.status + ' count=' + $_.count) }"
exit /b 0

:fetchOne
set "ID=%~1"
set "LABEL=%~2"
set "SCOPE=%~3"
set "RESP_FILE=%TEMP%\torn_resp_%ID%_%RANDOM%.json"
set "URL=%BASE_URL%?log=%ID%&limit=%LIMIT%&key=%API_KEY%"

curl -fsS "%URL%" -o "%RESP_FILE%"
if errorlevel 1 (
  echo WARN log=%ID% request failed
  powershell -NoProfile -Command "$e = Get-Content -Raw '%TMP_ENTRIES%' | ConvertFrom-Json; $row = [ordered]@{ logTypeId = [int]'%ID%'; label = '%LABEL%'; scope = '%SCOPE%'; status = 'request_error'; count = 0; response = $null }; $e = @($e) + $row; $e | ConvertTo-Json -Depth 100 | Set-Content -NoNewline '%TMP_ENTRIES%'"
  goto :eof
)

powershell -NoProfile -Command "$p='%RESP_FILE%'; $j=Get-Content -Raw $p | ConvertFrom-Json; function Scrub($n){ if($null -eq $n){ return $null }; if($n -is [System.Collections.IDictionary]){ foreach($k in @($n.Keys)){ $kl=($k.ToString().ToLower()); if($kl -eq 'anonymousid' -or $kl -eq 'tornid' -or $kl -match '(^|_|-)(user|player|sender|target|opponent|member|attacker|defender)id(s)?$'){ $n[$k]='REDACTED' } else { $n[$k]=Scrub $n[$k] } }; return $n }; if($n -is [System.Collections.IEnumerable] -and -not ($n -is [string])){ $arr=@(); foreach($i in $n){ $arr += ,(Scrub $i) }; return $arr }; return $n }; $clean=Scrub $j; $count=0; if($clean.log -is [System.Array]){ $count=$clean.log.Count }; $e = Get-Content -Raw '%TMP_ENTRIES%' | ConvertFrom-Json; $row = [ordered]@{ logTypeId = [int]'%ID%'; label = '%LABEL%'; scope = '%SCOPE%'; status = 'ok'; count = $count; response = $clean }; $e = @($e) + $row; $e | ConvertTo-Json -Depth 100 | Set-Content -NoNewline '%TMP_ENTRIES%'; Write-Output ('OK log=%ID% scope=%SCOPE% count=' + $count)"

del "%RESP_FILE%" >nul 2>nul
goto :eof

:usage
echo Usage:
echo   scripts\helpers\fetch-underused-log-samples.bat
echo   scripts\helpers\fetch-underused-log-samples.bat [--api-key key] [--limit 100] [--out .\torn-log-samples.json]
exit /b 2
