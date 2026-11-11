using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Inventaire : tuiles de catégories, recherche (ou scan), tableau du matériel.</summary>
public sealed class EcranInventaire : EcranBase
{
    private readonly ServiceInventaire _inventaire;
    private readonly ServiceReglages _reglages;
    private readonly ServiceCompteur _compteur;
    private IReadOnlyDictionary<long, UtilisationInventaire> _utilisations = new Dictionary<long, UtilisationInventaire>();
    private readonly BarreRecherche _recherche = new("Rechercher un code, un équipement, une sortie…", "ou scannez");
    private readonly GrilleAdaptative _categories = new() { LargeurMin = 176, HauteurElement = 76, Ecart = 12, Margin = new Padding(0, 0, 0, 20) };
    private readonly List<Tuile> _tuiles = [];
    private readonly DataGridView _grille = new();
    private readonly System.Windows.Forms.Timer _attenteRecherche = new() { Interval = 300 };
    private long? _categorie;

    private readonly GardeGestion _garde;

    public EcranInventaire(ServiceInventaire inventaire, ServiceReglages reglages, ServiceCompteur compteur, GardeGestion garde)
    {
        _garde = garde;
        _inventaire = inventaire;
        _compteur = compteur;
        _reglages = reglages;

        var ajouter = new BoutonArrondi("Ajouter un équipement", StyleBouton.Principal, Icones.Plus);
        _garde.Lier(ajouter, ActionProtegee.Materiel);
        ajouter.Click += (_, _) => Executer(AjouterEquipementAsync);
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire, Icones.Telecharger);
        exporter.Click += (_, _) => Executer(ExporterAsync);

        _recherche.Margin = new Padding(0, 0, 0, 20);
        _recherche.Champ.TextChanged += (_, _) =>
        {
            _attenteRecherche.Stop();
            _attenteRecherche.Start();
        };

