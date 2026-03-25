using FoodStreetMobile.Localization;
using FoodStreetMobile.Services;
using System.Collections.ObjectModel;

namespace FoodStreetMobile;

public partial class HomePage : ContentPage
{
    private readonly AppLanguageService _languageService;
    private readonly ObservableCollection<string> _featuredPlaces = new();

    public HomePage(AppLanguageService languageService)
    {
        InitializeComponent();
        _languageService = languageService;
        FeaturedPlacesCollection.ItemsSource = _featuredPlaces;

        RefreshFeaturedPlaces();
        _languageService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(string _)
    {
        MainThread.BeginInvokeOnMainThread(RefreshFeaturedPlaces);
    }

    private void RefreshFeaturedPlaces()
    {
        _featuredPlaces.Clear();
        _featuredPlaces.Add(LocalizationResourceManager.Instance["Home_FeaturedItem1"]);
        _featuredPlaces.Add(LocalizationResourceManager.Instance["Home_FeaturedItem2"]);
        _featuredPlaces.Add(LocalizationResourceManager.Instance["Home_FeaturedItem3"]);
        _featuredPlaces.Add(LocalizationResourceManager.Instance["Home_FeaturedItem4"]);
    }
}

