namespace GestionSite.Modules.Identite.Domain;

/// <summary>
/// Donnée hors organisation (compte de connexion, jeton de renouvellement) : un compte peut appartenir
/// à plusieurs organisations. Ni filtre d'organisation ni RLS d'organisation ; accès par compte seulement.
/// </summary>
internal interface IDonneeGlobale;
