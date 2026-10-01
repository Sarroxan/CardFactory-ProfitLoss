using CardFactory.ProfitLoss.Flooid.Models;

namespace CardFactory.ProfitLoss.Flooid.Services;

/// <summary>
/// Defines the Flooid entry point, persistent browser-profile location and session-state rules.
/// Authentication data remains inside the WebView2 profile and is never stored in calculator files.
/// </summary>
public sealed class FlooidSessionService
{
    public static readonly Uri BackOfficeEntryUri =
        new("https://app-ingress.prod3.cfuk.gcp.flooidcloudhosting.com/backoffice/");

    public FlooidSessionService(string? localAppData = null)
    {
        var userRoot = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        UserDataFolder = Path.Combine(userRoot, "CardFactory", "ProfitLoss", "WebView2");
    }

    public string UserDataFolder { get; }

    public FlooidSessionSnapshot Evaluate(Uri? currentUri)
    {
        if (currentUri is null)
        {
            return new FlooidSessionSnapshot(FlooidConnectionState.Ready, "Flooid ready", "Sign in using the Flooid page when needed.");
        }

        if (IsAuthenticatedFlooidUri(currentUri))
        {
            return new FlooidSessionSnapshot(FlooidConnectionState.SignedIn, "Signed in", "Flooid session is active inside this application.", currentUri);
        }

        return new FlooidSessionSnapshot(FlooidConnectionState.SignedOut, "Sign-in required", "Complete the normal Flooid sign-in below.", currentUri);
    }

    /// <summary>Stage 6B.50: Flooid's post-password step - where the store is chosen.</summary>
    public static bool IsLoginFlowUri(Uri? uri) =>
        uri is not null && uri.IsAbsoluteUri
        && uri.Host.EndsWith("flooidcloudhosting.com", StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath.TrimEnd('/'), "/web/spring/login", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAuthenticatedFlooidUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !uri.Host.EndsWith("flooidcloudhosting.com", StringComparison.OrdinalIgnoreCase)) return false;
        var path = uri.AbsolutePath;

        // Stage 6B.50: /web/spring/login is still the sign-in flow, not a signed-in page.
        // A single-store account passes through it on the way to /web/spring/menu; an
        // account with several stores STOPS here on Flooid's "Select Store" page until one
        // is chosen (saved copy: a saved copy of the page). Counting
        // it as signed in showed CONNECTED while every report would fail.
        if (IsLoginFlowUri(uri)) return false;

        if (path.StartsWith("/web/spring/", StringComparison.OrdinalIgnoreCase)) return true;
        if (!path.StartsWith("/backoffice", StringComparison.OrdinalIgnoreCase)) return false;

        // The bare entry route is also used before/while Flooid is establishing
        // authentication. Treat only concrete backoffice actions/pages as signed in.
        return !string.Equals(path, "/backoffice", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, "/backoffice/", StringComparison.OrdinalIgnoreCase);
    }
}
