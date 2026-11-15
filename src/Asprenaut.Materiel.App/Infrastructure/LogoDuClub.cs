using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>
/// Image du logo du club pour l'accueil et Paramètres › Club : celle fournie avec l'appli (ressource LogoClub.png), celle choisie
/// par le club (Club\logo.png à côté de la base), ou aucune (les initiales du club la remplacent). Gardée en mémoire jusqu'au
/// prochain <see cref="Recharger"/>.
/// </summary>
public sealed class LogoDuClub(ServiceReglages reglages)
{
    /// <summary>Côté maximal de l'image rangée : largement assez pour la tuile de 150 px à 200 % d'échelle.</summary>
    public const int CoteMax = 512;

    private static Image? _fourni;
    private Image? _choisi;
    private bool _choisiLu;

    /// <summary>Logo fourni avec l'appli (logo Asprenaut).</summary>
    public static Image Fourni
    {
        get
        {
            if (_fourni is null)
            {
                using var flux = typeof(LogoDuClub).Assembly.GetManifestResourceStream("LogoClub.png")
                    ?? throw new InvalidOperationException("Logo du club introuvable (ressource LogoClub.png).");
                using var lu = Image.FromStream(flux);
                _fourni = new Bitmap(lu); // Copie : l'image reste valable après la fermeture du flux.
            }

            return _fourni;
        }
    }

    /// <summary>Logo à afficher, ou null : pas de logo (ou image choisie disparue), les initiales du club le remplacent.</summary>
    public Image? Image => reglages.Actuels.LogoClub switch
    {
        ChoixLogo.Fourni => Fourni,
        ChoixLogo.Personnalise => Choisi(),
        _ => null,
    };

    public string Initiales => ServiceReglages.InitialesClub(reglages.Actuels.NomClub);

    /// <summary>Relit l'image choisie (après un enregistrement ou une restauration).</summary>
    public void Recharger()
    {
        _choisi?.Dispose();
        _choisi = null;
        _choisiLu = false;
    }

    private Image? Choisi()
    {
        if (!_choisiLu)
        {
            _choisiLu = true;
            if (reglages.CheminLogo() is { } chemin)
            {
                try
                {
                    // Lu en mémoire : le fichier n'est jamais verrouillé (il peut être remplacé pendant que l'appli tourne).
                    using var flux = new MemoryStream(File.ReadAllBytes(chemin));
                    using var lu = Image.FromStream(flux);
                    _choisi = new Bitmap(lu);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
                {
                    _choisi = null;
                }
            }
        }

        return _choisi;
    }

    /// <summary>Image choisie pour devenir le logo, prête à enregistrer (PNG) et à montrer dans l'aperçu.</summary>
    public sealed record ImagePreparee(byte[] Png, Bitmap Apercu);

    /// <summary>
    /// Lit le fichier choisi (format et poids vérifiés par <see cref="ServiceReglages.VerifierFichierLogo"/>), le réduit à
    /// <see cref="CoteMax"/> px au plus et le met en PNG (transparence gardée). Erreur : message simple, image null.
    /// </summary>
    public static (ImagePreparee? Image, string? Erreur) Preparer(string chemin)
    {
        try
        {
            var infos = new FileInfo(chemin);
            if (ServiceReglages.VerifierFichierLogo(infos.Name, infos.Exists ? infos.Length : 0) is { } erreur)
            {
                return (null, erreur);
            }

            using var flux = new MemoryStream(File.ReadAllBytes(chemin));
            using var lu = Image.FromStream(flux);
            var echelle = Math.Min(1.0, (double)CoteMax / Math.Max(lu.Width, lu.Height));
            var largeur = Math.Max(1, (int)Math.Round(lu.Width * echelle));
            var hauteur = Math.Max(1, (int)Math.Round(lu.Height * echelle));
            var image = new Bitmap(largeur, hauteur, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(image))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(lu, 0, 0, largeur, hauteur);
            }

            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            if (png.Length > ServiceReglages.TailleMaxLogo)
            {
                image.Dispose();
                return (null, "Image trop détaillée une fois réduite. Essayez une autre image.");
            }

            return (new ImagePreparee(png.ToArray(), image), null);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            return (null, "Cette image est illisible. Essayez une autre image (PNG ou JPG).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "Impossible de lire ce fichier.");
        }
    }
}
