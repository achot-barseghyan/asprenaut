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
