using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Saisie;
using Asprenaut.Materiel.Core.Services;
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
    public async Task Creation_nettoie_la_saisie_et_reserve_la_requalification_aux_blocs()
    {
        var blocs = _ctx.Db.Categories.Single(c => c.Nom == "Blocs").Id;
        var detendeurs = _ctx.Db.Categories.Single(c => c.Nom == "Détendeurs").Id;
        var requalif = new DateOnly(2030, 1, 1);

        var bloc = await _ctx.Inventaire.CreerAsync(new Equipement { Code = "asp:bloc-040", Libelle = " Bloc 12 L ", CategorieId = blocs, Taille = " ", DateRequalification = requalif });
        var det = await _ctx.Inventaire.CreerAsync(new Equipement { Code = "DET-040", Libelle = "Détendeur", CategorieId = detendeurs, DateRequalification = requalif, Etat = EtatEquipement.Reforme });

        Assert.True(bloc.Reussi, bloc.Message);
        Assert.True(det.Reussi, det.Message);
        var b = _ctx.Db.Equipements.Single(e => e.Code == "BLOC-040");
        Assert.Equal(("Bloc 12 L", null, requalif), (b.Libelle, b.Taille, b.DateRequalification));
        var d = _ctx.Db.Equipements.Single(e => e.Code == "DET-040");
        Assert.Equal((null, EtatEquipement.EnService), (d.DateRequalification, d.Etat));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.EquipementCree);
    }

    [Fact]
    public async Task Creation_refusee_sans_categorie_sans_nom_ou_avec_une_date_passee()
    {
        var blocs = _ctx.Db.Categories.Single(c => c.Nom == "Blocs").Id;

        Assert.False((await _ctx.Inventaire.CreerAsync(new Equipement { Code = "X-1", Libelle = "x", CategorieId = 9999 })).Reussi);
        Assert.False((await _ctx.Inventaire.CreerAsync(new Equipement { Code = "X-1", Libelle = "  ", CategorieId = blocs })).Reussi);
        Assert.False((await _ctx.Inventaire.CreerAsync(new Equipement { Code = "X-1", Libelle = "x", CategorieId = blocs, DateProchaineRevision = new DateOnly(2026, 1, 1) })).Reussi);
        Assert.Empty(_ctx.Db.Equipements);
    }

    [Fact]
    public async Task Code_propose_suit_les_codes_de_la_categorie()
    {
        var blocs = _ctx.Db.Categories.Single(c => c.Nom == "Blocs").Id;
        var detendeurs = _ctx.Db.Categories.Single(c => c.Nom == "Détendeurs").Id;

        Assert.Equal("BLOC-001", await _ctx.Inventaire.ProposerCodeAsync(blocs));
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-034");
        _ctx.Equipement("STOCK-A");
        Assert.Equal("BLOC-035", await _ctx.Inventaire.ProposerCodeAsync(blocs));
        Assert.Equal("DET-001", await _ctx.Inventaire.ProposerCodeAsync(detendeurs));
    }

    [Fact]
    public async Task Categorie_devinee_d_apres_le_code_scanne()
    {
        var blocs = _ctx.Db.Categories.Single(c => c.Nom == "Blocs").Id;
        var detendeurs = _ctx.Db.Categories.Single(c => c.Nom == "Détendeurs").Id;
        _ctx.Equipement("REG-001", categorie: "Détendeurs");

        Assert.Equal(detendeurs, await _ctx.Inventaire.DevinerCategorieAsync("asp:reg-099"));  // Même préfixe qu'un détendeur.
        Assert.Equal(blocs, await _ctx.Inventaire.DevinerCategorieAsync("BLOC-777"));          // Préfixe par défaut des blocs.
        Assert.Null(await _ctx.Inventaire.DevinerCategorieAsync("NOUV-777"));                  // Pas de catégorie « Autre » ici.

        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Autre" });
        Assert.Equal(_ctx.Db.Categories.Last().Id, await _ctx.Inventaire.DevinerCategorieAsync("NOUV-777"));
    }

    [Theory]
    [InlineData("Blocs", "BLOC")]
    [InlineData("Détendeurs", "DET")]
    [InlineData("Gilets", "GIL")]
    [InlineData("Lampes", "LAMP")]
    [InlineData("Écrans", "ECRA")]
    public void Prefixe_par_defaut_d_une_categorie(string nom, string prefixe) =>
        Assert.Equal(prefixe, ServiceInventaire.PrefixeDeCategorie(nom));

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
