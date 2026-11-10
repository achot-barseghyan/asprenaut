using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class RegistreTests
{
    private readonly Contexte _ctx = new();

    // Règle : le registre liste tous les blocs (même sans contrôle) puis le matériel suivi (un contrôle ou une révision prévue).
    [Fact]
    public async Task Le_registre_liste_les_blocs_puis_le_materiel_suivi_avec_leurs_controles()
    {
        _ctx.Pascal.InspecteurTiv = true;
        var bloc = _ctx.Equipement("BLOC-032");
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-099", EtatEquipement.Reforme); // Réformé sans historique : absent.
        _ctx.Equipement("DET-001", categorie: "Détendeurs", revision: new DateOnly(2027, 1, 1));
        _ctx.Equipement("DET-002", categorie: "Détendeurs"); // Ni contrôle ni révision prévue : absent.
        await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.ASurveiller, new DateOnly(2026, 3, 2), _ctx.Pascal.Id);
        await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, new DateOnly(2025, 3, 2), _ctx.Pascal.Id);

        var registre = await _ctx.Revisions.RegistreAsync();

        Assert.Equal(["BLOC-031", "BLOC-032"], registre.Blocs.Select(f => f.Code));
        Assert.Equal(["DET-001"], registre.Autres.Select(f => f.Code));
        Assert.Empty(registre.Blocs[0].Controles);
        Assert.Equal(
            [
                new LigneRegistre(new DateOnly(2025, 3, 2), "TIV annuelle", "Conforme", ResultatControle.Conforme, "Pascal Martin", ""),
                new LigneRegistre(new DateOnly(2026, 3, 2), "TIV annuelle", "À surveiller", ResultatControle.ASurveiller, "Pascal Martin", ""),
            ],
            registre.Blocs[1].Controles);
        Assert.Equal(2, registre.NombreControles);
        Assert.Equal(new DateOnly(2026, 11, 6), registre.Edition);
    }
}
