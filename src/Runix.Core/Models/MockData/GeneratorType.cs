namespace Runix.Core.Models.MockData;

/// <summary>
/// Categorias semânticas e tipos de geradores para preenchimento de dados fictícios.
/// </summary>
public enum GeneratorType
{
    Name,
    Email,
    Phone,
    Address,
    City,
    Country,
    ZipCode,
    DateTime,
    Number,
    Decimal,
    Uuid,
    Boolean,
    ChoiceList,
    ForeignKeySample,
    Sequence,
    Null,
    CustomFormat
}
