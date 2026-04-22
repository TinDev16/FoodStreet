using FoodStreetMobile.Localization;
using FoodStreetMobile.Services;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FoodStreetMobile.ViewModels;

public sealed class PoiViewHistoryViewModel : INotifyPropertyChanged
{
    private readonly PoiViewHistoryService _historyService;
    private readonly DeepLinkService _deepLinkService;
    private bool _isEmpty;

    public PoiViewHistoryViewModel(PoiViewHistoryService historyService, DeepLinkService deepLinkService)
    {
        _historyService = historyService;
        _deepLinkService = deepLinkService;

        Items = new ObservableCollection<PoiViewHistoryListItem>();
        RefreshCommand = new Command(async () => await RefreshAsync());
        OpenPoiCommand = new Command<PoiViewHistoryListItem>(async item => await OpenPoiAsync(item));
        ClearHistoryCommand = new Command(async () => await ClearHistoryAsync(), () => !IsEmpty);
    }

    public ObservableCollection<PoiViewHistoryListItem> Items { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenPoiCommand { get; }
    public ICommand ClearHistoryCommand { get; }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (_isEmpty == value)
            {
                return;
            }

            _isEmpty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotEmpty));
            ((Command)ClearHistoryCommand).ChangeCanExecute();
        }
    }

    public bool IsNotEmpty => !IsEmpty;

    public async Task RefreshAsync()
    {
        IReadOnlyList<Models.PoiViewHistoryEntity> history;
        try
        {
            history = await _historyService.GetRecentAsync(PoiViewHistoryService.GuestUserId, limit: 250);
        }
        catch
        {
            history = Array.Empty<Models.PoiViewHistoryEntity>();
        }

        var list = BuildItems(history);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Items.Clear();
            foreach (var item in list)
            {
                Items.Add(item);
            }

            IsEmpty = history.Count == 0;
        });
    }

    private static List<PoiViewHistoryListItem> BuildItems(IReadOnlyList<Models.PoiViewHistoryEntity> history)
    {
        var result = new List<PoiViewHistoryListItem>(history.Count + 12);
        var culture = CultureInfo.CurrentCulture;

        int? currentMonth = null;
        int? currentYear = null;
        DateOnly? currentDay = null;

        foreach (var entry in history.OrderByDescending(x => x.ViewedAtUtcTicks))
        {
            var local = new DateTime(entry.ViewedAtUtcTicks, DateTimeKind.Utc).ToLocalTime();
            var month = local.Month;
            var year = local.Year;
            var day = DateOnly.FromDateTime(local);

            if (currentMonth != month || currentYear != year)
            {
                currentMonth = month;
                currentYear = year;
                currentDay = null;

                result.Add(new PoiViewHistoryListItem
                {
                    Kind = PoiViewHistoryItemKind.MonthHeader,
                    Title = local.ToString("MMMM yyyy", culture)
                });
            }

            if (currentDay != day)
            {
                currentDay = day;
                result.Add(new PoiViewHistoryListItem
                {
                    Kind = PoiViewHistoryItemKind.DayHeader,
                    Title = local.ToString("d", culture)
                });
            }

            result.Add(new PoiViewHistoryListItem
            {
                Kind = PoiViewHistoryItemKind.Entry,
                PoiId = entry.PoiId,
                PoiName = entry.PoiName,
                PoiImageUrl = entry.PoiImageUrl,
                ViewedTimeText = local.ToString("t", culture)
            });
        }

        return result;
    }

    private async Task OpenPoiAsync(PoiViewHistoryListItem? item)
    {
        if (item is null || item.Kind != PoiViewHistoryItemKind.Entry || string.IsNullOrWhiteSpace(item.PoiId))
        {
            return;
        }

        try
        {
            _deepLinkService.QueuePendingPoiLink(new PendingPoiLink { PoiId = item.PoiId });
            if (Shell.Current is AppShell shell)
            {
                shell.NavigateToMainTabsTab(1);
            }
        }
        catch
        {
        }
    }

    private async Task ClearHistoryAsync()
    {
        if (IsEmpty) return;

        if (Shell.Current == null) return;

        bool confirm = await Shell.Current.DisplayAlert(
            LocalizationResourceManager.Instance["PoiHistory_Clear"],
            LocalizationResourceManager.Instance["PoiHistory_ClearConfirm"],
            LocalizationResourceManager.Instance["General_Yes"],
            LocalizationResourceManager.Instance["General_No"]);

        if (!confirm) return;

        try
        {
            await _historyService.ClearAsync(PoiViewHistoryService.GuestUserId);
            await RefreshAsync();
        }
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
