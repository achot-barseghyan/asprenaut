using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>
/// Pages d'un PDF transformées en images par Windows (Windows.Data.Pdf, Windows 10 et 11) : aucune bibliothèque à ajouter.
/// Le fichier est lu en mémoire puis relâché : il peut être retiré ou remplacé pendant qu'il est affiché.
/// Les attentes internes n'ont pas besoin du fil de l'interface : <see cref="RendrePage"/> peut être appelé en bloquant
/// (impression) sans risque d'interblocage.
/// </summary>
public sealed class LecteurPdf
{
    private readonly PdfDocument _document;

    // Gardé tant que le document est utilisé : Windows relit les pages dans ce flux.
    private readonly InMemoryRandomAccessStream _flux;

    private LecteurPdf(PdfDocument document, InMemoryRandomAccessStream flux)
    {
        _document = document;
        _flux = flux;
    }

    public int NbPages => (int)_document.PageCount;

    /// <summary>Ouvre un PDF ; lève une exception s'il est illisible ou protégé par un mot de passe.</summary>
    public static async Task<LecteurPdf> OuvrirAsync(string chemin)
    {
        var octets = await File.ReadAllBytesAsync(chemin).ConfigureAwait(false);
        var flux = new InMemoryRandomAccessStream();
        var ecriture = flux.AsStreamForWrite();
        await ecriture.WriteAsync(octets).ConfigureAwait(false);
        await ecriture.FlushAsync().ConfigureAwait(false);
        flux.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(flux).AsTask().ConfigureAwait(false);
        return new LecteurPdf(document, flux);
    }

    /// <summary>Nombre de pages, ou 0 si Windows ne sait pas lire le fichier.</summary>
    public static async Task<int> CompterPagesAsync(string chemin)
    {
        try
        {
            return (await OuvrirAsync(chemin).ConfigureAwait(false)).NbPages;
        }
        catch (Exception ex) when (EstIllisible(ex))
        {
            return 0;
        }
    }

    /// <summary>Erreurs d'un PDF abîmé, protégé ou inaccessible (à montrer simplement, sans journal d'erreur).</summary>
    public static bool EstIllisible(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException;

    /// <summary>Image d'une page à la largeur voulue (pixels), fond blanc. <paramref name="page"/> commence à 1.</summary>
    public async Task<Bitmap> RendrePageAsync(int page, int largeur)
    {
        using var p = _document.GetPage((uint)(Math.Clamp(page, 1, NbPages) - 1));
        using var sortie = new InMemoryRandomAccessStream();
        await p.RenderToStreamAsync(sortie, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Clamp(largeur, 16, 6000),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        }).AsTask().ConfigureAwait(false);
        using var lecture = sortie.AsStreamForRead();
        using var image = new Bitmap(lecture);
        return new Bitmap(image); // Copie détachée du flux, qui va être fermé.
    }

    /// <summary>Version bloquante, pour l'impression (événement PrintPage synchrone).</summary>
    public Bitmap RendrePage(int page, int largeur) => Task.Run(() => RendrePageAsync(page, largeur)).GetAwaiter().GetResult();

    /// <summary>Le flux reste vivant aussi longtemps que le lecteur (aucune ressource à libérer avant).</summary>
    public override string ToString() => $"PDF de {NbPages} pages ({_flux.Size} octets)";
}
