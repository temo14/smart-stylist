namespace SmartStylist.Modules.Catalog.Tests;

internal static class FixtureFile
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
