using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Utilisations par sortie : réglées par type (piscine 1, lac 2, mer 4), modifiables sur chaque sortie, cumulées par le compteur.</summary>
public class UtilisationsTests
{
    private readonly Contexte _ctx = new();

    private long Type(string nom) => _ctx.Db.TypesSortie.Single(t => t.Nom == nom).Id;

    private async Task<long> SortieAsync(string type, int? utilisations = null)
    {
        var r = await _ctx.Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 7), TypeSortie.Autre, type, _ctx.Pascal.Id, TypeId: Type(type), Utilisations: utilisations));
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    private async Task AllerRetourAsync(long sortie, string code)
    {
        await _ctx.Scan.ScannerDepartAsync(sortie, code);
        await _ctx.Sorties.ValiderDepartAsync(sortie);
        await _ctx.Scan.ScannerRetourAsync(sortie, code);
    }

    [Fact]
    public void Les_types_d_origine_ont_leurs_utilisations_par_defaut()
    {
        Assert.Equal([1, 1, 4, 2, 1, 1], _ctx.Db.TypesSortie.OrderBy(t => t.Ordre).Select(t => t.Utilisations));
    }

    // Règle : une sortie prend les utilisations de son type ; on peut les changer sur la sortie (2 plongées dans la journée).
    [Fact]
    public async Task Une_sortie_prend_les_utilisations_de_son_type_ou_celles_choisies()
    {
        var mer = await SortieAsync("Mer");
        var piscine = await SortieAsync("Piscine", utilisations: 2);

        Assert.Equal(4, _ctx.Db.Sorties.Single(s => s.Id == mer).Utilisations);
        Assert.Equal(2, _ctx.Db.Sorties.Single(s => s.Id == piscine).Utilisations);
        Assert.Equal(2, (await _ctx.Sorties.ObtenirResumeAsync(piscine))!.Utilisations);
        Assert.Equal("Utilisations : entre 1 et 10 par sortie.",
            (await _ctx.Sorties.CreerAsync(new NouvelleSortie(new DateOnly(2026, 11, 7), TypeSortie.Autre, "X", _ctx.Pascal.Id, TypeId: Type("Mer"), Utilisations: 11))).Message);
    }

    [Fact]
    public async Task Modifier_la_sortie_garde_ses_utilisations_sauf_si_le_type_change()
    {
        var id = await SortieAsync("Piscine", utilisations: 2);

        await _ctx.Sorties.ModifierAsync(id, new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Autre, "Piscine", _ctx.Pascal.Id, TypeId: Type("Piscine")));
        Assert.Equal(2, _ctx.Db.Sorties.Single(s => s.Id == id).Utilisations);

        await _ctx.Sorties.ModifierAsync(id, new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Autre, "Lac", _ctx.Pascal.Id, TypeId: Type("Lac")));
        Assert.Equal(2, _ctx.Db.Sorties.Single(s => s.Id == id).Utilisations); // Lac : 2 par défaut.

        await _ctx.Sorties.ModifierAsync(id, new NouvelleSortie(new DateOnly(2026, 11, 8), TypeSortie.Autre, "Lac", _ctx.Pascal.Id, TypeId: Type("Lac"), Utilisations: 3));
        Assert.Equal(3, _ctx.Db.Sorties.Single(s => s.Id == id).Utilisations);
    }

    // Règle : chaque retour scanné ajoute au compteur les utilisations de sa sortie.
    [Fact]
    public async Task Le_compteur_cumule_les_utilisations_des_sorties()
    {
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        await AllerRetourAsync(await SortieAsync("Mer"), "DET-001");
        await AllerRetourAsync(await SortieAsync("Piscine"), "DET-001");
        await AllerRetourAsync(await SortieAsync("Lac", utilisations: 3), "DET-001");

        Assert.Equal(new UsageEquipement(8, 100, EtatCompteur.Normal), await _ctx.Compteur.UsageAsync(det));
    }

    [Fact]
    public async Task Les_utilisations_d_un_type_se_reglent_de_1_a_10_et_ne_changent_pas_les_sorties_existantes()
    {
        var mer = await SortieAsync("Mer");

        Assert.Equal(10, (await _ctx.TypesSortie.ChangerUtilisationsAsync(Type("Mer"), 15)).Id);
        Assert.Equal(1, (await _ctx.TypesSortie.ChangerUtilisationsAsync(Type("Lac"), 0)).Id);
        Assert.Equal(4, _ctx.Db.Sorties.Single(s => s.Id == mer).Utilisations);
        Assert.Equal(4, (await _ctx.TypesSortie.CreerAsync("Sortie bateau", TypeSortie.Mer)) is { Id: { } id } ? _ctx.Db.TypesSortie.Single(t => t.Id == id).Utilisations : 0);

        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        Assert.False((await _ctx.TypesSortie.ChangerUtilisationsAsync(Type("Mer"), 3)).Reussi);
    }

    // Règle : le bouton œil masque un type utilisé (plus proposé, historique gardé) et le réaffiche.
    [Fact]
    public async Task Le_bouton_oeil_masque_puis_reaffiche_un_type()
    {
        await SortieAsync("Lac");

        Assert.Equal("« Lac » masqué.", (await _ctx.TypesSortie.BasculerMasqueAsync(Type("Lac"))).Message);
        Assert.DoesNotContain(await _ctx.TypesSortie.ListerProposesAsync(), t => t.Nom == "Lac");
        Assert.Equal("« Lac » de nouveau proposé.", (await _ctx.TypesSortie.BasculerMasqueAsync(Type("Lac"))).Message);
        Assert.Contains(await _ctx.TypesSortie.ListerProposesAsync(), t => t.Nom == "Lac");
    }

    // Règle : en mode consultation, changer les utilisations d'une sortie (autre valeur que celle du type) demande le code.
    [Fact]
    public async Task Changer_les_utilisations_demande_le_mode_gestion()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        NouvelleSortie Demande(int? utilisations, string type = "Mer") =>
            new(new DateOnly(2026, 11, 7), TypeSortie.Autre, "Sortie", _ctx.Pascal.Id, TypeId: Type(type), Utilisations: utilisations);

        // Valeur du type (ou rien) : libre. Autre valeur : refusée.
        Assert.True((await _ctx.Sorties.CreerAsync(Demande(4))).Reussi);
        var id = (await _ctx.Sorties.CreerAsync(Demande(null))).Id!.Value;
        Assert.Equal(ServiceGestion.MessageVerrouille, (await _ctx.Sorties.CreerAsync(Demande(2))).Message);

        // Modifier : garder la valeur, ou prendre celle du nouveau type, reste libre.
        Assert.True((await _ctx.Sorties.ModifierAsync(id, Demande(4))).Reussi);
        Assert.True((await _ctx.Sorties.ModifierAsync(id, Demande(2, "Lac"))).Reussi);
        Assert.Equal(ServiceGestion.MessageVerrouille, (await _ctx.Sorties.ModifierAsync(id, Demande(3, "Lac"))).Message);
        Assert.Equal(2, _ctx.Db.Sorties.Single(s => s.Id == id).Utilisations);

        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        Assert.True((await _ctx.Sorties.ModifierAsync(id, Demande(3, "Lac"))).Reussi);
    }
}
