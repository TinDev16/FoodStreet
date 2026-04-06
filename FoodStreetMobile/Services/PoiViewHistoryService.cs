using FoodStreetMobile.Models;

namespace FoodStreetMobile.Services;

public sealed class PoiViewHistoryService
{
    private readonly AppDatabase _database;

    public PoiViewHistoryService(AppDatabase database)
    {
        _database = database;
    }

    public async Task RecordViewedAsync(string poiId, string poiName, string? poiImageUrl, DateTime? viewedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(poiId) || string.IsNullOrWhiteSpace(poiName))
        {
            return;
        }

        var nowUtc = viewedAtUtc ?? DateTime.UtcNow;
        var conn = await _database.GetConnectionAsync();

        var last = await conn.Table<PoiViewHistoryEntity>()
            .Where(x => x.PoiId == poiId)
            .OrderByDescending(x => x.ViewedAtUtcTicks)
            .FirstOrDefaultAsync();

        var nowTicks = nowUtc.Ticks;
        if (last is not null)
        {
            var lastViewedUtc = new DateTime(last.ViewedAtUtcTicks, DateTimeKind.Utc);
            if ((nowUtc - lastViewedUtc).TotalMinutes <= 5)
            {
                last.PoiName = poiName;
                last.PoiImageUrl = poiImageUrl;
                last.ViewedAtUtcTicks = nowTicks;
                await conn.UpdateAsync(last);
                return;
            }
        }

        await conn.InsertAsync(new PoiViewHistoryEntity
        {
            PoiId = poiId.Trim(),
            PoiName = poiName.Trim(),
            PoiImageUrl = poiImageUrl,
            ViewedAtUtcTicks = nowTicks
        });

        await TrimAsync(conn, maxRows: 300);
    }

    public async Task<IReadOnlyList<PoiViewHistoryEntity>> GetRecentAsync(int limit = 200)
    {
        limit = Math.Clamp(limit, 1, 1000);
        var conn = await _database.GetConnectionAsync();
        var list = await conn.Table<PoiViewHistoryEntity>()
            .OrderByDescending(x => x.ViewedAtUtcTicks)
            .Take(limit)
            .ToListAsync();

        return list;
    }

    public async Task ClearAsync()
    {
        var conn = await _database.GetConnectionAsync();
        await conn.DeleteAllAsync<PoiViewHistoryEntity>();
    }

    private static async Task TrimAsync(SQLite.SQLiteAsyncConnection conn, int maxRows)
    {
        var count = await conn.Table<PoiViewHistoryEntity>().CountAsync();
        if (count <= maxRows)
        {
            return;
        }

        var overflow = count - maxRows;
        var oldRows = await conn.Table<PoiViewHistoryEntity>()
            .OrderBy(x => x.ViewedAtUtcTicks)
            .Take(overflow)
            .ToListAsync();

        if (oldRows.Count == 0)
        {
            return;
        }

        await conn.RunInTransactionAsync(db =>
        {
            foreach (var row in oldRows)
            {
                db.Delete(row);
            }
        });
    }
}

