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
    public Moniteur Pascal { get; }

    public Contexte()
    {
        var equipements = new EquipementRepositoryMemoire(Db);
        var sorties = new SortieRepositoryMemoire(Db);
        var journal = new JournalRepositoryMemoire(Db);
        var moniteurs = new MoniteurRepositoryMemoire(Db);
        Scan = new ServiceScan(equipements, sorties, journal, Horloge, Reglages);
        Sorties = new ServiceSortie(sorties, moniteurs, journal, Horloge, Reglages);
        Inventaire = new ServiceInventaire(equipements, new CategorieRepositoryMemoire(Db), journal, Horloge, Reglages);
        ServiceReglages = new ServiceReglages(Reglages, Stockage, Sauvegardes);

        Db.Categories.Add(new Categorie { Id = Db.ProchainId(), Nom = "Blocs" });
        Pascal = new Moniteur { Id = Db.ProchainId(), Prenom = "Pascal", Nom = "Martin", Actif = true };
        Db.Moniteurs.Add(Pascal);
    }

    public Equipement Equipement(
        string code,
        EtatEquipement etat = EtatEquipement.EnService,
        DateOnly? revision = null)
    {
        var e = new Equipement
        {
            Id = Db.ProchainId(),
            Code = code,
            Libelle = "Bloc 12 L",
            CategorieId = Db.Categories[0].Id,
            Etat = etat,
            DateProchaineRevision = revision,
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
