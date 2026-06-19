using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Base class dùng chung cho tất cả JSON persistence services.
///
/// Thread-safety:
///   SemaphoreSlim(1,1) là async mutex — đảm bảo chỉ một coroutine đọc/ghi file tại một thời điểm.
///   Ghi qua file temp rồi rename → atomic write, tránh corrupt nếu app crash giữa chừng.
///
/// JSON options:
///   PropertyNameCaseInsensitive = true — đọc đúng ngay cả khi file cũ có casing khác (e.g. camelCase vs PascalCase).
///   WriteIndented = true — dễ đọc khi debug.
/// </summary>
public abstract class AsyncJsonStore<T> where T : class, new()
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    protected abstract string FilePath { get; }
    protected abstract ILogger Logger { get; }

    protected async Task<T> ReadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(FilePath))
                return new T();

            var json = await File.ReadAllTextAsync(FilePath);

            if (string.IsNullOrWhiteSpace(json))
                return new T();

            return JsonSerializer.Deserialize<T>(json, ReadOptions) ?? new T();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Không đọc được file {Path}, dùng default", FilePath);
            return new T();
        }
        finally
        {
            _lock.Release();
        }
    }

    protected async Task WriteAsync(T data)
    {
        await _lock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, WriteOptions);

            // Ghi vào file temp rồi rename → atomic write
            var tempPath = FilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Không ghi được file {Path}", FilePath);
        }
        finally
        {
            _lock.Release();
        }
    }
}
