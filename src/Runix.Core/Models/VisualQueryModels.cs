using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Runix.Core.Models;

public class VisualSelectColumn : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _tableAlias = string.Empty;
    private string _columnName = string.Empty;
    private string _dataType = string.Empty;
    private string _aggregateFunction = string.Empty;
    private string _outputAlias = string.Empty;
    private bool _isGroupBy;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string TableAlias
    {
        get => _tableAlias;
        set { if (_tableAlias != value) { _tableAlias = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayExpression)); } }
    }

    public string ColumnName
    {
        get => _columnName;
        set { if (_columnName != value) { _columnName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayExpression)); } }
    }

    public string DataType
    {
        get => _dataType;
        set { if (_dataType != value) { _dataType = value; OnPropertyChanged(); } }
    }

    public string AggregateFunction
    {
        get => _aggregateFunction;
        set { if (_aggregateFunction != value) { _aggregateFunction = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayExpression)); } }
    }

    public string OutputAlias
    {
        get => _outputAlias;
        set { if (_outputAlias != value) { _outputAlias = value; OnPropertyChanged(); } }
    }

    public bool IsGroupBy
    {
        get => _isGroupBy;
        set { if (_isGroupBy != value) { _isGroupBy = value; OnPropertyChanged(); } }
    }

    public string DisplayExpression =>
        string.IsNullOrWhiteSpace(TableAlias) ? ColumnName : $"{TableAlias}.{ColumnName}";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VisualJoinCondition : INotifyPropertyChanged
{
    private string _sourceColumn = string.Empty;
    private string _operator = "=";
    private string _targetColumn = string.Empty;

    public string SourceColumn
    {
        get => _sourceColumn;
        set { if (_sourceColumn != value) { _sourceColumn = value; OnPropertyChanged(); } }
    }

    public string Operator
    {
        get => _operator;
        set { if (_operator != value) { _operator = value; OnPropertyChanged(); } }
    }

    public string TargetColumn
    {
        get => _targetColumn;
        set { if (_targetColumn != value) { _targetColumn = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VisualJoinClause : INotifyPropertyChanged
{
    private string _joinType = "INNER JOIN";
    private string _targetTable = string.Empty;
    private string _targetAlias = string.Empty;
    private string _sourceTableAlias = string.Empty;
    private string _sourceColumn = string.Empty;
    private string _operator = "=";
    private string _targetColumn = string.Empty;
    private string _relationshipDescription = string.Empty;
    private List<VisualJoinCondition> _additionalConditions = new();

    public string JoinType
    {
        get => _joinType;
        set { _joinType = value; OnPropertyChanged(); }
    }

    public string TargetTable
    {
        get => _targetTable;
        set { _targetTable = value; OnPropertyChanged(); }
    }

    public string TargetAlias
    {
        get => _targetAlias;
        set { _targetAlias = value; OnPropertyChanged(); }
    }

    public string SourceTableAlias
    {
        get => _sourceTableAlias;
        set { _sourceTableAlias = value; OnPropertyChanged(); }
    }

    public string SourceColumn
    {
        get => _sourceColumn;
        set { _sourceColumn = value; OnPropertyChanged(); }
    }

    public string Operator
    {
        get => _operator;
        set { _operator = value; OnPropertyChanged(); }
    }

    public string TargetColumn
    {
        get => _targetColumn;
        set { _targetColumn = value; OnPropertyChanged(); }
    }

    public string RelationshipDescription
    {
        get => _relationshipDescription;
        set { _relationshipDescription = value; OnPropertyChanged(); }
    }

    public List<VisualJoinCondition> AdditionalConditions
    {
        get => _additionalConditions;
        set { _additionalConditions = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public abstract class VisualFilterNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VisualFilterCondition : VisualFilterNode
{
    private string _logicalOperator = "AND";
    private string _tableAlias = string.Empty;
    private string _columnName = string.Empty;
    private string _comparisonOperator = "=";
    private string _value = string.Empty;
    private string _secondValue = string.Empty;

    public string LogicalOperator
    {
        get => _logicalOperator;
        set { if (_logicalOperator != value) { _logicalOperator = value; OnPropertyChanged(); } }
    }

    public string TableAlias
    {
        get => _tableAlias;
        set { if (_tableAlias != value) { _tableAlias = value; OnPropertyChanged(); } }
    }

    public string ColumnName
    {
        get => _columnName;
        set { if (_columnName != value) { _columnName = value; OnPropertyChanged(); } }
    }

    public string ComparisonOperator
    {
        get => _comparisonOperator;
        set { if (_comparisonOperator != value) { _comparisonOperator = value; OnPropertyChanged(); } }
    }

    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; OnPropertyChanged(); } }
    }

    public string SecondValue
    {
        get => _secondValue;
        set { if (_secondValue != value) { _secondValue = value; OnPropertyChanged(); } }
    }
}

public class VisualFilterGroup : VisualFilterNode
{
    private string _logicalOperator = "AND";
    private bool _isNegated;
    private ObservableCollection<VisualFilterNode> _children = new();

    public string LogicalOperator
    {
        get => _logicalOperator;
        set { if (_logicalOperator != value) { _logicalOperator = value; OnPropertyChanged(); } }
    }

    public bool IsNegated
    {
        get => _isNegated;
        set { if (_isNegated != value) { _isNegated = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<VisualFilterNode> Children
    {
        get => _children;
        set { _children = value; OnPropertyChanged(); }
    }

    public void AddCondition(VisualFilterCondition condition)
    {
        condition.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Children));
        _children.Add(condition);
        OnPropertyChanged(nameof(Children));
    }

    public void AddGroup(VisualFilterGroup group)
    {
        group.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Children));
        _children.Add(group);
        OnPropertyChanged(nameof(Children));
    }

    public bool RemoveNode(VisualFilterNode node)
    {
        bool removed = _children.Remove(node);
        if (removed) OnPropertyChanged(nameof(Children));
        return removed;
    }

    public bool IsEmpty => _children.Count == 0;
}

public class VisualSortClause : INotifyPropertyChanged
{
    private string _tableAlias = string.Empty;
    private string _columnName = string.Empty;
    private string _direction = "ASC";

    public string TableAlias
    {
        get => _tableAlias;
        set { _tableAlias = value; OnPropertyChanged(); }
    }

    public string ColumnName
    {
        get => _columnName;
        set { _columnName = value; OnPropertyChanged(); }
    }

    public string Direction
    {
        get => _direction;
        set { _direction = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VisualQueryModel
{
    public string BaseTable { get; set; } = string.Empty;
    public string BaseTableAlias { get; set; } = string.Empty;
    public bool Distinct { get; set; }
    public int? Limit { get; set; } = 100;
    public int? Offset { get; set; }

    public List<VisualSelectColumn> SelectColumns { get; set; } = new();
    public List<VisualJoinClause> Joins { get; set; } = new();
    public List<VisualFilterCondition> Filters { get; set; } = new();
    public VisualFilterGroup? FilterTree { get; set; }
    public VisualFilterGroup? HavingTree { get; set; }
    public List<string> GroupByColumns { get; set; } = new();
    public List<VisualSortClause> Sorts { get; set; } = new();
}
