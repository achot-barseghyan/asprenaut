using Asprenaut.Materiel.App.Ecrans;
using Microsoft.Extensions.DependencyInjection;

namespace Asprenaut.Materiel.App.Navigation;

/// <summary>Entrées du menu latéral.</summary>
public enum EntreeMenu
{
    Sorties,
    Inventaire,
    Moniteurs,
    Historique,
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

    public void AllerA<TEcran>(object? parametre = null)
        where TEcran : EcranBase
    {
        var hote = _hote ?? throw new InvalidOperationException("La zone de contenu n'est pas attachée.");

        // CreateInstance (et non GetService) : l'écran n'est pas retenu par le conteneur,
        // il est libéré ici quand on change d'écran.
        var ecran = ActivatorUtilities.CreateInstance<TEcran>(services);
        var ancien = EcranCourant;

        hote.SuspendLayout();
        Apparence.Dessin.MettreALEchelle(ecran, hote); // Écrans construits en pixels logiques (96 dpi).
        ecran.Dock = DockStyle.Fill;
        hote.Controls.Add(ecran);
        if (ancien is not null)
        {
            hote.Controls.Remove(ancien);
            ancien.Dispose();
        }

        hote.ResumeLayout(true);
        EcranCourant = ecran;
        EcranChange?.Invoke(this, ecran);
        ecran.Demarrer(parametre);
    }
}
