using System.Collections.Generic;
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

    public string DisplayExpression =>
        string.IsNullOrWhiteSpace(TableAlias) ? ColumnName : $"{TableAlias}.{ColumnName}";

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class VisualFilterCondition : INotifyPropertyChanged
{
    private string _logicalOperator = "AND";
    private string _tableAlias = string.Empty;
    private string _columnName = string.Empty;
    private string _comparisonOperator = "=";
    private string _value = string.Empty;

    public string LogicalOperator
    {
        get => _logicalOperator;
        set { _logicalOperator = value; OnPropertyChanged(); }
    }

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

    public string ComparisonOperator
    {
        get => _comparisonOperator;
        set { _comparisonOperator = value; OnPropertyChanged(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    public List<VisualSortClause> Sorts { get; set; } = new();
}
