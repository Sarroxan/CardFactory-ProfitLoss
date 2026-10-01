namespace CardFactory.ProfitLoss.ReportParsing.Exceptions;

/// <summary>
/// Raised when a Flooid report does not match the strict structure expected by the parser.
/// </summary>
public sealed class ReportParseException : Exception
{
    public ReportParseException(string message)
        : base(message)
    {
    }

    public ReportParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
