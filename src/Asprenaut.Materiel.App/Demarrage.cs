using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Connexion;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Club;
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
        services.AddSingleton<IModeleSortieRepository, ModeleSortieRepository>();
        services.AddSingleton<IStockageManuels>(_ => new StockageManuels(StockageManuels.DossierParDefaut(parametres.CheminBase)));
        services.AddSingleton<IStockageLogo>(_ => new StockageLogo(StockageLogo.DossierParDefaut(parametres.CheminBase)));
        services.AddSingleton<IReglagesSauvegardables, ReglagesSauvegardables>();
        services.AddSingleton<ISauvegardeComplete>(sp => new SauvegardeComplete(
            sp.GetRequiredService<FabriqueConnexion>(),
            sp.GetRequiredService<IServiceSauvegarde>(),
            sp.GetRequiredService<IReglagesSauvegardables>(),
            StockageManuels.DossierParDefaut(parametres.CheminBase),
            () => DateTime.Now,
            StockageLogo.DossierParDefaut(parametres.CheminBase)));

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
        services.AddSingleton<LogoDuClub>();
        services.AddSingleton<ServiceRevisions>();
        services.AddSingleton<ServiceManuels>();
        services.AddSingleton<ServiceSignalements>();
        services.AddSingleton<ServiceAdherents>();
        services.AddSingleton<ServicePrestataires>();
        services.AddSingleton<ServiceModelesSortie>();

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
        // Seulement dans une base d'essai (--base) : la base réelle du club ne reçoit jamais les données de démo,
        // même lancée depuis le code (dotnet run) avant qu'on y ait saisi quoi que ce soit.
        if (services.GetRequiredService<ParametresApplication>().BaseEssai)
        {
            await Data.Demo.DonneesDemo.ChargerSiVideAsync(
                services.GetRequiredService<FabriqueConnexion>(),
                services.GetRequiredService<IHorloge>().AujourdHui);
        }
#endif

        // Un enseignant est un adhérent : chaque fiche d'enseignant est reliée à la fiche d'adhérent de la même personne (même
        // prénom et nom, une seule) ou en reçoit une ; les cas douteux attendent dans Membres (« enseignants à relier »).
        // Sans effet une fois tout relié. Un échec ne bloque pas le démarrage.
        try
        {
            await services.GetRequiredService<ServiceAdherents>().RelierEnseignantsAsync();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            Erreurs.Journaliser(ex);
        }

        // Pictogramme choisi pour chaque catégorie (Lampes, Plombs…), utilisé partout où une catégorie est dessinée.
        Apparence.Icones.RetenirCategories(await services.GetRequiredService<ServiceInventaire>().ListerCategoriesAsync());

        // Pictogramme et couleur choisis pour chaque type de sortie, dessinés partout où un type n'est connu que par son nom.
        Apparence.TypesSortieRetenus.Retenir(await services.GetRequiredService<ServiceTypesSortie>().ListerAsync());

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
