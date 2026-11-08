using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Inventaire : recherche, filtre par catégorie, tableau du matériel.</summary>
public sealed class EcranInventaire : EcranBase
{
    private readonly ServiceInventaire _inventaire;
    private readonly IHorloge _horloge;
    private readonly ChampSaisie _recherche = new() { TexteIndicatif = "Rechercher un code, un nom, une taille…" };
    private readonly FlowLayoutPanel _filtres;
    private readonly DataGridView _grille = new();
    private readonly Label _compte = Ui.SousTitre("");
    private readonly System.Windows.Forms.Timer _attenteRecherche = new() { Interval = 300 };
    private long? _categorie;

    public EcranInventaire(ServiceInventaire inventaire, IHorloge horloge)
    {
        _inventaire = inventaire;
        _horloge = horloge;

        var ajouter = new BoutonArrondi("+  Ajouter un équipement", StyleBouton.Principal);
        ajouter.Click += (_, _) => Dialogues.Information(FindForm(), "L'ajout d'équipement arrive dans une prochaine version.");
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire);
        exporter.Click += (_, _) => Dialogues.Information(FindForm(), "L'export CSV arrive dans une prochaine version.");

        _recherche.Width = 380;
        _recherche.Margin = new Padding(0, 0, 16, 0);
        _recherche.Champ.TextChanged += (_, _) =>
        {
            _attenteRecherche.Stop();
            _attenteRecherche.Start();
        };
        _attenteRecherche.Tick += (_, _) =>
        {
            _attenteRecherche.Stop();
            Executer(RechargerAsync);
        };

        // Filtres par catégorie : "puces" qui passent à la ligne ; la hauteur de la rangée suit le contenu.
        _filtres = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Fond, Margin = new Padding(0, 0, 0, 8) };
        var barreRecherche = Ui.Rangee(true, _recherche);
        barreRecherche.Margin = new Padding(0, 0, 0, 12);

        Theme.AppliquerGrille(_grille);
        _grille.Columns.Add("code", "Code");
        _grille.Columns.Add("equipement", "Équipement");
        _grille.Columns.Add("taille", "Taille");
        _grille.Columns.Add("statut", "Statut");
        _grille.Columns.Add("sortie", "Sortie en cours");
        _grille.Columns.Add("revision", "Prochaine révision");
        _grille.Columns["equipement"]!.FillWeight = 180;
        _grille.Columns["taille"]!.FillWeight = 60;
        _grille.Columns["revision"]!.FillWeight = 130;
        _grille.CellFormatting += ColorerCellule;

        var carteGrille = new Carte { Padding = new Padding(12), Margin = Padding.Empty };
        _grille.Dock = DockStyle.Fill;
        carteGrille.Controls.Add(_grille);

        var colonne = Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), _compte), ajouter, exporter), SizeType.AutoSize, 0),
            (barreRecherche, SizeType.AutoSize, 0),
            (_filtres, SizeType.Absolute, 60),
            (carteGrille, SizeType.Percent, 100));
        _filtres.Layout += (_, _) =>
        {
            var hauteur = _filtres.GetPreferredSize(new Size(_filtres.Width, 0)).Height + _filtres.Margin.Vertical;
            if (Math.Abs(colonne.RowStyles[2].Height - hauteur) > 1)
            {
                colonne.RowStyles[2].Height = hauteur;
            }
        };
        Controls.Add(colonne);
    }

    public override string Titre => "Inventaire";

    public override EntreeMenu Menu => EntreeMenu.Inventaire;

    protected override async Task ChargerAsync(object? parametre)
    {
        var categories = await _inventaire.ListerCategoriesAsync();
        _filtres.Controls.Clear();
        AjouterFiltre("Toutes", null);
        foreach (var c in categories)
        {
            AjouterFiltre(c.Nom, c.Id);
        }

        await RechargerAsync();
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

    private void AjouterFiltre(string texte, long? categorieId)
    {
        var bouton = new BoutonArrondi(texte, categorieId == _categorie ? StyleBouton.Principal : StyleBouton.Secondaire)
        {
            Margin = new Padding(0, 0, 8, 8),
            Padding = new Padding(14, 0, 14, 0),
            Tag = categorieId,
        };
        bouton.Click += (_, _) =>
        {
            _categorie = categorieId;
            foreach (Control c in _filtres.Controls)
            {
                if (c is BoutonArrondi b)
                {
                    b.Style = (long?)b.Tag == _categorie ? StyleBouton.Principal : StyleBouton.Secondaire;
                }
            }

            Executer(RechargerAsync);
        };
        _filtres.Controls.Add(bouton);
    }

    private async Task RechargerAsync()
    {
        var lignes = await _inventaire.ListerAsync(_recherche.Valeur, _categorie);
        if (IsDisposed)
        {
            return;
        }

        _grille.SuspendLayout();
        _grille.Rows.Clear();
        foreach (var l in lignes)
        {
            var i = _grille.Rows.Add(
                l.Code,
                $"{l.Libelle} ({l.Categorie})",
                l.Taille ?? "—",
                Libelles.De(l.Statut),
                l.SortieEnCoursIntitule ?? "—",
                l.DateProchaineRevision is { } d ? Libelles.Date(d) : "—");
            _grille.Rows[i].Tag = l;
        }

        _grille.ResumeLayout();
        _compte.Text = Libelles.Pluriel(lignes.Count, "équipement");
    }

    private void ColorerCellule(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneInventaire l || e.CellStyle is null)
        {
            return;
        }

        if (e.ColumnIndex == _grille.Columns["statut"]!.Index)
        {
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = l.Statut switch
            {
                StatutInventaire.Disponible => Theme.Retour,
                StatutInventaire.Dehors => Theme.Depart,
                StatutInventaire.Manquant => Theme.Erreur,
                _ => Theme.TexteSecondaire,
            };
        }
        else if (e.ColumnIndex == _grille.Columns["revision"]!.Index && l.DateProchaineRevision is { } d && d < _horloge.AujourdHui)
        {
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.Avertissement;
        }
        else if (e.ColumnIndex == _grille.Columns["code"]!.Index)
        {
            e.CellStyle.Font = Theme.PoliceTitre(10.5f);
        }
    }
}
