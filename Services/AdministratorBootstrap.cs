using Sati.Models;

namespace Sati.Services
{
    /// <summary>
    /// The rules for creating the FIRST administrator on an installation.
    ///
    /// WHY THIS EXISTS. A Sati database can reach a state where nobody can
    /// administer it: a fresh install, or a real install whose only account is a
    /// case manager. Without a way out, the owner of the data cannot create a
    /// supervisor, cannot promote anyone, and cannot recover except by someone
    /// editing the database by hand.
    ///
    /// WHAT IT DELIBERATELY IS NOT. No account is ever created automatically, and
    /// no password is ever defaulted, generated-and-stored, or shipped. An
    /// administrator that exists on every install with a password the software
    /// knows is a backdoor on every install — the single most reliably exploited
    /// defect in self-hosted software. Sati instead REFUSES to have an
    /// administrator until a human sits down and chooses a password for one.
    ///
    /// The window is therefore narrow and self-closing: the bootstrap path is open
    /// only while no administrator exists, and creating one shuts it. The check is
    /// enforced inside the service against the database, not by hiding a button —
    /// see the engineering rule about UI visibility and security.
    ///
    /// SCOPE: desktop-local installations only. There is deliberately no API route
    /// for this. An unauthenticated bootstrap endpoint on a hosted, multi-tenant
    /// service is a different and much worse proposition than one on a database the
    /// caller already has direct access to. Hosted environments provision their
    /// first administrator with scripts/Provision-DemoGlobalAdmin.ps1, run by an
    /// operator who is already trusted with the database.
    /// </summary>
    public static class AdministratorBootstrap
    {
        /// <summary>
        /// Minimum length for the first administrator's password.
        ///
        /// Higher than the 8 characters MyAccountViewModel accepts for ordinary
        /// self-service changes, and matched to the 16 the hosted provisioner
        /// already demands. This account can create every other account; it is the
        /// one credential where a memorable-but-weak choice is least affordable,
        /// and it is typed once at setup rather than daily.
        /// </summary>
        public const int MinimumPasswordLength = 16;

        public const int MaximumPasswordLength = 128;

        /// <summary>
        /// Everything standing between the entered details and a valid first
        /// administrator, phrased for the person reading it. Empty means valid.
        ///
        /// Takes the password LENGTH rather than the value: nothing here needs to
        /// see the characters, so nothing here is given them.
        /// </summary>
        public static IReadOnlyList<string> FindProblems(
            string? username,
            string? displayName,
            int passwordLength,
            bool passwordsMatch)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(username))
                problems.Add("A username is required.");
            else if (!IsWellFormedUsername(username))
                problems.Add("The username must be 3 to 50 characters, using lowercase letters, numbers, dots, hyphens, or underscores.");

            if (string.IsNullOrWhiteSpace(displayName))
                problems.Add("A display name is required.");

            if (passwordLength < MinimumPasswordLength)
                problems.Add($"The password must be at least {MinimumPasswordLength} characters.");
            else if (passwordLength > MaximumPasswordLength)
                problems.Add($"The password must be no more than {MaximumPasswordLength} characters.");

            if (!passwordsMatch)
                problems.Add("The two passwords do not match.");

            // The agency is resolved by the service, not chosen here. A local
            // installation has exactly one; zero or several is a database problem
            // to report, not a dropdown to offer.
            return problems;
        }

        /// <summary>
        /// Mirrors the username rule the hosted provisioner enforces, so an
        /// account created here and one created there cannot differ in shape.
        /// </summary>
        public static bool IsWellFormedUsername(string username) =>
            username.Length is >= 3 and <= 50 &&
            username.All(character =>
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character is '.' or '-' or '_');

        /// <summary>
        /// The roles that count as "somebody can administer this installation".
        /// PlatformOperator is excluded on purpose: it is a cross-tenant
        /// operational identity for Sati's own telemetry, not an agency
        /// administrator, and its presence must not convince an agency that it has
        /// an administrator of its own.
        /// </summary>
        public static bool IsAdministrator(UserRole role) => role == UserRole.Admin;
    }
}
