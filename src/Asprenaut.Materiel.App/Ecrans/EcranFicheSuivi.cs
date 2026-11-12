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
/// Fiche de suivi EPI (maquette « Fiche de suivi EPI »), ouverte depuis l'inventaire. Paramètre de navigation : l'id de
/// l'équipement. En-tête (code, nom, statut, compteur, prochaine révision, Manuel, Imprimer la fiche), cartes des composants
/// (un détendeur est détaillé), puis toutes ses interventions, filtrables par composant, avec le formulaire « Ajouter une
/// intervention » de Révisions. Ajouter une intervention (Révisions) et modifier la fiche (Matériel) demandent le code.
/// </summary>
public sealed class EcranFicheSuivi : EcranBase
{
    /// <summary>Colonne « Type » des interventions : Contrôle, Atelier ou Signalement (aussi dans le PDF de la fiche).</summary>
    public static string LibelleType(TypeInterventionSuivie type) => type switch
    {
        TypeInterventionSuivie.Atelier => "Atelier",
        TypeInterventionSuivie.Signalement => "Signalement",
        _ => "Contrôle",
    };

    private readonly ServiceHistorique _historique;
    private readonly ServiceRevisions _revisions;
    private readonly ServiceCompteur _compteur;
    private readonly ServiceManuels _manuels;
    private readonly ServiceMoniteurs _moniteurs;
    private readonly ServiceInventaire _inventaire;
    private readonly ServiceReglages _reglages;
    private readonly INavigation _navigation;
    private readonly GardeGestion _garde;

