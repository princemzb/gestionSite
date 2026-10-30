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
| RG-08 | V | Rappels puis escalade selon priorité ; notifications internes et push. |
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
- Photos : jusqu’à 3 par dépôt, JPEG, PNG ou HEIC, 10 Mo au plus chacune ; métadonnées (dont la position GPS) retirées à la réception.
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

| Priorité | Usage | Prise en charge (dossier affecté) | Résolution (clôture, hors attente) |
| --- | --- | --- | --- |
| Urgente | Danger, activité bloquée | 4 h | 2 jours |
| Haute | Gêne importante | 1 jour | 7 jours |
| Normale | Cas courant | 3 jours | 30 jours |
| Basse | Amélioration, peut attendre | 7 jours | 90 jours |

- Délais comptés en temps calendaire (week-ends et jours fériés inclus), pour toutes les priorités. Ce sont des cibles de suivi, pas des engagements de service.
- Les périodes « En attente » suspendent le délai de résolution ; l’ancienneté totale reste affichée.
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
