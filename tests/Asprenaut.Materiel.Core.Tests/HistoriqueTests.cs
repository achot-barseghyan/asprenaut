using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

public class HistoriqueTests
{
    private readonly Contexte _ctx = new();

    // Règle : chaque scan est relié dans le journal à son équipement et à sa sortie.
    [Fact]
    public async Task Le_journal_note_l_equipement_et_la_sortie_du_scan()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieEnCoursAsync("Cours en piscine", "BLOC-031");
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");

        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ScanDepart && j.EquipementId == bloc.Id && j.SortieId == sortie);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.ScanRetour && j.EquipementId == bloc.Id && j.SortieId == sortie);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.SortieCreee && j.SortieId == sortie && j.EquipementId == null);
    }

    [Fact]
    public async Task Un_depart_affiche_l_equipement_la_sortie_et_le_moniteur()
    {
        _ctx.Equipement("BLOC-031");
        var sortie = await _ctx.SortieAsync("Cours en piscine");
        await _ctx.Scan.ScannerDepartAsync(sortie, "BLOC-031");

        var depart = (await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout)).First(e => e.Type == TypeEvenement.Depart);

        Assert.Equal("Départ : Bloc 12 L", depart.Titre);
        Assert.Equal("Cours en piscine du 06/11 · Pascal Martin", depart.Detail);
        Assert.Equal("BLOC-031", depart.Code);
    }

    // Règle : les scans d'une même sortie à moins de 15 minutes d'écart forment un seul événement.
    [Fact]
    public async Task Les_scans_rapproches_d_une_sortie_sont_regroupes()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        var sortie = await _ctx.SortieEnCoursAsync("Fosse", "BLOC-031", "BLOC-032", "DET-001");
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddMinutes(5);
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-032");
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddMinutes(40); // Trop tard : nouvel événement.
        await _ctx.Scan.ScannerRetourAsync(sortie, "DET-001");

        var retours = (await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout)).Where(e => e.Type == TypeEvenement.Retour).ToList();

        Assert.Equal(2, retours.Count);
        Assert.Equal("Retour : Détendeur", retours[0].Titre);
        Assert.Equal("Retour : 2 équipements", retours[1].Titre);
        Assert.Equal(2, retours[1].Nombre);
        Assert.Null(retours[1].Code);
        Assert.Equal("Fosse du 06/11 · Pascal Martin · Bloc 12 L", retours[1].Detail);

        var detailles = await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout, regrouper: false);
        Assert.Equal(3, detailles.Count(e => e.Type == TypeEvenement.Retour));
    }

    [Fact]
    public async Task La_cloture_et_les_manquants_sont_decrits()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-032");
        var sortie = await _ctx.SortieEnCoursAsync("Sortie mer", "BLOC-031", "BLOC-032");
        await _ctx.Scan.ScannerRetourAsync(sortie, "BLOC-031");
        await _ctx.Sorties.CloturerAsync(sortie);

        var evenements = await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout);

        var cloture = evenements.Single(e => e.Titre == "Sortie clôturée : Sortie mer");
        Assert.Equal(TypeEvenement.Sortie, cloture.Type);
        Assert.Equal("2 équipements · 1 manquant · Pascal Martin", cloture.Detail);
        var manquant = evenements.Single(e => e.Type == TypeEvenement.Manquant);
        Assert.Equal("Manquant : Bloc 12 L", manquant.Titre);
        Assert.Equal("BLOC-032", manquant.Code);
        Assert.Equal("Départ validé : Sortie mer", evenements.Single(e => e.Titre.StartsWith("Départ validé", StringComparison.Ordinal)).Titre);
    }

    [Fact]
    public async Task La_periode_limite_les_evenements()
    {
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddDays(-20);
        await _ctx.SortieAsync("Ancienne sortie");
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant;
        await _ctx.SortieAsync("Sortie récente");

        var semaine = await _ctx.Historique.ListerAsync(PeriodeHistorique.SeptJours);
        var mois = await _ctx.Historique.ListerAsync(PeriodeHistorique.TrenteJours);

        Assert.Equal(["Sortie créée : Sortie récente"], semaine.Select(e => e.Titre));
        Assert.Equal(2, mois.Count);
        Assert.Equal("Saison 2026-2027", _ctx.Historique.LibellePeriode(PeriodeHistorique.Saison));
        Assert.Equal(new DateOnly(2026, 9, 1), _ctx.Historique.PremierJour(PeriodeHistorique.Saison));
    }

    [Fact]
    public void Une_entree_ancienne_sans_lien_garde_son_texte()
    {
        var e = ServiceHistorique.Decrire(new LigneJournal
        {
            Id = 1,
            Horodatage = Contexte.Maintenant,
            Action = ActionsJournal.ScanRetour,
            Details = "BLOC-031 rendu (sortie 3)",
        });

        Assert.Equal(TypeEvenement.Retour, e.Type);
        Assert.Equal("Retour", e.Titre);
        Assert.Equal("BLOC-031 rendu (sortie 3)", e.Detail);
        Assert.Null(e.Code);
    }

    [Fact]
    public async Task Controle_et_atelier_sont_des_revisions()
    {
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Revision, ResultatControle.Conforme, new DateOnly(2026, 11, 5), null);
        await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "Aqua Service", "Fuite", null);

        var revisions = (await _ctx.Historique.ListerAsync(PeriodeHistorique.Tout)).Where(e => e.Type == TypeEvenement.Revision).ToList();

        Assert.Equal(2, revisions.Count);
        Assert.Equal("Envoi en atelier : Détendeur", revisions[0].Titre);
        Assert.Equal("Envoyé chez Aqua Service", revisions[0].Detail);
        Assert.Equal("Contrôle : Détendeur", revisions[1].Titre);
        Assert.StartsWith("Révision fabricant du 05/11/2026", revisions[1].Detail);
    }

    // Règle : la vie d'un équipement reprend ses sorties (départ, retour, manquant, retrouvé) et ses contrôles.
    [Fact]
    public async Task La_vie_d_un_equipement_retrace_son_parcours()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: new DateOnly(2026, 11, 20));
        _ctx.Equipement("BLOC-033");
        _ctx.Pascal.InspecteurTiv = true;
        var premiere = await _ctx.SortieEnCoursAsync("Piscine", "BLOC-031");
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddHours(2);
        await _ctx.Sorties.CloturerAsync(premiere);
        _ctx.Horloge.MaintenantUtc = Contexte.Maintenant.AddDays(1);
        var seconde = await _ctx.SortieEnCoursAsync("Fosse", "BLOC-033");
        await _ctx.Scan.ScannerRetourAsync(seconde, "BLOC-031"); // Manquant retrouvé depuis une autre sortie.
        await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, new DateOnly(2026, 11, 7), _ctx.Pascal.Id);

        var vie = await _ctx.Historique.ObtenirVieAsync("asp:bloc-031");

        Assert.NotNull(vie);
        Assert.Equal(1, vie.SortiesSaison);
        Assert.Equal(StatutInventaire.Disponible, vie.Statut);
        Assert.Equal("Au local", vie.LibelleStatut);
        Assert.Equal(
            ["Retrouvé et rendu", "TIV annuelle conforme", "Noté manquant", "Parti", "Ajouté à l'inventaire"],
            vie.Etapes.Select(e => e.Titre));
        Assert.Equal("Pascal Martin", vie.Etapes[1].Detail);
        Assert.StartsWith("TIV ", vie.Echeance);
    }

    [Fact]
    public async Task La_fiche_de_suivi_se_lit_aussi_par_identifiant()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        _ctx.Equipement("BLOC-033");

        var vie = await _ctx.Historique.ObtenirVieAsync(bloc.Id);

        Assert.NotNull(vie);
        Assert.Equal(bloc.Id, vie.Equipement.Id);
        Assert.Equal("BLOC-031", vie.Equipement.Code);
        Assert.Null(await _ctx.Historique.ObtenirVieAsync(999_999));
    }

    [Fact]
    public async Task La_vie_indique_ou_est_l_equipement()
    {
        _ctx.Equipement("BLOC-031");
        _ctx.Equipement("DET-001", categorie: "Détendeurs");
        await _ctx.SortieEnCoursAsync("Cours en piscine", "BLOC-031");
        await _ctx.Revisions.EnvoyerEnAtelierAsync("DET-001", "Aqua Service", "", null);

        var sorti = await _ctx.Historique.ObtenirVieAsync("BLOC-031");
        var atelier = await _ctx.Historique.ObtenirVieAsync("DET-001");

        Assert.Equal(("Sorti", "Cours en piscine"), (sorti!.LibelleStatut, sorti.PrecisionStatut));
        Assert.Equal(StatutInventaire.Dehors, sorti.Statut);
        Assert.Equal(("Atelier", "Aqua Service"), (atelier!.LibelleStatut, atelier.PrecisionStatut));
        Assert.Null(await _ctx.Historique.ObtenirVieAsync("INCONNU-999"));
        Assert.Null(await _ctx.Historique.ObtenirVieAsync("  "));
    }

    // Règle : la fiche de suivi détaille les composants d'un détendeur et reprend les dates réelles de l'équipement.
    [Fact]
    public async Task La_fiche_detaillee_montre_les_composants_du_detendeur()
    {
        var det = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        det.CreeLe = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Local);
        det.Marque = "Scubapro";
        det.Modele = "MK2";
        det.NumeroSerie = "0614500681";
        det.MarqueDeuxiemeEtage = "Scubapro";
        det.ModeleDeuxiemeEtage = "R295";
        det.NumeroDeuxiemeEtage = "0614500682";
        det.Octopus = "R095";
        det.NumeroOctopus = "1641000963";
        det.Manometre = "Suunto";
        await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Revision, ResultatControle.Conforme, new DateOnly(2026, 11, 5), null);
        det.DateProchaineRevision = new DateOnly(2027, 3, 1);

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(det.Id);

        Assert.NotNull(fiche);
        Assert.Equal(["Premier étage", "Deuxième étage", "Octopus", "Manomètre"], fiche.Composants.Select(c => c.Role));
        Assert.Equal("Scubapro MK2", fiche.Composants[0].Modele);
        Assert.Equal("0614500681", fiche.Composants[0].NumeroSerie);
        Assert.Equal("Scubapro R295", fiche.Composants[1].Modele);
        Assert.Equal("R095", fiche.Composants[2].Modele);
        Assert.Equal("Suunto", fiche.Composants[3].Modele);
        Assert.Equal(
            [("Ajouté le", "06/11/2026"), ("Dernier contrôle", "05/11/2026"), ("Prochaine révision", "03/2027"), ("État", "En service")],
            fiche.Composants[0].Tuiles.Select(t => (t.Libelle, t.Valeur)));
    }

    [Fact]
    public async Task Un_detendeur_sans_sous_ensemble_n_a_qu_une_carte()
    {
        var det = _ctx.Equipement("DET-002", categorie: "Détendeurs");
        det.Marque = "Aqualung";
        det.Modele = "Calypso";

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(det.Id);

        Assert.NotNull(fiche);
        var composant = Assert.Single(fiche.Composants);
        Assert.Equal(("Premier étage", "Aqualung Calypso"), (composant.Role, composant.Modele));
    }

    [Fact]
    public async Task Un_autre_equipement_n_a_qu_une_carte_equipement_complet()
    {
        var bloc = _ctx.Equipement("BLOC-031", revision: new DateOnly(2027, 5, 1));
        bloc.Marque = "Aqualung";
        bloc.Modele = "Wave";
        bloc.NumeroSerie = "BB208115";

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(bloc.Id);

        Assert.NotNull(fiche);
        var composant = Assert.Single(fiche.Composants);
        Assert.Equal(("Équipement complet", "Aqualung Wave", "BB208115"), (composant.Role, composant.Modele, composant.NumeroSerie));
        Assert.Equal("05/2027", composant.Tuiles[2].Valeur);
    }

    // Règle : les interventions de la fiche mêlent contrôles et passages en atelier, du plus récent au plus ancien.
    [Fact]
    public async Task Les_interventions_melangent_controles_et_ateliers()
    {
        var bloc = _ctx.Equipement("BLOC-031");
        _ctx.Pascal.InspecteurTiv = true;
        await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Tiv, ResultatControle.Conforme, new DateOnly(2026, 11, 5), _ctx.Pascal.Id);
        await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Requalification, ResultatControle.ASurveiller, new DateOnly(2026, 10, 1), null,
            "Contrôle visuel", "Aqua Service");
        await _ctx.Revisions.EnvoyerEnAtelierAsync("BLOC-031", "Aqua Service", "Fuite", null);

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(bloc.Id);

        Assert.NotNull(fiche);
        Assert.Equal(3, fiche.Interventions.Count);
        Assert.Equal(new DateOnly(2026, 11, 6), fiche.Interventions[0].Date);
        Assert.Equal(("Fuite", "Aqua Service", TypeInterventionSuivie.Atelier, "En atelier"),
            (fiche.Interventions[0].Nature, fiche.Interventions[0].Intervenant, fiche.Interventions[0].Type, fiche.Interventions[0].Resultat));
        Assert.Equal(("TIV annuelle", "Pascal Martin", TypeInterventionSuivie.Controle, "Conforme"),
            (fiche.Interventions[1].Nature, fiche.Interventions[1].Intervenant, fiche.Interventions[1].Type, fiche.Interventions[1].Resultat));
        Assert.Equal(("Contrôle visuel", "Aqua Service", "À surveiller"),
            (fiche.Interventions[2].Nature, fiche.Interventions[2].Intervenant, fiche.Interventions[2].Resultat));
        Assert.All(fiche.Interventions, i => Assert.Equal("Équipement complet", i.Composant));
        Assert.Null(await _ctx.Historique.ObtenirFicheSuiviAsync(999_999));
    }

    // Règle : une intervention peut viser un composant du détendeur ; elle ne compte que pour lui, celle de l'équipement complet pour tous.
    [Fact]
    public async Task Une_intervention_sur_un_composant_reste_sur_ce_composant()
    {
        var det = _ctx.Equipement("A05", categorie: "Détendeurs");
        det.Libelle = "Détendeur Scubapro";
        det.Marque = "Scubapro";
        det.Modele = "MK2";
        det.ModeleDeuxiemeEtage = "R295";
        det.Octopus = "R095";
        await _ctx.Revisions.EnregistrerControleAsync(det.Id, TypeControle.Revision, ResultatControle.Conforme, new DateOnly(2026, 3, 17), null, "Révision", "Aquasub");
        var echeance = det.DateProchaineRevision;
        var r = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.ASurveiller,
            new DateOnly(2026, 11, 5), "Réparation", "Aquasub", Composant: "octopus", Notes: "  changement flexible MP "));
        Assert.True(r.Reussi, r.Message);

        // Un octopus réparé ne repousse pas (ni n'avance) la révision du détendeur.
        Assert.Equal(new DateOnly(2028, 3, 17), echeance);
        Assert.Equal(echeance, det.DateProchaineRevision);
        Assert.Contains("ne changent pas", r.Message);
        Assert.DoesNotContain(await _ctx.DepotRevisions.ListerDerniersControlesAsync(), d => d.DateControle == new DateOnly(2026, 11, 5));

        var fiche = await _ctx.Historique.ObtenirFicheSuiviAsync(det.Id);

        Assert.NotNull(fiche);
        Assert.True(fiche.ParComposant);
        Assert.Equal("Détendeur Scubapro · MK2 · R295 · R095", fiche.Titre);
        var derniere = fiche.Interventions[0];
        Assert.Equal(("Octopus", "Réparation : changement flexible MP", "À surveiller"), (derniere.Composant, derniere.Nature, derniere.Resultat));
        Assert.Equal(ServiceInventaire.ComposantComplet, fiche.Interventions[1].Composant);
        Assert.Equal(2, fiche.InterventionsDe("Octopus").Count);
        Assert.Equal([new DateOnly(2026, 3, 17)], fiche.InterventionsDe("Deuxième étage").Select(i => i.Date));
        Assert.Equal(2, fiche.InterventionsDe(null).Count);
        string Dernier(string role) => fiche.Composants.Single(c => c.Role == role).Tuiles.Single(t => t.Libelle == "Dernier contrôle").Valeur;
        Assert.Equal("05/11/2026", Dernier("Octopus"));
        Assert.Equal("17/03/2026", Dernier("Premier étage"));
        Assert.Equal(new DateOnly(2026, 3, 17), fiche.DerniereRevision); // Celle de l'équipement complet.
    }

    [Fact]
    public async Task Un_composant_absent_de_l_equipement_est_refuse()
    {
        var det = _ctx.Equipement("A06", categorie: "Détendeurs");
        var bloc = _ctx.Equipement("BLOC-031");

        var sansOctopus = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme,
            new DateOnly(2026, 11, 5), "Révision", "", Composant: "Octopus"));
        var surUnBloc = await _ctx.Revisions.EnregistrerControleAsync(bloc.Id, TypeControle.Requalification, ResultatControle.Conforme, new DateOnly(2026, 11, 5), null,
            "Requalification", "", "Premier étage", null);
        var complet = await _ctx.Revisions.EnregistrerInterventionAsync(new Intervention(det.Id, TypeControle.Revision, ResultatControle.Conforme,
            new DateOnly(2026, 11, 5), "Révision", "", Composant: ServiceInventaire.ComposantComplet));

        Assert.False(sansOctopus.Reussi);
        Assert.Contains("Octopus", sansOctopus.Message);
        Assert.False(surUnBloc.Reussi);
        Assert.True(complet.Reussi, complet.Message);
        Assert.Equal("", _ctx.Db.Controles.Single(c => c.EquipementId == det.Id).Composant);
        Assert.Equal(["Premier étage"], ServiceInventaire.ComposantsDe(det, "Détendeurs"));
        Assert.Empty(ServiceInventaire.ComposantsDe(bloc, "Blocs"));
    }

    [Fact]
    public void L_export_csv_echappe_les_points_virgules()
    {
        var csv = ServiceHistorique.ExporterCsv(
        [
            new EvenementHistorique(1, new DateTime(2026, 11, 6, 19, 38, 0), TypeEvenement.Depart, "Départ : Bloc", "Piscine; Pascal", "BLOC-031"),
        ]);

        var lignes = csv.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("Date;Heure;Type;Événement;Détail;Code", lignes[0]);
        Assert.Equal("06/11/2026;19:38;Départs;Départ : Bloc;\"Piscine; Pascal\";BLOC-031", lignes[1]);
    }

    [Fact]
    public void Les_jours_proches_sont_nommes()
    {
        var aujourdHui = new DateOnly(2026, 11, 9);

        Assert.Equal(("Aujourd'hui", "lundi 9 novembre"), ServiceHistorique.LibelleJour(aujourdHui, aujourdHui));
        Assert.Equal(("Hier", "dimanche 8 novembre"), ServiceHistorique.LibelleJour(aujourdHui.AddDays(-1), aujourdHui));
        Assert.Equal(("Vendredi 6 novembre", ""), ServiceHistorique.LibelleJour(new DateOnly(2026, 11, 6), aujourdHui));
        Assert.Equal(("Mardi 4 novembre 2025", ""), ServiceHistorique.LibelleJour(new DateOnly(2025, 11, 4), aujourdHui));
    }
}
