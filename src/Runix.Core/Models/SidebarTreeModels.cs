using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Runix.Core.Models;

public enum SidebarItemType
{
    Group,
    Table,
    View,
    Column,
    Trigger,
    Function,
    Index,
    Package,
    Sequence
}

public abstract class DatabaseSidebarItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public SidebarItemType ItemType { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnExpandedChanged(value);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<DatabaseSidebarItem> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void OnExpandedChanged(bool isExpanded) { }
}

public class SchemaOrGroupSidebarItem : DatabaseSidebarItem
{
    public int Count => Children.Count;
    public string HeaderText => $"{DisplayName} ({Count})";

    public SchemaOrGroupSidebarItem(string title, string icon, SidebarItemType type = SidebarItemType.Group)
    {
        Name = title;
        DisplayName = title;
        Icon = icon;
        ItemType = type;
        IsExpanded = true;
    }

    public void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HeaderText));
    }
}

public class TableSidebarItem : DatabaseSidebarItem
{
    private bool _isFavorite;
    private bool _isLoadingColumns;
    private bool _columnsLoaded;

    public TableInfo Table { get; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteIcon));
            }
        }
    }

    public string FavoriteIcon => IsFavorite ? "⭐" : "☆";

    public long? RowCount => Table.RowCount;
    public bool HasRowCount => RowCount.HasValue && RowCount.Value >= 0;

    public bool IsLoadingColumns
    {
        get => _isLoadingColumns;
        set { _isLoadingColumns = value; OnPropertyChanged(); }
    }

    public bool ColumnsLoaded
    {
        get => _columnsLoaded;
        set { _columnsLoaded = value; OnPropertyChanged(); }
    }

    public Func<TableSidebarItem, Task>? LoadColumnsHandler { get; set; }

    public TableSidebarItem(TableInfo table, bool isFavorite = false)
    {
        Table = table;
        Name = table.Name;
        DisplayName = table.DisplayName;
        Icon = table.Icon;
        ItemType = table.IsView ? SidebarItemType.View : SidebarItemType.Table;
        IsFavorite = isFavorite;
    }

    protected override async void OnExpandedChanged(bool isExpanded)
    {
        base.OnExpandedChanged(isExpanded);
        if (isExpanded && !ColumnsLoaded && !IsLoadingColumns && LoadColumnsHandler != null)
        {
            await LoadColumnsHandler(this);
        }
    }
}

public class ColumnSidebarItem : DatabaseSidebarItem
{
    public ColumnInfo Column { get; }

    public string DataType => Column.DataType;
    public bool IsPrimaryKey => Column.IsPrimaryKey;
    public bool IsForeignKey => Column.IsForeignKey;
    public string KeyBadge => IsPrimaryKey ? "🔑" : (IsForeignKey ? "🔗" : "•");
    public string KeyTooltip => Column.KeyText;

    public ColumnSidebarItem(ColumnInfo column)
    {
        Column = column;
        Name = column.Name;
        DisplayName = column.Name;
        Icon = KeyBadge;
        ItemType = SidebarItemType.Column;
    }
}

public class ProgrammabilitySidebarItem : DatabaseSidebarItem
{
    public DatabaseObjectInfo ObjectInfo { get; }
    public string Subtitle => ObjectInfo.Subtitle;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public string TypeBadge => ObjectInfo.TypeBadge;

    public ProgrammabilitySidebarItem(DatabaseObjectInfo objectInfo)
    {
        ObjectInfo = objectInfo;
        Name = objectInfo.Name;
        DisplayName = objectInfo.QualifiedName;
        Icon = objectInfo.Icon;
        ItemType = objectInfo.ObjectType switch
        {
            DatabaseObjectType.View => SidebarItemType.View,
            DatabaseObjectType.Trigger => SidebarItemType.Trigger,
            DatabaseObjectType.Function or DatabaseObjectType.Procedure => SidebarItemType.Function,
            DatabaseObjectType.Index => SidebarItemType.Index,
            DatabaseObjectType.Package => SidebarItemType.Package,
            DatabaseObjectType.Sequence => SidebarItemType.Sequence,
            _ => SidebarItemType.View
        };
    }
}
