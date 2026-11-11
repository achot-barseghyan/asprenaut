using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Ajouter un composant à un détendeur (fiche de suivi EPI).</summary>
public class ComposantsTests
{
    private readonly Contexte _ctx = new();

    private Equipement Detendeur()
    {
        var det = _ctx.Equipement("A24", categorie: "Détendeurs");
        det.Libelle = "Détendeur Scubapro";
        det.Marque = "Scubapro";
        det.Modele = "MK25 EVO";
        det.ModeleDeuxiemeEtage = "A700";
        return det;
    }

    private static NouveauComposant Octopus(string modele = "R095", string fabrication = "03/2019", DateOnly? service = null, bool noter = true) =>
        new("Octopus", modele, "1641000963", fabrication, service ?? new DateOnly(2026, 11, 6), OrigineComposant.Recuperee, noter);

    // Règle : la composition actuelle montre les quatre composants, présents ou non.
    [Fact]
    public void La_composition_montre_les_composants_presents_et_absents()
    {
        var composition = ServiceInventaire.Composition(Detendeur());

        Assert.Equal(
            [("Premier étage", true, "MK25 EVO"), ("Deuxième étage", true, "A700"), ("Octopus", false, ""), ("Manomètre", false, "")],
            composition.Select(c => (c.Role, c.Present, c.Modele)));
    }

    // Règle : un octopus ajouté va dans la fiche matériel, ses dates et son origine dans la table composant, et une
    // intervention « Montage octopus » est notée sur ce composant sans changer les échéances du détendeur.
    [Fact]
    public async Task Ajouter_un_octopus_le_note_dans_la_fiche_et_l_historique()
    {
        var det = Detendeur();
        det.DateProchaineRevision = new DateOnly(2027, 3, 1);

        var r = await _ctx.Inventaire.AjouterComposantAsync(det.Id, Octopus());

        Assert.True(r.Reussi, r.Message);
        Assert.Equal(("R095", "1641000963"), (det.Octopus, det.NumeroOctopus));
        var detail = Assert.Single(_ctx.Db.Composants);
        Assert.Equal((new DateOnly(2019, 3, 1), new DateOnly(2026, 11, 6), OrigineComposant.Recuperee),
            (detail.Fabrication, detail.MiseEnService, detail.Origine));
        var montage = Assert.Single(_ctx.Db.Controles);
        Assert.Equal(("Montage octopus", "Octopus", ResultatControle.Conforme), (montage.Nature, montage.Composant, montage.Resultat));
        Assert.Contains("Récupéré sur un autre détendeur", montage.Notes);
        Assert.Equal(new DateOnly(2027, 3, 1), det.DateProchaineRevision);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ComposantAjoute && j.EquipementId == det.Id);

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(det.Id);
        var carte = fiche!.Composants.Single(c => c.Role == "Octopus");
        Assert.Equal(["Fabrication", "Mise en service", "Dernier contrôle", "Prochaine révision", "Origine", "État"], carte.Tuiles.Select(t => t.Libelle));
        Assert.Equal(("03/2019", "06/11/2026", "Récupérée"), (carte.Tuiles[0].Valeur, carte.Tuiles[1].Valeur, carte.Tuiles[4].Valeur));
    }

    [Fact]
    public async Task Le_numero_du_manometre_est_garde_et_l_historique_est_optionnel()
    {
        var det = Detendeur();

        var r = await _ctx.Inventaire.AjouterComposantAsync(det.Id,
            new NouveauComposant("manomètre", "Suunto", "SM-77", "", null, OrigineComposant.Neuve, NoterDansHistorique: false));

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Suunto", det.Manometre);
        Assert.Empty(_ctx.Db.Controles);
        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(det.Id);
        Assert.Equal("SM-77", fiche!.Composants.Single(c => c.Role == "Manomètre").NumeroSerie);
    }

    [Theory]
    [InlineData("Deuxième étage", "R295", "", "déjà")]          // Déjà présent.
    [InlineData("Premier étage", "MK2", "", "Choisissez")]     // Le premier étage ne s'ajoute pas.
    [InlineData("Octopus", "  ", "", "modèle")]                // Modèle obligatoire.
    [InlineData("Octopus", "R095", "13/2019", "mois")]         // Fabrication illisible.
    [InlineData("Octopus", "R095", "12/2026", "futur")]        // Fabrication dans le futur.
    public async Task Un_ajout_incorrect_est_refuse(string role, string modele, string fabrication, string attendu)
    {
        var det = Detendeur();

        var r = await _ctx.Inventaire.AjouterComposantAsync(det.Id,
            new NouveauComposant(role, modele, "", fabrication, null, OrigineComposant.Neuve, true));

        Assert.False(r.Reussi);
        Assert.Contains(attendu, r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_ctx.Db.Composants);
    }

    [Fact]
    public async Task La_mise_en_service_ne_precede_pas_la_fabrication_et_un_bloc_n_a_pas_de_composant()
    {
        var det = Detendeur();
        var bloc = _ctx.Equipement("BLOC-031");

        var avant = await _ctx.Inventaire.AjouterComposantAsync(det.Id, Octopus(fabrication: "05/2026", service: new DateOnly(2026, 4, 30)));
        var surBloc = await _ctx.Inventaire.AjouterComposantAsync(bloc.Id, Octopus());

        Assert.False(avant.Reussi);
        Assert.False(surBloc.Reussi);
        Assert.Equal((new DateOnly(2019, 3, 1), true), ServiceInventaire.LireMoisAnnee("3/2019"));
        Assert.Equal((null, true), ServiceInventaire.LireMoisAnnee(" "));
        Assert.False(ServiceInventaire.LireMoisAnnee("mars 2019").Valide);
    }

    [Fact]
    public async Task Les_modeles_connus_viennent_de_l_inventaire()
    {
        var a = Detendeur();
        var b = _ctx.Equipement("A25", categorie: "Détendeurs");
        b.ModeleDeuxiemeEtage = "R295";
        b.Octopus = "R095";
        b.Manometre = "Suunto";
        a.Octopus = "R095";

        Assert.Equal(["R095", "A700", "R295"], await _ctx.Inventaire.ModelesConnusAsync("Octopus"));
        Assert.Equal(["Suunto"], await _ctx.Inventaire.ModelesConnusAsync("Manomètre"));
    }
}
