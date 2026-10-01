namespace CardFactory.ProfitLoss.ReportParsing.Exceptions;

/// <summary>
/// Raised when two valid Flooid report results cannot be safely combined.
/// </summary>
public sealed class ReportMergeException : Exception
{
    public ReportMergeException(string message)
        : base(message)
    {
    }
}
