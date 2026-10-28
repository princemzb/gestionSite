using GestionSite.Modules.Identite.Authentification;
using GestionSite.Modules.Identite.Domain;

namespace GestionSite.Modules.Tests;

public sealed class JetonRenouvellementTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UsableUntilExpiryReplacementOrRevocation()
    {
        var jeton = Nouveau();

        Assert.True(jeton.EstUtilisable(Instant.AddDays(13)));
        Assert.False(jeton.EstUtilisable(Instant.AddDays(14)));

        jeton.Remplacer(Guid.NewGuid(), Instant.AddHours(1));
        Assert.False(jeton.EstUtilisable(Instant.AddHours(2)));
        Assert.Equal(Instant.AddHours(1), jeton.RevoqueLe);
    }

    [Fact]
    public void RevocationKeepsTheFirstDate()
    {
        var jeton = Nouveau();

        jeton.Revoquer(Instant.AddHours(1));
        jeton.Revoquer(Instant.AddHours(2));

        Assert.Equal(Instant.AddHours(1), jeton.RevoqueLe);
    }

    [Fact]
    public void InvalidTokensAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new JetonRenouvellement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new byte[16], Instant, Instant.AddDays(1)));
        Assert.Throws<ArgumentNullException>(() => new JetonRenouvellement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null!, Instant, Instant.AddDays(1)));
        Assert.Throws<ArgumentException>(() => new JetonRenouvellement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new byte[32], Instant, Instant));
        Assert.Throws<ArgumentException>(() => new JetonRenouvellement(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), new byte[32], Instant, Instant.AddDays(1)));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("pas du base64", false)]
    [InlineData("c2hvcnQ=", false)] // 5 octets
    [InlineData("bG9jYWwtZGV2LXNpZ25pbmcta2V5LW5vdC1zZW5zaXRpdmUtMDA=", true)]
    public void SigningKeyMustBeBase64OfAtLeast32Bytes(string cle, bool valide)
    {
        Assert.Equal(valide, new AuthentificationOptions { CleSignature = cle }.CleValide());
    }

    private static JetonRenouvellement Nouveau() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new byte[32], Instant, Instant.AddDays(14));
}
