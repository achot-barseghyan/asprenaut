using System.Drawing.Drawing2D;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Couleurs d'un thème. Les noms décrivent un rôle, pas une teinte : en thème clair, les variantes « Clair »
/// (AccentClair, DepartClair, RetourClair, TexteSecondaireClair) sont les plus contrastées sur le fond, donc plus foncées.
/// </summary>
public sealed class Palette
{
    public required bool Sombre { get; init; }

    // Fonds et surfaces
    public required Color Fond { get; init; }
    public required Color FondMenu { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceCreuse { get; init; }
    public required Color SurfaceActive { get; init; }

    // Textes
    public required Color Texte { get; init; }
    public required Color TexteSecondaire { get; init; }
    public required Color TexteSecondaireClair { get; init; }
    public required Color TexteDoux { get; init; }
    public required Color TexteActif { get; init; }

    // Couleurs d'action et d'état, et le texte posé sur leur aplat
    public required Color Accent { get; init; }
    public required Color AccentClair { get; init; }
    public required Color TexteSurAccent { get; init; }
    public required Color Depart { get; init; }
    public required Color DepartClair { get; init; }
    public required Color TexteSurDepart { get; init; }
    public required Color Retour { get; init; }
    public required Color RetourClair { get; init; }
    public required Color TexteSurRetour { get; init; }
    public required Color Avertissement { get; init; }
    public required Color TexteSurAvertissement { get; init; }
    public required Color Erreur { get; init; }
    public required Color Materiel { get; init; }

    /// <summary>Réparation externe, chez un prestataire (violet de la maquette Révisions), sa variante texte et le texte sur son aplat.</summary>
    public required Color Prestataire { get; init; }
    public required Color PrestataireClair { get; init; }
    public required Color TexteSurPrestataire { get; init; }

    /// <summary>Sorties en fosse (violet de la maquette).</summary>
    public required Color Profondeur { get; init; }

    // Bordures et fonds teintés
    public required Color Bordure { get; init; }
    public required Color BordureForte { get; init; }
    public required Color FondDepart { get; init; }
    public required Color FondRetour { get; init; }
    public required Color FondAvertissement { get; init; }
    public required Color FondErreur { get; init; }
    public required Color FondAccent { get; init; }

    // Bandeau d'accueil
    public required Color BandeauHaut { get; init; }
    public required Color BandeauMilieu { get; init; }
    public required Color BandeauBas { get; init; }
    public required Color FondMarin { get; init; }
    public required Color FondMarinProfond { get; init; }
    public required Color Lumiere { get; init; }
}

/// <summary>
/// Thème « océan » (maquette Asprenaut), sombre ou clair au choix dans Paramètres. Seule source des couleurs,
/// polices et dimensions de l'interface : aucun écran ne doit contenir de couleur en dur.
/// Contrastes vérifiés ≥ 4.5:1 pour chaque couple texte/fond utilisé, dans les deux thèmes.
/// Les couleurs sont lues à la construction des contrôles : après <see cref="Appliquer"/>, la fenêtre reconstruit
/// son menu et l'écran courant est recréé (voir <see cref="Changement"/>).
/// </summary>
public static class Theme
{
    /// <summary>Thème sombre d'origine (maquette).</summary>
    public static readonly Palette PaletteSombre = CreerSombre();

    /// <summary>Thème clair : mêmes rôles, fond bleu très pâle, cartes blanches, couleurs d'état assombries pour le contraste.</summary>
    public static readonly Palette PaletteClaire = CreerClaire();

    private static Palette _p = PaletteSombre;

    /// <summary>Le thème a changé : les contrôles déjà construits doivent être recréés.</summary>
    public static event EventHandler? Changement;

    public static ModeAffichage Mode { get; private set; } = ModeAffichage.Sombre;

    public static bool EstSombre => _p.Sombre;

    public static Palette PaletteDe(ModeAffichage mode) => mode == ModeAffichage.Clair ? PaletteClaire : PaletteSombre;

    /// <summary>Choisit le thème. À appeler au démarrage avant toute fenêtre, puis depuis Paramètres.</summary>
    public static void Appliquer(ModeAffichage mode)
    {
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;
        _p = PaletteDe(mode);
        Changement?.Invoke(null, EventArgs.Empty);
    }

    // --- Fonds et surfaces ---
    public static Color Fond => _p.Fond;
    public static Color FondMenu => _p.FondMenu;
    public static Color Surface => _p.Surface;
    public static Color SurfaceCreuse => _p.SurfaceCreuse;
    public static Color SurfaceActive => _p.SurfaceActive;

    // --- Textes ---
    public static Color Texte => _p.Texte;
    public static Color TexteSecondaire => _p.TexteSecondaire;
    public static Color TexteSecondaireClair => _p.TexteSecondaireClair;
    public static Color TexteDoux => _p.TexteDoux;

