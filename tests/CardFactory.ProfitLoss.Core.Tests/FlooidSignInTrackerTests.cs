using CardFactory.ProfitLoss.Flooid.Models;
using CardFactory.ProfitLoss.Flooid.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class FlooidSignInTrackerTests
{
    private const string Host = "https://app-ingress.prod3.cfuk.gcp.flooidcloudhosting.com";
    private static readonly FlooidSessionService Session = new("unused");

    // What FlooidView publishes: "Opening Flooid…" as a navigation starts, then the page's own state.
    private static FlooidSessionSnapshot Starting(string path) =>
        new(FlooidConnectionState.NotInitialised, "Opening Flooid…", "", new Uri(Host + path));

    private static FlooidSessionSnapshot Arrived(string path) => Session.Evaluate(new Uri(Host + path));

    [Fact]
    public void SigningIn_FromTheLoginPage_IsASignIn()
    {
        var tracker = new FlooidSignInTracker();
        Assert.False(tracker.Update(Arrived("/authserver/login")));
        Assert.False(tracker.Update(Starting("/web/spring/menu")));
        Assert.True(tracker.Update(Arrived("/web/spring/menu?execution=e1s1")));
    }

    [Fact]
    public void OpeningTheBackOffice_WhileSignedIn_IsNotASignIn()
    {
        var tracker = new FlooidSignInTracker();
        tracker.Update(Arrived("/authserver/login"));
        Assert.True(tracker.Update(Arrived("/web/spring/menu?execution=e1s1")));

        // "Open Flooid back office": the entry page, then the home page.
        Assert.False(tracker.Update(Starting("/backoffice/")));
        Assert.False(tracker.Update(Arrived("/backoffice/")));
        Assert.False(tracker.Update(Starting("/web/spring/menu")));
        Assert.False(tracker.Update(Arrived("/web/spring/menu?execution=e2s1")));

        // A link clicked in the back office.
        Assert.False(tracker.Update(Starting("/web/spring/redirect?server=backoffice&inputUrl=/x.action")));
        Assert.False(tracker.Update(Arrived("/backoffice/refundsVoidsAndNoSalesReportCriteria.action")));
    }

    [Fact]
    public void SessionRunningOut_ThenSigningInAgain_IsASignIn()
    {
        var tracker = new FlooidSignInTracker();
        tracker.Update(Arrived("/authserver/login"));
        tracker.Update(Arrived("/web/spring/menu?execution=e1s1"));

        Assert.False(tracker.Update(Starting("/authserver/login")));
        Assert.False(tracker.Update(Arrived("/authserver/login")));
        Assert.True(tracker.Update(Arrived("/web/spring/menu?execution=e3s1")));
    }
}
