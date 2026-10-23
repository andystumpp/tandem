using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskList.Core;

namespace TaskList.App;

/// <summary>The AI-news ticker in the title bar.</summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _newsTimer;
    private List<NewsItem> _news = new();
    private int _newsIndex;
    private DateTime _newsFetchedUtc;
    private bool _newsFetching;

    private void InitNews()
    {
        if (_newsTimer == null)
        {
            _newsTimer = DispatcherQueue.CreateTimer();
            _newsTimer.Interval = TimeSpan.FromSeconds(9);
            _newsTimer.Tick += (_, _) => RotateNews();
            _newsTimer.Start();

            var menu = new MenuFlyout();
            menu.Items.Add(MenuItem("Refresh headlines", "", () => RefreshNews(force: true)));
            menu.Items.Add(MenuItem("Pause the news ticker", "", () =>
            {
                _prefs.ShowNews = false;
                _prefs.Save();
                InitNews();
            }));
            NewsPill.ContextFlyout = menu;
        }
        if (!_prefs.ShowNews)
        {
            NewsPill.Visibility = Visibility.Collapsed;
            UpdateTitleBarPassthrough();
            return;
        }
        (_news, _newsFetchedUtc) = NewsService.LoadCached();
        ShowNewsItem(animate: false);
        RefreshNews(force: false);
    }

    private async void RefreshNews(bool force)
    {
        if (!_prefs.ShowNews || _newsFetching) return;
        if (!force && DateTime.UtcNow - _newsFetchedUtc < NewsService.RefreshInterval) return;
        _newsFetching = true;
        try
        {
            var items = await Task.Run(() => NewsService.FetchAsync());
            if (items.Count > 0)
            {
                _news = items;
                _newsIndex = 0;
                ShowNewsItem(animate: true);
                if (_newsPanelOpen) BuildNewsPanel();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // The ticker is a nice-to-have: try again at the next interval.
            if (force) ShowStatus("Couldn't refresh the news right now");
        }
        finally
        {
            _newsFetchedUtc = DateTime.UtcNow;
            _newsFetching = false;
        }
    }

    private void RotateNews()
    {
        if (_news.Count > 1 && _prefs.ShowNews)
        {
            _newsIndex = (_newsIndex + 1) % _news.Count;
            ShowNewsItem(animate: true);
        }
        RefreshNews(force: false);
    }

    private void ShowNewsItem(bool animate)
    {
        if (!_prefs.ShowNews || _news.Count == 0)
        {
            NewsPill.Visibility = Visibility.Collapsed;
            UpdateTitleBarPassthrough();
            return;
        }
        var item = _news[Math.Min(_newsIndex, _news.Count - 1)];
        NewsText.Text = item.Headline;
        ToolTipService.SetToolTip(NewsPill, (item.Url == null ? item.Headline : $"{item.Headline}\n{item.Url}") + "\nRight-click to refresh or pause");
        NewsPill.Visibility = Visibility.Visible;
        if (animate) Motion.EnterFromBelow(NewsText, 6, 0, 360);
    }

    /// <summary>The ticker opens the news panel; each headline there links to its source.</summary>
    private void OnNewsClick(object sender, RoutedEventArgs e) => ToggleNewsPanel();
}