    /// <summary>Texte de l'entrée de menu active.</summary>
    public static Color TexteActif => _p.TexteActif;

    // --- Accent turquoise (bouton principal, liens, codes) ---
    public static Color Accent => _p.Accent;
    public static Color AccentClair => _p.AccentClair;
    public static Color TexteSurAccent => _p.TexteSurAccent;

    // --- Départ (orange corail) ---
    public static Color Depart => _p.Depart;
    public static Color DepartClair => _p.DepartClair;
    public static Color TexteSurDepart => _p.TexteSurDepart;

    // --- Retour / succès (vert) ---
    public static Color Retour => _p.Retour;
    public static Color RetourClair => _p.RetourClair;
    public static Color TexteSurRetour => _p.TexteSurRetour;

    // --- Avertissement (révision), erreur, matériel (historique) ---
    public static Color Avertissement => _p.Avertissement;

    /// <summary>Texte et cadenas posés sur un aplat <see cref="Avertissement"/> (pastille « verrouillé », bouton « Déverrouiller »).</summary>
    public static Color TexteSurAvertissement => _p.TexteSurAvertissement;
    public static Color Erreur => _p.Erreur;
    public static Color Materiel => _p.Materiel;

    // --- Réparation externe (Révisions : à envoyer, chez le prestataire) ---
    public static Color Prestataire => _p.Prestataire;
    public static Color PrestataireClair => _p.PrestataireClair;
    public static Color TexteSurPrestataire => _p.TexteSurPrestataire;
    public static Color Profondeur => _p.Profondeur;

    /// <summary>Couleur d'un type de sortie (point du filtre, pastille du tableau « Utilisations »), d'après sa famille.</summary>
    public static Color CouleurTypeSortie(TypeSortie famille) => famille switch
    {
        TypeSortie.Piscine => AccentClair,
        TypeSortie.Bapteme => Retour,
        TypeSortie.Lac => Materiel,
        TypeSortie.Mer => DepartClair,
        TypeSortie.Fosse => Profondeur,
        _ => TexteSecondaireClair,
    };

    // --- Bordures ---
    public static Color Bordure => _p.Bordure;
    public static Color BordureForte => _p.BordureForte;

    // --- Fonds teintés (bandeaux, pastilles, cartes colorées) ---
    public static Color FondDepart => _p.FondDepart;
    public static Color FondRetour => _p.FondRetour;
    public static Color FondAvertissement => _p.FondAvertissement;
    public static Color FondErreur => _p.FondErreur;
    public static Color FondAccent => _p.FondAccent;

    // --- Bandeau d'accueil (dégradé "eau profonde", "lagon" en clair) ---
    public static Color BandeauHaut => _p.BandeauHaut;
    public static Color BandeauMilieu => _p.BandeauMilieu;
    public static Color BandeauBas => _p.BandeauBas;
    public static Color FondMarin => _p.FondMarin;
    public static Color FondMarinProfond => _p.FondMarinProfond;
    public static Color Lumiere => _p.Lumiere;

    // --- Logo : mêmes couleurs dans les deux thèmes (identité du club) ---
    public static readonly Color LogoClair = Hex("#8FF3F7");
    public static readonly Color LogoCentre = Hex("#2BB7C8");
    public static readonly Color LogoFonce = Hex("#0F5F86");
    public static readonly Color TexteSurLogo = Hex("#04222B");

    /// <summary>Tuile blanche du logo du club (bandeau d'accueil) : fond d'origine de l'image.</summary>
    public static readonly Color FondLogoClub = Hex("#FFFFFF");

    /// <summary>Papier d'une page de manuel (PDF rendu sur fond blanc) : le même dans les deux thèmes.</summary>
    public static readonly Color PapierPdf = Hex("#FFFFFF");
    public static readonly Color HaloLogoClub = Color.FromArgb(36, 0xD9, 0xFB, 0xFF);

    // --- Pastilles des moniteurs (dégradés de la maquette, du clair au foncé), initiales foncées dans les deux thèmes ---
    public static readonly (Color Clair, Color Fonce)[] DegradesAvatar =
    [
        (Hex("#8FF3F7"), Hex("#2BB7C8")),
        (Hex("#FFC6A8"), Hex("#FF9A6B")),
        (Hex("#A8F5CF"), Hex("#3FD99A")),
        (Hex("#FFE39A"), Hex("#FFD166")),
        (Hex("#C9D8FF"), Hex("#7D9CFF")),
    ];

    public static readonly Color TexteSurAvatar = Hex("#061A2E");

