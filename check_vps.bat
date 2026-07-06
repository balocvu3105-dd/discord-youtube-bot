@echo off
echo === Docker status ===
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && docker compose ps && echo. && echo === Log 50 dong cuoi === && docker compose logs --tail=50 bot"
pause
