using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Champ « Catégorie » de la fiche d'un équipement (maquette « Ajouter un équipement ») : pastille colorée, nom et nombre
/// d'équipements ; un clic ouvre la liste (recherche, nombre par catégorie, coche, corbeille avec confirmation dans la ligne,
/// cadenas si la catégorie est utilisée) et « Nouvelle catégorie » (nom, pictogramme, couleur). Ajouter et supprimer passent
/// par <see cref="ServiceInventaire"/> (règle 26 : une catégorie utilisée ne se supprime pas).
/// </summary>
public sealed class ChoixCategorie : ControleDessine
{
    private const int Hauteur = 64;

    private IReadOnlyList<CategorieUsage> _categories = [];
    private ServiceInventaire? _inventaire;
    private ToolStripDropDown? _menu;
    private bool _survol;

    public ChoixCategorie()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Hauteur;
        AccessibleName = "Catégorie";
        AccessibleRole = AccessibleRole.ComboBox;
    }

    /// <summary>La catégorie choisie a changé (clic dans la liste, création, suppression de la catégorie choisie).</summary>
    public event EventHandler? Changement;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long? CategorieId { get; private set; }

    /// <summary>Nom de la catégorie choisie (« » si aucune).</summary>
    public string NomCategorie => Choisie?.Categorie.Nom ?? "";

    private CategorieUsage? Choisie => _categories.FirstOrDefault(c => c.Categorie.Id == CategorieId);

    private bool Ouvert => _menu is { Visible: true };

    /// <summary>Lit les catégories et choisit <paramref name="choisir"/> (sinon la première).</summary>
    public async Task ChargerAsync(ServiceInventaire inventaire, long? choisir)
    {
        _inventaire = inventaire;
        await RelireAsync(choisir);
    }

    private async Task RelireAsync(long? choisir)
    {
        if (_inventaire is null)
        {
            return;
        }

        _categories = await _inventaire.ListerCategoriesUsageAsync();
        Icones.RetenirCategories(_categories.Select(c => c.Categorie));
        var avant = CategorieId;
        CategorieId = _categories.Any(c => c.Categorie.Id == choisir) ? choisir : _categories.FirstOrDefault()?.Categorie.Id;
        MajAccessibilite();
        Invalidate();
        if (CategorieId != avant)
        {
            Changement?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Choisir(long id)
    {
        var change = CategorieId != id;
        CategorieId = id;
        MajAccessibilite();
        Invalidate();
        if (change)
        {
            Changement?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MajAccessibilite() =>
        AccessibleDescription = Choisie is { } c ? $"{c.Categorie.Nom}, {SousTitre(c)}" : null;

    /// <summary>« 3 équipements dans l'inventaire » / « Aucun équipement pour l'instant ».</summary>
    internal static string SousTitre(CategorieUsage c) =>
        c.NbEquipements == 0 ? "Aucun équipement pour l'instant" : $"{Libelles.Pluriel(c.NbEquipements, "équipement")} dans l'inventaire";

    // ---------------------------------------------------------------- Ouverture

    private void Ouvrir()
    {
        if (Ouvert || _inventaire is null || !Enabled)
        {
            return;
        }

        var panneau = new PanneauCategories(this, Dessin.Echelle(this, 1));
        var hote = new ToolStripControlHost(panneau) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panneau.Size };
        _menu = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true, BackColor = Theme.BordureForte };
        _menu.Items.Add(hote);
        panneau.TailleChangee += (_, _) => hote.Size = panneau.Size;
        _menu.Closed += (_, _) =>
        {
            var menu = _menu;
            _menu = null;
            Invalidate();
            BeginInvoke(() => menu?.Dispose());
        };
        _menu.Show(this, new Point(0, Height + (int)Dessin.Echelle(this, 8)));
        panneau.Recherche.Champ.Focus();
        Invalidate();
    }

    private void Fermer() => _menu?.Close(ToolStripDropDownCloseReason.ItemClicked);

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Ouvrir();
        }
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Down or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4 || (e.KeyCode == Keys.Down && e.Alt))
        {
            e.Handled = true;
            Ouvrir();
            return;
        }

        base.OnKeyDown(e);
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

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    // ---------------------------------------------------------------- Dessin

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(E(1), E(1), Width - E(2) - 1, Height - E(2) - 1);
        var ouvert = Ouvert;
        var fond = ChampSaisie.FondChamp;
        using (var chemin = Dessin.Arrondi(r, E(16)))
        {
            using var pinceau = new SolidBrush(_survol && !ouvert ? Theme.Sur(fond, Theme.AccentClair, 0.05f) : fond);
            g.FillPath(pinceau, chemin);
            using var bord = new Pen(ouvert || Focused ? Theme.Accent : Theme.BordureForte, E(ouvert ? 1.5f : 1.2f));
            g.DrawPath(bord, chemin);
        }

        if (Choisie is not { } c)
        {
            Dessin.Texte(g, "Choisir une catégorie", Theme.Police(11.25f), Rectangle.Round(RectangleF.Inflate(r, -E(16), 0)), Theme.TexteSecondaire);
            return;
        }

        var cote = E(40);
        var boite = new RectangleF(E(12), (Height - cote) / 2, cote, cote);
        PeindrePastille(g, c.Categorie, boite, fond, this);

        var x = (int)(boite.Right + E(14));
        var largeur = Width - x - (int)E(44);
        var nom = new Rectangle(x, (int)(Height / 2f - E(21)), largeur, (int)E(24));
        Dessin.Texte(g, c.Categorie.Nom, Theme.PoliceTitre(12f), nom, Theme.Texte);
        Dessin.Texte(g, SousTitre(c), Theme.Police(9f), new Rectangle(x, (int)(Height / 2f + E(2)), largeur, (int)E(20)), Theme.TexteSecondaire);

        // Chevron, tourné vers le haut quand la liste est ouverte.
        var t = E(18);
        var zone = new RectangleF(Width - E(16) - t, (Height - t) / 2, t, t);
        var etat = g.Save();
        if (ouvert)
        {
            g.TranslateTransform(zone.X + (t / 2), zone.Y + (t / 2));
            g.RotateTransform(180);
            g.TranslateTransform(-(zone.X + (t / 2)), -(zone.Y + (t / 2)));
        }

        Icones.Dessiner(g, Icones.ChevronBas, zone, Theme.AccentClair, 2f);
        g.Restore(etat);
    }

    /// <summary>Pastille arrondie teintée de la couleur de la catégorie, avec son pictogramme.</summary>
    internal static void PeindrePastille(Graphics g, Categorie categorie, RectangleF boite, Color fond, Control echelle)
    {
        if (boite.Width < 2 || boite.Height < 2)
        {
            return; // Pas encore placée (aperçu de la création avant la mise en page).
        }

        var couleur = Theme.CouleurCategorie(categorie.CouleurEffective);
        using (var chemin = Dessin.Arrondi(boite, boite.Width * 0.28f))
        {
            using var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.14f));
            g.FillPath(pinceau, chemin);
            using var bord = new Pen(Theme.Sur(fond, couleur, 0.32f), Dessin.Echelle(echelle, 1));
            g.DrawPath(bord, chemin);
        }

        var t = boite.Width * 0.5f;
        Icones.Dessiner(g, Icones.DeCategorie(categorie.IconeEffective), new RectangleF(boite.X + ((boite.Width - t) / 2), boite.Y + ((boite.Height - t) / 2), t, t), couleur);
    }

    // ================================================================ Panneau déroulant

    /// <summary>
    /// Contenu de la liste déroulante : recherche, liste des catégories, message, et en bas « Nouvelle catégorie » ou le
    /// formulaire de création. Tout est placé à la main (<see cref="Disposer"/>) ; la taille suit le contenu.
    /// </summary>
    private sealed class PanneauCategories : Panel
    {
        private const int Marge = 10, HauteurListeMax = 360, HauteurPied = 56;
        private readonly ChoixCategorie _choix;
        private readonly float _k;
        private readonly Label _nombre;
        private readonly ListeCategories _liste;
        private readonly PanneauDefilant _defile = new() { AutoScroll = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        private readonly Label _message;
        private readonly PiedNouvelle _pied;

        // Création d'une catégorie.
        private readonly Label _titreCreation;
        private readonly ChampSaisie _nom;
        private readonly Label _doublon;
        private readonly Label _titreIcones;
        private readonly GrilleChoix _icones;
        private readonly Label _titreCouleurs;
        private readonly GrilleChoix _couleurs;
        private readonly BoutonListe _annuler;
        private readonly BoutonListe _creer;
        private bool _creation;
        private string _icone = "lampe";
        private string _couleur = "jaune";

        public PanneauCategories(ChoixCategorie choix, float k)
        {
            _choix = choix;
            _k = k;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Width = Math.Max(choix.Width, K(420));

            Recherche = new ChampSaisie { TexteIndicatif = "Chercher une catégorie", Icone = Icones.Recherche, Padding = new Padding(K(14), 0, K(14), 0) };
            Recherche.Champ.AccessibleName = "Chercher une catégorie";
            _nombre = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
            _nombre.AutoSize = true;
            Recherche.ADroite = _nombre;
            Recherche.Champ.TextChanged += (_, _) => Actualiser();
            Recherche.Champ.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Down)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    _liste!.Focus();
                    _liste.Deplacer(0);
                }
                else if (e.KeyCode == Keys.Enter && _liste!.Premiere is { } premiere)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    _choix.Choisir(premiere.Categorie.Id);
                    _choix.Fermer();
                }
            };

            _liste = new ListeCategories(k);
            _liste.Choisie += (_, id) =>
            {
                _choix.Choisir(id);
                _choix.Fermer();
            };
            _liste.Supprimer += async (_, id) => await SupprimerAsync(id);
            _liste.HauteurChangee += (_, _) => Disposer();
            _defile.Controls.Add(_liste);
            Natif.ScrollbarsDuTheme(_defile);

            _message = Ui.Libelle("", Theme.Police(9.75f), Theme.Erreur);
            _message.AutoSize = false;
            _message.Visible = false;

            _pied = new PiedNouvelle(k);
            _pied.Click += (_, _) => CommencerCreation();

            _titreCreation = Ui.Libelle("NOUVELLE CATÉGORIE", Theme.PoliceTitre(9f), Theme.AccentClair);
            _nom = new ChampSaisie { TexteIndicatif = "Ex. : Lampes, Plombs, Sacs", Padding = new Padding(K(14), 0, K(14), 0) };
            _nom.Champ.AccessibleName = "Nom de la catégorie";
            _nom.Champ.MaxLength = Categorie.LongueurMaxNom;
            _nom.Champ.TextChanged += (_, _) => MajCreation();
            _nom.Champ.KeyDown += async (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    await CreerAsync();
                }
            };
            _doublon = Ui.Libelle("Cette catégorie existe déjà.", Theme.Police(9.75f), Theme.Erreur);
            _titreIcones = Ui.Libelle("Icône", Theme.PoliceTitre(9f), Theme.TexteSecondaireClair);
            _icones = new GrilleChoix(k, Categorie.IconesNouvelle, GrilleChoix.Forme.Pictogramme) { AccessibleName = "Icône de la catégorie" };
            _icones.Changement += (_, cle) =>
            {
                _icone = cle;
                MajCreation();
            };
            _titreCouleurs = Ui.Libelle("Couleur", Theme.PoliceTitre(9f), Theme.TexteSecondaireClair);
            _couleurs = new GrilleChoix(k, Categorie.Couleurs, GrilleChoix.Forme.Couleur) { AccessibleName = "Couleur de la catégorie" };
            _couleurs.Changement += (_, cle) =>
            {
                _couleur = cle;
                MajCreation();
            };
            _annuler = new BoutonListe("Annuler", StyleBouton.Secondaire) { AutoSize = false };
            _annuler.Click += (_, _) => ArreterCreation();
            _creer = new BoutonListe("Créer et choisir", StyleBouton.Principal) { AutoSize = false };
            _creer.Click += async (_, _) => await CreerAsync();

            Controls.AddRange([Recherche, _defile, _message, _pied, _titreCreation, _nom, _doublon, _titreIcones, _icones, _titreCouleurs, _couleurs, _annuler, _creer]);
            Actualiser();
        }

        /// <summary>La hauteur du panneau a changé (le menu suit).</summary>
        public event EventHandler? TailleChangee;

        public ChampSaisie Recherche { get; }

        private int K(float v) => (int)Math.Round(v * _k);

        /// <summary>Catégories qui correspondent à la recherche (sans majuscules ni accents).</summary>
        private List<CategorieUsage> Visibles()
        {
            var q = ServiceAdherents.Simplifier(Recherche.Valeur);
            return _choix._categories.Where(c => q.Length == 0 || ServiceAdherents.Simplifier(c.Categorie.Nom).Contains(q, StringComparison.Ordinal)).ToList();
        }

        private void Actualiser()
        {
            var visibles = Visibles();
            _nombre.Text = $"{visibles.Count} / {_choix._categories.Count}";
            var derniere = _choix._categories.Count <= 1;
            _liste.Afficher(visibles, _choix.CategorieId, derniere, Recherche.Valeur.Trim());
            _pied.Texte = visibles.Count == 0 && Recherche.Valeur.Trim().Length > 0 ? $"Créer « {Recherche.Valeur.Trim()} »" : "Nouvelle catégorie";
            Disposer();
        }

        private void Afficher(string? message)
        {
            _message.Text = message ?? "";
            _message.Visible = message is not null;
            Disposer();
        }

        // ------------------------------------------------------------ Création

        private void CommencerCreation()
        {
            // Le focus passe au champ du nom AVANT de cacher le bouton cliqué : sinon Windows le rend hors de la liste, qui se ferme.
            _nom.Visible = true;
            _nom.Champ.Focus();
            _creation = true;
            _nom.Valeur = Visibles().Count == 0 ? Recherche.Valeur.Trim() : "";
            _icone = "lampe";
            _couleur = "jaune";
            _icones.Valeur = _icone;
            _couleurs.Valeur = _couleur;
            _liste.AnnulerConfirmation();
            Afficher(null);
            MajCreation();
            _nom.Champ.Focus();
        }

        private void ArreterCreation()
        {
            Recherche.Champ.Focus(); // Avant de cacher « Annuler » (même raison).
            _creation = false;
            Disposer();
        }

        private bool Doublon(string nom) =>
            nom.Length > 0 && _choix._categories.Any(c => string.Equals(ServiceAdherents.Simplifier(c.Categorie.Nom), ServiceAdherents.Simplifier(nom), StringComparison.Ordinal));

        private void MajCreation()
        {
            var nom = _nom.Valeur.Trim();
            var doublon = Doublon(nom);
            _doublon.Visible = _creation && doublon;
            _creer.Enabled = nom.Length > 0 && !doublon;
            _icones.Couleur = Theme.CouleurCategorie(_couleur);
            Disposer();
            Invalidate();
        }

        private async Task CreerAsync()
        {
            if (_choix._inventaire is not { } inventaire || !_creer.Enabled)
            {
                return;
            }

            try
            {
                var resultat = await inventaire.AjouterCategorieAsync(_nom.Valeur, _icone, _couleur);
                if (!resultat.Reussi || resultat.Id is not { } id)
                {
                    Afficher(resultat.Message);
                    return;
                }

                await _choix.RelireAsync(id);
                _choix.Fermer();
            }
            catch (Exception ex)
            {
                Erreurs.Journaliser(ex);
                Afficher("Une erreur inattendue est survenue : la catégorie n'a pas été créée.");
            }
        }

        private async Task SupprimerAsync(long id)
        {
            if (_choix._inventaire is not { } inventaire)
            {
                return;
            }

            try
            {
                var resultat = await inventaire.SupprimerCategorieAsync(id);
                if (!resultat.Reussi)
                {
                    Afficher(resultat.Message);
                    return;
                }

                await _choix.RelireAsync(_choix.CategorieId == id ? null : _choix.CategorieId);
                Afficher(null);
                Actualiser();
                Recherche.Champ.Focus();
            }
            catch (Exception ex)
            {
                Erreurs.Journaliser(ex);
                Afficher("Une erreur inattendue est survenue : la catégorie n'a pas été supprimée.");
            }
        }

        // ------------------------------------------------------------ Placement

        /// <summary>Place les éléments de haut en bas et ajuste la hauteur du panneau.</summary>
        private void Disposer()
        {
            if (_liste is null || _pied is null || _creer is null)
            {
                return; // Construction en cours.
            }

            SuspendLayout();
            var l = Width - (2 * K(Marge));
            var y = K(Marge);
            Recherche.SetBounds(K(Marge), y, l, K(44));
            y += K(44) + K(8);

            var hauteurListe = Math.Min(_liste.HauteurContenu, K(HauteurListeMax));
            _defile.SetBounds(K(Marge) - K(2), y, l + K(4), hauteurListe);
            // En haut du contenu, à la position de défilement actuelle (0, 0 ramènerait la liste tout en haut).
            _liste.SetBounds(0, _defile.AutoScrollPosition.Y, _defile.ClientSize.Width - (_liste.HauteurContenu > hauteurListe ? SystemInformation.VerticalScrollBarWidth : 0), _liste.HauteurContenu);
            y += hauteurListe + K(6);

            if (_message.Visible)
            {
                var taille = TextRenderer.MeasureText(_message.Text, _message.Font, new Size(l, 0), TextFormatFlags.WordBreak);
                _message.SetBounds(K(Marge) + K(4), y, l - K(8), taille.Height);
                y += taille.Height + K(8);
            }

            // Bas : « Nouvelle catégorie », ou le formulaire de création.
            _pied.Visible = !_creation;
            foreach (var c in new Control[] { _titreCreation, _nom, _titreIcones, _icones, _titreCouleurs, _couleurs, _annuler, _creer })
            {
                c.Visible = _creation;
            }

            _doublon.Visible = _creation && Doublon(_nom.Valeur.Trim());
            _pied.Separateur = y;
            if (!_creation)
            {
                _pied.SetBounds(0, y, Width, K(HauteurPied));
                y += K(HauteurPied);
            }
            else
            {
                var x = K(18);
                var largeur = Width - (2 * x);
                y += K(16);
                _titreCreation.SetBounds(x, y, largeur, K(20));
                y += K(28);
                var cote = K(48);
                _apercu = new Rectangle(x, y, cote, cote);
                _nom.SetBounds(x + cote + K(10), y, largeur - cote - K(10), cote);
                y += cote + K(8);
                if (_doublon.Visible)
                {
                    _doublon.SetBounds(x, y, largeur, K(20));
                    y += K(24);
                }

                _titreIcones.SetBounds(x, y, largeur, K(18));
                y += K(24);
                _icones.SetBounds(x, y, largeur, _icones.HauteurPour(largeur));
                y += _icones.Height + K(12);
                _titreCouleurs.SetBounds(x, y, largeur, K(18));
                y += K(24);
                _couleurs.SetBounds(x, y, largeur, _couleurs.HauteurPour(largeur));
                y += _couleurs.Height + K(16);
                var lb = K(150);
                _creer.SetBounds(x + largeur - lb, y, lb, K(44));
                _annuler.SetBounds(_creer.Left - K(8) - K(110), y, K(110), K(44));
                y += K(44) + K(18);
            }

            ResumeLayout(false);
            if (Height != y)
            {
                Height = y;
                TailleChangee?.Invoke(this, EventArgs.Empty);
            }

            Invalidate();
        }

        private Rectangle _apercu;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Dessin.Qualite(g);
            using (var bord = new Pen(Theme.BordureForte, 1))
            {
                g.DrawRectangle(bord, 0, 0, Width - 1, Height - 1);
            }

            if (_creation)
            {
                // Fond du bas, séparateur et aperçu de la nouvelle catégorie (pictogramme et couleur choisis).
                using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Fond, 0.35f)))
                {
                    g.FillRectangle(fond, 1, _pied.Separateur, Width - 2, Height - _pied.Separateur - 1);
                }

                using (var trait = new Pen(Theme.Bordure, 1))
                {
                    g.DrawLine(trait, 1, _pied.Separateur, Width - 2, _pied.Separateur);
                }

                PeindrePastille(g, new Categorie { Nom = _nom.Valeur, Icone = _icone, Couleur = _couleur }, _apercu, Theme.Sur(Theme.Surface, Theme.Fond, 0.35f), this);
            }
        }
    }

    // ================================================================ Liste dessinée

    /// <summary>
    /// Lignes des catégories (pastille, nom, nombre, coche, corbeille ou cadenas) ; la ligne à supprimer devient une
    /// confirmation « Supprimer « Lampes » ? » avec « Garder » / « Supprimer ». Souris et clavier (flèches, Entrée, Suppr).
    /// </summary>
    private sealed class ListeCategories : Control
    {
        private const int HauteurLigne = 52, HauteurConfirmation = 108, Ecart = 2;
        private readonly float _k;
        private readonly ToolTip _bulle = new() { InitialDelay = 300 };
        private List<CategorieUsage> _lignes = [];
        private long? _choisie;
        private bool _derniere;
        private string _recherche = "";
        private long? _confirmation;
        private int _survol = -1;
        private string? _zoneSurvol;
        private int _curseur = -1;

        public ListeCategories(float k)
        {
            _k = k;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            AccessibleName = "Catégories";
            AccessibleRole = AccessibleRole.List;
        }

        public event EventHandler<long>? Choisie;

        public event EventHandler<long>? Supprimer;

        public event EventHandler? HauteurChangee;

        public CategorieUsage? Premiere => _lignes.FirstOrDefault();

        public int HauteurContenu =>
            _lignes.Count == 0
                ? K(56)
                : _lignes.Sum(l => K(l.Categorie.Id == _confirmation ? HauteurConfirmation : HauteurLigne) + K(Ecart));

        private int K(float v) => (int)Math.Round(v * _k);

        public void Afficher(List<CategorieUsage> lignes, long? choisie, bool derniere, string recherche)
        {
            _lignes = lignes;
            _choisie = choisie;
            _derniere = derniere;
            _recherche = recherche;
            if (_confirmation is { } c && lignes.All(l => l.Categorie.Id != c))
            {
                _confirmation = null;
            }

            _curseur = Math.Min(_curseur, lignes.Count - 1);
            Invalidate();
        }

        public void AnnulerConfirmation()
        {
            if (_confirmation is not null)
            {
                _confirmation = null;
                HauteurChangee?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        /// <summary>Déplace la ligne active (clavier) ; 0 = la ligne choisie, ou la première.</summary>
        public void Deplacer(int pas)
        {
            if (_lignes.Count == 0)
            {
                return;
            }

            _curseur = pas == 0
                ? Math.Max(0, _lignes.FindIndex(l => l.Categorie.Id == _choisie))
                : Math.Clamp(_curseur + pas, 0, _lignes.Count - 1);
            MontrerLigne(_curseur);
            AccessibleDescription = _lignes[_curseur].Categorie.Nom;
            Invalidate();
        }

        /// <summary>Fait défiler la liste juste assez pour que la ligne soit entièrement visible (confirmation comprise).</summary>
        public void MontrerLigne(int index)
        {
            if (index < 0 || index >= _lignes.Count || Parent is not ScrollableControl defile)
            {
                return;
            }

            var r = Ligne(index);
            var vue = -defile.AutoScrollPosition.Y;
            var hauteur = defile.ClientSize.Height;
            if (r.Bottom > vue + hauteur)
            {
                defile.AutoScrollPosition = new Point(0, Math.Max(0, r.Bottom - hauteur));
            }
            else if (r.Top < vue)
            {
                defile.AutoScrollPosition = new Point(0, r.Top);
            }
        }

        private Rectangle Ligne(int index)
        {
            var y = 0;
            for (var i = 0; i < index; i++)
            {
                y += K(_lignes[i].Categorie.Id == _confirmation ? HauteurConfirmation : HauteurLigne) + K(Ecart);
            }

            var h = K(_lignes[index].Categorie.Id == _confirmation ? HauteurConfirmation : HauteurLigne);
            return new Rectangle(K(2), y, Width - K(4), h);
        }

        private Rectangle BoutonCote(Rectangle ligne) => new(ligne.Right - K(44), ligne.Y + ((ligne.Height - K(44)) / 2), K(44), K(44));

        private (Rectangle Garder, Rectangle Supprimer) BoutonsConfirmation(Rectangle ligne)
        {
            var h = K(40);
            var y = ligne.Bottom - K(12) - h;
            var supprimer = new Rectangle(ligne.Right - K(14) - K(110), y, K(110), h);
            return (new Rectangle(supprimer.Left - K(8) - K(90), y, K(90), h), supprimer);
        }

        /// <summary>Ligne et zone sous la souris : « choisir », « supprimer », « garder », « confirmer », « cadenas ».</summary>
        private (int Index, string? Zone) Cible(Point p)
        {
            for (var i = 0; i < _lignes.Count; i++)
            {
                var r = Ligne(i);
                if (!r.Contains(p))
                {
                    continue;
                }

                var c = _lignes[i];
                if (c.Categorie.Id == _confirmation)
                {
                    var (garder, supprimer) = BoutonsConfirmation(r);
                    return (i, garder.Contains(p) ? "garder" : supprimer.Contains(p) ? "confirmer" : null);
                }

                if (BoutonCote(r).Contains(p))
                {
                    return (i, c.Supprimable && !_derniere ? "supprimer" : "cadenas");
                }

                return (i, "choisir");
            }

            return (-1, null);
        }

        private static string RaisonCadenas(CategorieUsage c, bool derniere)
        {
            if (derniere && c.Supprimable)
            {
                return "Dernière catégorie : elle ne peut pas être supprimée.";
            }

            var parties = new List<string>();
            if (c.NbEquipements > 0)
            {
                parties.Add(Libelles.Pluriel(c.NbEquipements, "équipement"));
            }

            if (c.NbManuels > 0)
            {
                parties.Add(Libelles.Pluriel(c.NbManuels, "manuel"));
            }

            return $"Utilisée par {string.Join(" et ", parties)} : elle ne peut pas être supprimée.";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var (index, zone) = Cible(e.Location);
            if (index != _survol || zone != _zoneSurvol)
            {
                _survol = index;
                _zoneSurvol = zone;
                Cursor = zone is null or "cadenas" ? Cursors.Default : Cursors.Hand;
                _bulle.SetToolTip(this, zone == "cadenas" && index >= 0 ? RaisonCadenas(_lignes[index], _derniere)
                    : zone == "supprimer" && index >= 0 ? $"Supprimer la catégorie {_lignes[index].Categorie.Nom}" : null);
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survol = -1;
            _zoneSurvol = null;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var (index, zone) = Cible(e.Location);
            if (index < 0)
            {
                return;
            }

            Agir(_lignes[index], zone);
        }

        private void Agir(CategorieUsage c, string? zone)
        {
            switch (zone)
            {
                case "choisir":
                    Choisie?.Invoke(this, c.Categorie.Id);
                    break;
                case "supprimer":
                    _confirmation = c.Categorie.Id;
                    HauteurChangee?.Invoke(this, EventArgs.Empty); // La ligne grandit : la liste est replacée.
                    MontrerLigne(_lignes.IndexOf(c)); // « Garder » et « Supprimer » restent visibles.
                    Invalidate();
                    break;
                case "garder":
                    AnnulerConfirmation();
                    break;
                case "confirmer":
                    _confirmation = null;
                    Supprimer?.Invoke(this, c.Categorie.Id);
                    break;
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter or Keys.Delete or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_lignes.Count == 0 || _curseur < 0 && e.KeyCode is not (Keys.Down or Keys.Up))
            {
                base.OnKeyDown(e);
                return;
            }

            var c = _curseur >= 0 ? _lignes[_curseur] : null;
            switch (e.KeyCode)
            {
                case Keys.Down: Deplacer(_curseur < 0 ? 0 : 1); break;
                case Keys.Up: Deplacer(-1); break;
                case Keys.Home: _curseur = 0; Invalidate(); break;
                case Keys.End: _curseur = _lignes.Count - 1; Invalidate(); break;
                case Keys.Enter when c is not null:
                    Agir(c, c.Categorie.Id == _confirmation ? "confirmer" : "choisir");
                    break;
                case Keys.Delete when c is not null && c.Supprimable && !_derniere:
                    Agir(c, "supprimer");
                    break;
                default: base.OnKeyDown(e); return;
            }

            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Dessin.Qualite(g);
            if (_lignes.Count == 0)
            {
                Dessin.Texte(g, $"Aucune catégorie « {_recherche} ».", Theme.Police(10.5f), new Rectangle(0, 0, Width, K(56)), Theme.TexteSecondaireClair, centre: true);
                return;
            }

            for (var i = 0; i < _lignes.Count; i++)
            {
                var r = Ligne(i);
                if (!e.ClipRectangle.IntersectsWith(r))
                {
                    continue;
                }

                var c = _lignes[i];
                if (c.Categorie.Id == _confirmation)
                {
                    PeindreConfirmation(g, c, r, i == _survol ? _zoneSurvol : null);
                }
                else
                {
                    PeindreLigne(g, c, r, i);
                }
            }
        }

        private void PeindreLigne(Graphics g, CategorieUsage c, Rectangle r, int index)
        {
            var choisie = c.Categorie.Id == _choisie;
            var survol = index == _survol;
            var fond = choisie ? Theme.Sur(Theme.Surface, Theme.Accent, 0.10f)
                : survol && _zoneSurvol == "choisir" ? Theme.Sur(Theme.Surface, Theme.AccentClair, 0.06f) : Theme.Surface;
            using (var chemin = Dessin.Arrondi(r, K(14)))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                if (index == _curseur && Focused)
                {
                    using var focus = new Pen(Theme.Accent, K(1.5f));
                    g.DrawPath(focus, chemin);
                }
            }

            var cote = K(36);
            var boite = new RectangleF(r.X + K(10), r.Y + ((r.Height - cote) / 2f), cote, cote);
            PeindrePastille(g, c.Categorie, boite, fond, this);

            // Coche, nombre, puis le nom dans la place qui reste.
            var cote2 = BoutonCote(r);
            var coche = new Rectangle(cote2.Left - K(4) - K(20), r.Y, K(20), r.Height);
            if (choisie)
            {
                Icones.Dessiner(g, Icones.Coche, new RectangleF(coche.X, r.Y + ((r.Height - K(18)) / 2f), K(18), K(18)), Theme.Accent, 2.6f);
            }

            var nombre = c.NbEquipements.ToString(Libelles.Culture);
            var police = Theme.PoliceTitre(8.25f);
            var lp = Math.Max(K(26), TextRenderer.MeasureText(nombre, police).Width + K(10));
            var pastille = new Rectangle(coche.Left - K(8) - lp, r.Y + ((r.Height - K(22)) / 2), lp, K(22));
            if (c.NbEquipements > 0)
            {
                using var chemin = Dessin.Arrondi(pastille, pastille.Height / 2f);
                using var pinceau = new SolidBrush(Theme.Sur(fond, Theme.AccentClair, 0.10f));
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, nombre, police, pastille, c.NbEquipements > 0 ? Theme.TexteSecondaireClair : Theme.TexteSecondaire, centre: true);

            var xNom = (int)boite.Right + K(12);
            Dessin.Texte(g, c.Categorie.Nom, Theme.PoliceTitre(11.25f), new Rectangle(xNom, r.Y, pastille.Left - K(8) - xNom, r.Height),
                choisie ? Theme.Texte : Theme.TexteDoux);

            // Corbeille (catégorie vide) ou cadenas (utilisée, ou dernière).
            var supprimable = c.Supprimable && !_derniere;
            var zone = new RectangleF(cote2.X + ((cote2.Width - K(17)) / 2f), cote2.Y + ((cote2.Height - K(17)) / 2f), K(17), K(17));
            if (supprimable)
            {
                var actif = index == _survol && _zoneSurvol == "supprimer";
                if (actif)
                {
                    using var chemin = Dessin.Arrondi(cote2, K(12));
                    using var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Erreur, 0.14f));
                    g.FillPath(pinceau, chemin);
                }

                Icones.Dessiner(g, Icones.Corbeille, zone, actif ? Theme.Erreur : Theme.TexteSecondaire, 1.9f);
            }
            else
            {
                Icones.Dessiner(g, Icones.Cadenas, RectangleF.Inflate(zone, -K(1), -K(1)), Theme.Sur(fond, Theme.TexteSecondaire, 0.65f), 2f);
            }
        }

        private void PeindreConfirmation(Graphics g, CategorieUsage c, Rectangle r, string? survol)
        {
            var fond = Theme.Sur(Theme.Surface, Theme.Erreur, 0.12f);
            using (var chemin = Dessin.Arrondi(r, K(14)))
            {
                using var pinceau = new SolidBrush(fond);
                g.FillPath(pinceau, chemin);
                using var bord = new Pen(Theme.Sur(Theme.Surface, Theme.Erreur, 0.45f), K(1));
                g.DrawPath(bord, chemin);
            }

            Icones.Dessiner(g, Icones.Corbeille, new RectangleF(r.X + K(14), r.Y + K(14), K(20), K(20)), Theme.Erreur, 2f);
            var x = r.X + K(44);
            Dessin.Texte(g, $"Supprimer « {c.Categorie.Nom} » ?", Theme.PoliceTitre(10.5f), new Rectangle(x, r.Y + K(10), r.Right - x - K(14), K(24)), Theme.Texte);
            Dessin.Texte(g, "Aucun équipement ni manuel dans cette catégorie.", Theme.Police(9.75f), new Rectangle(x, r.Y + K(32), r.Right - x - K(14), K(20)), Theme.TexteSecondaireClair);

            var (garder, supprimer) = BoutonsConfirmation(r);
            using (var chemin = Dessin.Arrondi(garder, K(10)))
            {
                using var pinceau = new SolidBrush(survol == "garder" ? Theme.Sur(fond, Theme.Texte, 0.08f) : fond);
                g.FillPath(pinceau, chemin);
                using var bord = new Pen(Theme.Sur(fond, Theme.Texte, 0.30f), K(1));
                g.DrawPath(bord, chemin);
            }

            Dessin.Texte(g, "Garder", Theme.PoliceTitre(9.75f), garder, Theme.Texte, centre: true);
            using (var chemin = Dessin.Arrondi(supprimer, K(10)))
            using (var pinceau = new SolidBrush(survol == "confirmer" ? Theme.Eclaircir(Theme.Erreur, 0.1f) : Theme.Erreur))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, "Supprimer", Theme.PoliceTitre(9.75f), supprimer, Theme.EstSombre ? Theme.TexteSurDepart : Color.White, centre: true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bulle.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // ================================================================ Bas de la liste

    /// <summary>« + Nouvelle catégorie » (ou « Créer « Lampes » » quand la recherche ne trouve rien).</summary>
    private sealed class PiedNouvelle : Control
    {
        private readonly float _k;
        private bool _survol;
        private string _texte = "Nouvelle catégorie";

        public PiedNouvelle(float k)
        {
            _k = k;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = _texte;
        }

        /// <summary>Position du séparateur dans le panneau (le fond du bas commence là).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Separateur { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Texte
        {
            get => _texte;
            set
            {
                _texte = value;
                AccessibleName = value;
                Invalidate();
            }
        }

        private int K(float v) => (int)Math.Round(v * _k);

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

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.Qualite(g);
            var fond = Theme.Sur(Theme.Surface, Theme.Fond, 0.35f);
            g.Clear(_survol ? Theme.Sur(fond, Theme.AccentClair, 0.06f) : fond);
            using (var trait = new Pen(Theme.Bordure, 1))
            {
                g.DrawLine(trait, 1, 0, Width - 2, 0);
            }

            var cote = K(34);
            var boite = new RectangleF(K(18), (Height - cote) / 2f, cote, cote);
            using (var chemin = Dessin.Arrondi(boite, K(10)))
            using (var tirets = new Pen(Theme.Sur(fond, Theme.AccentClair, 0.5f), K(1.5f)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            {
                g.DrawPath(tirets, chemin);
            }

            Icones.Dessiner(g, Icones.Plus, RectangleF.Inflate(boite, -K(9), -K(9)), Theme.AccentClair, 2.4f);
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Accent, K(1.5f));
                g.DrawRectangle(focus, K(2), K(2), Width - K(4) - 1, Height - K(4) - 1);
            }

            Dessin.Texte(g, _texte, Theme.PoliceTitre(11.25f), new Rectangle((int)boite.Right + K(12), 0, Width - (int)boite.Right - K(24), Height), Theme.AccentClair);
        }
    }

    // ================================================================ Choix d'un pictogramme ou d'une couleur

    /// <summary>Rangée de cases à choisir : pictogrammes (dans la couleur choisie) ou pastilles de couleur.</summary>
    private sealed class GrilleChoix : Control
    {
        public enum Forme
        {
            Pictogramme,
            Couleur,
        }

        private readonly float _k;
        private readonly IReadOnlyList<string> _cles;
        private readonly Forme _forme;
        private string _valeur;
        private int _survol = -1;
        private int _curseur;

        public GrilleChoix(float k, IReadOnlyList<string> cles, Forme forme)
        {
            _k = k;
            _cles = cles;
            _forme = forme;
            _valeur = cles[0];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.List;
        }

        public event EventHandler<string>? Changement;

        /// <summary>Couleur des pictogrammes (celle choisie pour la catégorie).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Couleur
        {
            get => _couleurPictos;
            set
            {
                _couleurPictos = value;
                Invalidate();
            }
        }

        private Color _couleurPictos = Theme.Accent;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Valeur
        {
            get => _valeur;
            set
            {
                _valeur = value;
                _curseur = Math.Max(0, _cles.ToList().IndexOf(value));
                AccessibleDescription = value;
                Invalidate();
            }
        }

        private int K(float v) => (int)Math.Round(v * _k);

        private int Colonnes(int largeur) => _forme == Forme.Couleur ? _cles.Count : largeur < K(360) ? 4 : _cles.Count;

        public int HauteurPour(int largeur)
        {
            var rangees = (int)Math.Ceiling(_cles.Count / (double)Colonnes(largeur));
            return (rangees * K(44)) + ((rangees - 1) * K(6));
        }

        private Rectangle Case(int i)
        {
            if (_forme == Forme.Couleur)
            {
                return new Rectangle(i * (K(44) + K(8)), 0, K(44), K(44));
            }

            var colonnes = Colonnes(Width);
            var l = (Width - ((colonnes - 1) * K(6))) / colonnes;
            return new Rectangle(i % colonnes * (l + K(6)), i / colonnes * (K(44) + K(6)), l, K(44));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var i = Enumerable.Range(0, _cles.Count).FirstOrDefault(n => Case(n).Contains(e.Location), -1);
            if (i != _survol)
            {
                _survol = i;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survol = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var i = Enumerable.Range(0, _cles.Count).FirstOrDefault(n => Case(n).Contains(e.Location), -1);
            if (e.Button == MouseButtons.Left && i >= 0)
            {
                Choisir(i);
            }
        }

        private void Choisir(int i)
        {
            Valeur = _cles[i];
            Changement?.Invoke(this, _valeur);
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                e.Handled = true;
                Choisir(Math.Clamp(_curseur + (e.KeyCode == Keys.Right ? 1 : -1), 0, _cles.Count - 1));
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var fond = Theme.Sur(Theme.Surface, Theme.Fond, 0.35f);
            g.Clear(fond);
            Dessin.Qualite(g);
            for (var i = 0; i < _cles.Count; i++)
            {
                var r = Case(i);
                var choisi = _cles[i] == _valeur;
                if (_forme == Forme.Pictogramme)
                {
                    using (var chemin = Dessin.Arrondi(r, K(11)))
                    {
                        using var pinceau = new SolidBrush(choisi ? Theme.Sur(fond, Couleur, 0.18f) : i == _survol ? Theme.Sur(fond, Theme.AccentClair, 0.08f) : Theme.Sur(fond, Theme.Fond, 0.4f));
                        g.FillPath(pinceau, chemin);
                        using var bord = new Pen(choisi ? Couleur : Theme.Bordure, K(choisi ? 1.5f : 1));
                        g.DrawPath(bord, chemin);
                    }

                    var t = K(18);
                    Icones.Dessiner(g, Icones.DeCategorie(_cles[i]), new RectangleF(r.X + ((r.Width - t) / 2f), r.Y + ((r.Height - t) / 2f), t, t), choisi ? Couleur : Theme.TexteSecondaireClair);
                }
                else
                {
                    var couleur = Theme.CouleurCategorie(_cles[i]);
                    if (choisi || i == _survol)
                    {
                        using var anneau = new Pen(choisi ? couleur : Theme.Bordure, K(2));
                        g.DrawEllipse(anneau, RectangleF.Inflate(r, -K(1), -K(1)));
                    }

                    using var pinceau = new SolidBrush(couleur);
                    g.FillEllipse(pinceau, RectangleF.Inflate(r, -K(9), -K(9)));
                }

                if (i == _curseur && Focused && ShowFocusCues)
                {
                    using var focus = new Pen(Theme.Texte, K(1)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                    g.DrawRectangle(focus, Rectangle.Inflate(r, -K(3), -K(3)));
                }
            }
        }
    }
}

/// <summary>
/// Bouton dans la liste déroulante : le menu prend la souris entre l'appui et le relâchement, et un Button n'émet alors pas
/// Click ; le relâchement sur le bouton le déclenche (une seule fois).
/// </summary>
internal sealed class BoutonListe(string texte, StyleBouton style) : BoutonArrondi(texte, style)
{
    private bool _clique;

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _clique = false;
        base.OnMouseDown(mevent);
    }

    protected override void OnClick(EventArgs e)
    {
        _clique = true;
        base.OnClick(e);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        if (mevent.Button == MouseButtons.Left && !_clique && Enabled && ClientRectangle.Contains(mevent.Location))
        {
            PerformClick();
        }
    }
}

/// <summary>
/// Panneau défilant qui ne revient pas en haut quand un enfant prend le focus (WinForms fait défiler pour montrer le coin
/// haut gauche du contrôle actif : un clic dans une longue liste la ramenait tout en haut).
/// </summary>
internal sealed class PanneauDefilant : Panel
{
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
}
