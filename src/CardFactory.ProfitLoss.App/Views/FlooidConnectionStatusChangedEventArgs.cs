using CardFactory.ProfitLoss.Flooid.Models;

namespace CardFactory.ProfitLoss.App.Views;

public sealed class FlooidConnectionStatusChangedEventArgs : EventArgs
{
    public FlooidConnectionStatusChangedEventArgs(FlooidSessionSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public FlooidSessionSnapshot Snapshot { get; }
}
