using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;

namespace Runix.Desktop.Services;

/// <summary>
/// Gerencia a infraestrutura de internacionalização dinâmica e alternância a quente de idiomas no Avalonia UI.
/// </summary>
public static class LocalizationManager
{
    public const string Portuguese = "pt-BR";
    public const string English = "en-US";

    private const string PtBrUri = "avares://Runix.Desktop/Locale/Strings.pt-BR.axaml";
    private const string EnUsUri = "avares://Runix.Desktop/Locale/Strings.en-US.axaml";

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runix");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

    public static string CurrentLanguage { get; private set; } = English;

    public static string CurrentLanguageLabel => CurrentLanguage.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "PT" : "EN";

    public static event Action<string>? LanguageChanged;

    /// <summary>
    /// Inicializa a localização com base nas preferências salvas ou na cultura do sistema operacional.
    /// </summary>
    public static void Initialize(string? explicitLanguage = null)
    {
        string chosenLanguage = English;

        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            chosenLanguage = explicitLanguage.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? Portuguese : English;
        }
        else
        {
            string? saved = LoadSavedLanguage();
            if (!string.IsNullOrEmpty(saved))
            {
                chosenLanguage = saved.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? Portuguese : English;
            }
            else
            {
                // Detecção automática da cultura do SO
                var osCulture = CultureInfo.CurrentUICulture;
                chosenLanguage = osCulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase)
                    ? Portuguese
                    : English;
            }
        }

        SetLanguage(chosenLanguage, savePreference: false);
    }

    /// <summary>
    /// Alterna entre Português e Inglês.
    /// </summary>
    public static string ToggleLanguage()
    {
        var target = CurrentLanguage.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? English : Portuguese;
        SetLanguage(target, savePreference: true);
        return target;
    }

    /// <summary>
    /// Aplica o idioma dinamicamente sem reiniciar a aplicação.
    /// </summary>
    public static void SetLanguage(string languageCode, bool savePreference = true)
    {
        bool isPt = languageCode.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
        CurrentLanguage = isPt ? Portuguese : English;
        var uriString = isPt ? PtBrUri : EnUsUri;

        try
        {
            var uri = new Uri(uriString);
            var resourceDict = (IResourceDictionary)AvaloniaXamlLoader.Load(uri);

            var app = Application.Current;
            if (app != null)
            {
                var merged = app.Resources.MergedDictionaries;

                // Remove os dicionários de idioma prévios
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    if (merged[i] is ResourceInclude || (merged[i] is ResourceDictionary d && d.ContainsKey("App.Title")))
                    {
                        merged.RemoveAt(i);
                    }
                }

                merged.Add(resourceDict);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalizationManager] Failed to load resource dictionary {uriString}: {ex.Message}");
        }

        if (savePreference)
        {
            SaveLanguagePreference(CurrentLanguage);
        }

        LanguageChanged?.Invoke(CurrentLanguage);
    }

    /// <summary>
    /// Obtém uma string localizada pelo recurso ou retorna o fallback fornecido.
    /// </summary>
    public static string GetString(string key, string fallback = "")
    {
        if (Application.Current != null && Application.Current.TryFindResource(key, out var resource) && resource is string str)
        {
            return str;
        }
        return fallback;
    }

    private static string? LoadSavedLanguage()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Language", out var langProp))
                {
                    return langProp.GetString();
                }
            }
        }
        catch
        {
            // Silencioso se houver falha de I/O
        }
        return null;
    }

    private static void SaveLanguagePreference(string language)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var content = JsonSerializer.Serialize(new { Language = language }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, content);
        }
        catch
        {
            // Silencioso se houver falha de I/O
        }
    }
}
