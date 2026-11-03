# Catalogue des règles métier
V = validée ; P = proposition complémentaire. Les détails ouverts sont dans le registre des arbitrages.

| ID | Statut | Règle |
| --- | --- | --- |
| RG-01 | V | Organisations séparées ; accès aux sites selon habilitation. |
| RG-02 | V | Dépôt public sans compte ni vérification : nom, contact, site, emplacement, description ; photo facultative ; aucun suivi public. |
| RG-03 | V | Un QR général par site ; aucun accès public aux plans ou à l’inventaire. |
| RG-04 | V | Responsable/délégués qualifient, priorisent et affectent ; pasteurs/coordination consultent, déclarent et relancent. |
| RG-05 | V | Danger signalé : alerte avant qualification et suivi distinct de la mise en sécurité. |
| RG-06 | V | Technicien affecté clôture avec actions et état final ; photo selon catégorie. Responsable peut rouvrir. |
| RG-07 | V | Attente : motif et réexamen ; délai de résolution suspendu, ancienneté conservée. |
| RG-08 | V | Rappels puis escalade selon priorité ; notifications internes et push. Push reporté à US-13 (décision du porteur du 02/11, section US-10). |
| RG-09 | V | Préventif calendaire/ponctuel ; non-conformité génère un signalement. |
| RG-10 | V | Inventaire individuel et lots, avec quantités réparties par état. |
| RG-11 | V | Note sur 10, moyenne par niveaux, dernière évaluation conservée, fraîcheur et couverture visibles. |
| RG-12 | V | Arrondir uniquement l’affichage à une décimale ; conserver précision de calcul ; sans évaluation, aucune note. |
| RG-13 | V | IA lancée par utilisateur technique habilité ; informations insuffisantes : compléments, sans total global. |
| RG-14 | V | Produits France, fournisseurs ouverts, proposition principale et jusqu’à deux alternatives. |
| RG-15 | V | Responsable/délégué valide estimation et solution ; envoi PDF déclenché explicitement et tracé. |
| RG-16 | V | Enveloppe par site/période ; réservation dès autorisation ; pas de plafond par dossier. |
| RG-17 | V | Dépassement : accord préalable même urgent, limité au dossier et au montant ; justificatif requis. |
| RG-18 | V | Dépense réelle : montant, date, fournisseur, justificatif, dossier ; ajuster la réservation. |
| RG-19 | V | Demande matérielle suivie jusqu’à remise/installation ; achat réalisé hors application. |
| RG-20 | V | Transfert : départ puis réception ; état en transit entre les deux. |
| RG-21 | V | Technicien consomme en intervention ; responsable/délégués réceptionnent, ajustent et mettent au rebut. |
| RG-22 | V | Surconsommation hors connexion : garder les faits, signaler l’écart et régulariser après comptage. |
| RG-23 | V | Hors connexion 24 h, profils préparés, déverrouillage personnel ; conserver les saisies à expiration. |
| RG-24 | V | Ajouts compatibles conservés ; conflits de valeurs soumis à arbitrage, sans écrasement silencieux. |
| RG-25 | V | Position personnelle hors connexion à démontrer ; recalage puis choix manuel en cas d’incertitude. |
| RG-26 | V | Partage explicite, suspendable, limité à l’équipe habilitée du site ; aucun trajet conservé. |
| RG-27 | V | Rapports à la demande, filtrés selon les droits, export PDF. |
| RG-28 | V | Support temporaire autorisé et tracé ; délégation opérationnelle distincte d’administration. |
| RG-29 | V | Email/mot de passe initial ; MFA et fédération dans des lots ultérieurs. |
| RG-30 | V | Ouverture SaaS sur demande ; abonnement par organisation, facturation manuelle initiale. |
| RG-31 | P | Toute correction sensible conserve l’auteur, le motif et la valeur précédente dans un journal approprié, hors trajets de position. |
| RG-32 | P | Une modification matérielle d’estimation invalide sa validation antérieure ; nouvelle autorisation si montant ou périmètre autorisé change. |

