namespace GestionSite.Modules.Identite.Domain;

internal static class Identifiants
{
    internal static void ExigerNonVide(Guid id, string paramName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant ne peut pas être vide.", paramName);
        }
    }
}
