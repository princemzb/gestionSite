using GestionSite.Web;
using GestionSite.Web.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GestionSite.Api.Tests;

/// <summary>Limite par adresse du formulaire public (S08-04) : en-tête du relais, envois aboutis seulement, fenêtre de 10 minutes.</summary>
public sealed class LimiteParAdresseTests
{
    [Fact]
    public void BehindTheRelayEachVisitorAddressHasItsOwnLimitForTenMinutes()
    {
        var horloge = new HorlogeReglable();
        var limite = new LimiteParAdresse(Options.Create(new WebOptions { LimiteDepotsParAdresse = 1, EnteteAdresseClient = "CF-Connecting-IP" }), horloge);
        var premier = Visiteur("203.0.113.1");
        var second = Visiteur("203.0.113.2");

        limite.Compter(premier);

        Assert.False(limite.Permis(premier));
        Assert.True(limite.Permis(second)); // même connexion au conteneur, autre visiteur
        horloge.Avancer(TimeSpan.FromMinutes(10));
        Assert.True(limite.Permis(premier));
    }

    [Fact]
    public void WithoutTheRelayHeaderTheConnectionAddressCounts()
    {
        var limite = new LimiteParAdresse(Options.Create(new WebOptions { LimiteDepotsParAdresse = 1 }), new HorlogeReglable());
        var visiteur = Visiteur("198.51.100.7");

        limite.Compter(visiteur);

        Assert.False(limite.Permis(Visiteur("198.51.100.7")));
        Assert.True(limite.Permis(Visiteur("198.51.100.8")));
    }

    private static DefaultHttpContext Visiteur(string adresse)
    {
        var contexte = new DefaultHttpContext();
        contexte.Request.Headers["CF-Connecting-IP"] = adresse;
        contexte.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(adresse);
        return contexte;
    }

    private sealed class HorlogeReglable : TimeProvider
    {
        private DateTimeOffset _maintenant = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _maintenant;

        internal void Avancer(TimeSpan duree) => _maintenant += duree;
    }
}
