using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Import de l'inventaire depuis Excel (règle 42).</summary>
public class ImportInventaireTests
{
    private readonly Contexte _ctx = new();

    private Equipement Par(string code) => _ctx.Db.Equipements.Single(e => e.Code == code);

    [Fact]
    public async Task Un_code_nouveau_est_cree_avec_sa_fiche_et_une_echeance_passee_est_acceptee()
    {
        var r = await _ctx.Inventaire.ImporterAsync([
            ["Inventaire du club"],
            ["Code", "Équipement", "Catégorie", "Marque", "N° série", "Étrier / DIN", "Prochaine révision / TIV", "Nitrox"],
            ["a24", "Détendeur adulte", "detendeur", "Aqualung", "123", "DIN", "", "oui"],
            ["BLOC-040", "Bloc 15 L", "Blocs", "", "", "", "01/03/2026", ""],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 2 équipements ajoutés, 0 mis à jour.", r.Message);
        var a24 = Par("A24");
        Assert.Equal(_ctx.Db.Categories.Single(c => c.Nom == "Détendeurs").Id, a24.CategorieId);
        Assert.Equal(("Aqualung", "123", MontageDetendeur.Din, true), (a24.Marque, a24.NumeroSerie, a24.Montage, a24.Nitrox));
        Assert.Equal(new DateOnly(2026, 3, 1), Par("BLOC-040").DateProchaineRevision);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.InventaireImporte);
    }

    [Fact]
    public async Task Un_code_connu_est_mis_a_jour_seulement_avec_les_cases_remplies()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        a01.Marque = "Scubapro";
        a01.Notes = "Embout neuf";

        var r = await _ctx.Inventaire.ImporterAsync([
            ["Code", "Équipement", "Catégorie", "Marque", "Modèle", "Notes"],
            ["A01", "", "", "", "MK25", ""],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 0 équipement ajouté, 1 mis à jour.", r.Message);
        Assert.Equal(("Détendeur", "Scubapro", "MK25", "Embout neuf"), (a01.Libelle, a01.Marque, a01.Modele, a01.Notes));

        var deNouveau = await _ctx.Inventaire.ImporterAsync([["Code", "Modèle"], ["A01", "MK25"]]);
        Assert.Equal("Import terminé : 0 équipement ajouté, 0 mis à jour, 1 inchangé.", deNouveau.Message);
    }

    [Fact]
    public async Task La_colonne_utilisations_reprend_le_compteur()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");

        var r = await _ctx.Inventaire.ImporterAsync([
            ["Code", "Équipement", "Catégorie", "Nombre d'utilisations"],
            ["A01", "", "", "64 / 80 utilisations"],
            ["S40", "Détendeur adulte", "Détendeurs", "12"],
            ["S41", "Détendeur adulte", "Détendeurs", "0"],
            ["S42", "Détendeur adulte", "Détendeurs", "beaucoup"],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 2 équipements ajoutés, 1 mis à jour, 2 compteurs d'utilisation repris, 1 ligne ignorée : "
            + "ligne 5 (utilisations « beaucoup » (un nombre de 0 à 10000))", r.Message.TrimEnd('.'));
        var compteurs = (await new EquipementRepositoryMemoire(_ctx.Db).ListerCompteursAsync(null)).ToDictionary(c => c.EquipementId, c => c.Utilisations);
        Assert.Equal((64, 12, 0), (compteurs[a01.Id], compteurs[Par("S40").Id], compteurs[Par("S41").Id]));
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.UtilisationsReprises && j.EquipementId == a01.Id);

        // Même nombre : rien n'est réécrit.
        var deNouveau = await _ctx.Inventaire.ImporterAsync([["Code", "Utilisations"], ["A01", "64"]]);
        Assert.Equal("Import terminé : 0 équipement ajouté, 0 mis à jour, 1 inchangé.", deNouveau.Message);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData(" 42,0 ", 42)]
    [InlineData("64 / 80 utilisations", 64)]
    [InlineData("1 utilisation", 1)]
    [InlineData("", null)]
    [InlineData("-3", null)]
    [InlineData("10001", null)]
    [InlineData("42,5", null)]
    public void Les_utilisations_se_lisent(string texte, int? attendu) =>
        Assert.Equal(attendu, ServiceInventaire.LireUtilisations(texte));

    [Fact]
    public async Task La_derniere_revision_fixe_la_prochaine_et_devient_un_controle()
    {
        var r = await _ctx.Inventaire.ImporterAsync([
            ["Code", "Équipement", "Catégorie", "Date de la dernière révision"],
            ["A05", "Détendeur adulte", "Détendeurs", "15/01/2025"],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal(new DateOnly(2027, 1, 15), Par("A05").DateProchaineRevision);
        Assert.Contains(_ctx.Db.Controles, c => c.EquipementId == Par("A05").Id && c.DateControle == new DateOnly(2025, 1, 15));
    }

    [Fact]
    public async Task Une_categorie_inconnue_est_creee()
    {
        var r = await _ctx.Inventaire.ImporterAsync([["Code", "Équipement", "Catégorie"], ["LAMP-01", "Lampe", "lampes"], ["LAMP-02", "Lampe", "Lampes"]]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 2 équipements ajoutés, 0 mis à jour, 1 catégorie créée (Lampes).", r.Message);
        Assert.Single(_ctx.Db.Categories, c => c.Nom == "Lampes");
    }

    [Fact]
    public async Task Les_lignes_en_erreur_sont_ignorees_et_signalees()
    {
        var r = await _ctx.Inventaire.ImporterAsync([
            ["Code", "Équipement", "Catégorie", "Prochaine révision / TIV", "D.S"],
            ["", "Bloc", "Blocs", "", ""],
            ["BLOC-001", "", "Blocs", "", ""],
            ["BLOC-002", "Bloc", "Blocs", "bientôt", ""],
            ["A01", "Détendeur", "Détendeurs", "", "peut-être"],
            ["BLOC-003", "Bloc", "Blocs", "", ""],
            ["BLOC-003", "Bloc", "Blocs", "", ""],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 1 équipement ajouté, 0 mis à jour, 5 lignes ignorées : ligne 2 (code manquant) ; "
            + "ligne 3 (BLOC-001 : nom manquant) ; ligne 4 (date de la prochaine révision illisible) ; "
            + "ligne 5 (D.S : « peut-être » (oui ou non)) ; ligne 7 (BLOC-003 déjà plus haut dans le fichier).", r.Message);
        Assert.Single(_ctx.Db.Equipements);
    }

    [Fact]
    public async Task Sans_colonne_code_rien_n_est_importe()
    {
        var r = await _ctx.Inventaire.ImporterAsync([["Nom", "Prénom"], ["Martin", "Julie"]]);

        Assert.False(r.Reussi);
        Assert.Empty(_ctx.Db.Equipements);
    }

    [Fact]
    public async Task Importer_demande_le_mode_gestion()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        Assert.False((await _ctx.Inventaire.ImporterAsync([["Code", "Équipement", "Catégorie"], ["A01", "Détendeur", "Détendeurs"]])).Reussi);
        Assert.Empty(_ctx.Db.Equipements);
    }

    [Fact]
    public async Task Le_modele_a_remplir_a_les_colonnes_reconnues_par_l_import()
    {
        var entete = ServiceInventaire.ColonnesModeleInventaire(["Blocs", "Détendeurs"]).Select(c => c.EnTete).ToList();
        var ligne = new List<string>(entete.Select(_ => "")) { [0] = "A30", [1] = "Détendeur adulte", [2] = "Détendeurs", [11] = "Étrier", [15] = "Non" };

        var r = await _ctx.Inventaire.ImporterAsync([entete, ligne]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal((MontageDetendeur.Etrier, (bool?)false), (Par("A30").Montage, Par("A30").DirectSystem));
        Assert.Equal("Inventaire à remplir - Asprenaut.xlsx", ServiceInventaire.NomFichierModeleInventaire(" Asprenaut "));
    }

    [Fact]
    public async Task La_colonne_etat_pose_l_etat_de_l_equipement()
    {
        var a01 = _ctx.Equipement("A01", categorie: "Détendeurs");
        var a02 = _ctx.Equipement("A02", categorie: "Détendeurs");
        await _ctx.SortieEnCoursAsync("Fosse", "A02");

        var r = await _ctx.Inventaire.ImporterAsync([
            ["Code", "Équipement", "Catégorie", "État"],
            ["A01", "", "", "HS"],
            ["A02", "", "", "Réformé"],
            ["A30", "Détendeur adulte", "Détendeurs", "Réformé"],
            ["A31", "Détendeur adulte", "Détendeurs", ""],
            ["A32", "Détendeur adulte", "Détendeurs", "cassé"],
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal("Import terminé : 2 équipements ajoutés, 1 mis à jour, 2 lignes ignorées : "
            + "ligne 3 (A02 est dehors : attendez son retour avant de le mettre hors service) ; "
            + "ligne 6 (état « cassé » (En service, Hors service ou Réformé))", r.Message.TrimEnd('.'));
        Assert.Equal((EtatEquipement.HorsService, EtatEquipement.EnService), (a01.Etat, a02.Etat));
        Assert.Equal((EtatEquipement.Reforme, EtatEquipement.EnService), (Par("A30").Etat, Par("A31").Etat));
    }

    [Theory]
    [InlineData("En service", EtatEquipement.EnService)]
    [InlineData("hors-service", EtatEquipement.HorsService)]
    [InlineData("H.S.", EtatEquipement.HorsService)]
    [InlineData("REFORME", EtatEquipement.Reforme)]
    [InlineData("", null)]
    [InlineData("bof", null)]
    public void LireEtat_reconnait_les_ecritures_courantes(string texte, EtatEquipement? attendu) =>
        Assert.Equal(attendu, ServiceInventaire.LireEtat(texte));
}
