@echo off
echo [VPS Deploy] git pull + docker rebuild...
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && git stash && git pull origin main && docker compose up --build -d && echo === Deploy OK ==="
if %errorlevel% neq 0 (echo Deploy that bai! & pause & exit /b 1)
echo.
echo === Hoan thanh! ===
pause
