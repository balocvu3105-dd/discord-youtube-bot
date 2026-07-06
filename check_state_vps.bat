@echo off
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && echo === live_state.json === && cat data/live_state.json && echo. && echo === last_video_state.json === && cat data/last_video_state.json" > "D:\source code\YouTubeDiscordBot\vps_state.txt" 2>&1
echo Done. Check vps_state.txt
pause
