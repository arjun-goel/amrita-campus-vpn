@echo off
title Amrita Campus Bridge - Hosteller Wi-Fi Relay Agent
cd /d "%~dp0"
echo ====================================================================
echo  AMRITA CAMPUS BRIDGE - HOSTELLER RELAY LAUNCHER
echo  Make sure your laptop is connected to AMRITA COLLEGE WI-FI!
echo ====================================================================
set /p HNAME="Enter your Name / Hostel Block (default: Campus-Hosteller): "
if "%HNAME%"=="" set HNAME=Campus-Hosteller
set /p HDUR="Enter duration in minutes to share Campus Wi-Fi (default: 30): "
if "%HDUR%"=="" set HDUR=30
set /p HCODE="Enter Day Scholar Session Code if approving one (or press Enter to auto-match/create): "

if "%HCODE%"=="" (
  node hosteller-agent/agent.js --name "%HNAME%" --duration %HDUR%
) else (
  node hosteller-agent/agent.js --name "%HNAME%" --duration %HDUR% --session "%HCODE%"
)
pause
