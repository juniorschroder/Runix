using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Representa uma consulta SQL salva pelo usuário na biblioteca local do RUNIX.
/// </summary>
public class SavedQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ConnectionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string TagsSummary => string.Join(", ", Tags);
    public string FavoriteStar => IsFavorite ? "★" : "☆";
}
