using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Manuels;
using Dapper;

namespace Asprenaut.Materiel.Data.Tests;

public class ManuelsDepotsTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();

    public Task InitializeAsync() => _base.InitializeAsync();

    public Task DisposeAsync() => _base.DisposeAsync();

    private async Task<long> CategorieAsync(string nom)
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        return await cx.ExecuteScalarAsync<long>("SELECT id FROM categorie WHERE nom = @nom;", new { nom });
    }

    // Règle : équipements liés = même catégorie, même marque (majuscules, espaces, tirets et points ignorés), hors réformés.
    [Fact]
    public async Task Manuel_enregistre_relu_avec_sa_categorie_et_ses_equipements_lies()
    {
        var detendeurs = await CategorieAsync("Détendeurs");
        var gilets = await CategorieAsync("Gilets");
        await using (var cx = await _base.Fabrique.OuvrirAsync())
        {
            await cx.ExecuteAsync("""
                INSERT INTO equipement (code, libelle, categorie_id, marque, etat, cree_le) VALUES
                    ('A07', 'Détendeur', @detendeurs, 'scubapro ', 'EnService', '2026-01-01T00:00:00.000Z'),
                    ('A08', 'Détendeur', @detendeurs, 'Scubapro', 'HorsService', '2026-01-01T00:00:00.000Z'),
                    ('A09', 'Détendeur', @detendeurs, 'Scubapro', 'Reforme', '2026-01-01T00:00:00.000Z'),
                    ('A10', 'Détendeur', @detendeurs, 'Aqua Lung', 'EnService', '2026-01-01T00:00:00.000Z'),
                    ('A11', 'Détendeur', @detendeurs, 'SCUBA-PRO', 'EnService', '2026-01-01T00:00:00.000Z'),
                    ('S12', 'Gilet', @gilets, 'Scubapro', 'EnService', '2026-01-01T00:00:00.000Z');
                """, new { detendeurs, gilets });
        }

        var depot = new ManuelRepository(_base.Fabrique);
        var id = await depot.AjouterAsync(new Manuel
        {
            Marque = "Scubapro", Titre = "Détendeurs", CategorieId = detendeurs, Modeles = "MK25 EVO", Edition = "07/2020",
            Fichier = "scp.pdf", NbPages = 24, MoisRevision = 24, PageEntretien = 17,
            Entretien = "Révision : tous les 2 ans", Sommaire = "8.2 Entretien ; 17",
        });

        var lu = Assert.Single(await depot.ListerAsync());
        Assert.Equal(id, lu.Id);
        Assert.Equal("Détendeurs", lu.Categorie);
        Assert.Equal(24, lu.MoisRevision);
        Assert.Equal(17, lu.PageEntretien);
        Assert.Equal(3, lu.NbEquipements);
        Assert.Equal(["A07", "A08", "A11"], await depot.ListerCodesEquipementsAsync(id));
        var parEquipement = await depot.ListerManuelsDesEquipementsAsync();
        Assert.Equal(4, parEquipement.Count); // A07, A08, A09 (réformé : son manuel reste consultable) et A11.
        Assert.All(parEquipement.Values, m => Assert.Equal(id, m));

        lu.MoisRevision = null;
        lu.CategorieId = null;
        await depot.MettreAJourAsync(lu);
        var modifie = await depot.ObtenirAsync(id);
        Assert.NotNull(modifie);
        Assert.Null(modifie.MoisRevision);
        Assert.Null(modifie.Categorie);
        Assert.Equal(0, modifie.NbEquipements);

        await depot.SupprimerAsync(id);
        Assert.Empty(await depot.ListerAsync());
    }

    [Fact]
    public async Task Un_manuel_sans_page_est_refuse_par_la_base()
    {
        await using var cx = await _base.Fabrique.OuvrirAsync();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => cx.ExecuteAsync(
            "INSERT INTO manuel (marque, titre, fichier, nb_pages) VALUES ('X', 'Y', 'z.pdf', 0);"));
    }

    [Fact]
    public void Le_stockage_copie_le_pdf_et_ne_remplace_pas_un_fichier_existant()
    {
        var source = Path.Combine(_base.Dossier, "notice:FR.pdf".Replace(':', '_'));
        File.WriteAllText(source, "%PDF-1.4");
        var stockage = new StockageManuels(Path.Combine(_base.Dossier, "Manuels"));

        var premier = stockage.Importer(source);
        var second = stockage.Importer(source);

        Assert.Equal("notice_FR.pdf", premier);
        Assert.Equal("notice_FR (2).pdf", second);
        Assert.True(stockage.Existe(premier) && stockage.Existe(second));
        stockage.Supprimer(premier);
        Assert.False(stockage.Existe(premier));
        Assert.Equal(Path.Combine(_base.Dossier, "Manuels"), StockageManuels.DossierParDefaut(_base.CheminBase));
    }
}
