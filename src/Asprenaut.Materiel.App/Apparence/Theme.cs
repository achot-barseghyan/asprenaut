using System.Drawing.Drawing2D;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Thème sombre "océan". Seule source des couleurs, polices et dimensions de l'interface :
/// aucun écran ne doit contenir de couleur en dur.
/// Contrastes vérifiés ≥ 4.5:1 pour chaque couple texte/fond utilisé.
/// </summary>
public static class Theme
{
    // --- Fonds et surfaces ---
    public static readonly Color Fond = Hex("#061A2E");
    public static readonly Color Surface = Hex("#08223B");
    public static readonly Color SurfaceActive = Hex("#0F3A5E");
    public static readonly Color Bordure = Hex("#13355A");
    public static readonly Color BordureForte = Hex("#1C4670");

    // --- Textes ---
    public static readonly Color Texte = Hex("#E6F1FA");
    public static readonly Color TexteSecondaire = Hex("#8FB0CC");
    public static readonly Color TexteSecondaireClair = Hex("#B7CDE0");

    // --- Accent (bouton principal) ---
    public static readonly Color Accent = Hex("#5FE3EA");
    public static readonly Color TexteSurAccent = Hex("#04222B");

    // --- Départ (orange corail) ---
    public static readonly Color Depart = Hex("#FF9A6B");
    public static readonly Color FondDepart = Hex("#3A2216");

    // --- Retour / succès (vert) ---
    public static readonly Color Retour = Hex("#3FD99A");
    public static readonly Color FondRetour = Hex("#0D3B34");

    // --- Avertissement (révision) ---
    public static readonly Color Avertissement = Hex("#FFD166");
    public static readonly Color FondAvertissement = Hex("#3A3014");

    // --- Erreur (bandeau rouge du scan) ---
    public static readonly Color Erreur = Hex("#FF8A80");
    public static readonly Color FondErreur = Hex("#3D1620");

    // --- Dimensions (pixels logiques à 96 dpi, mises à l'échelle par Dessin.Echelle) ---
    public const int HauteurCliquable = 44;
    public const int HauteurBouton = 48;
    public const int RayonBouton = 12;
    public const int RayonCarte = 16;
    public const int RayonZoneScan = 20;
    public const int LargeurMenu = 280;

    // --- Polices ---
    private static readonly string FamilleTexte = PremiereDisponible("Segoe UI Variable Text", "Segoe UI");
    private static readonly string FamilleTitre = PremiereDisponible("Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI");
    private static readonly Dictionary<(string, float, FontStyle), Font> Cache = [];

    /// <summary>Police du texte courant (points).</summary>
    public static Font Police(float taille = 10.5f, FontStyle style = FontStyle.Regular) => Obtenir(FamilleTexte, taille, style);

    /// <summary>Police semi-grasse des titres et boutons (points).</summary>
    public static Font PoliceTitre(float taille = 10.5f) =>
        Obtenir(FamilleTitre, taille, FamilleTitre == "Segoe UI" ? FontStyle.Bold : FontStyle.Regular);

    public static Color Eclaircir(Color c, float part = 0.12f) => Melanger(c, Color.White, part);

    public static Color Assombrir(Color c, float part = 0.15f) => Melanger(c, Color.Black, part);

    // --- Couleurs des messages de scan ---
    public static (Color Fond, Color Texte) CouleursSucces => (FondRetour, Retour);

    public static (Color Fond, Color Texte) CouleursAvertissement => (FondAvertissement, Avertissement);

    public static (Color Fond, Color Texte) CouleursErreur => (FondErreur, Erreur);

    /// <summary>Applique le thème à un tableau.</summary>
    public static void AppliquerGrille(DataGridView grille)
    {
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
        grille.ColumnHeadersHeight = HauteurCliquable;
        grille.RowTemplate.Height = HauteurCliquable;
        grille.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = TexteSecondaire,
            SelectionBackColor = Surface,
            SelectionForeColor = TexteSecondaire,
            Font = PoliceTitre(10f),
            Padding = new Padding(8, 0, 8, 0),
        };
        grille.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface,
            ForeColor = Texte,
            SelectionBackColor = SurfaceActive,
            SelectionForeColor = Texte,
            Font = Police(10.5f),
            Padding = new Padding(8, 0, 8, 0),
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

/// <summary>Aides de dessin pour les contrôles arrondis.</summary>
public static class Dessin
{
    /// <summary>Convertit une dimension logique (96 dpi) en pixels pour l'écran du contrôle.</summary>
    public static float Echelle(Control controle, float pixelsLogiques) => pixelsLogiques * controle.DeviceDpi / 96f;

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

    /// <summary>Peint un rectangle arrondi plein avec bordure optionnelle, après avoir effacé avec la couleur du parent.</summary>
    public static void PeindreFondArrondi(Control controle, Graphics g, Color fond, Color? bordure, float rayonLogique, float epaisseurLogique = 1f)
    {
        g.Clear(CouleurParent(controle));
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

    /// <summary>Couleur de fond opaque du premier parent (pour les coins des contrôles arrondis).</summary>
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
}
