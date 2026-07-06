@echo off
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && docker compose logs --tail=200 bot" > "D:\source code\YouTubeDiscordBot\vps_log200.txt" 2>&1
echo Done. Check vps_log200.txt
pause
