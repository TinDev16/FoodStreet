using FoodStreetMobile.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace FoodStreetMobile.Services;

public sealed class AppDatabase
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "foodstreet.db3");
            var connection = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

            await connection.CreateTableAsync<PoiEntity>();
            await connection.CreateTableAsync<PoiTranslationEntity>();
            await connection.CreateTableAsync<PlaybackStateEntity>();
            await connection.CreateTableAsync<AppSettingEntity>();
            await connection.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_poi_lang ON poi_translations(poi_id, lang_code);");
            await CleanupLegacySeedAsync(connection);
            await SeedIfNeededAsync(connection);

            _connection = connection;
            return connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task SeedIfNeededAsync(SQLiteAsyncConnection connection)
    {
        var poiCount = await connection.Table<PoiEntity>().CountAsync();
        if (poiCount > 0)
        {
            return;
        }

        await connection.InsertOrReplaceAsync(new AppSettingEntity { Key = "current_language", Value = "vi" });
        await connection.InsertOrReplaceAsync(new AppSettingEntity { Key = "audio_cooldown_seconds", Value = "90" });
    }

    private static async Task CleanupLegacySeedAsync(SQLiteAsyncConnection connection)
    {
        var poiCount = await connection.Table<PoiEntity>().CountAsync();
        if (poiCount == 0 || poiCount > 2)
        {
            return;
        }

        var poi1 = await connection.FindAsync<PoiEntity>("poi-001");
        var poi2 = await connection.FindAsync<PoiEntity>("poi-002");
        if (poi1 is null || poi2 is null)
        {
            return;
        }

        var looksLikeLegacySeed =
            poi1.ImageUrl.Contains("photo-1555396273-367ea4eb4db5", StringComparison.OrdinalIgnoreCase) &&
            poi2.ImageUrl.Contains("photo-1517248135467-4c7edcad34c4", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeLegacySeed)
        {
            return;
        }

        await connection.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM poi_translations WHERE poi_id IN ('poi-001', 'poi-002');");
            conn.Execute("DELETE FROM pois WHERE id IN ('poi-001', 'poi-002');");
        });
    }
}
