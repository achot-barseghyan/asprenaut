using System.Drawing.Drawing2D;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Thème sombre "océan" (maquette Asprenaut · parcours d'une sortie). Seule source des couleurs,
/// polices et dimensions de l'interface : aucun écran ne doit contenir de couleur en dur.
/// Contrastes vérifiés ≥ 4.5:1 pour chaque couple texte/fond utilisé.
/// </summary>
public static class Theme
{
    // --- Fonds et surfaces ---
    public static readonly Color Fond = Hex("#061A2E");
    public static readonly Color FondMenu = Hex("#051729");
    public static readonly Color Surface = Hex("#0A2640");
    public static readonly Color SurfaceCreuse = Hex("#071E34");
    public static readonly Color SurfaceActive = Hex("#0F3A5E");

    // --- Textes ---
    public static readonly Color Texte = Hex("#E6F1FA");
    public static readonly Color TexteSecondaire = Hex("#8FB0CC");
    public static readonly Color TexteSecondaireClair = Hex("#A9C4DA");
    public static readonly Color TexteDoux = Hex("#CFE6F5");

    // --- Accent turquoise (bouton principal, liens, codes) ---
    public static readonly Color Accent = Hex("#5FE3EA");
    public static readonly Color AccentClair = Hex("#8FF3F7");
    public static readonly Color TexteSurAccent = Hex("#04222B");

    // --- Départ (orange corail) ---
    public static readonly Color Depart = Hex("#FF9A6B");
    public static readonly Color DepartClair = Hex("#FFB48F");
    public static readonly Color TexteSurDepart = Hex("#2A1206");

    // --- Retour / succès (vert) ---
    public static readonly Color Retour = Hex("#3FD99A");
    public static readonly Color RetourClair = Hex("#9FD9C2");
    public static readonly Color TexteSurRetour = Hex("#062A1C");

    // --- Avertissement (révision) ---
    public static readonly Color Avertissement = Hex("#FFD166");

    // --- Erreur (bandeau rouge du scan) ---
    public static readonly Color Erreur = Hex("#FF8A80");

    // --- Bordures : l'accent clair très atténué sur la surface ---
    public static readonly Color Bordure = Teinte(AccentClair, 0.14f);
    public static readonly Color BordureForte = Teinte(AccentClair, 0.26f);

    // --- Fonds teintés (bandeaux, pastilles, cartes colorées) ---
    public static readonly Color FondDepart = Teinte(Depart, 0.16f);
    public static readonly Color FondRetour = Melanger(Hex("#0D3B34"), Surface, 0.15f);
    public static readonly Color FondAvertissement = Teinte(Avertissement, 0.14f);
    public static readonly Color FondErreur = Teinte(Erreur, 0.16f);
    public static readonly Color FondAccent = Teinte(Accent, 0.12f);

    // --- Bandeau d'accueil (dégradé "eau profonde") ---
    public static readonly Color BandeauHaut = Hex("#0F5F86");
    public static readonly Color BandeauMilieu = Hex("#0B3F66");
    public static readonly Color BandeauBas = Hex("#082B4B");
    public static readonly Color FondMarin = Hex("#0A3556");
    public static readonly Color FondMarinProfond = Hex("#072A45");
    public static readonly Color Lumiere = Hex("#D9FBFF");
    public static readonly Color LogoCentre = Hex("#2BB7C8");

    // --- Dimensions (pixels logiques à 96 dpi, mises à l'échelle par Dessin.Echelle) ---
    public const int HauteurCliquable = 44;
    public const int HauteurBouton = 50;
    public const int HauteurChamp = 52;
    public const int RayonBouton = 14;
    public const int RayonCarte = 24;
    public const int RayonZoneScan = 28;
    public const int LargeurMenu = 264;

    // --- Polices ---
    private static readonly string FamilleTexte = PremiereDisponible("Segoe UI Variable Text", "Segoe UI");
    private static readonly string FamilleTitre = PremiereDisponible("Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI");
    private static readonly string FamilleTitreGras = PremiereDisponible("Segoe UI Variable Display", "Segoe UI");
    private static readonly string FamilleCode = PremiereDisponible("Cascadia Mono", "Consolas", "Courier New");
    private static readonly Dictionary<(string, float, FontStyle), Font> Cache = [];

    /// <summary>Police du texte courant (points).</summary>
    public static Font Police(float taille = 10.5f, FontStyle style = FontStyle.Regular) => Obtenir(FamilleTexte, taille, style);

    /// <summary>Police semi-grasse des titres et boutons (points).</summary>
    public static Font PoliceTitre(float taille = 10.5f) =>
        Obtenir(FamilleTitre, taille, FamilleTitre == "Segoe UI" ? FontStyle.Bold : FontStyle.Regular);

    /// <summary>Police grasse des grands titres et des chiffres (points).</summary>
    public static Font PoliceGrasse(float taille) => Obtenir(FamilleTitreGras, taille, FontStyle.Bold);

    /// <summary>Police à chasse fixe des codes d'étiquette (points).</summary>
    public static Font PoliceCode(float taille = 9f) => Obtenir(FamilleCode, taille, FontStyle.Regular);

    public static Color Eclaircir(Color c, float part = 0.12f) => Melanger(c, Color.White, part);

    public static Color Assombrir(Color c, float part = 0.15f) => Melanger(c, Color.Black, part);

    /// <summary>Couleur <paramref name="c"/> posée avec une opacité <paramref name="part"/> sur la surface des cartes.</summary>
    public static Color Teinte(Color c, float part) => Melanger(Surface, c, part);

    /// <summary>Couleur <paramref name="c"/> posée avec une opacité <paramref name="part"/> sur <paramref name="fond"/>.</summary>
    public static Color Sur(Color fond, Color c, float part) => Melanger(fond, c, part);

    /// <summary>Couleur <paramref name="c"/> avec une opacité (pour les dessins en dégradé).</summary>
    public static Color Alpha(Color c, float opacite) => Color.FromArgb((int)(255 * Math.Clamp(opacite, 0, 1)), c);

    // --- Couleurs des messages de scan : (fond, texte, pastille) ---
    public static (Color Fond, Color Texte, Color Pastille) CouleursSucces => (FondRetour, Texte, Retour);

    public static (Color Fond, Color Texte, Color Pastille) CouleursAvertissement => (FondAvertissement, Texte, Avertissement);

    public static (Color Fond, Color Texte, Color Pastille) CouleursErreur => (FondErreur, Texte, Erreur);

    /// <summary>Applique le thème à un tableau ; hauteurs en pixels logiques, mises à l'échelle de l'écran.</summary>
    public static void AppliquerGrille(DataGridView grille, int hauteurLigne = 56)
    {
        void Hauteurs()
        {
            var f = grille.DeviceDpi / 96f;
            grille.ColumnHeadersHeight = (int)(50 * f);
            grille.RowTemplate.Height = (int)(hauteurLigne * f);
            foreach (DataGridViewRow ligne in grille.Rows)
            {
                ligne.Height = grille.RowTemplate.Height;
            }
        }

        grille.HandleCreated += (_, _) => Hauteurs();
        grille.DpiChangedAfterParent += (_, _) => Hauteurs();

        // Pas de ligne surlignée d'office : la sélection n'apparaît qu'au clic.
        grille.DataBindingComplete += (_, _) => grille.ClearSelection();
        grille.RowsAdded += (_, _) =>
        {
            if (grille.IsHandleCreated)
            {
                grille.BeginInvoke(new Action(() =>
                {
                    grille.CurrentCell = null; // Pas de cadre de focus sur la première cellule.
                    grille.ClearSelection();
                }));
            }
        };
        grille.BackgroundColor = Surface;
        grille.BorderStyle = BorderStyle.None;
        grille.GridColor = Bordure;
        grille.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grille.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grille.EnableHeadersVisualStyles = false;
        grille.RowHeadersVisible = false;
        grille.AllowUserToAddRows = false;
        grille.AllowUserToDeleteRows = false;
        grille.AllowUserToResizeRows = false;
        grille.ReadOnly = true;
        grille.MultiSelect = false;
        grille.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grille.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grille.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grille.ColumnHeadersHeight = 50;
        grille.RowTemplate.Height = hauteurLigne;
        grille.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = TexteSecondaire,
            SelectionBackColor = Surface,
            SelectionForeColor = TexteSecondaire,
            Font = PoliceTitre(9f),
            Padding = new Padding(14, 0, 8, 0),
        };
        grille.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = TexteSecondaireClair,
            SelectionBackColor = SurfaceActive,
            SelectionForeColor = Texte,
            Font = Police(10.5f),
            Padding = new Padding(14, 0, 8, 0),
        };
        Natif.ScrollbarsSombres(grille);
    }

    private static Font Obtenir(string famille, float taille, FontStyle style)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((famille, taille, style), out var police))
            {
                police = new Font(famille, taille, style, GraphicsUnit.Point);
                Cache[(famille, taille, style)] = police;
            }

            return police;
        }
    }

    private static string PremiereDisponible(params string[] familles)
    {
        foreach (var nom in familles)
        {
            using var essai = new Font(nom, 10f);
            if (string.Equals(essai.Name, nom, StringComparison.OrdinalIgnoreCase))
            {
                return nom;
            }
        }

        return familles[^1];
    }

    private static Color Melanger(Color a, Color b, float part) => Color.FromArgb(
        a.A,
        (int)(a.R + ((b.R - a.R) * part)),
        (int)(a.G + ((b.G - a.G) * part)),
        (int)(a.B + ((b.B - a.B) * part)));

    private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);
}

