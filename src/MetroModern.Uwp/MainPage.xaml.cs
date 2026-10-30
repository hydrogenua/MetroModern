using System;
using System.Collections.Generic;
using MetroModern.Uwp.Rendering;
using MetroModern.Uwp.Services;
using MetroCore;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Point = Windows.Foundation.Point;

namespace MetroModern.Uwp
{
    public sealed partial class MainPage : Page
    {
        private MetroTaskbar _service = new MetroTaskbar();
        private MetroNetwork _net;
        private SvgScheme _svg;

        private Station _from, _to;

        public MainPage()
        {
            InitializeComponent();
            CityPicker.SelectedIndex = 0;
        }

        private async void CityPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = CityPicker.SelectedIndex;
            string city = idx == 0 ? "saint-petersburg" : "moscow";
            Status.Text = "Загрузка схемы…";
            try
            {
                var result = await _service.GetSchemeAsync(city);
                _net = result.Item1;
                _svg = result.Item2;
                RenderScheme();
                Status.Text = $"{_net.CityRuName}: станций {_net.Stations.Count}, линий {_net.LinesById.Count}";
            }
            catch (Exception ex)
            {
                Status.Text = "Ошибка загрузки: " + ex.Message;
            }
        }

        private void RenderScheme()
        {
            MapCanvas.Children.Clear();
            if (_svg == null) return;
            SchemeRenderer.Render(_svg, MapCanvas, HighlightStations);
            FitScheme();
        }

        /// <summary>Scale to fit the whole scheme into the current cmap viewport.</summary>
        private void FitScheme()
        {
            if (_svg == null || MapCanvas.ActualWidth < 1 || MapCanvas.ActualHeight < 1) return;
            double zx = MapHost.ActualWidth / _svg.Width;
            double zy = MapHost.ActualHeight / _svg.Height;
            double z = Math.Min(zx, zy);
            MapTransform.ScaleX = z;
            MapTransform.ScaleY = z;
        }

        private Dictionary<string, Windows.UI.Xaml.Shapes.Path> HighlightStations;

        private void OnRouteClick(object sender, RoutedEventArgs e)
        {
            if (_net == null) return;
            OpenStationPicker();
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            _from = null; _to = null;
            RoutePanel.Visibility = Visibility.Collapsed;
            ClearRouteOverlay();
            Status.Text = "Сброшено";
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            _service.InvalidateCacheAsync();
            CityPicker_SelectionChanged(CityPicker, null);
        }

