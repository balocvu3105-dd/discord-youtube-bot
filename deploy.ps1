# ============================================================
#  deploy.ps1 — Commit, push GitHub, deploy VPS (Docker)
#  Chạy: Right-click → "Run with PowerShell" HOẶC
#         .\deploy.ps1 trong PowerShell tại thư mục repo
# ============================================================

# ── Cấu hình VPS ────────────────────────────────────────────
$VPS_USER    = "root"
$VPS_IP      = "103.77.243.86"
$PROJECT_DIR = "/root/bot/discord-youtube-bot"
# ────────────────────────────────────────────────────────────

$REPO = "D:\source code\YouTubeDiscordBot"
$COMMIT_MSG = "feat: add TanCataww channel + refactor & optimize

- Add YouTube channel TanCataww (UCS8tTU195JRDUbuagiIwvgQ) to YoutubeChannelIds
- Models: tach Models.cs thanh 5 file rieng (BotState, VideoInfo, ShopGameConfig, ShopMessageState, LdShopPromo)
- ShopService: extract helpers, validate URL affiliate
- LdShopScraperService: IHttpClientFactory, fix _nameCache thread safety
- ShopCommandModule: fix race condition, an ex.Message, RequireContext(Guild), cooldown 60s
- LdShopDiscountService: parallel WarmCacheAsync (Task.WhenAll), CancellationToken
- LootbarDiscountService: CancellationToken, pass ct xuong HttpClient
- AsyncJsonStore: PropertyNameCaseInsensitive, tach ReadOptions/WriteOptions
- ShopBackgroundService: log exception trong UpsertMessageAsync
- DiscordService: StatusChannelId startup notification
- Log noise: downgrade API response logs xuong LogDebug"

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
git add -u
git add deploy.ps1

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
$VPS_COMMANDS = @"
cd $PROJECT_DIR

# Them TanCataww channel ID vao appsettings.json neu chua co
if ! grep -q 'UCS8tTU195JRDUbuagiIwvgQ' appsettings.json 2>/dev/null; then
    sed -i 's/"UCHfFNHHKK6phqWfordByyEQ"/"UCHfFNHHKK6phqWfordByyEQ",\n      "UCS8tTU195JRDUbuagiIwvgQ"/' appsettings.json
    echo '  [OK] Da them TanCataww channel ID vao appsettings.json'
else
    echo '  [SKIP] TanCataww channel ID da co san'
fi

git pull origin main && docker compose up --build -d && echo '=== Deploy OK ==='
"@

ssh "${VPS_USER}@${VPS_IP}" $VPS_COMMANDS

if ($LASTEXITCODE -ne 0) { Write-Host "Deploy VPS that bai!" -ForegroundColor Red; exit 1 }

Write-Host "`n OK Hoan thanh!" -ForegroundColor Green
