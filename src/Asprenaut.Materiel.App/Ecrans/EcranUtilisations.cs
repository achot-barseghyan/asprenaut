using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Utilisations d'un équipement (maquette « Utilisations d'un équipement »), ouverte par « Voir les sorties » de la
/// colonne Utilisation de l'inventaire. Paramètre de navigation : l'id de l'équipement. Quatre chiffres (compteur avant
/// révision, sorties depuis le contrôle, total, problèmes), filtre par type de sortie, puis le tableau de ses sorties
/// avec le compteur après chaque retour ; les contrôles qui l'ont remis à zéro sont intercalés. Consultation libre.
/// </summary>
public sealed class EcranUtilisations : EcranBase
{
    /// <summary>Sorties montrées d'abord, puis par paquets.</summary>
    private const int PremieresSorties = 12;
    private const int Paquet = 50;

    private readonly ServiceHistorique _historique;
    private readonly INavigation _navigation;

    private readonly FilAriane _ariane;
    private readonly EnteteUtilisations _entete = new();
    private readonly ChiffresUtilisations _chiffres = new();
    private readonly FiltreCategories _filtre = new(Theme.AccentClair) { Unite = "sortie" };
    private readonly TableauUtilisations _tableau = new();
    private readonly LigneCentree _suite;
    private readonly EcranFicheSuivi.PageFiche _page = new();

    private long _id;
    private UtilisationsEquipement? _donnees;
    private int _limite = PremieresSorties;

    public EcranUtilisations(ServiceHistorique historique, INavigation navigation)
    {
        _historique = historique;
        _navigation = navigation;

        _ariane = new FilAriane("Inventaire", () => _navigation.AllerA<EcranInventaire>()) { Margin = Padding.Empty };
        _entete.Fiche.Click += (_, _) => _navigation.AllerA<EcranFicheSuivi>(_id);
        _entete.Exporter.Click += (_, _) => Executer(ExporterAsync);
        _filtre.Changement += (_, _) =>
        {
            _limite = PremieresSorties;
            AfficherLignes();
        };

        var plus = new BoutonArrondi("Afficher les sorties plus anciennes", StyleBouton.Secondaire) { Margin = Padding.Empty };
        plus.Click += (_, _) =>
        {
            _limite += Paquet;
            AfficherLignes();
        };
        _suite = new LigneCentree(plus) { Visible = false };

        _page.Ajouter(_ariane, _ => _ariane.PreferredSize.Height, 2);
        _page.Ajouter(_entete, _ => E(76), 22);
        _page.Ajouter(_chiffres, l => _chiffres.HauteurPour(l), 22);
        _page.Ajouter(_filtre, _ => E(52), 14);
        _page.Ajouter(_tableau, _ => _tableau.HauteurPour(), 14);
        _page.Ajouter(_suite, _ => Math.Max(E(Theme.HauteurBouton), _suite.Bouton.GetPreferredSize(Size.Empty).Height) + E(4), 24);

        // Toute la page défile (même mécanique que la fiche de suivi).
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

    public override string Titre => "Utilisations";

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
        var donnees = await _historique.ObtenirUtilisationsAsync(id);
        if (IsDisposed)
        {
            return;
        }

        if (donnees is null)
        {
            Dialogues.Erreur(FindForm(), "Cet équipement n'existe plus.");
            _navigation.AllerA<EcranInventaire>();
            return;
        }

        _donnees = donnees;
        _ariane.Courant = $"Utilisations · {donnees.Equipement.Code}";
        _entete.Afficher(donnees);
        _chiffres.Afficher(donnees);

        var types = donnees.Types;
        _filtre.CouleurPoint = nom => types.FirstOrDefault(t => t.Type == nom) is { Type: not null } t ? Theme.CouleurTypeSortie(t.Famille) : null;
        _filtre.Afficher(donnees.Sorties.Count(), types.Select(t => (t.Type, t.Nombre)));
        _filtre.Visible = types.Count > 1;
        AfficherLignes();
    }

