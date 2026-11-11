using System.Text;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Historique : tout ce qui s'est passé (départs, retours, sorties, révisions, matériel), filtré par type et par période,
/// regroupé par jour ; à droite, la vie d'un équipement scanné ou choisi dans le journal. Paramètre de navigation : un code.
/// </summary>
public sealed class EcranHistorique : EcranBase
{
    private readonly ServiceHistorique _historique;
    private readonly ListeDeroulante _periode = new() { Width = 236, Margin = new Padding(0, 0, 10, 0), AccessibleName = "Période" };
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = new Padding(0, 0, 0, 16), Unite = "événement" };
    private readonly JournalHistorique _journal = new() { Margin = new Padding(0, 0, 22, 0) };
    private readonly ChampSaisie _code = new() { TexteIndicatif = "Scannez ou tapez un code", Icone = Icones.Qr };
    private readonly Panel _fiche = new() { AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
    private IReadOnlyList<EvenementHistorique> _evenements = [];
    private bool _chargement;

    public EcranHistorique(ServiceHistorique historique)
    {
        _historique = historique;

        foreach (var p in Enum.GetValues<PeriodeHistorique>())
        {
            _periode.Items.Add(new Choix<PeriodeHistorique>(p, _historique.LibellePeriode(p)));
        }

        _periode.SelectedIndex = (int)PeriodeHistorique.TrenteJours;
        _periode.SelectedIndexChanged += (_, _) =>
        {
            if (!_chargement)
            {
                Executer(RechargerAsync);
            }
        };
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire, Icones.Telecharger);
        exporter.Click += (_, _) => Executer(ExporterAsync);

        var types = ServiceHistorique.TypesFiltrables.ToDictionary(ServiceHistorique.Libelle);
        _filtre.CouleurPoint = libelle => types.TryGetValue(libelle, out var t) ? FicheVie.Couleur(t) : null;
        _filtre.Changement += (_, _) =>
        {
            AfficherJournal();
            _code.Champ.Focus();
        };
        _journal.CodeChoisi += (_, code) =>
        {
            _code.Valeur = code;
            Executer(() => AfficherVieAsync(code));
        };

        // Le pistolet tape le code puis Entrée.
        _code.Champ.Font = Theme.PoliceCode(12f);
        _code.Champ.AccessibleName = "Code de l'équipement";
        _code.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Executer(() => AfficherVieAsync(_code.Valeur));
            }
        };

        // Carte « Vie d'un équipement » : titre, champ de scan (fixes) et fiche qui défile.
        Natif.ScrollbarsDuTheme(_fiche);
        var titreFiche = Ui.TitreCarte("Vie d'un équipement");
        titreFiche.Margin = new Padding(0, 0, 0, 14);
        _code.Margin = new Padding(0, 0, 0, 18);
        var carteFiche = new Carte { Padding = new Padding(24), CouleurBordure = Theme.BordureForte, Rayon = 26 };
        carteFiche.Controls.Add(Ui.Colonne(
            (titreFiche, SizeType.AutoSize, 0),
            (_code, SizeType.Absolute, Theme.HauteurChamp + 18),
            (_fiche, SizeType.Percent, 100)));

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Tout ce qui s'est passé : départs, retours, sorties, révisions et matériel.")), exporter, _periode), SizeType.AutoSize, 0),
            (_filtre, SizeType.Absolute, 44 + 16),
            (Ui.Colonnes((_journal, SizeType.Percent, 63), (carteFiche, SizeType.Percent, 37)), SizeType.Percent, 100)));
    }

    public override string Titre => "Historique";

    public override EntreeMenu Menu => EntreeMenu.Historique;

    private PeriodeHistorique Periode => _periode.SelectedItem is Choix<PeriodeHistorique> c ? c.Valeur : PeriodeHistorique.TrenteJours;

    /// <summary>Type choisi dans les puces ; null = « Tout ».</summary>
    private TypeEvenement? TypeChoisi =>
        _filtre.Selection is { } libelle ? ServiceHistorique.TypesFiltrables.First(t => ServiceHistorique.Libelle(t) == libelle) : null;

    protected override async Task ChargerAsync(object? parametre)
    {
        await RechargerAsync();
        // Fiche : le code demandé, sinon le dernier équipement du journal.
        var code = parametre as string ?? _evenements.FirstOrDefault(e => e.Code is not null)?.Code;
        if (code is not null)
        {
            _code.Valeur = code;
            await AfficherVieAsync(code);
        }
        else
        {
            AfficherFiche(Indication("Scannez une étiquette ou cliquez sur un code du journal pour voir tout le parcours de l'équipement."));
        }

        _code.Champ.Focus();
    }

    private async Task RechargerAsync()
    {
        _chargement = true;
        try
        {
            _evenements = await _historique.ListerAsync(Periode);
        }
        finally
        {
            _chargement = false;
        }

        if (IsDisposed)
        {
            return;
        }

        _filtre.Afficher(_evenements.Count, ServiceHistorique.TypesFiltrables.Select(t => (ServiceHistorique.Libelle(t), _evenements.Count(e => e.Type == t))));
        AfficherJournal();
        _code.Champ.Focus();
    }

    private void AfficherJournal()
    {
        var type = TypeChoisi;
        var aujourdHui = DateOnly.FromDateTime(DateTime.Now);
        var jours = _evenements
            .Where(e => type is null || e.Type == type)
            .GroupBy(e => DateOnly.FromDateTime(e.Moment))
            .Select(g =>
            {
                var (titre, precision) = ServiceHistorique.LibelleJour(g.Key, aujourdHui);
                return new JourJournal(titre, precision, g.Select(e => new LigneJournalAffichee(
                    e.Moment.ToString("HH:mm", Libelles.Culture), e.Titre, e.Detail, e.Code, FicheVie.Icone(e.Type), FicheVie.Couleur(e.Type))).ToList());
            })
            .ToList();
        _journal.Afficher(jours, type is null ? "Rien sur cette période." : "Aucun événement de ce type sur la période.");
    }

    private async Task ExporterAsync()
    {
        var type = TypeChoisi;
        var evenements = (await _historique.ListerAsync(Periode, regrouper: false)).Where(e => type is null || e.Type == type).ToList();
        if (evenements.Count == 0)
        {
            Dialogues.Information(FindForm(), "Rien à exporter sur cette période.");
            return;
        }

        using var dialogue = new SaveFileDialog
        {
            Title = "Exporter l'historique",
            Filter = "Fichier CSV (Excel)|*.csv",
            FileName = _historique.NomFichierExport(),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // UTF-8 avec BOM : Excel lit correctement les accents.
            await File.WriteAllTextAsync(dialogue.FileName, ServiceHistorique.ExporterCsv(evenements), new UTF8Encoding(true));
            Dialogues.Information(FindForm(), $"Historique exporté : {Libelles.Pluriel(evenements.Count, "événement")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogues.Erreur(FindForm(), "Impossible d'écrire le fichier. Est-il ouvert dans Excel ?");
        }
    }

    // ---------------------------------------------------------------- Vie d'un équipement

    private async Task AfficherVieAsync(string saisie)
    {
        if (saisie.Trim().Length == 0)
        {
            return;
        }

        var vie = await _historique.ObtenirVieAsync(saisie);
        if (IsDisposed)
        {
            return;
        }

        if (vie is null)
        {
            var inconnu = new BandeauMessage { Height = 76, Margin = Padding.Empty };
            inconnu.Afficher(NiveauResultat.Erreur, $"Code inconnu : {saisie.Trim()}.");
            AfficherFiche(inconnu);
        }
        else
        {
            _code.Valeur = vie.Equipement.Code;
            AfficherFiche(FicheVie.Construire(vie).ToArray());
        }

        _code.Champ.SelectAll(); // Le scan suivant remplace le code.
        _code.Champ.Focus();
    }

    private Label Indication(string texte)
    {
        var l = Ui.Libelle(texte, Theme.Police(10.5f), Theme.TexteSecondaire);
        l.AutoSize = false;
        l.Height = 70;
        return l;
    }

    /// <summary>Remplace le contenu de la fiche (contrôles en pixels logiques, mis à l'échelle ici).</summary>
    private void AfficherFiche(params Control[] controles)
    {
        var contenu = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        contenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var c in controles)
        {
            contenu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            contenu.Controls.Add(c, 0, contenu.RowStyles.Count - 1);
        }

        Dessin.MettreALEchelle(contenu, this);
        _fiche.SuspendLayout();
        while (_fiche.Controls.Count > 0)
        {
            var c = _fiche.Controls[0];
            _fiche.Controls.RemoveAt(0);
            c.Dispose();
        }

        _fiche.Controls.Add(contenu);
        _fiche.ResumeLayout(true);
    }
}
