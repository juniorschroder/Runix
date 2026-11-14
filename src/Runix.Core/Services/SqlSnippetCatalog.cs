namespace Runix.Core.Services;

using System.Collections.Generic;
using Runix.Core.Models;

public static class SqlSnippetCatalog
{
    public static readonly IReadOnlyList<CompletionItem> Items = new[]
    {
        new CompletionItem
        {
            Label = "sel",
            Kind = CompletionItemKind.Snippet,
            Detail = "SELECT * FROM <table>",
            InsertText = "SELECT * FROM ",
            Priority = 65
        },
        new CompletionItem
        {
            Label = "ins",
            Kind = CompletionItemKind.Snippet,
            Detail = "INSERT INTO <table> (<cols>) VALUES (<vals>);",
            InsertText = "INSERT INTO ",
            Priority = 65
        },
        new CompletionItem
        {
            Label = "upd",
            Kind = CompletionItemKind.Snippet,
            Detail = "UPDATE <table> SET <col> = <val> WHERE <condition>;",
            InsertText = "UPDATE ",
            Priority = 65
        },
        new CompletionItem
        {
            Label = "del",
            Kind = CompletionItemKind.Snippet,
            Detail = "DELETE FROM <table> WHERE <condition>;",
            InsertText = "DELETE FROM ",
            Priority = 65
        },
        new CompletionItem
        {
            Label = "join",
            Kind = CompletionItemKind.Snippet,
            Detail = "JOIN <tbl> ON <col1> = <col2>",
            InsertText = "JOIN ",
            Priority = 65
        },
        new CompletionItem
        {
            Label = "cnt",
            Kind = CompletionItemKind.Snippet,
            Detail = "SELECT COUNT(*) FROM <table>",
            InsertText = "SELECT COUNT(*) FROM ",
            Priority = 65
        },
        new CompletionItem
        {
            Label = "ctbl",
            Kind = CompletionItemKind.Snippet,
            Detail = "CREATE TABLE <name> (id INTEGER PRIMARY KEY);",
            InsertText = "CREATE TABLE ",
            Priority = 65
        }
    };
}
