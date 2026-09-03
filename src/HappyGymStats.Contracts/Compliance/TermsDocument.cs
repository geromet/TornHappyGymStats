namespace HappyGymStats.Contracts.Compliance;

/// <summary>
/// The published key-terms disclosure, as one fact in one place.
///
/// Three things must agree or the disclosure is worthless: the file in the
/// repository (<c>docs/torn-api/terms-of-service.md</c>), the page the site
/// serves, and the version stamped on a member's consent record. Drift between
/// them is not cosmetic — consent recorded against a version nobody can produce
/// cannot be honoured, and a page that disagrees with the published document is
/// the breach the whole gate exists to prevent.
///
/// <c>scripts/verify/w07-key-vault-contract.sh</c> asserts this constant matches
/// the markdown file.
/// </summary>
public static class TermsDocument
{
    /// <summary>
    /// Semantic version of the disclosure. Bump the MAJOR when a change alters
    /// what members agreed to — new data collected, a new key usage, a weakened
    /// promise — because that is what forces re-consent. Typos and formatting
    /// take a PATCH and force nothing.
    /// </summary>
    public const string Version = "2.0.0";

    /// <summary>Date this version was published, ISO-8601.</summary>
    public const string PublishedOn = "2026-09-04";
}
