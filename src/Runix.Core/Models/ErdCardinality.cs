namespace Runix.Core.Models;

/// <summary>
/// Tipos de cardinalidade entre entidades no Diagrama Entidade-Relacionamento.
/// </summary>
public enum ErdCardinality
{
    /// <summary>1 para 1 (ex: tabela pai e extensão de perfil único)</summary>
    OneToOne,

    /// <summary>1 para N (relação padrão chave primária para chave estrangeira)</summary>
    OneToMany,

    /// <summary>N para M (relação através de tabela associativa / junção)</summary>
    ManyToMany
}
