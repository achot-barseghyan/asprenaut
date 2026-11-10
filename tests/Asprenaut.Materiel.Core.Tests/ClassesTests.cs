using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Moniteurs et classes : une classe (Niveau 1, Niveau 2…) peut être responsable d'une sortie.</summary>
public class ClassesTests
{
    private readonly Contexte _ctx = new();

    private Task<Core.Resultats.ResultatOperation> AjouterClasseAsync(string nom, string abrege = "", bool actif = true) =>
        _ctx.Moniteurs.EnregistrerAsync(new Moniteur { Genre = GenreResponsable.Classe, Nom = nom, Abrege = abrege, Actif = actif });

    // Règle : une classe a un nom obligatoire, unique sans tenir compte des majuscules ; l'abréviation est proposée si vide.
    [Fact]
    public async Task Une_classe_a_un_nom_unique_et_une_abreviation()
    {
        Assert.Equal("Indiquez le nom de la classe.", (await AjouterClasseAsync("  ")).Message);
        var r = await AjouterClasseAsync("Niveau 2");
        Assert.True(r.Reussi, r.Message);
        Assert.Equal("La classe « niveau 2 » existe déjà.", (await AjouterClasseAsync("niveau 2")).Message);
        Assert.Equal("Abréviation trop longue : 4 caractères au plus.", (await AjouterClasseAsync("Enfants", "ENFAN")).Message);

        var classe = _ctx.Db.Moniteurs.Single(m => m.Id == r.Id);
        Assert.Equal((GenreResponsable.Classe, "Niveau 2", "N2", ""), (classe.Genre, classe.NomComplet, classe.Abrege, classe.Prenom));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ClasseCreee);
    }

    [Theory]
    [InlineData("Niveau 2", "N2")]
    [InlineData("Initiateurs", "IN")]
    [InlineData("Plongée enfants", "PE")]
    [InlineData("Niveau 2 encadrant", "N2")]
    public void L_abreviation_proposee_suit_le_nom(string nom, string attendu) =>
        Assert.Equal(attendu, ServiceMoniteurs.AbregeDe(nom));

    // Règle : une classe n'a ni téléphone, ni niveau, ni TIV ; son genre ne change pas à la modification.
    [Fact]
    public async Task Une_classe_n_est_jamais_inspecteur_TIV_et_garde_son_genre()
    {
        var id = (await AjouterClasseAsync("Niveau 3")).Id!.Value;
        _ctx.Pascal.InspecteurTiv = true;

        var r = await _ctx.Moniteurs.EnregistrerAsync(new Moniteur
        {
            Id = id, Genre = GenreResponsable.Moniteur, Prenom = "Niveau", Nom = "3 bis", Telephone = "06", Niveau = "MF1", InspecteurTiv = true,
        });

        Assert.True(r.Reussi, r.Message);
        var classe = _ctx.Db.Moniteurs.Single(m => m.Id == id);
        Assert.Equal((GenreResponsable.Classe, "Niveau 3 bis", "", null, false), (classe.Genre, classe.NomComplet, classe.Telephone, classe.Niveau, classe.InspecteurTiv));
        Assert.Equal([_ctx.Pascal.Id], (await _ctx.Moniteurs.ListerInspecteursTivAsync()).Select(m => m.Id));
        Assert.Equal(["Niveau 3 bis"], (await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Classe)).Select(m => m.NomComplet));
        Assert.DoesNotContain(await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Moniteur), m => m.EstClasse);
    }

    // Règle : une classe active peut être responsable d'une sortie ; inactive, elle n'est plus proposée.
    [Fact]
    public async Task Une_classe_peut_etre_responsable_d_une_sortie()
    {
        var n2 = (await AjouterClasseAsync("Niveau 2")).Id!.Value;
        var ancienne = (await AjouterClasseAsync("Ancienne", actif: false)).Id!.Value;

        var r = await _ctx.Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Fosse, "Entraînement fosse N2", n2));
        Assert.True(r.Reussi, r.Message);
        var resume = Assert.Single(await _ctx.Sorties.ListerEnCoursAsync());
        Assert.Equal("Classe : Niveau 2", resume.LibelleResponsable);

        Assert.Equal("Cette classe n'est pas disponible.",
            (await _ctx.Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Fosse, "Fosse", ancienne))).Message);
        Assert.Equal("Choisissez le responsable : un moniteur ou une classe.",
            (await _ctx.Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Fosse, "Fosse", null))).Message);

        // La fiche de la classe compte ses sorties de la saison, comme pour un moniteur.
        Assert.Equal(1, (await _ctx.Moniteurs.ListerFichesAsync()).Single(f => f.Moniteur.Id == n2).SortiesSaison);
    }
}
