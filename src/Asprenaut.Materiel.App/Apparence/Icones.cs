using System.Drawing.Drawing2D;
using System.Globalization;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Pictogrammes au trait de la maquette (grille 24 × 24, trait arrondi), décrits en syntaxe de chemin SVG
/// et dessinés en GDI+. Aucune image ni police d'icônes : tout est vectoriel et net à toutes les échelles.
/// </summary>
public static class Icones
{
    private static Icon? _fenetre;

    /// <summary>
    /// Icône de l'application (masque et tuba, Ressources\Asprenaut.ico embarqué, de 16 à 256 px) pour la barre de titre
    /// et la barre des tâches. Le .exe porte la même via ApplicationIcon dans le .csproj.
    /// </summary>
    public static Icon IconeFenetre
    {
        get
        {
            if (_fenetre is null)
            {
                using var flux = typeof(Icones).Assembly.GetManifestResourceStream("Asprenaut.ico")
                    ?? throw new InvalidOperationException("Icône de l'application introuvable (ressource Asprenaut.ico).");
                _fenetre = new Icon(flux);
            }

            return _fenetre;
        }
    }

    // --- Navigation ---
    public const string Sorties = "M3 10c2 0 2-2 4.5-2S10 10 12 10s2-2 4.5-2S19 10 21 10M3 16c2 0 2-2 4.5-2S10 16 12 16s2-2 4.5-2S19 16 21 16";
    public const string Inventaire = "M4 4h16v16H4zM4 10h16M10 10v10";
    public const string Moniteurs = "M9 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM3 20c0-3 3-5 6-5s6 2 6 5M16 5a3 3 0 0 1 0 6M18 15c2 .6 3 2.4 3 5";
    public const string Historique = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 8v4l3 2";
    public const string Bilan = "M4 20V11M10 20V5M16 20v-7M21 20H3";
    public const string Revisions = "M14.7 6.3a4 4 0 0 0-5.4 5.4L3 18l3 3 6.3-6.3a4 4 0 0 0 5.4-5.4l-2.5 2.5-2.4-.6-.6-2.4z";
    public const string Parametres ="M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM12 3v3M12 18v3M3 12h3M18 12h3";

    // --- Actions ---
    public const string Plus = "M12 5v14M5 12h14";
    public const string Cadenas = "M7 11h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-6a2 2 0 0 1 2-2zM8 11V7a4 4 0 0 1 8 0v4";
    public const string CadenasOuvert = "M7 11h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-6a2 2 0 0 1 2-2zM8 11V7a4 4 0 0 1 7.5-2";
    public const string Effacer = "M20 6H9l-6 6 6 6h11a1 1 0 0 0 1-1V7a1 1 0 0 0-1-1zM12 10l4 4M16 10l-4 4";
    public const string Crayon ="M4 20h4L19 9l-4-4L4 16zM14 6l4 4";
    public const string Oeil = "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z";
    public const string OeilBarre = "M3 3l18 18M10.6 6.1A10 10 0 0 1 12 6c6 0 10 6 10 6a17 17 0 0 1-3.2 3.8M6.1 6.9C3.6 8.6 2 12 2 12s4 6 10 6a9.7 9.7 0 0 0 4-.9";
    public const string Corbeille ="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6";
    public const string Fleche = "M5 12h14M13 6l6 6-6 6";
    public const string Coche = "M5 12l5 5 9-10";
    public const string Croix = "M6 6l12 12M18 6L6 18";
    public const string ChevronGauche = "M15 6l-6 6 6 6";
    public const string ChevronDroite = "M9 6l6 6-6 6";
    public const string ChevronBas = "M6 9l6 6 6-6";
    public const string Exclamation = "M12 6v8M12 18v.01";
    public const string Recherche = "M11 5a6 6 0 1 0 0 12 6 6 0 0 0 0-12zM20 20l-4.5-4.5";
    public const string Telecharger = "M12 4v11M7 10l5 5 5-5M5 20h14";
    public const string CercleCoche = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM8 12l3 3 5-6";
    public const string Qr = "M4.5 3h4a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-4A1.5 1.5 0 0 1 3 8.5v-4A1.5 1.5 0 0 1 4.5 3z"
        + "M15.5 3h4a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-4A1.5 1.5 0 0 1 14 8.5v-4A1.5 1.5 0 0 1 15.5 3z"
        + "M4.5 14h4a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-4A1.5 1.5 0 0 1 3 19.5v-4A1.5 1.5 0 0 1 4.5 14z"
        + "M14 14h3v3h-3zM20 14v.01M14 20h.01M17 20h4v-3";

