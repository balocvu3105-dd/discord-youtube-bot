@echo off
cd /d "D:\source code\YouTubeDiscordBot"
echo [1/2] Git push...
git push origin main
if %errorlevel% neq 0 (echo Git push that bai! & pause & exit /b 1)

echo [2/2] Deploy len VPS...
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && git stash && git pull origin main && docker compose up --build -d && echo === Deploy OK ==="
if %errorlevel% neq 0 (echo Deploy that bai! & pause & exit /b 1)

echo.
echo === Hoan thanh! ===
pause
