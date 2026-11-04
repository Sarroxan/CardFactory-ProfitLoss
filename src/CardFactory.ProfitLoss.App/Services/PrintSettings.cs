using System.IO;
using System.Printing;
using System.Text.Json;
using CardFactory.ProfitLoss.App.Infrastructure;

namespace CardFactory.ProfitLoss.App.Services;

/// <summary>How the app prints, kept on this till (owner, 4 Nov 2026, concept M2 with M1 behind
/// "Change…"). Starts at: the till's normal Windows printer, colour, both sides flipped on the
/// short edge, one copy. Paper is always A4, fitted to the page.</summary>
public sealed class PrintChoices
{
    /// <summary>The printer's name; null for the till's normal (default) Windows printer.</summary>
    public string? Printer { get; set; }
    public bool Colour { get; set; } = true;
    public bool BothSides { get; set; } = true;
    public int Copies { get; set; } = 1;

    public PrintChoices Copy() => new() { Printer = Printer, Colour = Colour, BothSides = BothSides, Copies = Copies };
}

/// <summary>What a printer can do, so the print box only offers what will work.</summary>
public sealed record PrinterAbilities(bool Colour, bool BothSidesShortEdge);

public static class PrintSettings
{
    private static string FilePath => Path.Combine(AppFiles.Folder, "print-settings.json");

    public static PrintChoices Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new PrintChoices();
            var loaded = JsonSerializer.Deserialize<PrintChoices>(File.ReadAllText(FilePath)) ?? new PrintChoices();
            loaded.Copies = Math.Clamp(loaded.Copies, 1, 20);
            return loaded;
        }
        catch
        {
            return new PrintChoices();   // an unreadable file just means the starting settings
        }
    }

    public static void Save(PrintChoices choices)
    {
        try
        {
            Directory.CreateDirectory(AppFiles.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(choices, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not kept this time; printing still goes ahead with these settings.
        }
    }

    private static readonly EnumeratedPrintQueueTypes[] Kinds =
        { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections };

    /// <summary>The printers on this till, the normal one first.</summary>
    public static IReadOnlyList<string> PrinterNames()
    {
        try
        {
            using var server = new LocalPrintServer();
            var normal = DefaultPrinterName();
            return server.GetPrintQueues(Kinds)
                .Select(q => q.FullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => string.Equals(n, normal, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string? DefaultPrinterName()
    {
        try { return LocalPrintServer.GetDefaultPrintQueue()?.FullName; }
        catch { return null; }
    }

    /// <summary>The chosen printer, or the normal one if that printer is not there any more.</summary>
    public static PrintQueue? OpenQueue(string? name)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var server = new LocalPrintServer();
                var match = server.GetPrintQueues(Kinds)
                    .FirstOrDefault(q => string.Equals(q.FullName, name, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
            return LocalPrintServer.GetDefaultPrintQueue();
        }
        catch
        {
            return null;
        }
    }

    public static PrinterAbilities Abilities(PrintQueue queue)
    {
        try
        {
            var caps = queue.GetPrintCapabilities();
            return new PrinterAbilities(
                caps.OutputColorCapability.Contains(OutputColor.Color),
                caps.DuplexingCapability.Contains(Duplexing.TwoSidedShortEdge));
        }
        catch
        {
            return new PrinterAbilities(true, false);   // unknown: don't promise both sides
        }
    }

    /// <summary>The print settings for one job: A4, the page's own orientation, colour or black
    /// and white, both sides flipped on the short edge or one side, and the number of copies -
    /// each only as far as the printer can do it.</summary>
    public static PrintTicket Ticket(PrintQueue queue, PrintChoices choices, PageOrientation orientation)
    {
        var abilities = Abilities(queue);
        var ticket = (queue.UserPrintTicket ?? queue.DefaultPrintTicket).Clone();
        ticket.PageOrientation = orientation;
        ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
        ticket.CopyCount = Math.Clamp(choices.Copies, 1, 20);
        ticket.OutputColor = choices.Colour && abilities.Colour ? OutputColor.Color : OutputColor.Monochrome;
        ticket.Duplexing = choices.BothSides && abilities.BothSidesShortEdge ? Duplexing.TwoSidedShortEdge : Duplexing.OneSided;
        try
        {
            return queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, ticket).ValidatedPrintTicket;
        }
        catch
        {
            return ticket;
        }
    }

    /// <summary>The short words for the strip above the preview.</summary>
    public static IReadOnlyList<string> Describe(PrintQueue? queue, PrintChoices choices, PageOrientation orientation)
    {
        if (queue is null) return new[] { "No printer found on this till" };
        var abilities = Abilities(queue);
        return new[]
        {
            queue.Name,
            "A4 " + (orientation == PageOrientation.Landscape ? "landscape" : "portrait"),
            choices.Colour && abilities.Colour ? "Colour" : "Black & white",
            choices.BothSides && abilities.BothSidesShortEdge ? "Both sides (short edge)" : "One side",
            choices.Copies == 1 ? "1 copy" : $"{choices.Copies} copies",
        };
    }
}