        // Le pistolet tape le code puis Entrée : on nettoie le code ("asp:bloc-031" → "BLOC-031") et on cherche tout de suite.
        _recherche.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var code = Saisie.CodeScanne.Nettoyer(_recherche.Champ.Text, _reglages.Actuels.PrefixeEtiquette);
                if (code.Length > 0)
                {
                    _recherche.Champ.Text = code;
                    _recherche.Champ.SelectAll();
                }
            }
        };
        _attenteRecherche.Tick += (_, _) =>
        {
            _attenteRecherche.Stop();
            Executer(RechargerAsync);
        };

        Theme.AppliquerGrille(_grille, hauteurLigne: 60);
        _grille.AccessibleName = "Matériel du club";
        _grille.ScrollBars = ScrollBars.Both; // Les colonnes des détendeurs défilent en largeur.
        DefinirColonnes(ColonnesGenerales);
        _grille.CellFormatting += ColorerCellule;
        _grille.CellPainting += PeindreCellule;

        // Modifier : bouton dessiné dans la dernière colonne, double-clic ou Entrée sur une ligne.
        _grille.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && Colonne(e.ColumnIndex) == "action" && _grille.Rows[e.RowIndex].Tag is LigneInventaire l)
            {
                Executer(() => ModifierEquipementAsync(l));
            }
        };
        _grille.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && Colonne(e.ColumnIndex) != "action" && _grille.Rows[e.RowIndex].Tag is LigneInventaire l)
            {
                Executer(() => ModifierEquipementAsync(l));
            }
        };
        _grille.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _grille.CurrentRow?.Tag is LigneInventaire l)
            {
                e.SuppressKeyPress = true;
                Executer(() => ModifierEquipementAsync(l));
            }
        };
        _grille.CellMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && SurLeNom(e.RowIndex, e.ColumnIndex, e.Location) && _grille.Rows[e.RowIndex].Tag is LigneInventaire l)
            {
                Executer(() => ModifierEquipementAsync(l));
            }
        };
        _grille.CellMouseMove += (_, e) =>
            _grille.Cursor = e.RowIndex >= 0 && (Colonne(e.ColumnIndex) == "action" || SurLeNom(e.RowIndex, e.ColumnIndex, e.Location)) ? Cursors.Hand : Cursors.Default;

        var carteGrille = new Carte { Padding = new Padding(8, 6, 8, 8), Rayon = 22 };
        _grille.Dock = DockStyle.Fill;
        carteGrille.Controls.Add(_grille);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Tout le matériel du club, son état et sa prochaine révision.")), ajouter, exporter), SizeType.AutoSize, 0),
            (_categories, SizeType.Absolute, 76 + 20),
            (_recherche, SizeType.Absolute, 54 + 20),
            (carteGrille, SizeType.Percent, 100)));
    }

    public override string Titre => "Inventaire";

    public override EntreeMenu Menu => EntreeMenu.Inventaire;

    protected override async Task ChargerAsync(object? parametre)
    {
        var categories = await _inventaire.ListerCategoriesAsync();
        var tout = await _inventaire.ListerAsync(null, null);
        _utilisations = await _compteur.ListerUtilisationsAsync(tout);
        if (IsDisposed)
        {
            return;
        }

        _categories.SuspendLayout();
        foreach (var tuile in _tuiles)
        {
            _categories.Controls.Remove(tuile);
            tuile.Dispose();
        }

        _tuiles.Clear();
        _nomsCategories.Clear();
        foreach (var c in categories)
        {
            _nomsCategories[c.Id] = c.Nom;
        }

        AjouterTuile("Tout", Icones.Tout, null, tout.Count);
        foreach (var c in categories)
        {
            AjouterTuile(c.Nom, Icones.PourCategorie(c.Nom), c.Id, tout.Count(l => l.CategorieId == c.Id));
        }

        _categories.ResumeLayout(true);
        Afficher(tout);
        _recherche.Champ.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _attenteRecherche.Dispose();
            foreach (var police in _policesBarrees.Values)
            {
                police.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>Ajout d'un équipement ; la catégorie filtrée est proposée par défaut, puis on le montre dans la liste.</summary>
    private async Task AjouterEquipementAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Materiel, "Ajouter un équipement"))
        {
            return;
        }

        var code =await FormulaireEquipement.OuvrirAsync(FindForm(), _inventaire, categorieId: _categorie);
        if (code is null || IsDisposed)
        {
            return;
        }

        await ChargerAsync(null);
        _recherche.Champ.Text = code; // Recherche sur le nouveau code : l'équipement apparaît seul.
    }

    private void AjouterTuile(string texte, string icone, long? categorieId, int nombre)
    {
        var tuile = new Tuile(texte, icone, DispositionTuile.Horizontale)
        {
            Compte = nombre,
            Selectionne = categorieId == _categorie,
            Tag = categorieId,
            AccessibleName = $"{texte} : {nombre}",
        };
        tuile.Click += (_, _) =>
        {
            _categorie = categorieId;
            foreach (var t in _tuiles)
            {
                t.Selectionne = (long?)t.Tag == _categorie;
            }

            Executer(RechargerAsync);
        };
        _tuiles.Add(tuile);
        _categories.Controls.Add(tuile);
    }

    private async Task RechargerAsync()
    {
        var lignes = await _inventaire.ListerAsync(_recherche.Champ.Text, _categorie);
        _utilisations = await _compteur.ListerUtilisationsAsync(lignes);
        if (!IsDisposed)
        {
            Afficher(lignes);
        }
    }

    /// <summary>Exporte les lignes affichées (catégorie et recherche en cours).</summary>
    private async Task ExporterAsync()
    {
        if (_lignes.Count == 0)
        {
            Dialogues.Information(FindForm(), "Rien à exporter : la liste est vide.");
            return;
        }

        if (await Fichiers.EnregistrerCsvAsync(FindForm(), "Exporter l'inventaire", _inventaire.NomFichierExport(), _inventaire.ExporterCsv(_lignes)))
        {
            Dialogues.Information(FindForm(), $"Inventaire exporté : {Libelles.Pluriel(_lignes.Count, "équipement")}.");
        }
    }

    /// <summary>Fiche de l'équipement (action protégée), puis la liste est rechargée.</summary>
    private async Task ModifierEquipementAsync(LigneInventaire ligne)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Materiel, "Modifier un équipement"))
        {
            return;
        }

        if (await _inventaire.ObtenirAsync(ligne.EquipementId) is not { } equipement)
        {
            Dialogues.Erreur(FindForm(), "Cet équipement n'existe plus.");
            return;
        }

        if (await FormulaireEquipement.ModifierAsync(FindForm(), _inventaire, equipement, ligne.DerniereRevision) && !IsDisposed)
        {
            await RechargerAsync();
        }
    }

    private IReadOnlyList<LigneInventaire> _lignes = [];

    // ---------------------------------------------------------------- Colonnes selon la catégorie

    /// <summary>Une colonne du tableau : nom interne, titre, largeur (poids, ou pixels logiques en largeur fixe), valeur affichée.</summary>
    private sealed record ColonneInventaire(string Nom, string Titre, int Largeur, Func<LigneInventaire, string> Valeur);

    private static string Ou(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;

    private static string Date(DateOnly? d) => d is { } v ? Libelles.Date(v) : "—";

    private static readonly ColonneInventaire Action = new("action", "", 100, _ => "");

    private static readonly ColonneInventaire Statut = new("statut", "Statut", 110, l => Libelles.De(l.Statut));

    /// <summary>Compteur d'utilisation (maquette) : « 64 / 80 utilisations », barre et origine du seuil dessinées.</summary>
    private ColonneInventaire Utilisation => new("utilisation", "Utilisation", 175, TexteUtilisation);

    private string TexteUtilisation(LigneInventaire l) => _utilisations.TryGetValue(l.EquipementId, out var u) ? u.Texte : "—";

    /// <summary>« Tout » et les catégories sans fiche particulière.</summary>
    private ColonneInventaire[] ColonnesGenerales =>
    [
        new("equipement", "Équipement", 190, l => l.Libelle),
        new("code", "Code", 90, l => l.Code),
        new("marque", "Marque", 90, l => Ou(l.Marque)),
        new("taille", "Taille", 60, l => Ou(l.Taille)),
        Statut,
        Utilisation,
        new("sortie", "Sortie en cours", 130, l => Ou(l.SortieEnCoursIntitule)),
        new("revision", "Prochaine révision", 130, l => Date(ProchaineEcheance(l))),
        Action,
    ];

    /// <summary>Stabs : comme le registre du club (marque, modèle, taille, n° de série, identification).</summary>
    private ColonneInventaire[] ColonnesGilets =>
    [
        new("code", "Code", 70, l => l.Code),
        new("marque", "Marque", 90, l => Ou(l.Marque)),
        new("modele", "Modèle", 80, l => Ou(l.Modele)),
        new("taille", "Taille", 55, l => Ou(l.Taille)),
        new("serie", "N° série", 100, l => Ou(l.NumeroSerie)),
        Statut,
        Utilisation,
        new("identification", "Identification", 230, l => _inventaire.Identification(l.Categorie, l.Code, l.Taille)),
        Action,
    ];

    /// <summary>Détendeurs : toutes les colonnes du registre (largeurs fixes, la grille défile en largeur).</summary>
    private ColonneInventaire[] ColonnesDetendeurs =>
    [
        new("code", "Code", 80, l => l.Code),
        new("marque", "Marque", 100, l => Ou(l.Marque)),
        new("modele", "Premier étage", 110, l => Ou(l.Modele)),
        new("montage", "Étrier / DIN", 100, l => l.Montage is { } m ? Libelles.De(m) : "—"),
        new("serie", "N° premier étage", 130, l => Ou(l.NumeroSerie)),
        new("marque2", "Marque 2e étage", 120, l => Ou(l.MarqueDeuxiemeEtage)),
        new("modele2", "Deuxième étage", 120, l => Ou(l.ModeleDeuxiemeEtage)),
        new("serie2", "N° 2e étage", 130, l => Ou(l.NumeroDeuxiemeEtage)),
        new("ds", "D.S", 60, l => l.DirectSystem is { } ds ? (ds ? "Oui" : "Non") : "—"),
        new("mano", "Mano", 100, l => Ou(l.Manometre)),
        new("octopus", "Octopus", 90, l => string.IsNullOrWhiteSpace(l.Octopus) ? "Non" : l.Octopus),
        new("serieOctopus", "N° octopus", 120, l => Ou(l.NumeroOctopus)),
        new("statut", "Statut", 120, l => Libelles.De(l.Statut)),
        new("utilisation", "Utilisation", 230, l => TexteUtilisation(l)),
        new("derniere", "Dernière révision", 120, l => Date(l.DerniereRevision)),
        new("revision", "À réviser avant", 150, l => Date(l.DateProchaineRevision)),
        new("identification", "Identification", 300, l => _inventaire.Identification(l.Categorie, l.Code, l.Taille)),
        new("action", "", 110, _ => ""),
    ];

    private ColonneInventaire[] _colonnes = [];
    private readonly Dictionary<Font, Font> _policesBarrees = [];
    private readonly Dictionary<long, string> _nomsCategories = [];

    private string? Colonne(int index) => index >= 0 && index < _grille.Columns.Count ? _grille.Columns[index].Name : null;

    /// <summary>Remplace les colonnes ; largeur fixe et défilement horizontal pour les détendeurs, sinon pleine largeur.</summary>
    private void DefinirColonnes(ColonneInventaire[] colonnes)
    {
        if (_colonnes.Select(c => c.Nom).SequenceEqual(colonnes.Select(c => c.Nom)))
        {
            _colonnes = colonnes;
            return;
        }

        _colonnes = colonnes;
        var largeursFixes = colonnes.Length > 10;
        _grille.SuspendLayout();
        _grille.Rows.Clear();
        _grille.Columns.Clear();
        _grille.AutoSizeColumnsMode = largeursFixes ? DataGridViewAutoSizeColumnsMode.None : DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var c in colonnes)
        {
            Tableaux.AjouterColonne(_grille, c.Nom, c.Titre, c.Largeur);
            var colonne = _grille.Columns[c.Nom]!;
            colonne.HeaderCell.Style.Padding = colonne.DefaultCellStyle.Padding = new Padding((int)Dessin.Echelle(this, 8), 0, 4, 0);
            if (largeursFixes)
            {
                colonne.Width = (int)Dessin.Echelle(this, c.Largeur);
            }
        }

        if (largeursFixes)
        {
            _grille.Columns[0].Frozen = true; // Le code reste visible quand on fait défiler.
        }

        _grille.ResumeLayout();
    }

    private void Afficher(IReadOnlyList<LigneInventaire> lignes)
    {
        _lignes = lignes;
        var categorie = _categorie is { } id && _nomsCategories.TryGetValue(id, out var nom) ? nom : null;
        DefinirColonnes(ServiceInventaire.EstDetendeur(categorie) ? ColonnesDetendeurs
            : ServiceInventaire.EstGilet(categorie) ? ColonnesGilets
            : ColonnesGenerales);
        _grille.SuspendLayout();
        _grille.Rows.Clear();
        foreach (var l in lignes)
        {
            var i = _grille.Rows.Add(_colonnes.Select(c => (object)c.Valeur(l)).ToArray());
            _grille.Rows[i].Tag = l;
            if (_grille.Columns.Contains("utilisation") && _utilisations.TryGetValue(l.EquipementId, out var u) && u.Seuil is not null)
            {
                _grille.Rows[i].Cells["utilisation"].ToolTipText = u.Origine switch
                {
                    OrigineSeuil.Modele => "Seuil propre à ce modèle (Paramètres › Compteur d'utilisation)",
                    OrigineSeuil.Equipement => "Seuil propre à cet équipement (Paramètres › Compteur d'utilisation)",
                    _ => $"Seuil des {l.Categorie.ToLower(Libelles.Culture)}",
                };
            }
        }

        _grille.ResumeLayout();
    }

    /// <summary>Ligne sous le nom : « Scubapro MK25 · Détendeurs » (catégorie seule si la fiche est vide).</summary>
    private static string SousTitre(LigneInventaire l)
    {
        var fiche = string.Join(" ", new[] { l.Marque, l.Modele }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
        return fiche.Length > 0 ? $"{fiche} · {l.Categorie}" : l.Categorie;
    }

    /// <summary>Le nom souligné ouvre la fiche, comme le bouton « Modifier ».</summary>
    private bool SurLeNom(int ligne, int colonne, Point position)
    {
        if (Colonne(colonne) != "equipement" || ligne < 0 || _grille.Rows[ligne].Tag is not LigneInventaire l)
        {
            return false;
        }

        var cellule = _grille.GetCellDisplayRectangle(colonne, ligne, false);
        using var g = _grille.CreateGraphics();
        var zone = Tableaux.ZoneTitre(g, cellule, _grille.Columns[colonne].DefaultCellStyle.Padding, l.Libelle);
        zone.Inflate(0, 3);
        return zone.Contains(cellule.Left + position.X, cellule.Top + position.Y);
    }

    /// <summary>Révision (ou TIV) ou requalification : la plus proche.</summary>
    private static DateOnly? ProchaineEcheance(LigneInventaire l) =>
        new[] { l.DateProchaineRevision, l.DateRequalification }.Where(d => d is not null).Min();

    private void ColorerCellule(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneInventaire l || e.CellStyle is null)
        {
            return;
        }

        if (Colonne(e.ColumnIndex) == "code")
        {
            e.CellStyle.Font = Theme.PoliceCode(10f);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.Accent;
        }

        // Matériel réformé : barré et grisé, comme dans les registres du club.
        if (l.Etat == EtatEquipement.Reforme)
        {
            var police = e.CellStyle.Font ?? _grille.DefaultCellStyle.Font ?? Theme.Police();
            if (!_policesBarrees.TryGetValue(police, out var barree))
            {
                barree = new Font(police, police.Style | FontStyle.Strikeout); // Une par police, libérées avec l'écran.
                _policesBarrees[police] = barree;
            }

            e.CellStyle.Font = barree;
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.TexteSecondaire;
        }
    }

    private void PeindreCellule(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneInventaire l)
        {
            return;
        }

        var colonne = Colonne(e.ColumnIndex);
        if (colonne == "equipement")
        {
            Tableaux.PeindreIconeDeuxLignes(e, Icones.PourCategorie(l.Categorie), l.Libelle, SousTitre(l), barre: l.Etat == EtatEquipement.Reforme);
        }
        else if (colonne == "utilisation" && _utilisations.TryGetValue(l.EquipementId, out var u))
        {
            var (couleur, texte) = u.Etat switch
            {
                EtatCompteur.Atteint => (Theme.Erreur, Theme.Erreur),
                EtatCompteur.Proche => (Theme.Avertissement, Theme.Avertissement),
                _ => (Theme.Accent, u.Seuil is null ? Theme.TexteSecondaire : Theme.Texte),
            };
            var badge = u.Seuil is null ? null : u.Origine switch
            {
                OrigineSeuil.Modele => "SEUIL MODÈLE",
                OrigineSeuil.Equipement => "SEUIL ÉQUIPEMENT",
                _ => null,
            };
            Tableaux.PeindreUtilisation(e, u.Texte, u.Part, couleur, texte, badge, barreVisible: u.Seuil is not null);
        }
        else if (colonne == "action")
        {
            Tableaux.PeindreBouton(e, "Modifier");
        }
        else if (colonne == "statut")
        {
            var (couleur, texte) = l.Statut switch
            {
                StatutInventaire.Disponible => (Theme.Retour, Theme.Retour),
                StatutInventaire.Dehors => (Theme.Depart, Theme.DepartClair),
                StatutInventaire.Manquant => (Theme.Erreur, Theme.Erreur),
                StatutInventaire.HorsService => (Theme.Avertissement, Theme.Avertissement),
                _ => (Theme.TexteSecondaire, Theme.TexteSecondaireClair),
            };
            Tableaux.PeindrePastille(e, Libelles.De(l.Statut), couleur, texte);
        }
        else if (colonne == "revision" && l.Etat != EtatEquipement.Reforme && ProchaineEcheance(l) is { } date)
        {
            // Badge : jaune si la révision approche (réglage « Prévenir avant une révision »), rouge si dépassée.
            switch (_inventaire.EtatDeRevision(date))
            {
                case EtatRevision.Proche:
                    Tableaux.PeindrePastille(e, Libelles.Date(date), Theme.Avertissement);
                    break;
                case EtatRevision.Depassee:
                    Tableaux.PeindrePastille(e, $"{Libelles.Date(date)} · dépassée", Theme.Erreur);
                    break;
            }
        }
    }
}
