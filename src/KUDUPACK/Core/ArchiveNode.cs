using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Kudupack.Core
{
    /// <summary>A file or folder inside an archive.</summary>
    public sealed class ArchiveNode : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;

        public string Name { get; set; } = string.Empty;

        /// <summary>Path inside the archive, using backslashes, e.g. "src\assets\logo.png".</summary>
        public string FullPath { get; set; } = string.Empty;

        public bool IsFolder { get; set; }
        public bool IsFile => !IsFolder;
        public ulong Size { get; set; }
        public DateTime Modified { get; set; }
        public bool Encrypted { get; set; }

        /// <summary>Index in the archive, or -1 for folders that have no entry of their own.</summary>
        public int Index { get; set; } = -1;

        public ArchiveNode Parent { get; set; }
        public List<ArchiveNode> Children { get; } = new List<ArchiveNode>();
        public ObservableCollection<ArchiveNode> SubFolders { get; } = new ObservableCollection<ArchiveNode>();

        public string SizeText => IsFolder ? string.Empty : Paths.FormatSize(Size);

        public string ModifiedText => Modified <= new DateTime(1981, 1, 1) ? string.Empty : Modified.ToString("g");

        public string TypeText
        {
            get
            {
                if (IsFolder) return "Folder";
                var ext = Path.GetExtension(Name);
                return string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant() + " file";
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnChanged(); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
        }

        public IEnumerable<ArchiveNode> AllFilesBelow()
        {
            foreach (var child in Children)
            {
                if (child.IsFolder)
                {
                    foreach (var f in child.AllFilesBelow()) yield return f;
                }
                else
                {
                    yield return child;
                }
            }
        }

        /// <summary>All archive indexes this node covers (itself plus everything below a folder).</summary>
        public void CollectIndexes(ICollection<int> into)
        {
            if (Index >= 0) into.Add(Index);
            foreach (var child in Children) child.CollectIndexes(into);
        }

        public void SortChildren()
        {
            Children.Sort((a, b) =>
                a.IsFolder != b.IsFolder
                    ? (a.IsFolder ? -1 : 1)
                    : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            var folders = new List<ArchiveNode>(SubFolders);
            folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            SubFolders.Clear();
            foreach (var f in folders) SubFolders.Add(f);

            foreach (var f in folders) f.SortChildren();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
