using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kudupack.Core;

namespace Kudupack.Views
{
    public sealed class InputItem
    {
        public InputItem(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(Name)) Name = path;
            if (Directory.Exists(path))
            {
                Detail = "Folder";
            }
            else
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }
                Size = size;
                Detail = Paths.FormatSize(size);
            }
        }

        public string Path { get; }
        public string Name { get; }
        public string Detail { get; }
        public long Size { get; }
    }

    public partial class CompressWindow : Window
    {
        private static readonly string[] LevelNames = { "Store", "Fast", "Normal", "Maximum", "Ultra" };
        private readonly ObservableCollection<InputItem> _inputs = new ObservableCollection<InputItem>();
        private bool _nameEdited;
        private bool _settingName;

        public CompressWindow(IEnumerable<string> inputs)
        {
            InitializeComponent();
            ThemeManager.Attach(this);

            InputList.ItemsSource = _inputs;
            foreach (var path in inputs ?? Enumerable.Empty<string>()) AddInput(path);

            LevelSlider.Value = Settings.LastLevel;
            SelectFormat(Settings.LastFormat);

            FolderBox.Text = DefaultFolder();
            SuggestName();
            NameBox.TextChanged += (s, e) => { if (!_settingName) _nameEdited = true; };

            Loaded += (s, e) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private CreateFormat SelectedFormat
        {
            get
            {
                if (Format7z.IsChecked == true) return CreateFormat.SevenZip;
                if (FormatTar.IsChecked == true) return CreateFormat.Tar;
                if (FormatTgz.IsChecked == true) return CreateFormat.TarGz;
                if (FormatTbz.IsChecked == true) return CreateFormat.TarBz2;
                if (FormatTxz.IsChecked == true) return CreateFormat.TarXz;
                return CreateFormat.Zip;
            }
        }

        private void SelectFormat(CreateFormat format)
        {
            switch (format)
            {
                case CreateFormat.SevenZip: Format7z.IsChecked = true; break;
                case CreateFormat.Tar: FormatTar.IsChecked = true; break;
                case CreateFormat.TarGz: FormatTgz.IsChecked = true; break;
                case CreateFormat.TarBz2: FormatTbz.IsChecked = true; break;
                case CreateFormat.TarXz: FormatTxz.IsChecked = true; break;
                default: FormatZip.IsChecked = true; break;
            }
            UpdateFormatOptions();
        }

        private void Format_Checked(object sender, RoutedEventArgs e) => UpdateFormatOptions();

        private void UpdateFormatOptions()
        {
            if (ExtensionText == null) return; // Still loading.
            var format = SelectedFormat;
            ExtensionText.Text = ArchiveCreator.Extension(format);
            LevelPanel.Visibility = ArchiveCreator.SupportsLevel(format) ? Visibility.Visible : Visibility.Collapsed;
            PasswordPanel.Visibility = ArchiveCreator.SupportsPassword(format) ? Visibility.Visible : Visibility.Collapsed;
            NoPasswordText.Visibility = ArchiveCreator.SupportsPassword(format) ? Visibility.Collapsed : Visibility.Visible;
            EncryptNames.Visibility = format == CreateFormat.SevenZip ? Visibility.Visible : Visibility.Collapsed;
            SplitPanel.Visibility = ArchiveCreator.SupportsVolumes(format) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LevelName != null) LevelName.Text = LevelNames[(int)Math.Round(LevelSlider.Value)];
        }

        private long SelectedVolumeSize
        {
            get
            {
                const long mb = 1024L * 1024;
                if (Split100.IsChecked == true) return 100 * mb;
                if (Split700.IsChecked == true) return 700 * mb;
                if (Split4G.IsChecked == true) return 4095 * mb; // Just under the FAT32 file size limit.
                return 0;
            }
        }

        // ---------- Inputs ----------

        private void AddInput(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            if (_inputs.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            _inputs.Add(new InputItem(path));
            RefreshInputs();
        }

        private void RefreshInputs()
        {
            NoInputsText.Visibility = _inputs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var files = _inputs.Count(i => i.Detail != "Folder");
            var folders = _inputs.Count - files;
            var size = _inputs.Sum(i => i.Size);
            InputSummary.Text = _inputs.Count == 0
                ? string.Empty
                : $"{files} file(s), {folders} folder(s)" + (files > 0 ? $" · {Paths.FormatSize(size)} in files" : string.Empty);
            if (!_nameEdited) SuggestName();
        }

        private void SuggestName()
        {
            string name;
            if (_inputs.Count == 1)
            {
                var item = _inputs[0];
                name = Directory.Exists(item.Path) ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
            }
            else if (_inputs.Count > 1)
            {
                var parent = Path.GetDirectoryName(_inputs[0].Path.TrimEnd('\\'));
                name = string.IsNullOrEmpty(parent) ? "archive" : Path.GetFileName(parent);
            }
            else
            {
                name = "archive";
            }

            if (string.IsNullOrWhiteSpace(name)) name = "archive";
            _settingName = true;
            NameBox.Text = name;
            _settingName = false;
        }

        private string DefaultFolder()
        {
            var first = _inputs.FirstOrDefault()?.Path;
            var parent = first == null ? null : Path.GetDirectoryName(first.TrimEnd('\\'));
            return !string.IsNullOrEmpty(parent) && Paths.IsWritable(parent) ? parent : Paths.Desktop;
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var files = Dialogs.PickFiles(this, "Choose files to add");
            var wasEmpty = _inputs.Count == 0;
            foreach (var f in files) AddInput(f);
            if (wasEmpty && _inputs.Count > 0) FolderBox.Text = DefaultFolder();
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Dialogs.PickFolder(this, "Choose a folder to add");
            if (folder == null) return;
            var wasEmpty = _inputs.Count == 0;
            AddInput(folder);
            if (wasEmpty && _inputs.Count > 0) FolderBox.Text = DefaultFolder();
        }

        private void RemoveInput_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is string path)) return;
            var item = _inputs.FirstOrDefault(i => i.Path == path);
            if (item != null) _inputs.Remove(item);
            RefreshInputs();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] dropped)) return;
            var wasEmpty = _inputs.Count == 0;
            foreach (var p in dropped) AddInput(p);
            if (wasEmpty && _inputs.Count > 0) FolderBox.Text = DefaultFolder();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var folder = Dialogs.PickFolder(this, "Save the archive in:", FolderBox.Text);
            if (folder != null) FolderBox.Text = folder;
        }

        // ---------- Create ----------

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_inputs.Count == 0)
            {
                Dialogs.Info(this, "Add at least one file or folder first.");
                return;
            }

            var name = (NameBox.Text ?? string.Empty).Trim();
            var format = SelectedFormat;
            var extension = ArchiveCreator.Extension(format);
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - extension.Length);
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Dialogs.Info(this, "Please enter a valid archive name. It can't contain \\ / : * ? \" < > |");
                NameBox.Focus();
                return;
            }

            var folder = (FolderBox.Text ?? string.Empty).Trim();
            if (folder.Length == 0 || !Path.IsPathRooted(folder))
            {
                Dialogs.Info(this, "Please choose a folder to save the archive in.");
                return;
            }

            var password = string.Empty;
            if (ArchiveCreator.SupportsPassword(format) && PasswordInput.Password.Length > 0)
            {
                if (PasswordInput.Password != PasswordRepeat.Password)
                {
                    Dialogs.Info(this, "The two passwords don't match.");
                    PasswordRepeat.Focus();
                    return;
                }
                password = PasswordInput.Password;
            }

            var output = Path.Combine(folder, name + extension);
            var volumeSize = ArchiveCreator.SupportsVolumes(format) ? SelectedVolumeSize : 0;

            if (_inputs.Any(i => string.Equals(i.Path, output, StringComparison.OrdinalIgnoreCase)))
            {
                Dialogs.Info(this, "The archive can't have the same name as one of the files being added.");
                return;
            }

            if (File.Exists(output) || (volumeSize > 0 && File.Exists(output + ".001")))
            {
                if (!Dialogs.Confirm(this, $"{Path.GetFileName(output)} already exists. Replace it?")) return;
                try
                {
                    if (File.Exists(output)) File.Delete(output);
                    if (File.Exists(output + ".001")) File.Delete(output + ".001");
                }
                catch (Exception ex)
                {
                    Dialogs.Error(this, Dialogs.Describe(ex));
                    return;
                }
            }

            var options = new CreateOptions
            {
                OutputPath = output,
                Format = format,
                Level = (int)Math.Round(LevelSlider.Value),
                Password = password,
                EncryptNames = format == CreateFormat.SevenZip && EncryptNames.IsChecked == true,
                VolumeSize = volumeSize
            };
            options.Inputs.AddRange(_inputs.Select(i => i.Path));

            Settings.LastFormat = format;
            Settings.LastLevel = options.Level;

            var result = ProgressWindow.Run(this, "Creating " + Path.GetFileName(output), "Collecting files…",
                r => ArchiveCreator.Create(options, r));

            if (result.Outcome == OperationOutcome.Failed)
            {
                Dialogs.Error(this, Dialogs.Describe(result.Error));
                return;
            }
            if (!result.Ok) return;

            var created = File.Exists(output) ? output : output + ".001";
            Operations.ShowInFolder(created);
            Close();
        }
    }
}
