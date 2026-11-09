using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Classe de base des écrans. Un écran ne contient que de la mise en page et des appels
/// aux services de Core : aucune règle métier ici.
/// </summary>
public abstract class EcranBase : UserControl
{
    protected EcranBase()
    {
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = Theme.Fond;
        ForeColor = Theme.Texte;
        Font = Theme.Police();
        DoubleBuffered = true;
        Margin = Padding.Empty;
    }

    /// <summary>Titre de l'écran.</summary>
    public abstract string Titre { get; }

    /// <summary>Entrée du menu latéral à mettre en évidence.</summary>
    public abstract EntreeMenu Menu { get; }

    /// <summary>Chargement des données à l'affichage. <paramref name="parametre"/> vient de la navigation.</summary>
    protected virtual Task ChargerAsync(object? parametre) => Task.CompletedTask;

    /// <summary>Lance le chargement ; une erreur est affichée simplement à l'utilisateur.</summary>
    internal Task Chargement { get; private set; } = Task.CompletedTask;

    internal async void Demarrer(object? parametre)
    {
        try
        {
            Chargement = ChargerAsync(parametre);
            await Chargement;
        }
        catch (Exception ex)
        {
            Erreurs.Signaler(this, ex);
        }
    }

    /// <summary>Exécute une action asynchrone d'un gestionnaire d'événement, avec gestion d'erreur.</summary>
    protected async void Executer(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Erreurs.Signaler(this, ex);
        }
    }
}
