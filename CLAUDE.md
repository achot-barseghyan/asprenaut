# Asprenaut · Matériel

Application desktop Windows de suivi des sorties et retours du matériel du club de plongée Asprenaut
(blocs, gilets, détendeurs, combinaisons, palmes, masques, ordinateurs). Un seul PC, pas de réseau.
Utilisateurs : bénévoles peu à l'aise avec l'informatique → interface simple, gros boutons, messages courts.

Le matériel porte une étiquette QR. Le pistolet scanner USB est en mode clavier (HID) : il tape le code puis Entrée.

## Commandes

```powershell
dotnet build Asprenaut.Materiel.sln
dotnet test Asprenaut.Materiel.sln
dotnet run --project src/Asprenaut.Materiel.App                       # base réelle (%LOCALAPPDATA%\Asprenaut\materiel.db)
dotnet run --project src/Asprenaut.Materiel.App -- --base C:\tmp\essai.db   # base d'essai

# Publication : un seul .exe autonome (aucun .NET requis sur le PC du club) → dossier publish/
dotnet publish src/Asprenaut.Materiel.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# ou, via le profil : dotnet publish src/Asprenaut.Materiel.App -p:PublishProfile=win-x64-autonome
```

Le SDK est épinglé par `global.json` (10.0.x) : un SDK 11 préversion est aussi installé sur ce poste.

## Stack (imposée — aucune autre dépendance sans accord du demandeur)

