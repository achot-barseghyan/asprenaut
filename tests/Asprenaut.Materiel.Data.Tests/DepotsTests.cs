using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class DepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc { get; set; } = new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    [Fact]
    public async Task Equipement_aller_retour_avec_enum_et_dates_en_texte_iso()
    {
        var depot = new EquipementRepository(_base.Fabrique);
        var e = new Equipement
        {
            Code = "BLOC-031",
            Libelle = "Bloc 12 L",
            CategorieId = 1,
            Taille = null,
            Etat = EtatEquipement.HorsService,
            DateProchaineRevision = new DateOnly(2027, 3, 15),
            CreeLe = new DateTime(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc),
        };

        var id = await depot.AjouterAsync(e);
        var relu = await depot.ObtenirParCodeAsync("BLOC-031");

        Assert.NotNull(relu);
        Assert.Equal(id, relu.Id);
        Assert.Equal(EtatEquipement.HorsService, relu.Etat);
        Assert.Equal(new DateOnly(2027, 3, 15), relu.DateProchaineRevision);
        Assert.Equal(e.CreeLe, relu.CreeLe);
        Assert.Equal(DateTimeKind.Utc, relu.CreeLe.Kind);
        Assert.Null(relu.Taille);

        await using var cx = await _base.Fabrique.OuvrirAsync();
        var brut = await cx.QuerySingleAsync<(string Etat, string Revision, string CreeLe)>(
            "SELECT etat, date_prochaine_revision, cree_le FROM equipement WHERE id = @id;", new { id });
        Assert.Equal("HorsService", brut.Etat);
        Assert.Equal("2027-03-15", brut.Revision);
        Assert.Equal("2026-11-06T18:30:00.000Z", brut.CreeLe);
    }

    [Fact]
    public async Task Moniteur_aller_retour_et_filtre_actifs()
    {
        var depot = new MoniteurRepository(_base.Fabrique);
        await depot.AjouterAsync(new Moniteur { Nom = "Durand", Prenom = "Pascal", Niveau = "MF1", Actif = true });
        await depot.AjouterAsync(new Moniteur { Nom = "Ancien", Prenom = "Paul", Actif = false });

        var actifs = await depot.ListerAsync(actifsSeulement: true);
        var tous = await depot.ListerAsync(actifsSeulement: false);

        Assert.Equal("Pascal Durand", Assert.Single(actifs).NomComplet);
        Assert.Equal(2, tous.Count);
    }

    /// <summary>Scénario complet sur SQLite : départ, retour partiel, clôture avec manquant, manquant retrouvé.</summary>
    [Fact]
    public async Task Scenario_complet_d_une_sortie_sur_sqlite()
    {
        var horloge = new HorlogeFixe();
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var reglages = new Reglages();
        var scan = new ServiceScan(equipements, sorties, journal, horloge, reglages);
        var serviceSortie = new ServiceSortie(sorties, moniteurs, journal, horloge, reglages);
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var pascal = (await moniteurs.ListerAsync(true)).Single(m => m.Prenom == "Pascal");

        var creation = await serviceSortie.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 6), TypeSortie.Piscine, "Cours en piscine", pascal.Id));
        var id = creation.Id!.Value;
        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerDepartAsync(id, "BLOC-031")).Niveau);
        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerDepartAsync(id, "ASP:gil-001")).Niveau);
        Assert.Equal(NiveauResultat.Avertissement, (await scan.ScannerDepartAsync(id, "BLOC-033")).Niveau); // révision dépassée
        Assert.Equal(NiveauResultat.Erreur, (await scan.ScannerDepartAsync(id, "BLOC-034")).Niveau);       // hors service
        Assert.True((await serviceSortie.ValiderDepartAsync(id)).Reussi);

        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerRetourAsync(id, "BLOC-031")).Niveau);
        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerRetourAsync(id, "GIL-001")).Niveau);

        var resume = await serviceSortie.ObtenirResumeAsync(id);
        Assert.NotNull(resume);
        Assert.Equal((3, 2, "Pascal Durand"), (resume.NbTotal, resume.NbRendus, resume.MoniteurNom));
        Assert.Equal(horloge.MaintenantUtc, resume.DepartValideLe); // relu depuis le texte ISO, en UTC
        Assert.Single(await serviceSortie.ListerEnCoursAsync());

        Assert.Equal(1, (await serviceSortie.ApercuClotureAsync(id)).NbManquants);
        Assert.True((await serviceSortie.CloturerAsync(id)).Reussi);
        Assert.Single(await serviceSortie.ListerClotureesAsync());
        var lignes = await serviceSortie.ListerMaterielAsync(id);
        Assert.Equal(StatutSortieEquipement.Manquant, lignes.Single(l => l.Code == "BLOC-033").Statut);

        var inventaire = await new ServiceInventaire(equipements, new CategorieRepository(_base.Fabrique), journal, horloge, reglages).ListerAsync("BLOC-033", null);
        Assert.Equal(StatutInventaire.Manquant, Assert.Single(inventaire).Statut);

        var retrouve = await scan.ScannerRetourAsync(id, "BLOC-033");
        Assert.Equal(NiveauResultat.Succes, retrouve.Niveau);
        Assert.Contains(await journal.ListerRecentsAsync(50), j => j.Action == ActionsJournal.ManquantRetrouve);

        // Tous les horodatages sont stockés en texte ISO 8601 UTC.
        await using var cx = await _base.Fabrique.OuvrirAsync();
        var horodatages = await cx.QueryAsync<string>("""
            SELECT scanne_depart_le FROM sortie_equipement
            UNION ALL SELECT scanne_retour_le FROM sortie_equipement WHERE scanne_retour_le IS NOT NULL
            UNION ALL SELECT depart_valide_le FROM sortie UNION ALL SELECT cloturee_le FROM sortie
            UNION ALL SELECT horodatage FROM journal WHERE action <> 'DonneesDemo';
            """);
        Assert.All(horodatages, h => Assert.Equal("2026-11-06T18:30:00.000Z", h));
        Assert.Equal("2026-11-06", await cx.ExecuteScalarAsync<string>("SELECT date_sortie FROM sortie;"));
    }

    [Fact]
    public async Task Inventaire_filtre_par_recherche_et_categorie()
    {
        await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, new DateOnly(2026, 11, 6));
        var depot = new EquipementRepository(_base.Fabrique);
        var categories = await new CategorieRepository(_base.Fabrique).ListerAsync();
        var blocs = categories.Single(c => c.Nom == "Blocs").Id;

        Assert.Equal(15, (await depot.ListerInventaireAsync(null, null)).Count);
        Assert.Equal(4, (await depot.ListerInventaireAsync(null, blocs)).Count);
        Assert.Equal(2, (await depot.ListerInventaireAsync("aqualung", null)).Count);
    }

    [Fact]
    public async Task Donnees_demo_ne_sont_chargees_qu_une_fois()
    {
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, new DateOnly(2026, 11, 6)));
        Assert.False(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, new DateOnly(2026, 11, 6)));
    }
}
