@echo off
echo [1/2] Copy .env len VPS...
scp "D:\source code\YouTubeDiscordBot\.env" root@103.77.243.86:/root/bot/discord-youtube-bot/.env
if %errorlevel% neq 0 (echo SCP that bai! & pause & exit /b 1)

echo [2/2] Restart bot...
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && docker compose up -d && echo === Restart OK ==="
if %errorlevel% neq 0 (echo Restart that bai! & pause & exit /b 1)

echo.
echo === Hoan thanh! Token da duoc cap nhat ===
pause
