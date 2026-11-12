using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class ExportEtCompteurDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc { get; set; } = new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    private sealed class StockageNul : IStockageReglages
    {
        public void Enregistrer(Reglages reglages)
        {
        }
    }

    private HorlogeFixe Horloge { get; } = new();

    private Reglages Reglages { get; } = new();

    private (ServiceScan Scan, ServiceSortie Sorties, long PascalId) Services()
    {
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var pascal = moniteurs.ListerAsync(true).Result.Single(m => m.Prenom == "Pascal").Id;
        return (new ServiceScan(equipements, sorties, journal, Horloge, Reglages), new ServiceSortie(sorties, moniteurs, journal, Horloge, Reglages), pascal);
    }

    private async Task AllerRetourAsync(string code)
    {
        var (scan, sorties, pascal) = Services();
        var id = (await sorties.CreerAsync(new NouvelleSortie(Horloge.AujourdHui, TypeSortie.Piscine, "Piscine", pascal))).Id!.Value;
        await scan.ScannerDepartAsync(id, code);
        await sorties.ValiderDepartAsync(id);
        await scan.ScannerRetourAsync(id, code);
    }

    [Fact]
    public async Task L_export_lit_toutes_les_tables_avec_des_noms_lisibles()
    {
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, Horloge.AujourdHui));
        await AllerRetourAsync("S01");

        var fichiers = await new ServiceExport(new ExportRepository(_base.Fabrique), Horloge).ExporterAsync();

        Assert.Equal(["materiel.csv", "moniteurs.csv", "sorties.csv", "materiel_des_sorties.csv", "controles.csv", "atelier.csv", "seances_tiv.csv", "journal.csv", "manuels.csv", "signalements.csv"],
            fichiers.Select(f => f.Nom));
        var materiel = fichiers[0].Contenu.Split(Environment.NewLine);
        Assert.Equal(15, fichiers[0].Lignes);
        Assert.StartsWith("Code;Équipement;Catégorie;Marque;Modèle;Taille;N° série;État;Dernière révision;", materiel[0], StringComparison.Ordinal);
        Assert.Contains(materiel, l => l.StartsWith("BLOC-034;Bloc 6 L alu;Blocs;;;;;Hors service;;", StringComparison.Ordinal));
        Assert.Contains("Moniteur;Pascal;Durand;;MF1;06 12 34 56 78;Oui;Oui", fichiers[1].Contenu, StringComparison.Ordinal);
        Assert.Contains("Classe;;Niveau 2;N2;;;Non;Oui", fichiers[1].Contenu, StringComparison.Ordinal);
        Assert.Contains($";06/11/2026;Piscine;Piscine;Pascal Durand;;En cours;1;0;", fichiers[2].Contenu, StringComparison.Ordinal);
        Assert.Contains(";Piscine;S01;Gilet Aqualung;Rendu;", fichiers[3].Contenu, StringComparison.Ordinal);
        Assert.Contains(";BLOC-031;Bloc 12 L acier;TIV;Conforme;Pascal Durand;", fichiers[4].Contenu, StringComparison.Ordinal);
        Assert.Equal(1, fichiers[6].Lignes);
        Assert.Contains(fichiers[7].Contenu.Split(Environment.NewLine), l => l.Contains(";ScanRetour;", StringComparison.Ordinal) && l.EndsWith(";S01;Piscine", StringComparison.Ordinal));
    }

    // Règle : le compteur compte les retours scannés après le jour du dernier contrôle de l'équipement.
    [Fact]
    public async Task Le_compteur_compte_les_retours_depuis_le_dernier_controle()
    {
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, Horloge.AujourdHui));
        var depot = new EquipementRepository(_base.Fabrique);
        var det = (await depot.ObtenirParCodeAsync("A01"))!;
        await AllerRetourAsync("A01");
        Horloge.MaintenantUtc = Horloge.MaintenantUtc.AddDays(1);
        await AllerRetourAsync("A01");

        Assert.Equal(2, (await depot.ListerCompteursAsync(det.Id)).Single().Utilisations);

        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync(
                "INSERT INTO controle (equipement_id, type, date_controle, resultat, notes, saisi_le) VALUES (@id, 'Revision', '2026-11-06', 'Conforme', '', '2026-11-06T19:00:00.000Z');",
                new { id = det.Id });
        }

        Assert.Equal(1, (await depot.ListerCompteursAsync(det.Id)).Single().Utilisations); // Le retour du 6 compte avant le contrôle du 6.
        var tous = await depot.ListerCompteursAsync(null);
        Assert.Equal(15, tous.Count);
        Assert.All(tous.Where(c => c.EquipementId != det.Id), c => Assert.Equal(0, c.Utilisations));

        // De bout en bout : seuil des détendeurs à 1 → avertissement au départ.
        Reglages.SeuilsSorties["Détendeurs"] = 1;
        var compteur = new ServiceCompteur(Reglages, new StockageNul(), depot, new CategorieRepository(_base.Fabrique));
        Assert.Equal(new UsageEquipement(1, 1, EtatCompteur.Atteint), await compteur.UsageAsync(det));
    }

    [Fact]
    public async Task Une_modification_d_equipement_est_enregistree()
    {
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, Horloge.AujourdHui));
        var depot = new EquipementRepository(_base.Fabrique);
        var inventaire = new ServiceInventaire(depot, new CategorieRepository(_base.Fabrique), new JournalRepository(_base.Fabrique), Horloge, Reglages,
            null, new SortieRepository(_base.Fabrique), new RevisionRepository(_base.Fabrique));
        var gilet = (await depot.ObtenirParCodeAsync("S03"))!;
        gilet.Code = "S30";
        gilet.Taille = "XS";
        gilet.Etat = EtatEquipement.Reforme;

        Assert.True((await inventaire.ModifierAsync(gilet)).Reussi);

        var relu = (await depot.ObtenirAsync(gilet.Id))!;
        Assert.Equal(("S30", "XS", EtatEquipement.Reforme), (relu.Code, relu.Taille, relu.Etat));
        await using var cx = await _base.Fabrique.OuvrirAsync();
        Assert.Equal("Reforme", await cx.ExecuteScalarAsync<string>("SELECT etat FROM equipement WHERE id = @id;", new { id = gilet.Id }));
        Assert.Equal(gilet.Id, await cx.ExecuteScalarAsync<long>("SELECT equipement_id FROM journal WHERE action = 'EquipementModifie';"));
    }
}
