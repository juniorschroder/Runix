using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do repositório local de aplicação para queries salvas, snippets e histórico.
/// </summary>
public interface IAppStorageService
{
    Task InitializeAsync(CancellationToken ct = default);

    // Queries Salvas
    Task<IReadOnlyList<SavedQuery>> GetSavedQueriesAsync(
        string? connectionId = null,
        string? searchFilter = null,
        bool? onlyFavorites = null,
        CancellationToken ct = default);

    Task<SavedQuery?> GetSavedQueryByIdAsync(string id, CancellationToken ct = default);
    Task SaveQueryAsync(SavedQuery query, CancellationToken ct = default);
    Task ToggleFavoriteAsync(string queryId, bool isFavorite, CancellationToken ct = default);
    Task DeleteSavedQueryAsync(string id, CancellationToken ct = default);

    // Snippets Customizados
    Task<IReadOnlyList<CustomSnippet>> GetCustomSnippetsAsync(CancellationToken ct = default);
    Task SaveCustomSnippetAsync(CustomSnippet snippet, CancellationToken ct = default);
    Task DeleteCustomSnippetAsync(string id, CancellationToken ct = default);

    // Histórico de Execução
    Task LogQueryExecutionAsync(QueryHistoryEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<QueryHistoryEntry>> GetQueryHistoryAsync(string? connectionId = null, int limit = 50, CancellationToken ct = default);
    Task ClearQueryHistoryAsync(string? connectionId = null, CancellationToken ct = default);
}
