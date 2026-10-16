using CardFactory.ProfitLoss.Core.Updates;

// Usage: MakeUpdateManifest <app exe> <piece list .json to write> <version> [previous release's exe]
if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: MakeUpdateManifest <app exe> <output .json> <version> [previous exe]");
    return 2;
}

var file = File.ReadAllBytes(args[0]);
var manifest = PieceManifest.Create(file, args[2]);
File.WriteAllText(args[1], manifest.ToJson());

// Read it back the way the updater will, so a bad list fails the build rather than a till.
var check = PieceManifest.FromJson(File.ReadAllText(args[1]));
if (PieceUpdate.Plan(file, check).DownloadBytes != 0)
    throw new InvalidOperationException("The piece list does not match the file it was made from.");

var summary = $"Pieces: {check.Pieces.Count} for {file.LongLength / 1048576.0:0.0} MB, version {check.Version}";

// With the previous release's exe: do what a till on that version will do - plan, take the
// missing ranges, rebuild - and check the result is this file byte for byte.
if (args.Length == 4 && File.Exists(args[3]))
{
    var previous = File.ReadAllBytes(args[3]);
    var plan = PieceUpdate.Plan(previous, check);
    var downloaded = plan.Downloads.ToDictionary(d => d.Offset, d => file.AsSpan((int)d.Offset, (int)d.Length).ToArray());
    using var rebuilt = new MemoryStream();
    PieceUpdate.Assemble(previous, check, plan, downloaded, rebuilt);
    if (!rebuilt.ToArray().AsSpan().SequenceEqual(file))
        throw new InvalidOperationException("Rebuilding from the previous release did not give this file.");
    summary += $"; from the previous release: {plan.DownloadBytes / 1048576.0:0.00} MB in {plan.Downloads.Count} parts, rebuilt and checked";
}

Console.WriteLine(summary);
return 0;