/// <summary>Conteneur qui dessine son propre fond (dégradé) : les coins des contrôles arrondis posés dessus le recopient.</summary>
public interface IFondDessine
{
}

/// <summary>Aides de dessin pour les contrôles arrondis.</summary>
public static class Dessin
{
    /// <summary>Convertit une dimension logique (96 dpi) en pixels pour l'écran du contrôle.</summary>
    public static float Echelle(Control controle, float pixelsLogiques) => pixelsLogiques * controle.DeviceDpi / 96f;

    /// <summary>
    /// Met à l'échelle de l'écran de <paramref name="reference"/> un contrôle construit en pixels logiques (96 dpi) :
    /// tailles, marges, Padding et lignes/colonnes fixes des TableLayoutPanel, enfants compris. Les polices,
    /// en points, sont déjà à la bonne taille. WinForms ne le fait pas seul pour les contrôles créés par code
    /// après l'affichage de la fenêtre : à appeler une seule fois par contrôle, avant de l'ajouter.
    /// </summary>
    public static void MettreALEchelle(Control controle, Control reference)
    {
        var facteur = reference.DeviceDpi / 96f;
        if (Math.Abs(facteur - 1) > 0.01f)
        {
            controle.Scale(new SizeF(facteur, facteur));
        }
    }

