using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>
/// Un enseignant est un adhérent (règle 44, migration 029) : rôle d'enseignant dans la fiche de l'adhérent, rapprochement des
/// fiches existantes au démarrage, cas douteux à relier à la main, matériel pris au départ comme tout adhérent.
/// </summary>
public class EnseignantsAdherentsTests
{
    private readonly Contexte _ctx = new();

    private async Task<Adherent> AdherentAsync(string prenom, string nom, string? licence = null, bool actif = true, string? telephone = null)
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = prenom, Nom = nom, Licence = licence, Actif = actif, Telephone = telephone });
        Assert.True(r.Reussi, r.Message);
        return (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
    }

    private Moniteur Enseignant(string prenom, string nom, string licence = "", bool actif = true, string telephone = "")
    {
        var m = new Moniteur { Id = _ctx.Db.ProchainId(), Prenom = prenom, Nom = nom, Licence = licence, Actif = actif, Telephone = telephone, Niveau = "E2" };
        _ctx.Db.Moniteurs.Add(m);
        return m;
    }

    private async Task<Adherent?> AdherentDeAsync(Moniteur m) =>
        (await _ctx.DepotAdherents.ListerAsync(actifsSeulement: false)).SingleOrDefault(a => a.MoniteurId == m.Id);

    // ---------------------------------------------------------------- Rapprochement des fiches existantes

    [Fact]
    public async Task Le_doublon_cree_a_la_main_est_relie_a_l_enseignant_du_meme_nom()
    {
        var pascal = Enseignant("Pascal", "Vermenot", licence: "A-12-345678", telephone: "06 11 22 33 44");
        var doublon = await AdherentAsync("pascal", "VERMENOT");

        var bilan = await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.Equal(1, bilan.Relies);
        var lu = (await _ctx.DepotAdherents.ObtenirAsync(doublon.Id))!;
        Assert.Equal((pascal.Id, true, "E2"), (lu.MoniteurId, lu.Enseignant, lu.NiveauEncadrement));
        Assert.Equal(("A-12-345678", "06 11 22 33 44"), (lu.Licence, lu.Telephone)); // Repris de l'enseignant : il n'en avait pas.
        Assert.Single(_ctx.Db.Adherents, a => a.Nom.Equals("vermenot", StringComparison.OrdinalIgnoreCase)); // Pas de nouvelle fiche.
    }

    [Fact]
    public async Task Les_accents_et_les_majuscules_ne_comptent_pas()
    {
        var helene = Enseignant("Hélène", "Lefèvre");
        await AdherentAsync("Helene", "LEFEVRE");

        Assert.Equal(1, (await _ctx.Adherents.RelierEnseignantsAsync()).Relies);
        Assert.NotNull(await AdherentDeAsync(helene));
    }

    [Fact]
    public async Task Un_enseignant_sans_fiche_d_adherent_en_recoit_une()
    {
        var pascal = Enseignant("Pascal", "Durand", licence: "b-1", telephone: "06 00 00 00 00");
        await AdherentAsync("Julie", "Martin");

        var bilan = await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.Equal((0, 2, 0), (bilan.Relies, bilan.Crees, bilan.ARelier.Count)); // Pascal Durand, et Pascal Martin du contexte.
        var cree = (await AdherentDeAsync(pascal))!;
        Assert.Equal(("Pascal", "Durand", "B-1", "06 00 00 00 00", true), (cree.Prenom, cree.Nom, cree.Licence, cree.Telephone, cree.Actif));
        Assert.True(cree.Enseignant);
    }

    [Fact]
    public async Task Le_contexte_de_test_relie_aussi_pascal_martin()
    {
        // Pascal Martin (enseignant du contexte) n'a pas de fiche : elle est créée, il peut alors sortir du matériel.
        await _ctx.Adherents.RelierEnseignantsAsync();
        Assert.NotNull(await AdherentDeAsync(_ctx.Pascal));
    }

    [Fact]
    public async Task Deux_adherents_du_meme_nom_attendent_un_choix()
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        var a1 = await AdherentAsync("Pascal", "Vermenot", licence: "A-1");
        var a2 = await AdherentAsync("Pascal", "Vermenot", licence: "A-2");

        var bilan = await _ctx.Adherents.RelierEnseignantsAsync();

        var cas = bilan.ARelier.Single(c => c.Enseignant.Id == pascal.Id);
        Assert.Equal(new[] { a1.Id, a2.Id }.Order(), cas.Candidats.Select(c => c.Id).Order());
        Assert.Null(await AdherentDeAsync(pascal)); // Rien n'est deviné.
        Assert.Equal(2, _ctx.Db.Adherents.Count(a => a.Nom == "Vermenot")); // Aucune fiche créée.
        Assert.Contains(await _ctx.Adherents.ListerEnseignantsARelierAsync(), c => c.Enseignant.Id == pascal.Id);
    }

    [Fact]
    public async Task Deux_homonymes_sont_departages_par_la_licence()
    {
        var pascal = Enseignant("Pascal", "Vermenot", licence: "A-2");
        await AdherentAsync("Pascal", "Vermenot", licence: "A-1");
        var bon = await AdherentAsync("Pascal", "Vermenot", licence: "A-2");

        Assert.Equal(1, (await _ctx.Adherents.RelierEnseignantsAsync()).Relies);
        Assert.Equal(bon.Id, (await AdherentDeAsync(pascal))!.Id);
    }

    [Theory]
    [InlineData("Pascal", "Vermenaut")] // Faute de frappe sur le nom.
    [InlineData("Pascale", "Vermenot")] // Sur le prénom.
    [InlineData("Vermenot", "Pascal")] // Prénom et nom inversés.
    public async Task Un_nom_proche_n_est_ni_relie_ni_double(string prenom, string nom)
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        var proche = await AdherentAsync(prenom, nom);

        var bilan = await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.Equal(proche.Id, bilan.ARelier.Single(c => c.Enseignant.Id == pascal.Id).Candidats.Single().Id);
        Assert.Null(await AdherentDeAsync(pascal));
        Assert.DoesNotContain(_ctx.Db.Adherents, a => a.Prenom == "Pascal" && a.Nom == "Vermenot"); // Pas de troisième fiche.
    }

    [Fact]
    public async Task La_meme_licence_sous_un_autre_nom_est_proposee_sans_etre_reliee()
    {
        var pascal = Enseignant("Pascal", "Vermenot", licence: "A-12");
        var autre = await AdherentAsync("Paul", "Durand", licence: "A-12");

        var bilan = await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.Equal(autre.Id, bilan.ARelier.Single(c => c.Enseignant.Id == pascal.Id).Candidats.Single().Id);
    }

    [Fact]
    public async Task Le_rapprochement_ne_refait_rien_une_fois_tout_relie()
    {
        Enseignant("Pascal", "Vermenot");
        await AdherentAsync("Pascal", "Vermenot");
        await _ctx.Adherents.RelierEnseignantsAsync();
        var nombre = _ctx.Db.Adherents.Count;
        var journal = _ctx.Db.Journal.Count;

        var encore = await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.Equal((0, 0, 0), (encore.Relies, encore.Crees, encore.ARelier.Count));
        Assert.Equal((nombre, journal), (_ctx.Db.Adherents.Count, _ctx.Db.Journal.Count));
    }

    [Fact]
    public async Task Une_fiche_desactivee_par_l_ancien_passer_en_enseignant_redevient_active()
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        await AdherentAsync("Pascal", "Vermenot", actif: false);

        await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.True((await AdherentDeAsync(pascal))!.Actif); // Il peut de nouveau sortir du matériel.
    }

    [Fact]
    public async Task Un_ancien_enseignant_garde_son_lien_sans_le_role()
    {
        var ancien = Enseignant("Paul", "Durand", actif: false);
        await AdherentAsync("Paul", "Durand");

        await _ctx.Adherents.RelierEnseignantsAsync();

        var lu = (await AdherentDeAsync(ancien))!;
        Assert.Equal((true, false), (lu.Actif, lu.Enseignant));
    }

    [Fact]
    public async Task Les_classes_ne_sont_pas_des_personnes()
    {
        _ctx.AjouterClasses("Niveau 2");
        await _ctx.Adherents.RelierEnseignantsAsync();
        Assert.DoesNotContain(_ctx.Db.Adherents, a => a.Nom == "Niveau 2");
    }

    // ---------------------------------------------------------------- Relier à la main

    [Fact]
    public async Task On_choisit_la_bonne_fiche_parmi_les_homonymes()
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        await AdherentAsync("Pascal", "Vermenot", licence: "A-1");
        var bon = await AdherentAsync("Pascal", "Vermenot", licence: "A-2");

        var r = await _ctx.Adherents.RelierEnseignantAsync(pascal.Id, bon.Id);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal(bon.Id, (await AdherentDeAsync(pascal))!.Id);
        Assert.DoesNotContain(await _ctx.Adherents.ListerEnseignantsARelierAsync(), c => c.Enseignant.Id == pascal.Id);
        Assert.False((await _ctx.Adherents.RelierEnseignantAsync(pascal.Id, bon.Id)).Reussi); // Déjà relié.
    }

    [Fact]
    public async Task On_peut_creer_sa_fiche_quand_aucune_n_est_la_sienne()
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        var autre = await AdherentAsync("Pascal", "Vermenaut");

        var r = await _ctx.Adherents.RelierEnseignantAsync(pascal.Id, null);

        Assert.True(r.Reussi, r.Message);
        var cree = (await AdherentDeAsync(pascal))!;
        Assert.Equal((r.Id, "Vermenot"), (cree.Id, cree.Nom));
        Assert.Null((await _ctx.DepotAdherents.ObtenirAsync(autre.Id))!.MoniteurId);
    }

    [Fact]
    public async Task Une_fiche_deja_reliee_a_un_autre_enseignant_est_refusee()
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        var paul = Enseignant("Paul", "Vermenot");
        var a = await AdherentAsync("Pascal", "Vermenot");
        await _ctx.Adherents.RelierEnseignantsAsync();

        Assert.Equal(pascal.Id, (await _ctx.DepotAdherents.ObtenirAsync(a.Id))!.MoniteurId);
        Assert.False((await _ctx.Adherents.RelierEnseignantAsync(paul.Id, a.Id)).Reussi);
    }

    [Fact]
    public async Task Relier_a_la_main_demande_le_code()
    {
        var pascal = Enseignant("Pascal", "Vermenot");
        var a = await AdherentAsync("Pascal", "Vermenaut");
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.Adherents.RelierEnseignantAsync(pascal.Id, a.Id)).Reussi);
        Assert.Null(await AdherentDeAsync(pascal));
    }

    // ---------------------------------------------------------------- Rôle d'enseignant dans la fiche de l'adhérent

    [Fact]
    public async Task Cocher_enseignant_en_fait_un_responsable_de_sortie()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois", Telephone = "06 12 34 56 78", Licence = "c-9" },
            new RolesAdherent(true, "MF1", InspecteurTiv: true));

        Assert.True(r.Reussi, r.Message);
        var lea = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        Assert.Equal((true, "MF1", true), (lea.Enseignant, lea.NiveauEncadrement, lea.InspecteurTiv));
        var fiche = (await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Moniteur)).Single(m => m.Id == lea.MoniteurId);
        Assert.Equal(("Léa", "Dubois", "06 12 34 56 78", "C-9"), (fiche.Prenom, fiche.Nom, fiche.Telephone, fiche.Licence));
        Assert.Contains(await _ctx.Moniteurs.ListerInspecteursTivAsync(), m => m.Id == fiche.Id);
    }

    [Fact]
    public async Task Decocher_enseignant_le_retire_des_responsables_sans_rien_supprimer()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois" }, new RolesAdherent(true, "E2"));
        var lea = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        var ficheId = lea.MoniteurId!.Value;

        Assert.True((await _ctx.Adherents.EnregistrerAsync(new Adherent { Id = lea.Id, Prenom = "Léa", Nom = "Dubois", Actif = true }, new RolesAdherent(false))).Reussi);

        Assert.DoesNotContain(await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Moniteur), m => m.Id == ficheId);
        var lu = (await _ctx.DepotAdherents.ObtenirAsync(lea.Id))!;
        Assert.Equal((ficheId, false, "E2"), (lu.MoniteurId, lu.Enseignant, lu.NiveauEncadrement)); // Lien et niveau gardés.

        // Le recocher reprend la même fiche d'enseignant (ses sorties restent les siennes).
        Assert.True((await _ctx.Adherents.EnregistrerAsync(new Adherent { Id = lea.Id, Prenom = "Léa", Nom = "Dubois", Actif = true }, new RolesAdherent(true, "E3"))).Reussi);
        Assert.Equal((ficheId, true, "E3"), ((await _ctx.DepotAdherents.ObtenirAsync(lea.Id))!.MoniteurId, (await _ctx.DepotAdherents.ObtenirAsync(lea.Id))!.Enseignant, (await _ctx.DepotAdherents.ObtenirAsync(lea.Id))!.NiveauEncadrement));
        Assert.Single(_ctx.Db.Moniteurs, m => m.Nom == "Dubois");
    }

    [Fact]
    public async Task Sans_role_saisi_le_role_ne_change_pas_mais_l_identite_suit()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois" }, new RolesAdherent(true, "E2"));
        var id = r.Id!.Value;

        Assert.True((await _ctx.Adherents.EnregistrerAsync(new Adherent { Id = id, Prenom = "Léa", Nom = "Dubois-Martin", Telephone = "0612", Actif = true })).Reussi);

        var lu = (await _ctx.DepotAdherents.ObtenirAsync(id))!;
        Assert.True(lu.Enseignant);
        var fiche = _ctx.Db.Moniteurs.Single(m => m.Id == lu.MoniteurId);
        Assert.Equal(("Dubois-Martin", "0612"), (fiche.Nom, fiche.Telephone)); // Le nom affiché comme responsable suit.
    }

    [Fact]
    public async Task Un_adherent_desactive_ne_peut_pas_etre_enseignant()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois", Actif = false }, new RolesAdherent(true));

        Assert.False(r.Reussi);
        Assert.DoesNotContain(_ctx.Db.Adherents, a => a.Nom == "Dubois"); // Rien d'écrit.
        Assert.DoesNotContain(_ctx.Db.Moniteurs, m => m.Nom == "Dubois");
    }

    [Fact]
    public async Task La_licence_d_un_autre_enseignant_est_refusee()
    {
        Enseignant("Paul", "Durand", licence: "A-12");

        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois", Licence = "a-12" }, new RolesAdherent(true));

        Assert.False(r.Reussi);
        Assert.Equal("La licence A-12 est déjà celle de l'enseignant Paul Durand.", r.Message);
        Assert.DoesNotContain(_ctx.Db.Adherents, a => a.Nom == "Dubois");
    }

    [Fact]
    public async Task Desactiver_l_adherent_coupe_aussi_son_role_d_enseignant()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois" }, new RolesAdherent(true));
        var ficheId = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!.MoniteurId;

        var d = await _ctx.Adherents.ChangerActifAsync(r.Id!.Value, false);

        Assert.True(d.Reussi);
        Assert.Contains("ni être responsable d'une sortie", d.Message);
        Assert.False(_ctx.Db.Moniteurs.Single(m => m.Id == ficheId).Actif);
    }

    [Fact]
    public async Task Les_filtres_comptent_les_enseignants_et_les_inspecteurs_tiv()
    {
        await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois" }, new RolesAdherent(true, "MF1", InspecteurTiv: true));
        await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Hugo", Nom = "Petit" }, new RolesAdherent(true, "E1"));
        await AdherentAsync("Julie", "Martin");

        var liste = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Enseignants);
        Assert.Equal((3, 2, 1), (liste.Tous, liste.Enseignants, liste.InspecteursTiv));
        Assert.Equal(["Dubois", "Petit"], liste.Fiches.Select(f => f.Adherent.Nom).Order());
        Assert.Equal("Dubois", (await _ctx.Adherents.ListerAsync(null, FiltreAdherents.InspecteursTiv)).Fiches.Single().Adherent.Nom);
    }

    // ---------------------------------------------------------------- Inspecteur TIV sans être enseignant (migration 030)

    [Fact]
    public async Task Un_adherent_peut_etre_inspecteur_tiv_sans_etre_enseignant()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Marc", Nom = "Robert" }, new RolesAdherent(false, InspecteurTiv: true));

        Assert.True(r.Reussi, r.Message);
        var marc = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        Assert.Equal((false, true), (marc.Enseignant, marc.InspecteurTiv));
        Assert.DoesNotContain(await _ctx.Moniteurs.ListerActifsAsync(GenreResponsable.Moniteur), m => m.Id == marc.MoniteurId); // Pas responsable.
        Assert.Contains(await _ctx.Moniteurs.ListerInspecteursTivAsync(), m => m.Id == marc.MoniteurId);
        Assert.Contains(await _ctx.Revisions.InspecteursTivAsync(), m => m.Id == marc.MoniteurId);

        // Il peut enregistrer la TIV d'un bloc.
        var bloc = _ctx.Equipement("BLOC-031");
        var tiv = await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, _ctx.Horloge.AujourdHui, marc.MoniteurId);
        Assert.True(tiv.Reussi, tiv.Message);

        var liste = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.InspecteursTiv);
        Assert.Equal((0, 1), (liste.Enseignants, liste.InspecteursTiv));
    }

    [Fact]
    public async Task Couper_enseignant_garde_inspecteur_tiv()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois" }, new RolesAdherent(true, "E2", InspecteurTiv: true));
        var id = r.Id!.Value;

        Assert.True((await _ctx.Adherents.EnregistrerAsync(new Adherent { Id = id, Prenom = "Léa", Nom = "Dubois", Actif = true }, new RolesAdherent(false, InspecteurTiv: true))).Reussi);

        var lea = (await _ctx.DepotAdherents.ObtenirAsync(id))!;
        Assert.Equal((false, true, "E2"), (lea.Enseignant, lea.InspecteurTiv, lea.NiveauEncadrement));
        Assert.Contains("n'est plus enseignant", _ctx.Db.Journal.Last().Details);
    }

    [Fact]
    public async Task Desactiver_l_adherent_retire_aussi_l_inspecteur_tiv()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Marc", Nom = "Robert" }, new RolesAdherent(false, InspecteurTiv: true));
        var ficheId = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!.MoniteurId;

        Assert.True((await _ctx.Adherents.ChangerActifAsync(r.Id!.Value, false)).Reussi);

        Assert.DoesNotContain(await _ctx.Moniteurs.ListerInspecteursTivAsync(), m => m.Id == ficheId);
        Assert.Contains("n'est plus inspecteur TIV", _ctx.Db.Journal.Last().Details);
    }

    [Fact]
    public async Task Un_adherent_desactive_ne_peut_pas_etre_inspecteur_tiv()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Marc", Nom = "Robert", Actif = false }, new RolesAdherent(false, InspecteurTiv: true));
        Assert.False(r.Reussi);
        Assert.DoesNotContain(_ctx.Db.Moniteurs, m => m.Nom == "Robert");
    }

    // ---------------------------------------------------------------- Niveau obtenu (migration 031)

    [Theory]
    [InlineData("Niveau 1", "Niveau 1")]
    [InlineData("n2", "Niveau 2")]
    [InlineData(" niv 3 ", "Niveau 3")]
    [InlineData("PA40", "PA40")]
    [InlineData("  ", null)]
    public void Le_niveau_obtenu_est_reconnu(string saisie, string? attendu) =>
        Assert.Equal(attendu, ServiceAdherents.NiveauObtenuReconnu(saisie));

    [Fact]
    public async Task Niveau_obtenu_et_formation_en_cours_vont_ensemble()
    {
        var classes = _ctx.AjouterClasses("Niveau 2");
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Julie", Nom = "Martin", NiveauObtenu = "N1", ClasseId = classes[0].Id });

        Assert.True(r.Reussi, r.Message);
        var julie = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        Assert.Equal(("Niveau 1", "Niveau 2"), (julie.NiveauObtenu, julie.Niveau));
        Assert.False((await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Paul", Nom = "Long", NiveauObtenu = new string('x', 31) })).Reussi);
    }

    [Fact]
    public async Task L_import_lit_la_colonne_niveau_obtenu()
    {
        var r = await _ctx.Adherents.ImporterAsync(
        [
            ["Nom", "Prénom", "Brevet"],
            ["Martin", "Julie", "N1"],
            ["Petit", "Hugo", "Guide de palanquée"],
        ]);

        Assert.True(r.Reussi, r.Message);
        var adherents = _ctx.Db.Adherents.ToDictionary(a => a.Prenom);
        Assert.Equal("Niveau 1", adherents["Julie"].NiveauObtenu);
        Assert.Null(adherents["Julie"].Niveau); // « Brevet » n'est pas la formation en cours.

        // Hors de l'échelle Débutant… Niveau 4 : rangé dans les autres brevets.
        Assert.Equal((null, "Guide de palanquée"), (adherents["Hugo"].NiveauObtenu, adherents["Hugo"].AutresBrevets));

        // Case vide : niveau inchangé.
        Assert.True((await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Niveau obtenu"], ["Martin", "Julie", ""]])).Reussi);
        Assert.Equal("Niveau 1", _ctx.Db.Adherents.Single(a => a.Prenom == "Julie").NiveauObtenu);
        Assert.Contains(ServiceAdherents.ColonnesModele([]), c => c.EnTete == "Niveau obtenu");
    }

    // ---------------------------------------------------------------- Niveau et formation, autres brevets (migration 032)

    [Theory]
    [InlineData("aucun", 0)]
    [InlineData("Débutant", 0)]
    [InlineData("N0", 0)]
    [InlineData("n3", 3)]
    [InlineData("Niveau 4", 4)]
    [InlineData("PA40", null)]
    [InlineData(null, null)]
    public void Le_brevet_obtenu_se_place_sur_l_echelle(string? saisie, int? attendu) =>
        Assert.Equal(attendu, ServiceAdherents.IndiceBrevet(saisie));

    [Fact]
    public void La_formation_est_la_classe_du_brevet_suivant()
    {
        var classes = _ctx.AjouterClasses("Niveau 1", "Niveau 3", "Autonome");

        Assert.Equal("Niveau 1", ServiceAdherents.ClasseSuivante(0, classes)?.Nom);
        Assert.Equal("Niveau 3", ServiceAdherents.ClasseSuivante(2, classes)?.Nom);
        Assert.Null(ServiceAdherents.ClasseSuivante(1, classes)); // Pas de classe « Niveau 2 ».
        Assert.Null(ServiceAdherents.ClasseSuivante(4, classes)); // Niveau maximum.
        Assert.Null(ServiceAdherents.ClasseSuivante(null, classes));

        // Brevet non renseigné : déduit de la classe suivie.
        Assert.Equal((0, 2, (int?)null), (ServiceAdherents.BrevetDeduit("Niveau 1"), ServiceAdherents.BrevetDeduit("N3"), ServiceAdherents.BrevetDeduit("Autonome")));

        Assert.Equal("Niveau 2 · prépare le N3", ServiceAdherents.ResumeNiveau(2, "Niveau 3"));
        Assert.Equal("Débutant", ServiceAdherents.ResumeNiveau(0, null));
        Assert.Equal("Niveau 1 · suit Autonome", ServiceAdherents.ResumeNiveau(1, "Autonome"));
        Assert.Equal("Non renseigné", ServiceAdherents.ResumeNiveau(null, null));
    }

    [Fact]
    public async Task Les_autres_brevets_sont_nettoyes_et_nitrox_coche_la_certification()
    {
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent
        {
            Prenom = "Julie", Nom = "Martin", NiveauObtenu = "aucun", AutresBrevets = "pa 40; rifap;PA40; Nitrox ;Plongée sous glace",
        });

        Assert.True(r.Reussi, r.Message);
        var julie = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        Assert.Equal((ServiceAdherents.Debutant, "PA40; RIFAP; Plongée sous glace", true), (julie.NiveauObtenu, julie.AutresBrevets, julie.Nitrox));
        Assert.Equal(["PA40", "RIFAP", "Plongée sous glace"], ServiceAdherents.ListeBrevets(julie.AutresBrevets));

        // Un ancien brevet hors échelle (« PA40 » dans le niveau obtenu) rejoint les autres brevets.
        julie.NiveauObtenu = "Guide de palanquée";
        Assert.True((await _ctx.Adherents.EnregistrerAsync(julie)).Reussi);
        julie = (await _ctx.DepotAdherents.ObtenirAsync(julie.Id))!;
        Assert.Equal((null, "Guide de palanquée; PA40; RIFAP; Plongée sous glace"), (julie.NiveauObtenu, julie.AutresBrevets));

        // Trop long, trop nombreux : refusés.
        Assert.False((await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Paul", Nom = "Long", AutresBrevets = new string('x', 31) })).Reussi);
        Assert.False((await _ctx.Adherents.EnregistrerAsync(new Adherent
        {
            Prenom = "Paul", Nom = "Long", AutresBrevets = string.Join(";", Enumerable.Range(1, Adherent.NombreMaxBrevets + 1).Select(i => $"B{i}")),
        })).Reussi);
    }

    [Fact]
    public async Task L_import_ajoute_les_autres_brevets_sans_en_retirer()
    {
        Assert.True((await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Autres brevets"], ["Martin", "Julie", "PA40, RIFAP"]])).Reussi);
        Assert.Equal("PA40; RIFAP", _ctx.Db.Adherents.Single(a => a.Prenom == "Julie").AutresBrevets);

        Assert.True((await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Autres brevets"], ["Martin", "Julie", "rifap; Nitrox; Initiateur"]])).Reussi);
        var julie = _ctx.Db.Adherents.Single(a => a.Prenom == "Julie");
        Assert.Equal(("PA40; RIFAP; Initiateur", true), (julie.AutresBrevets, julie.Nitrox));
        Assert.Contains(ServiceAdherents.ColonnesModele([]), c => c.EnTete == "Autres brevets");
    }

    // ---------------------------------------------------------------- Départ : l'enseignant prend du matériel

    [Fact]
    public async Task Un_enseignant_prend_du_matériel_dans_une_sortie_nominative()
    {
        await _ctx.Adherents.RelierEnseignantsAsync(); // Pascal Martin, responsable, reçoit sa fiche d'adhérent.
        var pascal = (await AdherentDeAsync(_ctx.Pascal))!;
        Assert.Contains(await _ctx.Adherents.ListerPourDepartAsync("pascal"), a => a.Id == pascal.Id);

        var a12 = _ctx.Equipement("A12", categorie: "Détendeurs");
        var mer = _ctx.Db.TypesSortie.Single(t => t.Nom == "Mer");
        mer.Nominatif = true;
        var sortie = (await _ctx.Sorties.CreerAsync(new NouvelleSortie(_ctx.Horloge.AujourdHui, TypeSortie.Mer, "Marseille", _ctx.Pascal.Id, TypeId: mer.Id))).Id!.Value;
        await _ctx.Scan.ScannerDepartAsync(sortie, "A12");

        Assert.True((await _ctx.Sorties.AttribuerAdherentAsync(sortie, a12.Id, pascal.Id)).Reussi);
        Assert.Equal(pascal.Id, _ctx.Db.Affectations.Single().AdherentId);
    }

    [Fact]
    public async Task Le_caci_d_un_enseignant_suit_la_regle_des_adherents()
    {
        // CACI non coché : aucun contrôle ; coché et expiré : refusé, comme pour tout adhérent.
        var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois" }, new RolesAdherent(true));
        var lea = (await _ctx.DepotAdherents.ObtenirAsync(r.Id!.Value))!;
        Assert.Null(ServiceAdherents.RefusPour(lea, false, "A12", _ctx.Horloge.AujourdHui));

        lea.Caci = true;
        lea.CaciExpiration = _ctx.Horloge.AujourdHui.AddDays(-1);
        Assert.NotNull(ServiceAdherents.RefusPour(lea, false, "A12", _ctx.Horloge.AujourdHui));
    }
}
