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

        // La migration 008 ajoute les classes Niveau 1 à 4, listées après les moniteurs.
        Assert.Equal("Pascal Durand", Assert.Single(actifs, m => !m.EstClasse).NomComplet);
        Assert.Equal(2, tous.Count(m => !m.EstClasse));
        Assert.Equal(["Pascal Durand", "Niveau 1", "Niveau 2", "Niveau 3", "Niveau 4"], actifs.Select(m => m.NomComplet));
    }

    [Fact]
    public async Task Une_classe_garde_son_genre_et_son_abreviation()
    {
        var depot = new MoniteurRepository(_base.Fabrique);
        var id = await depot.AjouterAsync(new Moniteur { Genre = GenreResponsable.Classe, Nom = "Plongée enfants", Abrege = "PE" });

        var relue = (await depot.ObtenirAsync(id))!;
        Assert.Equal((GenreResponsable.Classe, "Plongée enfants", "PE"), (relue.Genre, relue.NomComplet, relue.Abrege));
        relue.Abrege = "ENF";
        relue.Actif = false;
        await depot.MettreAJourAsync(relue);

        await using var cx = await _base.Fabrique.OuvrirAsync();
        // Le genre est stocké par son nom (CHECK en SQL), pas par sa valeur numérique.
        Assert.Equal(("Classe", "ENF", 0L), await cx.QuerySingleAsync<(string, string, long)>(
            "SELECT genre, abrege, actif FROM moniteur WHERE id = @id;", new { id }));
        await Assert.ThrowsAnyAsync<Exception>(() => cx.ExecuteAsync("INSERT INTO moniteur (nom, prenom, genre) VALUES ('x', '', 'Groupe');"));

        // Une sortie dont la classe est responsable : le résumé le dit (« Classe : … »).
        await cx.ExecuteAsync("INSERT INTO sortie (date_sortie, type, intitule, moniteur_id) VALUES ('2026-11-08', 'Fosse', 'Fosse N2', @id);", new { id });
        var resume = Assert.Single(await new SortieRepository(_base.Fabrique).ListerResumesAsync(cloturees: false));
        Assert.Equal((GenreResponsable.Classe, "Classe : Plongée enfants"), (resume.MoniteurGenre, resume.LibelleResponsable));
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
        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerDepartAsync(id, "ASP:s01")).Niveau);
        Assert.Equal(NiveauResultat.Avertissement, (await scan.ScannerDepartAsync(id, "BLOC-033")).Niveau); // révision dépassée
        Assert.Equal(NiveauResultat.Erreur, (await scan.ScannerDepartAsync(id, "BLOC-034")).Niveau);       // hors service
        Assert.True((await serviceSortie.ValiderDepartAsync(id)).Reussi);

        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerRetourAsync(id, "BLOC-031")).Niveau);
        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerRetourAsync(id, "S01")).Niveau);

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
            UNION ALL SELECT horodatage FROM journal WHERE action NOT IN ('DonneesDemo', 'EquipementCree');
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

    [Fact]
    public async Task Modifier_puis_supprimer_une_sortie_seulement_si_elle_est_vide()
    {
        var horloge = new HorlogeFixe();
        var sorties = new SortieRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var service = new ServiceSortie(sorties, moniteurs, journal, horloge, new Reglages());
        var scan = new ServiceScan(new EquipementRepository(_base.Fabrique), sorties, journal, horloge, new Reglages());
        await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui);
        var pascal = (await moniteurs.ListerAsync(true)).Single(m => m.Prenom == "Pascal");
        var vide = (await service.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 6), TypeSortie.Piscine, "Vide", pascal.Id))).Id!.Value;
        var pleine = (await service.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 6), TypeSortie.Piscine, "Pleine", pascal.Id))).Id!.Value;
        await scan.ScannerDepartAsync(pleine, "BLOC-031");

        Assert.True((await service.ModifierAsync(vide, new NouvelleSortie(new DateOnly(2026, 11, 7), TypeSortie.Mer, "Mer", pascal.Id, "Port"))).Reussi);
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal(("2026-11-07", "Mer", "Mer", "Port"), await cx.QuerySingleAsync<(string, string, string, string)>(
                "SELECT date_sortie, type, intitule, lieu FROM sortie WHERE id = @vide;", new { vide }));
        }

        // Le dépôt lui-même refuse de supprimer une sortie qui a du matériel (condition dans la requête).
        Assert.False(await sorties.SupprimerAsync(pleine));
        Assert.True(await sorties.SupprimerAsync(vide));
        Assert.Null(await sorties.ObtenirAsync(vide));
        Assert.NotNull(await sorties.ObtenirAsync(pleine));
    }
}
