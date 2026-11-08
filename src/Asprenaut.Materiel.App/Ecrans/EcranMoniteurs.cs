using System.ComponentModel;
using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Moniteurs : une carte par moniteur (sorties de la saison, matériel dehors) et la fiche du moniteur choisi
/// (matériel dehors sous sa responsabilité, dernières sorties, modifier, créer une sortie).
/// </summary>
public sealed class EcranMoniteurs : EcranBase
{
    private readonly ServiceMoniteurs _moniteurs;
    private readonly INavigation _navigation;
    private readonly GrilleAdaptative _cartes = new() { LargeurMin = 250, HauteurElement = 176, Ecart = 14, Margin = new Padding(0, 0, 22, 0) };
    private readonly Panel _fiche = new() { AutoScroll = true, BackColor = Theme.Surface, Margin = Padding.Empty };
    private readonly Carte _carteFiche = new() { Padding = new Padding(24), CouleurBordure = Theme.BordureForte };
    private IReadOnlyList<FicheMoniteur> _liste = [];
    private long? _selection;

    public EcranMoniteurs(ServiceMoniteurs moniteurs, INavigation navigation)
    {
        _moniteurs = moniteurs;
        _navigation = navigation;

        var ajouter = new BoutonArrondi("Ajouter un moniteur", StyleBouton.Principal, Icones.Plus);
        ajouter.Click += (_, _) => Executer(() => ModifierAsync(null));

        Natif.ScrollbarsSombres(_fiche);
        _fiche.Dock = DockStyle.Fill;
        _carteFiche.Controls.Add(_fiche);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Les responsables de sortie et le matériel qu'ils ont encore dehors.")), ajouter), SizeType.AutoSize, 0),
            (Ui.Colonnes((_cartes, SizeType.Percent, 60), (_carteFiche, SizeType.Percent, 40)), SizeType.Percent, 100)));
    }

    public override string Titre => "Moniteurs";

    public override EntreeMenu Menu => EntreeMenu.Moniteurs;

    protected override Task ChargerAsync(object? parametre) => RechargerAsync();

    private async Task RechargerAsync()
    {
        _liste = await _moniteurs.ListerFichesAsync();
        if (IsDisposed)
        {
            return;
        }

        if (_selection is null || _liste.All(f => f.Moniteur.Id != _selection))
        {
            _selection = _liste.FirstOrDefault()?.Moniteur.Id;
        }

        _cartes.SuspendLayout();
        while (_cartes.Controls.Count > 0)
        {
            var c = _cartes.Controls[0];
            _cartes.Controls.RemoveAt(0);
            c.Dispose();
        }

        for (var i = 0; i < _liste.Count; i++)
        {
            var fiche = _liste[i];
            var carte = new CarteMoniteur(fiche, i) { Selectionne = fiche.Moniteur.Id == _selection };
            carte.Click += (_, _) => Executer(() => ChoisirAsync(fiche.Moniteur.Id));
            _cartes.Controls.Add(carte);
        }

        _cartes.ResumeLayout(true);
        await AfficherFicheAsync();
    }

    private async Task ChoisirAsync(long id)
    {
        _selection = id;
        foreach (var carte in _cartes.Controls.OfType<CarteMoniteur>())
        {
            carte.Selectionne = carte.MoniteurId == id;
        }

        await AfficherFicheAsync();
    }

    private async Task AfficherFicheAsync()
    {
        var detail = _selection is { } id ? await _moniteurs.ObtenirDetailAsync(id) : null;
        if (IsDisposed)
        {
            return;
        }

        var contenu = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        contenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Ligne(Control c)
        {
            contenu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            contenu.Controls.Add(c, 0, contenu.RowStyles.Count - 1);
        }

        if (detail is null)
        {
            Ligne(Ui.Libelle("Aucun moniteur. Cliquez sur « Ajouter un moniteur ».", Theme.Police(11f), Theme.TexteSecondaireClair));
        }
        else
        {
            ConstruireFiche(detail, Ligne);
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

    private void ConstruireFiche(DetailMoniteur detail, Action<Control> ligne)
    {
        var m = detail.Moniteur;
        var index = Math.Max(0, _liste.ToList().FindIndex(f => f.Moniteur.Id == m.Id));

        // En-tête : pastille, nom, niveau, téléphone.
        var nom = Ui.Libelle(m.NomComplet, Theme.PoliceGrasse(16f));
        nom.Margin = new Padding(0, 0, 0, 4);
        var qualite = Ui.Libelle(Qualite(m), Theme.Police(9.75f), Theme.TexteSecondaireClair);
        var textes = Ui.Pile(nom, qualite);
        if (m.Telephone.Length > 0)
        {
            var tel = Ui.Rangee(true, new Picto(Icones.Telephone, 18, Theme.TexteSecondaire, Color.Transparent) { Margin = new Padding(0, 2, 6, 0) },
                Ui.Libelle(m.Telephone, Theme.Police(9.75f), Theme.TexteDoux));
            textes.Controls.Add(tel);
        }

        var entete = Ui.Colonnes((new Avatar(m, index, 72), SizeType.Absolute, 72 + 16), (textes, SizeType.Percent, 100));
        entete.Height = 80;
        entete.Margin = new Padding(0, 0, 0, 18);
        entete.Controls[0].Dock = DockStyle.None;
        entete.Controls[0].Anchor = AnchorStyles.Left;
        textes.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;
        ligne(entete);

        // Matériel dehors sous sa responsabilité.
        ligne(Section("Matériel dehors sous sa responsabilité"));
        if (detail.EnCours.Count == 0)
        {
            var rien = new BandeauMessage { Margin = new Padding(0, 0, 0, 18), Height = 60 };
            rien.Afficher(NiveauResultat.Succes, "Rien dehors. Tout le matériel est rentré.");
            ligne(rien);
        }

        foreach (var sd in detail.EnCours)
        {
            ligne(CarteDehors(sd));
        }

        // Dernières sorties clôturées.
        ligne(Section("Dernières sorties"));
        if (detail.Dernieres.Count == 0)
        {
            var aucune = Ui.Libelle("Aucune sortie clôturée.", Theme.Police(9.75f), Theme.TexteSecondaire);
            aucune.Margin = new Padding(0, 0, 0, 18);
            ligne(aucune);
        }

        foreach (var s in detail.Dernieres)
        {
            ligne(new LigneHistorique(s));
        }

        // Actions.
        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(0, 0, 10, 0) };
        modifier.Click += (_, _) => Executer(() => ModifierAsync(m));
        var creer = new BoutonArrondi("Créer une sortie", StyleBouton.Principal) { AutoSize = false, Margin = Padding.Empty, Enabled = m.Actif };
        creer.Click += (_, _) => _navigation.AllerA<EcranNouvelleSortie>(m.Id);
        var actions = Ui.Colonnes((modifier, SizeType.Percent, 42), (creer, SizeType.Percent, 58));
        actions.Height = Theme.HauteurBouton;
        actions.Margin = new Padding(0, 18, 0, 0);
        ligne(actions);
    }

    private Carte CarteDehors(SortieDehors sd)
    {
        var s = sd.Sortie;
        var carte = new Carte
        {
            BackColor = Theme.Teinte(Theme.Depart, 0.08f),
            CouleurBordure = Theme.Teinte(Theme.Depart, 0.30f),
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12),
            Rayon = 18,
        };
        var titre = Ui.Libelle($"{s.DateSortie.ToString("dd/MM", Libelles.Culture)} · {s.Intitule}", Theme.PoliceTitre(11.25f));
        titre.AutoEllipsis = true;
        var nombre = Ui.Libelle(Libelles.Pluriel(sd.Dehors.Count, "équipement") + " à rendre", Theme.Police(9f), Theme.DepartClair);
        var textes = Ui.Pile(titre, nombre);
        var aller = new BoutonArrondi(s.Statut == StatutSortie.EnPreparation ? "Continuer le départ" : "Scanner le retour", StyleBouton.Lien, Icones.ChevronDroite, iconeApres: true)
        {
            Margin = Padding.Empty,
            Height = Theme.HauteurCliquable,
            Font = Theme.PoliceTitre(9.75f),
        };
        aller.Click += (_, _) =>
        {
            if (s.Statut == StatutSortie.EnPreparation)
            {
                _navigation.AllerA<EcranDepart>(s.Id);
            }
            else
            {
                _navigation.AllerA<EcranRetour>(s.Id);
            }
        };
        var haut = Ui.Colonnes((textes, SizeType.Percent, 100), (aller, SizeType.AutoSize, 0));
        textes.Dock = DockStyle.None;
        textes.Anchor = AnchorStyles.Left;
        aller.Dock = DockStyle.None;
        aller.Anchor = AnchorStyles.Right;

        var liste = new ListeMateriel(StyleListe.ARendre, "") { BackColor = carte.BackColor };
        liste.Afficher(sd.Dehors.Select(l => new ElementMateriel(l.EquipementId, l.Code, l.Libelle, l.Categorie)));
        var visibles = Math.Min(sd.Dehors.Count, 5);
        carte.Controls.Add(Ui.Colonne((haut, SizeType.Absolute, 50), (liste, SizeType.Percent, 100)));
        carte.Height = 28 + 50 + 8 + (visibles * 58);
        return carte;
    }

    private static Label Section(string texte)
    {
        var l = Ui.LibelleChamp(texte);
        l.Margin = new Padding(0, 0, 0, 10);
        return l;
    }

    private static string Qualite(Moniteur m)
    {
        var parties = new List<string> { "Moniteur" };
        if (!string.IsNullOrWhiteSpace(m.Niveau))
        {
            parties.Add(m.Niveau);
        }

        if (m.InspecteurTiv)
        {
            parties.Add("TIV");
        }

        if (!m.Actif)
        {
            parties.Add("inactif");
        }

        return string.Join(" · ", parties);
    }

    /// <summary>Ajout (m = null) ou modification d'un moniteur dans une petite fenêtre.</summary>
    private async Task ModifierAsync(Moniteur? m)
    {
        var prenom = new ChampSaisie { Valeur = m?.Prenom ?? "" };
        var nom = new ChampSaisie { Valeur = m?.Nom ?? "" };
        var niveau = new ChampSaisie { Valeur = m?.Niveau ?? "", TexteIndicatif = "E3, MF1, MF2…" };
        var telephone = new ChampSaisie { Valeur = m?.Telephone ?? "", TexteIndicatif = "06 12 34 56 78" };
        var tiv = new Interrupteur("Inspecteur TIV") { Checked = m?.InspecteurTiv ?? false };
        var actif = new Interrupteur("Actif") { Checked = m?.Actif ?? true };
        prenom.Champ.AccessibleName = "Prénom";
        nom.Champ.AccessibleName = "Nom";
        niveau.Champ.AccessibleName = "Niveau";
        telephone.Champ.AccessibleName = "Téléphone";

        ResultatOperation? enregistre = null;
        using var fenetre = new FenetreFormulaire(
            m is null ? "Ajouter un moniteur" : "Modifier le moniteur",
            m is null ? "Ajouter" : "Enregistrer",
            async () => enregistre = await _moniteurs.EnregistrerAsync(new Moniteur
            {
                Id = m?.Id ?? 0,
                Prenom = prenom.Valeur,
                Nom = nom.Valeur,
                Niveau = niveau.Valeur,
                Telephone = telephone.Valeur,
                InspecteurTiv = tiv.Checked,
                Actif = actif.Checked,
            }));
        fenetre.Champ("Prénom", prenom);
        fenetre.Champ("Nom", nom);
        fenetre.Champ("Niveau", niveau);
        fenetre.Champ("Téléphone", telephone);
        fenetre.Ajouter(new LigneReglage("Inspecteur TIV", "Peut faire les TIV des blocs du club", tiv, filet: false));
        if (m is not null)
        {
            fenetre.Ajouter(new LigneReglage("Actif", "Un moniteur inactif n'est plus proposé pour les sorties", actif));
        }

        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK && enregistre?.Id is { } id)
        {
            _selection = id;
            await RechargerAsync();
        }
    }

    /// <summary>Pastille ronde avec les initiales, en dégradé.</summary>
    private sealed class Avatar : ControleDessine
    {
        private readonly string _initiales;
        private readonly (Color Clair, Color Fonce) _degrade;
        private readonly bool _actif;

        public Avatar(Moniteur m, int index, int taille)
        {
            _initiales = Initiales(m);
            _degrade = Theme.DegradesAvatar[index % Theme.DegradesAvatar.Length];
            _actif = m.Actif;
            Size = new Size(taille, taille);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = m.NomComplet;
        }

        public static string Initiales(Moniteur m) =>
            $"{m.Prenom.FirstOrDefault()}{m.Nom.FirstOrDefault()}".ToUpper(Libelles.Culture);

        protected override void Peindre(Graphics g) => Dessiner(g, new RectangleF(0, 0, Width - 1, Height - 1), _initiales, _degrade, _actif, this);

        public static void Dessiner(Graphics g, RectangleF r, string initiales, (Color Clair, Color Fonce) degrade, bool actif, Control c)
        {
            var (clair, fonce) = actif ? degrade : (Theme.TexteSecondaire, Theme.SurfaceActive);
            using (var pinceau = new LinearGradientBrush(r, clair, fonce, 40f))
            {
                g.FillEllipse(pinceau, r);
            }

            Dessin.Texte(g, initiales, Theme.PoliceGrasse(r.Height * 72 / c.DeviceDpi / 2.9f), Rectangle.Round(r), Theme.Fond, centre: true);
        }
    }

    /// <summary>Carte d'un moniteur dans la grille (bouton accessible au clavier).</summary>
    private sealed class CarteMoniteur : Button
    {
        private readonly FicheMoniteur _fiche;
        private readonly int _index;
        private bool _selectionne;
        private bool _survol;

        public CarteMoniteur(FicheMoniteur fiche, int index)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            _fiche = fiche;
            _index = index;
            var m = fiche.Moniteur;
            AccessibleName = $"{m.NomComplet}, {Libelles.Pluriel(fiche.SortiesSaison, "sortie")} cette saison, {Libelles.Pluriel(fiche.NbDehors, "équipement")} dehors";
        }

        public long MoniteurId => _fiche.Moniteur.Id;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selectionne
        {
            get => _selectionne;
            set
            {
                _selectionne = value;
                AccessibleDescription = value ? "Fiche affichée" : null;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _survol = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survol = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            float E(float v) => Dessin.Echelle(this, v);
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var m = _fiche.Moniteur;

            var r = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
            var fond = _selectionne ? Theme.Teinte(Theme.Accent, 0.10f) : _survol ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.05f) : Theme.Surface;
            using (var chemin = Dessin.Arrondi(r, E(22)))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                using var bord = new Pen(_selectionne ? Theme.Sur(Theme.Surface, Theme.Accent, 0.55f) : Theme.Bordure, E(_selectionne ? 1.5f : 1));
                g.DrawPath(bord, chemin);
                if (Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Accent, E(1.5f));
                    using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -3, -3), E(19));
                    g.DrawPath(focus, interieur);
                }
            }

            // Pastille, nom, niveau et TIV.
            var x = E(18);
            var y = E(18);
            var d = E(52);
            Avatar.Dessiner(g, new RectangleF(x, y, d, d), Avatar.Initiales(m), Theme.DegradesAvatar[_index % Theme.DegradesAvatar.Length], m.Actif, this);
            var gauche = (int)(x + d + E(14));
            var nom = Theme.PoliceTitre(12.75f);
            Dessin.Texte(g, m.NomComplet, nom, new Rectangle(gauche, (int)(y + E(2)), Width - gauche - (int)E(12), (int)E(26)), m.Actif ? Theme.Texte : Theme.TexteSecondaire);
            var px = (float)gauche;
            var py = y + E(32);
            if (!string.IsNullOrWhiteSpace(m.Niveau))
            {
                px = Pastille(g, m.Niveau.ToUpper(Libelles.Culture), px, py, Theme.AccentClair) + E(6);
            }

            if (m.InspecteurTiv)
            {
                px = Pastille(g, "TIV", px, py, Theme.Avertissement) + E(6);
            }

            if (!m.Actif)
            {
                Pastille(g, "INACTIF", px, py, Theme.TexteSecondaire);
            }

            // Deux encadrés : sorties de la saison, équipements dehors.
            var hb = E(58);
            var yb = Height - E(18) - hb;
            var lb = (Width - (2 * x) - E(8)) / 2;
            Encadre(g, new RectangleF(x, yb, lb, hb), _fiche.SortiesSaison.ToString(Libelles.Culture), "sorties cette saison",
                Theme.Sur(fond, Theme.Fond, 0.4f), Theme.Texte, Theme.TexteSecondaire);
            var dehors = _fiche.NbDehors > 0;
            Encadre(g, new RectangleF(x + lb + E(8), yb, lb, hb), _fiche.NbDehors.ToString(Libelles.Culture), "équip. dehors",
                Theme.Sur(fond, dehors ? Theme.Depart : Theme.Retour, dehors ? 0.12f : 0.10f), dehors ? Theme.DepartClair : Theme.Retour, dehors ? Theme.DepartClair : Theme.Retour);
        }

        private float Pastille(Graphics g, string texte, float x, float y, Color couleur)
        {
            var police = Theme.PoliceTitre(7.5f);
            var l = TextRenderer.MeasureText(g, texte, police, Size.Empty, TextFormatFlags.NoPadding).Width + Dessin.Echelle(this, 16);
            var r = new RectangleF(x, y, l, Dessin.Echelle(this, 20));
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, texte, police, Rectangle.Round(r), couleur, centre: true);
            return r.Right;
        }

        private void Encadre(Graphics g, RectangleF r, string nombre, string legende, Color fond, Color couleurNombre, Color couleurLegende)
        {
            using (var chemin = Dessin.Arrondi(r, Dessin.Echelle(this, 12)))
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            var x = (int)(r.X + Dessin.Echelle(this, 12));
            Dessin.Texte(g, nombre, Theme.PoliceGrasse(13.5f), new Rectangle(x, (int)(r.Y + Dessin.Echelle(this, 6)), (int)r.Width - 16, (int)Dessin.Echelle(this, 26)), couleurNombre);
            Dessin.Texte(g, legende, Theme.Police(8.25f), new Rectangle(x, (int)(r.Y + Dessin.Echelle(this, 32)), (int)r.Width - 16, (int)Dessin.Echelle(this, 18)), couleurLegende);
        }
    }

    /// <summary>Ligne « 30/10 · Cours en piscine · 10 équip. · Tout rendu ».</summary>
    private sealed class LigneHistorique : ControleDessine
    {
        private readonly SortieResume _s;

        public LigneHistorique(SortieResume s)
        {
            _s = s;
            Height = 40;
            AccessibleName = $"{Libelles.Date(s.DateSortie)}, {s.Intitule}, {Libelles.Pluriel(s.NbTotal, "équipement")}, " +
                (s.NbManquants == 0 ? "tout rendu" : Libelles.Pluriel(s.NbManquants, "manquant"));
        }

        protected override void Peindre(Graphics g)
        {
            using (var filet = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.08f)))
            {
                g.DrawLine(filet, 0, 0, Width, 0);
            }

            var police = Theme.Police(9.75f);
            var ok = _s.NbManquants == 0;
            var bilan = ok ? "Tout rendu" : Libelles.Pluriel(_s.NbManquants, "manquant");
            var couleur = ok ? Theme.Retour : Theme.DepartClair;
            var pp = Theme.PoliceTitre(8.25f);
            var lp = TextRenderer.MeasureText(g, bilan, pp, Size.Empty, TextFormatFlags.NoPadding).Width + E(16);
            var rp = new RectangleF(Width - lp, (Height - E(22)) / 2, lp, E(22));
            using (var chemin = Dessin.Arrondi(rp, rp.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, bilan, pp, Rectangle.Round(rp), couleur, centre: true);
            var nb = $"{_s.NbTotal} équip.";
            var lnb = TextRenderer.MeasureText(g, nb, police).Width;
            var xnb = (int)(rp.Left - E(10) - lnb);
            Dessin.Texte(g, nb, police, new Rectangle(xnb, 0, lnb, Height), Theme.TexteSecondaire);
            Dessin.Texte(g, _s.DateSortie.ToString("dd/MM", Libelles.Culture), police, new Rectangle(0, 0, (int)E(48), Height), Theme.TexteSecondaire);
            Dessin.Texte(g, _s.Intitule, police, Rectangle.FromLTRB((int)E(52), 0, xnb - (int)E(8), Height), Theme.TexteDoux);
        }
    }
}
