namespace FoodStreetMobile.ViewModels;

public enum PoiViewHistoryItemKind
{
    MonthHeader = 0,
    DayHeader = 1,
    Entry = 2
}

public sealed class PoiViewHistoryListItem
{
    public required PoiViewHistoryItemKind Kind { get; init; }

    public string Title { get; init; } = string.Empty;

    public string PoiId { get; init; } = string.Empty;

    public string PoiName { get; init; } = string.Empty;

    public string? PoiImageUrl { get; init; }

    public string ViewedTimeText { get; init; } = string.Empty;
}

