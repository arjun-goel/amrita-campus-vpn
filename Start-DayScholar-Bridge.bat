@echo off
title Amrita Campus Bridge - Day Scholar Cloud Proxy Bridge
cd /d "%~dp0"
echo Starting Day Scholar Local Proxy Bridge (connects Chrome Extension to https://amrita-campus-vpn.onrender.com)...
node day-scholar-bridge.js
pause
