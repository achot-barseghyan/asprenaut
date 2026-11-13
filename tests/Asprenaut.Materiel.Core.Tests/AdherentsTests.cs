using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Sorties nominatives et adhérents (règle 30).</summary>
public class AdherentsTests
{
    private readonly Contexte _ctx = new();

    private TypeDeSortie Type(string nom) => _ctx.Db.TypesSortie.Single(t => t.Nom == nom);

    private async Task<Adherent> AdherentAsync(string prenom, string nom, string? licence = null, string? niveau = null)
    {
        var a = new Adherent { Prenom = prenom, Nom = nom, Licence = licence, Niveau = niveau };
        var r = await _ctx.Adherents.EnregistrerAsync(a);
        Assert.True(r.Reussi, r.Message);
        return _ctx.Db.Adherents.Single(x => x.Id == r.Id);
    }

    /// <summary>Sortie en préparation d'un type (Mer rendu nominatif pour ces tests).</summary>
    private async Task<long> SortieAsync(string type = "Mer", bool nominatif = true)
    {
        Type(type).Nominatif = nominatif;
        var r = await _ctx.Sorties.CreerAsync(new NouvelleSortie(_ctx.Horloge.AujourdHui, TypeSortie.Autre, $"Sortie {type}", _ctx.Pascal.Id, TypeId: Type(type).Id));
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    private Task<ResultatOperation> AttribuerAsync(long sortie, Equipement e, Adherent a) =>
        _ctx.Sorties.AttribuerAdherentAsync(sortie, e.Id, a.Id);

    [Fact]
    public async Task Une_sortie_non_nominative_accepte_un_scan_sans_adherent()
    {
        _ctx.Equipement("A01", categorie: "Détendeurs");
        _ctx.Equipement("A02", categorie: "Détendeurs");
        var sortie = await SortieAsync("Piscine", nominatif: false);

        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(sortie, "A01")).Niveau);
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(sortie, "A02")).Niveau);
        Assert.True((await _ctx.Sorties.ValiderDepartAsync(sortie)).Reussi);
        Assert.False(_ctx.Db.Sorties.Single(s => s.Id == sortie).Nominatif);
    }

    [Fact]
    public async Task Une_sortie_nominative_refuse_un_nouveau_scan_tant_que_le_precedent_n_a_pas_d_adherent()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        _ctx.Equipement("BLOC-031");
        var julie = await AdherentAsync("Julie", "Martin");
        var sortie = await SortieAsync();
        Assert.True(_ctx.Db.Sorties.Single(s => s.Id == sortie).Nominatif);

        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(sortie, "A01")).Niveau);
        var refus = await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");
        Assert.Equal(NiveauResultat.Erreur, refus.Niveau);
        Assert.Contains("A01", refus.Message);
        Assert.Single(_ctx.Db.Affectations); // Le bloc n'est pas parti.

        Assert.True((await AttribuerAsync(sortie, a01, julie)).Reussi);
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031")).Niveau);
    }

    [Fact]
    public async Task Une_sortie_nominative_refuse_la_validation_si_un_equipement_n_a_pas_d_adherent()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        var julie = await AdherentAsync("Julie", "Martin");
        var sortie = await SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "A01");

        var refus = await _ctx.Sorties.ValiderDepartAsync(sortie);
        Assert.False(refus.Reussi);
        Assert.Equal("Attribuez A01 à un adhérent avant de valider le départ.", refus.Message);
        Assert.Equal("A01", ServiceSortie.AAttribuer(true, await _ctx.Sorties.ListerMaterielAsync(sortie))!.Code);

        await AttribuerAsync(sortie, a01, julie);
        Assert.True((await _ctx.Sorties.ValiderDepartAsync(sortie)).Reussi);
        Assert.Equal("Julie Martin", (await _ctx.Sorties.ListerMaterielAsync(sortie)).Single().AdherentNom);
    }

    [Fact]
    public async Task Un_adherent_inactif_n_est_plus_propose_au_depart_mais_reste_dans_l_historique()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        var a02 = _ctx.Equipement("A02", categorie: "Détendeurs");
        var julie = await AdherentAsync("Julie", "Martin");
        await AdherentAsync("Thomas", "Bernard");
        var sortie = await SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "A01");
        await AttribuerAsync(sortie, a01, julie);

        julie.Actif = false;
        Assert.True((await _ctx.Adherents.EnregistrerAsync(julie)).Reussi);

        Assert.Equal(["Thomas Bernard"], (await _ctx.Adherents.ListerPourDepartAsync()).Select(a => a.NomComplet));
        await _ctx.Scan.ScannerDepartAsync(sortie, "A02");
        Assert.False((await AttribuerAsync(sortie, a02, julie)).Reussi);

        // Historique : la ligne garde son nom, sa fiche garde le matériel qu'elle a encore.
        Assert.Equal("Julie Martin", (await _ctx.Sorties.ListerMaterielAsync(sortie)).Single(l => l.Code == "A01").AdherentNom);
        var fiche = (await _ctx.Adherents.ObtenirFicheAsync(julie.Id))!;
        Assert.Equal(["A01"], fiche.Dehors.Select(p => p.Code));
        Assert.Contains((await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Tous)).Fiches, f => f.Adherent.Id == julie.Id); // Inactive, mais du matériel dehors.
    }

    [Fact]
    public async Task Changer_le_reglage_du_type_ne_touche_pas_les_sorties_deja_creees()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        var julie = await AdherentAsync("Julie", "Martin");
        var nominative = await SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(nominative, "A01");
        await AttribuerAsync(nominative, a01, julie);
        Assert.True((await _ctx.Sorties.ValiderDepartAsync(nominative)).Reussi);

        Assert.True((await _ctx.TypesSortie.ChangerNominatifAsync(Type("Mer").Id, false)).Reussi);

        Assert.True(_ctx.Db.Sorties.Single(s => s.Id == nominative).Nominatif);
        var apres = await SortieAsync(nominatif: false);
        Assert.False(_ctx.Db.Sorties.Single(s => s.Id == apres).Nominatif);

        // Et l'inverse : une sortie validée sans nom ne se met pas à demander des noms.
        _ctx.Equipement("A02", categorie: "Détendeurs");
        var piscine = await SortieAsync("Piscine", nominatif: false);
        await _ctx.Scan.ScannerDepartAsync(piscine, "A02");
        await _ctx.Sorties.ValiderDepartAsync(piscine);
        Assert.True((await _ctx.TypesSortie.ChangerNominatifAsync(Type("Piscine").Id, true)).Reussi);
        _ctx.Equipement("A03", categorie: "Détendeurs");
        Assert.Equal(NiveauResultat.Succes, (await _ctx.Scan.ScannerDepartAsync(piscine, "A03")).Niveau);
    }

    [Fact]
    public async Task Changer_le_reglage_du_type_demande_le_mode_gestion()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.TypesSortie.ChangerNominatifAsync(Type("Piscine").Id, true)).Reussi);
        Assert.False(Type("Piscine").Nominatif);
    }

    [Fact]
    public async Task Un_equipement_perdu_sur_une_sortie_nominative_garde_l_adherent_dans_l_historique()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        var julie = await AdherentAsync("Julie", "Martin");
        var sortie = await SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "A01");
        await AttribuerAsync(sortie, a01, julie);
        await _ctx.Sorties.ValiderDepartAsync(sortie);

        Assert.True((await _ctx.Sorties.CloturerAsync(sortie)).Reussi);

        var entree = _ctx.Db.Journal.Single(j => j.Action == ActionsJournal.ManquantDeclare);
        Assert.Equal((a01.Id, julie.Id), (entree.EquipementId!.Value, entree.AdherentId!.Value));
        var evenement = (await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout)).First(e => e.Type == TypeEvenement.Manquant);
        Assert.Contains("pris par Julie Martin", evenement.Detail);
        var fiche = (await _ctx.Adherents.ObtenirFicheAsync(julie.Id))!;
        Assert.Equal(["A01"], fiche.NonRendus.Select(p => p.Code));
        Assert.Equal("1 non rendu", fiche.Sorties.Single().Etat);
        var liste = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.NonRendu);
        Assert.Equal((1, 0, 1), (liste.Tous, liste.MaterielDehors, liste.NonRendu));
    }

    [Fact]
    public async Task L_import_met_a_jour_par_licence_sans_creer_de_doublon()
    {
        var julie = await AdherentAsync("Julie", "Martin", licence: "A-12-345678", niveau: "Niveau 1");
        julie.Actif = false;
        await _ctx.Adherents.EnregistrerAsync(julie);
        IReadOnlyList<IReadOnlyList<string>> tableau =
        [
            ["Liste des adhérents 2026-2027"],
            ["NOM", "Prénom", "N° licence", "Niveau", "Tél."],
            ["Martin", "Julie", "a-12-345678", "Niveau 2", "06 11 22 33 44"],
            ["Dubois", "Léa", "A-12-345680", "Niveau 1", ""],
            ["", "", "", "", ""],
            ["Sans prénom", "", "A-12-999999", "", ""],
        ];

        var r = await _ctx.Adherents.ImporterAsync(tableau);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 1 adhérent ajouté, 1 mis à jour, 1 ligne ignorée (ligne 6).", r.Message);
        Assert.Equal(2, _ctx.Db.Adherents.Count);
        var maj = _ctx.Db.Adherents.Single(a => a.Licence == "A-12-345678");
        Assert.Equal((julie.Id, "Niveau 2", "06 11 22 33 44", true), (maj.Id, maj.Niveau, maj.Telephone, maj.Actif));

        // Réimporter le même fichier ne crée rien de plus.
        Assert.Equal("Import terminé : 0 adhérent ajouté, 2 mis à jour, 1 ligne ignorée (ligne 6).", (await _ctx.Adherents.ImporterAsync(tableau)).Message);
        Assert.Equal(2, _ctx.Db.Adherents.Count);
    }

    [Fact]
    public async Task Sans_en_tete_nom_et_prenom_l_import_ne_fait_rien()
    {
        var r = await _ctx.Adherents.ImporterAsync([["Code", "Libellé"], ["A01", "Détendeur"]]);
        Assert.False(r.Reussi);
        Assert.Empty(_ctx.Db.Adherents);
    }

    [Fact]
    public async Task Ajouter_un_adherent_demande_nom_prenom_et_une_licence_libre_et_le_code_gestionnaire()
    {
        await AdherentAsync("Julie", "Martin", licence: "a-1");
        Assert.Equal("Indiquez le nom et le prénom.", (await _ctx.Adherents.EnregistrerAsync(new Adherent { Nom = "Martin" })).Message);
        Assert.Equal("La licence A-1 est déjà celle de Julie Martin.",
            (await _ctx.Adherents.EnregistrerAsync(new Adherent { Nom = "Bernard", Prenom = "Thomas", Licence = " A-1 " })).Message);

        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        Assert.False((await _ctx.Adherents.EnregistrerAsync(new Adherent { Nom = "Bernard", Prenom = "Thomas" })).Reussi);
        Assert.Single(_ctx.Db.Adherents);
    }

    [Fact]
    public async Task Les_raccourcis_montrent_les_trois_derniers_adherents_le_plus_recent_d_abord()
    {
        var sortie = await SortieAsync();
        var julie = await AdherentAsync("Julie", "Martin");
        var thomas = await AdherentAsync("Thomas", "Bernard");
        string[] codes = ["BLOC-031", "S01", "A01", "BLOC-032"];
        Adherent[] qui = [julie, julie, thomas, julie];
        for (var i = 0; i < codes.Length; i++)
        {
            var e = _ctx.Equipement(codes[i]);
            _ctx.Horloge.MaintenantUtc = _ctx.Horloge.MaintenantUtc.AddMinutes(1);
            await _ctx.Scan.ScannerDepartAsync(sortie, codes[i]);
            await AttribuerAsync(sortie, e, qui[i]);
        }

        var recents = ServiceSortie.AdherentsRecents(await _ctx.Sorties.ListerMaterielAsync(sortie));
        Assert.Equal([("Julie Martin", 3), ("Thomas Bernard", 1)], recents.Select(r => (r.Nom, r.NbEquipements)));
    }

    [Fact]
    public async Task Apres_la_validation_un_scan_en_attente_peut_etre_annule()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        _ctx.Equipement("A02", categorie: "Détendeurs");
        var julie = await AdherentAsync("Julie", "Martin");
        var sortie = await SortieAsync();
        await _ctx.Scan.ScannerDepartAsync(sortie, "A01");
        await AttribuerAsync(sortie, a01, julie);
        await _ctx.Sorties.ValiderDepartAsync(sortie);
        await _ctx.Scan.ScannerDepartAsync(sortie, "A02"); // Ajout après le départ : attend son adhérent.

        Assert.False((await _ctx.Scan.RetirerDuDepartAsync(sortie, a01.Id)).Reussi); // Attribué : départ validé, on ne retire plus.
        Assert.True((await _ctx.Scan.RetirerDuDepartAsync(sortie, _ctx.Db.Equipements.Single(e => e.Code == "A02").Id)).Reussi);
        Assert.Null(ServiceSortie.AAttribuer(true, await _ctx.Sorties.ListerMaterielAsync(sortie)));
    }

    [Fact]
    public void La_recherche_ignore_majuscules_et_accents()
    {
        Adherent[] liste = [new() { Prenom = "Léa", Nom = "Dubois" }, new() { Prenom = "Julie", Nom = "Martin" }];
        Assert.Equal(["Léa Dubois"], ServiceAdherents.Filtrer(liste, "LEA").Select(a => a.NomComplet));
        Assert.Equal(["Julie Martin"], ServiceAdherents.Filtrer(liste, "julie mar").Select(a => a.NomComplet));
        Assert.Equal(2, ServiceAdherents.Filtrer(liste, "  ").Count);
        Assert.Equal("JM", liste[1].Initiales);
    }

    // Niveaux : une liste (Paramètres), le formulaire n'accepte qu'un niveau de la liste, l'import ramène les abréviations.
    [Fact]
    public async Task Le_niveau_est_pris_dans_la_liste()
    {
        Assert.Equal("Niveau 2", ServiceAdherents.NiveauReconnu(" n2 ", _ctx.Adherents.Niveaux));
        Assert.Equal("Niveau 2", ServiceAdherents.NiveauReconnu("NIVEAU 2", _ctx.Adherents.Niveaux));
        Assert.Equal("PA40", ServiceAdherents.NiveauReconnu("pa 40", _ctx.Adherents.Niveaux));
        Assert.Equal("Débutant", ServiceAdherents.NiveauReconnu("debutant", _ctx.Adherents.Niveaux));
        Assert.Null(ServiceAdherents.NiveauReconnu("N5", _ctx.Adherents.Niveaux));

        var hugo = await AdherentAsync("Hugo", "Moreau", niveau: "niv 3");
        Assert.Equal("Niveau 3", hugo.Niveau);
        var refus = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = "Léa", Nom = "Dubois", Niveau = "Plongeur confirmé" });
        Assert.Equal("Choisissez le niveau dans la liste.", refus.Message);

        // Un ancien niveau hors liste (saisi avant) est gardé tant qu'on ne le change pas.
        hugo.Niveau = "Niveau 3 ancien";
        _ctx.Db.Adherents.Single(a => a.Id == hugo.Id).Niveau = "Niveau 3 ancien";
        Assert.True((await _ctx.Adherents.EnregistrerAsync(new Adherent { Id = hugo.Id, Prenom = "Hugo", Nom = "Moreau", Niveau = "Niveau 3 ancien", Actif = true })).Reussi);
    }

    [Fact]
    public async Task L_import_ramene_les_niveaux_a_la_liste_et_signale_les_inconnus()
    {
        var r = await _ctx.Adherents.ImporterAsync([["Nom", "Prénom", "Niveau"], ["Martin", "Julie", "N2"], ["Petit", "Hugo", "super plongeur"], ["Dubois", "Léa", ""]]);

        Assert.Equal("Import terminé : 3 adhérents ajoutés, 0 mis à jour, 1 niveau inconnu laissé vide (ligne 3).", r.Message);
        Assert.Equal("Niveau 2", _ctx.Db.Adherents.Single(a => a.Nom == "Martin").Niveau);
        Assert.Null(_ctx.Db.Adherents.Single(a => a.Nom == "Petit").Niveau);
    }

    [Fact]
    public void La_liste_des_niveaux_se_regle_dans_les_parametres()
    {
        Assert.True(_ctx.ServiceReglages.ChangerNiveauxAdherents(["Débutant", " Niveau 1 ", "", "Bronze"]).Reussi);
        Assert.Equal(["Débutant", "Niveau 1", "Bronze"], _ctx.Adherents.Niveaux);
        Assert.Equal("« Niveau 1 » est en double.", _ctx.ServiceReglages.ChangerNiveauxAdherents(["Niveau 1", "NIVEAU 1"]).Message);
        Assert.Equal("Gardez au moins un niveau.", _ctx.ServiceReglages.ChangerNiveauxAdherents([" "]).Message);
        Assert.Equal("Bronze", ServiceAdherents.ColonnesModele(_ctx.Adherents.Niveaux)[2].Choix![2]); // Liste du modèle Excel.
    }}
