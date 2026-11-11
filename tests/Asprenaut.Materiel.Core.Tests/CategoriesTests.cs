using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Ajout et suppression des catégories depuis la fiche d'un équipement.</summary>
public class CategoriesTests
{
    private readonly Contexte _ctx = new();

    [Fact]
    public async Task Ajouter_une_categorie_la_rend_disponible()
    {
        var resultat = await _ctx.Inventaire.AjouterCategorieAsync("  Combinaisons  ");

        Assert.True(resultat.Reussi, resultat.Message);
        Assert.NotNull(resultat.Id);
        Assert.Contains(_ctx.Db.Categories, c => c.Id == resultat.Id && c.Nom == "Combinaisons");
    }

    [Fact]
    public async Task Categorie_nom_vide_doublon_ou_trop_long_refuse()
    {
        Assert.False((await _ctx.Inventaire.AjouterCategorieAsync("   ")).Reussi);
        Assert.False((await _ctx.Inventaire.AjouterCategorieAsync("blocs")).Reussi); // « Blocs » existe déjà.
        Assert.False((await _ctx.Inventaire.AjouterCategorieAsync(new string('x', Categorie.LongueurMaxNom + 1))).Reussi);
    }

    [Fact]
    public async Task Categorie_utilisee_par_un_equipement_ne_se_supprime_pas()
    {
        var blocs = _ctx.Db.Categories.Single(c => c.Nom == "Blocs");
        _ctx.Equipement("BLOC-001");

        var resultat = await _ctx.Inventaire.SupprimerCategorieAsync(blocs.Id);

        Assert.False(resultat.Reussi);
        Assert.Contains(_ctx.Db.Categories, c => c.Id == blocs.Id);
    }

    [Fact]
    public async Task Categorie_inutilisee_se_supprime_mais_pas_la_derniere()
    {
        var ajout = await _ctx.Inventaire.AjouterCategorieAsync("Palmes");
        Assert.True(ajout.Reussi, ajout.Message);

        var suppression = await _ctx.Inventaire.SupprimerCategorieAsync(ajout.Id!.Value);
        Assert.True(suppression.Reussi, suppression.Message);
        Assert.DoesNotContain(_ctx.Db.Categories, c => c.Id == ajout.Id);

        // « Blocs » et « Détendeurs » restent : on peut en retirer une, jamais la dernière.
        var detendeurs = _ctx.Db.Categories.Single(c => c.Nom == "Détendeurs");
        Assert.True((await _ctx.Inventaire.SupprimerCategorieAsync(detendeurs.Id)).Reussi);
        var derniere = _ctx.Db.Categories.Single();
        Assert.False((await _ctx.Inventaire.SupprimerCategorieAsync(derniere.Id)).Reussi);
    }
}
