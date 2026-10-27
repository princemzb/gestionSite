# Sécurité et protection des données
Statut : exigences techniques proposées ; ne vaut pas avis juridique ni audit de conformité.

## Données à inventorier
Identité et contact des déclarants, comptes, photos, plans, justificatifs, journaux, dernière position temporaire. Pour chaque catégorie : finalité, accès, fournisseur destinataire, durée active, archive, sauvegarde et procédure de suppression. Les durées ne sont pas encore validées.

## Mesures proposées
- Contrôle serveur organisation/site/objet sur toute opération ; refus par défaut.
- Stockage local protégé et compartimenté par compte ; aucun affichage du profil précédent après changement.
- Mot de passe traité via mécanisme d’authentification éprouvé ; aucune donnée secrète en clair dans logs ou dépôt.
- Téléversements bornés, types vérifiés, fichiers privés ; traitement des photos avant diffusion.
- Formulaire sans vérification conservé, avec protections d’abus techniques ; ne pas ajouter de vérification d’identité non demandée.
- Support avec périmètre, expiration et journal de ses actions.
- Photos et textes envoyés à l’IA limités au nécessaire ; identité/contact non transmis par défaut.
- Contenu web/IA traité comme donnée non fiable ; aucun lien marchand ne peut déclencher un achat ou une action privilégiée.
- Position : activation visible, arrêt, expiration et absence de collecte dans l’analytique.

## Menaces et preuves
| Menace | Mesure | Test |
| --- | --- | --- |
| Lecture d’un autre client | Filtrage et autorisation objet | T-01 |
| Tablette perdue | Protection locale, révocation au retour réseau, limite 24 h | T-07 |
| Dossier financier modifié | Permissions, révisions, audit | T-09 |
| Injection via réponse IA ou page produit | Validation structurée et absence d’exécution | T-10 |
| Fuite de trajet via logs | Exclusion coordonnées des journaux et traces | T-12 |
| Spam public | Limites et surveillance sans compte imposé | T-02 |

Le décalage entre révocation serveur et appareil hors connexion doit être accepté explicitement. Le MFA différé est une décision produit connue, à réexaminer avant ouverture commerciale.
Utiliser [OWASP ASVS](https://asvs.owasp.org/) comme référentiel pour sélectionner et tracer les contrôles web/API ; aucun niveau de certification n’est revendiqué.

