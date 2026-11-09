using System.Runtime.InteropServices;

namespace Asprenaut.Materiel.App.Apparence;

/// <summary>Appels Windows : barre de titre et barres de défilement assorties au thème, gel de l'affichage.</summary>
internal static class Natif
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE : 20 depuis Windows 10 20H1, 19 sur les versions antérieures.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeAncien = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int valeur, int taille);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? nomApplication, string? listeIds);

    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001, RdwErase = 0x0004, RdwAllChildren = 0x0080, RdwUpdateNow = 0x0100, RdwFrame = 0x0400;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    /// <summary>
    /// Gèle l'affichage de <paramref name="controle"/> : l'écran garde l'image actuelle tant que
    /// <see cref="Degeler"/> n'est pas appelé, même si on ajoute, construit ou retire des contrôles dedans.
    /// </summary>
    public static void Geler(Control controle)
    {
        if (controle.IsHandleCreated)
        {
            SendMessage(controle.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Dégèle et redessine d'un seul coup (enfants compris) : pas d'image intermédiaire à l'écran.</summary>
    public static void Degeler(Control controle)
    {
        if (controle.IsHandleCreated)
        {
            SendMessage(controle.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);

            RedrawWindow(controle.Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow | RdwFrame);
        }
    }

    /// <summary>Barre de titre Windows sombre ou claire selon le thème (sans effet sur les versions non prises en charge).</summary>
    public static void BarreDeTitre(Form fenetre)
    {
        var actif = Theme.EstSombre ? 1 : 0;
        if (DwmSetWindowAttribute(fenetre.Handle, DwmwaUseImmersiveDarkMode, ref actif, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(fenetre.Handle, DwmwaUseImmersiveDarkModeAncien, ref actif, sizeof(int));
        }
    }

    /// <summary>Barres de défilement du thème ("DarkMode_Explorer" en sombre, "Explorer" en clair), barres internes comprises.</summary>
    public static void ScrollbarsDuTheme(Control controle)
    {
        void Appliquer(Control c)
        {
            var theme = Theme.EstSombre ? "DarkMode_Explorer" : "Explorer";
            SetWindowTheme(c.Handle, theme, null);
            foreach (Control enfant in c.Controls)
            {
                if (enfant is ScrollBar)
                {
                    SetWindowTheme(enfant.Handle, theme, null);
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
