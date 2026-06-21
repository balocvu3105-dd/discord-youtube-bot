"""
tiktok_check.py — Check TikTok live status using TikTokLive library.
Không cần cookie thủ công. Library tự quản lý session.

Usage:
    python tiktok_check.py <username>

Exit codes / stdout:
    LIVE    → user đang live
    OFFLINE → không live hoặc không xác định được
    ERROR   → lỗi xảy ra (vẫn trả OFFLINE để bot không crash)
"""

import asyncio
import sys
import os


def main():
    if len(sys.argv) < 2:
        print("OFFLINE")
        return

    username = sys.argv[1].lstrip("@")

    try:
        from TikTokLive import TikTokLiveClient
        from TikTokLive.client.errors import UserOfflineError, UserNotFoundError
    except ImportError:
        print("ERROR: TikTokLive not installed", file=sys.stderr)
        print("OFFLINE")
        return

    async def check():
        client = TikTokLiveClient(unique_id=username)
        try:
            # fetch_room_info=True lấy thông tin phòng mà không join WebSocket
            await client.connect(fetch_room_info=True)
            # Nếu connect thành công (không raise exception) → đang live
            print("LIVE")
        except UserOfflineError:
            print("OFFLINE")
        except UserNotFoundError:
            print(f"ERROR: User @{username} not found", file=sys.stderr)
            print("OFFLINE")
        except Exception as e:
            # TikTokLive raise exception khi không live trong một số version
            err_msg = str(e).lower()
            if "offline" in err_msg or "not live" in err_msg or "failed to retrieve" in err_msg:
                print("OFFLINE")
            else:
                print(f"ERROR: {e}", file=sys.stderr)
                print("OFFLINE")
        finally:
            try:
                await client.disconnect()
            except Exception:
                pass

    asyncio.run(check())


if __name__ == "__main__":
    main()
