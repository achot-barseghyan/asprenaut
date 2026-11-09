using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Révisions : compteurs (en retard, ce mois-ci, à planifier, à jour), échéancier filtrable,
/// enregistrement d'un contrôle, prochaine séance TIV, matériel en atelier.
/// </summary>
public sealed class EcranRevisions : EcranBase
{
    private readonly ServiceRevisions _revisions;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceReglages _reglages;
    private readonly GrilleAdaptative _compteurs = new() { LargeurMin = 190, HauteurElement = 104, Ecart = 14, ColonnesMax = 4, Margin = new Padding(0, 0, 0, 22) };
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = new Padding(0, 0, 0, 12) };
    private readonly DataGridView _grille = new();
    private readonly TableLayoutPanel _droite = new() { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty };
    private IReadOnlyList<LigneEcheance> _echeances = [];
    private EtatEcheance? _etat;

    // --- Enregistrer un contrôle ---
    private readonly ChampSaisie _scan = new() { TexteIndicatif = "Scannez ou tapez un code" };
    private readonly Label _equipement = Ui.Libelle("", Theme.PoliceTitre(10.5f));
    private readonly Label _derniers = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
    private readonly ListeDeroulante _type = new() { AccessibleName = "Contrôle" };
    private readonly ListeDeroulante _inspecteur = new() { AccessibleName = "Inspecteur TIV" };
    private readonly ChampDate _date = new();
    private readonly List<ChoixResultat> _resultats = [];
    private readonly BandeauMessage _consequence = new() { Height = 88, Margin = new Padding(0, 0, 0, 12) };
    private readonly BoutonArrondi _valider = new("Valider le contrôle", StyleBouton.Principal) { AutoSize = false, Enabled = false };
    private PreparationControle? _preparation;
    private ResultatControle _resultat = ResultatControle.Conforme;

    // --- Séance et atelier (reconstruits à chaque chargement) ---
    private readonly CarteSection _carteSeance = new("Prochaine séance TIV", Icones.Calendrier, Theme.AccentClair);
    private readonly CarteSection _carteAtelier = new("En atelier", Icones.Revisions, Theme.Avertissement);
    private readonly TableLayoutPanel _contenuSeance = Pile();
    private readonly TableLayoutPanel _contenuAtelier = Pile();

    public EcranRevisions(ServiceRevisions revisions, ServiceMoniteurs moniteurs, ServiceReglages reglages)
    {
        _revisions = revisions;
        _moniteurs = moniteurs;
        _reglages = reglages;

        var registre = new BoutonArrondi("Registre PDF", StyleBouton.Secondaire, Icones.Document);
        registre.Click += (_, _) => Dialogues.Information(FindForm(), "Le registre PDF arrive dans une prochaine version.");
        var planifier = new BoutonArrondi("Planifier une séance TIV", StyleBouton.Principal, Icones.Calendrier);
        planifier.Click += (_, _) => Executer(PlanifierAsync);

        // --- Échéancier ---
        Theme.AppliquerGrille(_grille, hauteurLigne: 60);
        _grille.AccessibleName = "Échéancier des contrôles";
        Tableaux.AjouterColonne(_grille, "equipement", "Équipement", 140);
        Tableaux.AjouterColonne(_grille, "controle", "Contrôle", 128);
        Tableaux.AjouterColonne(_grille, "dernier", "Dernier", 74);
        Tableaux.AjouterColonne(_grille, "echeance", "Échéance", 96);
        Tableaux.AjouterColonne(_grille, "etat", "État", 112);
        Tableaux.AjouterColonne(_grille, "action", "", 106);
        foreach (DataGridViewColumn colonne in _grille.Columns)
        {
            colonne.HeaderCell.Style.Padding = new Padding(10, 0, 4, 0);
            colonne.DefaultCellStyle.Padding = new Padding(10, 0, 4, 0);
        }

        _grille.Columns["controle"]!.DefaultCellStyle.Font = Theme.Police(9.75f);
        _grille.CellPainting += PeindreCellule;
        _grille.CellFormatting += FormaterCellule;
        _grille.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _grille.Columns["action"]!.Index && _grille.Rows[e.RowIndex].Tag is LigneEcheance l && l.Etat != EtatEcheance.AJour)
            {
                Executer(() => PreparerAsync(l.Code, l.Type));
            }
        };
        _grille.CellMouseEnter += (_, e) => _grille.Cursor = e.ColumnIndex == _grille.Columns["action"]!.Index && e.RowIndex >= 0 ? Cursors.Hand : Cursors.Default;
        _filtre.Changement += (_, _) => AfficherGrille();

        var carteGrille = new Carte { Padding = new Padding(8, 6, 8, 8), Rayon = 22, Margin = new Padding(0, 0, 0, 14) };
        _grille.Dock = DockStyle.Fill;
        carteGrille.Controls.Add(_grille);

        var note = new NoteInfo(TexteNote()) { Height = 76 };
        var titreEcheancier = Ui.TitreSection("Échéancier");
        var gauche = Ui.Colonne(
            (titreEcheancier, SizeType.AutoSize, 0),
            (_filtre, SizeType.Absolute, 44 + 12),
            (carteGrille, SizeType.Percent, 100),
            (note, SizeType.Absolute, 76));
        gauche.Margin = new Padding(0, 0, 22, 0);

        // --- Colonne de droite : trois cartes qui défilent ensemble ---
        _droite.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var carte in new[] { CarteControle(), (CarteSection)_carteSeance, _carteAtelier })
        {
            _droite.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            carte.AutoSize = true;
            carte.Dock = DockStyle.Fill;
            carte.Margin = new Padding(0, 0, 0, 18);
            _droite.Controls.Add(carte, 0, _droite.RowStyles.Count - 1);
        }

        _carteSeance.Ajouter(_contenuSeance);
        _carteAtelier.Ajouter(_contenuAtelier);
        var envoyer = new BoutonArrondi("Envoyer du matériel en atelier", StyleBouton.Secondaire) { AutoSize = false, Height = 46, Margin = new Padding(0, 6, 0, 0) };
        envoyer.Click += (_, _) => Executer(EnvoyerAtelierAsync);
        _carteAtelier.Ajouter(envoyer);

        var defilement = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        defilement.HorizontalScroll.Maximum = 0;
        defilement.AutoScroll = false;
        defilement.VerticalScroll.Visible = false;
        defilement.AutoScroll = true;
        Natif.ScrollbarsDuTheme(defilement);
        defilement.Controls.Add(_droite);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("TIV, requalifications et entretien : ce qui arrive à échéance et ce qui est en retard.")), planifier, registre), SizeType.AutoSize, 0),
            (_compteurs, SizeType.Absolute, 104 + 22),
            (Ui.Colonnes((gauche, SizeType.Percent, 62), (defilement, SizeType.Percent, 38)), SizeType.Percent, 100)));
    }

    public override string Titre => "Révisions";

    public override EntreeMenu Menu => EntreeMenu.Revisions;

    protected override async Task ChargerAsync(object? parametre)
    {
        var inspecteurs = await _moniteurs.ListerInspecteursTivAsync();
        _inspecteur.Items.Clear();
        foreach (var m in inspecteurs)
        {
            _inspecteur.Items.Add(new Choix<long>(m.Id, m.NomComplet));
        }

        if (_inspecteur.Items.Count > 0)
        {
            _inspecteur.SelectedIndex = 0;
        }

        await RechargerAsync();
        _scan.Champ.Focus();
    }

    private async Task RechargerAsync()
    {
        _echeances = await _revisions.ListerEcheancesAsync();
        var seance = await _revisions.ProchaineSeanceAsync();
        var atelier = await _revisions.ListerAtelierAsync();
        if (IsDisposed)
        {
            return;
        }

        AfficherCompteurs();
        _filtre.Afficher(_echeances.Count, Familles(_echeances).Select(f => (f, _echeances.Count(l => Famille(l) == f))));
        AfficherGrille();
        AfficherSeance(seance);
        AfficherAtelier(atelier);
    }

    // ---------------------------------------------------------------- Compteurs et échéancier

    /// <summary>Clé du filtre : « TIV », « Requalification », ou la catégorie (Détendeurs, Gilets...).</summary>
    private static string Famille(LigneEcheance l) => l.Type switch
    {
        TypeControle.Tiv => "TIV",
        TypeControle.Requalification => "Requalification",
        _ => l.Categorie,
    };

    private static IEnumerable<string> Familles(IEnumerable<LigneEcheance> lignes)
    {
        var familles = lignes.Select(Famille).Distinct().ToList();
        return new[] { "TIV", "Requalification" }.Where(familles.Contains)
            .Concat(familles.Where(f => f is not ("TIV" or "Requalification")).OrderBy(f => f, StringComparer.Create(Libelles.Culture, true)));
    }

    private void AfficherCompteurs()
    {
        var r = _reglages.Actuels;
        var aujourdHui = DateOnly.FromDateTime(DateTime.Today);
        var finDuMois = new DateOnly(aujourdHui.Year, aujourdHui.Month, DateTime.DaysInMonth(aujourdHui.Year, aujourdHui.Month));
        (EtatEcheance Etat, string Libelle, string Aide)[] tuiles =
        [
            (EtatEcheance.EnRetard, "En retard", r.DepartSiRevisionDepassee ? "Partent avec un avertissement" : "Bloqués pour les sorties"),
            (EtatEcheance.CeMois, "Ce mois-ci", $"Avant le {finDuMois.ToString("d MMMM", Libelles.Culture)}"),
            (EtatEcheance.Bientot, $"Sous {r.JoursAPlanifier} jours", "À planifier"),
            (EtatEcheance.AJour, "À jour", "Rien à faire"),
        ];

        _compteurs.SuspendLayout();
        while (_compteurs.Controls.Count > 0)
        {
            var c = _compteurs.Controls[0];
            _compteurs.Controls.RemoveAt(0);
            c.Dispose();
        }

        foreach (var (etat, libelle, aide) in tuiles)
        {
            var tuile = new TuileCompteur(libelle, aide, _echeances.Count(l => l.Etat == etat), CouleurEtat(etat)) { Selectionne = _etat == etat };
            tuile.Click += (_, _) =>
            {
                _etat = _etat == etat ? null : etat;
                foreach (var t in _compteurs.Controls.OfType<TuileCompteur>())
                {
                    t.Selectionne = t == tuile && _etat is not null;
                }

                AfficherGrille();
            };
            _compteurs.Controls.Add(tuile);
        }

        _compteurs.ResumeLayout(true);
    }

    private void AfficherGrille()
    {
        _grille.SuspendLayout();
        _grille.Rows.Clear();
        foreach (var l in _echeances.Where(l => (_etat is null || l.Etat == _etat) && _filtre.Accepte(Famille(l))))
        {
            var i = _grille.Rows.Add(l.Libelle, l.Controle, l.Dernier is { } d ? Mois(d) : "—", l.Echeance.ToString("dd/MM/yy", Libelles.Culture), LibelleEtat(l.Etat), "");
            _grille.Rows[i].Tag = l;
        }

        _grille.ResumeLayout();
    }

    private void FormaterCellule(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneEcheance l || e.CellStyle is null)
        {
            return;
        }

        if (e.ColumnIndex == _grille.Columns["echeance"]!.Index)
        {
            e.CellStyle.Font = Theme.PoliceTitre(10.5f);
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = l.Etat is EtatEcheance.EnRetard or EtatEcheance.CeMois ? CouleurEtat(l.Etat) : Theme.TexteDoux;
        }
        else if (e.ColumnIndex == _grille.Columns["controle"]!.Index)
        {
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.TexteDoux;
        }
    }

    private void PeindreCellule(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || _grille.Rows[e.RowIndex].Tag is not LigneEcheance l || e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        if (e.ColumnIndex == _grille.Columns["equipement"]!.Index)
        {
            // Nom sur la première ligne, code turquoise en dessous.
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            var x = e.CellBounds.Left + e.CellStyle.Padding.Left;
            var h = e.CellBounds.Height / 2;
            Dessin.Texte(g, l.Libelle, Theme.PoliceTitre(10.5f), new Rectangle(x, e.CellBounds.Top + (h / 4), e.CellBounds.Width - 12, h), Theme.Texte);
            Dessin.Texte(g, l.Code, Theme.PoliceCode(9f), new Rectangle(x, e.CellBounds.Top + h - (h / 8), e.CellBounds.Width - 12, h), Theme.Accent);
            e.Handled = true;
        }
        else if (e.ColumnIndex == _grille.Columns["etat"]!.Index)
        {
            Tableaux.PeindrePastille(e, LibelleEtat(l.Etat), CouleurEtat(l.Etat));
        }
        else if (e.ColumnIndex == _grille.Columns["action"]!.Index)
        {
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            if (l.Etat != EtatEcheance.AJour)
            {
                // Bouton « Enregistrer » dessiné dans la cellule (clic géré par CellClick).
                Dessin.Qualite(g);
                var hb = 40f * g.DpiY / 96f;
                var lb = Math.Min(e.CellBounds.Width - 10, 104f * g.DpiX / 96f);
                var r = new RectangleF(e.CellBounds.Right - lb - 6, e.CellBounds.Top + ((e.CellBounds.Height - hb) / 2), lb, hb);
                using (var chemin = Dessin.Arrondi(r, 12f * g.DpiX / 96f))
                using (var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.12f)))
                using (var bord = new Pen(Theme.Teinte(Theme.Accent, 0.30f)))
                {
                    g.FillPath(fond, chemin);
                    g.DrawPath(bord, chemin);
                }

                Dessin.Texte(g, "Enregistrer", Theme.PoliceTitre(9f), Rectangle.Round(r), Theme.AccentClair, centre: true);
            }

            e.Handled = true;
        }
    }

    private string TexteNote()
    {
        var r = _reglages.Actuels;
        return $"Échéances calculées selon les réglages : TIV au plus tous les {r.MoisEntreTiv} mois. Requalification tous les "
            + $"{r.AnneesEntreRequalifications} ans (TIV annuelle faite). Détendeurs, gilets et ordinateurs : tous les {r.MoisEntreRevisions} mois. "
            + $"Un contrôle « à surveiller » est refait au bout de {r.MoisSiASurveiller} mois.";
    }

    // ---------------------------------------------------------------- Enregistrer un contrôle

    private CarteSection CarteControle()
    {
        var carte = new CarteSection("Enregistrer un contrôle", null, Theme.Accent)
        {
            CouleurBordure = Theme.Sur(Theme.Surface, Theme.Accent, 0.40f),
            BackColor = Theme.Teinte(Theme.Accent, 0.05f),
        };

        _scan.Champ.Font = Theme.PoliceCode(12f);
        _scan.Champ.TextAlign = HorizontalAlignment.Center;
        _scan.Champ.AccessibleName = "Code de l'équipement contrôlé";
        _scan.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Executer(() => PreparerAsync(_scan.Valeur, null));
            }
        };
        _scan.Champ.Leave += (_, _) =>
        {
            if (_scan.Valeur.Trim().Length > 0 && !string.Equals(_scan.Valeur.Trim(), _preparation?.Equipement.Code, StringComparison.OrdinalIgnoreCase))
            {
                Executer(() => PreparerAsync(_scan.Valeur, null));
            }
        };

        var consigne = Ui.Libelle("Scannez l'équipement contrôlé", Theme.Police(9.75f), Theme.TexteSecondaireClair);
        consigne.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(consigne);
        _scan.Margin = new Padding(0, 0, 0, 12);
        carte.Ajouter(_scan).Height = Theme.HauteurChamp;

        // Équipement reconnu et ses derniers contrôles.
        var info = new CadreInfo();
        _equipement.Margin = new Padding(0, 0, 0, 2);
        _derniers.Margin = Padding.Empty;
        info.Ajouter(_equipement);
        info.Ajouter(_derniers);
        info.Margin = new Padding(0, 0, 0, 14);
        carte.Ajouter(info);

        // Type de contrôle (TIV ou requalification pour un bloc).
        _type.SelectedIndexChanged += (_, _) => MettreAJourFormulaire();
        carte.Ajouter(Ui.LibelleChamp("Contrôle"));
        _type.Margin = new Padding(0, 0, 0, 12);
        carte.Ajouter(_type);

        // Résultat : trois choix.
        carte.Ajouter(Ui.LibelleChamp("Résultat"));
        var choix = new TableLayoutPanel { ColumnCount = 3, Height = Theme.HauteurCliquable, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 14) };
        foreach (var (resultat, i) in new[] { ResultatControle.Conforme, ResultatControle.ASurveiller, ResultatControle.Refuse }.Select((r, i) => (r, i)))
        {
            choix.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            var bouton = new ChoixResultat(ServiceRevisions.LibelleResultat(resultat), CouleurResultat(resultat))
            {
                Selectionne = resultat == _resultat,
                Margin = new Padding(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0),
                Dock = DockStyle.Fill,
            };
            bouton.Click += (_, _) =>
            {
                _resultat = resultat;
                foreach (var b in _resultats)
                {
                    b.Selectionne = b == bouton;
                }

                MettreAJourFormulaire();
            };
            _resultats.Add(bouton);
            choix.Controls.Add(bouton, i, 0);
        }

        carte.Ajouter(choix);

        // Inspecteur et date côte à côte.
        var champs = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 14) };
        champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        champs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        champs.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
        var lInspecteur = Ui.LibelleChamp("Inspecteur TIV");
        var lDate = Ui.LibelleChamp("Date");
        lDate.Margin = new Padding(6, 0, 0, 8);
        _inspecteur.Margin = new Padding(0, 0, 6, 0);
        _inspecteur.Dock = DockStyle.Fill;
        _date.Margin = new Padding(6, 0, 0, 0);
        _date.Dock = DockStyle.Fill;
        _date.Date = DateOnly.FromDateTime(DateTime.Today);
        _date.AccessibleName = "Date du contrôle";
        champs.Controls.Add(lInspecteur, 0, 0);
        champs.Controls.Add(lDate, 1, 0);
        champs.Controls.Add(_inspecteur, 0, 1);
        champs.Controls.Add(_date, 1, 1);
        carte.Ajouter(champs);

        carte.Ajouter(_consequence);
        _valider.Height = Theme.HauteurBouton;
        _valider.Margin = Padding.Empty;
        _valider.Click += (_, _) => Executer(ValiderControleAsync);
        carte.Ajouter(_valider);

        _type.Enabled = false;
        _inspecteur.Enabled = false;
        _equipement.Text = "Aucun équipement choisi";
        _derniers.Text = "Scannez une étiquette, ou cliquez « Enregistrer » dans l'échéancier.";
        _consequence.Afficher(NiveauResultat.Succes, "Le résultat du contrôle s'affichera ici.");
        return carte;
    }

    private async Task PreparerAsync(string code, TypeControle? type)
    {
        var preparation = await _revisions.PreparerControleAsync(code);
        if (IsDisposed)
        {
            return;
        }

        _preparation = preparation;
        if (preparation is null)
        {
            _equipement.Text = "Code inconnu";
            _derniers.Text = "Vérifiez l'étiquette ou le code tapé.";
            _type.Items.Clear();
            _type.Enabled = false;
            _valider.Enabled = false;
            _consequence.Afficher(NiveauResultat.Erreur, $"Aucun équipement avec le code « {code.Trim()} ».");
            return;
        }

        var e = preparation.Equipement;
        _scan.Valeur = e.Code;
        _equipement.Text = e.Libelle;
        var derniers = preparation.TypesPossibles.Contains(TypeControle.Tiv)
            ? $"Dernière TIV {MoisOuTiret(preparation.DerniereTiv)} · requalification {MoisOuTiret(preparation.DerniereRequalification)}"
            : $"Dernière révision {MoisOuTiret(preparation.DerniereRevision)}";
        _derniers.Text = e.Etat == EtatEquipement.HorsService ? $"{derniers} · hors service" : derniers;

        _type.Items.Clear();
        foreach (var t in preparation.TypesPossibles)
        {
            _type.Items.Add(new Choix<TypeControle>(t, ServiceRevisions.LibelleControle(t, preparation.Categorie)));
        }

        var voulu = type ?? preparation.TypePropose;
        _type.SelectedItem = _type.Items.OfType<Choix<TypeControle>>().First(c => c.Valeur == voulu);
        _type.Enabled = _type.Items.Count > 1;
        _valider.Enabled = true;
        MettreAJourFormulaire();
    }

    private void MettreAJourFormulaire()
    {
        if (_preparation is null || _type.SelectedItem is not Choix<TypeControle> type)
        {
            return;
        }

        var tiv = type.Valeur == TypeControle.Tiv;
        _inspecteur.Enabled = tiv && _inspecteur.Items.Count > 0;
        var date = _date.Date ?? DateOnly.FromDateTime(DateTime.Today);
        var niveau = _resultat switch
        {
            ResultatControle.Conforme => NiveauResultat.Succes,
            ResultatControle.ASurveiller => NiveauResultat.Avertissement,
            _ => NiveauResultat.Erreur,
        };
        var texte = tiv && _inspecteur.Items.Count == 0
            ? "Aucun inspecteur TIV : cochez « Inspecteur TIV » sur la fiche d'un moniteur."
            : _revisions.Consequence(type.Valeur, _resultat, date);
        _consequence.Afficher(tiv && _inspecteur.Items.Count == 0 ? NiveauResultat.Avertissement : niveau, texte);
    }

    private async Task ValiderControleAsync()
    {
        if (_preparation is null || _type.SelectedItem is not Choix<TypeControle> type)
        {
            return;
        }

        if (_date.Date is not { } date)
        {
            _consequence.Afficher(NiveauResultat.Erreur, "Date invalide : écrivez-la comme 08/11/2026.");
            return;
        }

        var inspecteur = type.Valeur == TypeControle.Tiv ? (_inspecteur.SelectedItem as Choix<long>)?.Valeur : null;
        var resultat = await _revisions.EnregistrerControleAsync(_preparation.Equipement.Id, type.Valeur, _resultat, date, inspecteur);
        _consequence.Afficher(resultat.Reussi ? NiveauResultat.Succes : NiveauResultat.Erreur, resultat.Message);
        if (!resultat.Reussi)
        {
            return;
        }

        // Prêt pour le contrôle suivant.
        _preparation = null;
        _scan.Valeur = "";
        _equipement.Text = "Contrôle enregistré";
        _derniers.Text = "Scannez l'équipement suivant.";
        _type.Items.Clear();
        _type.Enabled = false;
        _valider.Enabled = false;
        await RechargerAsync();
        _scan.Champ.Focus();
    }

    // ---------------------------------------------------------------- Séance TIV

    private void AfficherSeance(ProchaineSeance? prochaine)
    {
        var lignes = new List<Control>();
        if (prochaine is null)
        {
            lignes.Add(Ui.Libelle("Aucune séance prévue. Cliquez « Planifier une séance TIV ».", Theme.Police(9.75f), Theme.TexteSecondaireClair));
        }
        else
        {
            var s = prochaine.Seance;
            var date = new PastilleDate { Date = s.DateSeance, Margin = Padding.Empty };
            var details = string.Join(" · ", new[]
            {
                s.DateSeance.ToString("dddd d MMMM", Libelles.Culture),
                s.Heure,
                s.Lieu,
                prochaine.Inspecteurs.Count == 0 ? "aucun inspecteur" : string.Join(", ", prochaine.Inspecteurs),
            }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var texte = Ui.Libelle(details, Theme.Police(9.75f), Theme.TexteSecondaireClair);
            texte.MaximumSize = new Size(260, 0);
            var entete = Ui.Colonnes((date, SizeType.Absolute, 62 + 14), (texte, SizeType.Percent, 100));
            date.Dock = DockStyle.None;
            date.Anchor = AnchorStyles.Left;
            texte.Dock = DockStyle.None;
            texte.Anchor = AnchorStyles.Left;
            entete.Height = 74;
            entete.Margin = new Padding(0, 0, 0, 12);
            lignes.Add(entete);

            var blocs = prochaine.BlocsAPreparer;
            lignes.Add(Ui.LibelleChamp(blocs.Count == 0 ? "Aucun bloc à préparer" : $"{Libelles.Pluriel(blocs.Count, "bloc")} à préparer"));
            if (blocs.Count > 0)
            {
                var codes = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
                foreach (var b in blocs)
                {
                    var couleur = b.Etat == EtatEcheance.EnRetard ? Theme.DepartClair : Theme.Avertissement;
                    codes.Controls.Add(new Pastille(b.Code, Theme.Teinte(couleur, 0.14f), couleur, police: Theme.PoliceCode(9f), hauteur: 28) { Margin = new Padding(0, 0, 6, 6) });
                }

                lignes.Add(codes);
                lignes.Add(Ui.Libelle(
                    _reglages.Actuels.DepartSiRevisionDepassee
                        ? "Après leur échéance, ces blocs partent avec un avertissement."
                        : "Ces blocs ne pourront pas partir en sortie après leur échéance.",
                    Theme.Police(9.75f), Theme.TexteSecondaire));
            }
        }

        Remplacer(_contenuSeance, lignes);
    }

    private async Task PlanifierAsync()
    {
        var date = new ChampDate { Date = DateOnly.FromDateTime(DateTime.Today).AddDays(7) };
        var heure = new ChampSaisie { Valeur = "9:00", TexteIndicatif = "9:00" };
        var lieu = new ChampSaisie { Valeur = "Local du club" };
        heure.Champ.AccessibleName = "Heure";
        lieu.Champ.AccessibleName = "Lieu";
        using var fenetre = new FenetreFormulaire("Planifier une séance TIV", "Planifier", () =>
            date.Date is { } d
                ? _revisions.PlanifierSeanceAsync(d, heure.Valeur, lieu.Valeur)
                : Task.FromResult(ResultatOperation.Echec("Date invalide : écrivez-la comme 21/11/2026.")));
        fenetre.Champ("Date", date);
        fenetre.Champ("Heure", heure);
        fenetre.Champ("Lieu", lieu);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RechargerAsync();
        }
    }

    // ---------------------------------------------------------------- Atelier

    private void AfficherAtelier(IReadOnlyList<LigneAtelier> atelier)
    {
        _carteAtelier.AccessibleName = $"En atelier : {atelier.Count}";
        var lignes = new List<Control>();
        if (atelier.Count == 0)
        {
            lignes.Add(Ui.Libelle("Aucun équipement en atelier.", Theme.Police(9.75f), Theme.TexteSecondaire));
        }

        foreach (var a in atelier)
        {
            var detail = $"{a.Prestataire} · envoyé le {a.EnvoyeLe.ToString("dd/MM", Libelles.Culture)}"
                + (a.RetourPrevu is { } r ? $" · retour prévu {r.ToString("dd/MM", Libelles.Culture)}" : "")
                + (string.IsNullOrWhiteSpace(a.Motif) ? "" : $" · {a.Motif}");
            var revenu = new BoutonArrondi($"Revenu", StyleBouton.Secondaire) { Margin = Padding.Empty, AccessibleName = $"{a.Code} revenu d'atelier" };
            var passage = a.PassageId;
            revenu.Click += (_, _) => Executer(async () =>
            {
                var resultat = await _revisions.RetourAtelierAsync(passage);
                if (!resultat.Reussi)
                {
                    Dialogues.Erreur(FindForm(), resultat.Message);
                }

                await RechargerAsync();
            });
            lignes.Add(new LigneAtelierControle($"{a.Libelle}", a.Code, detail, revenu));
        }

        Remplacer(_contenuAtelier, lignes);
    }

    private async Task EnvoyerAtelierAsync()
    {
        var code = new ChampSaisie { TexteIndicatif = "Scannez ou tapez le code" };
        code.Champ.Font = Theme.PoliceCode(11.25f);
        code.Champ.AccessibleName = "Code de l'équipement";
        var prestataire = new ChampSaisie { TexteIndicatif = "Magasin, organisme de requalification…" };
        prestataire.Champ.AccessibleName = "Atelier ou magasin";
        var motif = new ChampSaisie { TexteIndicatif = "Révision, requalification, réparation…" };
        motif.Champ.AccessibleName = "Motif";
        var retour = new ChampDate();
        retour.AccessibleName = "Retour prévu";
        using var fenetre = new FenetreFormulaire("Envoyer en atelier", "Envoyer", () =>
            _revisions.EnvoyerEnAtelierAsync(code.Valeur, prestataire.Valeur, motif.Valeur, retour.Date));
        fenetre.Champ("Équipement", code);
        fenetre.Champ("Atelier ou magasin", prestataire);
        fenetre.Champ("Motif", motif);
        fenetre.Champ("Retour prévu (facultatif)", retour);
        var aide = Ui.Libelle("L'équipement passe hors service jusqu'à son retour.", Theme.Police(9.75f), Theme.TexteSecondaire);
        fenetre.Ajouter(aide);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RechargerAsync();
        }
    }

    // ---------------------------------------------------------------- Outils

    private static TableLayoutPanel Pile()
    {
        var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    /// <summary>Remplace le contenu d'une pile (mise à l'échelle comprise, car ajouté après l'affichage).</summary>
    private void Remplacer(TableLayoutPanel pile, IEnumerable<Control> lignes)
    {
        pile.SuspendLayout();
        while (pile.Controls.Count > 0)
        {
            var c = pile.Controls[0];
            pile.Controls.RemoveAt(0);
            c.Dispose();
        }

        pile.RowStyles.Clear();
        foreach (var c in lignes)
        {
            if (IsHandleCreated)
            {
                Dessin.MettreALEchelle(c, this);
            }

            pile.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            pile.Controls.Add(c, 0, pile.RowStyles.Count - 1);
        }

        pile.ResumeLayout(true);
    }

    private static string Mois(DateOnly d) => d.ToString("MM/yyyy", Libelles.Culture);

    private static string MoisOuTiret(DateOnly? d) => d is { } v ? Mois(v) : "—";

    private static string LibelleEtat(EtatEcheance e) => e switch
    {
        EtatEcheance.EnRetard => "En retard",
        EtatEcheance.CeMois => "Ce mois-ci",
        EtatEcheance.Bientot => "À planifier",
        _ => "À jour",
    };

    private static Color CouleurEtat(EtatEcheance e) => e switch
    {
        EtatEcheance.EnRetard => Theme.Erreur,
        EtatEcheance.CeMois => Theme.Avertissement,
        EtatEcheance.Bientot => Theme.AccentClair,
        _ => Theme.Retour,
    };

    private static Color CouleurResultat(ResultatControle r) => r switch
    {
        ResultatControle.Conforme => Theme.Retour,
        ResultatControle.ASurveiller => Theme.Avertissement,
        _ => Theme.Erreur,
    };

    /// <summary>Compteur cliquable « 2 · En retard · Bloqués pour les sorties » (filtre l'échéancier).</summary>
    private sealed class TuileCompteur : Button
    {
        private readonly string _aide;
        private readonly int _nombre;
        private readonly Color _couleur;
        private bool _selectionne;

        public TuileCompteur(string libelle, string aide, int nombre, Color couleur)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Text = libelle;
            _aide = aide;
            _nombre = nombre;
            _couleur = couleur;
            AccessibleName = $"{libelle} : {nombre}. {aide}";
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Filtre choisi" : null;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            float E(float v) => Dessin.Echelle(this, v);
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
            using (var chemin = Dessin.Arrondi(r, E(20)))
            {
                using var fond = new SolidBrush(_selectionne ? Theme.Teinte(_couleur, 0.14f) : Theme.Surface);
                g.FillPath(fond, chemin);
                using var bord = new Pen(_selectionne ? _couleur : Theme.Bordure, E(_selectionne ? 1.5f : 1));
                g.DrawPath(bord, chemin);
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Accent, E(1.5f));
                    using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), E(17));
                    g.DrawPath(focus, interieur);
                }
            }

            // Point lumineux.
            var d = E(12);
            var x = E(18);
            var y = E(24);
            using (var halo = new SolidBrush(Theme.Alpha(_couleur, 0.25f)))
            {
                g.FillEllipse(halo, x - E(4), y - E(4), d + E(8), d + E(8));
            }

            using (var point = new SolidBrush(_couleur))
            {
                g.FillEllipse(point, x, y, d, d);
            }

            var gauche = (int)(x + d + E(14));
            var largeur = Width - gauche - (int)E(10);
            Dessin.Texte(g, _nombre.ToString(Libelles.Culture), Theme.PoliceGrasse(21f), new Rectangle(gauche, (int)E(12), largeur, (int)E(38)), Theme.Texte);
            Dessin.Texte(g, Text, Theme.PoliceTitre(10.5f), new Rectangle(gauche, (int)E(50), largeur, (int)E(22)), Theme.Texte);
            Dessin.Texte(g, _aide, Theme.Police(9f), new Rectangle(gauche, (int)E(72), largeur, (int)E(20)), Theme.TexteSecondaireClair);
        }
    }

    /// <summary>Choix d'un résultat (Conforme, À surveiller, Refusé), coloré quand il est choisi.</summary>
    private sealed class ChoixResultat : Button
    {
        private readonly Color _couleur;
        private bool _selectionne;

        public ChoixResultat(string libelle, Color couleur)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Text = libelle;
            _couleur = couleur;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Choisi" : null;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var fondParent = Dessin.CouleurParent(this);
            var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
            using (var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 12)))
            {
                using var fond = new SolidBrush(_selectionne ? Theme.Sur(fondParent, _couleur, 0.14f) : Theme.Sur(fondParent, Theme.Fond, 0.45f));
                g.FillPath(fond, chemin);
                using var bord = new Pen(_selectionne ? _couleur : Theme.Bordure, Dessin.Echelle(this, _selectionne ? 1.5f : 1));
                g.DrawPath(bord, chemin);
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Accent, Dessin.Echelle(this, 1.5f));
                    using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), Dessin.Echelle(this, 9));
                    g.DrawPath(focus, interieur);
                }
            }

            Dessin.Texte(g, Text, _selectionne ? Theme.PoliceGrasse(9.75f) : Theme.PoliceTitre(9.75f), ClientRectangle, _selectionne ? _couleur : Theme.TexteDoux, centre: true);
        }
    }

    /// <summary>Encadré sombre de l'équipement reconnu.</summary>
    private sealed class CadreInfo : TableLayoutPanel
    {
        public CadreInfo()
        {
            ColumnCount = 1;
            AutoSize = true;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.45f);
            Padding = new Padding(14, 12, 14, 12);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Ajouter(Control c)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(c, 0, RowStyles.Count - 1);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Dessin.EffacerAvecParent(this, e.Graphics);
            Dessin.Qualite(e.Graphics);
            using var chemin = Dessin.Arrondi(new RectangleF(0, 0, Width - 1, Height - 1), Dessin.Echelle(this, 14));
            using var fond = new SolidBrush(BackColor);
            e.Graphics.FillPath(fond, chemin);
        }
    }

    /// <summary>Note d'information (pictogramme i + texte), sous l'échéancier.</summary>
    private sealed class NoteInfo : ControleDessine
    {
        public NoteInfo(string texte)
        {
            Text = texte;
            AccessibleName = texte;
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(18)))
            {
                using var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.06f));
                g.FillPath(fond, chemin);
                using var bord = new Pen(Theme.Teinte(Theme.Accent, 0.16f), E(1));
                g.DrawPath(bord, chemin);
            }

            var t = E(20);
            Icones.Dessiner(g, Icones.Info, new RectangleF(E(18), E(14), t, t), Theme.AccentClair, 1.9f);
            var gauche = (int)(E(18) + t + E(12));
            TextRenderer.DrawText(g, Text, Theme.Police(9.75f), new Rectangle(gauche, (int)E(12), Width - gauche - (int)E(16), Height - (int)E(16)),
                Theme.TexteSecondaireClair, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Ligne d'un équipement en atelier : nom, code, détail, bouton « Revenu ».</summary>
    private sealed class LigneAtelierControle : TableLayoutPanel
    {
        public LigneAtelierControle(string libelle, string code, string detail, BoutonArrondi revenu)
        {
            ColumnCount = 2;
            AutoSize = true;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.40f);
            Padding = new Padding(12, 10, 10, 10);
            Margin = new Padding(0, 0, 0, 8);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            var titre = Ui.Rangee(true, Ui.Libelle(libelle, Theme.PoliceTitre(10.5f)), Ui.Libelle(code, Theme.PoliceCode(9f), Theme.Accent));
            titre.Controls[1].Margin = new Padding(6, 2, 0, 0);
            var sous = Ui.Libelle(detail, Theme.Police(8.25f), Theme.TexteSecondaire);
            sous.MaximumSize = new Size(300, 0);
            var textes = Ui.Pile(titre, sous);
            textes.Anchor = AnchorStyles.Left;
            revenu.Anchor = AnchorStyles.Right;
            revenu.Height = Theme.HauteurCliquable;
            Controls.Add(textes, 0, 0);
            Controls.Add(revenu, 1, 0);
            AccessibleName = $"{libelle} {code}, {detail}";
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Dessin.EffacerAvecParent(this, e.Graphics);
            Dessin.Qualite(e.Graphics);
            using var chemin = Dessin.Arrondi(new RectangleF(0, 0, Width - 1, Height - 1), Dessin.Echelle(this, 14));
            using var fond = new SolidBrush(BackColor);
            e.Graphics.FillPath(fond, chemin);
        }
    }
}
