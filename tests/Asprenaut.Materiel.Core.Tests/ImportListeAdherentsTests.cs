using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>
/// Liste des adhérents exportée dans le modèle Excel, corrigée puis réimportée (règle 30) : chaque ligne retrouve sa fiche
/// (N° fiche, licence, nom et prénom), jamais de doublon.
/// </summary>
public class ImportListeAdherentsTests
{
    private readonly Contexte _ctx = new();

    private async Task<Adherent> AdherentAsync(string prenom, string nom, string? licence = null)
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = prenom, Nom = nom, Licence = licence });
        Assert.True(r.Reussi, r.Message);
        return _ctx.Db.Adherents.Single(x => x.Id == r.Id);
    }

    private async Task<List<List<string>>> ListeExporteeAsync()
    {
        var entete = ServiceAdherents.ColonnesModele([], avecListe: true).Select(c => c.EnTete).ToList();
        var lignes = await _ctx.Adherents.LignesModeleAsync();
        Assert.All(lignes, l => Assert.Equal(entete.Count, l.Count));
        return [entete, .. lignes.Select(l => l.ToList())];
    }

    private static int Colonne(List<List<string>> tableau, string enTete) => tableau[0].IndexOf(enTete);

    [Fact]
    public async Task La_liste_exportee_se_reimporte_sans_doublon_avec_les_corrections_et_les_nouveaux()
    {
        var julie = await AdherentAsync("Julie", "Martin", "A-12-345678");
        var lea = await AdherentAsync("Léa", "Duboi");
        var tableau = await ListeExporteeAsync();
        Assert.Equal(3, tableau.Count);
        Assert.Equal(lea.Id.ToString(), tableau[1][Colonne(tableau, ServiceAdherents.EnTeteFiche)]); // Par nom : Duboi avant Martin.

        // Julie a obtenu son niveau 2 ; le nom de Léa est corrigé ; un nouvel adhérent est ajouté en bas.
        tableau[2][Colonne(tableau, "Niveau obtenu")] = "Niveau 2";
        tableau[1][Colonne(tableau, "Nom")] = "Dubois-Petit";
        tableau.Add(["Petit", "Hugo", "", "", "", "", "", "", "", ""]);

        var r = await _ctx.Adherents.ImporterAsync(tableau.Select(l => (IReadOnlyList<string>)l).ToList());

        Assert.Equal("Import terminé : 1 adhérent ajouté, 2 mis à jour.", r.Message);
        Assert.Equal(3, _ctx.Db.Adherents.Count);
        Assert.Equal("Niveau 2", _ctx.Db.Adherents.Single(a => a.Id == julie.Id).NiveauObtenu);
        Assert.Equal("Dubois-Petit", _ctx.Db.Adherents.Single(a => a.Id == lea.Id).Nom);

        // Le même fichier réimporté : rien de plus.
        Assert.Equal("Import terminé : 0 adhérent ajouté, 3 mis à jour.",
            (await _ctx.Adherents.ImporterAsync(tableau.Select(l => (IReadOnlyList<string>)l).ToList())).Message);
        Assert.Equal(3, _ctx.Db.Adherents.Count);
    }

    [Fact]
    public async Task La_licence_reconnait_la_personne_meme_ecrite_autrement_et_meme_si_son_nom_a_change()
    {
        var julie = await AdherentAsync("Julie", "Martin", "A-12-345678");

        var r = await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Licence"], ["Martin-Durand", "Julie", "a12 345678"]]);

        Assert.Equal("Import terminé : 0 adhérent ajouté, 1 mis à jour.", r.Message);
        Assert.Equal("Martin-Durand", _ctx.Db.Adherents.Single().Nom);
        Assert.Equal(julie.Id, _ctx.Db.Adherents.Single().Id);
    }

    [Fact]
    public async Task Une_licence_corrigee_met_a_jour_la_fiche_du_meme_nom()
    {
        var julie = await AdherentAsync("Julie", "Martin", "L1");

        var r = await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Licence"], ["MARTIN", "julie", "L2"]]);

        Assert.Equal("Import terminé : 0 adhérent ajouté, 1 mis à jour.", r.Message);
        Assert.Equal((julie.Id, "L2"), (_ctx.Db.Adherents.Single().Id, _ctx.Db.Adherents.Single().Licence));
    }

    [Fact]
    public async Task Deux_homonymes_de_licences_differentes_restent_deux_personnes()
    {
        await AdherentAsync("Jean", "Martin", "L1");

        var r = await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Licence"], ["Martin", "Jean", "L1"], ["Martin", "Jean", "L2"]]);

        Assert.Equal("Import terminé : 1 adhérent ajouté, 1 mis à jour.", r.Message);
        Assert.Equal(["L1", "L2"], _ctx.Db.Adherents.Select(a => a.Licence).Order());
    }

    [Fact]
    public async Task La_meme_personne_deux_fois_dans_le_fichier_n_est_traitee_qu_une_fois()
    {
        await AdherentAsync("Julie", "Martin");

        var r = await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Téléphone"], ["Martin", "Julie", "0611223344"], ["Petit", "Hugo", ""], ["Martin", "Julie", ""], ["Petit", "Hugo", ""]]);

        Assert.Equal("Import terminé : 1 adhérent ajouté, 1 mis à jour, 2 lignes en double ignorées (lignes 4, 5).", r.Message);
        Assert.Equal(2, _ctx.Db.Adherents.Count);
    }

    [Fact]
    public async Task Un_numero_de_fiche_recopie_sur_une_autre_personne_n_ecrase_pas_sa_fiche()
    {
        var julie = await AdherentAsync("Julie", "Martin", "L1");

        var r = await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "N° fiche"], ["Bernard", "Paul", julie.Id.ToString()]]);

        Assert.Equal("Import terminé : 1 adhérent ajouté, 0 mis à jour.", r.Message);
        Assert.Equal("Martin", _ctx.Db.Adherents.Single(a => a.Id == julie.Id).Nom);
    }

    [Fact]
    public async Task La_colonne_enseignant_donne_ou_retire_le_role_sans_toucher_a_l_inspecteur_tiv()
    {
        var lea = await AdherentAsync("Léa", "Dubois");
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Paul", Nom = "Bernard" }, new RolesAdherent(true, "E2", InspecteurTiv: true));
        var paul = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;

        var tableau = await ListeExporteeAsync();
        Assert.Equal(("Oui", "E2"), (tableau[1][Colonne(tableau, "Enseignant")], tableau[1][Colonne(tableau, "Niveau d'encadrement")])); // Bernard avant Dubois.
        Assert.Equal(("Non", ""), (tableau[2][Colonne(tableau, "Enseignant")], tableau[2][Colonne(tableau, "Niveau d'encadrement")]));

        // Léa devient enseignante E1 ; Paul n'est plus enseignant ; Hugo arrive avec seulement un niveau d'encadrement (= enseignant).
        tableau[2][Colonne(tableau, "Enseignant")] = "Oui";
        tableau[2][Colonne(tableau, "Niveau d'encadrement")] = "e1";
        tableau[1][Colonne(tableau, "Enseignant")] = "Non";
        var hugo = Enumerable.Repeat("", tableau[0].Count).ToList();
        (hugo[0], hugo[1], hugo[Colonne(tableau, "Niveau d'encadrement")]) = ("Petit", "Hugo", "MF1");
        tableau.Add(hugo);

        Assert.True((await _ctx.Adherents.ImporterAsync(tableau.Select(l => (IReadOnlyList<string>)l).ToList())).Reussi);

        var leaLue = (await _ctx.DepotAdherents.ObtenirAsync(lea.Id))!;
        Assert.Equal((true, "E1"), (leaLue.Enseignant, leaLue.NiveauEncadrement));
        var paulLu = (await _ctx.DepotAdherents.ObtenirAsync(paul.Id))!;
        Assert.Equal((paul.MoniteurId, false, true), (paulLu.MoniteurId, paulLu.Enseignant, paulLu.InspecteurTiv)); // Fiche gardée, TIV aussi.
        var hugoLu = _ctx.Db.Adherents.Single(a => a.Prenom == "Hugo");
        Assert.Equal((true, "MF1"), ((await _ctx.DepotAdherents.ObtenirAsync(hugoLu.Id))!.Enseignant, (await _ctx.DepotAdherents.ObtenirAsync(hugoLu.Id))!.NiveauEncadrement));
        Assert.Contains(await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Moniteur), m => m.Nom == "Dubois" && m.Prenom == "Léa");
    }

    [Fact]
    public async Task Une_case_enseignant_vide_ne_change_pas_le_role()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Paul", Nom = "Bernard" }, new RolesAdherent(true, "E3"));

        Assert.True((await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Enseignant", "Niveau d'encadrement"], ["Bernard", "Paul", "", ""]])).Reussi);

        var paul = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        Assert.Equal((true, "E3"), (paul.Enseignant, paul.NiveauEncadrement));
    }

    [Fact]
    public async Task La_liste_exportee_ne_contient_que_les_adherents_actifs()
    {
        await AdherentAsync("Julie", "Martin");
        var hugo = await AdherentAsync("Hugo", "Petit");
        hugo.Actif = false;
        await _ctx.Adherents.EnregistrerAsync(hugo);

        var lignes = await _ctx.Adherents.LignesModeleAsync();

        Assert.Equal("Martin", Assert.Single(lignes)[0]);
        Assert.StartsWith("Liste des adhérents - Asprenaut - ", ServiceAdherents.NomFichierModele("Asprenaut", new DateOnly(2026, 11, 17)));
    }
}
