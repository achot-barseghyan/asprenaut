using System.Drawing.Drawing2D;
using System.Globalization;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Pictogrammes au trait de la maquette (grille 24 × 24, trait arrondi), décrits en syntaxe de chemin SVG
/// et dessinés en GDI+. Aucune image ni police d'icônes : tout est vectoriel et net à toutes les échelles.
/// </summary>
public static class Icones
{
    // --- Navigation ---
    public const string Sorties = "M3 10c2 0 2-2 4.5-2S10 10 12 10s2-2 4.5-2S19 10 21 10M3 16c2 0 2-2 4.5-2S10 16 12 16s2-2 4.5-2S19 16 21 16";
    public const string Inventaire = "M4 4h16v16H4zM4 10h16M10 10v10";
    public const string Moniteurs = "M9 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM3 20c0-3 3-5 6-5s6 2 6 5M16 5a3 3 0 0 1 0 6M18 15c2 .6 3 2.4 3 5";
    public const string Historique = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 8v4l3 2";
    public const string Parametres = "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM12 3v3M12 18v3M3 12h3M18 12h3";

    // --- Actions ---
    public const string Plus = "M12 5v14M5 12h14";
    public const string Fleche = "M5 12h14M13 6l6 6-6 6";
    public const string Coche = "M5 12l5 5 9-10";
    public const string Croix = "M6 6l12 12M18 6L6 18";
    public const string ChevronGauche = "M15 6l-6 6 6 6";
    public const string ChevronDroite = "M9 6l6 6-6 6";
    public const string Exclamation = "M12 6v8M12 18v.01";
    public const string Recherche = "M11 5a6 6 0 1 0 0 12 6 6 0 0 0 0-12zM20 20l-4.5-4.5";
    public const string Telecharger = "M12 4v11M7 10l5 5 5-5M5 20h14";
    public const string CercleCoche = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM8 12l3 3 5-6";
    public const string Qr = "M4.5 3h4a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-4A1.5 1.5 0 0 1 3 8.5v-4A1.5 1.5 0 0 1 4.5 3z"
        + "M15.5 3h4a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-4A1.5 1.5 0 0 1 14 8.5v-4A1.5 1.5 0 0 1 15.5 3z"
        + "M4.5 14h4a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5h-4A1.5 1.5 0 0 1 3 19.5v-4A1.5 1.5 0 0 1 4.5 14z"
        + "M14 14h3v3h-3zM20 14v.01M14 20h.01M17 20h4v-3";

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
