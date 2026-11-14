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

    // Choix de la catégorie (maquette « Ajouter un équipement ») : pictogramme et couleur, nombre d'équipements, suppression possible.
    [Fact]
    public async Task Une_categorie_a_un_pictogramme_et_une_couleur_choisis_ou_d_apres_son_nom()
    {
        var r = await _ctx.Inventaire.AjouterCategorieAsync("  lampes ", "lampe", "jaune");
        Assert.True(r.Reussi, r.Message);
        var lampes = _ctx.Db.Categories.Single(c => c.Id == r.Id);
        Assert.Equal(("Lampes", "lampe", "jaune"), (lampes.Nom, lampes.IconeEffective, lampes.CouleurEffective)); // Majuscule ajoutée.

        // Sans choix : d'après le nom (accents et majuscules ignorés).
        Assert.Equal(("detendeur", "bleu"), (Categorie.IconeParDefaut("Détendeurs"), Categorie.CouleurParDefaut("détendeurs")));
        Assert.Equal(("gilet", "vert"), (Categorie.IconeParDefaut("Stabs"), Categorie.CouleurParDefaut("Gilets")));
        Assert.Equal(("autre", "cyan"), (Categorie.IconeParDefaut("Divers"), Categorie.CouleurParDefaut("Divers")));
        Assert.Equal("autre", new Categorie { Nom = "Divers", Icone = "inconnue" }.IconeEffective);

        Assert.False((await _ctx.Inventaire.AjouterCategorieAsync("Sacs", "fusée", null)).Reussi);
    }

    [Fact]
    public async Task La_liste_des_categories_donne_le_nombre_d_equipements_et_si_elle_se_supprime()
    {
        var blocs = _ctx.Db.Categories.Single(c => c.Nom == "Blocs");
        _ctx.Equipement("BLOC-031");
        var vide = (await _ctx.Inventaire.AjouterCategorieAsync("Lampes", "lampe", "jaune")).Id;

        var usages = await _ctx.Inventaire.ListerCategoriesUsageAsync();
        Assert.Equal((1, false), usages.Where(u => u.Categorie.Id == blocs.Id).Select(u => (u.NbEquipements, u.Supprimable)).Single());
        Assert.Equal((0, true), usages.Where(u => u.Categorie.Id == vide).Select(u => (u.NbEquipements, u.Supprimable)).Single());
    }
}
