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
  (échéancier, contrôles, atelier, séances TIV), `ServiceReglages`, `ServiceHistorique`. Toutes les règles métier sont ici.
- **Core/Resultats** : `ResultatScan` (Niveau Succes/Avertissement/Erreur, Message, Equipement, `ActionProposee`, Code, AutreSortieId),
  `ResultatOperation`, `ApercuCloture`. Les cas métier ne lèvent pas d'exception : ils retournent un résultat.
- **Core/Saisie** : `CodeScanne.Nettoyer` (espaces, préfixe `ASP:`, majuscules) et `AntiRebond` — logique pure, testée.
- **Core/Abstractions** : interfaces des dépôts, `IHorloge` (heure injectable pour les tests), `IServiceSauvegarde`.
- **Data/Migrations** : `NNN_nom.sql` embarqués (LogicalName `Migrations.NNN_nom.sql`), appliqués au démarrage par
  `GestionnaireMigrations` selon `schema_version`, chacun dans sa transaction. Active le mode WAL.
- **Data/Connexion** : `FabriqueConnexion` (une connexion par appel, `Foreign Keys=True`), `ConfigurationDapper`, `FormatIso`.
- **App** : `Program.cs` → `Demarrage` (DI, sauvegarde, migrations, démo en DEBUG) → `MainForm` (menu latéral + zone de contenu).
  `INavigation.AllerA<TEcran>(parametre)` crée l'écran via `ActivatorUtilities` (non retenu par le conteneur), le met à l'échelle
  de l'écran et dispose le précédent. Écrans dans `Ecrans/` (héritent d'`EcranBase` ; Départ/Retour héritent d'`EcranScanBase`
  qui sérialise les scans et fournit fil d'Ariane + carte d'en-tête via `Composer(corps)`).
- **Maquette** : https://claude.ai/artifact/8op5sAWFbf4mRQCxzd37aa (Sorties, Nouvelle sortie, Départ, Retour, Inventaire,
  Moniteurs, Révisions, Paramètres). Polices Sora/Manrope remplacées par Segoe UI Variable ; pas de fond dégradé ni de bulles animées.
