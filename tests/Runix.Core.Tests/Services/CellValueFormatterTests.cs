using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class CellValueFormatterTests
{
    [Fact]
    public void InspectCell_ClassifiesJsonAndFormatsPrettyPrint()
    {
        string rawJson = "{\"id\":42,\"user\":{\"name\":\"Ana\",\"roles\":[\"admin\",\"dba\"]}}";
        var field = CellValueFormatter.InspectCell(0, "payload", rawJson);

        Assert.Equal(CellValueCategory.Json, field.Category);
        Assert.Contains("\n", field.FormattedDetail);
        Assert.Contains("\"name\": \"Ana\"", field.FormattedDetail);

        bool minifiedOk = CellValueFormatter.TryFormatJsonMinified(field.FormattedDetail, out string minified, out _);
        Assert.True(minifiedOk);
        Assert.Equal(rawJson, minified);
    }

    [Fact]
    public void InspectCell_ClassifiesBlobAndGeneratesHexDump()
    {
        byte[] sample = { 0x52, 0x55, 0x4E, 0x49, 0x58, 0x00, 0xFF };
        var field = CellValueFormatter.InspectCell(1, "binary_data", sample);

        Assert.Equal(CellValueCategory.HexBlob, field.Category);
        Assert.Equal(7, field.ByteOrCharLength);
        Assert.Contains("52 55 4E 49 58 00 FF", field.FormattedDetail);
        Assert.Contains("RUNIX..", field.FormattedDetail);
    }

    [Fact]
    public void InspectCell_ClassifiesNullAndLongText()
    {
        var nullField = CellValueFormatter.InspectCell(0, "deleted_at", null);
        Assert.Equal(CellValueCategory.Null, nullField.Category);

        string longText = new string('A', 120);
        var textField = CellValueFormatter.InspectCell(1, "notes", longText);
        Assert.Equal(CellValueCategory.LongText, textField.Category);
        Assert.Equal(120, textField.ByteOrCharLength);
    }
}
