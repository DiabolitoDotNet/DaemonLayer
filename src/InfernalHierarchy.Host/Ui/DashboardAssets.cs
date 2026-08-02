using System;
using System.IO;

namespace InfernalHierarchy.Host.Ui;

internal static partial class DashboardAssets
{
    private static readonly Lazy<string> _indexHtml = new(
        () => LayoutPrefix
            + PageHome
            + PagePerf
            + PageTimeline
            + PagePlayground
            + PagePersonas
            + PageDocs
            + PageMigrate
            + LayoutSuffix,
        isThreadSafe: true);

    private static readonly Lazy<string> _stylesCss = new(() => LoadAssetText("styles.css"), isThreadSafe: true);
    private static readonly Lazy<string> _appJs = new(() => LoadAssetText("app.js"), isThreadSafe: true);

    public static string IndexHtml => _indexHtml.Value;
    public static string StylesCss => _stylesCss.Value;
    public static string AppJs => _appJs.Value;

    private static string LoadAssetText(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UiAssets", fileName);
        return File.ReadAllText(path);
    }
}