    /// <summary>
    /// À appeler dans OnControlAdded d'un conteneur qui reçoit des enfants après l'affichage
    /// (cartes, lignes de liste) : avant l'affichage, la mise à l'échelle de l'écran entier s'en charge.
    /// </summary>
    public static void MettreALEchelleSiAjoutTardif(Control parent, Control enfant)
    {
        if (parent.IsHandleCreated)
        {
            MettreALEchelle(enfant, parent);
        }
    }

    public static void Qualite(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static GraphicsPath Arrondi(RectangleF r, float rayon)
    {
        var d = Math.Min(rayon * 2, Math.Min(r.Width, r.Height));
        var chemin = new GraphicsPath();
        if (d <= 0)
        {
            chemin.AddRectangle(r);
            return chemin;
        }

        chemin.AddArc(r.X, r.Y, d, d, 180, 90);
        chemin.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        chemin.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        chemin.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        chemin.CloseFigure();
        return chemin;
    }

    /// <summary>Peint un rectangle arrondi plein avec bordure optionnelle, après avoir effacé avec le fond du parent.</summary>
    public static void PeindreFondArrondi(Control controle, Graphics g, Color fond, Color? bordure, float rayonLogique, float epaisseurLogique = 1f)
    {
        EffacerAvecParent(controle, g);
        Qualite(g);
        var epaisseur = bordure is null ? 0 : Echelle(controle, epaisseurLogique);
        var r = new RectangleF(epaisseur / 2, epaisseur / 2, controle.Width - epaisseur - 1, controle.Height - epaisseur - 1);
        using var chemin = Arrondi(r, Echelle(controle, rayonLogique));
        using (var pinceau = new SolidBrush(fond))
        {
            g.FillPath(pinceau, chemin);
        }

        if (bordure is { } couleur)
        {
            using var stylo = new Pen(couleur, epaisseur);
            g.DrawPath(stylo, chemin);
        }
    }

    /// <summary>Remplit le contrôle avec ce qu'il y a derrière lui : couleur du parent, ou dégradé d'un <see cref="IFondDessine"/>.</summary>
    public static void EffacerAvecParent(Control controle, Graphics g)
    {
        for (var p = controle.Parent; p is not null; p = p.Parent)
        {
            if (p is IFondDessine)
            {
                ButtonRenderer.DrawParentBackground(g, controle.ClientRectangle, controle);
                return;
            }

            if (p.BackColor.A == 255)
            {
                g.Clear(p.BackColor);
                return;
            }
        }

        g.Clear(Theme.Fond);
    }

    /// <summary>Couleur de fond opaque du premier parent.</summary>
    public static Color CouleurParent(Control controle)
    {
        for (var p = controle.Parent; p is not null; p = p.Parent)
        {
            if (p.BackColor.A == 255)
            {
                return p.BackColor;
            }
        }

        return Theme.Fond;
    }

    /// <summary>Texte sur une ligne, aligné à gauche et centré verticalement, coupé par "…".</summary>
    public static void Texte(Graphics g, string texte, Font police, Rectangle zone, Color couleur, bool centre = false) =>
        TextRenderer.DrawText(g, texte, police, zone, couleur,
            (centre ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left)
            | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
}
