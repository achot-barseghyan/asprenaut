using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Sorties autorisées d'un équipement (règle 47) : types de sortie interdits par sa fiche.</summary>
public class SortiesAutoriseesTests
{
    private readonly Contexte _ctx = new();

    private long Type(string nom) => _ctx.Db.TypesSortie.Single(t => t.Nom == nom).Id;

    [Fact]
    public void Les_types_interdits_sont_lus_et_ecrits_sans_doublon_et_tries()
    {
        Assert.Equal([3L, 5L], Equipement.LireTypesInterdits(" 5; 3;3;x;-2;"));
        Assert.Empty(Equipement.LireTypesInterdits(null));
        Assert.Equal("3;5", Equipement.EcrireTypesInterdits([5, 3, 3, 0]));
        Assert.True(Equipement.AutoriseEn("3;5", 4));
        Assert.False(Equipement.AutoriseEn("3;5", 5));
        Assert.True(Equipement.AutoriseEn("3;5", null)); // Sortie sans type : rien à vérifier.
    }

    [Fact]
    public async Task Un_equipement_interdit_dans_ce_type_de_sortie_ne_part_pas()
    {
        var detendeur = _ctx.Equipement("A11", categorie: "Détendeurs");
        detendeur.TypesSortieInterdits = Equipement.EcrireTypesInterdits([Type("Mer"), Type("Lac")]);
        _ctx.Equipement("A12", categorie: "Détendeurs");
        var piscine = await _ctx.SortieAsync();
        var mer = (await _ctx.Sorties.CreerAsync(new NouvelleSortie(_ctx.Horloge.AujourdHui, TypeSortie.Mer, "Marseille", _ctx.Pascal.Id, TypeId: Type("Mer")))).Id!.Value;

        var refus = await _ctx.Scan.ScannerDepartAsync(mer, "A11");
        Assert.Equal(NiveauResultat.Erreur, refus.Niveau);
        Assert.Contains("« Mer »", refus.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_ctx.Db.Affectations, a => a.EquipementId == detendeur.Id);

        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(mer, "A12")).Niveau);
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(piscine, "A11")).Niveau);
        Assert.Equal(StatutSortieEquipement.Dehors, _ctx.StatutDe(piscine, detendeur));
    }

    [Fact]
    public async Task La_fiche_garde_les_types_connus_et_le_journal_note_le_changement()
    {
        var categorie = _ctx.Db.Categories.Single(c => c.Nom == "Détendeurs").Id;
        var cree = await _ctx.Inventaire.CreerAsync(new Equipement
        {
            Code = "A11", Libelle = "Détendeur adulte A11", CategorieId = categorie,
            TypesSortieInterdits = $"{Type("Mer")};{Type("Lac")};{Type("Lac")};99999",
        });
        Assert.True(cree.Reussi, cree.Message);
        var a11 = _ctx.Db.Equipements.Single(e => e.Code == "A11");
        Assert.Equal(Equipement.EcrireTypesInterdits([Type("Mer"), Type("Lac")]), a11.TypesSortieInterdits); // 99999 : type inconnu.

        var modifie = await _ctx.Inventaire.ModifierAsync(new Equipement
        {
            Id = a11.Id, Code = "A11", Libelle = a11.Libelle, CategorieId = categorie, TypesSortieInterdits = "",
        });
        Assert.True(modifie.Reussi, modifie.Message);
        Assert.Equal("", a11.TypesSortieInterdits);
        Assert.Contains("sorties autorisées", _ctx.Db.Journal.Last().Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changer_le_type_d_une_sortie_est_refuse_si_du_materiel_scanne_y_est_interdit()
    {
        var detendeur = _ctx.Equipement("A11", categorie: "Détendeurs");
        detendeur.TypesSortieInterdits = Equipement.EcrireTypesInterdits([Type("Mer")]);
        var sortie = await _ctx.SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "A11");

        NouvelleSortie Demande(string type, TypeSortie famille) => new(_ctx.Horloge.AujourdHui, famille, "Sortie", _ctx.Pascal.Id, TypeId: Type(type));
        var refus = await _ctx.Sorties.ModifierAsync(sortie, Demande("Mer", TypeSortie.Mer));
        Assert.False(refus.Reussi);
        Assert.Contains("A11", refus.Message, StringComparison.Ordinal);
        Assert.Equal(Type("Piscine"), _ctx.Db.Sorties.Single(s => s.Id == sortie).TypeId);

        Assert.True((await _ctx.Sorties.ModifierAsync(sortie, Demande("Fosse", TypeSortie.Fosse))).Reussi);
    }

    [Fact]
    public async Task La_fiche_propose_les_types_proposes_et_un_type_masque_deja_interdit()
    {
        var lac = _ctx.Db.TypesSortie.Single(t => t.Nom == "Lac");
        var autre = _ctx.Db.TypesSortie.Single(t => t.Nom == "Autre");
        lac.Actif = false;
        autre.Actif = false;
        var e = new Equipement { TypesSortieInterdits = lac.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        var noms = (await _ctx.Inventaire.TypesPourAutorisationAsync(e)).Select(t => t.Nom).ToList();
        Assert.Contains("Lac", noms);
        Assert.DoesNotContain("Autre", noms);
        Assert.DoesNotContain("Lac", (await _ctx.Inventaire.TypesPourAutorisationAsync()).Select(t => t.Nom));
    }

    [Fact]
    public void Le_resume_dit_toutes_aucune_uniquement_ou_les_interdits()
    {
        var types = new[] { "Piscine", "Baptêmes", "Fosse", "Lac", "Mer" }
            .Select((n, i) => new TypeDeSortie { Id = i + 1, Nom = n }).ToList();
        string Resume(params long[] interdits) => ServiceInventaire.ResumerAutorisation(types, interdits).Texte;

        Assert.Equal("Toutes les sorties", Resume());
        Assert.False(ServiceInventaire.ResumerAutorisation(types, []).Restreint);
        Assert.Equal("Piscine uniquement", Resume(2, 3, 4, 5));
        Assert.Equal("Piscine et Fosse uniquement", Resume(2, 4, 5));
        Assert.Equal("Interdit : Lac, Mer", Resume(4, 5));
        Assert.Equal("Aucune sortie", Resume(1, 2, 3, 4, 5));
        Assert.True(ServiceInventaire.ResumerAutorisation(types, [1, 2, 3, 4, 5]).Aucune);
    }
}
