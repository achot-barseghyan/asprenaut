# Asprenaut · Matériel

Application desktop Windows de suivi des sorties et retours du matériel du club de plongée Asprenaut
(blocs, gilets, détendeurs, combinaisons, palmes, masques, ordinateurs). Un seul PC, pas de réseau.
Utilisateurs : bénévoles peu à l'aise avec l'informatique → interface simple, gros boutons, messages courts.

Le matériel porte une étiquette QR. Le pistolet scanner USB est en mode clavier (HID) : il tape le code puis Entrée. Sans Entrée (pistolet sans suffixe,
téléphone en clavier Bluetooth), une rafale de frappe (`DetecteurRafale` : ≥ 3 caractères à ≤ 50 ms) suivie de 150 ms de pause
est validée seule (`Controles/ValidationSansEntree`, champ de scan et « Tester le pistolet ») ; la saisie à la main attend Entrée.

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
L'App cible `net10.0-windows10.0.19041.0` (Windows 10 1809 minimum) : c'est ce qui donne `Windows.Data.Pdf` (pages des manuels
rendues en images par Windows, `Infrastructure/LecteurPdf`), sans aucun paquet.

## Architecture

```
src/Asprenaut.Materiel.Core   net10.0          Modèles, règles métier, services, interfaces. Ne référence rien.
src/Asprenaut.Materiel.Data   net10.0          Dépôts SQLite (Dapper), migrations, sauvegardes, données de démo. → Core
src/Asprenaut.Materiel.App    net10.0-windows  WinForms : écrans, contrôles, thème, saisie scanner. Point d'entrée. → Core, Data
tests/…Core.Tests                              Règles métier sur dépôts en mémoire (Memoire/DepotsMemoire.cs, Contexte.cs)
tests/…Data.Tests                              Dépôts et schéma sur base SQLite temporaire (BaseTemporaire.cs)
```

