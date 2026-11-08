using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Saisie;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class CodeScanneTests
{
    [Theory]
    [InlineData("BLOC-031", "BLOC-031")]
    [InlineData("  bloc-031  ", "BLOC-031")]
    [InlineData("ASP:BLOC-031", "BLOC-031")]
    [InlineData("asp:bloc-031", "BLOC-031")]
    [InlineData("ASP: BLOC 031\r\n", "BLOC031")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Nettoyer_normalise_le_code(string? brut, string attendu) =>
        Assert.Equal(attendu, CodeScanne.Nettoyer(brut));
}

public class AntiRebondTests
{
    [Fact]
    public void Meme_code_en_moins_de_800_ms_est_ignore()
    {
        var filtre = new AntiRebond();

        Assert.True(filtre.Accepter("BLOC-031", 1_000));
        Assert.False(filtre.Accepter("BLOC-031", 1_500));
    }

    [Fact]
    public void Meme_code_apres_800_ms_est_accepte()
    {
        var filtre = new AntiRebond();

        Assert.True(filtre.Accepter("BLOC-031", 1_000));
        Assert.True(filtre.Accepter("BLOC-031", 1_800));
    }

    [Fact]
    public void Codes_differents_sont_toujours_acceptes()
    {
        var filtre = new AntiRebond();

        Assert.True(filtre.Accepter("BLOC-031", 1_000));
        Assert.True(filtre.Accepter("BLOC-032", 1_010));
    }
}

public class InventaireTests
{
    private readonly Contexte _ctx = new();

    [Fact]
    public async Task Creation_normalise_le_code_et_refuse_les_doublons()
    {
        var categorie = _ctx.Db.Categories[0].Id;

        var ok = await _ctx.Inventaire.CreerAsync(new Equipement { Code = " palme-099 ", Libelle = "Palmes M", CategorieId = categorie });
        var doublon = await _ctx.Inventaire.CreerAsync(new Equipement { Code = "PALME-099", Libelle = "Autres", CategorieId = categorie });

        Assert.True(ok.Reussi);
        Assert.False(doublon.Reussi);
        Assert.Equal("PALME-099", _ctx.Db.Equipements.Single().Code);
    }

    [Fact]
    public async Task Statut_inventaire_reflete_les_sorties()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032", EtatEquipement.HorsService);
        _ctx.Equipement("BLOC-033");
        await _ctx.SortieEnCoursAsync("Mer", "BLOC-031");

        var lignes = await _ctx.Inventaire.ListerAsync(null, null);

        Assert.Equal(StatutInventaire.Dehors, lignes.Single(l => l.Code == "BLOC-031").Statut);
        Assert.Equal("Mer", lignes.Single(l => l.Code == "BLOC-031").SortieEnCoursIntitule);
        Assert.Equal(StatutInventaire.HorsService, lignes.Single(l => l.Code == "BLOC-032").Statut);
        Assert.Equal(StatutInventaire.Disponible, lignes.Single(l => l.Code == "BLOC-033").Statut);
    }
}
