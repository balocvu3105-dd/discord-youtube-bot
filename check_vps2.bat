@echo off
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && docker compose ps && echo === LOG === && docker compose logs --tail=80 bot" > "D:\source code\YouTubeDiscordBot\vps_log.txt" 2>&1
echo Done. Check vps_log.txt
pause
