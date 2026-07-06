@echo off
echo [1/2] Xoa 2RzM_OS0J2A khoi live_state.json tren VPS...
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && python3 -c \"import json; d=json.load(open('data/live_state.json')); d['States'].pop('2RzM_OS0J2A', None); json.dump(d, open('data/live_state.json','w'), indent=2); print('OK - entries:', len(d['States']))\""
if %errorlevel% neq 0 (echo That bai! & pause & exit /b 1)

echo [2/2] Restart bot de chay startup sync...
ssh root@103.77.243.86 "cd /root/bot/discord-youtube-bot && docker compose restart bot && echo === Restart OK ==="
if %errorlevel% neq 0 (echo That bai! & pause & exit /b 1)

echo.
echo === Hoan thanh! Bot se gui thong bao video sau khi restart ===
pause