.NET 10 · WinForms · SQLite via Microsoft.Data.Sqlite + Dapper (pas d'EF) · Microsoft.Extensions.DependencyInjection · xUnit.
QRCoder est prévu pour les étiquettes, à ajouter seulement à cette étape.
Versions NuGet centralisées dans `Directory.Packages.props`. `Directory.Build.props` : Nullable, ImplicitUsings, **TreatWarningsAsErrors**.

## Architecture

```
src/Asprenaut.Materiel.Core   net10.0          Modèles, règles métier, services, interfaces. Ne référence rien.
src/Asprenaut.Materiel.Data   net10.0          Dépôts SQLite (Dapper), migrations, sauvegardes, données de démo. → Core
src/Asprenaut.Materiel.App    net10.0-windows  WinForms : écrans, contrôles, thème, saisie scanner. Point d'entrée. → Core, Data
tests/…Core.Tests                              Règles métier sur dépôts en mémoire (Memoire/DepotsMemoire.cs, Contexte.cs)
tests/…Data.Tests                              Dépôts et schéma sur base SQLite temporaire (BaseTemporaire.cs)
```

- **Core/Services** : `ServiceScan` (scan départ/retour, retrait), `ServiceSortie` (créer, valider départ, aperçu et clôture),
  `ServiceInventaire`, `ServiceMoniteurs` (cartes, fiche, ajout/modification, saison au 1er septembre), `ServiceRevisions`
  (échéancier, contrôles, atelier, séances TIV), `ServiceReglages`, `ServiceHistorique` (journal lisible, période, regroupement,
  export CSV, vie d'un équipement), `ServiceGestion` (mode consultation / mode gestion, code gestionnaire),
  `ServiceBilan` (bilan de saison pour l'AG). Toutes les règles métier sont ici.
- **Core/Resultats** : `ResultatScan` (Niveau Succes/Avertissement/Erreur, Message, Equipement, `ActionProposee`, Code, AutreSortieId),
  `ResultatOperation`, `ApercuCloture`. Les cas métier ne lèvent pas d'exception : ils retournent un résultat.
- **Core/Saisie** : `CodeScanne.Nettoyer` (espaces, préfixe `ASP:`, majuscules) et `AntiRebond` — logique pure, testée.
- **Core/Abstractions** : interfaces des dépôts, `IHorloge` (heure injectable pour les tests), `IServiceSauvegarde`.
- **Data/Migrations** : `NNN_nom.sql` embarqués (LogicalName `Migrations.NNN_nom.sql`), appliqués au démarrage par
  `GestionnaireMigrations` selon `schema_version`, chacun dans sa transaction. Active le mode WAL.
  `AppliquerAsync(jusqua)` s'arrête à une version (tests de reprise de données, voir `HistoriqueDepotsTests`).
- **Data/Connexion** : `FabriqueConnexion` (une connexion par appel, `Foreign Keys=True`), `ConfigurationDapper`, `FormatIso`.
- **App** : `Program.cs` → `Demarrage` (DI, sauvegarde, migrations, démo en DEBUG) → `MainForm` (menu latéral + zone de contenu).
  `INavigation.AllerA<TEcran>(parametre)` crée l'écran via `ActivatorUtilities` (non retenu par le conteneur), le met à l'échelle
  de l'écran et dispose le précédent. Écrans dans `Ecrans/` (héritent d'`EcranBase` ; Départ/Retour héritent d'`EcranScanBase`
  qui sérialise les scans et fournit fil d'Ariane + carte d'en-tête via `Composer(corps)`).
- **Maquette** : https://claude.ai/artifact/8op5sAWFbf4mRQCxzd37aa (Sorties, Nouvelle sortie, Départ, Retour, Inventaire,
  Moniteurs, Historique, Bilan de saison, Révisions, Paramètres, Déverrouillage). Polices Sora/Manrope remplacées par Segoe UI Variable ; pas de fond dégradé ni de bulles animées.
- **Briques d'interface** (`Controles/`) : `Carte`, `ControleDessine` (base des contrôles peints), `AnneauProgression`,
  `IndicateurEtapes`, `PastilleDate`, `Pastille`, `Picto`, `GrilleAdaptative` (colonnes auto, ajuste sa ligne de TableLayoutPanel),
  `Tuile`, `FilAriane`, `BarreRecherche`, `ChampSaisie` (`Icone` optionnelle), `ChampDate` (remplace le DateTimePicker, blanc et en anglais),
  `ListeDeroulante` (cadre et bouton repeints par-dessus Windows dans `WndProc`), `ListeMateriel` (styles Depart / ARendre / Rendu),
  `ScanInput` + `BandeauMessage`, `BandeauAccueil`, `Logo`, `CarteScanner`, `Tableaux` (pastilles et pictogrammes dans les DataGridView),
  `FiltreCategories` (puces « Tout 12 / Blocs 3 », `CouleurPoint` et `Unite` optionnels), `FenetreFormulaire` (fenêtre modale sombre ;
  la validation passe par un service et ne ferme qu'en cas de succès), `Interrupteur`, `SelecteurNombre`, `CarteSection`, `LigneReglage`,
  `JournalHistorique` (journal par jour dessiné dans un seul contrôle défilant : seule la partie visible est peinte, le clic sur un
  code est retrouvé par calcul — à préférer à un contrôle par ligne dès qu'une liste peut compter des milliers d'éléments).
  `EcranBilan` : cartes entièrement dessinées (classes imbriquées), placées par `PageBilan` selon la largeur (4/2 chiffres,
  3/2/1 cartes du bas) ; la page est focusable pour que la molette la fasse défiler, et sa largeur est posée à la main
  (en Dock Top, la barre de défilement apparue après coup recouvrait le bord droit des cartes).
- **Impression/** : `DocumentPdf` dessine des `BlocPdf` en GDI+ (thème clair) et les envoie à l'imprimante Windows
  « Microsoft Print to PDF » (`PrintToFile`), sans bibliothèque ; pagination par blocs, titre gardé avec la suite.
  `RapportBilan` (bilan pour l'AG), `RegistrePdf` (registre des contrôles, bouton « Registre PDF » de Révisions).
  `Infrastructure/Fichiers` : `EnregistrerPdfAsync` (imprimante, dialogue, erreurs, ouvrir) et `EnregistrerCsvAsync` (UTF-8 avec BOM).
- **Exports CSV** : `Core/Saisie/Csv` (séparateur « ; », guillemets, apostrophe devant = + - @). Inventaire : lignes affichées
  (`ServiceInventaire.ExporterCsv`). Paramètres › Exporter CSV : toutes les tables dans « Export Asprenaut AAAA-MM-JJ »
  (`ServiceExport` met en forme les valeurs brutes de `ExportRepository` selon `GenreColonne` : dates, états en français).
- **Révisions** (`EcranRevisions`) : tableau à 4 colonnes dessinées (`ColonneRevision`, 2 sous 1000 px logiques) et cartes
  (`CarteEquipement`, vrais `BoutonArrondi` liés à `ActionProtegee.Revisions`), filtre par famille, champ de scan qui ouvre
  le panneau « Ajouter une intervention » en haut de page ; « Fiche › » mène à Historique (vie de l'équipement).
  `PageRevisions.OnLayout` recommence le placement si la hauteur posée a fait apparaître la barre (mise en page imbriquée ignorée).
- **Inventaire** : bouton « Modifier » dessiné dans la grille (`Tableaux.PeindreBouton`), double-clic ou Entrée →
  `FormulaireEquipement.ModifierAsync` → `ServiceInventaire.ModifierAsync`.
- **Pictogrammes** : `Apparence/Icones.cs`, chemins SVG 24 × 24 de la maquette lus par `CheminSvg` et tracés en GDI+.
- **Icône de l'application** : `Ressources/Asprenaut.ico` (16 à 256 px ; tailles ≤ 96 en bitmap, 128 et 256 en PNG), fabriquée
  depuis `Ressources/Asprenaut.png` (1024 px). `ApplicationIcon` dans le .csproj pour le .exe, ressource embarquée lue par
  `Icones.IconeFenetre` pour `MainForm` et `FenetreFormulaire` (barre de titre, barre des tâches).

## Règles métier (chacune a au moins un test dans Core.Tests)

1. Un équipement n'est « Dehors » que dans une seule sortie (aussi garanti par l'index partiel `ux_sortie_equipement_dehors`).
2. HorsService / Reforme ne part pas.
3. Double scan au départ dans la même sortie → ignoré, « déjà scanné ».
4. Code inconnu au départ → `ActionProposee.CreerEquipement` : l'écran ouvre `FormulaireEquipement` (code pré-rempli, catégorie
   devinée par `ServiceInventaire.DevinerCategorieAsync`), puis rescanne le code dans la sortie.
   Création (`ServiceInventaire.CreerAsync`) : code nettoyé et unique, nom et catégorie obligatoires, pas de date de contrôle passée,
   requalification réservée aux blocs, toujours EnService. Code proposé : `ProposerCodeAsync` (BLOC-034 → BLOC-035).
5. Retour d'un équipement absent de la sortie → avertir sans rien modifier ; s'il est dehors ailleurs →
   `ActionProposee.RentrerDansAutreSortie` (l'écran rescanne alors dans `AutreSortieId`).
6. Retrait possible seulement en préparation (`StatutSortie.EnPreparation`).
   Modifier la sortie (date, type, intitulé, moniteur, lieu, notes) : seulement en préparation, mêmes contrôles qu'à la création,
   le moniteur déjà responsable reste accepté s'il est devenu inactif (`ServiceSortie.ModifierAsync`, journal `SortieModifiee`).
   Supprimer une sortie : seulement si aucun matériel n'y est rattaché (`ServiceSortie.SupprimerAsync` ; la requête SQL du dépôt
   re-vérifie, journal `SortieSupprimee`). Écran Départ : boutons « Modifier la sortie » et « Supprimer » (sortie vide) en haut à droite.
7. Clôture : Dehors → Manquant, confirmation avec le nombre (`ApercuClotureAsync`) s'il reste du matériel dehors
   (`ApercuCloture.ConfirmationNecessaire`, réglage `ConfirmerCloture`).
8. Un Manquant scanné plus tard au retour (dans sa sortie ou depuis une autre) → Rendu + journal `ManquantRetrouve`.
9. Révision (ou TIV) ou requalification dépassée → part quand même, avec avertissement (refusée si le réglage
   `DepartSiRevisionDepassee` est coupé). `Equipement.RevisionDepassee` / `EcheanceDepassee` tiennent compte des deux dates.
10. Sortie en cours avec du matériel dehors depuis `JoursAvantAlerteRetour` jours → en retard (`ServiceSortie.EstEnRetard`, carte orange).
11. Révision dans les `JoursAvantRevision` jours (15/30/60) → badge jaune ; dépassée → badge rouge (`ServiceInventaire.EtatDeRevision`).
12. Contrôles (`ServiceRevisions.EnregistrerControleAsync`) : TIV et requalification pour les blocs (catégorie « Blocs… »),
    révision pour le reste. Conforme → TIV + 12 mois ; révision + délai de la catégorie (`ServiceRevisions.MoisRevision` :
    `Reglages.MoisEntreRevisionsParCategorie`, Détendeurs 24, Gilets 0 = pas de date obligatoire, sinon `MoisEntreRevisions` 12) ;
    requalification + 6 ans (et TIV + 12 mois) ; à surveiller → + 6 mois ;
    refusé → HorsService (impossible si l'équipement est dehors). TIV : inspecteur habilité obligatoire. Pas de date future.
    Délais dans `Reglages` (`MoisEntreTiv`, `AnneesEntreRequalifications`, `MoisEntreRevisions…`, `MoisSiASurveiller`).
13. Échéancier : en retard / ce mois-ci / sous `JoursAPlanifier` (90) jours / à jour (`ServiceRevisions.EtatDe`). Matériel réformé exclu.
14. Atelier : l'envoi passe l'équipement HorsService (refusé s'il est dehors, déjà en atelier, ou sans prestataire) ; le retour le remet EnService.
    Séance TIV : date à venir ; blocs à préparer = TIV due au plus tard 30 jours après la séance.
15. Historique (`ServiceHistorique`) : chaque entrée du journal est reliée à son équipement et à sa sortie (`EntreeJournal.EquipementId /
    SortieId`, à renseigner pour toute nouvelle action journalisée). Type d'événement d'après l'action (`TypeDe`) ; titre et détail
    calculés depuis l'équipement, la sortie et le moniteur liés, sinon le texte enregistré. Scans d'une même sortie, même action,
    à moins de 15 minutes et le même jour → un seul événement « Retour : 3 équipements ». Périodes : 7 jours, 30 jours, saison
    (1er septembre), tout (5000 entrées au plus). Vie d'un équipement reconstituée depuis sortie_equipement, controle et atelier
    (manquant puis retrouvé si rendu après la clôture), pas depuis le journal.
16. Mode gestion (`ServiceGestion`, maquette « Déverrouillage » et Paramètres › Sécurité) : sans code gestionnaire, rien n'est verrouillé.
    Avec un code (4 à 6 chiffres, haché PBKDF2-SHA256 salé dans `Reglages.CodeGestionnaire`), l'appli démarre en consultation ;
    les `ActionProtegee` (matériel, moniteurs, supprimer une sortie, révisions/atelier/séances, paramètres, restaurer une sauvegarde,
    et clôturer avec des manquants si `CodePourCloturerAvecManquants`) sont refusées par les services tant que le mode gestion n'est
    pas ouvert. Tous les paramètres (thème compris) sont protégés. Libres : créer/modifier une sortie en préparation, scanner,
    valider, clôturer sans manquant, consulter, tester le pistolet, faire une sauvegarde.
    Retour en consultation après `MinutesDeverrouillage` (5/15/30/60) sans activité ; `VerrouillerALaFermeture` coupé → le mode
    gestion ouvert est repris au lancement (`DeverrouilleAuDemarrage`). Changer ou retirer le code demande l'ancien.
    5 codes faux d'affilée → saisie bloquée 1 minute. Les services reçoivent `ServiceGestion? gestion = null` (null = pas de contrôle,
    pour les tests qui n'en ont pas besoin). App : `GardeGestion.Exiger(...)` avant chaque action protégée (pavé
    `DialogueDeverrouillage`, chiffres lus d'après le caractère tapé : AZERTY), `CarteModeGestion` dans le menu, `FiltreActivite`.
    Visuel « verrouillé » : `GardeGestion.Lier(controle, action)` met `Verrouille` à jour à chaque changement de mode —
    cadenas après le texte d'un `BoutonArrondi`, badge « Code requis » d'une `CarteSection`. Tout nouveau bouton d'action protégée doit être lié.
    Code oublié : supprimer la ligne `CodeGestionnaire` de `parametres.json` (application fermée).
17. Bilan de saison (`ServiceBilan`, consultation libre) : saison précédente ou en cours (1er septembre → 31 août), seules les
    sorties parties comptent (en cours ou clôturées, datées dans la saison). Mois de septembre à juin, juillet/août seulement s'il
    y a eu des sorties. Manquant à la clôture : perdu s'il l'est encore, retrouvé s'il a été rendu après la clôture.
    Tension = part du stock d'un groupe (catégorie + taille, hors réformé, au moins `StockMinTension` = 2) sortie lors de la sortie
    la plus chargée ; rupture = tout le groupe dehors. Peu sortis : en service, moins de `SeuilPeuSorti` (3) sorties.
    Contrôles par famille : TIV, requalifications, puis catégorie pour les révisions. Requêtes : `ListerPassagesPeriodeAsync`,
    `ListerControlesPeriodeAsync`.
18. Modifier un équipement (`ServiceInventaire.ModifierAsync`, action protégée Materiel) : mêmes contrôles qu'à la création
    sauf les dates passées (acceptées) ; pas hors service ni réformé s'il est dehors ; état inchangeable en atelier (le retour
    d'atelier le remet en service). Journal `EquipementModifie` avec la liste des champs changés. Pas de suppression : on réforme.
19. Compteur d'utilisation (`ServiceCompteur`, Paramètres, action protégée) : chaque retour scanné ajoute les utilisations de
    sa sortie (`Sortie.Utilisations`, 1 à 10) depuis le jour du dernier contrôle (tous types ; `IEquipementRepository.ListerCompteursAsync`
    fait la somme). Seuil (en utilisations ; noms `SeuilsSorties` / `Sorties` gardés pour parametres.json) : propre au code, sinon au
    modèle (nom exact de l'équipement), sinon à la catégorie (0 = désactivé ; Détendeurs et Gilets 100 par défaut).
    À 90 % (`PrevenirAvantSeuil`) → « à planifier » dans Révisions (ligne `LigneEcheance.Usage` « 92/100 utilisations ») ;
    au seuil → en retard, départ avec avertissement, ou refusé si `BloquerAuSeuil` (coupé par défaut). Un bloc repart à zéro à sa TIV.
20. Registre des contrôles (`ServiceRevisions.RegistreAsync`) : tous les blocs (même sans contrôle), puis le matériel avec un
    contrôle ou une révision prévue ; réformés seulement s'ils ont un historique.
21. Types de sortie (`ServiceTypesSortie`, Paramètres › Types de sortie, action protégée Parametres) : des données
    (`TypeDeSortie` : nom, pictogramme, actif, ordre), plus l'enum. Nom obligatoire, unique sans tenir compte des majuscules,
    40 caractères au plus. Utilisé par une sortie → pas de suppression (la requête SQL re-vérifie), seulement masqué : plus
    proposé (tuiles de « Nouvelle sortie »), gardé par ses sorties et accepté quand on les modifie. Toujours au moins un
    type proposé. L'enum `TypeSortie` n'est plus que la famille / pictogramme du type (colonne sortie.type, CHECK inchangé) ;
    `NouvelleSortie.TypeId` choisit le type (sans lui : premier type proposé de la famille). Afficher `SortieResume.LibelleType`,
    jamais `Libelles.De(s.Type)`. Bilan, historique et export lisent le nom du type.
    Utilisations (migration 006) : chaque type a ses utilisations par sortie (piscine, fosse, baptême, autre 1 ; lac 2 ; mer 4 ;
    nouveau type selon son pictogramme, `TypeDeSortie.UtilisationsParDefaut`), réglées dans Paramètres › Types de sortie et
    utilisations (`ChangerUtilisationsAsync`, 1 à 10, sans toucher aux sorties existantes). Une sortie prend celles de son type,
    modifiables à la création et en modification (`NouvelleSortie.Utilisations`) ; en modification sans valeur, elle garde les
    siennes sauf si le type change. Liste des types : corbeille rouge si jamais utilisé, sinon bouton œil
    (`BasculerMasqueAsync`) pour masquer / réafficher ; type masqué grisé « · masqué ».
22. Fiche matériel (registres « Stab » et « Détendeurs » du club, migration 005) : marque, modèle, n° de série pour tous (premier
    étage pour un détendeur) ; détendeur seulement : montage `MontageDetendeur` (Étrier / DIN), deuxième étage (marque, modèle, n°),
    D.S (oui / non / non renseigné), mano (marque), octopus (modèle, vide = sans) et son n°. Ces champs sont vidés pour les
    autres catégories (`ServiceInventaire.Fiche`). « Date de la dernière révision » saisie dans la fiche (hors blocs) : pas dans le
    futur, pas avant une révision déjà enregistrée ; elle devient un contrôle « révision conforme » (note « Saisie dans l'inventaire »)
    et fixe « à réviser avant » (+ délai de la catégorie, même déjà passé) ; gilets : la date tapée est gardée.
    Identification calculée (`ServiceInventaire.Identification`) : « (Stab) S01 taille XL {NomClub} {NumeroClub} »,
    « (Detendeur) A01 … » ; `Reglages.NumeroClub` dans Paramètres › Club. Recherche aussi par marque, modèle et numéros de série.
    Inventaire : colonnes selon la catégorie choisie (générales, Gilets, Détendeurs en largeur fixe avec défilement horizontal),
    matériel réformé barré et grisé. `FenetreFormulaire` défile quand elle dépasse la hauteur de l'écran ; `DeuxChamps` retourne
    sa rangée (masquable, libellés via `FenetreFormulaire.Libelles`).
23. Tableau des révisions (`ServiceRevisions.TableauAsync`, migration 007) : À prévoir (échéance non à jour ou 90 % du seuil
    d'utilisations, équipement EnService, ni en atelier ni déjà planifié) → Planifié (blocs ajoutés à la prochaine séance TIV,
    table seance_equipement ; `AjouterASeanceAsync` refusé sans séance ou pour un non-bloc) → En atelier (passages ouverts) →
    Fait (contrôles des `JoursFait` = 30 derniers jours, 12 au plus). Un contrôle enregistré retire l'équipement des séances.
    « Bloqué pour les sorties » suit les réglages (`DepartSiRevisionDepassee`, `BloquerAuSeuil`), sans les changer.
    Intervention (`EnregistrerInterventionAsync`) : contrôle avec nature (`NaturesPour`) et intervenant (controle.nature /
    intervenant) ; depuis l'atelier, ferme le passage et remet en service, sauf refusé (reste HorsService).

Réglages : `Core/Modeles/Reglages` (instance unique injectée dans les services), modifiés via `ServiceReglages`
(validation + enregistrement par `IStockageReglages` → `parametres.json`). Avec `--base`, `parametres.json` est lu et écrit
à côté de la base d'essai : les essais ne touchent jamais les réglages réels. Préfixe des étiquettes : `CodeScanne.Nettoyer(brut, prefixe)`.
Sauvegardes : `IServiceSauvegarde` (dossier modifiable, dernière sauvegarde, `RestaurerAsync` qui vérifie le fichier,
copie les données actuelles en `materiel_…_avant-restauration.db`, puis réapplique les migrations).
Étiquettes QR : section retirée de l'écran Paramètres à la demande du demandeur (impression non prévue pour l'instant).

Cycle d'une sortie : `EnPreparation` (scan départ) → `ValiderDepart` → `EnCours` (scan retour ; ajout de matériel encore possible)
→ `Cloturer` → `Cloturee` (scan retour encore possible pour les manquants).

## Base de données

- Fichier : `%LOCALAPPDATA%\Asprenaut\materiel.db` (paramètre `CheminBase` dans `parametres.json`, même dossier).
- Tables snake_case : categorie, equipement, moniteur, sortie, sortie_equipement, journal, schema_version ;
  depuis 002 : controle, atelier (index partiel `ux_atelier_en_cours`), seance_tiv, colonnes moniteur.telephone / inspecteur_tiv,
  equipement.date_requalification ; depuis 003 : journal.equipement_id / sortie_id (sans clé étrangère, anciennes lignes
  complétées d'après leur texte) ; depuis 004 : type_sortie (6 types d'origine), sortie.type_sortie_id (sorties existantes
  reliées d'après leur famille). Pas de reconstruction de table : avec les clés étrangères actives, un DROP de sortie
  supprimerait en cascade son matériel (sortie_equipement) ; depuis 005 : colonnes de fiche de equipement (marque, modele,
  numero_serie, montage CHECK Etrier/Din, marque/modele/numero_deuxieme_etage, direct_system 0/1, manometre, octopus, numero_octopus) ;
  depuis 006 : type_sortie.utilisations et sortie.utilisations (1 à 10, sorties existantes reprises de leur type) ;
  depuis 007 : seance_equipement (blocs planifiés à une séance TIV, ON DELETE CASCADE), controle.nature et controle.intervenant.
- Horodatages en texte ISO 8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ` ; dates seules `yyyy-MM-dd`. Énumérations stockées par leur nom (CHECK en SQL).
- **Piège Dapper** : en paramètre, Dapper ignore les TypeHandler pour les enums et pour DateTime. Toujours passer
  `enum.ToString()` et `FormatIso.Ecrire(date)` dans les objets de paramètres (voir `Parametres(...)` dans les dépôts).
  En lecture, les handlers et la conversion texte → enum fonctionnent. Les tests Data vérifient le format brut stocké.
- Nouvelle migration : ajouter `Migrations/008_xxx.sql`, sans jamais modifier un script déjà livré
  (et mettre à jour `SchemaTests` : liste des scripts et des tables).
- Sauvegarde automatique au démarrage (avant les migrations) : `Sauvegardes\materiel_AAAA-MM-JJ_HHMM.db` à côté de la base,
  via l'API backup SQLite, 20 conservées.
- Données de démo : `DonneesDemo.ChargerSiVideAsync`, appelé seulement sous `#if DEBUG` si la base est vide.
- Erreurs inattendues : message simple + détail dans `%LOCALAPPDATA%\Asprenaut\erreurs.log`.

## Conventions

- Noms métier en français (Sortie, Equipement, Moniteur, Rendu…), anglais toléré pour la technique.
- Accès aux données asynchrones (`…Async`, `CancellationToken ct = default`).
- **Aucune logique métier dans les écrans** : un écran appelle un service Core et affiche le résultat.
  Les écrans passent par les services, jamais directement par les dépôts.
- **Aucune couleur en dur dans les écrans** : tout vient de `Apparence/Theme.cs` (couleurs, polices, rayons, hauteurs).
  Contrôles cliquables ≥ 44 px (`Theme.HauteurCliquable`), contraste ≥ 4.5:1 **dans les deux thèmes**.
- **Thème sombre / clair** : `Theme` lit une `Palette` (`PaletteSombre`, `PaletteClaire`) ; les noms sont des rôles
  (en clair, les variantes « Clair » sont les plus foncées). Choix dans Paramètres › Affichage → `ServiceReglages.ChangerAffichage`
  (réglage `Affichage`, écrit « Sombre »/« Clair » dans parametres.json) puis `Theme.Appliquer` : `MainForm` refait son menu
  (`Theme.Changement`) et Paramètres se recrée (`AllerA<EcranParametres>(EcranParametres.VersAffichage)`). Thème appliqué
  dans `Program` avant toute fenêtre. Les contrôles lisent les couleurs à leur construction : ne jamais mettre une couleur
  du thème dans un champ `static readonly` (utiliser une propriété). Couleurs fixes dans les deux thèmes : logo (`LogoClair/
  LogoCentre/LogoFonce/TexteSurLogo`), avatars (`DegradesAvatar`, `TexteSurAvatar`). `Natif.BarreDeTitre` et
  `Natif.ScrollbarsDuTheme` suivent le thème. Vérifier tout changement d'interface dans les deux thèmes.
- **Dimensions en pixels logiques 96 dpi.** WinForms ne met PAS à l'échelle les contrôles créés par code (ce poste est à 125 %) :
  les polices (en points) grandissent, pas les `Size`/`Padding`/`Margin`/lignes fixes. D'où :
  `ServiceNavigation` appelle `Dessin.MettreALEchelle(ecran, hote)` une fois par écran ; `MainForm.OnLoad` fait de même pour le menu ;
  un conteneur qui reçoit des enfants après l'affichage appelle `Dessin.MettreALEchelleSiAjoutTardif` dans `OnControlAdded`
  (voir `GrilleAdaptative`, `ListeMateriel`). Dans le dessin et les `OnLayout`, convertir avec `Dessin.Echelle` / `E(...)`.
  Les hauteurs de lignes des DataGridView sont fixées par `Theme.AppliquerGrille(grille, hauteurLigne)`.
- Boutons `AutoSize` : ne jamais repartir de `base.GetPreferredSize` (en GrowOnly, il inclut la taille actuelle → la largeur grandit
  à chaque mise en page).
- Petits écrans : `ScanInput` passe en mode compact (sans sonar, puis champ seul) ; le champ de scan reste toujours visible.
- Propriétés publiques des contrôles : `[DesignerSerializationVisibility(Hidden)]`, sinon l'analyseur WinForms WFO1000 casse le build.
- Pas de concepteur WinForms : les écrans sont construits en code (`Ui.Colonne`, `Ui.Entete`, `Ui.Rangee`, `Ui.Pile`).
- Dans un constructeur de contrôle, créer les enfants avant de modifier `Height`/`Size`, car `OnLayout` est appelé immédiatement.
- Un écran se construit avant d'être placé (préparé hors de vue par `ServiceNavigation`) : `EcranBase` lui donne une taille
  provisoire réaliste (1500 × 950 logiques). Sinon la première mise en page à 150 × 150 tassait les colonnes fixes et les
  grilles (date de l'en-tête Départ en « 0… », tuiles de « Nouvelle sortie » sur 6 rangées) sans retour en arrière.
  Un ajustement différé de ligne (`GrilleAdaptative`, `FiltreCategories`) relit toujours la dernière hauteur calculée.
- Messages à l'utilisateur : courts, en français simple.
- Chaque nouvelle règle métier → au moins un test dans Core.Tests ; chaque requête SQL nouvelle → couverte dans Data.Tests.
- Commits git petits et clairs, en français.

## Vérifier l'interface

L'application peut être pilotée sans intervention : lancer l'exe avec `--base <fichier temporaire>`, cliquer via UI Automation
(clic souris aux coordonnées de `BoundingRectangle`) et simuler le pistolet avec `SendKeys.SendWait("BLOC-031{ENTER}")`,
puis capturer la fenêtre. Après une erreur, consulter `erreurs.log`. Pour comparer à la maquette (1440 × 900 px CSS), donner
à la fenêtre 1800 × 1125 px à 125 % ; vérifier aussi la taille minimale 1280 × 800. Champs à trouver par nom + type Edit
(le premier champ texte de « Nouvelle sortie » est la date).
