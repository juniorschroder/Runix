namespace Runix.Core.Tests.Models;

using System;
using System.Collections.Generic;
using Runix.Core.Models;
using Xunit;

public class SafeRowComparerTests
{
    [Fact]
    public void Compare_StringAndLong_DoesNotThrowAndComparesCorrectly()
    {
        var comparer = new SafeRowComparer();
        
        // Exatamente o cenário de FK onde uma célula é string e a outra é long
        object valString = "10";
        object valLong = 42L;

        var result = comparer.Compare(valString, valLong);
        Assert.True(result < 0, "10 deve ser menor que 42");

        var resultReverse = comparer.Compare(valLong, valString);
        Assert.True(resultReverse > 0, "42 deve ser maior que 10");
    }

    [Fact]
    public void Compare_LongAndInt_ComparesNumerically()
    {
        var comparer = new SafeRowComparer();
        object valInt = 100;
        object valLong = 100L;

        var result = comparer.Compare(valInt, valLong);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Compare_NullsAndValues_HandlesNullsSafely()
    {
        var comparer = new SafeRowComparer();
        
        Assert.Equal(0, comparer.Compare(null, null));
        Assert.Equal(0, comparer.Compare(DBNull.Value, null));
        Assert.True(comparer.Compare(null, 1) < 0);
        Assert.True(comparer.Compare(1, null) > 0);
    }

    [Fact]
    public void Compare_RowsByColumnIndex_ExtractsAndComparesCorrectly()
    {
        var comparer = new SafeRowComparer(1); // Ordenando pela coluna 1 (user_id)

        var rowA = new List<object?> { "Alice", 100L };
        var rowB = new List<object?> { "Bob", "50" }; // Tipo misto na coluna 1!

        var result = comparer.Compare(rowA, rowB);
        Assert.True(result > 0, "100L deve ser maior que '50'");
    }

    [Fact]
    public void Compare_StringsAlphabetically_ComparesCaseInsensitive()
    {
        var comparer = new SafeRowComparer();
        
        Assert.Equal(0, comparer.Compare("alpha", "ALPHA"));
        Assert.True(comparer.Compare("alpha", "beta") < 0);
        Assert.True(comparer.Compare("gamma", "beta") > 0);
    }
}