    private static Palette CreerSombre()
    {
        var surface = Hex("#0A2640");
        var accentClair = Hex("#8FF3F7");
        var depart = Hex("#FF9A6B");
        var avertissement = Hex("#FFD166");
        var erreur = Hex("#FF8A80");
        var accent = Hex("#5FE3EA");
        return new Palette
        {
            Sombre = true,
            Fond = Hex("#061A2E"),
            FondMenu = Hex("#051729"),
            Surface = surface,
            SurfaceCreuse = Hex("#071E34"),
            SurfaceActive = Hex("#0F3A5E"),
            Texte = Hex("#E6F1FA"),
            TexteSecondaire = Hex("#8FB0CC"),
            TexteSecondaireClair = Hex("#A9C4DA"),
            TexteDoux = Hex("#CFE6F5"),
            TexteActif = Color.White,
            Accent = accent,
            AccentClair = accentClair,
            TexteSurAccent = Hex("#04222B"),
            Depart = depart,
            DepartClair = Hex("#FFB48F"),
            TexteSurDepart = Hex("#2A1206"),
            Retour = Hex("#3FD99A"),
            RetourClair = Hex("#9FD9C2"),
            TexteSurRetour = Hex("#062A1C"),
            Avertissement = avertissement,
            TexteSurAvertissement = Hex("#2A1F04"),
            Erreur = erreur,
            Materiel = Hex("#B7C8FF"),
            Prestataire = Hex("#B39DFF"),
            PrestataireClair = Hex("#CBBCFF"),
            TexteSurPrestataire = Hex("#1D1240"),
            Profondeur = Hex("#C9A8FF"),
            Bordure = Melanger(surface, accentClair, 0.14f),
            BordureForte = Melanger(surface, accentClair, 0.26f),
            FondDepart = Melanger(surface, depart, 0.16f),
            FondRetour = Melanger(Hex("#0D3B34"), surface, 0.15f),
            FondAvertissement = Melanger(surface, avertissement, 0.14f),
            FondErreur = Melanger(surface, erreur, 0.16f),
            FondAccent = Melanger(surface, accent, 0.12f),
            BandeauHaut = Hex("#0F5F86"),
            BandeauMilieu = Hex("#0B3F66"),
            BandeauBas = Hex("#082B4B"),
            FondMarin = Hex("#0A3556"),
            FondMarinProfond = Hex("#072A45"),
            Lumiere = Hex("#D9FBFF"),
        };
    }

    private static Palette CreerClaire()
    {
        var surface = Color.White;
        var depart = Hex("#C2410C");
        var avertissement = Hex("#B45309");
        var erreur = Hex("#C62828");
        var accent = Hex("#0B6F78");
        var retour = Hex("#036B4E");
        return new Palette
        {
            Sombre = false,
            Fond = Hex("#EDF3F8"),
            FondMenu = Hex("#F8FBFD"),
            Surface = surface,
            SurfaceCreuse = Hex("#F2F6FA"),
            SurfaceActive = Hex("#DDEBF4"),
            Texte = Hex("#0B2540"),
            TexteSecondaire = Hex("#4F6B85"),
            TexteSecondaireClair = Hex("#3B5670"),
            TexteDoux = Hex("#1E3A56"),
            TexteActif = Hex("#0B2540"),
            Accent = accent,
            AccentClair = Hex("#095C64"),
            TexteSurAccent = Color.White,
            Depart = depart,
            DepartClair = Hex("#9A3412"),
            TexteSurDepart = Color.White,
            Retour = retour,
            RetourClair = Hex("#05593F"),
            TexteSurRetour = Color.White,
            Avertissement = avertissement,
            TexteSurAvertissement = Color.White,
            Erreur = erreur,
            Materiel = Hex("#4338CA"),
            Prestataire = Hex("#6D28D9"),
            PrestataireClair = Hex("#5B21B6"),
            TexteSurPrestataire = Color.White,
            Profondeur = Hex("#7E22CE"),
            Bordure = Hex("#D5E1EA"),
            BordureForte = Hex("#B4C7D7"),
            FondDepart = Melanger(surface, depart, 0.10f),
            FondRetour = Melanger(surface, retour, 0.09f),
            FondAvertissement = Melanger(surface, avertissement, 0.10f),
            FondErreur = Melanger(surface, erreur, 0.09f),
            FondAccent = Melanger(surface, accent, 0.09f),
            BandeauHaut = Hex("#BFEAF4"),
            BandeauMilieu = Hex("#D6F0F7"),
            BandeauBas = Hex("#E8F5FA"),
            FondMarin = Hex("#B5DCEA"),
            FondMarinProfond = Hex("#9FD0E2"),
            Lumiere = Color.White,
        };
    }

    // --- Dimensions (pixels logiques à 96 dpi, mises à l'échelle par Dessin.Echelle) ---
    public const int HauteurCliquable = 44;
    public const int HauteurBouton = 50;
    public const int HauteurChamp = 52;
    public const int RayonBouton = 14;
    public const int RayonCarte = 24;
    public const int RayonZoneScan = 28;
    public const int LargeurMenu = 264;

    /// <summary>Menu réduit aux icônes (bouton « Réduire le menu »).</summary>
    public const int LargeurMenuReduit = 84;

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
        Natif.ScrollbarsDuTheme(grille);
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
