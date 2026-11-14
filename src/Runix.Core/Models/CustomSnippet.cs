using System;

namespace Runix.Core.Models;

/// <summary>
/// Representa um snippet SQL customizado cadastrado pelo usuário para expansão no editor.
/// </summary>
public class CustomSnippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Prefix { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
