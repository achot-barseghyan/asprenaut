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
        _filtre.CouleurPoint = libelle => types.TryGetValue(libelle, out var t) ? Couleur(t) : null;
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
                    e.Moment.ToString("HH:mm", Libelles.Culture), e.Titre, e.Detail, e.Code, Icone(e.Type), Couleur(e.Type))).ToList());
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
            AfficherFiche(ConstruireFiche(vie).ToArray());
        }

        _code.Champ.SelectAll(); // Le scan suivant remplace le code.
        _code.Champ.Focus();
    }

    private static IEnumerable<Control> ConstruireFiche(VieEquipement vie)
    {
        var e = vie.Equipement;
        var nom = Ui.Libelle(e.Libelle.Length > 0 ? e.Libelle : e.Code, Theme.PoliceGrasse(15f));
        nom.AutoSize = false;
        nom.AutoEllipsis = true;
        nom.Height = 34;
        nom.Margin = Padding.Empty;
        yield return nom;

        var precisions = new List<string> { vie.Categorie };
        if (!string.IsNullOrWhiteSpace(e.Taille))
        {
            precisions.Add($"taille {e.Taille}");
        }

        precisions.Add($"ajouté le {e.CreeLe.ToLocalTime().ToString("dd/MM/yyyy", Libelles.Culture)}");
        var sous = Ui.Libelle(string.Join(" · ", precisions.Where(p => p.Length > 0)), Theme.Police(9.75f), Theme.TexteSecondaireClair);
        sous.AutoSize = false;
        sous.AutoEllipsis = true;
        sous.Height = 24;
        sous.Margin = new Padding(0, 0, 0, 14);
        yield return sous;

        yield return new TuilesVie(vie) { Height = 76, Margin = new Padding(0, 0, 0, 20) };

        var frise = new FriseVie(vie.Etapes) { Margin = Padding.Empty };
        frise.Height = FriseVie.HauteurPour(vie.Etapes.Count);
        yield return frise;
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

    // ---------------------------------------------------------------- Couleurs et pictogrammes des types

    private static Color Couleur(TypeEvenement type) => type switch
    {
        TypeEvenement.Depart => Theme.Depart,
        TypeEvenement.Retour => Theme.Retour,
        TypeEvenement.Sortie => Theme.AccentClair,
        TypeEvenement.Revision => Theme.Avertissement,
        TypeEvenement.Materiel => Theme.Materiel,
        TypeEvenement.Manquant => Theme.Erreur,
        _ => Theme.TexteSecondaireClair,
    };

    private static string Icone(TypeEvenement type) => type switch
    {
        TypeEvenement.Depart => Icones.Depart,
        TypeEvenement.Retour => Icones.Retour,
        TypeEvenement.Sortie => Icones.Sorties,
        TypeEvenement.Revision => Icones.Revisions,
        TypeEvenement.Materiel => Icones.Plus,
        TypeEvenement.Manquant => Icones.Alerte,
        _ => Icones.Info,
    };

    /// <summary>Trois encadrés : sorties de la saison, où est l'équipement, prochaine révision.</summary>
    private sealed class TuilesVie : ControleDessine
    {
        private readonly VieEquipement _vie;

        public TuilesVie(VieEquipement vie)
        {
            _vie = vie;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = $"{Libelles.Pluriel(vie.SortiesSaison, "sortie")} cette saison. {vie.LibelleStatut}, {vie.PrecisionStatut}. Prochaine révision : {vie.Echeance}.";
        }

        protected override void Peindre(Graphics g)
        {
            var ecart = E(8);
            var l = (Width - (2 * ecart)) / 3;
            var fond = Theme.Sur(Theme.Surface, Theme.Fond, 0.45f);

            Encadre(g, new RectangleF(0, 0, l, Height - 1), _vie.SortiesSaison.ToString(Libelles.Culture), Theme.PoliceGrasse(15f),
                "sorties cette saison", fond, Theme.Texte, Theme.TexteSecondaire);

            var statut = _vie.Statut switch
            {
                StatutInventaire.Disponible => Theme.Retour,
                StatutInventaire.Dehors => Theme.DepartClair,
                StatutInventaire.Manquant => Theme.Erreur,
                StatutInventaire.HorsService => Theme.Avertissement,
                _ => Theme.TexteSecondaireClair,
            };
            Encadre(g, new RectangleF(l + ecart, 0, l, Height - 1), _vie.LibelleStatut, Theme.PoliceTitre(10.5f),
                _vie.PrecisionStatut, Theme.Teinte(statut, 0.12f), statut, statut);

            var (fondRevision, couleurRevision) = _vie.EtatEcheance switch
            {
                EtatRevision.Depassee => (Theme.Teinte(Theme.Erreur, 0.12f), Theme.Erreur),
                EtatRevision.Proche => (Theme.Teinte(Theme.Avertissement, 0.12f), Theme.Avertissement),
                _ => (fond, Theme.Texte),
            };
            Encadre(g, new RectangleF(2 * (l + ecart), 0, l, Height - 1), _vie.Echeance, Theme.PoliceTitre(10.5f),
                _vie.EtatEcheance == EtatRevision.Depassee ? "révision dépassée" : "prochaine révision",
                fondRevision, couleurRevision, _vie.EtatEcheance == EtatRevision.AJour ? Theme.TexteSecondaire : couleurRevision);
        }

        private void Encadre(Graphics g, RectangleF r, string valeur, Font police, string legende, Color fond, Color couleurValeur, Color couleurLegende)
        {
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            var x = (int)(r.X + E(12));
            var largeur = (int)(r.Width - E(20));
            Dessin.Texte(g, valeur, police, new Rectangle(x, (int)(r.Y + E(8)), largeur, (int)E(28)), couleurValeur);
            TextRenderer.DrawText(g, legende, Theme.Police(8.25f), new Rectangle(x, (int)(r.Y + E(38)), largeur, (int)(r.Height - E(40))), couleurLegende,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Frise verticale du parcours : pastille colorée, date, ce qui s'est passé, où et avec qui.</summary>
    private sealed class FriseVie : ControleDessine
    {
        private const int HauteurEtape = 68;
        private const int MargeBas = 14;
        private readonly IReadOnlyList<EtapeVie> _etapes;

        public FriseVie(IReadOnlyList<EtapeVie> etapes)
        {
            _etapes = etapes;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Parcours de l'équipement";
            AccessibleDescription = string.Join(". ", etapes.Select(e => $"{Date(e)}, {e.Titre}, {e.Detail}"));
        }

        public static int HauteurPour(int nombre) => Math.Max(1, nombre) * HauteurEtape + MargeBas;

        private static string Date(EtapeVie e) => e.Moment.ToString(e.DateSeule ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", Libelles.Culture);

        protected override void Peindre(Graphics g)
        {
            var h = E(HauteurEtape);
            var cx = E(11);
            var rHalo = E(11);
            var rPoint = E(7);
            var xTexte = (int)E(34);
            var largeur = Width - xTexte;
            for (var i = 0; i < _etapes.Count; i++)
            {
                var etape = _etapes[i];
                var y = i * h;
                var cy = y + E(11);
                var couleur = Couleur(etape.Type);

                // Trait vers l'étape suivante, puis pastille entourée d'un halo de sa couleur.
                if (i < _etapes.Count - 1)
                {
                    using var trait = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.16f), E(2));
                    g.DrawLine(trait, cx, cy + rHalo + E(4), cx, cy + h - rHalo - E(4));
                }

                using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.20f)))
                {
                    g.FillEllipse(pinceau, cx - rHalo, cy - rHalo, 2 * rHalo, 2 * rHalo);
                }

                using (var pinceau = new SolidBrush(couleur))
                {
                    g.FillEllipse(pinceau, cx - rPoint, cy - rPoint, 2 * rPoint, 2 * rPoint);
                }

                Dessin.Texte(g, Date(etape), Theme.Police(9f), new Rectangle(xTexte, (int)y, largeur, (int)E(18)), Theme.TexteSecondaire);
                Dessin.Texte(g, etape.Titre, Theme.PoliceTitre(10.5f), new Rectangle(xTexte, (int)(y + E(19)), largeur, (int)E(24)), Theme.Texte);
                Dessin.Texte(g, etape.Detail, Theme.Police(9f), new Rectangle(xTexte, (int)(y + E(43)), largeur, (int)E(18)), Theme.TexteSecondaireClair);
            }
        }
    }
}
