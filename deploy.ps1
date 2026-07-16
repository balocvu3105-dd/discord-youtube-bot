# ============================================================
#  deploy.ps1 — Commit, push GitHub, deploy VPS (Docker)
#  Chạy: Right-click → "Run with PowerShell" HOẶC
#         .\deploy.ps1 trong PowerShell tại thư mục repo
# ============================================================

# ── Cấu hình VPS ────────────────────────────────────────────
# Cho phép override từ biến môi trường (ví dụ: $env:VPS_IP) để tránh hardcode IP khi public repo
$VPS_USER    = if ($env:VPS_USER) { $env:VPS_USER } else { "root" }
$VPS_IP      = if ($env:VPS_IP) { $env:VPS_IP } else { "103.77.243.86" }
# ────────────────────────────────────────────────────────────

$REPO = "D:\source code\YouTubeDiscordBot"
$COMMIT_MSG = @'
fix: resolve NoneType crash in tiktok_check and force container recreation on deploy

Bug Fixes & Hardening:
- tiktok_check.py: handle NoneType, KeyError, and AttributeError responses when checking live status to prevent crashes and exit code 1 spam.
- TikTokCheckerBackgroundService: catch temporary network/timeout errors specifically without logging full stack traces.
- deploy.ps1: add --force-recreate to docker compose up so new code updates are guaranteed to apply on the VPS container during deployment.
'@

Set-Location $REPO

Write-Host "`n[1/5] Xoa git lock neu con ton tai..." -ForegroundColor Cyan
$lockFile = Join-Path $REPO ".git\index.lock"
if (Test-Path $lockFile) {
    Remove-Item $lockFile -Force
    Write-Host "     Da xoa index.lock" -ForegroundColor Yellow
} else {
    Write-Host "     Khong co lock file" -ForegroundColor Green
}

Write-Host "`n[2/5] Git add all changes..." -ForegroundColor Cyan
git add -A

if ($LASTEXITCODE -ne 0) { Write-Host "Git add that bai!" -ForegroundColor Red; exit 1 }

Write-Host "`n[3/5] Git commit..." -ForegroundColor Cyan
git commit -m $COMMIT_MSG

if ($LASTEXITCODE -ne 0) { Write-Host "Git commit that bai!" -ForegroundColor Red; exit 1 }

Write-Host "`n[4/5] Git push..." -ForegroundColor Cyan
git push origin main

if ($LASTEXITCODE -ne 0) { Write-Host "Git push that bai!" -ForegroundColor Red; exit 1 }

Write-Host "`n[5/5] Deploy len VPS..." -ForegroundColor Cyan

# Update appsettings.json tren VPS (them TanCataww channel ID neu chua co)
# Sau do git pull va rebuild Docker
$VPS_COMMANDS = @'
cd /root/bot/discord-youtube-bot

# Them TikTok config vao appsettings.json neu chua co
if ! grep -q 'TikTokUsernames' appsettings.json 2>/dev/null; then
    python3 - <<'PYEOF'
import json, sys
with open('appsettings.json', 'r', encoding='utf-8') as f:
    cfg = json.load(f)
bc = cfg.setdefault('BotConfiguration', {})
bc.setdefault('TikTokUsernames', ['catawuwa'])
bc.setdefault('TikTokLiveChannelId', 1491472477384081541)
bc.setdefault('TikTokLiveRoleId', 1505506790757105704)
bc.setdefault('TikTokCheckIntervalSeconds', 60)
bc.setdefault('TikTokLiveStateFilePath', 'data/tiktok_live_state.json')
with open('appsettings.json', 'w', encoding='utf-8') as f:
    json.dump(cfg, f, indent=2, ensure_ascii=False)
print('  [OK] Da them TikTok config vao appsettings.json')
PYEOF
else
    echo '  [SKIP] TikTok config da co san'
fi

git stash && git pull origin main && docker compose up --build --force-recreate -d && echo '=== Deploy OK ==='
'@

ssh "${VPS_USER}@${VPS_IP}" $VPS_COMMANDS

if ($LASTEXITCODE -ne 0) { Write-Host "Deploy VPS that bai!" -ForegroundColor Red; exit 1 }

Write-Host "`n OK Hoan thanh!" -ForegroundColor Green
