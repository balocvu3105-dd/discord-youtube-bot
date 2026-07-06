using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using YouTubeDiscordBot.Config;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Kiểm tra TikTok live status bằng cách gọi Python script (tiktok_check.py)
/// sử dụng thư viện TikTokLive — tự quản lý session, không cần cookie thủ công.
///
/// Yêu cầu:
///   - Python 3 đã được cài trong container/system
///   - pip install TikTokLive
///   - tiktok_check.py phải nằm cùng thư mục với bot (hoặc đường dẫn trong TikTokScriptPath)
/// </summary>
public class TikTokService : ITikTokService
{
    private readonly ILogger<TikTokService> _logger;
    private readonly BotConfiguration _config;

    // Đường dẫn đến script Python — có thể override qua config nếu cần
    private string ScriptPath => string.IsNullOrEmpty(_config.TikTokScriptPath)
        ? Path.Combine(AppContext.BaseDirectory, "tiktok_check.py")
        : _config.TikTokScriptPath;

    public TikTokService(
        IOptions<BotConfiguration> config,
        ILogger<TikTokService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<bool> IsLiveAsync(string username)
    {
        _logger.LogInformation("TikTok @{Username} — chạy tiktok_check.py...", username);

        var result = await RunPythonScriptAsync(username);

        _logger.LogInformation("TikTok @{Username} — kết quả: {Result}", username, result.Output);

        if (!string.IsNullOrEmpty(result.Stderr))
            _logger.LogWarning("TikTok @{Username} — stderr: {Stderr}", username, result.Stderr);

        return result.Output.Trim().Equals("LIVE", StringComparison.OrdinalIgnoreCase);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<(string Output, string Stderr)> RunPythonScriptAsync(string username)
    {
        // Thử python3 trước (Linux/Docker), fallback sang python (Windows)
        var pythonExecutable = await FindPythonAsync();

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add(username);

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        // Timeout 30 giây để tránh hang vô hạn
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout: kill process để tránh zombie, rồi rethrow
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Exit code != 0 → lỗi mạng/timeout từ script, ném exception
        // để caller bỏ qua lần check này và KHÔNG reset live state.
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"tiktok_check.py exited with code {process.ExitCode}: {stderr.Trim()}");

        return (stdout, stderr);
    }

    private static string? _cachedPython;

    private static async Task<string> FindPythonAsync()
    {
        if (_cachedPython is not null) return _cachedPython;

        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                using var probe = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                probe.Start();
                await probe.WaitForExitAsync();
                if (probe.ExitCode == 0)
                {
                    _cachedPython = candidate;
                    return candidate;
                }
            }
            catch { /* thử candidate tiếp theo */ }
        }

        _cachedPython = "python3"; // fallback mặc định
        return _cachedPython;
    }
}
