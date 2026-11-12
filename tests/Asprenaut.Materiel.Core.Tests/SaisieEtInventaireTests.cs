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

    // Médailles du club : « (Type) CODE Asprenaut Chalon 14 71-0025 » → le code est le mot qui suit le type.
    [Theory]
    [InlineData("(Detendeur) A24 Asprenaut Chalon 14 71-0025", "A24", "Detendeur")]
    [InlineData("(Detendeur) A24 Asprenaut Chalon 14 71-0025\r\n", "A24", "Detendeur")]
    [InlineData("  (Stab) s12 Asprenaut Chalon 14 71-0025", "S12", "Stab")]
    [InlineData("(Detendeur)E03 Asprenaut", "E03", "Detendeur")]
    [InlineData("a24", "A24", null)]                            // Tapé à la main.
    [InlineData("ASP:BLOC-031", "BLOC-031", null)]
    [InlineData("(Detendeur) Asprenaut 71-0025", "(DETENDEUR)ASPRENAUT71-0025", null)] // Pas de code après le type : texte entier.
    public void Lire_une_medaille(string brut, string code, string? type) =>
        Assert.Equal(new LectureEtiquette(code, type), CodeScanne.Lire(brut));

    [Fact]
    public void Nettoyer_rend_le_code_de_la_medaille() =>
        Assert.Equal("A24", CodeScanne.Nettoyer(CodeScanne.ExempleMedaille));
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

public class DetecteurRafaleTests
{
    private static DetecteurRafale Recevoir(params long[] instants)
    {
        var detecteur = new DetecteurRafale();
        foreach (var t in instants)
        {
            detecteur.Caractere(t);
        }

        return detecteur;
    }

    [Fact]
    public void Frappe_du_pistolet_est_une_rafale() =>
        Assert.True(Recevoir(1_000, 1_008, 1_016, 1_025).EstRafale);

    [Fact]
    public void Frappe_humaine_n_est_pas_une_rafale() =>
        Assert.False(Recevoir(1_000, 1_180, 1_350, 1_520).EstRafale);

    [Fact]
    public void Deux_caracteres_rapides_ne_suffisent_pas() =>
        Assert.False(Recevoir(1_000, 1_010).EstRafale);

    [Fact]
    public void Un_caractere_lent_apres_la_rafale_la_coupe() =>
        Assert.False(Recevoir(1_000, 1_010, 1_020, 1_400).EstRafale);

    [Fact]
    public void Reinitialiser_efface_la_rafale()
    {
        var detecteur = Recevoir(1_000, 1_010, 1_020);
        detecteur.Reinitialiser();
        Assert.False(detecteur.EstRafale);
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
        Assert.Equal("A01", await _ctx.Inventaire.ProposerCodeAsync(detendeurs)); // Étiquettes du club : A01, S01.
    }

    // Étiquettes du club : A = détendeur adulte, E = détendeur enfant, S = gilet stab, sans tiret (A24, E03, S12).
    [Fact]
    public async Task Codes_du_club_sans_tiret()
    {
        var detendeurs = _ctx.Db.Categories.Single(c => c.Nom == "Détendeurs").Id;
        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Gilets" });
        var gilets = _ctx.Db.Categories.Last().Id;

        Assert.Equal(detendeurs, await _ctx.Inventaire.DevinerCategorieAsync("A24"));
        Assert.Equal(detendeurs, await _ctx.Inventaire.DevinerCategorieAsync("e03"));
        Assert.Equal(gilets, await _ctx.Inventaire.DevinerCategorieAsync("S12"));
        Assert.Equal(gilets, await _ctx.Inventaire.DevinerCategorieAsync("(Stab) X99 Asprenaut Chalon 14 71-0025")); // Le type l'emporte sur la lettre.
        Assert.Equal(detendeurs, await _ctx.Inventaire.DevinerCategorieAsync(CodeScanne.ExempleMedaille));
        Assert.Equal("S01", await _ctx.Inventaire.ProposerCodeAsync(gilets));

        _ctx.Equipement("A09", categorie: "Détendeurs");
        _ctx.Equipement("A24", categorie: "Détendeurs");
        _ctx.Equipement("E03", categorie: "Détendeurs");
        Assert.Equal("A25", await _ctx.Inventaire.ProposerCodeAsync(detendeurs)); // Même forme : pas de tiret, 2 chiffres.

        Assert.Equal("Détendeur adulte", ServiceInventaire.NomPropose("A24"));
        Assert.Equal("Détendeur enfant", ServiceInventaire.NomPropose("E03"));
        Assert.Equal("Gilet stabilisateur", ServiceInventaire.NomPropose("S12"));
        Assert.Equal("", ServiceInventaire.NomPropose("BLOC-031"));
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
    [InlineData("Détendeurs", "A")]
    [InlineData("Gilets", "S")]
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