    // --- Paramètres ---
    public const string Scanner = "M4 7V5a1 1 0 0 1 1-1h2M17 4h2a1 1 0 0 1 1 1v2M20 17v2a1 1 0 0 1-1 1h-2M7 20H5a1 1 0 0 1-1-1v-2M8 9v6M11 9v6M14 9v6M17 9v6";
    public const string Bouclier = "M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6zM9 12l2 2 4-4";
    public const string Base = "M4 5a8 3 0 1 0 16 0 8 3 0 1 0-16 0zM4 5v6c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 11v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6";
    public const string Imprimante = "M7 9V3h10v6M7 17H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2M7 14h10v7H7z";
    public const string Moins = "M5 12h14";
    public const string Telephone = "M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2";
    public const string Document = "M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h5";
    public const string Calendrier = "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM3 10h18M8 3v4M16 3v4M12 13v5M9.5 15.5h5";
    public const string Info = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 11v5M12 8h.01";
    public const string Lune = "M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z";
    public const string Soleil = "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4";

    // --- Historique : familles d'événements ---
    public const string Depart = "M14 4h5v16h-5M3 12h11M10 8l4 4-4 4";
    public const string Retour = "M10 4H5v16h5M21 12H10M14 8l-4 4 4 4";
    public const string Alerte = "M12 8v5M12 16h.01M10.3 3.9L2.5 18a2 2 0 0 0 1.7 3h15.6a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z";
    // --- Marque : masque et tuba ---
    public const string Logo = "M2.5 9.5a3 3 0 0 1 3-3h10a3 3 0 0 1 3 3v2a3 3 0 0 1-3 3h-2.5l-1.5-2h-2l-1.5 2H5.5a3 3 0 0 1-3-3zM21.5 5v9a5 5 0 0 1-5 5h-2";

    // --- Catégories de matériel ---
    public const string Tout = "M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z";
    public const string Bloc = "M9 7h6v13a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1zM10 7V4h4v3M12 2v2";
    public const string Gilet = "M6 4l3 2h6l3-2v14a3 3 0 0 1-3 3H9a3 3 0 0 1-3-3zM9 6v6h6V6";
    public const string Detendeur = "M12 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8zM12 11v3a4 4 0 0 0 4 4h4M4 18h4";
    public const string Combinaison = "M8 3h8l2 5-2 1v12H8V9L6 8z";
    public const string Palmes = "M8 3h4v6l5 10a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V9z";
    public const string Masque = "M2.5 9.5a3 3 0 0 1 3-3h13a3 3 0 0 1 3 3v2a3 3 0 0 1-3 3h-4l-2-2h-1l-2 2h-4a3 3 0 0 1-3-3z";
    public const string Ordinateur = "M7 4h10a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM9 9h6M9 13h3";
    public const string Autre = "M5 12h.01M12 12h.01M19 12h.01";

    // --- Types de sortie ---
    public const string Piscine = "M8 3v13M16 3v13M8 7h8M8 11h8M3 20c2 0 2-1.5 4.5-1.5S10 20 12 20s2-1.5 4.5-1.5S19 20 21 20";
    public const string Fosse = "M12 3v14M7 12l5 5 5-5M4 21h16";
    public const string Mer = "M3 9c2 0 2-2 4.5-2S10 9 12 9s2-2 4.5-2S19 9 21 9M3 14c2 0 2-2 4.5-2S10 14 12 14s2-2 4.5-2S19 14 21 14M3 19c2 0 2-2 4.5-2S10 19 12 19s2-2 4.5-2S19 19 21 19";
    public const string Lac = "M3 14l5-7 4 5 3-3 6 5M3 18c2 0 2-1.5 4.5-1.5S10 18 12 18s2-1.5 4.5-1.5S19 18 21 18";
    public const string Bapteme = "M12 3l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.4l-5.2 2.7 1-5.8L3.5 9.2l5.9-.9z";