    /// <summary>Les sorties du type choisi, les premières seulement ; le bouton du bas montre les suivantes.</summary>
    private void AfficherLignes()
    {
        if (_donnees is not { } d)
        {
            return;
        }

        var lignes = d.LignesDe(_filtre.Selection);
        var montrees = new List<LigneUtilisation>();
        var sorties = 0;
        foreach (var l in lignes)
        {
            if (!l.EstControle && sorties == _limite)
            {
                break;
            }

            sorties += l.EstControle ? 0 : 1;
            montrees.Add(l);
        }

        var reste = lignes.Count(l => !l.EstControle) - sorties;
        _tableau.Afficher(montrees, d.Lignes.Count == 0);
        _suite.Visible = reste > 0;
        _suite.Bouton.Text = reste == 1 ? "Afficher la sortie plus ancienne"
            : reste <= Paquet ? $"Afficher les {reste} sorties plus anciennes"
            : $"Afficher {Paquet} sorties plus anciennes (encore {reste})";
        _suite.PerformLayout();
        _page.PerformLayout();
    }

    /// <summary>Les lignes du type choisi (toutes, pas seulement les premières), pour Excel.</summary>
    private async Task ExporterAsync()
    {
        if (_donnees is not { } d)
        {
            return;
        }

        var lignes = d.LignesDe(_filtre.Selection);
        if (lignes.Count == 0)
        {
            Dialogues.Information(FindForm(), "Rien à exporter : cet équipement n'est encore jamais sorti.");
            return;
        }

        if (await Fichiers.EnregistrerCsvAsync(FindForm(), "Exporter les utilisations", _historique.NomFichierUtilisations(d.Equipement.Code),
            ServiceHistorique.ExporterUtilisationsCsv(d, lignes)))
        {
            Dialogues.Information(FindForm(), $"Utilisations exportées : {Libelles.Pluriel(lignes.Count(l => !l.EstControle), "sortie")}.");
        }
    }

    // ================================================================ En-tête

    /// <summary>Pictogramme de la catégorie, « Utilisations de A07 », la ligne « nom · marque modèle · en service depuis » ; boutons à droite.</summary>
    private sealed class EnteteUtilisations : Panel
    {
        private UtilisationsEquipement? _donnees;