## Inventaire — décisions US-07, prises par l'agent le 29/10 (délégation du porteur jusqu'à la fin du sprint, révisables)
Choix faits en expert du bâtiment (corps d'état, pratique des contrôleurs pour un ERP de type V — lieux de culte).
- Structure : site → bâtiment → niveau (facultatif) → pièce ou zone. Chaque bâtiment reçoit automatiquement une zone « Parties communes » (DEC-01). Les extérieurs se décrivent comme un bâtiment « Extérieurs » et ses zones (parvis, parking, jardin…).
- Composant : nom, catégorie, zone (une seule), mode individuel ou lot, et les informations utiles au technicien : marque, modèle, numéro de série, date de mise en service, emplacement précis, remarques ; indicateur « soumis à vérification périodique » (proposé d'après la catégorie, modifiable) ; état actif ou retiré. Un composant retiré sort du calcul et reste consultable (DEC-01).
- Catégories (corps d'état, liste fermée à valider avec le terrain ; « vérif. » = vérification périodique habituelle dans un ERP) :
  1. Structure et gros œuvre (fondations, murs porteurs, planchers, charpente, clocher) ;
  2. Couverture et étanchéité (toiture, zinguerie, gouttières, descentes d'eaux pluviales) ;
  3. Menuiseries extérieures et vitraux (portes, fenêtres, vitraux) ;
  4. Menuiseries intérieures et serrurerie (portes, issues de secours, ferme-portes, cloisons) ;
  5. Revêtements et finitions (peintures, enduits, sols, plafonds, faïences) ;
  6. Électricité courants forts (TGBT, tableaux, prises, éclairage normal, mise à la terre) — vérif. ;
  7. Éclairage de sécurité (BAES, BAEH, blocs d'ambiance) — vérif. ;
  8. Sécurité incendie (alarme et SSI, extincteurs, désenfumage, RIA, plans et signalétique d'évacuation) — vérif. ;
  9. Courants faibles (sonorisation, vidéo, informatique, contrôle d'accès, téléphonie) ;
  10. Chauffage, ventilation, climatisation (chaudière, émetteurs, CTA, VMC) — vérif. pour les chaudières ;
  11. Plomberie et sanitaires (eau froide et chaude, sanitaires, ballon, évacuations) ;
  12. Gaz (installation et appareils au gaz) — vérif. ;
  13. Ascenseurs et élévateurs — vérif. ;
  14. Paratonnerre et protection foudre — vérif. ;
  15. Campanaire (cloches, beffroi, horloge) — vérif. ;
  16. Accessibilité PMR (rampes, places réservées, sanitaires adaptés, bandes d'éveil) ;
  17. Mobilier (chaises, bancs, tables, rangements) ;
  18. Équipements de culte (autel, chaire, baptistère, orgue) ;
  19. Extérieurs et voirie (clôtures, portails, voirie, éclairage extérieur, espaces verts) ;
  20. Autre.
- Lot : unité (unité, m², mètre linéaire, m³, ensemble), quantité totale entière positive, répartition facultative par état de l'échelle commune (Bon, Satisfaisant, Dégradé, Mauvais, Critique). Si une répartition est saisie, la somme des quantités par état égale la quantité totale (RG-10) ; sans répartition, le lot est « non évalué ». Les évaluations versionnées viendront avec US-11.
- Historique : journal en ajout seul de chaque création, modification (valeurs avant et après) et retrait, avec auteur et date.
- Photos des composants : reportées aux évaluations (US-11), qui portent les photos de constat.
- Vérifications périodiques : l'inventaire ne fait que les signaler ; leur calendrier, les rapports et organismes relèvent d'US-12 (préventif et inspections).
- Écrans : application web (ordinateur) en premier ; la tablette viendra avec US-13.
- Droits (PERMISSIONS, déjà V) : lecture pour les comptes internes du site ; création et correction (structure et composants) pour technicien, responsable et délégué du site (`ModifierInventaire`), tracées.
- Précisions S07-02 (agent, révisables) : mise en service saisie en année, mois facultatif (plaque signalétique ; extincteurs et BAES se suivent à l'année), jamais future, dès l'an 1000 (cloches, charpentes) ; « soumis à vérification » proposé pour courants forts, éclairage de sécurité, sécurité incendie, chauffage (chaudières), gaz, ascenseurs, paratonnerre et campanaire ; une modification part de la version lue (deux personnes ne s'écrasent pas) ; le retrait exige un motif (déposé, remplacé, cédé…) et est définitif ; un composant retiré ne se modifie plus. Pas d'unicité des noms de composants (deux « Extincteur CO2 2 kg » dans une même zone sont possibles, le n° de série les distingue).
- Reporté : type et catégorie d'ERP du site (utile aux obligations réglementaires), à décider avec US-12.

## Dépôt public — décisions US-08, V, 28 octobre 2026 (porteur du projet)
- Photos : jusqu’à 3 par dépôt, JPEG, PNG ou HEIC, 10 Mo au plus chacune, 12 000 px de côté et 50 Mpx au plus (au-delà : « Image trop grande », jamais « illisible » ; SOC-04) ; métadonnées (dont la position GPS) retirées à la réception.
- Anti-abus : Turnstile (Cloudflare, ADR-08) vérifié côté serveur, derrière une interface remplaçable, plus une limite de dépôts par adresse ; aucune vérification d’identité.
- QR code et lien public d’un site : affichables et imprimables par l’administrateur (tous les sites) et par responsable et délégués (leurs sites).
- Dépôts reçus : liste en lecture par site dès US-08, pour les comptes habilités sur le site (T, R/D, P/C, conformément à DEC-04) ; leur traitement relève d’US-09.
- Numéro à appeler en cas de danger : saisi par site (administrateur) ; affiché au déclarant public qui signale un danger, seulement s'il est renseigné (décision du 28/10, maquettes UX-03).
- Ouvert, bloquant avant la production seulement : durée de conservation des coordonnées des déclarants publics (données personnelles, SECURITE).

## Traitement des dossiers — US-09, V, 29 octobre 2026 (porteur du projet)
Acquis (V) : cycle à 7 statuts et motifs d'attente et de classement (DEC-03, PROCESSUS) ; priorités et délais (DEC-05) ; qualification, priorité et affectation par R/D, clôture par le technicien affecté, réouverture par R/D (RG-04, RG-06) ; attente suspendant le délai de résolution, ancienneté conservée (RG-07) ; contact du déclarant lisible par T, R/D, P/C du site (DEC-04).
Décisions du 29/10 (QCM, recommandations de l'agent retenues) :
- Intervenant : technicien interne du site, ou prestataire extérieur nommé dont le suivi est assuré par un référent interne (technicien ou R/D du site).
- Qualification : zone de l'inventaire obligatoire, composant facultatif.
- Clôture d'une intervention : actions réalisées, état final parmi « Résolu », « Mesure provisoire » (mis en sécurité, à reprendre) et « Non résolu » ; photo après intervention obligatoire pour les corps d'état électricité courants forts, éclairage de sécurité, sécurité incendie, gaz, couverture et étanchéité, structure et gros œuvre (déduits du composant rattaché ; sans composant, photo facultative). L'évaluation de l'état d'un composant reste celle d'US-11.
- Déclaration interne : tout compte interne du site (T, R/D, P/C) déclare un problème par un formulaire simple (zone, description, danger, activité bloquée, photos) ; le dossier arrive « À qualifier » comme un dépôt public.
- Écrans (maquettes UX-05) : suivi des dossiers en colonnes par statut, qualification dans un panneau (B) ; dossier avec barre d'étapes et onglets Constat / Interventions / Historique / Déclarant (B) ; « Mes interventions » du technicien en liste par priorité avec délai restant (B), web adapté au téléphone (tablette hors ligne : US-13).
Découpage : S09-01 dossier et qualification (dont doublons et classement sans suite) ; S09-02 affectation et interventions ; S09-03 attente et reprise ; S09-04 clôture et réouverture ; S09-05 écrans web et préproduction. Hors périmètre : alertes danger, rappels et escalades (US-10), estimation et budget (US-17 à US-19), consommation de stock (US-15).

## Évaluations et score — US-11, V, 29 octobre 2026 (porteur du projet)
Acquis (V) : DEC-01 (échelle Bon 9 / Satisfaisant 7 / Dégradé 5 / Mauvais 3 / Critique 1 ; moyennes composants → zone → site ; lot pondéré par les quantités ; Parties communes comptées comme une zone ; évaluation « ancienne » au-delà de 6 mois, toujours comptée ; une décimale à l'affichage, libellé sur la valeur arrondie) ; RG-11, RG-12 ; grilles modifiées par R/D avec version (PERMISSIONS).
Décisions du 29/10 (QCM, recommandations de l'agent retenues) :
- Évaluateurs : technicien, responsable et délégué du site ; pasteur et coordination consultent.
- Grilles : une grille par défaut par corps d'état, rédigée par l'agent en expert du bâtiment (critères à observer, mesures utiles avec unité) ; R/D l'adaptent pour leur site (choix de l'agent du 29/10, révisable : PERMISSIONS donne ce droit par site, une adaptation n'atteint donc jamais un site où son auteur n'a pas de rôle), chaque modification crée une version ; une évaluation garde la version utilisée et le texte des points présentés.
- Photo à l'évaluation : obligatoire pour « Mauvais » et « Critique », facultative sinon (3 au plus, mêmes règles que les dépôts).
- « Critique » : l'application propose d'ouvrir un dossier prérempli (zone, composant, constat), sans création automatique.
- Lot : l'évaluateur saisit la répartition par état (somme = quantité) ; la note est la moyenne pondérée (DEC-01) et la répartition du lot dans l'inventaire devient celle de la dernière évaluation.
- Historique du score : recalculé à toute date à partir des évaluations datées (courbe mensuelle), au lieu d'un instantané stocké ; remplace l'instantané mensuel de DEC-01. Précisions de l'agent (29/10, révisables) : composant compté dans sa zone actuelle sur toute la courbe ; mois arrêtés à minuit, heure de Paris ; part ancienne = poids des composants à évaluation ancienne dans la note du site.
- Écrans (maquettes UX-06, choix du porteur du 29/10) : tableau de l'état du site avec la note en tête (couverture, fraîcheur, points critiques) puis une carte par zone, la moins bien notée d'abord (A) ; évaluation sur téléphone pas à pas : points à observer et mesures, état ou répartition, puis photos et remarque (A) ; fiche composant avec dernière évaluation et historique (commune).
Découpage : S11-01 grilles par défaut et versions ; S11-02 évaluations (état ou répartition, critères, mesures, photos, proposition de dossier) ; S11-03 score (zone, site, couverture, fraîcheur, historique) ; S11-04 écrans web et préproduction (maquettes UX-06 d'abord).

## Préventif et inspections — US-12, V, 30 octobre 2026 (porteur du projet)
Cadrage complet : docs/pilotage/cadrage/US-12.md. Décisions du 30/10 (QCM, recommandations de l'agent retenues) :
- Plans réglementaires par défaut : proposés par l'application avec la mention « à confirmer » et leur référence ; R/D les confirment un à un ; une proposition écartée reste visible avec son motif. L'application n'endosse jamais la responsabilité de l'exploitant et n'affiche jamais « conforme ».
- Inspection interne d'un composant : grille du corps d'état et évaluation US-11 enregistrée dans la même saisie (état, photo pour Mauvais et Critique, score mis à jour).
- Droits : « Planifier le préventif » R/D ; « Exécuter un contrôle » technicien affecté et R/D ; rapport d'organisme saisi par R/D ou le référent interne ; consultation (rapports compris) T, R/D, P/C du site.
- Rapport PDF : obligatoire pour une vérification réglementaire faite par un intervenant extérieur, facultatif ailleurs ; 10 Mo au plus ; conservé tant que le bâtiment existe dans l'application.
- Périmètre : complet, en 7 sous-cartes (S12-01 classement ERP et catalogue, S12-02 plans et échéances, S12-03 exécution, S12-04 vérification extérieure et PDF, S12-05 non-conformités vers dossiers et levée des réserves, S12-06 écrans de planification, S12-07 écrans de terrain ; maquettes validées avant tout code d'écran).
- Maquettes (S12-06, S12-07, choix du porteur du 30/10) : échéancier du site en calendrier mensuel avec la liste des retards à côté (B) ; contrôle sur téléphone pas à pas, même parcours que l'évaluation (A) ; écrans communs validés : plans proposés à confirmer avec le classement ERP du bâtiment, saisie d'une vérification extérieure avec observations et rapport PDF.
Règles de l'agent (révisables) : classement ERP porté par chaque bâtiment (type, catégorie déclarée, drapeaux), « salariés présents » sur le site ; échéance glissante par défaut (date réelle + période), fixe en option ; fenêtre « À faire » de 10 % de la période (7 à 30 jours), aucune tolérance après l'échéance (« En retard ») ; échéances calculées à la lecture, sans tâche planifiée ; occurrence enregistrée seulement à l'affectation, la réalisation ou le saut (clé plan + échéance, rejeu sans doublon) ; aucune notification envoyée (US-10).

## Danger, attentes et relances — US-10, V, 2 novembre 2026 (porteur du projet)
Cadrage complet : docs/pilotage/cadrage/US-10.md. Acquis (V) : RG-05, RG-07, RG-08, DEC-03, DEC-05 ; « Relancer » : R/D, P/C, jamais le technicien, le pasteur relance en interne seulement (décision du 28/10). L'application n'est ni une alarme ni un système de sécurité incendie : elle ne remplace jamais l'appel des secours (18 ou 112), le numéro d'urgence du site ni les consignes affichées dans le bâtiment.

Décisions du porteur du 02/11 (QCM, Q1 à Q12) :
- **Q1 — Push : B.** Pas de push avant l'application tablette ; côté web, centre de notifications seul ; le push natif (Firebase Cloud Messaging) est décidé avec US-13. **RG-08 (push) est reporté à US-13.** Pas de push web du navigateur.
- **Q2 — Canaux externes : D.** E-mail pour les dangers (alerte, absence de prise en compte) et les escalades à 100 % ; **SMS pour les dangers**. Cela suppose un achat par le porteur (nom de domaine, fournisseur d'e-mail, fournisseur de SMS) et la collecte des numéros de mobile des membres (données personnelles : base légale, information des membres, minimisation, droit de retrait, à fixer dans la sous-carte S10-09). **S10-09 « E-mail et SMS » est bloquée par l'achat ; aucun service n'est configuré.**
- **Conséquence, tant que l'achat e-mail et SMS n'est pas fait :** une alerte de danger n'est visible que dans le **centre de notifications web, au chargement d'une page** (rendu serveur statique, ADR-08) ; personne n'est prévenu hors de l'application. Le premier réflexe reste l'appel des secours et du numéro d'urgence du site, rappelés au déclarant public. Accusé du dépôt public corrigé le 02/11 (PR #101) : « Votre signalement est enregistré et transmis aux responsables du site. En cas de danger immédiat, appelez les secours (18 ou 112). » (puis le numéro d'urgence du site s'il est renseigné) ; la tuile « Il y a un danger » (« Danger immédiat : appelez d'abord le 18 ou le 112. ») et l'en-tête du formulaire de danger (« En cas de danger immédiat, appelez d'abord les secours (18 ou 112). ») ne promettent plus d'alerte non plus.
- **Q3 — Destinataires d'une alerte de danger : A.** R/D du site (pour agir), pasteurs et coordinateurs du site (pour information) ; techniciens seulement s'ils sont en charge d'une intervention ouverte du dossier.
- **Q4 — Suivi d'une alerte : A.** Un R/D prend en compte l'alerte. Sans prise en compte en 15 min : nouvelle alerte à tous les destinataires, puis toutes les heures, jour et nuit. Après prise en compte, tant que la mise en sécurité n'est pas déclarée réalisée : rappel toutes les heures à celui qui l'a prise en compte, et aux autres R/D au bout de 4 h.
- **Q5 — Mise en sécurité : A.** L'action réalisée est déclarée par un technicien, un R/D, un pasteur ou un coordinateur du site (mesures conservatoires simples : fermer ou baliser l'accès, éloigner le public) ; la vérification est faite par un R/D.
- **Q6 — Attente sous danger : A.** Mise en attente refusée tant que la mise en sécurité n'est pas déclarée réalisée (« Mettez d'abord en sécurité »).
- **Q7 — Délai de prise en charge : A.** De la qualification au démarrage de la première intervention (section DEC-05) ; rappel aux R/D tant que le dossier n'est pas affecté, puis à l'intervenant. Les tests d'US-09 et `deploy/verification/us09.mjs` sont mis à jour dans la sous-carte S10-02.
- **Q8 — Dossier resté « À qualifier » : A.** Rappel aux R/D du site 24 h après la réception, puis toutes les 24 h tant qu'il n'est pas qualifié, classé ou rattaché.
- **Q9 — Réexamen d'une attente : A.** Le jour du réexamen à 8 h (heure de Paris), rappel à la personne en charge ; sans reprise ni report 3 jours plus tard (J+3), escalade aux R/D du site ; puis un rappel par semaine aux deux tant que rien n'est fait.
- **Q10 — Relance d'un prestataire : A.** Toute relance dans l'application vise un compte interne (le référent du prestataire) ; R/D et coordination peuvent en plus noter « prestataire relancé hors application » (date, moyen, commentaire), trace seule, sans message envoyé ni coordonnées du prestataire enregistrées ; le pasteur ne fait que des relances internes.
- **Q11 — Heures calmes : A.** De 21 h à 7 h (heure de Paris), les e-mails et SMS qui ne concernent pas un danger partent à 7 h ; le centre enregistre toujours à l'heure exacte et les délais DEC-05 ne changent pas. **Le danger part toujours**, jour et nuit.
- **Q12 — Périmètre : A.** Le cœur (S10-01 à S10-05, S10-07, S10-08) et les rappels du préventif (S10-10) ; l'e-mail et le SMS (S10-09) sont lancés quand l'achat est fait.

Découpage (cadrage §3, révisé le 02/11) : S10-01 centre de notifications et émission après transaction ; S10-02 planificateur et rappels DEC-05 ; S10-03 attentes échues et dossiers à qualifier ; S10-04 danger (prise en compte, mise en sécurité, levée) ; S10-05 relances manuelles ; S10-07 maquettes ; S10-08 écrans web et préproduction ; S10-09 e-mail et SMS (bloquée par l'achat) ; S10-10 rappels du préventif. S10-06 (push web) est retirée (Q1). Les autres propositions du cadrage marquées « agent » (cycle du danger, liste des actions de mise en sécurité, report du réexamen, fréquence des relances, contenu minimal hors de l'application, conservation) restent des choix de l'agent, révisables.

### Centre de notifications — règles d'implémentation (S10-01, choix de l'agent, révisables)
- Une notification par destinataire et par événement (table `maintenance.notifications`) : organisation, site, compte destinataire, type, objet (dossier), clé de l'événement, instant exact, date de lecture. Aucun texte libre : ni description, ni nom, ni contact du déclarant ; le titre du dossier est relu à l'affichage, droits vérifiés.
- Écrite dans la **même transaction** que l'événement métier : si l'événement n'est pas enregistré, aucune notification ; un rejeu (même `commandeId`) n'en écrit aucune de plus (clé d'unicité destinataire + événement).
- Destinataires lus au moment de l'émission parmi les membres actifs du site (invitation acceptée, non révoqués, rôle sur le site) ; l'auteur de l'action n'est jamais notifié (le déclarant public n'a pas de compte).
- Événements de S10-01 : danger signalé (R/D, P/C, personnes en charge — intervenant ou référent d'un prestataire — d'une intervention ouverte du dossier ; une notification par alerte) ; activité bloquée sans danger (R/D ; avec danger, seule l'alerte part) ; affectation (personne en charge) ; clôture automatique du dossier (R/D, T-03.11) ; constat d'inspection rattaché (R/D, T-03.19 ; un même contrôle donne une seule notification par dossier et par destinataire, l'alerte de danger primant). Un dossier ouvert par un dépôt, une déclaration ou un constat sans danger ni activité bloquée ne notifie rien tout de suite (rappel « à qualifier » : S10-03).
- Lecture : par le seul destinataire, dans une organisation dont il est membre actif, sur les sites où il a encore un rôle ; un membre révoqué n'y a plus accès ; « tout marquer lu » peut s'arrêter à la plus récente affichée. Seconde barrière en base : la RLS ne montre une notification qu'à son destinataire et le rôle applicatif ne peut modifier que la date de lecture (ni suppression ni autre colonne).
- Conservation (6 mois, cadrage §1.5) : purge à faire avec DEC-07, non implémentée.

## Stocks — US-15, V, 30 octobre 2026 (porteur du projet) — en attente d'US-14
Cadrage : docs/pilotage/cadrage/US-15.md. Décisions du 30/10 (QCM) :
- Transfert entre sites : un R/D de l'un ou l'autre site fait toutes les étapes (préparation, départ, réception).
- Visibilité : les R/D de l'organisation voient les quantités (sans le journal des mouvements) de tous les sites, pour savoir à qui demander ; les autres comptes ne voient que leurs sites.
- Prix : aucun ; quantités seulement (montants et budget : US-17/18).
- Sortie de stock hors intervention : permise, motivée, par les techniciens et R/D du site (nouveau motif de mouvement, glossaire à compléter).
- Dépendance : US-15 attend US-14 (synchronisation) — ordre US-13, US-14, puis US-15 d'un seul bloc.

## Plans de bâtiment — US-21, V, 30 octobre 2026 (porteur du projet)
Cadrage complet : docs/pilotage/cadrage/US-21.md (règles RP-21.1 à RP-21.8, sous-cartes S21-01 à S21-10). Décisions du 30/10 (QCM, recommandations de l'agent retenues) :
- Droit nominatif « modifier les plans du bâtiment » (en plus de R/D) : attribuable par l'administrateur, site par site, à tout membre interne du site (technicien, pasteur, coordination), avec motif et journal ; révocation immédiate.
- Documents : import d'images (JPEG, PNG, HEIC) et de PDF (une page choisie) dès la V1, plus le plan vierge sur grille métrique ; pas de DXF ni DWG (export PDF à l'échelle demandé).
- Objets de valeur (trésor, objets de culte, coffre) : jamais placés sur le plan ; ils restent dans l'inventaire sans marqueur.
- Recette de l'éditeur : le porteur fournit un croquis coté ou un plan d'un site pilote avant S21-07, utilisé en préproduction seulement, jamais dans le dépôt.
- Maquettes (S21-06, choix du porteur du 30/10) : éditeur « Atelier » (A : outils à gauche, plan au centre, propriétés de la pièce à droite, sélecteur de niveau) pour l'usage courant, et parcours guidé (C : fond, échelle avec cote de contrôle, pièces zone par zone, composants) pour créer un nouveau plan ; ordinateur et tablette ; B écartée.
Règles de l'agent (révisables) : un plan par niveau (plan masse pour un bâtiment sans niveau), géométrie en mètres dans un repère par bâtiment, calibration obligatoire pour un plan importé ; pièces liées aux zones d'US-07 ; chaque enregistrement crée une version immuable, conflit à la version lue (409) ; « plan de bâtiment » partout pour ne pas confondre avec les plans préventifs d'US-12.

### Plans de bâtiment — règles d'implémentation (S21-02, choix de l'agent, révisables)
- Pièce : une zone dessinée au plus une fois par version ; les Parties communes ne sont jamais dessinées (listées à côté du plan) ; plan masse réservé à un bâtiment sans niveau, ses pièces sont les zones sans niveau.
- Contour : 3 à 500 sommets, aucun sommet répété, aire d'au moins 0,25 m², plus large que 5 cm ; chevauchement entre deux pièces refusé au-delà de 5 cm de largeur (chaque contour réduit de 2,5 cm ne doit plus rencontrer l'autre) : un mur commun et une petite imprécision de tracé restent admis.
- Ouverture : 0,20 à 30 m, posée à 5 cm près sur une arête d'une des pièces qu'elle relie (une ou deux) ; le contour suivant les faces intérieures, l'autre pièce est de l'autre côté du mur et n'est pas contrôlée géométriquement.
- Objets de valeur : faute d'un attribut « objet de valeur » dans l'inventaire, tout composant de la catégorie « Équipements de culte » est refusé comme marqueur (autel, orgue et calices compris). Le trésor ou un coffre classé ailleurs (Mobilier, Autre) n'est pas reconnu : à trancher par le porteur (attribut dédié dans l'inventaire, ou consigne aux utilisateurs).
- Marqueur : seulement pour un composant d'une zone du niveau du plan ; un composant des Parties communes d'un bâtiment à niveaux ne se place pas. Hors du contour de sa zone : avertissement, jamais refus.
- Chaque enregistrement crée une version, même sans changement (renvoi du même envoi excepté). Restauration : marqueurs de composants retirés, passés sur un autre niveau ou devenus objets de valeur retirés de la copie (avertissement) ; la position d'un composant reclassé en objet de valeur n'est plus servie, même dans les anciennes versions.
- Arrondi au centimètre identique dans l'éditeur et le serveur (ADR-11).

### Droit nominatif « modifier les plans du bâtiment » — règles d'implémentation (S21-03, choix de l'agent, révisables)
- Liste blanche limitée à `ModifierPlans`, tenue aussi par une contrainte en base. Autorisation = union des rôles et des droits nominatifs, relue à chaque requête (retrait immédiat, jeton en cours compris).
- Éligible : membre ayant accepté son invitation et au moins un rôle sur le site (T, P ou C ; R/D l'ont déjà par leur rôle, l'accord reste possible et sans effet). Un administrateur sans rôle sur le site n'est pas éligible ; s'il a aussi un rôle sur le site, il peut se l'accorder (tracé au journal).
- Un droit n'ouvre rien sur un site où le membre n'a plus de rôle : il est retiré avec la révision qui supprime ce rôle, et avec la révocation de l'adhésion, dans la même entrée de journal (droits avant/après).
- Accord ou retrait déjà en l'état : rien n'est écrit ni journalisé ; le motif reste exigé.

### Fonds de plan importés en image — règles d'implémentation (S21-04, choix de l'agent, révisables)
- Un fond appartient à un plan de bâtiment (un niveau ou le plan masse) ; un plan peut en recevoir plusieurs, chaque import crée un nouveau fond, jamais modifié ni supprimé en V1 (une version de plan pourra s'y référer avec sa calibration, S21-07).
- Import : R/D et titulaires du droit nominatif (`ModifierPlans`) ; lecture : comptes internes du site (`ConsulterSite`), l'administrateur sans rôle sur le site n'y a pas accès ; aucune voie publique.
- Fichier : un seul par envoi, 25 Mo au plus, JPEG, PNG ou HEIC reconnu par ses octets ; refus avant décodage au-delà de 12 000 px ou 50 Mpx annoncés (« Image trop grande ») ; jusqu'à ces bornes, les formes les plus lourdes mesurées (PNG en palette, transparent ou en 16 bits, JPEG CMJN) sont traitées (SOC-04) ; SVG, DWG et TIFF refusés ; PDF : voir ci-dessous (S21-05).
- Sorties : maître de 8 000 px au plus et dérivé d'affichage de 4 096 px au plus, réencodés sans métadonnées ; un PNG reste un PNG (traits nets d’un plan dessiné) sauf si, réduit, il dépasserait 25 Mo ; le reste devient JPEG. Seul le dérivé est servi ; le fichier d'origine n'est jamais conservé.
- Quota de stockage des fonds par organisation (maîtres et dérivés) : 1 Gio proposé par défaut, réglable (`PlansBatiment:QuotaFondsOctets`), **valeur à confirmer par le porteur** ; message clair quand il est atteint.

### Fonds de plan importés en PDF — règles d'implémentation (S21-05 ; validées par le porteur le 1er novembre 2026)
- Une seule page par import, choisie par son numéro (la première par défaut) ; page absente : refus qui donne le nombre de pages du PDF. Pour une autre page, un nouvel import (nouveau fond).
- Tout PDF chiffré est refusé, même ouvrable sans mot de passe (simples restrictions) : « enregistrez-le sans protection ». PDF corrompu, trop long à rendre (20 s, RP-21.3) ou trop lourd (mémoire du rendu, 1 Gio) : refus avec un message, l'application reste disponible.
- Rendu : fond blanc, annotations comprises, rotation de la page appliquée, grand côté à 8 000 px et 50 Mpx au plus, comme une image importée (une page carrée sort en 7 071 px ; SOC-04), puis mêmes sorties qu'une image PNG importée. Aucun script ni formulaire dynamique (XFA) n'est exécuté.
- Échelle 1:N déclarée à l'import (facultative, N de 1 à 10 000) : gardée avec la taille de la page en millimètres, elle ne fait que proposer une valeur à la calibration, qui doit être confirmée par une cote mesurée (RP-21.4).
- Le PDF d'origine est conservé en privé (jamais servi), pour un nouveau rendu ; il compte dans le quota des fonds avec le maître et le dérivé.
- Décisions du porteur (01/11, QCM) : un PDF à simples restrictions reste refusé (on ne contourne pas la protection posée par l'auteur) ; limites gardées (mémoire du rendu 1 Gio, échelle 1:1 à 1:10 000, aucun nombre maximal de pages : le délai de 20 s et les 25 Mo bornent le travail) ; quota des fonds de plan 1 Gio par organisation.

### Éditeur de plans : noyau et calibration (S21-07 ; choix de l'agent, révisables, et décisions du porteur du 2 novembre 2026)
- Décisions du porteur (02/11, QCM) : une version dépassée (conflit 409 à l'enregistrement, ou brouillon fait sur une version plus ancienne) n'est jamais réappliquée automatiquement ; l'éditeur propose « Reprendre mon brouillon sur la version M », avec confirmation : le brouillon devient la prochaine version enregistrée, et la version M de l'autre auteur reste dans l'historique, restaurable. L'étape « fond du plan » du parcours guidé et l'état « aucun plan pour ce niveau » sont construits avec les composants déjà validés (panneau latéral, alertes, onglets, charte Jour et Nuit) ; le porteur les verra en préproduction.
- Écran « Plans » (entrée de la navigation des comptes internes) : site, bâtiment et niveau choisis par des onglets ; un niveau sans version ouvre le parcours guidé (C) à qui peut enregistrer (R/D ou droit nominatif), sinon le message « Aucun plan de bâtiment pour ce niveau » ; dès la version 1, l'atelier (A). Un lecteur ne voit aucune commande d'édition ; le serveur refuse quand même.
- Parcours guidé : 1. fond (fond déjà importé, import d'une image ou d'une page de PDF, ou grille métrique sans fond) ; 2. calibration ; chaque étape validée s'enregistre comme une version. Les étapes 3 (pièces) et 4 (composants) arrivent avec S21-08 et S21-09 ; en attendant, la suite se fait dans l'atelier.
- Calibration (RP-21.4) : la personne touche les deux extrémités d'une longueur connue sur le fond puis saisit la distance réelle (10 m ou plus conseillés ; au moins 10 px sur l'image) ; la cote de contrôle, conseillée, se pose de la même façon : écart affiché en %, avertissement au-delà de 2 %, sans blocage. Pour un PDF à échelle déclarée, la distance est proposée d'après l'échelle et reste à confirmer. Le fond est posé coin haut gauche à l'origine du repère, axes confondus ; changer de fond oblige à le calibrer ; recalibrer ou changer de fond donne une nouvelle version.
- Repère : x vers la droite, y vers le bas du plan affiché (sens de l'image).
- Gestes : un doigt (ou la souris) glisse la vue, deux doigts zooment, la molette zoome ; un appui sans glisser pose un point. Sans glisser (WCAG 2.5.7) : boutons « + », « − », « Tout afficher » et flèches de déplacement, et au clavier flèches, « + », « − », « 0 » ; annuler et rétablir par boutons ou Ctrl+Z, Ctrl+Y.
- Calque du niveau inférieur (niveau d'ordre immédiatement inférieur du même bâtiment, s'il a une version) : contours de ses pièces en pointillés, masqué par défaut.
- Brouillon local (RP-21.8, simple confort) : écrit à chaque modification non enregistrée, sous une clé par compte, organisation et plan, et vérifié dans son contenu ; effacé après un enregistrement réussi ou sur « Abandonner le brouillon » ; conservé après un conflit 409 ; repris d'office s'il porte sur la version courante ; les brouillons d'un autre compte sont effacés à l'ouverture de l'éditeur ; tout le stockage local du site est effacé à la déconnexion (`Clear-Site-Data: "storage"`).

## Tablette et synchronisation — US-13, US-14, V, 30 octobre 2026 (porteur du projet)
Cadrage : docs/pilotage/cadrage/US-13-US-14.md. Décisions du 30/10 (QCM) :
- Plateformes (DEC-06) : Android (version minimale proposée : Android 11), distribution par un test interne Google Play ; l'achat du compte développeur (25 $, unique) est fait par le porteur ; iPad reporté.
- US-04 : le POC tablette est terminé d'abord, dans une carte séparée (base chiffrée par profil, déverrouillage, expiration des 24 h, révocation, photo interrompue, coupures réelles et envoi en arrière-plan sur la Tab M11), avant tout travail sur US-13.
- Les 24 h hors connexion (RG-23) partent du dernier contact réussi du profil (renouvellement accepté, adhésion relue) : une synchronisation prolonge le délai sans mot de passe.
- Contacts des déclarants : copiés sur la tablette comme en ligne (DEC-04), dans les limites des droits du profil.
- Inventaire modifiable hors connexion : ajouts libres, modifications contrôlées champ par champ au retour du réseau.
- Clôture hors connexion d'un dossier changé entre-temps : acceptée tant que l'intervention reste confiée à son auteur ; conflit seulement après réaffectation, clôture ou classement du dossier (RG-24).
- Saisies d'un compte qui a perdu son droit avant l'envoi : quarantaine visible par les R/D du site (auteur, heure), ressaisie possible sous leur nom avec « repris de la saisie de … » ; l'auteur garde la lecture des siennes ; l'administrateur seulement s'il est aussi R/D du site.
- Tablette perdue : le titulaire révoque sa session sur l'appareil (toutes organisations) ; l'administrateur coupe l'accès de son organisation sur l'appareil (synchronisation refusée, données effacées au prochain contact), jamais la session du compte.
Règle de l'agent (révisable, conséquence de RG-23) : déverrouillage par PIN à 6 chiffres propre à chaque profil, biométrie exclue sur une tablette partagée.

## Scoring — DEC-01, V, 27 octobre 2026 (porteur du projet)
- Notation d’un composant : l’évaluateur choisit un état sur une échelle commune — Bon = 9, Satisfaisant = 7, Dégradé = 5, Mauvais = 3, Critique = 1. Les critères de la grille guident ce choix sans calcul automatique.
- Libellés d’un score moyen : [0;2[ Critique, [2;4[ Mauvais, [4;6[ Dégradé, [6;8[ Satisfaisant, [8;10] Bon.
- Lot : compte comme un seul composant de sa zone ; note = moyenne des états pondérée par les quantités (ex. 30×9 + 8×5 + 2×1 sur 40 = 7,8).
- Éléments communs à plusieurs pièces : chaque bâtiment a une zone « Parties communes » qui les reçoit et compte comme une zone dans la moyenne du site. Un composant appartient à une seule zone.
- Fraîcheur : une évaluation de plus de 6 mois est signalée « ancienne » ; elle reste comptée. L’écran montre la part du score reposant sur des évaluations anciennes.
- Historique : instantané mensuel (et à la demande) du score du site et de chaque zone ; les évaluations sont toujours historisées.
- Inventaire : un composant retiré sort immédiatement du calcul, son historique reste consultable ; un composant ajouté est « non évalué » et réduit la couverture sans modifier la note.
- Arrondi : une décimale à l’affichage, précision complète au calcul (RG-12).
- Libellé : appliqué à la valeur arrondie affichée (7,96 → « 8,0 — Bon »), pour qu’un chiffre et son libellé ne se contredisent jamais. Arrondi arithmétique au plus proche (0,05 vers le haut).
Un danger ne disparaît pas si la moyenne est bonne. Les grilles doivent distinguer constat, état, mesure et note ; l’IA ne remplace pas l’évaluation humaine validée.

## Priorités, délais et relances — DEC-05, V, 27 octobre 2026 (porteur du projet)
Priorité fixée par R/D à la qualification, indépendamment du danger déclaré (qui reste une alerte distincte).

| Priorité | Usage | Prise en charge (de la qualification au démarrage, US-10 Q7) | Résolution (clôture, hors attente) |
| --- | --- | --- | --- |
| Urgente | Danger, activité bloquée | 4 h | 2 jours |
| Haute | Gêne importante | 1 jour | 7 jours |
| Normale | Cas courant | 3 jours | 30 jours |
| Basse | Amélioration, peut attendre | 7 jours | 90 jours |

- Délais comptés en temps calendaire (week-ends et jours fériés inclus), pour toutes les priorités. Ce sont des cibles de suivi, pas des engagements de service.
- Les périodes « En attente » suspendent le délai de résolution ; l’ancienneté totale reste affichée.
- Délai de prise en charge (décision du porteur du 02/11, US-10 Q7) : il court de la **qualification** jusqu'au démarrage de la première intervention, et non plus de l'affectation ; un dossier qualifié mais jamais affecté a donc une échéance et des rappels (conforme à T-04.10 et T-04.14). Le code d'US-09 (départ à l'affectation) est corrigé par la sous-carte S10-02.
- Rappels : à 75 % du délai, rappel à la personne en charge (R/D si non affecté, sinon technicien affecté). À 100 %, escalade au responsable et aux délégués du site, puis rappel quotidien tant que le délai est dépassé. Une relance ne crée pas de doublon de notification pour le même seuil.
- Récurrence : au moins 3 dossiers (hors « classée sans suite » et doublons rattachés) sur le même composant ou la même zone en 12 mois glissants.

## Budget — DEC-02, V, 27 octobre 2026 (porteur du projet)
- Tous les montants sont TTC, en euros. La TVA peut être saisie à titre informatif sur une dépense.
- Une enveloppe par site et par année civile (1er janvier – 31 décembre).
- Le responsable logistique (ou un administrateur) saisit le montant décidé par l’autorité financière, avec justificatif. Abondement ou réduction en cours d’année possibles avec motif et justificatif, jamais en dessous du montant déjà engagé.
- Clôture d’année : les engagements non soldés sont reportés sur l’enveloppe de l’année suivante, chaque report étant confirmé par R/D ; l’année close devient non modifiable.
- Dépense supérieure à l’autorisé : la dépense est enregistrée ; l’écart exige une autorisation complémentaire de R/D dans le disponible, ou un accord finance s’il manque (RG-17, RG-32). Le dossier est signalé « dépassement à régulariser » jusque-là.
- Saisie des dépenses : R/D, et le technicien affecté sur ses dossiers. La dépense d’un technicien est provisoire (« à valider ») mais s’impute immédiatement sur l’engagement ; si R/D la refuse, le montant est rétabli. Le technicien ne voit que ses propres saisies et leur statut, jamais enveloppes, soldes ni autres dépenses.
- Correction : ancienne valeur, auteur et motif conservés (RG-31) ; une dépense n’est jamais supprimée, elle est annulée avec motif.
- Reliquat d’un dossier terminé : libéré vers le disponible du site après confirmation par R/D qu’aucune facture n’est attendue ; reste réservé jusque-là.

## Préventif — règles d'implémentation US-12 (choix de l'agent, révisables)
Précisions de l'agent pour appliquer les décisions du 30/10 (section « Préventif et inspections ») ; valeurs réglementaires à confirmer par un bureau de contrôle ou le SDIS.
- Classement ERP (S12-01) : déclaré par R/D (« Planifier le préventif »), à la version lue du bâtiment. Un ERP a une catégorie (1 à 5) et un type principal (V par défaut) ; ses types secondaires (5 au plus) sont distincts du principal. Hors ERP, type, catégorie, effectif, locaux à sommeil et visite de la commission sont refusés ; « classé ou inscrit » et « permis avant le 1er juillet 1997 » (Oui, Non, Inconnu) valent pour tout bâtiment. « Salariés présents » est déclaré une fois par site.
- Catalogue des plans proposés (S12-01), pour chaque composant actif soumis à vérification : inspection avec la grille [P] annuelle (sauf éclairage de sécurité) ; électricité : organisme agréé annuel en 1re à 4e catégorie (EL 19), technicien compétent annuel en 5e (PE 4), personne qualifiée annuelle avec salariés (Code du travail R.4226-16) ; éclairage de sécurité : essai mensuel (EC 14, [R] en ERP ou avec salariés, [P] sinon) et vérification annuelle (organisme agréé en 1re à 4e catégorie, technicien compétent en 5e ou avec salariés) ; sécurité incendie : vérification annuelle (MS 38, MS 73, DF 10, PE 26 en ERP ; Code du travail avec salariés ; APSAD R4 [P] sinon) ; chauffage et ventilation : entretien annuel [P] (obligatoire pour une chaudière de 4 à 400 kW, la référence le dit) et vérification annuelle CH 58 en ERP ; gaz : vérification annuelle (GZ 30 / PE 4 en ERP, [P] sinon) ; ascenseurs : entretien toutes les 6 semaines et contrôle technique quinquennal par un organisme ; paratonnerre : vérification complète tous les 2 ans [P] ; campanaire : visite annuelle [P] ; menuiseries extérieures soumises à vérification : portes automatiques semestrielles [R] en ERP ou avec salariés.
- Par bâtiment : ERP, revue annuelle du registre de sécurité [R] ; commission de sécurité en 1re à 4e catégorie et en 5e avec locaux à sommeil (type V : 3 ans en 1re et 2e catégorie, 5 ans ensuite ; autres types : 3 ans par prudence ; échéance suggérée depuis la dernière visite connue) ; maintenance annuelle du DAE en 1re à 4e catégorie ; tout bâtiment au permis antérieur au 1er juillet 1997 ou inconnu, repérage et surveillance triennale de l'amiante ; monument classé ou inscrit, visite annuelle du clos et couvert [P] ; tout bâtiment, nettoyage des chéneaux à échéance fixe fin octobre [P].
- Une proposition est identifiée par son modèle et sa cible (clé stable) ; l'échéance suggérée est aujourd'hui (heure de Paris), la prochaine fin octobre pour les chéneaux, ou la dernière visite connue + la période.
- Plans et échéances (S12-02) : une proposition confirmée donne un seul plan (clé unique, rejeu sans doublon) ; écartée, elle reste visible avec son motif (non concerné, suivi ailleurs, autre avec commentaire) et peut encore être confirmée. Inspection interne seulement sur un composant ou une zone et un corps d'état (la grille). Fenêtre « À faire » = 10 % des jours de la période qui s'achève à l'échéance, arrondi au plus proche, bornée à 7 et 30 jours (30 pour un plan ponctuel). En mode glissant, l'échéance suivante n'est jamais avant le lendemain de la précédente (réalisation saisie très tôt). Première échéance comprise entre 5 ans avant et 10 ans après aujourd'hui. Un composant retiré ou décoché « soumis à vérification » suspend son plan à la lecture ; le recocher le reprend.
- Exécution (S12-03) : seule l'échéance courante d'un plan s'affecte, se saute ou se réalise ; l'occurrence est créée à ce moment (une par plan et échéance). Le technicien affecté ou le référent du prestataire exécute ; tout autre technicien est refusé, R/D peuvent toujours saisir (réalisation après coup comprise). Date réelle de réalisation passée et de 5 ans au plus ; un entretien exige un compte rendu ; un point en défaut interdit le résultat « Conforme » et donne « Réalisée avec réserves » ; le danger se signale sur un point en défaut. Photos du contrôle (3 au plus) ; pour l'inspection d'un composant, les mêmes photos accompagnent l'évaluation US-11. La grille utilisée est celle du corps d'état actuel du composant.
- Vérification extérieure (S12-04) : le rapport PDF est exigé pour une vérification de fondement réglementaire faite par un prestataire (nommé à la saisie ou à l'affectation) ; il est reconnu par sa signature « %PDF- », limité à 10 Mo, gardé avec son empreinte et jamais supprimé ; il se télécharge en pièce jointe. Chaque observation (libellé, zone — celle du plan à défaut —, danger) est un écart à lever : réserves, et jamais le résultat « Conforme ». 50 observations au plus par rapport.
- Choix de l'agent acceptés le 30/10 (révisables) : une vérification réglementaire faite par un compte interne n'exige pas de rapport PDF ; la zone d'une observation est contrôlée au niveau du site (une vérification peut couvrir plusieurs bâtiments) ; le résultat déclaré d'un contrôle reste « Conforme / AvecObservations / NonConforme », sans jamais de statut « conforme » de l'établissement. Commission de sécurité à 3 ans pour les types autres que V : proposition « à confirmer ».
- Non-conformités (S12-05) : un dossier par point en défaut ou par observation, sauf dossier ouvert du même composant (ou de la même zone sans composant) et du même critère : le constat y est rattaché et marqué « nouveau constat » pour R/D jusqu'à la prochaine action sur le dossier. Critère normalisé (sans accents, casse ni ponctuation) : une reformulation du point par R/D ouvre un nouveau dossier, la détection de doublons d'US-09 reste disponible. Réserves levées quand tous les dossiers liés sont clos (clôturés, classés sans suite, rattachés), calculé à la lecture ; un résultat « non conforme » sans constat reste en réserve.
- Écrans de terrain (S12-07, maquette A du 30/10 : même parcours pas à pas que l'évaluation ; choix de l'agent, révisables) : inspection en 3 étapes (points et mesures, état ou répartition du composant, résultat et photos ; 2 pour une zone et un corps d'état), entretien et vérification extérieure en 2 (intervenant, observations du rapport et relevés, puis rapport, résultat et compte rendu). Un point se note « Sans défaut », « Défaut », « Défaut, danger » ou « Non vérifié » (jamais « conforme ») ; après un défaut ou une observation, « Sans observation » n'est plus proposé. Le rapport PDF est signalé obligatoire dès qu'un prestataire est saisi ou affecté sur une vérification réglementaire. « Faire le contrôle » et « Sauter cette échéance » (motif, commentaire pour « Autre ») figurent sur la fiche du plan pour la personne en charge et R/D ; « Contrôler » dans « Mes contrôles ». Fiche d'un contrôle : points, mesures et relevés, observations et zones, rapport à télécharger (taille, empreinte), photos, et les dossiers ouverts ou rattachés ; historique des contrôles du site, le plus récent d'abord.
- Évaluation issue d'une inspection (correctif d'audit, choix de l'agent, révisable) : datée du début du jour réel de la réalisation (heure de Paris, jamais après la saisie) ; une inspection saisie après coup ne masque jamais une évaluation plus récente du composant, même du même jour (score, points critiques, historique au bon mois), et ne réécrit la répartition d’un lot que si aucune évaluation ni modification de l’inventaire du composant n’est postérieure. L'API d'évaluation directe reste datée à l'heure du serveur jusqu'à US-14.