        private void MapHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var pt = e.GetCurrentPoint(MapHost);
            double factor = pt.Properties.MouseWheelDelta > 0 ? 1.25 : 1 / 1.25;
            SetScaleAroundPoint(pt.Position, Clamp(MapTransform.ScaleX * factor, 0.3, 6.0));
            e.Handled = true;
        }

        private void MapCanvas_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var t = MapTransform;
            // pinch
            if (e.Delta.Scale > 0.0001)
            {
                SetScaleAroundPoint(e.Position, Clamp(t.ScaleX * e.Delta.Scale, 0.3, 6.0));
            }
            // pan
            var tx = e.Delta.Translation;
            double panX = -tx.X, panY = -tx.Y;
            MapHost.Margin = new Thickness(MapHost.Margin.Left + panX, MapHost.Margin.Top + panY, 0, MapHost.Margin.Bottom);
            e.Handled = true;
        }

        private void SetScaleAroundPoint(Point p, double newScale)
        {
            double oldScale = MapTransform.ScaleX;
            if (Math.Abs(oldScale) < 1e-9) return;
            double ox = p.X - MapCanvas.Margin.Left;
            double oy = p.Y - MapCanvas.Margin.Top;
            MapTransform.ScaleX = newScale;
            MapTransform.ScaleY = newScale;
            double factor = newScale / oldScale;
            MapCanvas.Margin = new Thickness(p.X - ox * factor, p.Y - oy * factor, 0, 0);
        }

        private static double Clamp(double v, double min, double max) { return v < min ? min : (v > max ? max : v); }

        private void OpenStationPicker()
        {
            var dlg = new ContentDialog
            {
                Title = "Маршрут",
                PrimaryButtonText = "Проложить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Primary
            };
            var stack = new StackPanel { Spacing = 8 };

            var fromBox = new AutoSuggestBox { Header = "Откуда:", Text = _from != null ? _from.Name : "" };
            var toBox = new AutoSuggestBox { Header = "Куда:", Text = _to != null ? _to.Name : "" };

            fromBox.SuggestionsRequested += (s, a) =>
            {
                var match = Search(_net, a.QueryText);
                fromBox.ItemsSource = match;
            };
            toBox.SuggestionsRequested += (s, a) =>
            {
                var match = Search(_net, a.QueryText);
                toBox.ItemsSource = match;
            };
            fromBox.SuggestionChosen += (s, a) =>
            {
                var st = a.SelectedItem as Station;
                if (st != null) { fromBox.Text = st.Name; _from = st; }
            };
            toBox.SuggestionChosen += (s, a) =>
            {
                var st = a.SelectedItem as Station;
                if (st != null) { toBox.Text = st.Name; _to = st; }
            };

            stack.Children.Add(fromBox);
            stack.Children.Add(toBox);
            dlg.Content = stack;

            dlg.PrimaryButtonClick += (s, a) =>
            {
                if (_from == null || _to == null)
                {
                    a.Cancel = true;
                    Status.Text = "Выберите обе станции маршрута.";
                    return;
                }
                ShowRoute(_from, _to);
            };
            _ = dlg.ShowAsync();
        }

        private static List<Station> Search(MetroNetwork net, string query)
        {
            var list = new List<Station>();
            if (net == null) return list;
            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (var s in net.Stations) { list.Add(s); if (list.Count >= 12) break; }
                return list;
            }
            string n = query.Trim().Replace("ё", "е").ToLowerInvariant();
            foreach (var s in net.Stations)
            {
                if (string.IsNullOrEmpty(s.Name)) continue;
                var nn = s.Name.Replace("ё", "е").ToLowerInvariant();
                if (nn.Contains(n)) { list.Add(s); if (list.Count >= 12) break; }
            }
            return list;
        }

        private void ShowRoute(Station from, Station to)
        {
            var router = new Router();
            var r = router.Find(_net, from, to);
            RouteList.Children.Clear();
            if (r == null)
            {
                RouteList.Children.Add(new TextBlock { Text = "Маршрут не найден" });
                RoutePanel.Visibility = Visibility.Visible;
                return;
            }

            ClearRouteOverlay();

            RouteList.Children.Add(new TextBlock
            {
                Text = $"{from.Name} → {to.Name}",
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Windows.UI.Xaml.Thickness(0, 0, 0, 6)
            });

            foreach (var leg in r.legs())
            {
                if (leg.IsTransfer)
                {
                    RouteList.Children.Add(new TextBlock
                    {
                        Text = $"  Пересадка ({(int)(leg.Seconds / 60)} мин)",
                        Foreground = new SolidColorBrush(Windows.UI.Colors.Gray)
                    });
                }
                else
                {
                    _net.LinesById.TryGetValue(leg.LineId, out var li);
                    var c = ColorHelperLite.TryParseColor(li?.Color);
                    string seq = leg.Nodes.Count > 0
                        ? leg.Nodes[0].Station.Name + " → " + leg.Nodes[leg.Nodes.Count - 1].Station.Name
                        : "";
                    RouteList.Children.Add(new TextBlock
                    {
                        Text = $"  Линия {li?.ShortName ?? leg.LineId}: {leg.Nodes.Count} ст. — {seq} · {(int)(leg.Seconds / 60)} мин",
                        Foreground = new SolidColorBrush(c),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }
            RouteList.Children.Add(new TextBlock
            {
                Text = $"Итого: {(int)(r.TotalSeconds / 60)} мин",
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                Margin = new Windows.UI.Xaml.Thickness(0, 6, 0, 0)
            });
            RoutePanel.Visibility = Visibility.Visible;
        }

        private void ClearRouteOverlay()
        {
            // remove highlight shapes drawn by the renderer (they are tagged "routeOverlay")
            var doomed = new List<UIElement>();
            foreach (var el in MapCanvas.Children) if (el is Windows.UI.Xaml.Shapes.Path p && (string)p.Tag == "routeOverlay") doomed.Add(el);
            foreach (var d in doomed) MapCanvas.Children.Remove(d);
        }
    }
}
