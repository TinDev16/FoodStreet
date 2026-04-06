using FoodStreetMobile.ViewModels;

namespace FoodStreetMobile;

public sealed class PoiViewHistoryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MonthHeaderTemplate { get; set; }
    public DataTemplate? DayHeaderTemplate { get; set; }
    public DataTemplate? EntryTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not PoiViewHistoryListItem listItem)
        {
            return EntryTemplate ?? new DataTemplate();
        }

        return listItem.Kind switch
        {
            PoiViewHistoryItemKind.MonthHeader => MonthHeaderTemplate ?? new DataTemplate(),
            PoiViewHistoryItemKind.DayHeader => DayHeaderTemplate ?? new DataTemplate(),
            _ => EntryTemplate ?? new DataTemplate()
        };
    }
}

