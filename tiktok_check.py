"""
tiktok_check.py — Check TikTok live status using TikTokLive v6.x.

TikTokLive v6+ API thay đổi hoàn toàn so với v4:
  - v4: client.connect(fetch_room_info=True) → raise UserOfflineError nếu offline
  - v6: client.is_live() → trả về bool trực tiếp, KHÔNG cần connect WebSocket

Usage:
    python tiktok_check.py <username>

Stdout:
    LIVE    → user đang live
    OFFLINE → không live hoặc không xác định được
"""

import asyncio
import sys


def main():
    if len(sys.argv) < 2:
        print("OFFLINE")
        return

    username = sys.argv[1].lstrip("@")
    asyncio.run(_check(username))


async def _check(username: str):
    try:
        from TikTokLive import TikTokLiveClient
    except ImportError:
        print("ERROR: TikTokLive not installed. Run: pip install TikTokLive==6.6.5", file=sys.stderr)
        print("OFFLINE")
        return

    client = TikTokLiveClient(unique_id=f"@{username}")

    try:
        # TikTokLive v6.x: is_live() kiểm tra trực tiếp qua API, không cần WebSocket
        # Gọi endpoint: GET /api-live/user/room/?uniqueId=<username>
        # Trả về True nếu liveRoom.status != 4 (status 4 = offline)
        is_live: bool = await client.is_live()
        print("LIVE" if is_live else "OFFLINE")

    except Exception as e:
        err_type = type(e).__name__
        err_msg = str(e).lower()

        # Phân loại lỗi thường gặp
        if any(kw in err_msg for kw in [
            "user_not_found", "not found", "not capable",
            "offline", "not live", "not broadcasting"
        ]):
            print("OFFLINE")
        else:
            # Log để debug nhưng vẫn trả OFFLINE để bot không crash
            print(f"ERROR [{err_type}]: {e}", file=sys.stderr)
            print("OFFLINE")


if __name__ == "__main__":
    main()
