using System.Runtime.InteropServices;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>Appels Windows pour la barre de titre sombre et les barres de défilement sombres.</summary>
internal static class Natif
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE : 20 depuis Windows 10 20H1, 19 sur les versions antérieures.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeAncien = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int valeur, int taille);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? nomApplication, string? listeIds);

    /// <summary>Passe la barre de titre Windows en sombre (sans effet sur les versions non prises en charge).</summary>
    public static void BarreDeTitreSombre(Form fenetre)
    {
        var actif = 1;
        if (DwmSetWindowAttribute(fenetre.Handle, DwmwaUseImmersiveDarkMode, ref actif, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(fenetre.Handle, DwmwaUseImmersiveDarkModeAncien, ref actif, sizeof(int));
        }
    }

    /// <summary>Barres de défilement sombres (thème "DarkMode_Explorer") pour un contrôle et ses barres internes.</summary>
    public static void ScrollbarsSombres(Control controle)
    {
        void Appliquer(Control c)
        {
            SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            foreach (Control enfant in c.Controls)
            {
                if (enfant is ScrollBar)
                {
                    SetWindowTheme(enfant.Handle, "DarkMode_Explorer", null);
                }
            }
        }

        if (controle.IsHandleCreated)
        {
            Appliquer(controle);
        }

        controle.HandleCreated += (_, _) => Appliquer(controle);
    }
}
