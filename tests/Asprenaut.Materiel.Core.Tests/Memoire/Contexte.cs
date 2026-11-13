using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.Core.Tests.Memoire;

/// <summary>Contexte de test : base en mémoire, services, et petites fabriques de données.</summary>
public sealed class Contexte
{
    public static readonly DateTime Maintenant = new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

    public BaseMemoire Db { get; } = new();
    public HorlogeFixe Horloge { get; } = new(Maintenant);
    public ServiceScan Scan { get; }
    public ServiceSortie Sorties { get; }
    public ServiceInventaire Inventaire { get; }
    public ServiceReglages ServiceReglages { get; }
    public Reglages Reglages { get; } = new();
    public StockageReglagesMemoire Stockage { get; } = new();
    public SauvegardeFactice Sauvegardes { get; } = new();
    public ServiceRevisions Revisions { get; }
    public RevisionRepositoryMemoire DepotRevisions { get; }
    public ServiceMoniteurs Moniteurs { get; }
    public ServiceHistorique Historique { get; }
    public ServiceGestion Gestion { get; }
    public ServiceBilan Bilan { get; }
    public ServiceCompteur Compteur { get; }
    public Moniteur Pascal { get; }
    public ServiceTypesSortie TypesSortie { get; }
    public ServiceSignalements Signalements { get; }
    public SignalementRepositoryMemoire DepotSignalements { get; }
    public ServiceAdherents Adherents { get; }
    public AdherentRepositoryMemoire DepotAdherents { get; }
    public ServicePrestataires Prestataires { get; }
    public PrestataireRepositoryMemoire DepotPrestataires { get; }

    public Contexte()
    {
        var equipements = new EquipementRepositoryMemoire(Db);
        var sorties = new SortieRepositoryMemoire(Db);
        var journal = new JournalRepositoryMemoire(Db);
        var moniteurs = new MoniteurRepositoryMemoire(Db);
        var categories = new CategorieRepositoryMemoire(Db);
        DepotRevisions = new RevisionRepositoryMemoire(Db);
        Gestion = new ServiceGestion(Reglages, Stockage, Horloge);
        Compteur = new ServiceCompteur(Reglages, Stockage, equipements, categories, Gestion);
        Scan = new ServiceScan(equipements, sorties, journal, Horloge, Reglages, Compteur, Gestion);
        var types = new TypeSortieRepositoryMemoire(Db);
        TypesSortie = new ServiceTypesSortie(types, Gestion);
        DepotAdherents = new AdherentRepositoryMemoire(Db);
        Adherents = new ServiceAdherents(DepotAdherents, journal, Horloge, Gestion);
        Sorties = new ServiceSortie(sorties, moniteurs, journal, Horloge, Reglages, Gestion, types, DepotAdherents);
        Inventaire = new ServiceInventaire(equipements, categories, journal, Horloge, Reglages, Gestion, sorties, DepotRevisions);
        ServiceReglages = new ServiceReglages(Reglages, Stockage, Sauvegardes, Gestion);
        DepotSignalements = new SignalementRepositoryMemoire(Db);
        Signalements = new ServiceSignalements(DepotSignalements, equipements, categories, sorties, DepotRevisions, journal, Horloge, Gestion);
        DepotPrestataires = new PrestataireRepositoryMemoire(Db, DepotRevisions.Prestataires);
        Prestataires = new ServicePrestataires(DepotPrestataires, journal, Horloge, Gestion);
        Revisions = new ServiceRevisions(equipements, categories, sorties, DepotRevisions, moniteurs, journal, Horloge, Reglages, Gestion, Compteur, DepotSignalements, DepotPrestataires);
        Moniteurs = new ServiceMoniteurs(moniteurs, sorties, journal, Horloge, Gestion);
        Historique = new ServiceHistorique(journal, equipements, categories, sorties, DepotRevisions, moniteurs, Horloge, Reglages, DepotSignalements);
        Bilan = new ServiceBilan(sorties, DepotRevisions, equipements, Horloge);

        // Les six types d'origine (comme la migration 004).
        (string Nom, TypeSortie Pictogramme)[] typesOrigine =
            [("Piscine", TypeSortie.Piscine), ("Fosse", TypeSortie.Fosse), ("Mer", TypeSortie.Mer), ("Lac", TypeSortie.Lac), ("Baptême", TypeSortie.Bapteme), ("Autre", TypeSortie.Autre)];
        for (var i = 0; i < typesOrigine.Length; i++)
        {
            Db.TypesSortie.Add(new TypeDeSortie
            {
                Id = Db.ProchainId(), Nom = typesOrigine[i].Nom, Pictogramme = typesOrigine[i].Pictogramme, Ordre = i + 1,
                Utilisations = TypeDeSortie.UtilisationsParDefaut(typesOrigine[i].Pictogramme),
            });
        }

        Db.Categories.Add(new Categorie { Id = Db.ProchainId(), Nom = "Blocs" });
        Db.Categories.Add(new Categorie { Id = Db.ProchainId(), Nom = "Détendeurs" });
        Pascal = new Moniteur { Id = Db.ProchainId(), Prenom = "Pascal", Nom = "Martin", Actif = true };
        Db.Moniteurs.Add(Pascal);
    }

    public Equipement Equipement(
        string code,
        EtatEquipement etat = EtatEquipement.EnService,
        DateOnly? revision = null,
        DateOnly? requalification = null,
        string categorie = "Blocs")
    {
        var e = new Equipement
        {
            Id = Db.ProchainId(),
            Code = code,
            Libelle = categorie == "Blocs" ? "Bloc 12 L" : "Détendeur",
            CategorieId = Db.Categories.First(c => c.Nom == categorie).Id,
            Etat = etat,
            DateProchaineRevision = revision,
            DateRequalification = requalification,
            CreeLe = Maintenant,
        };
        Db.Equipements.Add(e);
        return e;
    }

    public async Task<long> SortieAsync(string intitule = "Cours en piscine")
    {
        var r = await Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 6), TypeSortie.Piscine, intitule, Pascal.Id));
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    /// <summary>Crée une sortie, scanne les codes au départ et valide le départ.</summary>
    public async Task<long> SortieEnCoursAsync(string intitule, params string[] codes)
    {
        var id = await SortieAsync(intitule);
        foreach (var code in codes)
        {
            var r = await Scan.ScannerDepartAsync(id, code);
            Assert.NotEqual(Resultats.NiveauResultat.Erreur, r.Niveau);
        }

        var v = await Sorties.ValiderDepartAsync(id);
        Assert.True(v.Reussi, v.Message);
        return id;
    }

    public StatutSortieEquipement StatutDe(long sortieId, Equipement e) =>
        Db.Affectations.Single(a => a.SortieId == sortieId && a.EquipementId == e.Id).Statut;
}