- **Core/Services** : `ServiceScan` (scan départ/retour, retrait), `ServiceSortie` (créer, valider départ, aperçu et clôture),
  `ServiceInventaire`, `ServiceMoniteurs` (moniteurs et classes : cartes, fiche, ajout/modification, saison au 1er septembre), `ServiceRevisions`
  (échéancier, contrôles, atelier, séances TIV), `ServiceReglages`, `ServiceHistorique` (journal lisible, période, regroupement,
  export CSV, vie d'un équipement, utilisations d'un équipement), `ServiceGestion` (mode consultation / mode gestion, code gestionnaire),
  `ServiceBilan` (bilan de saison pour l'AG), `ServiceManuels` (notices des fabricants), `ServiceSignalements` (problèmes
  signalés au retour ou depuis l'inventaire), `ServiceAdherents` (adhérents : liste, filtres, fiche, ajout, import Excel),
  `ServicePrestataires` (magasins et organismes, Paramètres). Toutes les règles métier sont ici.
- **Core/Resultats** : `ResultatScan` (Niveau Succes/Avertissement/Erreur, Message, Equipement, `ActionProposee`, Code, AutreSortieId),
  `ResultatOperation`, `ApercuCloture`. Les cas métier ne lèvent pas d'exception : ils retournent un résultat.
- **Core/Saisie** : `CodeScanne.Lire` / `Nettoyer` et `AntiRebond` — logique pure, testée. Médailles QR du club :
  « (Detendeur) A24 Asprenaut Chalon 14 71-0025 » → code A24, type Detendeur (`LectureEtiquette`) ; sinon texte entier nettoyé
  (espaces, préfixe `ASP:`, majuscules) pour taper A24 à la main. Le type de la médaille choisit la catégorie d'un code inconnu
  (`ScanInput` → `CodeScanneEventArgs.Type` → `EcranScanBase.TypeLu` → `DevinerCategorieAsync(code, type)`).
  Paramètres › Pistolet : ligne « Lecture des médailles » (le préfixe n'est plus modifiable à l'écran).
- **Core/Abstractions** : interfaces des dépôts, `IHorloge` (heure injectable pour les tests), `IServiceSauvegarde`.
- **Data/Migrations** : `NNN_nom.sql` embarqués (LogicalName `Migrations.NNN_nom.sql`), appliqués au démarrage par
  `GestionnaireMigrations` selon `schema_version`, chacun dans sa transaction. Active le mode WAL.
  `AppliquerAsync(jusqua)` s'arrête à une version (tests de reprise de données, voir `HistoriqueDepotsTests`).
- **Data/Connexion** : `FabriqueConnexion` (une connexion par appel, `Foreign Keys=True`), `ConfigurationDapper`, `FormatIso`.
- **App** : `Program.cs` → `Demarrage` (DI, sauvegarde, migrations, démo en DEBUG) → `MainForm` (menu latéral + zone de contenu).
  Menu réductible : bouton « Réduire le menu » / « Agrandir le menu » en bas (`Reglages.MenuReduit`, `ServiceReglages.ChangerMenuReduit`,
  libre) ; réduit = `Theme.LargeurMenuReduit` (84), entrées `BoutonArrondi.IconeSeule` (trait turquoise sur l'entrée active),
  `CarteModeGestion.Compact` (cadenas seul), `CarteScanner.Compact` (point seul), noms en bulles ; `RefaireMenu` recrée le menu.
  `INavigation.AllerA<TEcran>(parametre)` crée l'écran via `ActivatorUtilities` (non retenu par le conteneur), le met à l'échelle
  de l'écran et dispose le précédent (masqué puis disposé, jamais `Controls.Remove` : le retrait déplace tout l'arbre de
  fenêtres vers la « parking window » de WinForms, 1,4 s pour Paramètres). Écrans dans `Ecrans/` (héritent d'`EcranBase` ; Départ/Retour héritent d'`EcranScanBase`
  qui sérialise les scans et fournit fil d'Ariane + carte d'en-tête via `Composer(corps)`).
- **Maquette** : https://claude.ai/artifact/8op5sAWFbf4mRQCxzd37aa (Sorties, Nouvelle sortie, Départ, Retour, Inventaire, Manuels,
  Moniteurs (page « Membres du club »), Historique, Bilan de saison, Révisions, Paramètres, Déverrouillage, Utilisations d'un équipement). Polices Sora/Manrope remplacées par Segoe UI Variable ; pas de fond dégradé ni de bulles animées.
- **Briques d'interface** (`Controles/`) : `Carte`, `ControleDessine` (base des contrôles peints), `AnneauProgression`,
  `IndicateurEtapes`, `PastilleDate`, `Pastille`, `Picto`, `GrilleAdaptative` (colonnes auto, ajuste sa ligne de TableLayoutPanel),
  `Tuile`, `FilAriane`, `BarreRecherche`, `ChampSaisie` (`Icone`, `ADroite` et `IndicatifSiFocus` optionnels), `ChampDate` (remplace le DateTimePicker, blanc et en anglais),
  `ListeDeroulante` (cadre et bouton repeints par-dessus Windows dans `WndProc`), `ListeMateriel` (styles Depart / ARendre / Rendu),
  `ScanInput` + `BandeauMessage` (scan réussi : `Ressources/Sons/ScanReussi.wav`, ressource `Sons.ScanReussi.wav`, PCM 16 bits mono
  car `SoundPlayer` ne lit pas tous les WAV ; avertissement / erreur : sons de Windows ; réglage `SonsActifs`), `BandeauAccueil`, `Logo`, `CarteScanner`, `Tableaux` (pastilles et pictogrammes dans les DataGridView),
  `FiltreCategories` (puces « Tout 12 / Blocs 3 », `CouleurPoint` et `Unite` optionnels), `FenetreFormulaire` (fenêtre modale sombre ;
  la validation passe par un service et ne ferme qu'en cas de succès), `Interrupteur`, `SelecteurNombre`, `Bascule` (options côte à côte, nombres), `ChoixResponsable`, `CarteSection`, `LigneReglage`,
  `ListeChoix` (liste de choix dessinée, badge + titre + détail, clic ou flèches), `JournalHistorique` (journal par jour dessiné dans un seul contrôle défilant : seule la partie visible est peinte, le clic sur un
  code est retrouvé par calcul — à préférer à un contrôle par ligne dès qu'une liste peut compter des milliers d'éléments).
  `EcranBilan` : cartes entièrement dessinées (classes imbriquées), placées par `PageBilan` selon la largeur (4/2 chiffres,
  3/2/1 cartes du bas) ; la page est focusable pour que la molette la fasse défiler, et sa largeur est posée à la main
  (en Dock Top, la barre de défilement apparue après coup recouvrait le bord droit des cartes).
- **Impression/** : `DocumentPdf` dessine des `BlocPdf` en GDI+ (thème clair) et les envoie à l'imprimante Windows
  « Microsoft Print to PDF » (`PrintToFile`), sans bibliothèque ; pagination par blocs, titre gardé avec la suite.
  `RapportBilan` (bilan pour l'AG), `RegistrePdf` (registre des contrôles, bouton « Registre PDF » de Révisions), `FicheSuiviPdf` (fiche de suivi EPI).
  `Infrastructure/Fichiers` : `EnregistrerPdfAsync` (imprimante, dialogue, erreurs, ouvrir) et `EnregistrerCsvAsync` (UTF-8 avec BOM).
- **Exports CSV** : `Core/Saisie/Csv` (séparateur « ; », guillemets, apostrophe devant = + - @). Inventaire : lignes affichées
  (`ServiceInventaire.ExporterCsv`). Paramètres › Exporter CSV : toutes les tables dans « Export Asprenaut AAAA-MM-JJ »
  (`ServiceExport` met en forme les valeurs brutes de `ExportRepository` selon `GenreColonne` : dates, états en français).
- **Révisions** (`EcranRevisions`) : compteurs « au club » / « hors du club » (`CompteursLieu`), accolade « Réparation externe »
  (`AccoladeExterne`) au-dessus des colonnes 3 et 4, tableau à 5 colonnes dessinées (`ColonneRevision`, 2 par rangée sous 1050 px logiques) et cartes
  (`CarteEquipement`, vrais `BoutonArrondi` liés à `ActionProtegee.Revisions`), filtre par famille, champ de scan qui ouvre
  le panneau « Ajouter une intervention » en haut de page, comme le bouton « Ajouter une intervention » de l'en-tête (fenêtre de
  choix : onglets « À prévoir » (règle 35) / « Déjà faite », recherche `ServiceInventaire.ListerAsync` ou médaille, `ListeChoix`,
  réformés exclus) ; « Fiche › » mène à Historique.
  Panneau : champ « Description (facultatif) » (controle.notes). Cartes : précision du signalement ouvert (`CarteRevision.Precision`,
  trait doré) et description de l'intervention dans « Fait » (`CarteRevision.Description`), en citation sous le détail.
  `PageRevisions.OnLayout` recommence le placement si la hauteur posée a fait apparaître la barre (mise en page imbriquée ignorée).
  Colonnes repliables : bouton œil de chaque en-tête → bande de `ColonneRevision.LargeurRepliee` (56) avec numéro, nombre et titre
  vertical (cercle placé sous `Bascule.Bottom`), un clic la rouvre ; les colonnes ouvertes se partagent la place (les repliées ne comptent pas dans les 5 / 2 par rangée),
  accolade seulement si assez large. Choix retenu : `Reglages.ColonnesRevisionsRepliees`, `ChangerColonnesRevisionsRepliees` (libre,
  une colonne reste ouverte).
- **Paramètres** (`EcranParametres`) : deux onglets (`Bascule`). « Paramètres » (fonctionnement de l'app) : Sécurité, Pistolet,
  Règles des sorties, Données et sauvegardes, Affichage, À propos. « Configuration du club » : Club, Types de sortie, Compteur
  d'utilisation, Prestataires, Problèmes signalés — construit et rempli seulement à sa première ouverture (`AfficherOngletAsync`,
  mis à l'échelle à ce moment-là) : la page s'ouvre deux fois plus vite. « Paramètres › Types de sortie / Club / Prestataires /
  Problèmes signalés » dans ce document = cet onglet.
- **Inventaire** : colonne « Utilisation » (`ServiceCompteur.ListerUtilisationsAsync` : « 64 / 80 utilisations », barre jaune à 90 %,
  rouge au seuil, pastille « SEUIL MODÈLE / ÉQUIPEMENT » selon `OrigineSeuil`, `Tableaux.PeindreUtilisation`) dans les trois jeux
  de colonnes ; nom souligné en pointillé + ligne « marque modèle · catégorie » (`Tableaux.PeindreIconeDeuxLignes`), le clic
  sur le nom, le double-clic ou Entrée ouvrent la fiche de suivi. Colonne « Actions » (maquette) : trois boutons carrés dessinés
  (`Tableaux.PeindreBoutonsIcones`, clic par `BoutonIconeSous`) — fiche de suivi EPI, drapeau « Signaler » (rempli et coloré
  si un problème est ouvert : il le modifie), crayon « Modifier » (`FormulaireEquipement.ModifierAsync`, cadenas jaune tant
  que le mode gestion est fermé, mis à jour sur `ServiceGestion.EtatChange`) ; au-dessus, la pastille du problème ouvert
  (« À surveiller · Débit continu », rouge si hors service) et la ligne légèrement teintée. Bulle de la cellule : les trois
  boutons (une bulle par bouton avec `ToolTip.Show` ne s'affichait pas). Lien « Voir les sorties » sous le compteur
  (`Tableaux.ZoneLienUtilisation` pour le clic, lignes de 70 px) → `AllerA<EcranUtilisations>(idEquipement)` (règle 29).
- **Pictogrammes** : `Apparence/Icones.cs`, chemins SVG 24 × 24 de la maquette lus par `CheminSvg` et tracés en GDI+.
- **Icône de l'application** : `Ressources/Asprenaut.ico` (16 à 256 px ; tailles ≤ 96 en bitmap, 128 et 256 en PNG), fabriquée
  depuis `Ressources/Asprenaut.png` (1024 px). `ApplicationIcon` dans le .csproj pour le .exe, ressource embarquée lue par
  `Icones.IconeFenetre` pour `MainForm` et `FenetreFormulaire` (barre de titre, barre des tâches).
- **Logo du club** : `Ressources/LogoClub.png` (image de la maquette, ressource `LogoClub.png`), dessiné par `LogoClub` (`Controles/Decor.cs`)
  dans une tuile blanche (`Theme.FondLogoClub`, fixe dans les deux thèmes) à gauche du bandeau de l'écran Sorties.

## Règles métier (chacune a au moins un test dans Core.Tests)

1. Un équipement n'est « Dehors » que dans une seule sortie (aussi garanti par l'index partiel `ux_sortie_equipement_dehors`).
2. HorsService / Reforme ne part pas.
3. Double scan au départ dans la même sortie → ignoré, « déjà scanné ».
4. Code inconnu au départ → `ActionProposee.CreerEquipement` : l'écran ouvre `FormulaireEquipement` (code pré-rempli, catégorie
   devinée par `ServiceInventaire.DevinerCategorieAsync`), puis rescanne le code dans la sortie.
   Création (`ServiceInventaire.CreerAsync`) : code nettoyé et unique, nom et catégorie obligatoires, pas de date de contrôle passée,
   requalification réservée aux blocs, toujours EnService. Code proposé : `ProposerCodeAsync` dans la forme déjà utilisée
   (BLOC-034 → BLOC-035, A24 → A25). Étiquettes QR du club, sans tiret : A = détendeur adulte, E = détendeur enfant,
   S = gilet stab (A24, E03, S12) ; catégorie vide → A01 / S01 ; `DevinerCategorieAsync` reconnaît A/E → Détendeurs,
   S → Gilets (DET, GIL encore acceptés) ; `NomPropose` pré-remplit le nom (« Détendeur enfant »). Données de démo : A01, S01, E01.
5. Retour d'un équipement absent de la sortie → avertir sans rien modifier ; s'il est dehors ailleurs →
   `ActionProposee.RentrerDansAutreSortie` (l'écran rescanne alors dans `AutreSortieId`).
6. Retrait possible seulement en préparation (`StatutSortie.EnPreparation`).
   Modifier la sortie (date, type, intitulé, moniteur, lieu, notes) : seulement en préparation, mêmes contrôles qu'à la création,
   le moniteur déjà responsable reste accepté s'il est devenu inactif (`ServiceSortie.ModifierAsync`, journal `SortieModifiee`).
   Supprimer une sortie : seulement si aucun matériel n'y est rattaché (`ServiceSortie.SupprimerAsync` ; la requête SQL du dépôt
   re-vérifie, journal `SortieSupprimee`). Écran Départ : boutons « Modifier la sortie » et « Supprimer » (sortie vide) en haut à droite.
7. Clôture : Dehors → Manquant, confirmation avec le nombre (`ApercuClotureAsync`) s'il reste du matériel dehors
   (`ApercuCloture.ConfirmationNecessaire`, réglage `ConfirmerCloture`).
8. Un Manquant scanné plus tard au retour (dans sa sortie ou depuis une autre) ou au départ d'une autre sortie → Rendu + journal
   `ManquantRetrouve` (règle 32).
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
14. Atelier (passage = départ chez un prestataire, historique des envois) : l'envoi passe l'équipement HorsService (refusé s'il est dehors, déjà en atelier, ou sans prestataire) ; le retour le remet EnService.
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
    `CompteurSortie` : utilisations d'une sortie différentes de celles de son type, à la création comme en modification,
    et clôturer avec des manquants si `CodePourCloturerAvecManquants`, `RendreManquant` : scan ou « Marquer rendu » dans une
    sortie clôturée) sont refusées par les services tant que le mode gestion n'est
    pas ouvert. Tous les paramètres (thème compris) sont protégés, sauf réduire le menu et replier des colonnes des révisions (confort d'affichage). Libres : créer/modifier une sortie en préparation (avec les
    utilisations de son type), scanner,
    valider, clôturer sans manquant, consulter, tester le pistolet, faire une sauvegarde.
    Retour en consultation après `MinutesDeverrouillage` (5/15/30/60) sans activité ; `VerrouillerALaFermeture` coupé → le mode
    gestion ouvert est repris au lancement (`DeverrouilleAuDemarrage`). Changer ou retirer le code demande l'ancien.
    5 codes faux d'affilée → saisie bloquée 1 minute. Les services reçoivent `ServiceGestion? gestion = null` (null = pas de contrôle,
    pour les tests qui n'en ont pas besoin). App : `GardeGestion.Exiger(...)` avant chaque action protégée (pavé
    `DialogueDeverrouillage`, chiffres lus d'après le caractère tapé : AZERTY), `CarteModeGestion` dans le menu, `FiltreActivite`.
    Visuel « verrouillé » : `GardeGestion.Lier(controle, action)` met `Verrouille` à jour à chaque changement de mode —
    `PastilleVerrou` (rond jaune au cadenas foncé accroché au coin haut droit, débord de 9 px peint par les parents opaques,
    info-bulle « Code gestionnaire requis ») sur un `BoutonArrondi` ou un `SelecteurNombre` (qui prend aussi une bordure dorée ;
    compteur de Nouvelle sortie et de Modifier la sortie : le clic demande le code), badge « Code requis » d'une `CarteSection`,
    `BandeauConsultation` (bandeau doré + bouton « Déverrouiller ») sous le titre des pages presque entièrement protégées
    (Révisions, Paramètres) : leurs boutons ont `AfficherPastille = false`. Tout nouveau bouton d'action protégée doit être lié.
    Code oublié : supprimer la ligne `CodeGestionnaire` de `parametres.json` (application fermée).
17. Bilan de saison (`ServiceBilan`, consultation libre) : saison précédente ou en cours (1er septembre → 31 août), seules les
    sorties parties comptent (en cours ou clôturées, datées dans la saison). Mois de septembre à juin, juillet/août seulement s'il
    y a eu des sorties. Manquant à la clôture : perdu s'il l'est encore, retrouvé s'il a été rendu après la clôture.
    Tension = part du stock d'un groupe (catégorie + taille, hors réformé, au moins `StockMinTension` = 2 ; détendeurs séparés en
    « adultes » / « enfants » : médaille E + numéro ou « enfant » dans le nom, `ServiceBilan.EstDetendeurEnfant`) sortie lors de la sortie
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
   Ajouter un seuil spécifique (maquette) : Modèle | Équipement, recherche ou scan d'une médaille, liste de l'inventaire limitée aux
   catégories à compteur (`ListerModelesAsync` : seuil actuel ; `ListerEquipementsAsync` : seuil, origine, utilisations), valeur
   choisie (`AjouterSeuilSpecifiqueAsync(portee, saisie, sorties)`, 10 à 1000).
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
23. Tableau des révisions (`ServiceRevisions.TableauAsync`, migrations 007 et 016, voir règle 33) : À prévoir (calculé : échéance
    non à jour ou 90 % du seuil d'utilisations, équipement EnService, ou problème signalé ; pas s'il a déjà une révision en cours)
    → Réparation interne (séance TIV : `AjouterASeanceAsync`, refusé sans séance ou pour un non-bloc, table seance_equipement)
    → À envoyer → Chez le prestataire → Fait (révisions faites des `JoursFait` = 30 derniers jours, non archivées).
    Un contrôle enregistré retire l'équipement des séances.
    « Bloqué pour les sorties » suit les réglages (`DepartSiRevisionDepassee`, `BloquerAuSeuil`), sans les changer.
    Intervention (`EnregistrerInterventionAsync`) : contrôle avec nature (`NaturesPour`) et intervenant (controle.nature /
    intervenant) et description facultative (`Intervention.Notes` → controle.notes, `LongueurDescription` = 200) ; depuis l'atelier,
    ferme le passage et remet en service, sauf refusé (reste HorsService).
24. Moniteurs et classes (migration 008, maquette « Moniteurs et classes ») : une classe (Niveau 1 à 4 créées par la migration)
    est responsable d'une sortie comme un moniteur. Rangée dans la table moniteur (`Moniteur.Genre` = `GenreResponsable.Classe`,
    colonne moniteur.genre) : sortie.moniteur_id reste l'unique responsable. Classe : nom obligatoire (dans `Nom`, prénom vide),
    unique sans tenir compte des majuscules, 30 caractères au plus ; abréviation de la pastille (`Abrege`, 4 au plus, proposée par
    `ServiceMoniteurs.AbregeDe` : « Niveau 2 » → « N2 ») ; ni téléphone, ni niveau, ni TIV (jamais inspecteur). Le genre ne change
    pas à la modification. Journal `ClasseCreee` / `ClasseModifiee`. Afficher `SortieResume.LibelleResponsable`
    (« Moniteur : … » / « Classe : … »). App : onglets `Bascule` dans `EcranMoniteurs`, `ChoixResponsable` (Moniteur | Classe
    puis la liste) dans Nouvelle sortie et Modifier la sortie. Filtres de l'écran Membres (`FiltresMembresTests`) : onglet Moniteurs, recherche (prénom, nom, niveau, téléphone, accents ignorés) et puces Tous / Actifs / Inspecteurs TIV / Matériel dehors / Inactifs (`ServiceMoniteurs.Filtrer` → `ListeMoniteurs`, nombres après la recherche) ; onglet Adhérents, recherche, liste Niveau (tous, ceux du club, « Sans niveau » = `ServiceAdherents.SansNiveau`) et puces Tous / Matériel dehors / Non rendu / Nitrox / Désactivés ; pas de filtre pour les classes ; chaque onglet a sa recherche. Données de démo : chargées si aucun moniteur (genre Moniteur).
25. Manuels (`ServiceManuels`, migration 009, écran `EcranManuels`, maquette « Manuels ») : consultation libre ; ajouter, modifier,
    retirer = action protégée Matériel. Le PDF est copié dans `Manuels\` à côté de la base (`IStockageManuels`, nom déjà pris →
    « (2) ») ; l'écran lit son nombre de pages (`LecteurPdf.CompterPagesAsync`). Marque et titre obligatoires ; sommaire tapé une
    section par ligne, page à la fin (« 8.2 Entretien ; 17 », `SectionManuel.Lire`), pages ≤ celles du PDF ; délais du fabricant
    une ligne « Libellé : délai » ; révision du fabricant 1 à 120 mois ; page de l'entretien dans le PDF. Équipements liés : même
    catégorie et même marque (majuscules, espaces, tirets et points ignorés : « Aqualung » = « Aqua Lung »), hors réformés. `Conformite` compare la révision du fabricant à
    `ServiceRevisions.MoisRevision` de la catégorie (pas pour les blocs) ; « Régler le club » (`AlignerRevision`, action Paramètres)
    écrit le délai du fabricant dans `MoisEntreRevisionsParCategorie`. Le PDF est lu en mémoire (fichier jamais verrouillé) ;
    impression par `PrintDocument` (pages rendues à 200 dpi). Départ et Retour : bouton « Manuel » sur les lignes dont l'équipement
    a un manuel (`ManuelsDesEquipementsAsync`, `ElementMateriel.ManuelId`) → `AllerA<EcranManuels>(idManuel)`.
    Notices fournies : 5 PDF dans `App/Ressources/Manuels` (ressources « Manuels.<fichier> »), avec sommaire et délais dans
    `Infrastructure/ManuelsFournis` ; installées au démarrage par `InstallerFournisAsync`, chacune une seule fois (réglage
    `ManuelsFournis` : noms des fichiers déjà installés ; une notice ajoutée dans une version suivante s'installe au lancement suivant), sans code, sans doublon (même marque et titre) : un manuel retiré ne revient pas.

26. Catégories (`ServiceInventaire.AjouterCategorieAsync` / `SupprimerCategorieAsync`, action protégée Materiel) : depuis la fiche
    d'un équipement (boutons + et corbeille à côté de la liste), nom obligatoire, unique sans tenir compte des majuscules, 30 caractères
    au plus. Une catégorie utilisée par un équipement ou un manuel ne se supprime pas (la requête SQL re-vérifie) ; il en reste toujours
    une. La liste est relue après chaque ajout / suppression (le code proposé suit la catégorie choisie).

27. Fiche de suivi EPI (maquette « Fiche de suivi EPI », migration 011) : clic sur le nom souligné, double-clic, Entrée ou bouton
    « Fiche de suivi EPI » (colonne Actions) de l'inventaire → `AllerA<EcranFicheSuivi>(idEquipement)` (fil d'Ariane « Inventaire › Fiche de suivi EPI · A05 »).
    `ServiceHistorique.ObtenirFicheSuiviAsync` : `VieEquipement`, titre (nom puis modèles sans la marque répétée, sans le manomètre),
    composants (`ServiceInventaire.ComposantsDe` : détendeur → Premier étage, puis Deuxième étage / Octopus / Manomètre renseignés ;
    autre → une carte « Équipement complet »), interventions (contrôles + passages en atelier, du plus récent ; nature « : détails »).
    Une intervention peut viser un composant (`controle.composant`, vide = équipement complet ; composant absent → refusé) :
    elle ne change ni les échéances ni le compteur de l'équipement (seul un refus le met hors service) et ne compte pas comme
    « dernier contrôle » / « dernière révision » de l'équipement (requêtes filtrées sur `composant = ''`). Tuile « Dernier contrôle »
    d'une carte : le composant ou l'équipement complet ; filtre par composant (`InterventionsDe`) idem. Écran : en-tête (code, statut,
    compteur, prochaine révision, « Manuel » si un manuel correspond, « Imprimer la fiche » → `Impression/FicheSuiviPdf`), cartes
    dessinées, puces `FiltreCategories`, formulaire « Ajouter une intervention » (Révisions, avec composant et détails ;
    `ChoixResultat` partagé) et tableau dessiné. « + Ajouter un composant » / « Modifier la fiche » ouvre `FormulaireEquipement`
    (Materiel) ; « Ajouter une intervention » demande Revisions. `Controles/FicheVie` reste la fiche de l'Historique.
    Ajouter un composant (migration 012, `ServiceInventaire.AjouterComposantAsync`, Materiel, fenêtre `Ecrans/FormulaireComposant`) :
    détendeur seulement, deuxième étage / octopus / manomètre absent (`Composition`, `ComposantsAjoutables`) ; modèle obligatoire
    (marque pour le manomètre), puces des modèles connus (`ModelesConnusAsync` : deuxièmes étages et octopus confondus) ;
    fabrication « MM/AAAA » (`LireMoisAnnee`) et mise en service ni futures, mise en service pas avant la fabrication ; origine
    neuve / récupérée. Modèle et n° dans la fiche matériel (n° du manomètre dans la table composant), fabrication, mise en service
    et origine dans la table composant (tuiles Fabrication / Mise en service / Origine de la carte). Journal `ComposantAjoute` ;
    option « Noter « Montage octopus » » : intervention conforme sur ce composant, datée de la mise en service. Bouton
    « + Ajouter un composant » de la fiche s'il manque un composant, sinon « Modifier la fiche » (`FormulaireEquipement`).
    `FenetreFormulaire.EnTete(sousTitre, badge)` : sous-titre et pastille « Mode gestion ».

28. Problèmes signalés (`ServiceSignalements`, migration 013, maquette « Retour du matériel ») : fenêtre « Problème sur A07 »
    (`Ecrans/FormulaireSignalement`, `CibleSignalement`) ouverte au retour (bouton « Signaler un problème » à côté du message
    du dernier rendu, drapeau sur chaque ligne « Rendu ») et depuis l'inventaire (drapeau de la colonne Actions). Causes selon la
    catégorie (tuiles réglables, règle 34 ; d'origine `CausesParDefaut`, « Autre » en dernier), cause obligatoire (60 car.), précision
    facultative (200 car.) sauf pour une tuile « précision obligatoire ». « À surveiller » libre : l'équipement reste en service (pastille « À surveiller · cause » au-dessus des actions de l'inventaire).
    « Hors service » : action protégée Révisions, refusé si dehors, met HorsService (`MisHorsService`) ; redescendre à
    « À surveiller » demande aussi le code et ne remet en service que si c'est le signalement qui l'avait arrêté (et hors atelier).
    Un seul signalement ouvert par équipement (index `ux_signalement_ouvert`) : signaler de nouveau le modifie. Réformé refusé.
    Journal `ProblemeSignale` (type Révision). Révisions › À prévoir : cartes `CarteRevision.Signalement` en tête, famille
    « Signalé » (`ServiceRevisions.FamilleSignalement`, première du filtre), même hors service, sauf en atelier ; actions
    « Envoyer en atelier » / « Faire maintenant » (jamais la séance TIV). Toute intervention enregistrée (`EnregistrerControleAsync`)
    ferme le signalement ouvert et, s'il avait mis hors service, remet en service sauf résultat refusé ou atelier en cours.
    Fiche EPI : interventions `TypeInterventionSuivie.Signalement` (« Problème signalé : cause : précision », responsable de la
    sortie ou « Inventaire », « · traité »). Retour : bandeau « N problèmes signalés · Voir › » (ceux de la sortie) → Révisions.
    Export : `signalements.csv`.

29. Utilisations d'un équipement (`ServiceHistorique.ObtenirUtilisationsAsync`, écran `EcranUtilisations`, maquette « Utilisations
    d'un équipement », consultation libre) : toutes ses sorties, de la plus récente, avec les utilisations de chacune (« MODIFIÉ »
    si elles diffèrent de celles de son type) et le compteur juste après son retour ; chaque contrôle de l'équipement complet
    (pas d'un composant) est intercalé et remet le compteur à 0, comme `ListerCompteursAsync` (un retour du jour du contrôle compte
    avant lui). Sortie encore dehors ou manquante : pas encore comptée. Retour : Rendu, « Rendu · À surveiller · cause » si un
    problème a été signalé pour cette sortie, Retrouvé (rendu après la clôture), Sorti, Manquant. Chiffres : compteur et seuil
    (`ServiceCompteur.SeuilEtOrigine`), sorties depuis le dernier contrôle, total, problèmes depuis le dernier contrôle. Filtre par
    type de sortie (`Theme.CouleurTypeSortie`), les contrôles restent affichés ; 12 sorties puis par 50. « Exporter CSV » : lignes
    du filtre (`ExporterUtilisationsCsv`). `ListerPassagesAsync` ramène aussi type, utilisations (de la sortie et du type) et genre
    du responsable.

30. Sorties nominatives et adhérents (`ServiceAdherents`, `ServiceSortie.AttribuerAdherentAsync`, migration 014, maquettes « Départ du
    matériel », « Membres du club », Paramètres › Types de sortie) : case « Nom de l'adhérent à chaque scan » par type (`CaseOption`,
    `ServiceTypesSortie.ChangerNominatifAsync`, action Paramètres ; Mer et Lac d'origine). Une sortie reprend le réglage de son type à
    sa création (et si son type change en préparation) : changer le type ensuite ne la touche pas. Sortie nominative : badge « Nom
    obligatoire à chaque scan » ; chaque scan attend son adhérent (`ServiceSortie.AAttribuer`) : le scan suivant est refusé
    (`ServiceScan`), la validation aussi (« Attribuez A07 à un adhérent… »). Départ : `PanneauAdherent` remplace la zone de scan
    (« À qui est ce détendeur ? », 3 derniers adhérents de la sortie `AdherentsRecents`, liste des actifs filtrée sans accents,
    flèches + Entrée ; maquette : panneau en haut de sa zone (Dock Top, hauteur d'après le contenu, placement à la main),
    tuiles à la ligne, pastilles rondes de la couleur de l'adhérent (`PeindreAvatar`, d'après son numéro), liste dessinée
    `ListeAdherents` ouverte sous le champ par la frappe, les flèches, un clic ou le chevron, posée dans la zone parente
    pour déborder de la carte, jamais focusable ; Échap la referme ; un code tapé par le pistolet dans la recherche est refusé), puis le focus revient au champ de scan ; liste
    « À attribuer » / nom de l'adhérent, vue « Par adhérent ». « Annuler ce scan » retire l'équipement en attente (aussi après la
    validation). Retour : nom de l'adhérent sur le matériel dehors. Clôture : `ManquantDeclare` garde `AdherentId` (« pris par … »
    dans l'historique). Adhérents (onglet de `EcranMoniteurs`, menu « Membres ») : jamais responsables de sortie, jamais supprimés
    (inactif : plus proposé au départ, gardé dans l'historique) ; nom et prénom obligatoires, licence unique ; filtres Tous / Matériel
    dehors / Non rendu ; fiche (matériel en ce moment, non rendu, dernières sorties nominatives, Modifier) ; ajouter et importer
    demandent le code (action Moniteurs). Import (`ImporterAsync`, `LireTableau`) : en-tête « Nom » + « Prénom » (Niveau, Licence,
    Téléphone facultatifs), mise à jour par licence (sinon nom + prénom), ajout des autres, personne retiré ; fichier .xlsx ou .csv
    lu par `Infrastructure/LecteurTableur` (zip XML, sans bibliothèque). « Modèle Excel à remplir » (libre, onglet Adhérents) : classeur vide écrit par `Infrastructure/EcrivainTableur` (zip XML, sans bibliothèque ; en-têtes `ServiceAdherents.ColonnesModele(niveaux)` = ceux de l'import, listes Niveau et Nitrox rangées dans une feuille cachée « Listes », première ligne figée, cases au format texte, bulle d'aide par colonne, liste Oui / Non pour Nitrox, feuille « Mode d'emploi » `ModeEmploiModele`), à donner au club puis à réimporter ; nom `NomFichierModele`. Export : `adherents.csv`, colonne Adhérent du matériel des sorties.

31. Nitrox et adhérents désactivés (migration 015) : case « Nitrox » de la fiche des blocs et des détendeurs seulement
    (`ServiceInventaire.PeutEtreNitrox`, vidée pour les autres catégories, journal « nitrox » / « plus nitrox ») ; « Certifié nitrox »
    sur l'adhérent (formulaire, pastille NITROX, colonne « Nitrox » oui / non de l'import : absente ou case vide = inchangé). Au départ d'une sortie
    nominative, `ServiceAdherents.RefusPour` (utilisé par `AttribuerAdherentAsync`) refuse un adhérent désactivé et un équipement nitrox
    pour un adhérent non certifié, et un deuxième équipement de la même catégorie (deux détendeurs, deux blocs, deux gilets… : `DejaPris`, ceux déjà rendus ne comptent pas) : l'équipement reste en attente, le panneau marque l'adhérent « A déjà A12 » (`MotifCourt`). Ce contrôle est une option de la sortie (`Sortie.UnParCategorie`, `NouvelleSortie.UnParCategorie`, migration 017) : activée par défaut, carte « Un équipement par catégorie et par adhérent » (`Ecrans/OptionUnParCategorie`, interrupteur) dans Nouvelle sortie (types nominatifs) et Modifier la sortie ; la couper demande `ActionProtegee.PlusieursParCategorie` (cadenas sur l'interrupteur), la remettre est libre. Le panneau l'annonce (« nitrox : adhérent certifié nitrox »),
    met les certifiés en premier et marque les autres « pas certifié nitrox ». Fiche adhérent : « Désactiver » (confirmation) /
    « Réactiver » (`ChangerActifAsync`, action Moniteurs) ; désactivé : ne sort plus de matériel, gardé dans la liste (à la fin)
    pour être réactivé. Sortie non nominative : pas de contrôle nitrox.
    Niveaux (ServiceAdherents.Niveaux, Reglages.NiveauxAdherents, liste d'origine Reglages.NiveauxParDefaut FFESSM, réglée dans
    Paramètres › Configuration du club › Niveaux des adhérents, ServiceReglages.ChangerNiveauxAdherents : 1 à 30, 30 car., sans doublon) :
    liste déroulante du formulaire ; EnregistrerAsync n'accepte qu'un niveau de la liste (NiveauReconnu : majuscules, accents,
    espaces et ponctuation ignorés, « N2 » / « niv 2 » → « Niveau 2 ») ou l'ancien niveau inchangé (« hors liste ») ; l'import ramène
    le niveau à la liste, un inconnu est laissé vide (l'ancien gardé) et compté dans le message.

32. Manquants retrouvés (`ManquantsTests`) : un équipement Manquant scanné au départ d'une sortie est d'abord noté Rendu dans
    chaque sortie où il manquait (journal `ManquantRetrouve` relié à l'ancienne sortie, message « A01 était manquant (sortie
    « … » du 06/11) : noté retrouvé. » devant le résultat du départ), même si son départ est ensuite refusé ; l'inventaire ne le
    montre plus manquant. Sortie clôturée (ouverte d'un simple clic depuis Sorties) : scanner au retour ou « Marquer rendu »
    (bouton ✓ des lignes manquantes, confirmation, `ServiceScan.RendreManquantAsync`) demandent `ActionProtegee.RendreManquant`.
33. Révisions en 5 étapes et prestataires (migration 016, `EtapesRevisionsTests`, maquette « Révisions ») : `EtapeRevision` APrevoir
    (calculée) → Interne → AEnvoyer → ChezPrestataire → Fait, notées dans `SuiviRevision` (table revision, une seule en cours par
    équipement). Toutes les transitions demandent `ActionProtegee.Revisions` et écrivent `ActionsJournal.RevisionEtape` (lignes
    « Étape » de la fiche EPI, `TypeInterventionSuivie.Etape`). À prévoir : « Séance TIV » (bloc dont la TIV est due, pas un
    signalement) ou « Réparer au club » (`ReparerAuClubAsync` → Interne, par `Reglages.ResponsableMateriel`) ; « Envoyer chez un
    prestataire » (`EnvoyerChezPrestataireAsync` → AEnvoyer : prestataire actif obligatoire, note d'envoi 120 car. facultative ;
    refusé pour un bloc prévu à la séance TIV ; l'équipement reste au club). AEnvoyer : « Marquer comme parti » (`MarquerPartiAsync`
    → ChezPrestataire : `DateEnvoi` = aujourd'hui, retour prévu facultatif et pas passé, ouvre un passage en atelier → HorsService,
    refusé s'il est dehors) ou « Réparer au club ». Interne et ChezPrestataire : « Enregistrer le résultat » / « Retour du
    prestataire » = le panneau d'intervention ; toute intervention (`EnregistrerControleAsync`) passe la révision en cours en Fait
    (ou crée une ligne Fait), et `EnregistrerInterventionAsync` ferme le passage en atelier de la révision ; Refusé → hors service.
    Retour en retard : `RetourEnRetard` (ChezPrestataire et retour prévu passé). Compteurs (`TableauRevisions`) : `AuClub` =
    équipements en À prévoir, Interne ou À envoyer, sauf en sortie ou manquant (`CarteRevision.HorsClub` : « En sortie · intitulé »
    / « Manquant », pastille à la place de « Au club » ; `EnSortie`, « · 1 en sortie » dans le titre du compteur) ; `HorsClub` =
    ChezPrestataire, `ParPrestataire`, `RetoursEnRetard`.
    « Bloqué pour les sorties » à toutes les étapes sauf Fait (réglages, ou signalement Hors service). Fait : « Archiver tout » et
    bouton par carte (`ArchiverAsync` / `DesarchiverAsync`, sans code : archive = 1 seulement à l'étape Fait, rien n'est supprimé ;
    « Annuler » tant qu'on reste sur la page) ; au-delà de 30 jours, la carte quitte la colonne. `EnvoyerEnAtelierAsync` (ancien
    chemin, texte libre) crée aussi la révision ChezPrestataire ; `RetourAtelierAsync` sans intervention l'abandonne.
    Prestataires (`ServicePrestataires`, Paramètres › Prestataires, action Paramètres) : nom obligatoire, unique sans les majuscules,
    60 car. ; jamais supprimés, désactivés (plus proposés). Couleurs `Theme.Prestataire` / `PrestataireClair` /
    `TexteSurPrestataire`, boutons `StyleBouton.Prestataire` et `PrestataireContour`. Export : prestataires.csv, revisions.csv.

34. Tuiles des problèmes signalés (Paramètres › Problèmes signalés, `SignalementsTests`) : par catégorie (nom exact, majuscules
    ignorées), une liste de `CauseSignalement` (libellé, pictogramme = clé de `Icones.PictosCause`, `PrecisionObligatoire`) dans
    `Reglages.CausesSignalement` (parametres.json) ; catégorie sans réglage : `ServiceSignalements.CausesParDefaut`.
    `ServiceReglages.ChangerTuilesSignalement` (action Paramètres, `VerifierTuiles` : 1 à 12 tuiles, libellé obligatoire de 60 car.
    au plus, sans doublon) et `RetablirTuilesSignalement`. `ServiceSignalements.Tuiles(categorie)` alimente la fenêtre ; `SignalerAsync`
    refuse une précision vide pour une tuile « précision obligatoire » (« Précisez le problème : « Autre » demande une précision. »),
    et la fenêtre l'annonce au-dessus du champ. Carte : catégorie, puis `ListeTuiles`, un seul contrôle dessiné pour toutes les
    lignes (des contrôles par ligne ralentissaient l'ouverture de Paramètres) : monter / descendre / modifier / supprimer et case
    « Précision obligatoire » retrouvés par calcul, clavier (flèches, Ctrl + flèches, Espace, Entrée, Suppr) ; sa hauteur est posée
    (un TableLayoutPanel en Dock Fill garde la hauteur du contrôle) et la carte suit.

35. Interventions prévues (migration 018, `EtapesRevisionsTests`) : on voit un problème, il faudra intervenir.
    `ServiceRevisions.PrevoirInterventionAsync(equipement, « à faire » 60 car. obligatoire, description 200 car.)`, action Révisions :
    une seule ouverte par équipement (`ux_intervention_prevue_ouverte` ; la prévoir de nouveau la modifie), refusée si réformé ou
    déjà engagé dans une révision. Elle met l'équipement dans « À prévoir » (`CarteRevision.Prevue`, badge « Prévue », juste après
    les signalements) ; s'il y a déjà une carte, elle s'y ajoute (échéance : le motif devient le titre ; signalement : « Prévu : … »
    en description). Elle suit l'équipement aux étapes suivantes (description sur la carte) ; la prochaine intervention enregistrée
    la ferme (`FermerPrevueAsync`) ; croix de la carte → `RetirerInterventionPrevueAsync` (annulee = 1, rien n'est supprimé).
    Journal `RevisionEtape`. App : bouton « Ajouter une intervention » de Révisions, onglets « À prévoir » (choix de l'équipement,
    « À faire », description) / « Déjà faite » (panneau d'intervention). Export : interventions_prevues.csv.

Réglages : `Core/Modeles/Reglages` (instance unique injectée dans les services), modifiés via `ServiceReglages`
(validation + enregistrement par `IStockageReglages` → `parametres.json`). Avec `--base`, `parametres.json` est lu et écrit
à côté de la base d'essai : les essais ne touchent jamais les réglages réels. Préfixe des étiquettes : `CodeScanne.Nettoyer(brut, prefixe)`.
Sauvegardes : `IServiceSauvegarde` (dossier modifiable, dernière sauvegarde, `RestaurerAsync` qui vérifie le fichier,
copie les données actuelles en `materiel_…_avant-restauration.db`, puis réapplique les migrations).
Étiquettes QR : section retirée de l'écran Paramètres à la demande du demandeur (impression non prévue pour l'instant).

Écran Sorties : les sorties clôturées sont paginées (`ServiceSortie.ListerClotureesAsync(page)` → `PageSorties`, `SortiesParPage` = 12,
la plus récemment clôturée d'abord ; `ISortieRepository.ListerClotureesAsync(decalage, nombre)` ne lit que les sorties de la page,
`CompterClotureesAsync` le total) : barre « Sorties 13 à 24 sur 143 · page 2 / 12 » et « ‹ Précédentes / Suivantes › » sous le tableau.

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
  depuis 007 : seance_equipement (blocs planifiés à une séance TIV, ON DELETE CASCADE), controle.nature et controle.intervenant ;
  depuis 008 : moniteur.genre (CHECK Moniteur/Classe) et moniteur.abrege, classes Niveau 1 à 4 ;
  depuis 009 : manuel (fichier, nb_pages, sommaire et entretien en texte, une ligne par entrée ; PDF dans `Manuels\` à côté
  de la base, non copiés par les sauvegardes de la base) ; 010 : codes des médailles (DET-007 → A07, GIL-012 → S12, journal
  corrigé ; un code déjà pris reste inchangé) ; 011 : controle.composant (vide = équipement complet) ; 012 : composant (equipement_id + role, numero_serie, fabrication,
  mise_en_service, origine CHECK Neuve/Recuperee) ; 013 : signalement (cause, gravite CHECK ASurveiller/HorsService, precision,
  signale_le, mis_hors_service 0/1, traite_le ; sortie_id ON DELETE SET NULL ; index partiel `ux_signalement_ouvert`) ;
  014 : adherent (nom, prenom, niveau, licence unique si renseignée `ux_adherent_licence`, telephone, actif), type_sortie.nominatif
  (Mer et Lac à 1), sortie.nominatif (0 pour les sorties existantes), sortie_equipement.adherent_id, journal.adherent_id.
  015 : equipement.nitrox et adherent.nitrox (0/1).
  016 : prestataire (nom unique NOCASE `ux_prestataire_nom`, type CHECK Magasin/Organisme, actif) et revision (étape CHECK
  Interne/AEnvoyer/ChezPrestataire/Fait, une seule hors Fait par équipement `ux_revision_en_cours`, prestataire_id, motif,
  note_envoi, date_envoi, date_retour_prevue, intervenant_interne, seance_tiv, atelier_id, controle_id, date_fait, archive,
  date_archive) ; reprise : séance TIV à venir → Interne, atelier en cours → ChezPrestataire (prestataires repris des noms),
  contrôles des 30 derniers jours → Fait.
  017 : sortie.un_par_categorie (0/1, 1 par défaut, sorties existantes comprises).
  018 : intervention_prevue (equipement_id, motif, description, prevue_le, close_le, annulee 0/1 ; une ouverte par équipement
  `ux_intervention_prevue_ouverte`).
- Horodatages en texte ISO 8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ` ; dates seules `yyyy-MM-dd`. Énumérations stockées par leur nom (CHECK en SQL).
- **Piège Dapper** : en paramètre, Dapper ignore les TypeHandler pour les enums et pour DateTime. Toujours passer
  `enum.ToString()` et `FormatIso.Ecrire(date)` dans les objets de paramètres (voir `Parametres(...)` dans les dépôts).
  En lecture, les handlers et la conversion texte → enum fonctionnent. Les tests Data vérifient le format brut stocké.
- Nouvelle migration : ajouter `Migrations/019_xxx.sql`, sans jamais modifier un script déjà livré
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
