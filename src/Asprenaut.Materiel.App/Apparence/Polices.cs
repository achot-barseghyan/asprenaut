using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>
/// Polices de la maquette embarquées dans l'exe (Ressources\Polices, licence SIL OFL) : Sora pour les titres et les
/// chiffres, Manrope pour le texte. Elles marchent sans être installées sur le PC du club.
/// Chaque fichier est chargé deux fois : dans une <see cref="PrivateFontCollection"/> (GDI+ : objets <see cref="Font"/>,
/// <c>DrawString</c>, PDF) et par <c>AddFontMemResourceEx</c> (GDI : <c>TextRenderer</c>, champs de saisie Windows),
/// sinon GDI remplacerait la police par une autre en retrouvant son nom.
/// </summary>
internal static class Polices
{
    private static readonly string[] Fichiers =
    [
        "Manrope-Regular.ttf", "Manrope-SemiBold.ttf", "Manrope-Bold.ttf", "Manrope-ExtraBold.ttf",
        "Sora-SemiBold.ttf", "Sora-Bold.ttf",
    ];

    // Gardée toute la vie de l'application : les Font créées depuis ses familles en dépendent.
    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<string, FontFamily> Familles = Charger();

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr donnees, uint taille, IntPtr reserve, ref uint nombre);

    /// <summary>
    /// Famille embarquée <paramref name="nom"/> (« Manrope », « Manrope SemiBold », « Sora SemiBold »…), sinon la première
    /// installée parmi <paramref name="replis"/>, sinon Segoe UI.
    /// </summary>
    public static FontFamily Famille(string nom, params string[] replis)
    {
        if (Familles.TryGetValue(nom, out var famille))
        {
            return famille;
        }

        foreach (var repli in replis)
        {
            using var essai = new Font(repli, 10f);
            if (string.Equals(essai.Name, repli, StringComparison.OrdinalIgnoreCase))
            {
                return new FontFamily(repli);
            }
        }

        return new FontFamily("Segoe UI");
    }

    private static Dictionary<string, FontFamily> Charger()
    {
        var assemblage = typeof(Polices).Assembly;
        foreach (var fichier in Fichiers)
        {
            try
            {
                using var flux = assemblage.GetManifestResourceStream("Polices." + fichier);
                if (flux is null)
                {
                    continue;
                }

                var octets = new byte[flux.Length];
                flux.ReadExactly(octets);

                // Mémoire jamais libérée : GDI+ lit la police dedans tant que l'application tourne.
                var donnees = Marshal.AllocCoTaskMem(octets.Length);
                Marshal.Copy(octets, 0, donnees, octets.Length);
                Collection.AddMemoryFont(donnees, octets.Length);
                uint nombre = 0;
                AddFontMemResourceEx(donnees, (uint)octets.Length, IntPtr.Zero, ref nombre);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
            {
                // Police illisible : l'écran se rabat sur Segoe UI.
            }
        }

        return Collection.Families.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
    }
}