- **Briques d'interface** (`Controles/`) : `Carte`, `ControleDessine` (base des contrôles peints), `AnneauProgression`,
  `IndicateurEtapes`, `PastilleDate`, `Pastille`, `Picto`, `GrilleAdaptative` (colonnes auto, ajuste sa ligne de TableLayoutPanel),
  `Tuile`, `FilAriane`, `BarreRecherche`, `ChampSaisie`, `ChampDate` (remplace le DateTimePicker, blanc et en anglais),
  `ListeDeroulante`, `ListeMateriel` (styles Depart / ARendre / Rendu), `ScanInput` + `BandeauMessage`, `BandeauAccueil`,
  `Logo`, `CarteScanner`, `Tableaux` (pastilles et pictogrammes dans les DataGridView), `FiltreCategories` (puces « Tout 12 /
  Blocs 3 »), `FenetreFormulaire` (fenêtre modale sombre ; la validation passe par un service et ne ferme qu'en cas de succès),
  `Interrupteur`, `SelecteurNombre`, `CarteSection`, `LigneReglage`.
- **Pictogrammes** : `Apparence/Icones.cs`, chemins SVG 24 × 24 de la maquette lus par `CheminSvg` et tracés en GDI+.

## Règles métier (chacune a au moins un test dans Core.Tests)

1. Un équipement n'est « Dehors » que dans une seule sortie (aussi garanti par l'index partiel `ux_sortie_equipement_dehors`).
2. HorsService / Reforme ne part pas.
3. Double scan au départ dans la même sortie → ignoré, « déjà scanné ».
4. Code inconnu au départ → `ActionProposee.CreerEquipement`.
5. Retour d'un équipement absent de la sortie → avertir sans rien modifier ; s'il est dehors ailleurs →
   `ActionProposee.RentrerDansAutreSortie` (l'écran rescanne alors dans `AutreSortieId`).
6. Retrait possible seulement en préparation (`StatutSortie.EnPreparation`).
7. Clôture : Dehors → Manquant, confirmation avec le nombre (`ApercuClotureAsync`) s'il reste du matériel dehors
   (`ApercuCloture.ConfirmationNecessaire`, réglage `ConfirmerCloture`).
8. Un Manquant scanné plus tard au retour (dans sa sortie ou depuis une autre) → Rendu + journal `ManquantRetrouve`.
9. Révision (ou TIV) ou requalification dépassée → part quand même, avec avertissement (refusée si le réglage
   `DepartSiRevisionDepassee` est coupé). `Equipement.RevisionDepassee` / `EcheanceDepassee` tiennent compte des deux dates.
10. Sortie en cours avec du matériel dehors depuis `JoursAvantAlerteRetour` jours → en retard (`ServiceSortie.EstEnRetard`, carte orange).
11. Révision dans les `JoursAvantRevision` jours (15/30/60) → badge jaune ; dépassée → badge rouge (`ServiceInventaire.EtatDeRevision`).
12. Contrôles (`ServiceRevisions.EnregistrerControleAsync`) : TIV et requalification pour les blocs (catégorie « Blocs… »),
    révision pour le reste. Conforme → TIV/révision + 12 mois, requalification + 6 ans (et TIV + 12 mois) ; à surveiller → + 6 mois ;
    refusé → HorsService (impossible si l'équipement est dehors). TIV : inspecteur habilité obligatoire. Pas de date future.
    Délais dans `Reglages` (`MoisEntreTiv`, `AnneesEntreRequalifications`, `MoisEntreRevisions`, `MoisSiASurveiller`).
13. Échéancier : en retard / ce mois-ci / sous `JoursAPlanifier` (90) jours / à jour (`ServiceRevisions.EtatDe`). Matériel réformé exclu.
14. Atelier : l'envoi passe l'équipement HorsService (refusé s'il est dehors, déjà en atelier, ou sans prestataire) ; le retour le remet EnService.
    Séance TIV : date à venir ; blocs à préparer = TIV due au plus tard 30 jours après la séance.

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
  equipement.date_requalification.
- Horodatages en texte ISO 8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ` ; dates seules `yyyy-MM-dd`. Énumérations stockées par leur nom (CHECK en SQL).
- **Piège Dapper** : en paramètre, Dapper ignore les TypeHandler pour les enums et pour DateTime. Toujours passer
  `enum.ToString()` et `FormatIso.Ecrire(date)` dans les objets de paramètres (voir `Parametres(...)` dans les dépôts).
  En lecture, les handlers et la conversion texte → enum fonctionnent. Les tests Data vérifient le format brut stocké.
- Nouvelle migration : ajouter `Migrations/002_xxx.sql`, sans jamais modifier un script déjà livré.
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
  Contrôles cliquables ≥ 44 px (`Theme.HauteurCliquable`), contraste ≥ 4.5:1.
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
- Messages à l'utilisateur : courts, en français simple.
- Chaque nouvelle règle métier → au moins un test dans Core.Tests ; chaque requête SQL nouvelle → couverte dans Data.Tests.
- Commits git petits et clairs, en français.

## Vérifier l'interface

L'application peut être pilotée sans intervention : lancer l'exe avec `--base <fichier temporaire>`, cliquer via UI Automation
(clic souris aux coordonnées de `BoundingRectangle`) et simuler le pistolet avec `SendKeys.SendWait("BLOC-031{ENTER}")`,
puis capturer la fenêtre. Après une erreur, consulter `erreurs.log`. Pour comparer à la maquette (1440 × 900 px CSS), donner
à la fenêtre 1800 × 1125 px à 125 % ; vérifier aussi la taille minimale 1280 × 800. Champs à trouver par nom + type Edit
(le premier champ texte de « Nouvelle sortie » est la date).
