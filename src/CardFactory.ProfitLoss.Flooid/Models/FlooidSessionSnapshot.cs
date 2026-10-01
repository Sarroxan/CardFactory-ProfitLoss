namespace CardFactory.ProfitLoss.Flooid.Models;

public sealed record FlooidSessionSnapshot(
    FlooidConnectionState State,
    string StatusText,
    string Detail,
    Uri? CurrentUri = null)
{
    public bool IsSignedIn => State == FlooidConnectionState.SignedIn;
}
