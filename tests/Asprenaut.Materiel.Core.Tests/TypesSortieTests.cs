using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Types de sortie gérés dans Paramètres.</summary>
public class TypesSortieTests
{
    private readonly Contexte _ctx = new();

    private long Id(string nom) => _ctx.Db.TypesSortie.Single(t => t.Nom == nom).Id;

    private Task<Core.Resultats.ResultatOperation> CreerSortieAsync(long? typeId, TypeSortie famille = TypeSortie.Autre) =>
        _ctx.Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 7), famille, "Sortie", _ctx.Pascal.Id, TypeId: typeId));

    [Fact]
    public async Task Ajouter_un_type_verifie_le_nom_et_le_place_en_dernier()
    {
        var r = await _ctx.TypesSortie.CreerAsync("  Sortie bateau ", TypeSortie.Mer);

        Assert.True(r.Reussi, r.Message);
        var bateau = _ctx.Db.TypesSortie.Single(t => t.Id == r.Id);
        Assert.Equal(("Sortie bateau", TypeSortie.Mer, true, 7), (bateau.Nom, bateau.Pictogramme, bateau.Actif, bateau.Ordre));

        Assert.Equal("Le type « piscine » existe déjà.", (await _ctx.TypesSortie.CreerAsync("piscine", TypeSortie.Piscine)).Message);
        Assert.Equal("Indiquez le nom du type de sortie.", (await _ctx.TypesSortie.CreerAsync("  ", TypeSortie.Autre)).Message);
        Assert.False((await _ctx.TypesSortie.CreerAsync(new string('x', 41), TypeSortie.Autre)).Reussi);
        Assert.False((await _ctx.TypesSortie.CreerAsync("Nuit", (TypeSortie)42)).Reussi);
    }

    [Fact]
    public async Task Une_sortie_prend_le_type_choisi_et_sa_famille()
    {
        var bateau = (await _ctx.TypesSortie.CreerAsync("Sortie bateau", TypeSortie.Mer)).Id!.Value;

        var id = (await CreerSortieAsync(bateau)).Id!.Value;

        var sortie = _ctx.Db.Sorties.Single(s => s.Id == id);
        Assert.Equal((bateau, TypeSortie.Mer), (sortie.TypeId, sortie.Type));
        var resume = (await _ctx.Sorties.ObtenirResumeAsync(id))!;
        Assert.Equal("Sortie bateau", resume.LibelleType);
        Assert.Equal("Choisissez un type de sortie.", (await CreerSortieAsync(999)).Message);
    }

    [Fact]
    public async Task Sans_type_choisi_la_sortie_prend_le_premier_type_de_sa_famille()
    {
        var id = (await CreerSortieAsync(null, TypeSortie.Fosse)).Id!.Value;

        Assert.Equal(Id("Fosse"), _ctx.Db.Sorties.Single(s => s.Id == id).TypeId);
    }

    // Règle : un type masqué n'est plus proposé, mais une sortie qui l'a déjà le garde quand on la modifie.
    [Fact]
    public async Task Un_type_masque_n_est_plus_propose_mais_reste_sur_ses_sorties()
    {
        var lac = Id("Lac");
        var id = (await CreerSortieAsync(lac)).Id!.Value;
        Assert.True((await _ctx.TypesSortie.ModifierAsync(lac, "Lac", TypeSortie.Lac, actif: false)).Reussi);

        Assert.DoesNotContain(await _ctx.TypesSortie.ListerProposesAsync(), t => t.Id == lac);
        Assert.Contains(await _ctx.TypesSortie.ListerProposesAsync(lac), t => t.Id == lac);
        Assert.Equal("Choisissez un type de sortie.", (await CreerSortieAsync(lac)).Message);
        var modifiee = await _ctx.Sorties.ModifierAsync(id, new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Lac, "Lac du Der", _ctx.Pascal.Id, TypeId: lac));
        Assert.True(modifiee.Reussi, modifiee.Message);
    }

    [Fact]
    public async Task Renommer_change_le_nom_affiche_des_sorties()
    {
        var id = (await CreerSortieAsync(Id("Baptême"))).Id!.Value;

        Assert.True((await _ctx.TypesSortie.ModifierAsync(Id("Baptême"), "Baptême de plongée", TypeSortie.Bapteme, actif: true)).Reussi);
        Assert.Equal("Le type « FOSSE » existe déjà.", (await _ctx.TypesSortie.ModifierAsync(Id("Lac"), "FOSSE", TypeSortie.Lac, true)).Message);

        Assert.Equal("Baptême de plongée", (await _ctx.Sorties.ObtenirResumeAsync(id))!.LibelleType);
    }

    // Règle : un type utilisé par une sortie ne se supprime pas (on le masque) ; il reste toujours un type proposé.
    [Fact]
    public async Task Supprimer_seulement_un_type_inutilise_et_jamais_le_dernier_propose()
    {
        await CreerSortieAsync(Id("Piscine"));

        Assert.Equal("« Piscine » est utilisé par 1 sortie : il ne peut pas être supprimé. Vous pouvez le masquer (bouton œil).",
            (await _ctx.TypesSortie.SupprimerAsync(Id("Piscine"))).Message);
        Assert.True((await _ctx.TypesSortie.SupprimerAsync(Id("Lac"))).Reussi);
        Assert.DoesNotContain(_ctx.Db.TypesSortie, t => t.Nom == "Lac");

        foreach (var nom in new[] { "Fosse", "Mer", "Baptême", "Autre" })
        {
            Assert.True((await _ctx.TypesSortie.SupprimerAsync(Id(nom))).Reussi);
        }

        Assert.Equal("Gardez au moins un type proposé pour les nouvelles sorties.",
            (await _ctx.TypesSortie.ModifierAsync(Id("Piscine"), "Piscine", TypeSortie.Piscine, actif: false)).Message);
    }

    [Fact]
    public async Task Les_types_sont_une_action_protegee()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.TypesSortie.CreerAsync("Nuit", TypeSortie.Mer)).Reussi);
        Assert.False((await _ctx.TypesSortie.SupprimerAsync(Id("Lac"))).Reussi);
        Assert.Equal(6, _ctx.Db.TypesSortie.Count);
    }

    [Fact]
    public async Task Le_bilan_compte_par_type_du_club()
    {
        _ctx.Equipement("BLOC-031");
        var bateau = (await _ctx.TypesSortie.CreerAsync("Sortie bateau", TypeSortie.Mer)).Id!.Value;
        var id = (await CreerSortieAsync(bateau)).Id!.Value;
        await _ctx.Scan.ScannerDepartAsync(id, "BLOC-031");
        await _ctx.Sorties.ValiderDepartAsync(id);

        var bilan = await _ctx.Bilan.ObtenirAsync(2026);

        Assert.Equal([new TypeBilan(TypeSortie.Mer, "Sortie bateau", 1)], bilan.Types);
    }
}
