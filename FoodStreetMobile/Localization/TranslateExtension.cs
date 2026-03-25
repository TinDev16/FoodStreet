using Microsoft.Maui.Controls;

namespace FoodStreetMobile.Localization;

[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return new Binding();
        }

        return new Binding($"[{Key}]", source: LocalizationResourceManager.Instance, mode: BindingMode.OneWay);
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ProvideValue(serviceProvider);
}

