using System.Text.Json;

using Microsoft.Extensions.Logging;

using YouTubeDiscordBot.Models;

namespace YouTubeDiscordBot.Services;

/// <summary>
/// Chịu trách nhiệm đọc/ghi ShopMessageState.
/// </summary>
public class ShopMessagePersistenceService
{
	private const string FilePath =
		"data/shop_messages.json";

	private static readonly JsonSerializerOptions JsonOptions =
		new()
		{
			WriteIndented = true
		};

	private readonly ILogger<ShopMessagePersistenceService>
		_logger;

	public ShopMessagePersistenceService(
		ILogger<ShopMessagePersistenceService> logger)
	{
		_logger = logger;
	}

	// =====================================================
	// LOAD
	// =====================================================

	public async Task<ShopMessageState> LoadAsync()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				_logger.LogInformation(
					"📄 shop_messages.json chưa tồn tại → tạo state mới");

				return CreateDefaultState();
			}

			var json =
				await File.ReadAllTextAsync(FilePath);

			if (string.IsNullOrWhiteSpace(json))
			{
				return CreateDefaultState();
			}

			var state =
				JsonSerializer.Deserialize<ShopMessageState>(
					json);

			if (state is null)
			{
				return CreateDefaultState();
			}

			_logger.LogInformation(
				"✅ Loaded ShopMessageState: PinnedId={PinnedId}, GameCount={Count}",
				state.PinnedMessageId,
				state.GameMessageIds.Count);

			return state;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"❌ Không thể load shop_messages.json");

			return CreateDefaultState();
		}
	}

	// =====================================================
	// SAVE
	// =====================================================

	public async Task SaveAsync(
		ShopMessageState state)
	{
		try
		{
			EnsureDataDirectoryExists();

			var json =
				JsonSerializer.Serialize(
					state,
					JsonOptions);

			await File.WriteAllTextAsync(
				FilePath,
				json);

			_logger.LogInformation(
				"💾 Saved ShopMessageState: PinnedId={PinnedId}, GameCount={Count}",
				state.PinnedMessageId,
				state.GameMessageIds.Count);
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"❌ Không thể save shop_messages.json");
		}
	}

	// =====================================================
	// HELPERS
	// =====================================================

	private static ShopMessageState CreateDefaultState()
	{
		return new ShopMessageState();
	}

	private static void EnsureDataDirectoryExists()
	{
		const string dir = "data";

		if (!Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}
	}
}