using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Impression;

/// <summary>Un morceau de page : hauteur fixe, dessiné en (x, y, largeur). Un titre se garde avec le bloc qui le suit.</summary>
public sealed record BlocPdf(float Hauteur, Action<Graphics, float, float, float> Dessiner, bool GarderAvecSuivant = false);

/// <summary>
/// Documents PDF sans bibliothèque : les pages sont dessinées en GDI+ et envoyées à l'imprimante « Microsoft Print to PDF »
/// de Windows, qui écrit le fichier sans rien demander. Toujours en couleurs du thème clair (papier blanc).
/// Coordonnées en centièmes de pouce (unité des pages imprimées) ; A4 = 827 × 1169.
/// Le contenu est une suite de <see cref="BlocPdf"/> placés de haut en bas, page après page.
/// </summary>
public static class DocumentPdf
{
    public const string ImprimantePdf = "Microsoft Print to PDF";

    /// <summary>Palette du papier.</summary>
    public static Palette P => Theme.PaletteClaire;

    public static bool ImprimanteDisponible() =>
        PrinterSettings.InstalledPrinters.Cast<string>().Any(n => string.Equals(n, ImprimantePdf, StringComparison.OrdinalIgnoreCase));

    /// <summary>Écrit le document dans <paramref name="chemin"/> (remplacé s'il existe) ; pied de page « … · page 2 ».</summary>
    public static void Enregistrer(string nomDocument, string piedDePage, IReadOnlyList<BlocPdf> blocs, string chemin)
    {
        if (File.Exists(chemin))
        {
            File.Delete(chemin); // Ouvert ailleurs : IOException, message simple à l'écran.
        }

        var suivant = 0;
        var page = 0;
        using var document = new PrintDocument
        {
            DocumentName = nomDocument,
            PrintController = new StandardPrintController(), // Pas de fenêtre « Impression en cours ».
        };
        document.PrinterSettings.PrinterName = ImprimantePdf;
        document.PrinterSettings.PrintToFile = true;
        document.PrinterSettings.PrintFileName = chemin;
        document.DefaultPageSettings.Margins = new Margins(60, 60, 55, 55);
        var a4 = document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4);
        if (a4 is not null)
        {
            document.DefaultPageSettings.PaperSize = a4;
        }

        document.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var zone = e.MarginBounds;
            var bas = zone.Bottom - 24f; // Place du numéro de page.
            var y = (float)zone.Top;
            page++;
            while (suivant < blocs.Count)
            {
                var bloc = blocs[suivant];
                var besoin = bloc.Hauteur + (bloc.GarderAvecSuivant && suivant + 1 < blocs.Count ? blocs[suivant + 1].Hauteur : 0);
                if (y + besoin > bas && y > zone.Top)
                {
                    break;
                }

                bloc.Dessiner(g, zone.Left, y, zone.Width);
                y += bloc.Hauteur;
                suivant++;
            }

            Texte(g, $"{piedDePage} · page {page}", Theme.Police(7.5f), new RectangleF(zone.Left, zone.Bottom - 16, zone.Width, 16), P.TexteSecondaire, StringAlignment.Center);
            e.HasMorePages = suivant < blocs.Count;
        };
        document.Print();
    }

    // ---------------------------------------------------------------- Blocs courants

    /// <summary>En-tête de document : nom du club, titre, précision.</summary>
    public static BlocPdf Titre(string club, string titre, string precision) => new(96, (g, x, y, l) =>
    {
        Texte(g, club.ToUpper(Libelles.Culture), Theme.PoliceTitre(9f), new RectangleF(x, y, l, 18), P.Accent);
        Texte(g, titre, Theme.PoliceGrasse(20f), new RectangleF(x, y + 18, l, 40), P.Texte);
        Texte(g, precision, Theme.Police(9f), new RectangleF(x, y + 60, l, 18), P.TexteSecondaire);
    });

    public static BlocPdf TitreSection(string titre) => new(40, (g, x, y, l) =>
        Texte(g, titre, Theme.PoliceTitre(11.25f), new RectangleF(x, y + 14, l, 22), P.Texte), GarderAvecSuivant: true);

    public static BlocPdf LigneVide(string texte) => new(22, (g, x, y, l) =>
        Texte(g, texte, Theme.Police(9f), new RectangleF(x, y + 3, l, 18), P.TexteSecondaire));

    // ---------------------------------------------------------------- Dessin

    public static void EnteteTableau(Graphics g, float x, float y, float l, string[] titres, float[] positions)
    {
        for (var i = 0; i < titres.Length; i++)
        {
            var fin = i + 1 < positions.Length ? positions[i + 1] : 1f;
            Texte(g, titres[i].ToUpper(Libelles.Culture), Theme.PoliceTitre(7f), new RectangleF(x + (l * positions[i]), y + 4, l * (fin - positions[i]), 16), P.TexteSecondaire);
        }
    }

    public static void Filet(Graphics g, float x, float y, float l)
    {
        using var stylo = new Pen(P.Bordure, 0.75f);
        g.DrawLine(stylo, x, y, x + l, y);
    }

    public static void Cadre(Graphics g, RectangleF r, Color fond, Color bordure)
    {
        using var chemin = Dessin.Arrondi(r, 8);
        using var pinceau = new SolidBrush(fond);
        using var stylo = new Pen(bordure, 0.75f);
        g.FillPath(pinceau, chemin);
        g.DrawPath(stylo, chemin);
    }

    public static void Barre(Graphics g, RectangleF piste, double part, Color couleur)
    {
        using (var chemin = Dessin.Arrondi(piste, piste.Height / 2))
        using (var fond = new SolidBrush(P.SurfaceActive))
        {
            g.FillPath(fond, chemin);
        }

        if (part > 0)
        {
            using var chemin = Dessin.Arrondi(piste with { Width = Math.Max(piste.Height, (float)(piste.Width * Math.Min(1, part))) }, piste.Height / 2);
            using var pinceau = new SolidBrush(couleur);
            g.FillPath(pinceau, chemin);
        }
    }

    public static void Texte(Graphics g, string texte, Font police, RectangleF zone, Color couleur, StringAlignment alignement = StringAlignment.Near, bool retour = false)
    {
        using var format = new StringFormat
        {
            Alignment = alignement,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = retour ? 0 : StringFormatFlags.NoWrap,
        };
        using var pinceau = new SolidBrush(couleur);
        g.DrawString(texte, police, pinceau, zone, format);
    }
}
