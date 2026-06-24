using CommunityToolkit.Mvvm.ComponentModel;
using ImageVault.Models;

namespace ImageVault.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string SortMetricKey = "settings_sort_metric";
    private const string FilterThresholdKey = "settings_filter_threshold";
    private const string ThemeKey = "settings_theme";

    [ObservableProperty]
    private SortMetric _sortMetric;

    [ObservableProperty]
    private double _filterThreshold;

    [ObservableProperty]
    private int _selectedThemeIndex;

    public List<SortMetric> SortMetrics { get; } =
    [
        SortMetric.Relevance,
        SortMetric.Recency,
        SortMetric.FileSize
    ];

    public List<string> ThemeOptions { get; } =
    [
        "System default",
        "Light",
        "Dark"
    ];

    public SettingsViewModel()
    {
        _sortMetric = LoadSortMetric();
        _filterThreshold = LoadFilterThreshold();
        _selectedThemeIndex = LoadThemeIndex();
    }

    partial void OnSortMetricChanged(SortMetric value)
    {
        Preferences.Set(SortMetricKey, (int)value);
    }

    partial void OnFilterThresholdChanged(double value)
    {
        Preferences.Set(FilterThresholdKey, value);
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        Preferences.Set(ThemeKey, value);
        ApplyTheme(value);
    }

    private static SortMetric LoadSortMetric()
    {
        var val = Preferences.Get(SortMetricKey, (int)SortMetric.Relevance);
        return Enum.IsDefined(typeof(SortMetric), val) ? (SortMetric)val : SortMetric.Relevance;
    }

    private static double LoadFilterThreshold()
    {
        return Preferences.Get(FilterThresholdKey, 0.0);
    }

    private static int LoadThemeIndex()
    {
        return Preferences.Get(ThemeKey, 0);
    }

    private static void ApplyTheme(int index)
    {
        Application.Current!.UserAppTheme = index switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }
}
