using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtre « Ajouter un type de sortie » / « Modifier « Piscine » » (maquette) : carte du nom avec le grand pictogramme teinté,
/// « Pictogramme · Bateau » + recherche, grille de 28 pictogrammes (7 par ligne), couleurs (7 pastilles) et aperçu de la
/// pastille. Tant qu'aucun pictogramme n'a été cliqué, celui-ci suit le nom tapé (« Sortie bateau » → bateau).
/// </summary>
public static class FormulaireTypeSortie
{
    private const int Largeur = 680;
    private const int Marge = 26;

    /// <summary>Ouvre la fenêtre ; vrai si le type a été ajouté ou enregistré.</summary>
    public static bool Ouvrir(Form? proprietaire, TypeDeSortie? type, ServiceTypesSortie typesSortie)
    {
        var icone = type?.IconeEffective ?? "piscine";
        var couleur = type?.CouleurEffective ?? "cyan";
        var choisiALaMain = type is not null; // En modification, le pictogramme ne suit plus le nom.

        var nom = new ChampSaisie { Valeur = type?.Nom ?? "", TexteIndicatif = "Ex. : Sortie bateau" };
        nom.Champ.AccessibleName = "Nom du type de sortie";
        nom.Champ.MaxLength = TypeDeSortie.LongueurMaxNom;

        var grille = new GrillePictogrammes(icone) { AccessibleName = "Pictogramme" };
        var couleurs = new ChoixCouleur(TypeDeSortie.Couleurs.Select(c => (c.Cle, c.Nom, Theme.CouleurTypeSortie(c.Cle))), taille: 38, ecart: 6, coche: false)
        {
            Valeur = couleur,
            AccessibleName = "Couleur",
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0),
        };

        using var fenetre = new FenetreFormulaire(type is null ? "Ajouter un type de sortie" : $"Modifier « {type.Nom} »", type is null ? "Ajouter" : "Enregistrer",
            () => type is null
                ? typesSortie.CreerAsync(nom.Valeur, grille.Valeur, couleurs.Valeur)
                : typesSortie.ModifierAsync(type.Id, nom.Valeur, grille.Valeur, couleurs.Valeur, type.Actif),
            Largeur, Marge)
        {
            Separations = true,
        };
        if (type is null)
        {
            fenetre.Valider.Icone = Icones.Plus;
        }

        // 1. Carte du nom : grand pictogramme teinté de la couleur choisie, libellé « Nom », champ.
        var grandPicto = new Picto(Icones.DeTypeSortie(icone), 72) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 16, 0), TabStop = false };
        var libelleNom = FenetreFormulaire.LibelleChamp("Nom");
        nom.Dock = DockStyle.Top;
        var carteNom = new CadreArrondi { ColumnCount = 2, RowCount = 2, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 20) };
        carteNom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        carteNom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        carteNom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        carteNom.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
        carteNom.Controls.Add(grandPicto, 0, 0);
        carteNom.SetRowSpan(grandPicto, 2);
        carteNom.Controls.Add(libelleNom, 1, 0);
        carteNom.Controls.Add(nom, 1, 1);
        fenetre.Ajouter(carteNom);

        // 2. « Pictogramme · Bateau » à gauche, recherche à droite, puis la grille.
        var titrePicto = Ui.Libelle("Pictogramme", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        titrePicto.Margin = Padding.Empty;
        var nomPicto = Ui.Libelle("", Theme.PoliceTitre(9.75f), Theme.TexteSecondaire);
        nomPicto.Margin = Padding.Empty;
        var recherche = new ChampSaisie { TexteIndicatif = "Chercher", Icone = Icones.Recherche, Width = 220, Height = 40, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        recherche.Champ.Font = Theme.Police(9.75f);
        recherche.Champ.AccessibleName = "Chercher un pictogramme";
        var titres = Ui.Rangee(true, titrePicto, nomPicto);
        titres.Anchor = AnchorStyles.Left;
        var ligneRecherche = Ui.Colonnes((titres, SizeType.Percent, 100), (recherche, SizeType.AutoSize, 0));
        ligneRecherche.AutoSize = true;
        titres.Dock = recherche.Dock = DockStyle.None; // Colonnes met Dock Fill : Anchor ensuite.
        titres.Anchor = AnchorStyles.Left;
        recherche.Anchor = AnchorStyles.Right;
        ligneRecherche.Margin = new Padding(0, 0, 0, 10);
        fenetre.Ajouter(ligneRecherche);
        grille.Margin = new Padding(0, 0, 0, 20);
        fenetre.Ajouter(grille);

        // 3. Couleur, puis l'aperçu de la pastille à droite.
        var titreCouleur = Ui.Libelle("Couleur", Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        titreCouleur.Anchor = AnchorStyles.Left;
        titreCouleur.Margin = new Padding(0, 0, 14, 0);
        var titreApercu = Ui.Libelle("Aperçu", Theme.Police(9f), Theme.TexteSecondaire);
        titreApercu.Anchor = AnchorStyles.Right;
        titreApercu.Margin = new Padding(0, 0, 10, 0);
        var apercu = new ApercuTypeSortie { Anchor = AnchorStyles.Right };
        var ligneCouleur = new TableLayoutPanel { ColumnCount = 5, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = Padding.Empty };
        ligneCouleur.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligneCouleur.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligneCouleur.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ligneCouleur.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligneCouleur.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligneCouleur.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligneCouleur.Controls.Add(titreCouleur, 0, 0);
        ligneCouleur.Controls.Add(couleurs, 1, 0);
        ligneCouleur.Controls.Add(titreApercu, 3, 0);
        ligneCouleur.Controls.Add(apercu, 4, 0);
        fenetre.Ajouter(ligneCouleur);

        void Rafraichir()
        {
            var teinte = Theme.CouleurTypeSortie(couleurs.Valeur);
            grandPicto.Chemin = Icones.DeTypeSortie(grille.Valeur);
            grandPicto.Couleur = teinte;
            grandPicto.Fond = Theme.Teinte(teinte, 0.14f);
            grille.Couleur = teinte;
            nomPicto.Text = "· " + TypeDeSortie.NomIcone(grille.Valeur);
            var texte = nom.Valeur.Trim();
            apercu.Afficher(texte.Length > 0 ? texte : "Sortie bateau", Icones.DeTypeSortie(grille.Valeur), teinte);
            fenetre.Valider.Enabled = texte.Length > 0;
        }

        nom.Champ.TextChanged += (_, _) =>
        {
            if (!choisiALaMain && TypeDeSortie.DevinerIcone(nom.Valeur) is { } devine)
            {
                grille.Valeur = devine;
            }

            Rafraichir();
        };
        grille.ChoixUtilisateur += (_, _) =>
        {
            choisiALaMain = true;
            Rafraichir();
        };
        couleurs.ValeurChangee += (_, _) => Rafraichir();
        recherche.Champ.TextChanged += (_, _) => grille.Filtrer(recherche.Valeur);
        Rafraichir();

        fenetre.Shown += (_, _) => nom.Champ.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK;
    }

    /// <summary>Carte arrondie un peu plus sombre que la fenêtre (carte du nom).</summary>
    private sealed class CadreArrondi : TableLayoutPanel
    {
        public CadreArrondi()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.45f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            using var chemin = Dessin.Arrondi(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Dessin.Echelle(this, 18));
            using var fond = new SolidBrush(BackColor);
            using var bord = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
            g.FillPath(fond, chemin);
            g.DrawPath(bord, chemin);
        }
    }
}

