using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Page « Utilisations d'un équipement » (règle 29) : sorties, compteur après chaque retour, remise à zéro au contrôle.</summary>
public class UtilisationsEquipementTests
{
    private readonly Contexte _ctx = new();

    private void Le(int jour, int heure = 10) => _ctx.Horloge.MaintenantUtc = new DateTime(2026, 11, jour, heure, 0, 0, DateTimeKind.Utc);

    private async Task<long> SortieAsync(string type, string intitule, int? utilisations = null)
    {
        var typeId = _ctx.Db.TypesSortie.Single(t => t.Nom == type).Id;
        var r = await _ctx.Sorties.CreerAsync(new NouvelleSortie(_ctx.Horloge.AujourdHui, TypeSortie.Autre, intitule, _ctx.Pascal.Id, TypeId: typeId, Utilisations: utilisations));
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    private async Task<long> AllerRetourAsync(string type, string intitule, int? utilisations = null, bool retour = true)
    {
        var sortie = await SortieAsync(type, intitule, utilisations);
        await _ctx.Scan.ScannerDepartAsync(sortie, "A07");
        await _ctx.Sorties.ValiderDepartAsync(sortie);
        if (retour)
        {
            _ctx.Horloge.MaintenantUtc = _ctx.Horloge.MaintenantUtc.AddHours(2);
            await _ctx.Scan.ScannerRetourAsync(sortie, "A07");
        }

        return sortie;
    }

    /// <summary>Mer (4) le 1er, piscine à 2 le 3, révision le 3 au soir, piscine (1) le 5, lac encore dehors le 6.</summary>
    private async Task<(Equipement A07, long Piscine)> HistoireAsync()
    {
        var a07 = _ctx.Equipement("A07", categorie: "Détendeurs");
        Le(1);
        await AllerRetourAsync("Mer", "Week-end Marseille");
        Le(3);
        await AllerRetourAsync("Piscine", "Double séance", utilisations: 2);
        Assert.True((await _ctx.Revisions.EnregistrerControleAsync(a07.Id, TypeControle.Revision, ResultatControle.Conforme, _ctx.Horloge.AujourdHui, null,
            "Révision annuelle", "Atelier Dupont")).Reussi);
        Le(5);
        var piscine = await AllerRetourAsync("Piscine", "Cours en piscine");
        Le(6);
        await AllerRetourAsync("Lac", "Lac des Settons", retour: false);
        return (a07, piscine);
    }

    [Fact]
    public async Task Chaque_retour_ajoute_ses_utilisations_et_le_controle_remet_a_zero()
    {
        var (a07, _) = await HistoireAsync();

        var u = (await _ctx.Historique.ObtenirUtilisationsAsync(a07.Id))!;

        Assert.Equal(["Lac des Settons", "Cours en piscine", "Révision annuelle · Atelier Dupont", "Double séance", "Week-end Marseille"], u.Lignes.Select(l => l.Titre));
        Assert.Equal(new int?[] { null, 1, null, 6, 4 }, u.Lignes.Select(l => l.Compteur));
        Assert.True(u.Lignes[2].EstControle);
        Assert.Equal((false, EtatRetourUtilisation.Dehors, "Sorti"), (u.Lignes[0].Comptee, u.Lignes[0].Retour, u.Lignes[0].LibelleRetour));
        Assert.Equal((2, true), (u.Lignes[3].Utilisations, u.Lignes[3].Modifiees)); // Piscine : 1 par défaut, 2 pour cette sortie.
        Assert.False(u.Lignes[4].Modifiees);

        // Le compteur de la page est celui du départ (requête du dépôt) : 1 depuis la révision.
        Assert.Equal((1, 100, 99), (u.Compteur, u.Seuil!.Value, u.Reste!.Value));
        Assert.Equal((2, "Révisé le 03/11/2026", 7, "Depuis 11/2026 · 1 contrôle"), (u.SortiesDepuisControle, u.DernierControle, u.Total, u.DetailTotal));
    }

    [Fact]
    public async Task Le_filtre_par_type_garde_les_controles()
    {
        var (a07, _) = await HistoireAsync();

        var u = (await _ctx.Historique.ObtenirUtilisationsAsync(a07.Id))!;

        Assert.Equal([("Piscine", 2), ("Mer", 1), ("Lac", 1)], u.Types.Select(t => (t.Type, t.Nombre)));
        Assert.Equal(["Cours en piscine", "Révision annuelle · Atelier Dupont", "Double séance"], u.LignesDe("Piscine").Select(l => l.Titre));
    }

    [Fact]
    public async Task Un_probleme_signale_au_retour_apparait_sur_sa_sortie()
    {
        var (a07, piscine) = await HistoireAsync();
        Assert.True((await _ctx.Signalements.SignalerAsync(new NouveauSignalement(a07.Id, piscine, "Débit continu", GraviteSignalement.ASurveiller))).Reussi);

        var u = (await _ctx.Historique.ObtenirUtilisationsAsync(a07.Id))!;

        var ligne = u.Lignes.Single(l => l.SortieId == piscine);
        Assert.Equal((EtatRetourUtilisation.Signale, "Rendu · À surveiller · Débit continu"), (ligne.Retour, ligne.LibelleRetour));
        Assert.Equal(1, u.Problemes);
    }

    [Fact]
    public async Task Sans_sortie_ni_controle_la_page_est_vide_et_l_export_a_son_entete()
    {
        var bloc = _ctx.Equipement("BLOC-001");

        var u = (await _ctx.Historique.ObtenirUtilisationsAsync(bloc.Id))!;

        Assert.Empty(u.Lignes);
        Assert.Equal((0, "Jamais contrôlé", 0), (u.Compteur, u.DernierControle, u.Total));
        Assert.Null(await _ctx.Historique.ObtenirUtilisationsAsync(9999));
        Assert.StartsWith("Code;Date;Sortie;Type;Responsable", ServiceHistorique.ExporterUtilisationsCsv(u, u.Lignes));
    }

    [Fact]
    public async Task L_export_reprend_les_lignes_affichees()
    {
        var (a07, _) = await HistoireAsync();
        var u = (await _ctx.Historique.ObtenirUtilisationsAsync(a07.Id))!;

        var csv = ServiceHistorique.ExporterUtilisationsCsv(u, u.LignesDe("Mer")).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, csv.Length);
        Assert.Contains("Compteur remis à 0", csv[1]);
        Assert.StartsWith("A07;01/11/2026;Week-end Marseille;Mer;", csv[2]);
        Assert.EndsWith(";4;4;Rendu", csv[2]);
    }
}
