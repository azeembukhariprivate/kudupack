using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;

using Kudupack.Core;

namespace Kudupack.Views
{
    /// <summary>
    /// Shows KUDUPACK in the chosen language, in the same way as the other KUDU apps (kudu-common's LangUi): every window is
    /// looked through when it opens and whenever what it shows changes, and its texts are swapped for the chosen
    /// language's. What people type and the names of their files are never touched; in English nothing is done at all.
    /// </summary>
    public static class LangUi
    {
        private static bool _started;

        private sealed class Was
        {
            public string Text;
        }

        private static readonly ConditionalWeakTable<object, Was> Done = new ConditionalWeakTable<object, Was>();
        private static readonly ConditionalWeakTable<Window, Watch> Watches = new ConditionalWeakTable<Window, Watch>();

        /// <summary>Call once when the app starts, after the settings are loaded and before the first window.</summary>
        public static void Start()
        {
            if (_started) return;
            _started = true;
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.FindIndex(args, a => string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase));
            string code = at >= 0 && at + 1 < args.Length ? (Lang.Has(args[at + 1]) ? args[at + 1] : "en") : Lang.Choose(Settings.Language);
            Lang.Use(code);
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(WindowLoaded));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((s, e) => Apply(s as DependencyObject)));
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((s, e) => Apply(s as DependencyObject)));
            EventManager.RegisterClassHandler(typeof(Popup), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => Apply(s as DependencyObject)));
        }

        private static void WindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Window window) || !ReferenceEquals(e.OriginalSource, sender) || Lang.IsEnglish) return;
            if (Watches.TryGetValue(window, out _)) return;
            if (Lang.RightToLeft) window.FlowDirection = FlowDirection.RightToLeft;
            Watch watch = new Watch(window);
            Watches.Add(window, watch);
            watch.Look();
        }

        /// <summary>Looks through a window again each time its layout changes, but not more often than is cheap.</summary>
        private sealed class Watch
        {
            private readonly Window _window;
            private readonly DispatcherTimer _later;
            private DateTime _last = DateTime.MinValue;
            private TimeSpan _gap = TimeSpan.FromMilliseconds(120);
            private bool _looking;

            public Watch(Window window)
            {
                _window = window;
                _later = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = _gap };
                _later.Tick += (s, e) =>
                {
                    _later.Stop();
                    Look();
                };
                window.LayoutUpdated += (s, e) =>
                {
                    if (_looking) return;
                    if (DateTime.UtcNow - _last >= _gap) Look();
                    else if (!_later.IsEnabled) _later.Start();
                };
                window.Closed += (s, e) => _later.Stop();
            }

            public void Look()
            {
                if (_looking) return;
                _looking = true;
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                try { Apply(_window); }
                finally
                {
                    _looking = false;
                    _last = DateTime.UtcNow;
                    _gap = TimeSpan.FromMilliseconds(Math.Max(120, Math.Min(1000, clock.ElapsedMilliseconds * 12)));
                    _later.Interval = _gap;
                }
            }
        }

        private static string Fresh(object owner, string now)
        {
            if (string.IsNullOrEmpty(now)) return null;
            Done.TryGetValue(owner, out Was was);
            if (was != null && was.Text == now) return null;
            string other = Lang.T(now);
            if (was == null) Done.Add(owner, was = new Was());
            was.Text = other;
            return other == now ? null : other;
        }

        /// <summary>Puts the texts of a window, menu or any part of one into the chosen language.</summary>
        public static void Apply(DependencyObject root)
        {
            if (root == null || Lang.IsEnglish) return;
            One(root);
            foreach (object child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject d) Apply(d);
        }

        private static void One(DependencyObject o)
        {
            switch (o)
            {
                case TextBox _:
                    return; // what people type stays as it is
                case TextBlock t:
                    if (BindingOperations.IsDataBound(t, TextBlock.TextProperty)) break;
                    if (t.Inlines.Count <= 1 && (t.Inlines.Count == 0 || t.Inlines.FirstInline is Run first && !BindingOperations.IsDataBound(first, Run.TextProperty)))
                    {
                        string text = Fresh(t, t.Text);
                        if (text != null) t.Text = text;
                    }
                    break;
                case Run r:
                    if (r.Parent is TextBlock parent && parent.Inlines.Count <= 1) break;
                    if (BindingOperations.IsDataBound(r, Run.TextProperty)) break;
                    string run = Fresh(r, r.Text);
                    if (run != null) r.Text = run;
                    break;
                case Window w:
                    string title = Fresh(w, w.Title);
                    if (title != null) w.Title = title;
                    break;
                case HeaderedContentControl h:
                    Header(h, HeaderedContentControl.HeaderProperty);
                    Content(h);
                    break;
                case HeaderedItemsControl hi:
                    Header(hi, HeaderedItemsControl.HeaderProperty);
                    break;
                case ContentControl c:
                    Content(c);
                    break;
                case ListView list when list.View is GridView grid:
                    foreach (GridViewColumn column in grid.Columns)
                        if (column.Header is string head)
                        {
                            string other = Fresh(column, head);
                            if (other != null) column.Header = other;
                        }
                    break;
            }
            if (o is FrameworkElement fe)
            {
                if (fe.ToolTip is string tip)
                {
                    string other = Lang.T(tip);
                    if (other != tip) fe.ToolTip = other;
                }
                else if (fe.ToolTip is DependencyObject richTip) Apply(richTip);
                string name = System.Windows.Automation.AutomationProperties.GetName(fe);
                if (!string.IsNullOrEmpty(name))
                {
                    string other = Lang.T(name);
                    if (other != name) System.Windows.Automation.AutomationProperties.SetName(fe, other);
                }
            }
        }

        private static void Content(ContentControl c)
        {
            if (!(c.Content is string s) || BindingOperations.IsDataBound(c, ContentControl.ContentProperty)) return;
            string other = Fresh(c, s);
            if (other != null) c.Content = other;
        }

        private static void Header(DependencyObject o, DependencyProperty property)
        {
            if (!(o.GetValue(property) is string s) || BindingOperations.IsDataBound(o, property)) return;
            string other = Fresh(o, s);
            if (other != null) o.SetValue(property, other);
        }

        /// <summary>
        /// Fills the language chooser in the Settings window: "The same as Windows", then each language by its own name.
        /// The choice is kept at once and used from the next start (the note under it says so).
        /// </summary>
        public static void FillPicker(Panel chips, FrameworkElement note)
        {
            // The same chips as the theme choice above it; each language is shown by its own name.
            var style = chips.TryFindResource("ChipRadio") as Style;
            string chosen = Settings.Language ?? string.Empty;
            note.Visibility = Visibility.Collapsed;
            void Add(string code, string name)
            {
                var chip = new RadioButton { Content = name, Tag = code, GroupName = "Language", Style = style, IsChecked = code == chosen };
                chip.Checked += (s, e) =>
                {
                    Settings.Language = code;
                    note.Visibility = Lang.Choose(code) == Lang.Current ? Visibility.Collapsed : Visibility.Visible;
                };
                chips.Children.Add(chip);
            }
            Add(string.Empty, "Same as Windows");
            foreach ((string code, string name) in Lang.All) Add(code, name);
        }
    }
}