/// <summary>
/// Grille des pictogrammes d'un type de sortie (7 par ligne, tuiles de 70 px : pictogramme et nom), dessinée dans un seul
/// contrôle ; la tuile choisie prend la couleur du type. Clic, flèches, Espace ou Entrée. <see cref="Filtrer"/> ne garde que les
/// pictogrammes dont le nom ou un mot-clé correspond (« Aucun pictogramme pour « xyz ». »).
/// </summary>
public sealed class GrillePictogrammes : ControleDessine
{
    private const int ParLigne = 7;
    private const int HauteurTuile = 70;
    private const int Ecart = 6;

    private readonly ToolTip _bulle = new();
    private IReadOnlyList<(string Cle, string Nom, string MotsCles)> _affiches = TypeDeSortie.Icones;
    private string _valeur;
    private string _recherche = "";
    private Color _couleur = Theme.Accent;
    private int _survol = -1;
    private int _focus;

    public GrillePictogrammes(string valeur)
    {
        _valeur = valeur;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = HauteurPour(_affiches.Count, 1);
    }

    /// <summary>Clé du pictogramme choisi (<see cref="TypeDeSortie.Icones"/>). Changée par le code : pas d'événement.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Valeur
    {
        get => _valeur;
        set
        {
            if (TypeDeSortie.EstIcone(value) && _valeur != value)
            {
                _valeur = value;
                Invalidate();
            }
        }
    }

    /// <summary>Couleur de la tuile choisie (celle du type).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Couleur { get => _couleur; set => Invalider(ref _couleur, value); }

    /// <summary>Pictogramme choisi par un clic ou au clavier.</summary>
    public event EventHandler? ChoixUtilisateur;

    public void Filtrer(string? recherche)
    {
        _recherche = (recherche ?? "").Trim();
        _affiches = TypeDeSortie.ChercherIcones(_recherche);
        _survol = -1;
        _focus = Math.Clamp(_focus, 0, Math.Max(0, _affiches.Count - 1));
        Height = HauteurPour(_affiches.Count, DeviceDpi / 96f);
        Invalidate();
    }

    /// <summary>Hauteur pour <paramref name="nombre"/> tuiles ; une ligne pour le message quand il n'y en a aucune.</summary>
    private static int HauteurPour(int nombre, float echelle)
    {
        var lignes = Math.Max(1, (nombre + ParLigne - 1) / ParLigne);
        var logique = nombre == 0 ? 40 : (lignes * HauteurTuile) + ((lignes - 1) * Ecart);
        return (int)Math.Ceiling(logique * echelle);
    }

