using System.Text.Json;
using CardFactory.ProfitLoss.Storage.Models;

namespace CardFactory.ProfitLoss.Storage.Services;

public sealed class PortableStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PortableStateService(string? localAppData = null)
    {
        var userRoot = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AutoSavePath = Path.Combine(userRoot, "CardFactory", "ProfitLoss", "autosave.json");
    }

    public string AutoSavePath { get; }

    public void SaveAuto(CalculatorStateDocument state) => Save(AutoSavePath, state);
    public Task SaveAutoAsync(CalculatorStateDocument state) => SaveAsync(AutoSavePath, state);
    public Task<CalculatorStateDocument?> LoadAutoAsync() => LoadAsync(AutoSavePath);

    /// <summary>
    /// Stage 6A.92: removes the automatic state file. The application no longer carries
    /// anything from one session to the next, so this runs on close. Failures are
    /// swallowed deliberately - a file that cannot be deleted must not stop the
    /// application shutting down, and nothing reads it on startup any more regardless.
    /// </summary>
    public void ClearAuto()
    {
        try
        {
            if (File.Exists(AutoSavePath)) File.Delete(AutoSavePath);
        }
        catch
        {
        }
    }

    public void Save(string path, CalculatorStateDocument state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        WriteAtomic(path, json);
    }

    public async Task SaveAsync(string path, CalculatorStateDocument state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        ReplaceTempFile(tempPath, path);
    }

    public async Task<CalculatorStateDocument?> LoadAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CalculatorStateDocument>(stream, JsonOptions).ConfigureAwait(false);
    }

    private static void WriteAtomic(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        ReplaceTempFile(tempPath, path);
    }

    private static void ReplaceTempFile(string tempPath, string path)
    {
        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