    private static readonly Dictionary<string, GraphicsPath> Cache = [];

    /// <summary>Pictogramme d'un type de sortie, d'après sa famille.</summary>
    public static string PourTypeSortie(Core.Modeles.TypeSortie famille) => famille switch
    {
        Core.Modeles.TypeSortie.Piscine => Piscine,
        Core.Modeles.TypeSortie.Fosse => Fosse,
        Core.Modeles.TypeSortie.Mer => Mer,
        Core.Modeles.TypeSortie.Lac => Lac,
        Core.Modeles.TypeSortie.Bapteme => Bapteme,
        _ => Autre,
    };

    /// <summary>Nom du pictogramme proposé dans Paramètres › Types de sortie.</summary>
    public static string NomPictogramme(Core.Modeles.TypeSortie famille) => famille switch
    {
        Core.Modeles.TypeSortie.Piscine => "Piscine (bassin)",
        Core.Modeles.TypeSortie.Fosse => "Fosse (flèche vers le bas)",
        Core.Modeles.TypeSortie.Mer => "Mer (vagues)",
        Core.Modeles.TypeSortie.Lac => "Lac (montagne)",
        Core.Modeles.TypeSortie.Bapteme => "Baptême (étoile)",
        _ => "Autre (points)",
    };

    /// <summary>Pictogramme d'une catégorie d'après son nom (catégories de la migration 001).</summary>
    public static string PourCategorie(string? nom) => (nom ?? "").Trim().ToUpperInvariant() switch
    {
        var n when n.StartsWith("BLOC", StringComparison.Ordinal) => Bloc,
        var n when n.StartsWith("GILET", StringComparison.Ordinal) || n.StartsWith("STAB", StringComparison.Ordinal) => Gilet,
        var n when n.StartsWith("DÉTENDEUR", StringComparison.Ordinal) || n.StartsWith("DETENDEUR", StringComparison.Ordinal) => Detendeur,
        var n when n.StartsWith("COMBI", StringComparison.Ordinal) => Combinaison,
        var n when n.StartsWith("PALME", StringComparison.Ordinal) => Palmes,
        var n when n.StartsWith("MASQUE", StringComparison.Ordinal) => Masque,
        var n when n.StartsWith("ORDI", StringComparison.Ordinal) => Ordinateur,
        _ => Autre,
    };

    /// <summary>Dessine le pictogramme centré dans <paramref name="zone"/> (carrée de préférence).</summary>
    public static void Dessiner(Graphics g, string chemin, RectangleF zone, Color couleur, float epaisseur = 1.8f)
    {
        var cote = Math.Min(zone.Width, zone.Height);
        var echelle = cote / 24f;
        var ancien = g.Transform;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(zone.X + ((zone.Width - cote) / 2), zone.Y + ((zone.Height - cote) / 2));
        g.ScaleTransform(echelle, echelle);
        using (var stylo = new Pen(couleur, epaisseur))
        {
            stylo.StartCap = stylo.EndCap = LineCap.Round;
            stylo.LineJoin = LineJoin.Round;
            g.DrawPath(stylo, Chemin(chemin));
        }

        g.Transform = ancien;
        g.SmoothingMode = mode;
    }

    /// <summary>Chemin GDI+ (coordonnées de la grille 24 × 24) d'un chemin SVG, mis en cache.</summary>
    public static GraphicsPath Chemin(string svg)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(svg, out var chemin))
            {
                chemin = CheminSvg.Lire(svg);
                Cache[svg] = chemin;
            }

            return chemin;
        }
    }
}

