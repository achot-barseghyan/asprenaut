using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Inventaire : tuiles de catégories, recherche (ou scan), tableau du matériel.</summary>
public sealed class EcranInventaire : EcranBase
{
    private readonly ServiceInventaire _inventaire;
    private readonly IHorloge _horloge;
    private readonly BarreRecherche _recherche = new("Rechercher un code, un équipement, une sortie…", "ou scannez");
    private readonly GrilleAdaptative _categories = new() { LargeurMin = 176, HauteurElement = 76, Ecart = 12, Margin = new Padding(0, 0, 0, 20) };
    private readonly List<Tuile> _tuiles = [];
    private readonly DataGridView _grille = new();
    private readonly System.Windows.Forms.Timer _attenteRecherche = new() { Interval = 300 };
    private long? _categorie;

    public EcranInventaire(ServiceInventaire inventaire, IHorloge horloge)
    {
        _inventaire = inventaire;
        _horloge = horloge;

        var ajouter = new BoutonArrondi("Ajouter un équipement", StyleBouton.Principal, Icones.Plus);
        ajouter.Click += (_, _) => Dialogues.Information(FindForm(), "L'ajout d'équipement arrive dans une prochaine version.");
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire, Icones.Telecharger);
        exporter.Click += (_, _) => Dialogues.Information(FindForm(), "L'export CSV arrive dans une prochaine version.");

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
                var code = Saisie.CodeScanne.Nettoyer(_recherche.Champ.Text);
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
        Tableaux.AjouterColonne(_grille, "equipement", "Équipement", 190);
        Tableaux.AjouterColonne(_grille, "code", "Code", 90);
        Tableaux.AjouterColonne(_grille, "taille", "Taille", 60);
        Tableaux.AjouterColonne(_grille, "statut", "Statut", 110);
        Tableaux.AjouterColonne(_grille, "sortie", "Sortie en cours", 140);
        Tableaux.AjouterColonne(_grille, "revision", "Prochaine révision", 110);
        _grille.CellFormatting += ColorerCellule;
        _grille.CellPainting += PeindreCellule;

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
        if (IsDisposed)
        {
            return;
        }

        _categories.SuspendLayout();
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
        }

        base.Dispose(disposing);
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
        if (!IsDisposed)
        {
            Afficher(lignes);
        }
    }

    private void Afficher(IReadOnlyList<LigneInventaire> lignes)
    {
        _grille.SuspendLayout();
        _grille.Rows.Clear();
        foreach (var l in lignes)
        {
            var i = _grille.Rows.Add(
                l.Libelle,
                l.Code,
                l.Taille ?? "—",
                Libelles.De(l.Statut),
                l.SortieEnCoursIntitule ?? "—",
                l.DateProchaineRevision is { } d ? Libelles.Date(d) : "—");
            _grille.Rows[i].Tag = l;
        }

        _grille.ResumeLayout();
    }

    private void ColorerCellule(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneInventaire l || e.CellStyle is null)
        {
            return;
        }

        if (e.ColumnIndex == _grille.Columns["code"]!.Index)
        {
            e.CellStyle.Font = Theme.PoliceCode(10f);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.Accent;
        }
        else if (e.ColumnIndex == _grille.Columns["revision"]!.Index && l.DateProchaineRevision is { } d && d < _horloge.AujourdHui)
        {
            e.CellStyle.Font = Theme.PoliceTitre(10.5f);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.Avertissement;
        }
    }

    private void PeindreCellule(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneInventaire l)
        {
            return;
        }

        if (e.ColumnIndex == _grille.Columns["equipement"]!.Index)
        {
            Tableaux.PeindreIconeTexte(e, Icones.PourCategorie(l.Categorie), l.Libelle);
        }
        else if (e.ColumnIndex == _grille.Columns["statut"]!.Index)
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
    }
}
