@echo off
chcp 65001 >nul
title 园区AR巡检 - React 管理端 (5173)
cd /d "%~dp0"

if not exist node_modules (
  echo.
  echo 首次运行，正在安装前端依赖，大约需要 1 分钟 ...
  echo.
  call npm install
)

echo.
echo ====================================================
echo   开发服务器启动中：http://localhost:5173
echo   看到 Local: 后，用浏览器打开上面的地址即可。
echo   注意：这个黑窗口不能关闭，最小化即可。
echo ====================================================
echo.

call npm run dev
pause
