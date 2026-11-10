using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Fiche matériel des registres du club (stabs, détendeurs) et délais de révision par catégorie.</summary>
public class FicheMaterielTests
{
    private readonly Contexte _ctx = new();

    public FicheMaterielTests()
    {
        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Gilets" });
    }

    private long Categorie(string nom) => _ctx.Db.Categories.Single(c => c.Nom == nom).Id;

    private Equipement Detendeur(string code = "A05") => new()
    {
        Code = code,
        Libelle = "Détendeur Scubapro",
        CategorieId = Categorie("Détendeurs"),
        Marque = " Scubapro ",
        Modele = "MK2",
        NumeroSerie = "0614500681",
        Montage = MontageDetendeur.Etrier,
        MarqueDeuxiemeEtage = "Scub",
        ModeleDeuxiemeEtage = "R 295",
        NumeroDeuxiemeEtage = "0614500681",
        DirectSystem = true,
        Manometre = "Scubapro",
        Octopus = "R 095",
        NumeroOctopus = "1,641,000,963",
    };

    // Règle : un détendeur se révise tous les 2 ans ; la date de la dernière révision saisie fixe « à réviser avant ».
    [Fact]
    public async Task La_derniere_revision_d_un_detendeur_fixe_la_suivante_a_deux_ans()
    {
        var r = await _ctx.Inventaire.CreerAsync(Detendeur(), derniereRevision: new DateOnly(2021, 3, 17));

        Assert.True(r.Reussi, r.Message);
        var e = _ctx.Db.Equipements.Single(x => x.Id == r.Id);
        Assert.Equal(new DateOnly(2023, 3, 17), e.DateProchaineRevision); // Déjà passée : acceptée, c'est un retard réel.
        Assert.Equal(("Scubapro", MontageDetendeur.Etrier, true, "R 095"), (e.Marque, e.Montage, e.DirectSystem, e.Octopus));
        var controle = _ctx.DepotRevisions.Controles.Single(c => c.EquipementId == e.Id);
        Assert.Equal((TypeControle.Revision, new DateOnly(2021, 3, 17), ResultatControle.Conforme), (controle.Type, controle.DateControle, controle.Resultat));
        Assert.Equal(new DateOnly(2021, 3, 17), (await _ctx.Inventaire.ListerAsync("A05", null)).Single().DerniereRevision);
    }

    [Fact]
    public async Task Une_revision_conforme_de_detendeur_vaut_deux_ans_et_un_gilet_n_a_pas_de_date_obligatoire()
    {
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        var gilet = _ctx.Equipement("S01", categorie: "Gilets");
        gilet.DateProchaineRevision = new DateOnly(2027, 1, 1);
        var jour = DateOnly.FromDateTime(Contexte.Maintenant);

        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Revision, ResultatControle.Conforme, jour, null)).Reussi);
        var r = await _ctx.Revisions.EnregistrerControleAsync(gilet.Id, TypeControle.Revision, ResultatControle.Conforme, jour, null);

        Assert.Equal(jour.AddYears(2), det.DateProchaineRevision);
        Assert.Null(gilet.DateProchaineRevision);
        Assert.EndsWith("Pas de date obligatoire pour ce matériel : aucune prochaine révision n'est fixée.", r.Message, StringComparison.Ordinal);
        Assert.Equal(12, ServiceRevisions.MoisRevision(_ctx.Reglages, "Ordinateurs"));
    }

    [Fact]
    public async Task Les_champs_du_detendeur_sont_vides_pour_un_gilet()
    {
        var gilet = Detendeur("S01");
        gilet.CategorieId = Categorie("Gilets");
        gilet.Taille = "XL";

        var r = await _ctx.Inventaire.CreerAsync(gilet, derniereRevision: new DateOnly(2025, 6, 1));

        var e = _ctx.Db.Equipements.Single(x => x.Id == r.Id);
        Assert.Equal(("Scubapro", "MK2", "0614500681"), (e.Marque, e.Modele, e.NumeroSerie));
        Assert.Equal((null, "", (bool?)null, "", ""), (e.Montage, e.MarqueDeuxiemeEtage, e.DirectSystem, e.Manometre, e.Octopus));
        Assert.Null(e.DateProchaineRevision); // Gilet : pas de date obligatoire, la révision est seulement notée.
        Assert.Single(_ctx.DepotRevisions.Controles, c => c.EquipementId == e.Id);
    }

    [Fact]
    public async Task Modifier_la_fiche_et_ajouter_une_revision_plus_recente()
    {
        var id = (await _ctx.Inventaire.CreerAsync(Detendeur(), derniereRevision: new DateOnly(2022, 1, 24))).Id!.Value;
        var e = _ctx.Db.Equipements.Single(x => x.Id == id);
        var modifie = new Equipement
        {
            Id = id, Code = e.Code, Libelle = e.Libelle, CategorieId = e.CategorieId, Marque = e.Marque, Modele = e.Modele, NumeroSerie = e.NumeroSerie,
            Montage = MontageDetendeur.Din, DirectSystem = false, Octopus = "", Etat = e.Etat, DateProchaineRevision = e.DateProchaineRevision,
        };

        Assert.Equal("Une révision plus récente est déjà enregistrée (le 24/01/2022).",
            (await _ctx.Inventaire.ModifierAsync(modifie, new DateOnly(2021, 1, 1))).Message);
        var r = await _ctx.Inventaire.ModifierAsync(modifie, new DateOnly(2024, 1, 24));

        Assert.True(r.Reussi, r.Message);
        Assert.Equal((MontageDetendeur.Din, (bool?)false, "", new DateOnly(2026, 1, 24)), (e.Montage, e.DirectSystem, e.Octopus, e.DateProchaineRevision));
        Assert.Equal(2, _ctx.DepotRevisions.Controles.Count(c => c.EquipementId == id));
        Assert.Contains("fiche", _ctx.Db.Journal.Last().Details, StringComparison.Ordinal);
        Assert.Contains("révisé le 24/01/2024", _ctx.Db.Journal.Last().Details, StringComparison.Ordinal);
        Assert.Equal("La date de la dernière révision ne peut pas être dans le futur.",
            (await _ctx.Inventaire.ModifierAsync(modifie, DateOnly.FromDateTime(Contexte.Maintenant).AddDays(1))).Message);
    }

    [Fact]
    public void L_identification_reprend_le_registre_du_club()
    {
        _ctx.Reglages.NomClub = "Asprenaut Chalon";
        _ctx.Reglages.NumeroClub = "14 71-0025";

        Assert.Equal("(Stab) S01 taille XL Asprenaut Chalon 14 71-0025", _ctx.Inventaire.Identification("Gilets", "S01", "XL"));
        Assert.Equal("(Detendeur) A01 Asprenaut Chalon 14 71-0025", _ctx.Inventaire.Identification("Détendeurs", "A01", null));
        Assert.Equal("(Bloc) BLOC-031 Asprenaut Chalon 14 71-0025", _ctx.Inventaire.Identification("Blocs", "BLOC-031", " "));
    }

    [Fact]
    public async Task On_retrouve_un_equipement_par_son_numero_de_serie()
    {
        await _ctx.Inventaire.CreerAsync(Detendeur());

        Assert.Equal("A05", (await _ctx.Inventaire.ListerAsync("0614500681", null)).Single().Code);
    }
}