    private readonly FilAriane _ariane;
    private readonly EnteteFiche _entete;
    private readonly Label _titreComposants = Ui.Libelle("Composants", Theme.PoliceTitre(15f));
    private readonly BoutonArrondi _modifier = new("+ Ajouter un composant", StyleBouton.Secondaire) { Margin = Padding.Empty };
    private readonly GrilleComposants _grille = new();
    private readonly BoutonArrondi _ajouter = new("Ajouter une intervention", StyleBouton.Principal, Icones.Plus) { Margin = Padding.Empty };
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Unite = "intervention" };
    private readonly PanneauAjout _panneau;
    private readonly TableauInterventions _tableau = new();
    private readonly PageFiche _page = new();

    private long _id;
    private FicheSuiviEquipement? _fiche;
    private long? _manuelId;
    private IReadOnlyList<Moniteur> _inspecteurs = [];

    public EcranFicheSuivi(
        ServiceHistorique historique,
        ServiceRevisions revisions,
        ServiceCompteur compteur,
        ServiceManuels manuels,
        ServiceMoniteurs moniteurs,
        ServiceInventaire inventaire,
        ServiceReglages reglages,
        INavigation navigation,
        GardeGestion garde)
    {
        _historique = historique;
        _revisions = revisions;
        _compteur = compteur;
        _manuels = manuels;
        _moniteurs = moniteurs;
        _inventaire = inventaire;
        _reglages = reglages;
        _navigation = navigation;
        _garde = garde;

        _ariane = new FilAriane("Inventaire", () => _navigation.AllerA<EcranInventaire>()) { Margin = Padding.Empty };
        _entete = new EnteteFiche();
        _entete.Manuel.Click += (_, _) =>
        {
            if (_manuelId is { } id)
            {
                _navigation.AllerA<EcranManuels>(id);
            }
        };
        _entete.Imprimer.Click += (_, _) => Executer(ImprimerAsync);

        _modifier.Click += (_, _) => Executer(ModifierFicheAsync);
        _garde.Lier(_modifier, ActionProtegee.Materiel);
        _ajouter.Click += (_, _) => OuvrirPanneau();
        _garde.Lier(_ajouter, ActionProtegee.Revisions);

        _filtre.Changement += (_, _) => AfficherInterventions();

        _panneau = new PanneauAjout(revisions) { Visible = false };
        _panneau.Annule += (_, _) => FermerPanneau();
        _panneau.Enregistre += (_, message) => Executer(async () =>
        {
            FermerPanneau();
            await RechargerAsync();
            Dialogues.Information(FindForm(), message);
        });
        _panneau.Valider.AfficherPastille = false;
        _garde.Lier(_panneau.Valider, ActionProtegee.Revisions);

        _titreComposants.Margin = Padding.Empty;
        var titreInterventions = Ui.Libelle("Interventions", Theme.PoliceTitre(15f));
        titreInterventions.Margin = Padding.Empty;
        var sectionComposants = new EnteteSection(_titreComposants, _modifier);
        var sectionInterventions = new EnteteSection(titreInterventions, _ajouter);

        _page.Ajouter(_ariane, _ => _ariane.PreferredSize.Height, 6);
        _page.Ajouter(_entete, _ => E(164), 22);
        _page.Ajouter(sectionComposants, _ => E(60), 8);
        _page.Ajouter(_grille, l => _grille.HauteurPour(l), 26);
        _page.Ajouter(sectionInterventions, _ => E(60), 8);
        _page.Ajouter(_filtre, _ => E(52), 12);
        _page.Ajouter(_panneau, l => _panneau.HauteurPour(), 16, largeurMax: 760);
        _page.Ajouter(_tableau, l => _tableau.HauteurPour(), 24);

        // Toute la fiche défile (même mécanique que Révisions : largeur moins la barre dès que la page dépasse).
        var defilement = new Panel { BackColor = Theme.Fond, Margin = Padding.Empty };
        defilement.HorizontalScroll.Maximum = 0;
        defilement.AutoScroll = false;
        defilement.VerticalScroll.Visible = false;
        defilement.AutoScroll = true;
        Natif.ScrollbarsDuTheme(defilement);
        defilement.Controls.Add(_page);
        void Ajuster()
        {
            var barre = _page.Height > defilement.Height ? SystemInformation.GetVerticalScrollBarWidthForDpi(defilement.DeviceDpi) : 0;
            var largeur = defilement.Width - barre - E(6);
            if (largeur > 0 && _page.Width != largeur)
            {
                _page.Width = largeur;
            }
        }

        defilement.Layout += (_, _) => Ajuster();
        defilement.ClientSizeChanged += (_, _) => Ajuster();
        _page.SizeChanged += (_, _) => Ajuster();
        Controls.Add(Ui.Colonne((defilement, SizeType.Percent, 100)));
    }

    public override string Titre => "Fiche de suivi";

    public override EntreeMenu Menu => EntreeMenu.Inventaire;

    private int E(float v) => (int)Dessin.Echelle(this, v);

    protected override async Task ChargerAsync(object? parametre)
    {
        if (parametre is not long id)
        {
            _navigation.AllerA<EcranInventaire>();
            return;
        }

        _id = id;
        _inspecteurs = await _moniteurs.ListerInspecteursTivAsync();
        await RechargerAsync();
    }

    private async Task RechargerAsync()
    {
        var fiche = await _historique.ObtenirFicheSuiviAsync(_id);
        if (IsDisposed)
        {
            return;
        }

        if (fiche is null)
        {
            Dialogues.Erreur(FindForm(), "Cet équipement n'existe plus.");
            _navigation.AllerA<EcranInventaire>();
            return;
        }

        var usage = await _compteur.UsageAsync(fiche.Vie.Equipement);
        var manuels = await _manuels.ManuelsDesEquipementsAsync();
        if (IsDisposed)
        {
            return;
        }

        _fiche = fiche;
        _manuelId = manuels.TryGetValue(_id, out var manuel) ? manuel : null;
        var e = fiche.Vie.Equipement;
        _ariane.Courant = $"Fiche de suivi EPI · {e.Code}";
        _entete.Afficher(fiche, usage, _manuelId is not null);
        _grille.Afficher(fiche.Composants);
        _titreComposants.Text = fiche.ParComposant || ServiceInventaire.EstDetendeur(fiche.Vie.Categorie) ? "Composants" : "Identification";
        _modifier.Text = PeutAjouterComposant(fiche) ? "+ Ajouter un composant" : "Modifier la fiche";
        _modifier.Visible = e.Etat != EtatEquipement.Reforme;
        _ajouter.Visible = e.Etat != EtatEquipement.Reforme;

        // Filtre par composant (détendeur détaillé) : « Tout », puis chaque composant avec ses interventions.
        _filtre.Visible = fiche.ParComposant;
        var roles = fiche.Composants.Select(c => c.Role).ToList();
        _filtre.CouleurPoint = role => roles.IndexOf(role) is var i and >= 0 ? GrilleComposants.Couleur(i) : null;
        _filtre.Afficher(fiche.Interventions.Count, roles.Select(r => (r, fiche.InterventionsDe(r).Count)));
        AfficherInterventions();
    }

    private void AfficherInterventions()
    {
        if (_fiche is not { } fiche)
        {
            return;
        }

        var selection = fiche.ParComposant ? _filtre.Selection : null;
        _tableau.Afficher(fiche.InterventionsDe(selection), fiche.Composants);
        _page.PerformLayout();
    }

    // ---------------------------------------------------------------- Actions

    private void OuvrirPanneau()
    {
        if (_fiche is not { } fiche || !_garde.Exiger(FindForm(), ActionProtegee.Revisions, "Ajouter une intervention"))
        {
            return;
        }

        var composant = fiche.ParComposant ? _filtre.Selection : null;
        _panneau.Preparer(fiche, _inspecteurs, composant);
        _panneau.Visible = true;
        _page.PerformLayout();
        _panneau.PrendreFocus();
    }

    private void FermerPanneau()
    {
        _panneau.Visible = false;
        _page.PerformLayout();
        _ajouter.Focus();
    }

    /// <summary>« + Ajouter un composant » / « Modifier la fiche » : la fiche matériel (deuxième étage, octopus...), puis on relit.</summary>
    private async Task ModifierFicheAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.Materiel, "Modifier un équipement"))
        {
            return;
        }

        if (await _inventaire.ObtenirAsync(_id) is not { } equipement)
        {
            Dialogues.Erreur(FindForm(), "Cet équipement n'existe plus.");
            return;
        }

        var change = _fiche is { } fiche && PeutAjouterComposant(fiche)
            ? await FormulaireComposant.AjouterAsync(FindForm(), _inventaire, _garde.Gestion, equipement, fiche.Titre)
            : await FormulaireEquipement.ModifierAsync(FindForm(), _inventaire, equipement, _fiche?.DerniereRevision);
        if (change && !IsDisposed)
        {
            await RechargerAsync();
        }
    }

    /// <summary>Détendeur auquel il manque un deuxième étage, un octopus ou un manomètre.</summary>
    private static bool PeutAjouterComposant(FicheSuiviEquipement fiche) =>
        ServiceInventaire.EstDetendeur(fiche.Vie.Categorie)
        && ServiceInventaire.Composition(fiche.Vie.Equipement).Any(c => !c.Present && ServiceInventaire.ComposantsAjoutables.Contains(c.Role));

    private async Task ImprimerAsync()
    {
        if (_fiche is not { } fiche)
        {
            return;
        }

        var r = _reglages.Actuels;
        await Fichiers.EnregistrerPdfAsync(this, "Enregistrer la fiche de suivi", $"Fiche de suivi {fiche.Vie.Equipement.Code}.pdf",
            chemin => Impression.FicheSuiviPdf.Enregistrer(fiche, r.NomClub, DateOnly.FromDateTime(DateTime.Today), chemin));
    }

    // ================================================================ Mise en page

    /// <summary>Page qui défile : chaque ligne prend toute la largeur (ou une largeur maximale) et la hauteur qu'elle demande.</summary>
    internal sealed class PageFiche : Panel
    {
        private readonly List<(Control Controle, Func<int, int> Hauteur, int Ecart, int LargeurMax)> _lignes = [];

        public PageFiche()
        {
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            DoubleBuffered = true;
        }

        public void Ajouter(Control controle, Func<int, int> hauteur, int ecart, int largeurMax = 0)
        {
            _lignes.Add((controle, hauteur, ecart, largeurMax));
            Controls.Add(controle);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_lignes.Count == 0)
            {
                return;
            }

            // Changer la hauteur peut faire apparaître la barre de défilement (largeur réduite) : on recommence.
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
            var y = 0;
            foreach (var (controle, hauteur, ecart, largeurMax) in _lignes)
            {
                if (!controle.Visible)
                {
                    continue;
                }

                var l = largeurMax > 0 ? Math.Min(largeur, (int)Dessin.Echelle(this, largeurMax)) : largeur;
                var h = hauteur(l);
                controle.Bounds = new Rectangle(0, y, l, h);
                y += h + (int)Dessin.Echelle(this, ecart);
            }

            return y;
        }
    }

    /// <summary>Titre de section à gauche, boutons à droite (place gardée au-dessus pour la pastille du cadenas).</summary>
    internal sealed class EnteteSection : Panel
    {
        private readonly Label _titre;
        private readonly Control[] _droite;

        public EnteteSection(Label titre, params Control[] droite)
        {
            _titre = titre;
            _droite = droite;
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            Controls.Add(titre);
            Controls.AddRange(droite);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var x = ClientSize.Width - (int)Dessin.Echelle(this, 10);
            foreach (var c in _droite.Where(c => c.Visible).Reverse())
            {
                var taille = c.GetPreferredSize(Size.Empty);
                var h = Math.Max(taille.Height, (int)Dessin.Echelle(this, Theme.HauteurCliquable));
                c.SetBounds(x - taille.Width, Height - h, taille.Width, h);
                x -= taille.Width + (int)Dessin.Echelle(this, 10);
            }

            var ht = _titre.PreferredHeight;
            _titre.SetBounds(0, Height - (int)Dessin.Echelle(this, 22) - (ht / 2), Math.Max(0, x), ht);
        }
    }

    // ================================================================ En-tête

    /// <summary>
    /// Carte d'en-tête : tuile « EPI » avec le code, « Fiche de suivi EPI · catégorie », nom de l'équipement, pastilles
    /// (statut, compteur d'utilisation, prochaine révision) ; à droite, les boutons Manuel et Imprimer la fiche.
    /// </summary>
    private sealed class EnteteFiche : Carte
    {
        private FicheSuiviEquipement? _fiche;
        private UsageEquipement? _usage;

        public EnteteFiche()
        {
            Rayon = 26;
            BackColor = Theme.Teinte(Theme.Accent, 0.10f);
            CouleurBordure = Theme.Sur(Theme.Surface, Theme.AccentClair, 0.24f);
            Manuel = new BoutonArrondi("Manuel", StyleBouton.Secondaire, Icones.Livre) { Margin = Padding.Empty, Visible = false };
            Imprimer = new BoutonArrondi("Imprimer la fiche", StyleBouton.Secondaire, Icones.Imprimante) { Margin = Padding.Empty };
            Controls.Add(Manuel);
            Controls.Add(Imprimer);
            AccessibleRole = AccessibleRole.Grouping;
        }

        public BoutonArrondi Manuel { get; }

        public BoutonArrondi Imprimer { get; }

        private float E(float v) => Dessin.Echelle(this, v);

        public void Afficher(FicheSuiviEquipement fiche, UsageEquipement? usage, bool avecManuel)
        {
            _fiche = fiche;
            _usage = usage;
            Manuel.Visible = avecManuel;
            AccessibleName = $"Fiche de suivi {fiche.Vie.Equipement.Code}, {fiche.Titre}. {Pastilles().Select(p => p.Texte).Aggregate((a, b) => $"{a}. {b}")}.";
            PerformLayout();
            Invalidate();
        }

        private int GaucheBoutons { get; set; }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Imprimer is null)
            {
                return;
            }

            var x = Width - (int)E(26);
            foreach (var b in new Control[] { Imprimer, Manuel }.Where(b => b.Visible))
            {
                var taille = b.GetPreferredSize(Size.Empty);
                var h = (int)E(48);
                b.SetBounds(x - taille.Width, (Height - h) / 2, taille.Width, h);
                x -= taille.Width + (int)E(10);
            }

            GaucheBoutons = x;
        }

        private IEnumerable<(string Texte, Color Fond, Color Couleur)> Pastilles()
        {
            if (_fiche is not { } f)
            {
                yield break;
            }

            var vie = f.Vie;
            var statut = FicheVie.CouleurStatut(vie.Statut);
            var precision = vie.PrecisionStatut.Trim();
            yield return (precision.Length > 0 ? $"{vie.LibelleStatut} · {precision}" : vie.LibelleStatut, Theme.Sur(BackColor, statut, 0.16f), statut);

            if (_usage is { } u)
            {
                var couleur = u.Etat switch
                {
                    EtatCompteur.Atteint => Theme.Erreur,
                    EtatCompteur.Proche => Theme.Avertissement,
                    _ => Theme.TexteDoux,
                };
                yield return ($"{u.Utilisations} / {u.Seuil} utilisations depuis le dernier contrôle",
                    u.Etat == EtatCompteur.Normal ? Theme.Sur(BackColor, Theme.Fond, 0.45f) : Theme.Sur(BackColor, couleur, 0.16f), couleur);
            }

            var echeance = vie.EtatEcheance == EtatRevision.Depassee ? Theme.Erreur : Theme.Avertissement;
            yield return (vie.Echeance == "—" ? "Pas de révision prévue" : $"Prochaine révision : {vie.Echeance}",
                Theme.Sur(BackColor, echeance, 0.14f), echeance);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_fiche is not { } f)
            {
                return;
            }

            var g = e.Graphics;
            Dessin.Qualite(g);
            var equipement = f.Vie.Equipement;

            // Tuile « EPI » + code, police réduite si le code est long (BLOC-031).
            var cote = E(112);
            var tuile = new RectangleF(E(26), (Height - cote) / 2, cote, cote);
            using (var chemin = Dessin.Arrondi(tuile, E(24)))
            using (var fond = new SolidBrush(Theme.Sur(BackColor, Theme.Fond, 0.55f)))
            using (var bord = new Pen(Theme.Sur(BackColor, Theme.AccentClair, 0.30f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            Dessin.Texte(g, "EPI", Theme.PoliceGrasse(7.5f), Rectangle.Round(new RectangleF(tuile.X, tuile.Y + E(28), tuile.Width, E(16))), Theme.TexteSecondaire, centre: true);
            var taille = 24f;
            while (taille > 12f && TextRenderer.MeasureText(equipement.Code, Theme.PoliceGrasse(taille), Size.Empty, TextFormatFlags.NoPadding).Width > tuile.Width - E(16))
            {
                taille -= 1.5f;
            }

            Dessin.Texte(g, equipement.Code, Theme.PoliceGrasse(taille), Rectangle.Round(new RectangleF(tuile.X, tuile.Y + E(44), tuile.Width, E(44))), Theme.Texte, centre: true);

            // Textes et pastilles, jusqu'aux boutons.
            var x = (int)(tuile.Right + E(24));
            var droite = Math.Max(x + (int)E(120), GaucheBoutons - (int)E(16));
            var haut = (int)((Height / 2f) - E(54));
            var categorie = f.Vie.Categorie.Length > 0 ? f.Vie.Categorie : "Équipement";
            Dessin.Texte(g, $"FICHE DE SUIVI EPI · {categorie.ToUpper(Libelles.Culture)}", Theme.PoliceGrasse(8.25f), new Rectangle(x, haut, droite - x, (int)E(20)), Theme.AccentClair);
            Dessin.Texte(g, f.Titre, Theme.PoliceGrasse(18f), new Rectangle(x, haut + (int)E(22), droite - x, (int)E(44)), Theme.Texte);

            var px = (float)x;
            var py = haut + E(76);
            var police = Theme.PoliceTitre(8.5f);
            foreach (var (texte, fond, couleur) in Pastilles())
            {
                var l = TextRenderer.MeasureText(texte, police).Width + E(18);
                l = Math.Min(l, droite - px);
                if (l < E(60))
                {
                    break;
                }

                var r = new RectangleF(px, py, l, E(28));
                using (var chemin = Dessin.Arrondi(r, r.Height / 2))
                using (var pinceau = new SolidBrush(fond))
                {
                    g.FillPath(pinceau, chemin);
                }

                Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
                px += l + E(8);
            }
        }
    }

    // ================================================================ Composants

    /// <summary>
    /// Cartes des composants, en colonnes d'au moins 300 px : point de couleur, rôle, modèle, numéro de série,
    /// puis les tuiles (ajouté le, dernier contrôle, prochaine révision, état) sur deux colonnes.
    /// </summary>
    private sealed class GrilleComposants : ControleDessine
    {
        private const int LargeurMin = 300;
        private const int Ecart = 14;
        private const int HauteurTuile = 54;
        private IReadOnlyList<ComposantSuivi> _composants = [];

        public GrilleComposants()
        {
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Composants";
        }

        /// <summary>Couleur du composant n (point de la carte, du filtre et du tableau).</summary>
        public static Color Couleur(int index) => (index % 4) switch
        {
            0 => Theme.Accent,
            1 => Theme.Depart,
            2 => Theme.Avertissement,
            _ => Theme.Materiel,
        };

        public void Afficher(IReadOnlyList<ComposantSuivi> composants)
        {
            _composants = composants;
            AccessibleDescription = string.Join(". ", composants.Select(c =>
                $"{c.Role} {c.Modele}, n° de série {(c.NumeroSerie.Length > 0 ? c.NumeroSerie : "non renseigné")}, "
                + string.Join(", ", c.Tuiles.Select(t => $"{t.Libelle} {t.Valeur}"))));
            Invalidate();
        }

        private int Colonnes(int largeur) =>
            Math.Max(1, Math.Min(Math.Max(1, _composants.Count), (int)((largeur + E(Ecart)) / E(LargeurMin + Ecart))));

        private float HauteurCarte()
        {
            var lignes = (int)Math.Ceiling(Math.Max(1, _composants.Count == 0 ? 0 : _composants.Max(c => c.Tuiles.Count)) / 2.0);
            return E(98) + (lignes * E(HauteurTuile)) + ((lignes - 1) * E(8)) + E(18);
        }

        public int HauteurPour(int largeur)
        {
            if (_composants.Count == 0)
            {
                return 1;
            }

            var rangees = (int)Math.Ceiling(_composants.Count / (double)Colonnes(largeur));
            return (int)((rangees * HauteurCarte()) + ((rangees - 1) * E(Ecart))) + 2;
        }

        protected override void Peindre(Graphics g)
        {
            if (_composants.Count == 0)
            {
                return;
            }

            var n = Colonnes(Width);
            var l = (Width - 1 - ((n - 1) * E(Ecart))) / n;
            var h = HauteurCarte();
            for (var i = 0; i < _composants.Count; i++)
            {
                var r = new RectangleF((i % n) * (l + E(Ecart)), (i / n) * (h + E(Ecart)), l, h);
                PeindreCarte(g, _composants[i], Couleur(i), r);
            }
        }

        private void PeindreCarte(Graphics g, ComposantSuivi c, Color couleur, RectangleF r)
        {
            using (var chemin = Dessin.Arrondi(r, E(22)))
            using (var fond = new SolidBrush(Theme.Surface))
            using (var bord = new Pen(Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            // Point coloré avec halo.
            var cx = r.X + E(24);
            var cy = r.Y + E(28);
            using (var halo = new SolidBrush(Theme.Sur(Theme.Surface, couleur, 0.25f)))
            {
                g.FillEllipse(halo, cx - E(10), cy - E(10), E(20), E(20));
            }

            using (var point = new SolidBrush(couleur))
            {
                g.FillEllipse(point, cx - E(6), cy - E(6), E(12), E(12));
            }

            var x = (int)(r.X + E(44));
            var largeur = (int)(r.Right - E(18)) - x;
            Dessin.Texte(g, c.Role.ToUpper(Libelles.Culture), Theme.PoliceGrasse(8.25f), new Rectangle(x, (int)(r.Y + E(16)), largeur, (int)E(20)), Theme.TexteSecondaire);
            Dessin.Texte(g, c.Modele, Theme.PoliceGrasse(14f), new Rectangle(x, (int)(r.Y + E(36)), largeur, (int)E(30)), Theme.Texte);

            var prefixe = "N° de série ";
            var policeTexte = Theme.Police(9.75f);
            var lp = TextRenderer.MeasureText(g, prefixe, policeTexte).Width;
            var ys = (int)(r.Y + E(66));
            Dessin.Texte(g, prefixe, policeTexte, new Rectangle(x, ys, lp, (int)E(20)), Theme.TexteSecondaireClair);
            Dessin.Texte(g, c.NumeroSerie.Length > 0 ? c.NumeroSerie : "non renseigné", c.NumeroSerie.Length > 0 ? Theme.PoliceCode(9.75f) : policeTexte,
                new Rectangle(x + lp - (int)E(6), ys, Math.Max(0, largeur - lp + (int)E(6)), (int)E(20)), c.NumeroSerie.Length > 0 ? Theme.Accent : Theme.TexteSecondaire);

            // Tuiles sur deux colonnes.
            var ecart = E(8);
            var lt = (r.Width - E(36) - ecart) / 2;
            for (var i = 0; i < c.Tuiles.Count; i++)
            {
                var t = c.Tuiles[i];
                var rt = new RectangleF(r.X + E(18) + ((i % 2) * (lt + ecart)), r.Y + E(98) + ((i / 2) * (E(HauteurTuile) + ecart)), lt, E(HauteurTuile));
                using (var chemin = Dessin.Arrondi(rt, E(12)))
                using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Fond, 0.55f)))
                {
                    g.FillPath(fond, chemin);
                }

                var xt = (int)(rt.X + E(11));
                var lt2 = (int)(rt.Width - E(18));
                Dessin.Texte(g, t.Libelle, Theme.Police(8.25f), new Rectangle(xt, (int)(rt.Y + E(7)), lt2, (int)E(18)), Theme.TexteSecondaire);
                Dessin.Texte(g, t.Valeur, Theme.PoliceGrasse(10.5f), new Rectangle(xt, (int)(rt.Y + E(25)), lt2, (int)E(22)),
                    t.Valeur is "—" or "" ? Theme.TexteSecondaire : Theme.Texte);
            }
        }
    }

    // ================================================================ Interventions

    /// <summary>Tableau des interventions : date, composant, nature, intervenant, type (Contrôle / Atelier), résultat.</summary>
    private sealed class TableauInterventions : ControleDessine
    {
        private const int HauteurEntete = 46;
        private const int HauteurLigne = 50;
        private static readonly float[] Positions = [0, 0.12f, 0.33f, 0.62f, 0.77f, 0.88f];
        private static readonly string[] Titres = ["Date", "Composant", "Nature de l'intervention", "Intervenant", "Type", "Résultat"];
        private IReadOnlyList<InterventionSuivie> _lignes = [];
        private IReadOnlyList<ComposantSuivi> _composants = [];

        public TableauInterventions()
        {
            AccessibleRole = AccessibleRole.Table;
            AccessibleName = "Interventions";
        }

        public void Afficher(IReadOnlyList<InterventionSuivie> lignes, IReadOnlyList<ComposantSuivi> composants)
        {
            _lignes = lignes;
            _composants = composants;
            AccessibleDescription = lignes.Count == 0 ? "Aucune intervention enregistrée."
                : string.Join(". ", lignes.Select(l => $"{Libelles.Date(l.Date)}, {l.Composant}, {l.Nature}, {l.Intervenant}, {LibelleType(l.Type).ToLower(Libelles.Culture)}, {l.Resultat}"));
            Invalidate();
        }

        public int HauteurPour() => (int)E(HauteurEntete + (Math.Max(1, _lignes.Count) * HauteurLigne) + 8);

        protected override void Peindre(Graphics g)
        {
            var cadre = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var chemin = Dessin.Arrondi(cadre, E(22)))
            using (var fond = new SolidBrush(Theme.Surface))
            using (var bord = new Pen(Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var x0 = E(20);
            var l = Width - E(40);
            int Colonne(int i) => (int)(x0 + (l * Positions[i]));
            int Largeur(int i) => (int)(l * ((i + 1 < Positions.Length ? Positions[i + 1] : 1f) - Positions[i])) - (int)E(10);

            for (var i = 0; i < Titres.Length; i++)
            {
                Dessin.Texte(g, Titres[i].ToUpper(Libelles.Culture), Theme.PoliceGrasse(7.5f), new Rectangle(Colonne(i), 0, Largeur(i), (int)E(HauteurEntete)), Theme.TexteSecondaire);
            }

            if (_lignes.Count == 0)
            {
                Dessin.Texte(g, "Aucune intervention enregistrée.", Theme.Police(10f), new Rectangle((int)x0, (int)E(HauteurEntete), (int)l, (int)E(HauteurLigne)), Theme.TexteSecondaire);
                return;
            }

            using var filet = new Pen(Theme.Bordure, E(1));
            for (var n = 0; n < _lignes.Count; n++)
            {
                var ligne = _lignes[n];
                var y = (int)E(HauteurEntete + (n * HauteurLigne));
                var h = (int)E(HauteurLigne);
                g.DrawLine(filet, E(1), y, Width - E(2), y);

                Dessin.Texte(g, Libelles.Date(ligne.Date), Theme.Police(9.75f), new Rectangle(Colonne(0), y, Largeur(0), h), Theme.TexteDoux);

                // Composant : point de sa couleur, rôle et modèle.
                var index = _composants.ToList().FindIndex(c => c.Role == ligne.Composant);
                var couleur = index >= 0 && _composants.Count > 1 ? GrilleComposants.Couleur(index) : Theme.TexteSecondaire;
                var d = E(8);
                using (var point = new SolidBrush(couleur))
                {
                    g.FillEllipse(point, Colonne(1), y + ((h - d) / 2), d, d);
                }

                var texteComposant = index >= 0 && _composants[index].Modele != "—" && _composants.Count > 1
                    ? $"{ligne.Composant} {_composants[index].Modele}" : ligne.Composant;
                Dessin.Texte(g, texteComposant, Theme.Police(9f), new Rectangle(Colonne(1) + (int)E(16), y, Largeur(1) - (int)E(16), h), Theme.TexteDoux);
                Dessin.Texte(g, ligne.Nature, Theme.PoliceTitre(9.75f), new Rectangle(Colonne(2), y, Largeur(2), h), Theme.Texte);
                Dessin.Texte(g, ligne.Intervenant, Theme.Police(9.75f), new Rectangle(Colonne(3), y, Largeur(3), h), Theme.TexteSecondaireClair);

                var type = ligne.Type switch
                {
                    TypeInterventionSuivie.Atelier => Theme.Materiel,
                    TypeInterventionSuivie.Signalement => Theme.Avertissement,
                    _ => Theme.AccentClair,
                };
                Pilule(g, LibelleType(ligne.Type), Colonne(4), y, h, Largeur(4), type);

                var resultat = ligne.ResultatBrut is { } brut ? ChoixResultat.Couleur(brut)
                    : ligne.Resultat == "En atelier" ? Theme.Materiel : Theme.TexteSecondaireClair;
                Pilule(g, ligne.Resultat, Colonne(5), y, h, Largeur(5) + (int)E(10), resultat);
            }
        }

        private void Pilule(Graphics g, string texte, int x, int y, int h, int largeurMax, Color couleur)
        {
            var police = Theme.PoliceTitre(8.25f);
            var l = Math.Min(largeurMax, TextRenderer.MeasureText(g, texte, police).Width + E(14));
            var r = new RectangleF(x, y + ((h - E(26)) / 2), l, E(26));
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, couleur, 0.15f)))
            {
                g.FillPath(fond, chemin);
            }

            Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
        }
    }

    // ================================================================ Panneau « Ajouter une intervention »

    /// <summary>
    /// Même formulaire que dans Révisions, avec le composant visé et des détails : composant, nature, résultat,
    /// intervenant (inspecteur pour une TIV), date, détails, conséquence, Annuler / Enregistrer l'intervention.
    /// </summary>
    private sealed class PanneauAjout : Carte
    {
        private readonly ServiceRevisions _revisions;
        private readonly Label _titre = Ui.Libelle("", Theme.PoliceTitre(12.75f));
        private readonly Label _sousTitre = Ui.Libelle("Même formulaire que dans Révisions", Theme.Police(9f), Theme.TexteSecondaire);
        private readonly Label _libComposant = Ui.LibelleChamp("Composant");
        private readonly Label _libNature = Ui.LibelleChamp("Nature");
        private readonly Label _libResultat = Ui.LibelleChamp("Résultat");
        private readonly Label _libIntervenant = Ui.LibelleChamp("Intervenant");
        private readonly Label _libDate = Ui.LibelleChamp("Date");
        private readonly Label _libDetails = Ui.LibelleChamp("Détails");
        private readonly ListeDeroulante _composant = new() { AccessibleName = "Composant" };
        private readonly ListeDeroulante _nature = new() { AccessibleName = "Nature" };
        private readonly ListeDeroulante _inspecteur = new() { AccessibleName = "Inspecteur TIV" };
        private readonly ChampSaisie _intervenant = new() { TexteIndicatif = "Nom, TIV ou atelier" };
        private readonly ChampDate _date = new() { AccessibleName = "Date de l'intervention" };
        private readonly ChampSaisie _details = new() { TexteIndicatif = "RAS, changement flexible MP…" };
        private readonly List<(ChoixResultat Bouton, ResultatControle Resultat)> _choix = [];
        private readonly BandeauMessage _consequence = new();
        private readonly BoutonArrondi _annuler = new("Annuler", StyleBouton.Secondaire) { AutoSize = false, Margin = Padding.Empty };
        private ResultatControle _resultat = ResultatControle.Conforme;
        private FicheSuiviEquipement? _fiche;

        public PanneauAjout(ServiceRevisions revisions)
        {
            _revisions = revisions;
            Padding = new Padding(22);
            Rayon = 22;
            CouleurBordure = Theme.Sur(Theme.Surface, Theme.Accent, 0.50f);
            BackColor = Theme.Teinte(Theme.Accent, 0.06f);
            _intervenant.Champ.AccessibleName = "Intervenant";
            _details.Champ.AccessibleName = "Détails";
            AccessibleName = "Ajouter une intervention";

            foreach (var resultat in new[] { ResultatControle.Conforme, ResultatControle.ASurveiller, ResultatControle.Refuse })
            {
                var bouton = new ChoixResultat(ServiceRevisions.LibelleResultat(resultat), ChoixResultat.Couleur(resultat)) { Margin = Padding.Empty };
                bouton.Click += (_, _) =>
                {
                    _resultat = resultat;
                    foreach (var (b, _) in _choix)
                    {
                        b.Selectionne = b == bouton;
                    }

                    MettreAJour();
                };
                _choix.Add((bouton, resultat));
            }

            _nature.SelectedIndexChanged += (_, _) => MettreAJour();
            _composant.SelectedIndexChanged += (_, _) => MettreAJour();
            _date.Leave += (_, _) => MettreAJour();
            _annuler.Click += (_, _) => Annule?.Invoke(this, EventArgs.Empty);
            Valider = new BoutonArrondi("Enregistrer l'intervention", StyleBouton.Principal) { AutoSize = false, Margin = Padding.Empty };
            Valider.Click += async (_, _) => await EnregistrerAsync();

            Controls.AddRange([_titre, _sousTitre, _libComposant, _composant, _libNature, _nature, _libResultat]);
            Controls.AddRange(_choix.Select(c => (Control)c.Bouton).ToArray());
            Controls.AddRange([_libIntervenant, _inspecteur, _intervenant, _libDate, _date, _libDetails, _details, _consequence, _annuler, Valider]);
        }

        public BoutonArrondi Valider { get; }

        public event EventHandler? Annule;

        /// <summary>Intervention enregistrée (message du service).</summary>
        public event EventHandler<string>? Enregistre;

        private int E(float v) => (int)Dessin.Echelle(this, v);

        public void PrendreFocus() => _composant.Focus();

        /// <summary>Remplit le panneau : composants de l'équipement, natures (TIV / requalification pour un bloc), inspecteurs.</summary>
        public void Preparer(FicheSuiviEquipement fiche, IReadOnlyList<Moniteur> inspecteurs, string? composant)
        {
            _fiche = fiche;
            var e = fiche.Vie.Equipement;
            _titre.Text = $"Ajouter une intervention · {e.Code}";

            _composant.Items.Clear();
            _composant.Items.Add(ServiceInventaire.ComposantComplet);
            foreach (var c in fiche.Composants.Where(c => c.Role != ServiceInventaire.ComposantComplet))
            {
                _composant.Items.Add(c.Role);
            }

            _composant.SelectedIndex = composant is null ? 0 : Math.Max(0, _composant.Items.IndexOf(composant));
            _composant.Enabled = _composant.Items.Count > 1;

            IReadOnlyList<string> natures = ServiceRevisions.EstBloc(fiche.Vie.Categorie)
                ? ["TIV", "Requalification"]
                : ServiceRevisions.NaturesPour(TypeControle.Revision, fiche.Vie.Categorie);
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

            _intervenant.Valeur = "";
            _details.Valeur = "";
            _date.Date = DateOnly.FromDateTime(DateTime.Today);
            _resultat = ResultatControle.Conforme;
            foreach (var (b, r) in _choix)
            {
                b.Selectionne = r == _resultat;
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
            if (_fiche is not { } fiche)
            {
                return;
            }

            var tiv = Type == TypeControle.Tiv;
            _inspecteur.Visible = tiv;
            _intervenant.Visible = !tiv;
            _libIntervenant.Text = tiv ? "Inspecteur TIV" : "Intervenant";
            if (tiv && _inspecteur.Items.Count == 0)
            {
                _consequence.Afficher(NiveauResultat.Avertissement, "Aucun inspecteur TIV : cochez « Inspecteur TIV » sur la fiche d'un moniteur.");
                return;
            }

            var niveau = _resultat switch
            {
                ResultatControle.Conforme => NiveauResultat.Succes,
                ResultatControle.ASurveiller => NiveauResultat.Avertissement,
                _ => NiveauResultat.Erreur,
            };
            var surUnComposant = _composant.SelectedItem is string c && c != ServiceInventaire.ComposantComplet;
            _consequence.Afficher(niveau, _revisions.Consequence(Type, _resultat, _date.Date ?? DateOnly.FromDateTime(DateTime.Today), fiche.Vie.Categorie, surUnComposant));
        }

        private async Task EnregistrerAsync()
        {
            if (_fiche is not { } fiche)
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
                    fiche.Vie.Equipement.Id, Type, _resultat, date, _nature.SelectedItem as string ?? "",
                    tiv ? inspecteur?.Texte ?? "" : _intervenant.Valeur, inspecteur?.Valeur,
                    Composant: _composant.SelectedItem as string ?? "", Notes: _details.Valeur));
                if (!resultat.Reussi)
                {
                    _consequence.Afficher(NiveauResultat.Erreur, resultat.Message);
                    return;
                }

                Enregistre?.Invoke(this, resultat.Message);
            }
            finally
            {
                if (!IsDisposed)
                {
                    Valider.Enabled = true;
                }
            }
        }

        // Hauteurs (pixels logiques) : titre, deux champs, résultat, deux champs, détails, conséquence, boutons.
        private const int HTitre = 52, HLibelle = 28, HEcart = 14, HConsequence = 62;

        public int HauteurPour() => E(22 + HTitre + (3 * (HLibelle + Theme.HauteurChamp + HEcart)) + HLibelle + Theme.HauteurCliquable + HEcart
            + HConsequence + HEcart + Theme.HauteurBouton + 22);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_choix.Count == 0 || Valider is null)
            {
                return;
            }

            var x = E(22);
            var l = Width - (2 * x);
            var demi = (l - E(10)) / 2;
            var y = E(22);
            _titre.SetBounds(x, y, l, _titre.PreferredHeight);
            _sousTitre.SetBounds(x, y + _titre.PreferredHeight + E(2), l, _sousTitre.PreferredHeight);
            y += E(HTitre);

            void Paire(Label lg, Control g, Label ld, Control d)
            {
                lg.SetBounds(x, y, demi, E(HLibelle) - E(6));
                g.SetBounds(x, y + E(HLibelle), demi, E(Theme.HauteurChamp));
                ld.SetBounds(x + demi + E(10), y, demi, E(HLibelle) - E(6));
                d.SetBounds(x + demi + E(10), y + E(HLibelle), demi, E(Theme.HauteurChamp));
                y += E(HLibelle + Theme.HauteurChamp + HEcart);
            }

            Paire(_libComposant, _composant, _libNature, _nature);

            _libResultat.SetBounds(x, y, l, E(HLibelle) - E(6));
            y += E(HLibelle);
            var tiers = (l - E(16)) / 3;
            for (var i = 0; i < _choix.Count; i++)
            {
                _choix[i].Bouton.SetBounds(x + (i * (tiers + E(8))), y, tiers, E(Theme.HauteurCliquable));
            }

            y += E(Theme.HauteurCliquable + HEcart);
            Paire(_libIntervenant, _intervenant, _libDate, _date);
            _inspecteur.Bounds = _intervenant.Bounds;

            _libDetails.SetBounds(x, y, l, E(HLibelle) - E(6));
            _details.SetBounds(x, y + E(HLibelle), l, E(Theme.HauteurChamp));
            y += E(HLibelle + Theme.HauteurChamp + HEcart);

            _consequence.SetBounds(x, y, l, E(HConsequence));
            y += E(HConsequence + HEcart);
            var la = (l - E(10)) / 3;
            _annuler.SetBounds(x, y, la, E(Theme.HauteurBouton));
            Valider.SetBounds(x + la + E(10), y, l - la - E(10), E(Theme.HauteurBouton));
        }
    }
}
