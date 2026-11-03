using GestionSite.Modules.Identite.Contracts;
using GestionSite.Modules.Maintenance.Domain;
using GestionSite.Modules.Maintenance.Notifications;

namespace GestionSite.Modules.Tests;

/// <summary>Entrée du centre de notifications et destinataires (US-10, S10-01, Q3-A).</summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Instant = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANotificationTargetsACaseOfItsSiteAndKeepsItsFirstReadDate()
    {
        var dossier = Dossier();
        var compte = Guid.NewGuid();
        var notification = new Notification(Guid.NewGuid(), dossier, compte, TypeNotification.DangerSignale, "danger:1", Instant);

        Assert.True(notification.MarquerLue(Instant.AddMinutes(5)));
        Assert.False(notification.MarquerLue(Instant.AddHours(1)));
        Assert.Equal(Instant.AddMinutes(5), notification.LueLe);
        Assert.Equal((dossier.OrganisationId, dossier.SiteId, ObjetNotification.Dossier, dossier.Id),
            (notification.OrganisationId, notification.SiteId, notification.Objet, notification.ObjetId));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), dossier, Guid.Empty, TypeNotification.Affectation, "a", Instant));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), dossier, compte, TypeNotification.Affectation, string.Empty, Instant));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), dossier, compte, TypeNotification.Affectation, new string('a', 201), Instant));
    }

    [Fact]
    public void ADangerAlertGoesToManagersPastoralStaffAndThePeopleInChargeOnly()
    {
        var responsable = Membre("Responsable");
        var delegue = Membre("Delegue");
        var pasteur = Membre("Pasteur");
        var coordination = Membre("Coordination");
        var technicienEnCharge = Membre("Technicien");
        var autreTechnicien = Membre("Technicien");
        var destinataires = new DestinatairesSite([responsable, delegue, pasteur, coordination, technicienEnCharge, autreTechnicien]);

        Assert.Equal([responsable.CompteId, delegue.CompteId], destinataires.ResponsablesDelegues());
        Assert.Equal(
            [responsable.CompteId, delegue.CompteId, pasteur.CompteId, coordination.CompteId, technicienEnCharge.CompteId],
            destinataires.AlerteDanger([technicienEnCharge.CompteId, Guid.NewGuid()])); // un ancien membre en charge n'est pas notifié
    }

    private static MembreSite Membre(string role) => new(Guid.NewGuid(), role, [role]);

    private static Signalement Dossier() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), NatureSignalement.Incident, "Fuite", "Nef", true, false, "Anne", "06", Instant);
}
