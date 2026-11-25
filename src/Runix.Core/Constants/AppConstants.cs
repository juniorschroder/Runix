using System;
using System.IO;

namespace Runix.Core.Constants;

public static class AppConstants
{
    public const string AppName = "RUNIX Database Studio";
    public const string AppVersion = "2.0.0";
    public const string AppAuthor = "José Carlos Schröder Júnior";
    public const string AppCopyright = "© 2026 José Carlos Schröder Júnior";
    public const string AppLicense = "GPL-3.0 License";
    public const string AppRepositoryUrl = "https://github.com/juniorschroder/Runix";
    public const string AppAuthorLinkedInUrl = "https://www.linkedin.com/in/juniorschroder/";

    public static string DefaultSnapshotsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runix", "snapshots");

    public const int WindowDefaultWidth = 1200;
    public const int WindowDefaultHeight = 800;
    public const int WindowMinWidth = 800;
    public const int WindowMinHeight = 600;
    public const int PrimarySidebarMinWidth = 150;
    public const int SecondarySidebarMinWidth = 150;
    public const int MainContentMinWidth = 200;
    public const int MaxResults = 50000;
    public const int IdleThreshold = 300;
    public const int IdleCheckInterval = 30;
}
