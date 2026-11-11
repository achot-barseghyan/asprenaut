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
/// Révisions en tableau : chaque équipement avance de gauche à droite, jusqu'à son retour en service.
/// À prévoir (échéance proche ou dépassée, 90 % du seuil d'utilisations) → Planifié (séance TIV) → En atelier → Fait.
/// Le panneau « Ajouter une intervention » s'ouvre en haut depuis une carte, ou en scannant l'équipement.
/// </summary>
public sealed class EcranRevisions : EcranBase
{
    private readonly ServiceRevisions _revisions;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceReglages _reglages;
    private readonly INavigation _navigation;
    private readonly GardeGestion _garde;
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Margin = Padding.Empty };
    private readonly ChampSaisie _scan = new() { TexteIndicatif = "Scannez un équipement", Icone = Icones.Qr };
    private readonly PageRevisions _page = new();
    private readonly PanneauIntervention _panneau;
    private readonly ColonneRevision[] _colonnes;
    private IReadOnlyList<Moniteur> _inspecteurs = [];
    private TableauRevisions? _tableau;

    public EcranRevisions(ServiceRevisions revisions, ServiceMoniteurs moniteurs, ServiceReglages reglages, INavigation navigation, GardeGestion garde)
    {
        _revisions = revisions;
        _moniteurs = moniteurs;
        _reglages = reglages;
        _navigation = navigation;
        _garde = garde;

        var registre = new BoutonArrondi("Registre PDF", StyleBouton.Secondaire, Icones.Document);
        registre.Click += (_, _) => Executer(async () =>
        {
            var registreControles = await _revisions.RegistreAsync();
            var r = _reglages.Actuels;
            await Fichiers.EnregistrerPdfAsync(this, "Enregistrer le registre des contrôles", _revisions.NomFichierRegistre(),
                chemin => Impression.RegistrePdf.Enregistrer(registreControles, r.NomClub, r.ResponsableMateriel, chemin));
        });

        // Filtre par type d'entretien et champ de scan (le pistolet ouvre l'intervention de l'équipement).
        _filtre.Changement += (_, _) => AfficherColonnes();
        _scan.Champ.Font = Theme.PoliceCode(11.25f);
        _scan.Champ.AccessibleName = "Code de l'équipement";
        _scan.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Executer(() => ScannerAsync(_scan.Valeur));
            }
        };
        var barre = Ui.Colonnes((_filtre, SizeType.Percent, 100), (_scan, SizeType.Absolute, 300));
        _scan.Margin = new Padding(16, 0, 0, 0);

        _panneau = new PanneauIntervention(revisions) { Visible = false };
        _panneau.Annule += (_, _) => FermerPanneau();
        _panneau.Enregistre += (_, _) => Executer(async () =>
        {
            FermerPanneau();
            await RechargerAsync();
        });
        _panneau.FicheDemandee += (_, code) => _navigation.AllerA<EcranHistorique>(code);
        _panneau.Valider.AfficherPastille = false;
        _garde.Lier(_panneau.Valider, ActionProtegee.Revisions);

        _colonnes =
        [
            new ColonneRevision(1, "À prévoir", "Échéance proche ou dépassée. Choisissez : séance TIV ou atelier.", Theme.Avertissement),
            new ColonneRevision(2, "Planifié", "Prévu à la prochaine séance TIV du club.", Theme.AccentClair),
            new ColonneRevision(3, "En atelier", "Parti chez un magasin ou un organisme. On attend son retour.", Theme.Materiel),
            new ColonneRevision(4, "Fait", $"Intervention enregistrée ces {ServiceRevisions.JoursFait} derniers jours. Retour en service.", Theme.Retour),
        ];
        var note = new NoteInfo(TexteNote());
        _page.Placer(_panneau, _colonnes, note);

        var defilement = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        defilement.HorizontalScroll.Maximum = 0;
        defilement.AutoScroll = false;
        defilement.VerticalScroll.Visible = false;
        defilement.AutoScroll = true;
        Natif.ScrollbarsDuTheme(defilement);
        defilement.Controls.Add(_page);
        void Ajuster()
        {
            // Largeur extérieure moins la barre (à la résolution de l'écran) dès que la page dépasse : ne dépend pas
            // du moment où Windows affiche la barre, qui recouvrait sinon le bord droit de la colonne « Fait ».
            var barreDefilement = _page.Height > defilement.Height ? SystemInformation.GetVerticalScrollBarWidthForDpi(defilement.DeviceDpi) : 0;
            var largeur = defilement.Width - barreDefilement - (int)Dessin.Echelle(this, 6);
            if (largeur > 0 && _page.Width != largeur)
            {
                _page.Width = largeur;
            }
        }

        defilement.Layout += (_, _) => Ajuster();
        defilement.ClientSizeChanged += (_, _) => Ajuster();
        _page.SizeChanged += (_, _) => Ajuster();

        var consultation = new BandeauConsultation(_garde, "Consultez librement. Planifier, envoyer en atelier ou enregistrer une intervention demande le code gestionnaire.");
        _garde.Lier(consultation, ActionProtegee.Revisions);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Chaque équipement avance de gauche à droite, jusqu'à son retour en service.")), registre), SizeType.AutoSize, 0),
            (consultation, SizeType.AutoSize, 0),
            (barre, SizeType.Absolute, Theme.HauteurChamp + 18),
            (defilement, SizeType.Percent, 100)));
        barre.Margin = new Padding(0, 0, 0, 18);
    }

    public override string Titre => "Révisions";

    public override EntreeMenu Menu => EntreeMenu.Revisions;

    protected override async Task ChargerAsync(object? parametre)
    {
        _inspecteurs = await _moniteurs.ListerInspecteursTivAsync();
        await RechargerAsync();
        if (parametre is string code && !IsDisposed)
        {
            await ScannerAsync(code);
        }

        _scan.Champ.Focus();
    }

    private async Task RechargerAsync()
    {
        var tableau = await _revisions.TableauAsync();
        if (IsDisposed)
        {
            return;
        }

        _tableau = tableau;
        var actifs = tableau.Cartes.Where(c => c.Etape != EtapeRevision.Fait).ToList();
        _filtre.Afficher(actifs.Count, tableau.Familles.Select(f => (f, actifs.Count(c => c.Famille == f))));
        AfficherColonnes();
    }

    private void AfficherColonnes()
    {
        if (_tableau is not { } tableau)
        {
            return;
        }

        _page.SuspendLayout();
        foreach (var (colonne, etape) in _colonnes.Zip(Enum.GetValues<EtapeRevision>()))
        {
            var cartes = tableau.Cartes.Where(c => c.Etape == etape && _filtre.Accepte(c.Famille)).Select(Carte).ToList();
            colonne.Remplir(cartes, etape == EtapeRevision.Planifie ? EnteteSeance(tableau.Seance) : null);
        }

        // Les cartes sont ajoutées aux colonnes, pas à la page : la page ne demande pas d'elle-même une mise en page.
        _page.ResumeLayout(false);
        _page.PerformLayout();
    }

    // ---------------------------------------------------------------- Cartes et actions

    private CarteEquipement Carte(CarteRevision c)
    {
        var carte = new CarteEquipement(c, CouleurEtat(c.Etat));
        BoutonArrondi Action(string texte, StyleBouton style, Func<Task> action)
        {
            var bouton = carte.AjouterAction(texte, style);
            bouton.AfficherPastille = false; // Le bandeau « Mode consultation » de la page suffit.
            _garde.Lier(bouton, ActionProtegee.Revisions);
            bouton.Click += (_, _) => Executer(action);
            return bouton;
        }

        switch (c.Etape)
        {
            case EtapeRevision.APrevoir:
                if (c.Type == TypeControle.Tiv)
                {
                    Action("Ajouter à la séance TIV", StyleBouton.Principal, () => AjouterASeanceAsync(c));
                }
                else
                {
                    Action("Envoyer en atelier", StyleBouton.Principal, () => EnvoyerAtelierAsync(c.Code));
                }

                Action("Faire maintenant", StyleBouton.Secondaire, () => OuvrirAsync(c));
                break;
            case EtapeRevision.Planifie:
                Action("Enregistrer le résultat", StyleBouton.Principal, () => OuvrirAsync(c));
                Action("Retirer", StyleBouton.Secondaire, () => RetirerDeSeanceAsync(c));
                break;
            case EtapeRevision.EnAtelier:
                Action("Retour d'atelier", StyleBouton.Principal, () => OuvrirAsync(c));
                break;
        }

        return carte;
    }

    private static Color CouleurEtat(EtatCarte etat) => etat switch
    {
        EtatCarte.EnRetard or EtatCarte.HorsService => Theme.Erreur,
        EtatCarte.CeMois or EtatCarte.ASurveiller => Theme.Avertissement,
        EtatCarte.EnAtelier => Theme.Materiel,
        EtatCarte.Conforme => Theme.Retour,
        _ => Theme.AccentClair,
    };

    private async Task AjouterASeanceAsync(CarteRevision c)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Ajouter à la séance TIV"))
        {
            return;
        }

        var resultat = await _revisions.AjouterASeanceAsync(c.EquipementId);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        await RechargerAsync();
    }

    private async Task RetirerDeSeanceAsync(CarteRevision c)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Retirer de la séance TIV"))
        {
            return;
        }

        await _revisions.RetirerDeSeanceAsync(c.EquipementId);
        await RechargerAsync();
    }

    /// <summary>Ouvre le panneau d'intervention pour une carte (action protégée : le code est demandé avant).</summary>
    private Task OuvrirAsync(CarteRevision c)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Enregistrer une intervention"))
        {
            return Task.CompletedTask;
        }

        _panneau.Preparer(c, _inspecteurs);
        _panneau.Visible = true;
        _page.PerformLayout();
        if (_page.Parent is ScrollableControl defilement)
        {
            defilement.AutoScrollPosition = Point.Empty; // Le panneau est en haut de la page.
        }

        _panneau.PrendreFocus();
        return Task.CompletedTask;
    }

    private void FermerPanneau()
    {
        _panneau.Visible = false;
        _page.PerformLayout();
        _scan.Valeur = "";
        _scan.Champ.Focus();
    }

    /// <summary>Scan : la carte de l'équipement (hors « Fait ») s'il en a une, sinon une intervention « faire maintenant ».</summary>
    private async Task ScannerAsync(string saisie)
    {
        if (saisie.Trim().Length == 0)
        {
            return;
        }

        var preparation = await _revisions.PreparerControleAsync(saisie);
        if (IsDisposed)
        {
            return;
        }

        if (preparation is null)
        {
            Dialogues.Information(FindForm(), $"Code inconnu : {saisie.Trim()}.");
            _scan.Champ.SelectAll();
            return;
        }

        var e = preparation.Equipement;
        var carte = _tableau?.Cartes.FirstOrDefault(c => c.EquipementId == e.Id && c.Etape != EtapeRevision.Fait)
            ?? new CarteRevision(EtapeRevision.APrevoir, e.Id, e.Code, e.Libelle, preparation.Categorie, preparation.Categorie,
                preparation.TypePropose, ServiceRevisions.LibelleControle(preparation.TypePropose, preparation.Categorie),
                "", EtatCarte.APlanifier, "", false, null);
        _scan.Valeur = e.Code;
        await OuvrirAsync(carte);
    }

    // ---------------------------------------------------------------- Séance TIV

    /// <summary>En tête de « Planifié » : la prochaine séance, ou le bouton pour en planifier une.</summary>
    private Control EnteteSeance(ProchaineSeance? prochaine)
    {
        if (prochaine is null)
        {
            var planifier = new BoutonArrondi("Planifier une séance TIV", StyleBouton.Secondaire, Icones.Calendrier) { AutoSize = false, AfficherPastille = false };
            _garde.Lier(planifier, ActionProtegee.Revisions);
            planifier.Click += (_, _) => Executer(PlanifierAsync);
            return planifier;
        }

        var s = prochaine.Seance;
        var titre = $"Séance TIV · {s.DateSeance.ToString("dddd", Libelles.Culture)}{(string.IsNullOrWhiteSpace(s.Heure) ? "" : $" {s.Heure}")}";
        var detail = string.Join(" · ", new[] { s.Lieu, prochaine.Inspecteurs.Count == 0 ? "aucun inspecteur" : string.Join(", ", prochaine.Inspecteurs) }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        return new CarteSeance(s.DateSeance, titre, detail);
    }

    private async Task PlanifierAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Planifier une séance TIV"))
        {
            return;
        }

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

    private async Task EnvoyerAtelierAsync(string code)
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Envoyer en atelier"))
        {
            return;
        }

        var prestataire = new ChampSaisie { TexteIndicatif = "Magasin, organisme de requalification…" };
        prestataire.Champ.AccessibleName = "Atelier ou magasin";
        var motif = new ChampSaisie { TexteIndicatif = "Révision, requalification, réparation…" };
        motif.Champ.AccessibleName = "Motif";
        var retour = new ChampDate { AccessibleName = "Retour prévu" };
        using var fenetre = new FenetreFormulaire($"Envoyer {code} en atelier", "Envoyer", () =>
            _revisions.EnvoyerEnAtelierAsync(code, prestataire.Valeur, motif.Valeur, retour.Date));
        fenetre.Champ("Atelier ou magasin", prestataire);
        fenetre.Champ("Motif", motif);
        fenetre.Champ("Retour prévu (facultatif)", retour);
        fenetre.Ajouter(Ui.Libelle("L'équipement passe hors service jusqu'à son retour.", Theme.Police(9.75f), Theme.TexteSecondaire));
        fenetre.Shown += (_, _) => prestataire.Champ.Focus();
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RechargerAsync();
        }
    }

    private string TexteNote()
    {
        var r = _reglages.Actuels;
        var blocage = r.DepartSiRevisionDepassee
            ? "En retard, il part encore avec un avertissement"
            : "En retard, il est bloqué pour les sorties jusqu'à l'intervention";
        blocage += r.BloquerAuSeuil ? " ; au seuil d'utilisations, il est bloqué." : " ; au seuil d'utilisations, il part avec un avertissement.";
        return $"Un équipement arrive seul dans « À prévoir » {r.JoursAPlanifier} jours avant son échéance, ou à 90 % de son seuil d'utilisations. {blocage} "
            + $"Échéances : TIV tous les {r.MoisEntreTiv} mois, requalification tous les {r.AnneesEntreRequalifications} ans, "
            + $"détendeurs tous les {ServiceRevisions.MoisRevision(r, "Détendeurs")} mois, stabs sans date obligatoire.";
    }

    // ================================================================ Mise en page

    /// <summary>Page qui défile : panneau d'intervention (s'il est ouvert), colonnes (4, ou 2 sur écran étroit), note.</summary>
    private sealed class PageRevisions : Panel
    {
        private PanneauIntervention _panneau = null!;
        private ColonneRevision[] _colonnes = [];
        private NoteInfo _note = null!;

        public PageRevisions()
        {
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            DoubleBuffered = true;
        }

        public void Placer(PanneauIntervention panneau, ColonneRevision[] colonnes, NoteInfo note)
        {
            (_panneau, _colonnes, _note) = (panneau, colonnes, note);
            Controls.Add(panneau);
            Controls.AddRange(colonnes);
            Controls.Add(note);
        }

        private int E(float v) => (int)Dessin.Echelle(this, v);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_colonnes.Length == 0)
            {
                return;
            }

            // Changer la hauteur peut faire apparaître la barre de défilement, donc réduire la largeur
            // (voir Ajuster) ; la mise en page imbriquée est ignorée par WinForms : on recommence ici.
            for (var essai = 0; essai < 3; essai++)
            {
                var largeur = ClientSize.Width;
                var hauteur = Placer(largeur);
                if (Height != hauteur)
                {
                    Height = hauteur;
                }

                if (ClientSize.Width == largeur)
                {
                    break;
                }
            }
        }

        private int Placer(int largeur)
        {
            var ecart = E(14);
            var y = 0;
            if (_panneau.Visible)
            {
                var h = _panneau.HauteurPour(largeur);
                _panneau.Bounds = new Rectangle(0, 0, largeur, h);
                y = h + E(18);
            }

            var nb = largeur >= E(1000) ? 4 : 2;
            var lc = (largeur - ((nb - 1) * ecart)) / nb;
            for (var debut = 0; debut < _colonnes.Length; debut += nb)
            {
                var rangee = _colonnes.Skip(debut).Take(nb).ToList();
                var h = Math.Max(E(360), rangee.Max(c => c.Mesurer(lc)));
                for (var i = 0; i < rangee.Count; i++)
                {
                    rangee[i].Bounds = new Rectangle(i * (lc + ecart), y, lc, h);
                }

                y += h + ecart;
            }

            var hNote = E(84);
            _note.Bounds = new Rectangle(0, y + E(4), largeur, hNote);
            return y + hNote + E(12);
        }
    }

    /// <summary>Colonne du tableau : bandeau coloré, numéro, titre, nombre de cartes, aide, en-tête optionnel (séance) et cartes.</summary>
    private sealed class ColonneRevision : Panel
    {
        private readonly int _numero;
        private readonly string _titre;
        private readonly string _aide;
        private readonly Color _couleur;
        private Control? _entete;
        private readonly List<CarteEquipement> _cartes = [];

        public ColonneRevision(int numero, string titre, string aide, Color couleur)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            (_numero, _titre, _aide, _couleur) = (numero, titre, aide, couleur);
            BackColor = Theme.Surface;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = titre;
        }

        private float E(float v) => Dessin.Echelle(this, v);

        private Font PoliceAide => Theme.Police(8.25f);

        private int HauteurAide(int largeur) =>
            TextRenderer.MeasureText(_aide, PoliceAide, new Size(Math.Max(1, largeur - (int)E(28)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        public void Remplir(IReadOnlyList<CarteEquipement> cartes, Control? entete)
        {
            SuspendLayout();
            foreach (var c in _cartes)
            {
                Controls.Remove(c);
                c.Dispose();
            }

            if (_entete is not null)
            {
                Controls.Remove(_entete);
                _entete.Dispose();
            }

            _cartes.Clear();
            _cartes.AddRange(cartes);
            _entete = entete;
            if (entete is not null)
            {
                Controls.Add(entete);
            }

            Controls.AddRange([.. cartes]);
            AccessibleDescription = $"{Libelles.Pluriel(cartes.Count, "équipement")}";
            ResumeLayout(false);
            Invalidate();
        }

        /// <summary>Place l'en-tête et les cartes pour cette largeur ; retourne la hauteur nécessaire.</summary>
        public int Mesurer(int largeur)
        {
            var marge = (int)E(14);
            var interieur = largeur - (2 * marge);
            var y = (int)E(16) + (int)E(30) + (int)E(4) + HauteurAide(largeur) + (int)E(12);
            if (_entete is not null)
            {
                var h = _entete is CarteSeance ? (int)E(58) : (int)E(Theme.HauteurCliquable);
                _entete.Bounds = new Rectangle(marge, y, interieur, h);
                y += h + (int)E(10);
            }

            foreach (var c in _cartes)
            {
                var h = c.Mesurer(interieur);
                c.Bounds = new Rectangle(marge, y, interieur, h);
                y += h + (int)E(10);
            }

            if (_cartes.Count == 0)
            {
                y += (int)E(50); // « Rien ici ».
            }

            return y + marge;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(22)))
            using (var fond = new SolidBrush(Theme.Surface))
            using (var bord = new Pen(Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
                // Bandeau de couleur en haut, dans l'arrondi.
                var ancien = g.Clip;
                g.SetClip(chemin);
                using (var bandeau = new SolidBrush(_couleur))
                {
                    g.FillRectangle(bandeau, 0, 0, Width, E(3));
                }

                g.Clip = ancien;
            }

            var x = E(18);
            var y = E(16);
            var d = E(26);
            using (var pastille = new SolidBrush(_couleur))
            {
                g.FillEllipse(pastille, x, y + E(2), d, d);
            }

            Dessin.Texte(g, _numero.ToString(Libelles.Culture), Theme.PoliceGrasse(9.75f), Rectangle.Round(new RectangleF(x, y + E(2), d, d)), Theme.Fond, centre: true);
            var nombre = _cartes.Count.ToString(Libelles.Culture);
            var lNombre = TextRenderer.MeasureText(g, nombre, Theme.PoliceGrasse(9f)).Width + E(14);
            var rNombre = new RectangleF(Width - E(14) - lNombre, y + E(5), lNombre, E(22));
            using (var chemin = Dessin.Arrondi(rNombre, rNombre.Height / 2))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.12f)))
            {
                g.FillPath(fond, chemin);
            }

            Dessin.Texte(g, nombre, Theme.PoliceGrasse(9f), Rectangle.Round(rNombre), Theme.TexteDoux, centre: true);
            Dessin.Texte(g, _titre, Theme.PoliceTitre(12f), new Rectangle((int)(x + d + E(10)), (int)y, (int)(rNombre.Left - x - d - E(14)), (int)E(30)), Theme.Texte);
            var yAide = (int)(y + E(34));
            TextRenderer.DrawText(g, _aide, PoliceAide, new Rectangle((int)E(14), yAide, Width - (int)E(28), HauteurAide(Width)), Theme.TexteSecondaire,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            if (_cartes.Count == 0)
            {
                var yVide = _entete is null ? yAide + HauteurAide(Width) + (int)E(20) : _entete.Bottom + (int)E(18);
                Dessin.Texte(g, "Rien ici", Theme.Police(9.75f), new Rectangle(0, yVide, Width, (int)E(24)), Theme.TexteSecondaire, centre: true);
            }
        }
    }

    /// <summary>Carte d'un équipement : nom, code, pastille d'état, contrôle, détail, usage, blocage, boutons d'action.</summary>
    private sealed class CarteEquipement : Panel
    {
        private readonly CarteRevision _c;
        private readonly Color _couleur;
        private readonly List<BoutonArrondi> _actions = [];

        public CarteEquipement(CarteRevision c, Color couleur)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            (_c, _couleur) = (c, couleur);
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.55f);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = $"{c.Libelle} {c.Code}, {c.Badge}. {c.Controle}. {c.Detail}" + (c.Bloque ? ". Bloqué pour les sorties" : "");
        }

        private float E(float v) => Dessin.Echelle(this, v);

        private Font PoliceDetail => Theme.Police(8.25f);

        public BoutonArrondi AjouterAction(string texte, StyleBouton style)
        {
            var bouton = new BoutonArrondi(texte, style) { AutoSize = false, Font = Theme.PoliceTitre(9f), Margin = Padding.Empty, AccessibleName = $"{texte} {_c.Code}" };
            _actions.Add(bouton);
            Controls.Add(bouton);
            return bouton;
        }

        private int HauteurDetail(int largeur) => _c.Detail.Length == 0 ? 0
            : TextRenderer.MeasureText(_c.Detail, PoliceDetail, new Size(Math.Max(1, largeur - (int)E(24)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        /// <summary>Hauteur pour cette largeur, boutons placés (passés à la ligne s'ils ne tiennent pas).</summary>
        public int Mesurer(int largeur)
        {
            var p = (int)E(12);
            var y = p + (int)E(42) + (int)E(18) + HauteurDetail(largeur);
            if (_c.Usage is not null)
            {
                y += (int)E(18);
            }

            if (_c.Bloque)
            {
                y += (int)E(20);
            }

            if (_actions.Count > 0)
            {
                // Côte à côte (largeur partagée) si tous les textes tiennent, sinon empilés sur toute la largeur.
                y += (int)E(8);
                var h = (int)E(Theme.HauteurCliquable);
                var ecart = (int)E(6);
                var interieur = largeur - (2 * p);
                var part = (interieur - ((_actions.Count - 1) * ecart)) / _actions.Count;
                var cote = _actions.All(b => TextRenderer.MeasureText(b.Text, b.Font).Width + (int)E(20) <= part);
                for (var i = 0; i < _actions.Count; i++)
                {
                    _actions[i].Bounds = cote
                        ? new Rectangle(p + (i * (part + ecart)), y, part, h)
                        : new Rectangle(p, y + (i * (h + ecart)), interieur, h);
                }

                y += cote ? h : (_actions.Count * h) + ((_actions.Count - 1) * ecart);
            }

            return y + p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(16)))
            using (var fond = new SolidBrush(BackColor))
            using (var bord = new Pen(Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var p = (int)E(12);
            var largeur = Width - (2 * p);

            // Pastille d'état à droite du nom.
            var lBadge = 0;
            if (_c.Badge.Length > 0)
            {
                var police = Theme.PoliceGrasse(7.5f);
                lBadge = TextRenderer.MeasureText(g, _c.Badge, police).Width + (int)E(12);
                var rb = new RectangleF(Width - p - lBadge, p, lBadge, E(22));
                using var chemin = Dessin.Arrondi(rb, rb.Height / 2);
                using var fond = new SolidBrush(Theme.Sur(BackColor, _couleur, 0.16f));
                g.FillPath(fond, chemin);
                Dessin.Texte(g, _c.Badge, police, Rectangle.Round(rb), _couleur, centre: true);
            }

            var lNom = largeur - lBadge - (int)E(8);
            Dessin.Texte(g, _c.Libelle, Theme.PoliceTitre(10.5f), new Rectangle(p, p, lNom, (int)E(22)), Theme.Texte);
            Dessin.Texte(g, _c.Code, Theme.PoliceCode(9f), new Rectangle(p, p + (int)E(22), lNom, (int)E(18)), Theme.Accent);
            var y = p + (int)E(42);
            Dessin.Texte(g, _c.Controle, Theme.Police(8.25f), new Rectangle(p, y, largeur, (int)E(18)), Theme.TexteDoux);
            y += (int)E(18);
            var hDetail = HauteurDetail(Width);
            if (hDetail > 0)
            {
                TextRenderer.DrawText(g, _c.Detail, PoliceDetail, new Rectangle(p, y, largeur, hDetail), Theme.TexteSecondaire, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += hDetail;
            }

            if (_c.Usage is { } u)
            {
                // Barre d'usage : jaune, rouge au seuil.
                var lTexte = TextRenderer.MeasureText(g, $"{u.Utilisations} / {u.Seuil}", Theme.PoliceTitre(7.5f)).Width;
                var piste = new RectangleF(p, y + E(6), largeur - lTexte - E(8), E(6));
                using (var chemin = Dessin.Arrondi(piste, piste.Height / 2))
                using (var fond = new SolidBrush(Theme.Teinte(Theme.AccentClair, 0.12f)))
                {
                    g.FillPath(fond, chemin);
                }

                var part = Math.Min(1f, (float)u.Utilisations / Math.Max(1, u.Seuil));
                if (part > 0)
                {
                    var plein = piste with { Width = Math.Max(piste.Height, piste.Width * part) };
                    using var chemin = Dessin.Arrondi(plein, piste.Height / 2);
                    using var pinceau = new SolidBrush(u.Etat == EtatCompteur.Atteint ? Theme.Erreur : Theme.Avertissement);
                    g.FillPath(pinceau, chemin);
                }

                Dessin.Texte(g, $"{u.Utilisations} / {u.Seuil}", Theme.PoliceTitre(7.5f), new Rectangle((int)(piste.Right + E(8)), y, lTexte + 4, (int)E(18)), Theme.TexteSecondaire);
                y += (int)E(18);
            }

            if (_c.Bloque)
            {
                var t = E(13);
                Icones.Dessiner(g, "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM5.6 5.6l12.8 12.8", new RectangleF(p, y + E(4), t, t), Theme.Erreur, 2.4f);
                Dessin.Texte(g, "Bloqué pour les sorties", Theme.PoliceGrasse(7.5f), new Rectangle(p + (int)(t + E(6)), y + (int)E(1), largeur, (int)E(20)), Theme.Erreur);
            }
        }
    }

    /// <summary>Prochaine séance TIV (en tête de « Planifié ») : jour et mois, titre, lieu et inspecteurs.</summary>
    private sealed class CarteSeance : ControleDessine
    {
        private readonly DateOnly _date;
        private readonly string _titre;
        private readonly string _detail;

        public CarteSeance(DateOnly date, string titre, string detail)
        {
            (_date, _titre, _detail) = (date, titre, detail);
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = $"{titre}, {Libelles.DateLongue(date)}. {detail}";
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var fond = new SolidBrush(Theme.Teinte(Theme.Accent, 0.10f)))
            using (var bord = new Pen(Theme.Teinte(Theme.Accent, 0.25f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var l = (int)E(44);
            Dessin.Texte(g, _date.Day.ToString(Libelles.Culture), Theme.PoliceGrasse(13.5f), new Rectangle((int)E(8), (int)E(6), l, (int)E(26)), Theme.Texte, centre: true);
            Dessin.Texte(g, _date.ToString("MMM", Libelles.Culture).ToUpperInvariant(), Theme.PoliceGrasse(7.5f), new Rectangle((int)E(8), (int)E(31), l, (int)E(16)), Theme.AccentClair, centre: true);
            var x = (int)E(58);
            Dessin.Texte(g, _titre, Theme.PoliceTitre(9.75f), new Rectangle(x, (int)E(8), Width - x - (int)E(8), (int)E(22)), Theme.Texte);
            Dessin.Texte(g, _detail, Theme.Police(8.25f), new Rectangle(x, (int)E(30), Width - x - (int)E(8), (int)E(18)), Theme.TexteSecondaireClair);
        }
    }

    // ================================================================ Panneau « Ajouter une intervention »

    /// <summary>
    /// Panneau en haut de la page : l'équipement (lien vers sa fiche), nature, intervenant (inspecteur pour une TIV),
    /// date, résultat et conséquence, Annuler / Enregistrer l'intervention.
    /// </summary>
    private sealed class PanneauIntervention : Carte
    {
        private readonly ServiceRevisions _revisions;
        private readonly Label _code = Ui.Libelle("", Theme.PoliceCode(10f), Theme.Accent);
        private readonly Label _nom = Ui.Libelle("", Theme.PoliceTitre(10.5f));
        private readonly Label _controle = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
        private readonly ListeDeroulante _nature = new() { AccessibleName = "Nature" };
        private readonly ListeDeroulante _inspecteur = new() { AccessibleName = "Inspecteur TIV" };
        private readonly ChampSaisie _intervenant = new() { TexteIndicatif = "Magasin, organisme, personne…" };
        private readonly Label _libelleIntervenant = Ui.LibelleChamp("Intervenant");
        private readonly ChampDate _date = new() { AccessibleName = "Date de l'intervention" };
        private readonly List<ChoixResultat> _choix = [];
        private readonly BandeauMessage _consequence = new() { Margin = Padding.Empty };
        private ResultatControle _resultat = ResultatControle.Conforme;
        private CarteRevision? _carte;

        public PanneauIntervention(ServiceRevisions revisions)
        {
            _revisions = revisions;
            Padding = new Padding(22);
            Rayon = 24;
            CouleurBordure = Theme.Sur(Theme.Surface, Theme.Accent, 0.50f);
            BackColor = Theme.Teinte(Theme.Accent, 0.06f);
            _intervenant.Champ.AccessibleName = "Intervenant";

            // Gauche : étape, titre, équipement, lien vers la fiche, aide.
            var etape = Ui.Libelle("ÉTAPE 3 · ENREGISTRER LE RÉSULTAT", Theme.PoliceGrasse(8.25f), Theme.AccentClair);
            var titre = Ui.Libelle("Ajouter une intervention", Theme.PoliceGrasse(15f));
            titre.Margin = new Padding(0, 2, 0, 10);
            _code.Margin = new Padding(0, 0, 0, 2);
            _nom.Margin = Padding.Empty;
            _controle.Margin = new Padding(0, 0, 0, 6);
            var fiche = new BoutonArrondi("Fiche ›", StyleBouton.Lien) { Height = Theme.HauteurCliquable, Margin = Padding.Empty };
            fiche.Click += (_, _) =>
            {
                if (_carte is { } c)
                {
                    FicheDemandee?.Invoke(this, c.Code);
                }
            };
            var aide = Ui.Libelle("L'intervention s'ajoute à l'historique de l'équipement.", Theme.Police(8.25f), Theme.TexteSecondaire);
            aide.MaximumSize = new Size(300, 0);
            var gauche = Ui.Pile(etape, titre, _code, _nom, _controle, fiche, aide);

            // Droite : nature / intervenant, date, résultat, conséquence, boutons.
            _nature.SelectedIndexChanged += (_, _) => MettreAJour();
            _date.Leave += (_, _) => MettreAJour();
            var hoteIntervenant = new Panel { BackColor = Color.Transparent, Margin = Padding.Empty };
            _inspecteur.Dock = _intervenant.Dock = DockStyle.Fill;
            hoteIntervenant.Controls.Add(_inspecteur);
            hoteIntervenant.Controls.Add(_intervenant);
            var champs = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 12), Height = 34 + Theme.HauteurChamp };
            for (var i = 0; i < 3; i++)
            {
                champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            }

            champs.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            champs.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
            Control[] valeurs = [_nature, hoteIntervenant, _date];
            Label[] libelles = [Ui.LibelleChamp("Nature"), _libelleIntervenant, Ui.LibelleChamp("Date")];
            for (var i = 0; i < 3; i++)
            {
                var m = new Padding(i == 0 ? 0 : 5, 0, i == 2 ? 0 : 5, 0);
                libelles[i].Margin = m;
                valeurs[i].Margin = m;
                valeurs[i].Dock = DockStyle.Fill;
                champs.Controls.Add(libelles[i], i, 0);
                champs.Controls.Add(valeurs[i], i, 1);
            }

            var resultats = new TableLayoutPanel { ColumnCount = 3, Height = Theme.HauteurCliquable, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 10) };
            foreach (var (resultat, i) in new[] { ResultatControle.Conforme, ResultatControle.ASurveiller, ResultatControle.Refuse }.Select((r, i) => (r, i)))
            {
                resultats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
                var bouton = new ChoixResultat(ServiceRevisions.LibelleResultat(resultat), CouleurResultat(resultat))
                {
                    Selectionne = resultat == _resultat,
                    Margin = new Padding(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0),
                    Dock = DockStyle.Fill,
                };
                bouton.Click += (_, _) =>
                {
                    _resultat = resultat;
                    foreach (var b in _choix)
                    {
                        b.Selectionne = b == bouton;
                    }

                    MettreAJour();
                };
                _choix.Add(bouton);
                resultats.Controls.Add(bouton, i, 0);
            }

            var annuler = new BoutonArrondi("Annuler", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(0, 0, 6, 0) };
            annuler.Click += (_, _) => Annule?.Invoke(this, EventArgs.Empty);
            Valider = new BoutonArrondi("Enregistrer l'intervention", StyleBouton.Principal) { AutoSize = false, Margin = new Padding(6, 0, 0, 0) };
            Valider.Click += async (_, _) => await EnregistrerAsync();
            var boutons = Ui.Colonnes((annuler, SizeType.Percent, 34), (Valider, SizeType.Percent, 66));

            var droite = Ui.Colonne(
                (champs, SizeType.Absolute, 34 + Theme.HauteurChamp + 12),
                (Ui.LibelleChamp("Résultat"), SizeType.AutoSize, 0),
                (resultats, SizeType.Absolute, Theme.HauteurCliquable + 10),
                (_consequence, SizeType.Absolute, 62),
                (boutons, SizeType.Absolute, Theme.HauteurBouton + 12));
            _consequence.Margin = new Padding(0, 0, 0, 12);

            var corps = Ui.Colonnes((gauche, SizeType.Percent, 34), (droite, SizeType.Percent, 66));
            gauche.Margin = new Padding(0, 0, 22, 0);
            Controls.Add(corps);
            AccessibleName = "Ajouter une intervention";
        }

        public BoutonArrondi Valider { get; }

        public event EventHandler? Annule;

        public event EventHandler? Enregistre;

        public event EventHandler<string>? FicheDemandee;

        public int HauteurPour(int largeur) => (int)Dessin.Echelle(this, 22 + 34 + Theme.HauteurChamp + 12 + 30 + Theme.HauteurCliquable + 10 + 62 + Theme.HauteurBouton + 12 + 22);

        public void PrendreFocus() => _nature.Focus();

        /// <summary>Remplit le panneau pour une carte : natures du contrôle, intervenant (prestataire de l'atelier, inspecteur pour une TIV).</summary>
        public void Preparer(CarteRevision carte, IReadOnlyList<Moniteur> inspecteurs)
        {
            _carte = carte;
            _code.Text = carte.Code;
            _nom.Text = carte.Libelle;
            _controle.Text = carte.Controle;

            // Bloc : TIV ou requalification ; autre matériel : les natures de révision.
            var natures = ServiceRevisions.EstBloc(carte.Categorie)
                ? new[] { "TIV", "Requalification" }.OrderBy(n => n == (carte.Type == TypeControle.Requalification ? "Requalification" : "TIV") ? 0 : 1).ToList()
                : ServiceRevisions.NaturesPour(TypeControle.Revision, carte.Categorie).ToList();
            _nature.Items.Clear();
            foreach (var n in natures)
            {
                _nature.Items.Add(n);
            }

            _inspecteur.Items.Clear();
            foreach (var m in inspecteurs)
            {
                _inspecteur.Items.Add(new Choix<long>(m.Id, m.NomComplet));
            }

            if (_inspecteur.Items.Count > 0)
            {
                _inspecteur.SelectedIndex = 0;
            }

            _intervenant.Valeur = carte.Prestataire ?? "";
            _date.Date = DateOnly.FromDateTime(DateTime.Today);
            _resultat = ResultatControle.Conforme;
            foreach (var b in _choix)
            {
                b.Selectionne = b == _choix[0];
            }

            _nature.SelectedIndex = 0;
            MettreAJour();
        }

        private TypeControle Type => (_nature.SelectedItem as string) switch
        {
            "TIV" => TypeControle.Tiv,
            "Requalification" => TypeControle.Requalification,
            _ => TypeControle.Revision,
        };

        private void MettreAJour()
        {
            if (_carte is null)
            {
                return;
            }

            var tiv = Type == TypeControle.Tiv;
            _inspecteur.Visible = tiv;
            _intervenant.Visible = !tiv;
            _libelleIntervenant.Text = tiv ? "Inspecteur TIV" : "Intervenant";
            var niveau = _resultat switch
            {
                ResultatControle.Conforme => NiveauResultat.Succes,
                ResultatControle.ASurveiller => NiveauResultat.Avertissement,
                _ => NiveauResultat.Erreur,
            };
            if (tiv && _inspecteur.Items.Count == 0)
            {
                _consequence.Afficher(NiveauResultat.Avertissement, "Aucun inspecteur TIV : cochez « Inspecteur TIV » sur la fiche d'un moniteur.");
                return;
            }

            var texte = _revisions.Consequence(Type, _resultat, _date.Date ?? DateOnly.FromDateTime(DateTime.Today), _carte.Categorie);
            if (_resultat == ResultatControle.Conforme)
            {
                texte = "Retour en service. " + texte;
            }

            _consequence.Afficher(niveau, texte);
        }

        private async Task EnregistrerAsync()
        {
            if (_carte is not { } c)
            {
                return;
            }

            if (_date.Date is not { } date)
            {
                _consequence.Afficher(NiveauResultat.Erreur, "Date invalide : écrivez-la comme 08/11/2026.");
                return;
            }

            var tiv = Type == TypeControle.Tiv;
            var inspecteur = tiv ? _inspecteur.SelectedItem as Choix<long> : null;
            Valider.Enabled = false;
            try
            {
                var resultat = await _revisions.EnregistrerInterventionAsync(new Intervention(
                    c.EquipementId, Type, _resultat, date, _nature.SelectedItem as string ?? "",
                    tiv ? inspecteur?.Texte ?? "" : _intervenant.Valeur, inspecteur?.Valeur, c.PassageAtelierId));
                if (!resultat.Reussi)
                {
                    _consequence.Afficher(NiveauResultat.Erreur, resultat.Message);
                    return;
                }

                Enregistre?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                if (!IsDisposed)
                {
                    Valider.Enabled = true;
                }
            }
        }
    }

    private static Color CouleurResultat(ResultatControle resultat) => resultat switch
    {
        ResultatControle.Conforme => Theme.Retour,
        ResultatControle.ASurveiller => Theme.Avertissement,
        _ => Theme.Erreur,
    };

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

    /// <summary>Note d'information (pictogramme i + texte), sous le tableau.</summary>
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
}
