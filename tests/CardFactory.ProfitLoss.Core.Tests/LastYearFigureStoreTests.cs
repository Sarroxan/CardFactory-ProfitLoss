using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class LastYearFigureStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ly-store-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_folder, "last-year-figures.json");
    private static readonly DateTime Day = new(2025, 10, 23);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    [Fact]
    public void Saved_Figures_AreReadBack_ForTheSameStoreAndDays()
    {
        new LastYearFigureStore(FilePath).Save("0001 Sample Store", Day, Day, new LastYearFigures(1402, 4.86m, 2.91m));

        Assert.True(new LastYearFigureStore(FilePath).TryGet(" 0001 sample store ", Day, Day, out var figures));
        Assert.Equal(new LastYearFigures(1402, 4.86m, 2.91m), figures);
    }

    [Fact]
    public void AnotherStoreOrOtherDays_AreNotUsed()
    {
        var store = new LastYearFigureStore(FilePath);
        store.Save("0001 Sample Store", Day, Day, new LastYearFigures(1, 1m, 1m));

        Assert.False(store.TryGet("0002 Other Store", Day, Day, out _));
        Assert.False(store.TryGet("0001 Sample Store", Day.AddDays(-6), Day, out _));
    }

    [Fact]
    public void NoStoreName_KeepsNothing()
    {
        var store = new LastYearFigureStore(FilePath);
        store.Save("", Day, Day, new LastYearFigures(1, 1m, 1m));
        Assert.False(File.Exists(FilePath));
        Assert.False(store.TryGet("", Day, Day, out _));
    }

    [Fact]
    public void DamagedFile_IsIgnored_AndReplacedOnTheNextSave()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");
        var store = new LastYearFigureStore(FilePath);

        Assert.False(store.TryGet("0001 Sample Store", Day, Day, out _));
        store.Save("0001 Sample Store", Day, Day, new LastYearFigures(5, 2m, 3m));
        Assert.True(store.TryGet("0001 Sample Store", Day, Day, out var figures));
        Assert.Equal(5, figures.Transactions);
    }
}
