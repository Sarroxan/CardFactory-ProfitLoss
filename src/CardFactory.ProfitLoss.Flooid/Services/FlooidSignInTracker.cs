using CardFactory.ProfitLoss.Flooid.Models;

namespace CardFactory.ProfitLoss.Flooid.Services;

/// <summary>
/// Tells a real sign in apart from an ordinary page change while signed in.
///
/// Owner, 16 Oct 2026: "Open Flooid back office" closed the window and refreshed everything.
/// Every navigation first reports "Opening Flooid…" (NavigationStarting), and the main window
/// counted that as having left signed in, so the page arriving a moment later looked like a
/// fresh sign in: the window was hidden and all the reports were pulled again. Opening the back
/// office navigates to its home page, and every link clicked in it navigates too.
///
/// A page still loading, or Flooid's bare /backoffice/ entry on its way to a page, says nothing
/// about the session, so neither changes what is remembered. The login page (signed out) does.
/// </summary>
public sealed class FlooidSignInTracker
{
    public bool WasSignedIn { get; private set; }

    /// <returns>True only when this snapshot is the move into signed in.</returns>
    public bool Update(FlooidSessionSnapshot snapshot)
    {
        var signedIn = snapshot.State == FlooidConnectionState.SignedIn;
        var justSignedIn = signedIn && !WasSignedIn;
        if (!IsOnTheWay(snapshot)) WasSignedIn = signedIn;
        return justSignedIn;
    }

    private static bool IsOnTheWay(FlooidSessionSnapshot snapshot) =>
        snapshot.State == FlooidConnectionState.NotInitialised
        || (snapshot.State == FlooidConnectionState.SignedOut && IsBareBackOfficeEntry(snapshot.CurrentUri));

    private static bool IsBareBackOfficeEntry(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Host.EndsWith("flooidcloudhosting.com", StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath.TrimEnd('/'), "/backoffice", StringComparison.OrdinalIgnoreCase);
}
