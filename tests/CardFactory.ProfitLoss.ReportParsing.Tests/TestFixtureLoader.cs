namespace CardFactory.ProfitLoss.ReportParsing.Tests;

internal static class TestFixtureLoader
{
    public static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(path);
    }
}
