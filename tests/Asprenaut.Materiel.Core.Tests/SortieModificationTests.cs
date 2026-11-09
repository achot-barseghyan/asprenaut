using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Modifier une sortie pendant la préparation du départ, supprimer une sortie vide.</summary>
public class SortieModificationTests
{
    private readonly Contexte _ctx = new();

    private NouvelleSortie Demande(string intitule = "Fosse N2", long? moniteurId = null) =>
        new(new DateOnly(2026, 11, 14), TypeSortie.Fosse, intitule, moniteurId ?? _ctx.Pascal.Id, " Fosse de Villeneuve ", "8 élèves");

    // Règle : une sortie se modifie tant que le départ n'est pas validé (même avec du matériel déjà scanné).
    [Fact]
    public async Task Une_sortie_en_preparation_se_modifie()
    {
        _ctx.Equipement("BLOC-031");
        var id = await _ctx.SortieAsync("Piscine");
        await _ctx.Scan.ScannerDepartAsync(id, "BLOC-031");

        var r = await _ctx.Sorties.ModifierAsync(id, Demande());

        Assert.True(r.Reussi, r.Message);
        var s = _ctx.Db.Sorties.Single(x => x.Id == id);
        Assert.Equal((new DateOnly(2026, 11, 14), TypeSortie.Fosse, "Fosse N2", "Fosse de Villeneuve", "8 élèves"), (s.DateSortie, s.Type, s.Intitule, s.Lieu, s.Notes));
        Assert.Equal(StatutSortie.EnPreparation, s.Statut);
        Assert.Single(_ctx.Db.Affectations); // Le matériel scanné reste dans la sortie.
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.SortieModifiee && j.SortieId == id);
    }

    [Fact]
    public async Task Une_sortie_partie_ne_se_modifie_plus()
    {
        _ctx.Equipement("BLOC-031");
        var id = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");

        var r = await _ctx.Sorties.ModifierAsync(id, Demande());

        Assert.False(r.Reussi);
        Assert.Equal("Piscine", _ctx.Db.Sorties.Single(x => x.Id == id).Intitule);
    }

    [Fact]
    public async Task La_modification_verifie_intitule_et_moniteur()
    {
        var id = await _ctx.SortieAsync("Piscine");
        var inactif = new Moniteur { Id = _ctx.Db.ProchainId(), Prenom = "Paul", Nom = "Ancien", Actif = false };
        _ctx.Db.Moniteurs.Add(inactif);

        Assert.False((await _ctx.Sorties.ModifierAsync(id, Demande("  "))).Reussi);
        Assert.False((await _ctx.Sorties.ModifierAsync(id, Demande(moniteurId: inactif.Id))).Reussi);
        Assert.False((await _ctx.Sorties.ModifierAsync(999, Demande())).Reussi);
        Assert.Equal("Piscine", _ctx.Db.Sorties.Single(x => x.Id == id).Intitule);

        // Le moniteur déjà responsable reste accepté s'il est devenu inactif entre-temps.
        _ctx.Pascal.Actif = false;
        Assert.True((await _ctx.Sorties.ModifierAsync(id, Demande())).Reussi);
    }

    // Règle : seule une sortie sans matériel peut être supprimée.
    [Fact]
    public async Task Une_sortie_vide_se_supprime()
    {
        var id = await _ctx.SortieAsync("Créée par erreur");

        var r = await _ctx.Sorties.SupprimerAsync(id);

        Assert.True(r.Reussi, r.Message);
        Assert.DoesNotContain(_ctx.Db.Sorties, s => s.Id == id);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.SortieSupprimee && j.Details.Contains("Créée par erreur"));
    }

    [Fact]
    public async Task Une_sortie_avec_du_materiel_ne_se_supprime_pas()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var id = await _ctx.SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(id, "BLOC-031");

        Assert.False((await _ctx.Sorties.SupprimerAsync(id)).Reussi);
        Assert.Contains(_ctx.Db.Sorties, s => s.Id == id);

        // Une fois le matériel retiré, elle est vide : on peut la supprimer.
        await _ctx.Scan.RetirerDuDepartAsync(id, bloc.Id);
        Assert.True((await _ctx.Sorties.SupprimerAsync(id)).Reussi);
    }

    [Fact]
    public async Task L_historique_decrit_modification_et_suppression()
    {
        var id = await _ctx.SortieAsync("Piscine");
        await _ctx.Sorties.ModifierAsync(id, Demande());
        await _ctx.Sorties.SupprimerAsync(id);

        var evenements = await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout);

        var suppression = evenements.First(e => e.Titre == "Sortie supprimée : Fosse N2");
        Assert.Equal(TypeEvenement.Sortie, suppression.Type);
        Assert.Contains("« Fosse N2 » du 14/11/2026", suppression.Detail);
        var creation = evenements.Single(e => e.Titre == "Sortie créée : Piscine"); // Sortie disparue : intitulé repris du texte.
        Assert.StartsWith("Sortie « Piscine » du 06/11/2026", creation.Detail);
        Assert.Contains(evenements, e => e.Titre == "Sortie modifiée : Fosse N2");
    }
}
