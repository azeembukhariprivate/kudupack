using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Kudupack.Core;

namespace Kudupack.Views
{
    public sealed class RecentItem
    {
        public RecentItem(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            var folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            Detail = folder + " · " + Paths.FormatSize(size);
            var lower = Name.ToLowerInvariant();
            Type = lower.Contains(".tar.") ? "TAR." + System.IO.Path.GetExtension(lower).TrimStart('.').ToUpperInvariant()
                : System.IO.Path.GetExtension(lower).TrimStart('.').ToUpperInvariant();
        }

        public string Path { get; }
        public string Name { get; }
        public string Detail { get; }
        public string Type { get; }
    }

    public partial class MainWindow : Window
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp"
        };

        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".log", ".csv", ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg", ".conf", ".toml",
            ".html", ".htm", ".css", ".js", ".ts", ".cs", ".py", ".java", ".c", ".cpp", ".h", ".sh", ".bat",
            ".ps1", ".sql", ".srt", ".nfo", ".gitignore", ".editorconfig", ".properties", ".xaml", ".csproj"
        };

        private const long MaxPreviewBytes = 25L * 1024 * 1024;
        private const double MaxDragBytes = 300.0 * 1024 * 1024;

        private ArchiveSession _session;
        private ArchiveNode _current;
        private bool _searching;
        private bool _syncingTree;
        private bool _suppressSearch;
        private string _sortProperty;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private int _previewRequest;
        private UpdateInfo _update;

        private Point _dragStart;
        private bool _dragCandidate;
        private bool _dragging;
        private ListViewItem _pendingSingleSelect;

        public MainWindow(string startPath)
        {
            InitializeComponent();
            ThemeManager.Attach(this);

            ReadFormats.ItemsSource = ArchiveEngine.OpenFormatLabels;
            WriteFormats.ItemsSource = ArchiveEngine.CreateFormatLabels;
            PreviewToggle.IsChecked = Settings.ShowPreview;
            ApplyPreviewVisibility();
            LoadRecent();
            UpdateHomeStatus();

            FileList.PreviewMouseLeftButtonUp += FileList_PreviewMouseLeftButtonUp;

            Loaded += async (s, e) =>
            {
                if (!string.IsNullOrEmpty(startPath))
                {
                    if (ArchiveEngine.LooksLikeArchive(startPath)) await OpenArchiveAsync(startPath);
                    else if (File.Exists(startPath) || Directory.Exists(startPath)) ShowCompress(new List<string> { startPath });
                }
                await CheckUpdatesAsync();
            };

            Closed += (s, e) => _session?.Dispose();
        }

        // ================= Home =================

        private void LoadRecent()
        {
            var items = Settings.ExistingRecent().Select(p => new RecentItem(p)).ToList();
            RecentList.ItemsSource = items;
            NoRecentText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearRecentButton.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateHomeStatus()
        {
            HomeStatus.Text = "Right-click menu in File Explorer: " + (ShellIntegration.IsContextMenuEnabled ? "On" : "Off") +
                              "   ·   Version " + AppInfo.VersionText;
        }

        private async void OpenArchive_Click(object sender, RoutedEventArgs e)
        {
            var files = Dialogs.PickArchives(this, multiple: false);
            if (files.Count > 0) await OpenArchiveAsync(files[0]);
        }

        private void CreateArchive_Click(object sender, RoutedEventArgs e) => ShowCompress(new List<string>());

        private void BatchExtract_Click(object sender, RoutedEventArgs e)
        {
            var files = Dialogs.PickArchives(this, multiple: true);
            if (files.Count == 0) return;
            Operations.ExtractArchives(this, files, intoOwnFolder: true);
            LoadRecent();
        }

        private async void RecentItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string path) await OpenArchiveAsync(path);
        }

        private void ClearRecent_Click(object sender, RoutedEventArgs e)
        {
            Settings.ClearRecent();
            LoadRecent();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
            UpdateHomeStatus();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void ShowCompress(List<string> inputs)
        {
            new CompressWindow(inputs) { Owner = this }.ShowDialog();
        }

        // ================= Opening =================

        private async Task OpenArchiveAsync(string path, string knownPassword = null, string folderToShow = null)
        {
            if (!File.Exists(path))
            {
                Dialogs.Error(this, "This file no longer exists:\n" + path);
                Settings.RemoveRecent(path);
                LoadRecent();
                return;
            }

            ShowBusy("Opening " + Path.GetFileName(path) + "…");
            var percent = new Progress<int>(p =>
            {
                BusyBar.IsIndeterminate = false;
                BusyBar.Value = p;
            });
            var status = new Progress<string>(t => BusyText.Text = t);
            var reporter = new ProgressReporter(percent, status, CancellationToken.None,
                message => Dispatcher.Invoke(() => PasswordWindow.Ask(this, message)));

            try
            {
                var session = await Task.Run(() => ArchiveSession.Open(path, reporter, knownPassword));
                _session?.Dispose();
                _session = session;
                Settings.AddRecent(session.SourcePath);
                ShowArchive(folderToShow);
            }
            catch (OperationCanceledException)
            {
                // The user cancelled the password prompt.
            }
            catch (Exception ex)
            {
                Dialogs.Error(this, Dialogs.Describe(ex));
            }
            finally
            {
                HideBusy();
            }
        }

        private Task ReloadAsync()
        {
            if (_session == null) return Task.CompletedTask;
            var folder = _searching ? string.Empty : _current?.FullPath ?? string.Empty;
            return OpenArchiveAsync(_session.SourcePath, _session.Password, folder);
        }

        private void ShowArchive(string folderToShow)
        {
            HomeView.Visibility = Visibility.Collapsed;
            ArchiveView.Visibility = Visibility.Visible;
            Title = _session.DisplayName + " - KUDUPACK";

            FolderTree.ItemsSource = new[] { _session.Root };
            AddButton.IsEnabled = _session.CanModify;
            DeleteButton.IsEnabled = _session.CanModify;
            var why = "Adding and deleting works for ZIP and 7Z archives that aren't split into parts.";
            AddButton.ToolTip = _session.CanModify ? "Add files to this archive" : why;
            DeleteButton.ToolTip = _session.CanModify ? "Delete the selected items from this archive" : why;
            ToolTipService.SetShowOnDisabled(AddButton, true);
            ToolTipService.SetShowOnDisabled(DeleteButton, true);

            _suppressSearch = true;
            SearchBox.Text = string.Empty;
            SearchHint.Visibility = Visibility.Visible;
            _suppressSearch = false;

            StatusRight.Text = _session.FormatName + (_session.HasEncrypted ? "  ·  Password protected" : string.Empty);
            Navigate(FindFolder(folderToShow) ?? _session.Root);
            FileList.Focus();
        }

        private void CloseArchive()
        {
            _session?.Dispose();
            _session = null;
            _current = null;
            FileList.ItemsSource = null;
            FolderTree.ItemsSource = null;
            ClearPreview("Select a file to preview it.");
            ArchiveView.Visibility = Visibility.Collapsed;
            HomeView.Visibility = Visibility.Visible;
            Title = "KUDUPACK";
            LoadRecent();
        }

        private void CloseArchive_Click(object sender, RoutedEventArgs e) => CloseArchive();

        private ArchiveNode FindFolder(string path)
        {
            if (_session == null || string.IsNullOrEmpty(path)) return null;
            var node = _session.Root;
            foreach (var part in path.Split('\\'))
            {
                node = node.Children.FirstOrDefault(c => c.IsFolder && string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (node == null) return null;
            }
            return node;
        }

        private void ShowBusy(string text)
        {
            BusyText.Text = text;
            BusyBar.IsIndeterminate = true;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        private void HideBusy() => BusyOverlay.Visibility = Visibility.Collapsed;

        // ================= Browsing =================

        private void Navigate(ArchiveNode node)
        {
            if (node == null || !node.IsFolder) return;
            _current = node;
            _searching = false;

            if (SearchBox.Text.Length > 0)
            {
                _suppressSearch = true;
                SearchBox.Text = string.Empty;
                SearchHint.Visibility = Visibility.Visible;
                _suppressSearch = false;
            }

            SetItems(node.Children);
            PathText.Text = BuildPathText(node);

            _syncingTree = true;
            for (var p = node.Parent; p != null; p = p.Parent) p.IsExpanded = true;
            node.IsSelected = true;
            _syncingTree = false;

            UpdateStatus();
        }

        private void SetItems(IEnumerable<ArchiveNode> items)
        {
            var list = items.ToList();
            FileList.ItemsSource = list;
            ApplySort();
            EmptyText.Text = _searching ? "No files match your search." : "This folder is empty.";
            EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearPreview("Select a file to preview it.");
        }

        private static string BuildPathText(ArchiveNode node)
        {
            var parts = new List<string>();
            for (var n = node; n != null; n = n.Parent) parts.Insert(0, n.Name);
            return string.Join("  ›  ", parts);
        }

        private void UpdateStatus()
        {
            if (_session == null) return;
            var saved = _session.UnpackedSize > 0 && _session.PackedSize > 0
                ? $" ({Math.Max(0, 100 - (int)(_session.PackedSize * 100.0 / _session.UnpackedSize))}% smaller)"
                : string.Empty;
            var text = $"{_session.AllFiles.Count} files, {_session.FolderCount} folders  ·  " +
                       $"{Paths.FormatSize(_session.UnpackedSize)} → {Paths.FormatSize(_session.PackedSize)}{saved}";

            var selected = SelectedNodes();
            if (selected.Count > 0)
            {
                var size = selected.Sum(n => n.IsFolder ? n.AllFilesBelow().Sum(f => (double)f.Size) : n.Size);
                text += $"  ·  {selected.Count} selected ({Paths.FormatSize((ulong)size)})";
            }
            StatusLeft.Text = text;
        }

        private List<ArchiveNode> SelectedNodes() => FileList.SelectedItems.Cast<ArchiveNode>().ToList();

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_syncingTree) return;
            if (e.NewValue is ArchiveNode node && node != _current) Navigate(node);
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            if (_searching) Navigate(_current ?? _session.Root);
            else if (_current?.Parent != null) Navigate(_current.Parent);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_session == null || _suppressSearch) return;

            var query = SearchBox.Text.Trim();
            if (query.Length == 0)
            {
                Navigate(_current ?? _session.Root);
                return;
            }

            _searching = true;
            SetItems(_session.AllFiles
                .Where(f => f.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(5000));
            PathText.Text = $"Search results for \"{query}\"";
            UpdateStatus();
        }

        private void FileList_HeaderClick(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is GridViewColumnHeader header) || header.Column == null) return;
            string property;
            switch (header.Column.Header as string)
            {
                case "Size": property = "Size"; break;
                case "Modified": property = "Modified"; break;
                case "Type": property = "TypeText"; break;
                default: property = "Name"; break;
            }

            _sortDirection = _sortProperty == property && _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            _sortProperty = property;
            ApplySort();
        }

        private void ApplySort()
        {
            if (_sortProperty == null || FileList.ItemsSource == null) return;
            var view = CollectionViewSource.GetDefaultView(FileList.ItemsSource);
            if (view == null) return;
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription("IsFolder", ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
            }
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatus();
            if (PreviewToggle.IsChecked == true) _ = LoadPreviewAsync();
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(FileList, e.OriginalSource as DependencyObject) is ListViewItem item &&
                item.Content is ArchiveNode node)
            {
                OpenNode(node);
            }
        }

        private void FileList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && FileList.SelectedItem is ArchiveNode node)
            {
                OpenNode(node);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _session?.CanModify == true)
            {
                Delete_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                Up_Click(sender, e);
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.O)
            {
                OpenArchive_Click(sender, e);
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.F && _session != null)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0)
            {
                SearchBox.Text = string.Empty;
                e.Handled = true;
            }
        }

        private void OpenNode(ArchiveNode node)
        {
            if (node.IsFolder) Navigate(node);
            else OpenFileWithWindows(node);
        }

        /// <summary>Extracts one file to a temporary folder and opens it with its usual app.</summary>
        private void OpenFileWithWindows(ArchiveNode node)
        {
            var session = _session;
            var temp = Paths.NewTempFolder("view");
            var result = ProgressWindow.Run(this, "Opening " + node.Name, "Extracting to a temporary folder…",
                r => session.ExtractIndexes(temp, new[] { node.Index }, r));

            if (result.Outcome == OperationOutcome.Failed)
            {
                Dialogs.Error(this, Dialogs.Describe(result.Error));
                return;
            }
            if (!result.Ok) return;

            var path = Path.Combine(temp, node.FullPath);
            if (!File.Exists(path))
                path = Directory.GetFiles(temp, node.Name, SearchOption.AllDirectories).FirstOrDefault();
            if (path == null) return;

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                Dialogs.Info(this, "Windows doesn't have an app set up to open this type of file.");
            }
        }

        // ================= Actions =================

        private void ExtractAll_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            var parent = Path.GetDirectoryName(_session.SourcePath) ?? Paths.Desktop;
            var destination = Paths.UniqueFolder(parent, Paths.ArchiveBaseName(_session.SourcePath));
            RunExtract(destination, null);
        }

        private void ExtractTo_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            var selected = SelectedNodes();
            var prompt = selected.Count > 0
                ? $"Extract the {selected.Count} selected item(s) to:"
                : "Extract everything to:";
            var destination = Dialogs.PickFolder(this, prompt, Path.GetDirectoryName(_session.SourcePath));
            if (destination == null) return;
            RunExtract(destination, selected.Count > 0 ? selected : null);
        }

        private void RunExtract(string destination, List<ArchiveNode> nodes)
        {
            var session = _session;
            var searching = _searching;
            var baseFolder = searching ? string.Empty : _current?.FullPath ?? string.Empty;

            var result = ProgressWindow.Run(this, "Extracting " + session.DisplayName, "Extracting to " + destination, r =>
            {
                if (nodes == null)
                {
                    session.ExtractAll(destination, r);
                    return;
                }

                // Extract the selection, then move it so it lands without its parent folders.
                Paths.EnsureWritable(destination);
                var indexes = new List<int>();
                foreach (var node in nodes) node.CollectIndexes(indexes);
                var temp = Paths.NewTempFolder("extract");
                try
                {
                    session.ExtractIndexes(temp, indexes, r);
                    var source = string.IsNullOrEmpty(baseFolder) ? temp : Path.Combine(temp, baseFolder);
                    if (searching)
                    {
                        foreach (var node in nodes) MoveInto(Path.Combine(temp, node.FullPath), destination);
                    }
                    else if (Directory.Exists(source))
                    {
                        foreach (var entry in Directory.GetFileSystemEntries(source)) MoveInto(entry, destination);
                    }
                }
                finally
                {
                    Paths.TryDeleteFolder(temp);
                }
            });

            if (result.Outcome == OperationOutcome.Failed)
            {
                Dialogs.Error(this, Dialogs.Describe(result.Error));
                return;
            }
            if (!result.Ok) return;

            if (Settings.OpenFolderAfterExtract) Operations.OpenFolder(destination);

            if (nodes == null && Settings.DeleteAfterExtract && !session.IsMultiVolume)
            {
                var source = session.SourcePath;
                CloseArchive();
                if (Operations.RecycleFile(source))
                {
                    Settings.RemoveRecent(source);
                    LoadRecent();
                    HomeStatus.Text = Path.GetFileName(source) + " was extracted and moved to the Recycle Bin.";
                }
            }
        }

        /// <summary>Moves a file or folder into a destination folder, renaming on name clashes.</summary>
        private static void MoveInto(string sourcePath, string destinationFolder)
        {
            var name = Path.GetFileName(sourcePath);
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, UniqueFilePath(destinationFolder, name));
            }
            else if (Directory.Exists(sourcePath))
            {
                var target = Path.Combine(destinationFolder, name);
                Directory.CreateDirectory(target);
                foreach (var entry in Directory.GetFileSystemEntries(sourcePath)) MoveInto(entry, target);
            }
        }

        private static string UniqueFilePath(string folder, string name)
        {
            var path = Path.Combine(folder, name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            var i = 2;
            while (File.Exists(path) || Directory.Exists(path)) path = Path.Combine(folder, $"{stem} ({i++}){ext}");
            return path;
        }

        private async void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null || !_session.CanModify) return;
            var files = Dialogs.PickFiles(this, "Add files to " + _session.DisplayName);
            if (files.Count > 0) await AddToArchiveAsync(files);
        }

        private async Task AddToArchiveAsync(List<string> paths)
        {
            var session = _session;
            var folder = _searching ? string.Empty : _current?.FullPath ?? string.Empty;
            var result = ProgressWindow.Run(this, "Adding to " + session.DisplayName, "Adding files…",
                r => session.AddFiles(paths, folder, r));

            if (result.Outcome == OperationOutcome.Failed) Dialogs.Error(this, Dialogs.Describe(result.Error));
            await ReloadAsync();
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null || !_session.CanModify) return;
            var selected = SelectedNodes();
            if (selected.Count == 0) return;

            var what = selected.Count == 1 ? "\"" + selected[0].Name + "\"" : selected.Count + " items";
            if (!Dialogs.Confirm(this, $"Delete {what} from {_session.DisplayName}?\n\nThis can't be undone.")) return;

            var indexes = new List<int>();
            foreach (var node in selected) node.CollectIndexes(indexes);

            var session = _session;
            var result = ProgressWindow.Run(this, "Deleting from " + session.DisplayName, "Updating the archive…",
                r => session.Delete(indexes, r));

            if (result.Outcome == OperationOutcome.Failed) Dialogs.Error(this, Dialogs.Describe(result.Error));
            await ReloadAsync();
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            var session = _session;
            var ok = false;
            var result = ProgressWindow.Run(this, "Testing " + session.DisplayName, "Checking every file…",
                r => ok = session.Test(r));

            if (result.Outcome == OperationOutcome.Failed)
                Dialogs.Error(this, Dialogs.Describe(result.Error));
            else if (result.Ok && ok)
                Dialogs.Info(this, "No errors found in " + session.DisplayName + ".");
            else if (result.Ok)
                Dialogs.Error(this, session.DisplayName + " has errors. Some files may be damaged or incomplete.");
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null) Dialogs.Info(this, _session.Describe(), "Archive info");
        }

        // ================= Preview =================

        private void PreviewToggle_Changed(object sender, RoutedEventArgs e)
        {
            Settings.ShowPreview = PreviewToggle.IsChecked == true;
            ApplyPreviewVisibility();
            if (PreviewToggle.IsChecked == true) _ = LoadPreviewAsync();
        }

        private void ApplyPreviewVisibility()
        {
            var show = PreviewToggle.IsChecked == true;
            PreviewColumn.Width = show ? new GridLength(300) : new GridLength(0);
            PreviewSplitColumn.Width = show ? new GridLength(10) : new GridLength(0);
            PreviewPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PreviewSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearPreview(string message)
        {
            _previewRequest++;
            PreviewName.Text = string.Empty;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Text = string.Empty;
            PreviewText.Visibility = Visibility.Collapsed;
            PreviewMessage.Text = message;
            PreviewMessage.Visibility = Visibility.Visible;
        }

        private async Task LoadPreviewAsync()
        {
            var node = FileList.SelectedItems.Count == 1 ? FileList.SelectedItem as ArchiveNode : null;
            if (node == null)
            {
                ClearPreview("Select a file to preview it.");
                return;
            }
            if (node.IsFolder)
            {
                ClearPreview("Folders can't be previewed.");
                return;
            }

            var ext = Path.GetExtension(node.Name);
            var isImage = ImageExtensions.Contains(ext);
            var isText = TextExtensions.Contains(ext) || string.IsNullOrEmpty(ext) && node.Size < 512 * 1024;

            ClearPreview(string.Empty);
            var request = _previewRequest;
            PreviewName.Text = node.Name;

            if (!isImage && !isText)
            {
                PreviewMessage.Text = "There's no preview for this type of file. Double-click to open it.";
                return;
            }
            if ((long)node.Size > MaxPreviewBytes)
            {
                PreviewMessage.Text = "This file is too large to preview. Double-click to open it.";
                return;
            }

            var session = _session;
            if (session == null) return;
            if (session.NeedsPassword)
            {
                PreviewMessage.Text = "This archive is password-protected. Double-click the file to enter the password.";
                return;
            }

            PreviewMessage.Text = "Loading…";
            byte[] data;
            try
            {
                data = await Task.Run(() => session.ReadFile(node, ProgressReporter.None));
            }
            catch
            {
                if (request == _previewRequest) PreviewMessage.Text = "A preview couldn't be loaded.";
                return;
            }

            if (request != _previewRequest || session != _session || data == null) return;

            if (isImage)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using (var ms = new MemoryStream(data))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewMessage.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    PreviewMessage.Text = "This image couldn't be shown.";
                }
            }
            else
            {
                PreviewText.Text = DecodeText(data, 200_000);
                PreviewText.Visibility = Visibility.Visible;
                PreviewMessage.Visibility = Visibility.Collapsed;
            }
        }

        private static string DecodeText(byte[] data, int maxChars)
        {
            using (var reader = new StreamReader(new MemoryStream(data), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var buffer = new char[maxChars];
                var read = reader.ReadBlock(buffer, 0, maxChars);
                var text = new string(buffer, 0, read);
                if (!reader.EndOfStream) text += "\n\n… (preview shortened)";
                return text;
            }
        }

        // ================= Drag and drop =================

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_dragging && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_dragging || !(e.Data.GetData(DataFormats.FileDrop) is string[] dropped) || dropped.Length == 0) return;
            var paths = dropped.ToList();
            var singleArchive = paths.Count == 1 && ArchiveEngine.LooksLikeArchive(paths[0]);

            if (_session != null && !singleArchive)
            {
                if (_session.CanModify)
                {
                    var what = paths.Count == 1 ? "\"" + Path.GetFileName(paths[0]) + "\"" : paths.Count + " items";
                    if (Dialogs.Confirm(this, $"Add {what} to {_session.DisplayName}?"))
                        await AddToArchiveAsync(paths);
                }
                else
                {
                    ShowCompress(paths);
                }
                return;
            }

            if (singleArchive) await OpenArchiveAsync(paths[0]);
            else ShowCompress(paths);
        }

        private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _pendingSingleSelect = null;
            var item = ItemsControl.ContainerFromElement(FileList, e.OriginalSource as DependencyObject) as ListViewItem;
            _dragCandidate = item != null;

            // Keep a multi-selection when the user starts dragging one of the selected items.
            if (item != null && item.IsSelected && FileList.SelectedItems.Count > 1 &&
                Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1)
            {
                _pendingSingleSelect = item;
                e.Handled = true;
            }
        }

        private void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_pendingSingleSelect != null)
            {
                FileList.SelectedItems.Clear();
                _pendingSingleSelect.IsSelected = true;
                _pendingSingleSelect.Focus();
                _pendingSingleSelect = null;
            }
            _dragCandidate = false;
        }

        private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragCandidate || _dragging || _session == null || e.LeftButton != MouseButtonState.Pressed) return;

            var position = e.GetPosition(null);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _dragCandidate = false;
            _pendingSingleSelect = null;
            var nodes = SelectedNodes();
            if (nodes.Count > 0) DragOut(nodes);
        }

        /// <summary>Extracts the selection to a temporary folder and lets Explorer copy it from there.</summary>
        private void DragOut(List<ArchiveNode> nodes)
        {
            var total = nodes.Sum(n => n.IsFolder ? n.AllFilesBelow().Sum(f => (double)f.Size) : n.Size);
            if (total > MaxDragBytes)
            {
                Dialogs.Info(this, "That's a lot of data to drag. Use \"Extract to…\" instead so you can see the progress.");
                return;
            }
            if (_session.NeedsPassword)
            {
                Dialogs.Info(this, "This archive is password-protected. Use \"Extract to…\" and enter the password.");
                return;
            }

            var indexes = new List<int>();
            foreach (var node in nodes) node.CollectIndexes(indexes);
            var temp = Paths.NewTempFolder("drag");

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _session.ExtractIndexes(temp, indexes, ProgressReporter.None);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                Dialogs.Error(this, Dialogs.Describe(ex));
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            var paths = nodes.Select(n => Path.Combine(temp, n.FullPath))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToArray();
            if (paths.Length == 0) return;

            _dragging = true;
            try
            {
                DragDrop.DoDragDrop(FileList, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
            }
            finally
            {
                _dragging = false;
            }
        }

        // ================= Updates =================

        private async Task CheckUpdatesAsync()
        {
            if (!Settings.CheckForUpdates) return;
            try
            {
                var info = await UpdateChecker.CheckAsync();
                if (info == null || !info.IsNewer || Settings.SkippedVersion == info.Latest.ToString()) return;
                _update = info;
                UpdateText.Text = $"KUDUPACK {info.Latest.ToString(3)} is available. You have {AppInfo.VersionText}.";
                UpdateBanner.Visibility = Visibility.Visible;
            }
            catch
            {
                // Offline or GitHub unavailable: stay quiet.
            }
        }

        private void DownloadUpdate_Click(object sender, RoutedEventArgs e) =>
            AppInfo.OpenUrl(_update?.Url ?? AppInfo.ReleasesUrl);

        private void DismissUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_update != null) Settings.SkippedVersion = _update.Latest.ToString();
            UpdateBanner.Visibility = Visibility.Collapsed;
        }
    }
}
