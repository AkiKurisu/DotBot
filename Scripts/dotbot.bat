@echo off
REM DotBot Wrapper Script
REM This script calls DotBot.exe from the same directory

setlocal
set "SCRIPT_DIR=%~dp0"
"%SCRIPT_DIR%DotBot.exe" %*