/// <summary>Lecture minimale de la syntaxe "d" des chemins SVG (M L H V C S Q T A Z, absolues et relatives).</summary>
public static class CheminSvg
{
    public static GraphicsPath Lire(string d)
    {
        var chemin = new GraphicsPath();
        var lecteur = new Lecteur(d);
        PointF courant = default, depart = default, controle = default;
        var precedente = ' ';
        var commande = ' ';

        while (lecteur.Reste())
        {
            if (lecteur.EstCommande())
            {
                commande = lecteur.Commande();
            }
            else if (commande is 'M' or 'm')
            {
                commande = commande == 'M' ? 'L' : 'l'; // Coordonnées implicites après un M : des lignes.
            }

            var relatif = char.IsLower(commande);
            PointF P(float x, float y) => relatif ? new PointF(courant.X + x, courant.Y + y) : new PointF(x, y);

            switch (char.ToUpperInvariant(commande))
            {
                case 'M':
                    courant = depart = P(lecteur.Nombre(), lecteur.Nombre());
                    chemin.StartFigure();
                    break;
                case 'L':
                    {
                        var p = P(lecteur.Nombre(), lecteur.Nombre());
                        Ligne(chemin, courant, p);
                        courant = p;
                        break;
                    }

                case 'H':
                    {
                        var x = lecteur.Nombre();
                        var p = new PointF(relatif ? courant.X + x : x, courant.Y);
                        Ligne(chemin, courant, p);
                        courant = p;
                        break;
                    }

                case 'V':
                    {
                        var y = lecteur.Nombre();
                        var p = new PointF(courant.X, relatif ? courant.Y + y : y);
                        Ligne(chemin, courant, p);
                        courant = p;
                        break;
                    }

                case 'C':
                    {
                        var c1 = P(lecteur.Nombre(), lecteur.Nombre());
                        var c2 = P(lecteur.Nombre(), lecteur.Nombre());
                        var p = P(lecteur.Nombre(), lecteur.Nombre());
                        chemin.AddBezier(courant, c1, c2, p);
                        controle = c2;
                        courant = p;
                        break;
                    }

                case 'S':
                    {
                        var c1 = char.ToUpperInvariant(precedente) is 'C' or 'S' ? Symetrique(controle, courant) : courant;
                        var c2 = P(lecteur.Nombre(), lecteur.Nombre());
                        var p = P(lecteur.Nombre(), lecteur.Nombre());
                        chemin.AddBezier(courant, c1, c2, p);
                        controle = c2;
                        courant = p;
                        break;
                    }

                case 'Q':
                    {
                        var q = P(lecteur.Nombre(), lecteur.Nombre());
                        var p = P(lecteur.Nombre(), lecteur.Nombre());
                        Quadratique(chemin, courant, q, p);
                        controle = q;
                        courant = p;
                        break;
                    }

                case 'T':
                    {
                        var q = char.ToUpperInvariant(precedente) is 'Q' or 'T' ? Symetrique(controle, courant) : courant;
                        var p = P(lecteur.Nombre(), lecteur.Nombre());
                        Quadratique(chemin, courant, q, p);
                        controle = q;
                        courant = p;
                        break;
                    }

                case 'A':
                    {
                        var rx = lecteur.Nombre();
                        var ry = lecteur.Nombre();
                        lecteur.Nombre(); // Rotation : toujours 0 dans nos pictogrammes.
                        var grandArc = lecteur.Drapeau();
                        var sensHoraire = lecteur.Drapeau();
                        var p = P(lecteur.Nombre(), lecteur.Nombre());
                        Arc(chemin, courant, p, rx, ry, grandArc, sensHoraire);
                        courant = p;
                        break;
                    }

                case 'Z':
                    chemin.CloseFigure();
                    courant = depart;
                    break;
                default:
                    throw new FormatException($"Commande SVG non prise en charge : {commande}");
            }

            precedente = commande;
        }

        return chemin;
    }

    private static void Ligne(GraphicsPath chemin, PointF a, PointF b)
    {
        // Un segment de longueur nulle (point "v.01") reste visible grâce aux extrémités rondes.
        chemin.AddLine(a, b);
    }

