using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Manuels;
using Asprenaut.Materiel.Data.Migrations;
using Asprenaut.Materiel.Data.Sauvegardes;
using Microsoft.Extensions.DependencyInjection;

namespace Asprenaut.Materiel.App;

/// <summary>Composition de l'application : enregistrement des services et préparation de la base.</summary>
internal static class Demarrage
{
    public static ServiceProvider ConfigurerServices(ParametresApplication parametres)
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton(parametres);
        services.AddSingleton(parametres.Reglages);
        services.AddSingleton<IStockageReglages, StockageReglagesJson>();
        services.AddSingleton<IHorloge, HorlogeSysteme>();
        services.AddSingleton(_ => new FabriqueConnexion(parametres.CheminBase));
        services.AddSingleton<GestionnaireMigrations>();
        services.AddSingleton<IServiceSauvegarde>(sp =>
        {
            var service = new ServiceSauvegarde(
                sp.GetRequiredService<FabriqueConnexion>(),
                ServiceSauvegarde.DossierParDefaut(parametres.CheminBase),
                () => DateTime.Now);

            // Dossier choisi dans Paramètres (clé USB...). S'il est absent (clé débranchée), on reste sur celui par défaut.
            if (parametres.Reglages.DossierSauvegardes is { } dossier && !service.ChangerDossier(dossier).Reussi)
            {
                Erreurs.Journaliser(new IOException($"Dossier de sauvegarde inaccessible : {dossier}. Copie faite dans {service.Dossier}."));
            }

            return service;
        });

        // Dépôts SQLite (sans état : une connexion par appel)
        services.AddSingleton<ICategorieRepository, CategorieRepository>();
        services.AddSingleton<IEquipementRepository, EquipementRepository>();
        services.AddSingleton<IMoniteurRepository, MoniteurRepository>();
        services.AddSingleton<ISortieRepository, SortieRepository>();
        services.AddSingleton<IJournalRepository, JournalRepository>();
        services.AddSingleton<IRevisionRepository, RevisionRepository>();
        services.AddSingleton<IExportRepository, ExportRepository>();
        services.AddSingleton<ITypeSortieRepository, TypeSortieRepository>();
        services.AddSingleton<IManuelRepository, ManuelRepository>();
        services.AddSingleton<ISignalementRepository, SignalementRepository>();
        services.AddSingleton<IAdherentRepository, AdherentRepository>();
        services.AddSingleton<IPrestataireRepository, PrestataireRepository>();
        services.AddSingleton<IStockageManuels>(_ => new StockageManuels(StockageManuels.DossierParDefaut(parametres.CheminBase)));

        // Services métier (Core)
        services.AddSingleton<ServiceScan>();
        services.AddSingleton<ServiceSortie>();
        services.AddSingleton<ServiceInventaire>();
        services.AddSingleton<ServiceMoniteurs>();
        services.AddSingleton<ServiceHistorique>();
        services.AddSingleton<ServiceBilan>();
        services.AddSingleton<ServiceExport>();
        services.AddSingleton<ServiceCompteur>();
        services.AddSingleton<ServiceTypesSortie>();
        services.AddSingleton<ServiceGestion>();
        services.AddSingleton<ServiceReglages>();
        services.AddSingleton<ServiceRevisions>();
        services.AddSingleton<ServiceManuels>();
        services.AddSingleton<ServiceSignalements>();
        services.AddSingleton<ServiceAdherents>();
        services.AddSingleton<ServicePrestataires>();

        // Interface. Les écrans ne sont pas enregistrés : ServiceNavigation les crée à la demande.
        services.AddSingleton<ServiceNavigation>();
        services.AddSingleton<INavigation>(sp => sp.GetRequiredService<ServiceNavigation>());
        services.AddSingleton<GardeGestion>();
        services.AddSingleton<MainForm>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Sauvegarde (avant toute modification), migrations, puis données de démo en Debug.</summary>
    public static async Task PreparerBaseAsync(IServiceProvider services)
    {
        try
        {
            await services.GetRequiredService<IServiceSauvegarde>().SauvegarderAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Erreurs.Journaliser(ex);
            Dialogues.Erreur(null, "La sauvegarde automatique a échoué. L'application démarre quand même.\n\n" + ex.Message);
        }

        await services.GetRequiredService<GestionnaireMigrations>().AppliquerAsync();

#if DEBUG
        await Data.Demo.DonneesDemo.ChargerSiVideAsync(
            services.GetRequiredService<FabriqueConnexion>(),
            services.GetRequiredService<IHorloge>().AujourdHui);
#endif

        // Notices des fabricants livrées avec l'appli : installées une seule fois. Un échec ne bloque pas le démarrage.
        try
        {
            await ManuelsFournis.InstallerAsync(services.GetRequiredService<ServiceManuels>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Erreurs.Journaliser(ex);
        }
    }
}
