using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Data.Demo;
using Asprenaut.Materiel.Data.Depots;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Sorties autorisées (migration 034) : equipement.types_sortie_interdits, relu par la fiche et les lignes d'une sortie.</summary>
public class SortiesAutoriseesDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private sealed class HorlogeFixe : IHorloge
    {
        public DateTime MaintenantUtc => new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

        public DateOnly AujourdHui => DateOnly.FromDateTime(MaintenantUtc);
    }

    [Fact]
    public async Task Les_types_interdits_sont_enregistres_relus_et_verifies_au_depart()
    {
        var horloge = new HorlogeFixe();
        var equipements = new EquipementRepository(_base.Fabrique);
        var sorties = new SortieRepository(_base.Fabrique);
        var journal = new JournalRepository(_base.Fabrique);
        var moniteurs = new MoniteurRepository(_base.Fabrique);
        var types = new TypeSortieRepository(_base.Fabrique);
        var reglages = new Reglages();
        Assert.True(await DonneesDemo.ChargerSiVideAsync(_base.Fabrique, horloge.AujourdHui));
        var pascal = (await moniteurs.ListerAsync(true)).First(m => m.Genre == GenreResponsable.Moniteur);
        var tous = await types.ListerAsync();
        var mer = tous.Single(t => t.Nom == "Mer").Id;
        var lac = tous.Single(t => t.Nom == "Lac").Id;
        var piscine = tous.Single(t => t.Nom == "Piscine").Id;

        // Nouvel équipement : colonne vide par défaut ; puis « Mer » et « Lac » interdits.
        var categorie = (await new CategorieRepository(_base.Fabrique).ListerAsync()).First().Id;
        var e = new Equipement { Code = "A11", Libelle = "Détendeur adulte A11", CategorieId = categorie, CreeLe = horloge.MaintenantUtc };
        await equipements.AjouterAsync(e);
        Assert.Equal("", (await equipements.ObtenirAsync(e.Id))!.TypesSortieInterdits);
        e.TypesSortieInterdits = Equipement.EcrireTypesInterdits([mer, lac]);
        await equipements.MettreAJourAsync(e);

        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            Assert.Equal(Equipement.EcrireTypesInterdits([mer, lac]), await cx.ExecuteScalarAsync<string>(
                "SELECT types_sortie_interdits FROM equipement WHERE id = @id;", new { id = e.Id }));
        }

        Assert.Equal([Math.Min(mer, lac), Math.Max(mer, lac)], (await equipements.ObtenirAsync(e.Id))!.TypesInterdits);

        // Départ : refusé en mer, accepté en piscine ; la ligne de la sortie porte les types interdits.
        var serviceSortie = new ServiceSortie(sorties, moniteurs, journal, horloge, reglages, null, types);
        var scan = new ServiceScan(equipements, sorties, journal, horloge, reglages, null, null, types);
        var enMer = (await serviceSortie.CreerAsync(new NouvelleSortie(horloge.AujourdHui, TypeSortie.Mer, "Marseille", pascal.Id, TypeId: mer))).Id!.Value;
        var refus = await scan.ScannerDepartAsync(enMer, "A11");
        Assert.Equal(NiveauResultat.Erreur, refus.Niveau);
        Assert.Contains("« Mer »", refus.Message, StringComparison.Ordinal);

        var enPiscine = (await serviceSortie.CreerAsync(new NouvelleSortie(horloge.AujourdHui, TypeSortie.Piscine, "Cours", pascal.Id, TypeId: piscine))).Id!.Value;
        Assert.Equal(NiveauResultat.Succes, (await scan.ScannerDepartAsync(enPiscine, "A11")).Niveau);
        Assert.Equal(e.TypesSortieInterdits, Assert.Single(await sorties.ListerLignesAsync(enPiscine)).TypesSortieInterdits);
    }
}
