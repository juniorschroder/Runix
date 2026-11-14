using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class SqlFormatterServiceTests
{
    private readonly SqlFormatterService _formatter = new();

    [Fact]
    public void Format_FormatsSelectJoinWhereGroupByAndOrderByCleanly()
    {
        var raw = "select u.id, u.name, count(o.id) as total_orders from users u left join orders o on u.id = o.user_id where u.active = 1 and o.amount > 50 group by u.id, u.name order by total_orders desc limit 25;";
        var formatted = _formatter.Format(raw);

        Assert.Contains("SELECT\n    u.id,\n    u.name,\n    COUNT(o.id) AS total_orders", formatted);
        Assert.Contains("\nFROM users u", formatted);
        Assert.Contains("\nLEFT JOIN orders o\n    ON u.id = o.user_id", formatted);
        Assert.Contains("\nWHERE u.active = 1\n    AND o.amount > 50", formatted);
        Assert.Contains("\nGROUP BY\n    u.id,\n    u.name", formatted);
        Assert.Contains("\nORDER BY\n    total_orders DESC", formatted);
        Assert.Contains("\nLIMIT 25;", formatted);

        // Idempotency check
        var secondPass = _formatter.Format(formatted);
        Assert.Equal(formatted, secondPass);
    }

    [Fact]
    public void Format_PreservesStringLiteralsQuotedIdentifiersCommentsAndDollarBlocks()
    {
        var raw = @"
-- Comentário de cabeçalho select from where
select ""MixedCaseCol"", 'select * from -- not a comment' as literal_text
from ""MyTable""
where payload ->> 'status' = 'active';

create or replace function calc_tax(val numeric) returns numeric as $body$
begin
    -- select from where inside dollar quote must not be altered
    return val * 0.15;
end;
$body$ language plpgsql;";

        var formatted = _formatter.Format(raw);

        Assert.Contains("\"MixedCaseCol\"", formatted);
        Assert.Contains("'select * from -- not a comment'", formatted);
        Assert.Contains("$body$\nbegin\n    -- select from where inside dollar quote must not be altered\n    return val * 0.15;\nend;\n$body$", formatted);
        Assert.Equal(formatted, _formatter.Format(formatted));
    }
}
