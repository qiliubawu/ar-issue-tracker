@echo off
chcp 65001 >nul
title 园区AR巡检 - 一键启动
cd /d "%~dp0"

echo ====================================================
echo   园区巡检 AR 原型 - 一键启动
echo   会打开两个窗口：Go 后端(8080) 与 React 管理端(5173)
echo   两个窗口都不能关闭，最小化即可。
echo ====================================================
echo.

start "Go后端 8080" cmd /k "chcp 65001 >nul && cd /d "%~dp0backend" && go run main.go"
timeout /t 2 /nobreak >nul
start "React管理端 5173" cmd /k "chcp 65001 >nul && cd /d "%~dp0frontend" && npm run dev"
timeout /t 6 /nobreak >nul

start "" http://localhost:5173
exit
