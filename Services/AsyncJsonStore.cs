using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Base class dùng chung cho tất cả JSON persistence services.
///
/// Vấn đề cũ:
///   LiveStateService và PersistenceService đều có code đọc/ghi file gần
///   giống nhau, nhưng KHÔNG có lock → race condition khi 2 background
///   services cùng ghi file đúng lúc.
///
/// Giải pháp:
///   SemaphoreSlim(1,1) là một "mutex" cho async code.
///   - WaitAsync() = "tôi muốn vào vùng critical, chờ nếu có người đang dùng"
///   - Release()   = "tôi xong rồi, người tiếp theo vào được"
///   - try/finally = đảm bảo Release() luôn được gọi dù có exception
///
/// Pattern này gọi là "Async Lock" — rất phổ biến trong production .NET code.
/// </summary>
public abstract class AsyncJsonStore<T> where T : class, new()
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
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

            return JsonSerializer.Deserialize<T>(json) ?? new T();
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
            // Tạo thư mục nếu chưa có (tránh DirectoryNotFoundException)
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(data, _jsonOptions);

            // Ghi vào file temp trước, rồi rename → atomic write
            // Tránh file bị corrupt nếu app crash giữa chừng khi đang ghi
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