    private static PointF Symetrique(PointF controle, PointF centre) => new((2 * centre.X) - controle.X, (2 * centre.Y) - controle.Y);

    private static void Quadratique(GraphicsPath chemin, PointF a, PointF q, PointF b)
    {
        var c1 = new PointF(a.X + (2f / 3 * (q.X - a.X)), a.Y + (2f / 3 * (q.Y - a.Y)));
        var c2 = new PointF(b.X + (2f / 3 * (q.X - b.X)), b.Y + (2f / 3 * (q.Y - b.Y)));
        chemin.AddBezier(a, c1, c2, b);
    }

    /// <summary>Arc SVG (extrémités) converti en arc GDI+ (centre et angles), sans rotation d'axe.</summary>
    private static void Arc(GraphicsPath chemin, PointF a, PointF b, float rx, float ry, bool grandArc, bool sensHoraire)
    {
        if (rx == 0 || ry == 0 || a == b)
        {
            chemin.AddLine(a, b);
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        var dx = (a.X - b.X) / 2;
        var dy = (a.Y - b.Y) / 2;
        var lambda = (dx * dx / (rx * rx)) + (dy * dy / (ry * ry));
        if (lambda > 1)
        {
            var k = MathF.Sqrt(lambda);
            rx *= k;
            ry *= k;
        }

        var num = (rx * rx * ry * ry) - (rx * rx * dy * dy) - (ry * ry * dx * dx);
        var den = (rx * rx * dy * dy) + (ry * ry * dx * dx);
        var coef = MathF.Sqrt(Math.Max(0, num / den)) * (grandArc == sensHoraire ? -1 : 1);
        var cxp = coef * rx * dy / ry;
        var cyp = -coef * ry * dx / rx;
        var cx = cxp + ((a.X + b.X) / 2);
        var cy = cyp + ((a.Y + b.Y) / 2);

        static float Angle(float ux, float uy, float vx, float vy)
        {
            var angle = MathF.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
            return angle * 180 / MathF.PI;
        }

        var debut = Angle(1, 0, (dx - cxp) / rx, (dy - cyp) / ry);
        var balayage = Angle((dx - cxp) / rx, (dy - cyp) / ry, (-dx - cxp) / rx, (-dy - cyp) / ry);
        if (!sensHoraire && balayage > 0)
        {
            balayage -= 360;
        }
        else if (sensHoraire && balayage < 0)
        {
            balayage += 360;
        }

        chemin.AddArc(cx - rx, cy - ry, 2 * rx, 2 * ry, debut, balayage);
    }

    private sealed class Lecteur(string texte)
    {
        private int _i;

        public bool Reste()
        {
            Blancs();
            return _i < texte.Length;
        }

        public bool EstCommande()
        {
            Blancs();
            return _i < texte.Length && char.IsLetter(texte[_i]) && texte[_i] is not ('e' or 'E');
        }

        public char Commande() => texte[_i++];

        public bool Drapeau()
        {
            Blancs();
            return texte[_i++] == '1';
        }

        public float Nombre()
        {
            Blancs();
            var debut = _i;
            if (_i < texte.Length && texte[_i] is '-' or '+')
            {
                _i++;
            }

            var point = false;
            while (_i < texte.Length && (char.IsDigit(texte[_i]) || (texte[_i] == '.' && !point)))
            {
                point |= texte[_i] == '.';
                _i++;
            }

            if (_i < texte.Length && texte[_i] is 'e' or 'E')
            {
                _i++;
                if (texte[_i] is '-' or '+')
                {
                    _i++;
                }

                while (_i < texte.Length && char.IsDigit(texte[_i]))
                {
                    _i++;
                }
            }

            if (_i == debut)
            {
                throw new FormatException($"Nombre attendu en position {_i} : {texte}");
            }

            return float.Parse(texte.AsSpan(debut, _i - debut), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void Blancs()
        {
            while (_i < texte.Length && (char.IsWhiteSpace(texte[_i]) || texte[_i] == ','))
            {
                _i++;
            }
        }
    }
}
