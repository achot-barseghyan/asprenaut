using Asprenaut.Materiel.App.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Asprenaut.Materiel.App;

internal static class Program
{
    /// <summary>
    /// Point d'entrée. Option : <c>--base &lt;chemin&gt;</c> pour ouvrir une autre base
    /// (essais, démonstration) sans modifier les paramètres enregistrés.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Applique les réglages du .csproj : HighDpiMode.PerMonitorV2, styles visuels, rendu du texte.
        ApplicationConfiguration.Initialize();
        Erreurs.Installer();

        ParametresApplication parametres;
        var indexBase = Array.IndexOf(args, "--base");
        if (indexBase >= 0 && indexBase + 1 < args.Length)
        {
            // Base d'essai : ses paramètres sont à côté d'elle, les réglages réels du club restent intacts.
            var cheminBase = Path.GetFullPath(args[indexBase + 1]);
            parametres = ParametresApplication.Charger(Path.Combine(Path.GetDirectoryName(cheminBase) ?? ".", "parametres.json"));
            parametres.CheminBase = cheminBase;
        }
        else
        {
            parametres = ParametresApplication.Charger();
        }

        ServiceProvider services;
        try
        {
            services = Demarrage.ConfigurerServices(parametres);
            Demarrage.PreparerBaseAsync(services).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Erreurs.Journaliser(ex);
            Dialogues.Erreur(null, $"Impossible d'ouvrir la base de données :\n{parametres.CheminBase}\n\n{ex.Message}");
            return;
        }

        using (services)
        {
            Application.Run(services.GetRequiredService<MainForm>());
        }
    }
}