    private RectangleF Zone(int i)
    {
        var largeur = (Width - E(Ecart * (ParLigne - 1)) - 1) / ParLigne;
        return new RectangleF((i % ParLigne) * (largeur + E(Ecart)), (i / ParLigne) * E(HauteurTuile + Ecart), largeur, E(HauteurTuile));
    }

    protected override void Peindre(Graphics g)
    {
        if (_affiches.Count == 0)
        {
            Dessin.Texte(g, $"Aucun pictogramme pour « {_recherche} ».", Theme.Police(9.75f), ClientRectangle, Theme.TexteSecondaire);
            return;
        }

        var fondParent = Dessin.CouleurParent(this);
        for (var i = 0; i < _affiches.Count; i++)
        {
            var (cle, nom, _) = _affiches[i];
            var zone = Zone(i);
            var choisie = cle == _valeur;
            var focus = Focused && i == _focus;
            var fond = choisie ? Theme.Sur(fondParent, _couleur, 0.16f)
                : i == _survol ? Theme.Sur(fondParent, Theme.AccentClair, 0.08f)
                : Theme.Sur(fondParent, Theme.Fond, 0.45f);
            var bord = choisie ? _couleur : focus ? Theme.AccentClair : Theme.Bordure;
            using (var chemin = Dessin.Arrondi(RectangleF.Inflate(zone, -E(0.75f), -E(0.75f)), E(14)))
            {
                using var pinceau = new SolidBrush(fond);
                using var stylo = new Pen(bord, E(choisie || focus ? 1.5f : 1));
                g.FillPath(pinceau, chemin);
                g.DrawPath(stylo, chemin);
            }

            var encre = choisie ? _couleur : Theme.TexteSecondaireClair;
            var cote = E(24);
            Icones.Dessiner(g, Icones.DeTypeSortie(cle), new RectangleF(zone.X + ((zone.Width - cote) / 2), zone.Y + E(12), cote, cote), encre);
            var texte = Rectangle.Round(new RectangleF(zone.X + E(4), zone.Y + E(40), zone.Width - E(8), E(20)));
            Dessin.Texte(g, nom, Theme.PoliceTitre(8.25f), texte, encre, centre: true);
        }
    }

    private int Sous(Point p)
    {
        for (var i = 0; i < _affiches.Count; i++)
        {
            if (Zone(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    private void Choisir(int i)
    {
        if (i < 0 || i >= _affiches.Count)
        {
            return;
        }

        _focus = i;
        _valeur = _affiches[i].Cle;
        Invalidate();
        ChoixUtilisateur?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var i = Sous(e.Location);
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i != _survol)
        {
            _survol = i;
            Invalidate();
            _bulle.SetToolTip(this, i >= 0 ? _affiches[i].Nom : null);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _survol = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        Choisir(Sous(e.Location));
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_affiches.Count == 0)
        {
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            Choisir(_focus);
            e.Handled = true;
            return;
        }

        var avant = _focus;
        _focus = e.KeyCode switch
        {
            Keys.Left => Math.Max(0, _focus - 1),
            Keys.Right => Math.Min(_affiches.Count - 1, _focus + 1),
            Keys.Up => _focus >= ParLigne ? _focus - ParLigne : _focus,
            Keys.Down => Math.Min(_affiches.Count - 1, _focus + ParLigne),
            _ => _focus,
        };
        if (avant != _focus)
        {
            e.Handled = true;
            Invalidate();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        var i = _affiches.ToList().FindIndex(a => a.Cle == _valeur);
        _focus = i >= 0 ? i : 0;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
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

/// <summary>Aperçu de la pastille d'un type (« ▭ SORTIE BATEAU ») : pictogramme et nom en capitales, teintés de sa couleur.</summary>
public sealed class ApercuTypeSortie : ControleDessine
{
    private const int Hauteur = 30;
    private const int LargeurMax = 180;

    private string _texte = "";
    private string _icone = Icones.Autre;
    private Color _couleur = Theme.Accent;

    public ApercuTypeSortie()
    {
        TabStop = false;
        Size = new Size(120, Hauteur);
    }

    public void Afficher(string texte, string icone, Color couleur)
    {
        _texte = texte.ToUpper(Libelles.Culture);
        _icone = icone;
        _couleur = couleur;
        var largeur = (int)Math.Min(E(LargeurMax), E(12 + 14 + 7 + 12) + TextRenderer.MeasureText(_texte, Theme.PoliceGrasse(8.25f)).Width);
        Size = new Size(largeur, (int)E(Hauteur));
        Invalidate();
    }

    protected override void Peindre(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var chemin = Dessin.Arrondi(r, r.Height / 2))
        {
            using var fond = new SolidBrush(Theme.Sur(Dessin.CouleurParent(this), _couleur, 0.14f));
            g.FillPath(fond, chemin);
        }

        var cote = E(14);
        Icones.Dessiner(g, _icone, new RectangleF(E(12), (Height - cote) / 2, cote, cote), _couleur, 2.2f);
        var x = (int)E(12 + 14 + 7);
        Dessin.Texte(g, _texte, Theme.PoliceGrasse(8.25f), new Rectangle(x, 0, Width - x - (int)E(10), Height), _couleur);
    }
}
