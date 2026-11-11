using Asprenaut.Materiel.App.Ecrans;
using Microsoft.Extensions.DependencyInjection;

namespace Asprenaut.Materiel.App.Navigation;

/// <summary>Entrées du menu latéral.</summary>
public enum EntreeMenu
{
    Sorties,
    Inventaire,
    Manuels,
    Moniteurs,
    Historique,
    Bilan,
    Revisions,
    Parametres,
}

/// <summary>Navigation entre écrans : un seul écran (UserControl) affiché à la fois dans la zone de contenu.</summary>
public interface INavigation
{
    /// <summary>Affiche l'écran <typeparamref name="TEcran"/>, avec un paramètre optionnel (ex. id de sortie).</summary>
    void AllerA<TEcran>(object? parametre = null)
        where TEcran : EcranBase;

    EcranBase? EcranCourant { get; }

    event EventHandler<EcranBase>? EcranChange;
}

public sealed class ServiceNavigation(IServiceProvider services) : INavigation
{
    private Control? _hote;

    public EcranBase? EcranCourant { get; private set; }

    public event EventHandler<EcranBase>? EcranChange;

    /// <summary>Appelé par la fenêtre principale : zone où les écrans sont affichés.</summary>
    public void Attacher(Control hote) => _hote = hote;

    /// <summary>
    /// Attente maximale du chargement des données avant d'afficher quand même le nouvel écran
    /// (base lente, gros volume) : l'utilisateur ne reste jamais bloqué sur l'ancien.
    /// </summary>
    private const int AttenteMaxChargementMs = 600;

    private int _generation;

    public async void AllerA<TEcran>(object? parametre = null)
        where TEcran : EcranBase
    {
        try
        {
            await AllerAAsync<TEcran>(parametre);
        }
        catch (Exception ex)
        {
            Infrastructure.Erreurs.Signaler(_hote?.FindForm(), ex);
        }
    }

    /// <summary>
    /// Le nouvel écran est construit, mis en page et rempli DERRIÈRE l'écran actuel, qui reste affiché ;
    /// puis on bascule d'un coup. Sinon on verrait un écran vide se remplir morceau par morceau (clignotement).
    /// </summary>
    private async Task AllerAAsync<TEcran>(object? parametre)
        where TEcran : EcranBase
    {
        var hote = _hote ?? throw new InvalidOperationException("La zone de contenu n'est pas attachée.");
        var generation = ++_generation;

        // CreateInstance (et non GetService) : l'écran n'est pas retenu par le conteneur,
        // il est libéré ici quand on change d'écran.
        var ecran = ActivatorUtilities.CreateInstance<TEcran>(services);
        Apparence.Dessin.MettreALEchelle(ecran, hote); // Écrans construits en pixels logiques (96 dpi).

        // Préparé hors de la zone visible (à sa taille finale, juste à droite) : Windows le découpe,
        // rien n'apparaît à l'écran tant qu'il se construit et se remplit.
        var zone = hote.DisplayRectangle;
        ecran.Dock = DockStyle.None;
        ecran.Bounds = new Rectangle(zone.Right + zone.Width, zone.Top, zone.Width, zone.Height);
        hote.Controls.Add(ecran);
        EcranChange?.Invoke(this, ecran); // Le menu réagit tout de suite au clic.

        ecran.Demarrer(parametre);
        await Task.WhenAny(ecran.Chargement, Task.Delay(AttenteMaxChargementMs));

        if (generation != _generation || ecran.IsDisposed)
        {
            // Un autre écran a été demandé entre-temps : celui-ci ne sera jamais montré.
            hote.Controls.Remove(ecran);
            ecran.Dispose();
            return;
        }

        // Laisse passer les ajustements différés (hauteur des grilles de cartes, des filtres...) tant que l'écran
        // est hors de vue : sinon ils se verraient après la bascule, l'écran « bougerait » encore.
        for (var i = 0; i < 4 && !ecran.IsDisposed; i++)
        {
            await Task.Yield();
            ecran.PerformLayout();
        }

        if (generation != _generation || ecran.IsDisposed)
        {
            hote.Controls.Remove(ecran);
            ecran.Dispose();
            return;
        }

        // Premier dessin fait hors écran, dans une image jetée : le code de dessin, les polices et les formes
        // sont prêts, si bien que l'affichage réel après la bascule se fait d'un trait.
        Prechauffer(ecran);

        // Bascule gelée : l'ancien écran disparaît et le nouveau apparaît dans le même affichage.
        var ancien = EcranCourant;
        EcranCourant = ecran;
        Apparence.Natif.Geler(hote);
        try
        {
            hote.SuspendLayout();
            if (ancien is not null)
            {
                hote.Controls.Remove(ancien);
            }

            ecran.Dock = DockStyle.Fill;
            hote.ResumeLayout(true);
        }
        finally
        {
            Apparence.Natif.Degeler(hote);
        }

        if (ancien is not null)
        {
            hote.BeginInvoke(ancien.Dispose); // Libéré après l'affichage du nouvel écran.
        }
    }

    private static void Prechauffer(Control ecran)
    {
        if (ecran.Width <= 0 || ecran.Height <= 0)
        {
            return;
        }

        try
        {
            using var image = new Bitmap(ecran.Width, ecran.Height);
            ecran.DrawToBitmap(image, new Rectangle(Point.Empty, ecran.Size));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Simple optimisation : sans préchauffage, l'écran s'affiche quand même.
        }
    }
}
