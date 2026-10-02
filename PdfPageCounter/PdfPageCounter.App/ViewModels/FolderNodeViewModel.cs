using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PdfPageCounter.Core.Models;

namespace PdfPageCounter_App.ViewModels;

/// <summary>
/// Bindable wrapper around a <see cref="FolderNode"/>. Recomputes its display
/// label whenever the underlying node's totals change.
/// </summary>
public sealed class FolderNodeViewModel : INotifyPropertyChanged
{
    private readonly FolderNode _node;

    public FolderNodeViewModel(FolderNode node)
    {
        _node = node;
        // Deliberately does NOT populate Children: MainViewModel.BuildBranch owns
        // the single recursive pass over the tree. Populating here as well would
        // add every subtree twice, duplicating each folder once per level.
        RefreshLabel();
    }

    public FolderNode Node => _node;

    public string Name => _node.Name;

    public string FullPath => _node.FullPath;

    public ObservableCollection<FolderNodeViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    private string _displayLabel = string.Empty;
    public string DisplayLabel
    {
        get => _displayLabel;
        private set => SetField(ref _displayLabel, value);
    }

    private string _badgeText = string.Empty;
    public string BadgeText
    {
        get => _badgeText;
        private set => SetField(ref _badgeText, value);
    }

    private bool _hasBadge;
    public bool HasBadge
    {
        get => _hasBadge;
        private set => SetField(ref _hasBadge, value);
    }

    public void SyncFromModel()
    {
        // Keep the child collection in sync with the model (new children may
        // have been discovered after this ViewModel was created).
        // Simple approach: rebuild label only; children are added incrementally
        // by the MainViewModel during the scan.
        RefreshLabel();
    }

    public void RefreshLabel()
    {
        DisplayLabel = $"{_node.Name} — {_node.OwnPageCount} pages (own) / {_node.TotalPageCount} pages (total)";

        if (_node.DuplicateFileCount > 0)
        {
            HasBadge = true;
            BadgeText = $"{_node.DuplicateFileCount} dupes";
        }
        else
        {
            HasBadge = false;
            BadgeText = string.Empty;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