        public EnteteUtilisations()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            Fiche = new BoutonArrondi("Fiche EPI", StyleBouton.Secondaire, Icones.Document) { Margin = Padding.Empty };
            Exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire, Icones.Telecharger) { Margin = Padding.Empty };
            Controls.Add(Fiche);
            Controls.Add(Exporter);
            AccessibleRole = AccessibleRole.Grouping;
        }

        public BoutonArrondi Fiche { get; }

        public BoutonArrondi Exporter { get; }

        private float E(float v) => Dessin.Echelle(this, v);

        private int GaucheBoutons { get; set; }

        public void Afficher(UtilisationsEquipement donnees)
        {
            _donnees = donnees;
            AccessibleName = $"Utilisations de {donnees.Equipement.Code}. {donnees.SousTitre}";
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Exporter is null)
            {
                return;
            }

            var x = Width;
            foreach (var b in new Control[] { Exporter, Fiche })
            {
                var taille = b.GetPreferredSize(Size.Empty);
                var h = (int)E(Theme.HauteurBouton);
                b.SetBounds(x - taille.Width, (Height - h) / 2, taille.Width, h);
                x -= taille.Width + (int)E(10);
            }

            GaucheBoutons = x;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_donnees is not { } d)
            {
                return;
            }

            var g = e.Graphics;
            Dessin.Qualite(g);
            var cote = E(58);
            var tuile = new RectangleF(0, (Height - cote) / 2, cote, cote);
            using (var chemin = Dessin.Arrondi(tuile, E(18)))
            using (var fond = new SolidBrush(Theme.Sur(Theme.Fond, Theme.Accent, 0.12f)))
            using (var bord = new Pen(Theme.Sur(Theme.Fond, Theme.Accent, 0.30f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            Icones.Dessiner(g, Icones.PourCategorie(d.Categorie), RectangleF.Inflate(tuile, -E(15), -E(15)), Theme.AccentClair);

            var x = (int)(tuile.Right + E(16));
            var droite = Math.Max(x + (int)E(120), GaucheBoutons - (int)E(16));
            var police = Theme.PoliceGrasse(20f);
            const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var debut = "Utilisations de ";
            var tDebut = TextRenderer.MeasureText(g, debut, police, Size.Empty, drapeaux);
            var y = (int)((Height / 2f) - E(32));
            TextRenderer.DrawText(g, debut, police, new Point(x, y), Theme.Texte, drapeaux);
            TextRenderer.DrawText(g, d.Equipement.Code, police, Rectangle.FromLTRB(x + tDebut.Width, y, droite, y + tDebut.Height), Theme.Accent,
                drapeaux | TextFormatFlags.EndEllipsis);
            Dessin.Texte(g, d.SousTitre, Theme.Police(11f), new Rectangle(x, y + tDebut.Height + (int)E(4), droite - x, (int)E(24)), Theme.TexteSecondaireClair);
        }
    }

    // ================================================================ Chiffres

    /// <summary>Quatre cartes : compteur avant révision (barre et seuil), sorties depuis le contrôle, total, problèmes signalés.</summary>
    private sealed class ChiffresUtilisations : ControleDessine
    {
        private const int HauteurCarte = 132;
        private const int Ecart = 14;
        private UtilisationsEquipement? _donnees;

        public ChiffresUtilisations()
        {
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = "Chiffres clés";
        }

        public void Afficher(UtilisationsEquipement donnees)
        {
            _donnees = donnees;
            AccessibleDescription = string.Join(". ", Cartes().Select(c => $"{c.Libelle} : {c.Valeur} {c.Unite}, {c.Detail}"));
            Invalidate();
        }

        private int Colonnes(int largeur) => largeur >= E((4 * 200) + (3 * Ecart)) ? 4 : 2;

        public int HauteurPour(int largeur)
        {
            var rangees = 4 / Colonnes(largeur);
            return (int)((rangees * E(HauteurCarte)) + ((rangees - 1) * E(Ecart))) + 2;
        }

        private sealed record CarteChiffre(string Libelle, string Valeur, string Unite, string Detail, Color CouleurValeur, Color CouleurDetail, double? Barre, Color CouleurBarre);

        private IEnumerable<CarteChiffre> Cartes()
        {
            if (_donnees is not { } d)
            {
                yield break;
            }

            var couleur = d.Etat switch
            {
                EtatCompteur.Atteint => Theme.Erreur,
                EtatCompteur.Proche => Theme.Avertissement,
                _ => Theme.Accent,
            };
            if (d.Seuil is { } seuil)
            {
                var origine = d.Origine switch
                {
                    OrigineSeuil.Equipement => "SEUIL ÉQUIPEMENT",
                    OrigineSeuil.Modele => "SEUIL MODÈLE",
                    _ => "SEUIL CATÉGORIE",
                };
                var detail = d.Reste is > 0 ? $"{origine} · reste {d.Reste}" : $"{origine} · atteint";
                yield return new("Compteur avant révision", d.Compteur.ToString(Libelles.Culture), $"/ {seuil} utilis.", detail,
                    d.Etat == EtatCompteur.Normal ? Theme.Texte : couleur,
                    d.Origine == OrigineSeuil.Categorie && d.Etat == EtatCompteur.Normal ? Theme.TexteSecondaireClair : (d.Etat == EtatCompteur.Normal ? Theme.Avertissement : couleur),
                    d.Compteur / (double)seuil, couleur);
            }
            else
            {
                yield return new("Compteur depuis le contrôle", d.Compteur.ToString(Libelles.Culture), "utilis.", "Pas de seuil pour cette catégorie",
                    Theme.Texte, Theme.TexteSecondaireClair, null, couleur);
            }

            yield return new("Sorties depuis le contrôle", d.SortiesDepuisControle.ToString(Libelles.Culture), "", d.DernierControle,
                Theme.Texte, Theme.TexteSecondaireClair, null, couleur);
            yield return new("Total des utilisations", d.Total.ToString(Libelles.Culture), "utilis.", d.DetailTotal,
                Theme.Texte, Theme.TexteSecondaireClair, null, couleur);
            yield return new("Problèmes signalés", d.Problemes.ToString(Libelles.Culture), "", "Depuis le dernier contrôle",
                d.Problemes > 0 ? Theme.Avertissement : Theme.Texte, Theme.TexteSecondaireClair, null, couleur);
        }

        protected override void Peindre(Graphics g)
        {
            var cartes = Cartes().ToList();
            if (cartes.Count == 0)
            {
                return;
            }

            var n = Colonnes(Width);
            var l = (Width - 1 - ((n - 1) * E(Ecart))) / n;
            for (var i = 0; i < cartes.Count; i++)
            {
                var r = new RectangleF((i % n) * (l + E(Ecart)), (i / n) * (E(HauteurCarte) + E(Ecart)), l, E(HauteurCarte));
                PeindreCarte(g, cartes[i], r);
            }
        }

        private void PeindreCarte(Graphics g, CarteChiffre c, RectangleF r)
        {
            using (var chemin = Dessin.Arrondi(r, E(22)))
            using (var fond = new SolidBrush(Theme.Surface))
            using (var bord = new Pen(Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var x = (int)(r.X + E(20));
            var largeur = (int)(r.Width - E(40));
            Dessin.Texte(g, c.Libelle.ToUpper(Libelles.Culture), Theme.PoliceGrasse(7.75f), new Rectangle(x, (int)(r.Y + E(16)), largeur, (int)E(20)), Theme.TexteSecondaire);

            const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var police = Theme.PoliceGrasse(19f);
            var tValeur = TextRenderer.MeasureText(g, c.Valeur, police, Size.Empty, drapeaux);
            var yValeur = (int)(r.Y + E(40));
            TextRenderer.DrawText(g, c.Valeur, police, new Point(x, yValeur), c.CouleurValeur, drapeaux);
            if (c.Unite.Length > 0)
            {
                var pu = Theme.PoliceTitre(10.5f);
                var tu = TextRenderer.MeasureText(g, c.Unite, pu, Size.Empty, drapeaux);
                TextRenderer.DrawText(g, c.Unite, pu, Rectangle.FromLTRB(x + tValeur.Width + (int)E(6), yValeur + tValeur.Height - tu.Height - (int)E(4), x + largeur, yValeur + tValeur.Height),
                    Theme.TexteSecondaire, drapeaux | TextFormatFlags.EndEllipsis);
            }

            var yDetail = yValeur + tValeur.Height + (int)E(6);
            if (c.Barre is { } part)
            {
                var piste = new RectangleF(x, yDetail, largeur, E(6));
                using (var chemin = Dessin.Arrondi(piste, piste.Height / 2))
                using (var pinceau = new SolidBrush(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.14f)))
                {
                    g.FillPath(pinceau, chemin);
                }

                var plein = (float)(piste.Width * Math.Clamp(part, 0, 1));
                if (plein >= 1)
                {
                    using var chemin = Dessin.Arrondi(new RectangleF(piste.X, piste.Y, Math.Max(plein, piste.Height), piste.Height), piste.Height / 2);
                    using var pinceau = new SolidBrush(c.CouleurBarre);
                    g.FillPath(pinceau, chemin);
                }

                yDetail += (int)E(12);
                Dessin.Texte(g, c.Detail, Theme.PoliceGrasse(8.25f), new Rectangle(x, yDetail, largeur, (int)E(20)), c.CouleurDetail);
            }
            else
            {
                Dessin.Texte(g, c.Detail, Theme.Police(9.75f), new Rectangle(x, yDetail, largeur, (int)E(22)), c.CouleurDetail);
            }
        }
    }

    // ================================================================ Tableau

    /// <summary>
    /// Sorties de l'équipement : date (et jour), intitulé et type, responsable, heures de départ et de retour, utilisations
    /// (« MODIFIÉ » si la sortie n'a pas celles de son type), compteur après le retour, état au retour. Contrôles en bandeau.
    /// </summary>
    private sealed class TableauUtilisations : ControleDessine
    {
        private const int HauteurEntete = 48;
        private const int HauteurLigne = 62;
        private const int HauteurControle = 50;
        private static readonly float[] Positions = [0, 0.11f, 0.29f, 0.43f, 0.58f, 0.68f, 0.79f];
        private static readonly string[] Titres = ["Date", "Sortie", "Responsable", "Départ · retour", "Utilisations", "Compteur", "Retour"];
        private IReadOnlyList<LigneUtilisation> _lignes = [];
        private bool _jamaisSorti;

        public TableauUtilisations()
        {
            AccessibleRole = AccessibleRole.Table;
            AccessibleName = "Sorties de l'équipement";
        }

        public void Afficher(IReadOnlyList<LigneUtilisation> lignes, bool jamaisSorti)
        {
            _lignes = lignes;
            _jamaisSorti = jamaisSorti;
            AccessibleDescription = lignes.Count == 0 ? Vide
                : string.Join(". ", lignes.Select(l => l.EstControle
                    ? $"{Libelles.Date(l.Date)}, {l.Titre}, compteur remis à 0"
                    : $"{Libelles.Date(l.Date)}, {l.Titre}, {l.TypeNom}, {l.Responsable}, {l.Horaires}, +{l.Utilisations}{(l.Comptee ? "" : " au retour")}, compteur {l.Compteur?.ToString(Libelles.Culture) ?? "pas encore compté"}, {l.LibelleRetour}"));
            Invalidate();
        }

        private string Vide => _jamaisSorti ? "Cet équipement n'est encore jamais sorti." : "Aucune sortie de ce type.";

        public int HauteurPour() =>
            (int)E(HauteurEntete + (_lignes.Count == 0 ? HauteurLigne : _lignes.Sum(l => l.EstControle ? HauteurControle : HauteurLigne)) + 6);

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

            var x0 = E(22);
            var l = Width - E(44);
            int Colonne(int i) => (int)(x0 + (l * Positions[i]));
            int Fin(int i) => (int)(x0 + (l * (i + 1 < Positions.Length ? Positions[i + 1] : 1f))) - (int)E(i == 5 ? 28 : 14); // Compteur, aligné à droite, puis Retour.
            const TextFormatFlags droite = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            for (var i = 0; i < Titres.Length; i++)
            {
                // Titres alignés à droite (Utilisations, Compteur) : ils peuvent déborder un peu à gauche sur une fenêtre étroite.
                var zone = Rectangle.FromLTRB(Colonne(i) - (i is 4 or 5 ? (int)E(24) : 0), 0, Fin(i), (int)E(HauteurEntete));
                if (i is 4 or 5)
                {
                    TextRenderer.DrawText(g, Titres[i].ToUpper(Libelles.Culture), Theme.PoliceGrasse(7.5f), zone, Theme.TexteSecondaire, droite);
                }
                else
                {
                    Dessin.Texte(g, Titres[i].ToUpper(Libelles.Culture), Theme.PoliceGrasse(7.5f), zone, Theme.TexteSecondaire);
                }
            }

            if (_lignes.Count == 0)
            {
                Dessin.Texte(g, Vide, Theme.Police(10f), new Rectangle((int)x0, (int)E(HauteurEntete), (int)l, (int)E(HauteurLigne)), Theme.TexteSecondaire);
                return;
            }

            var clip = g.ClipBounds;
            using var filet = new Pen(Theme.Bordure, E(1));
            var y = (int)E(HauteurEntete);
            foreach (var ligne in _lignes)
            {
                var h = (int)E(ligne.EstControle ? HauteurControle : HauteurLigne);
                if (y > clip.Bottom)
                {
                    break;
                }

                if (y + h >= clip.Top)
                {
                    if (ligne.EstControle)
                    {
                        PeindreControle(g, ligne, y, h, Colonne(0), Colonne(1), (int)(x0 + l));
                    }
                    else
                    {
                        PeindreSortie(g, ligne, y, h, Colonne, Fin, droite);
                    }

                    g.DrawLine(filet, E(1), y, Width - E(2), y);
                }

                y += h;
            }
        }

        private void PeindreControle(Graphics g, LigneUtilisation ligne, int y, int h, int xDate, int xTitre, int droite)
        {
            using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Accent, 0.07f)))
            {
                g.FillRectangle(fond, E(1), y, Width - E(3), h);
            }

            Dessin.Texte(g, Libelles.Date(ligne.Date), Theme.PoliceTitre(9.75f), new Rectangle(xDate, y, xTitre - xDate, h), Theme.AccentClair);
            var cote = E(18);
            Icones.Dessiner(g, Icones.Revisions, new RectangleF(xTitre, y + ((h - cote) / 2), cote, cote), Theme.AccentClair);
            var x = (int)(xTitre + cote + E(10));
            var titre = Theme.PoliceTitre(10f);
            var t = TextRenderer.MeasureText(g, ligne.Titre, titre); // Avec la marge du dessin (Dessin.Texte), sinon « … ».
            var largeurTitre = Math.Min(t.Width, droite - x);
            Dessin.Texte(g, ligne.Titre, titre, new Rectangle(x, y, largeurTitre, h), Theme.AccentClair);
            Dessin.Texte(g, "Compteur remis à 0", Theme.Police(9.75f), Rectangle.FromLTRB(x + largeurTitre + (int)E(12), y, droite, y + h), Theme.TexteSecondaireClair);
        }

        private void PeindreSortie(Graphics g, LigneUtilisation ligne, int y, int h, Func<int, int> colonne, Func<int, int> fin, TextFormatFlags droite)
        {
            int Largeur(int i) => fin(i) - colonne(i);
            var milieu = y + (h / 2);

            // Date et jour.
            Dessin.Texte(g, Libelles.Date(ligne.Date), Theme.PoliceTitre(9.75f), new Rectangle(colonne(0), milieu - (int)E(20), Largeur(0), (int)E(22)), Theme.Texte);
            Dessin.Texte(g, ligne.Date.ToString("dddd", Libelles.Culture), Theme.Police(8.5f), new Rectangle(colonne(0), milieu + (int)E(1), Largeur(0), (int)E(18)), Theme.TexteSecondaire);

            // Intitulé et pastille du type.
            Dessin.Texte(g, ligne.Titre, Theme.PoliceTitre(10f), new Rectangle(colonne(1), milieu - (int)E(23), Largeur(1), (int)E(22)), Theme.Texte);
            var couleurType = Theme.CouleurTypeSortie(ligne.Famille);
            Pilule(g, ligne.TypeNom, colonne(1), milieu + (int)E(2), (int)E(22), Largeur(1), couleurType, Theme.PoliceGrasse(7.5f));

            Dessin.Texte(g, ligne.Responsable, Theme.Police(9.75f), new Rectangle(colonne(2), y, Largeur(2), h), Theme.TexteDoux);
            Dessin.Texte(g, ligne.Horaires, Theme.Police(9.75f), new Rectangle(colonne(3), y, Largeur(3), h), Theme.TexteSecondaireClair);

            // Utilisations : « +2 » (en gris si pas encore comptées : la pastille « Sorti » dit pourquoi), « MODIFIÉ » devant.
            var zoneNombre = Rectangle.FromLTRB(colonne(4), y, fin(4), y + h);
            var nombre = $"+{ligne.Utilisations}";
            var policeNombre = Theme.PoliceGrasse(10.5f);
            TextRenderer.DrawText(g, nombre, policeNombre, zoneNombre, ligne.Comptee ? Theme.Accent : Theme.TexteSecondaire, droite);
            if (ligne.Modifiees)
            {
                var largeurNombre = TextRenderer.MeasureText(g, nombre, policeNombre, Size.Empty, TextFormatFlags.NoPadding).Width;
                var pb = Theme.PoliceGrasse(6.75f);
                var tb = TextRenderer.MeasureText(g, "MODIFIÉ", pb, Size.Empty, TextFormatFlags.NoPadding);
                var rb = new RectangleF(zoneNombre.Right - largeurNombre - E(8) - tb.Width - E(12), milieu - ((tb.Height + E(4)) / 2), tb.Width + E(12), tb.Height + E(4));
                if (rb.Left >= colonne(4) - E(10))
                {
                    using (var chemin = Dessin.Arrondi(rb, rb.Height / 2))
                    using (var pinceau = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Avertissement, 0.16f)))
                    {
                        g.FillPath(pinceau, chemin);
                    }

                    Dessin.Texte(g, "MODIFIÉ", pb, Rectangle.Round(rb), Theme.Avertissement, centre: true);
                }
            }

            TextRenderer.DrawText(g, ligne.Compteur?.ToString(Libelles.Culture) ?? "—", Theme.PoliceTitre(10f), Rectangle.FromLTRB(colonne(5), y, fin(5), y + h),
                ligne.Compteur is null ? Theme.TexteSecondaire : Theme.Texte, droite);

            var couleurRetour = ligne.Retour switch
            {
                EtatRetourUtilisation.Dehors => Theme.DepartClair,
                EtatRetourUtilisation.Manquant => Theme.Erreur,
                EtatRetourUtilisation.Signale => Theme.Avertissement,
                _ => Theme.Retour,
            };
            Pilule(g, ligne.LibelleRetour, colonne(6), milieu - (int)E(13), (int)E(26), Width - (int)E(22) - colonne(6), couleurRetour, Theme.PoliceGrasse(7.75f));
        }

        private void Pilule(Graphics g, string texte, int x, int y, int h, int largeurMax, Color couleur, Font police)
        {
            if (texte.Length == 0)
            {
                return;
            }

            // Mesure avec la marge de TextRenderer (celle du dessin), sinon le texte est coupé par « … ».
            var l = Math.Min(largeurMax, TextRenderer.MeasureText(g, texte, police).Width + E(12));
            var r = new RectangleF(x, y, l, h);
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, couleur, 0.15f)))
            {
                g.FillPath(fond, chemin);
            }

            Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
        }
    }

    /// <summary>Un bouton centré sur la largeur de la page.</summary>
    private sealed class LigneCentree : Panel
    {
        public LigneCentree(BoutonArrondi bouton)
        {
            Bouton = bouton;
            BackColor = Theme.Fond;
            Margin = Padding.Empty;
            Controls.Add(bouton);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public BoutonArrondi Bouton { get; }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Bouton is null)
            {
                return;
            }

            var taille = Bouton.GetPreferredSize(Size.Empty);
            var h = Math.Min(Height, Math.Max(taille.Height, (int)Dessin.Echelle(this, Theme.HauteurCliquable)));
            Bouton.SetBounds((Width - taille.Width) / 2, 0, taille.Width, h);
        }
    }
}
