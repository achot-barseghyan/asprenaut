using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Import des fiches de suivi EPI du classeur Excel du club (un onglet par équipement, règle 45).</summary>
public class ImportFichesEpiTests
{
    private readonly Contexte _ctx = new();

    private static IReadOnlyList<string> L(params string[] cellules) => cellules;

    /// <summary>Fiche d'un détendeur : B/D/G premier étage, I/K/N deuxième étage, P/R/U octopus (comme le classeur).</summary>
    private static FeuilleTableur FicheDetendeur(string onglet, params IReadOnlyList<string>[] interventions)
    {
        var lignes = new List<IReadOnlyList<string>>
        {
            L("", "FICHE DE SUIVIE EPI"),
            L("", "EPI:", "", onglet),
            L("", "Premier étage", "MK2", "", "N°", "0614500755", "", "", "Deuxième étage", "R 295", "", "N°", "064500698", "", "", "Octopus", "R 095"),
            L("", "Nom de l'intervenant", "", "Nature de l'intervention", "", "", "date", "", "Nom de l'intervenant", "", "Nature de l'intervention", "", "", "date",
                "", "Nom de l'intervenant", "", "Nature de l'intervention", "", "", "date"),
        };
        lignes.AddRange(interventions);
        return new FeuilleTableur(onglet, lignes);
    }

    private static FeuilleTableur FicheSimple(string onglet, params IReadOnlyList<string>[] interventions)
    {
        var lignes = new List<IReadOnlyList<string>>
        {
            L("", "EPI:", "", onglet),
            L("", "Marque:", "Aqualung", "Modèle:", "Wave"),
            L("", "Nom de l'intervenant", "", "Nature de l'intervention", "", "", "date"),
        };
        lignes.AddRange(interventions);
        return new FeuilleTableur(onglet, lignes);
    }

    private Equipement Stab(string code)
    {
        if (!_ctx.Db.Categories.Any(c => c.Nom == "Gilets"))
        {
            _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Gilets" });
        }

        return _ctx.Equipement(code, categorie: "Gilets");
    }

    private Equipement Detendeur(string code)
    {
        var det = _ctx.Equipement(code, categorie: "Détendeurs");
        det.Modele = "MK2";
        det.ModeleDeuxiemeEtage = "R 295";
        det.Octopus = "R 095";
        return det;
    }

    // Règle : chaque tableau de la fiche va sur son composant (détendeur), l'onglet « A2 » trouve A02, les dates Excel
    // (numéros de série) sont lues, et ni l'échéance ni l'état ne changent.
    [Fact]
    public async Task Les_interventions_de_chaque_composant_rejoignent_l_historique()
    {
        var det = Detendeur("A02");
        det.DateProchaineRevision = new DateOnly(2027, 1, 1);
        var fiche = FicheDetendeur("A2",
            L("", "Christian DODET", "", "Changement fléxible direct système", "", "", "43251", "", "Aquasub", "", "Révision", "", "", "43504",
                "", "Aquasub", "", "Révision", "", "", "43504"),
            L("", "", "", "", "", "", "", "", "Christian DODET", "", "Changemnt flexible manomètre", "", "", "44998"));

        var r = await _ctx.Revisions.ImporterFichesEpiAsync([fiche]);

        Assert.True(r.Reussi, r.Message);
        Assert.Contains("4 interventions", r.Message);
        Assert.Equal(
            [("Premier étage", new DateOnly(2018, 5, 31), "Changement fléxible direct système", "Christian DODET"),
             ("Deuxième étage", new DateOnly(2019, 2, 8), "Révision", "Aquasub"),
             ("Deuxième étage", new DateOnly(2023, 3, 13), "Changemnt flexible manomètre", "Christian DODET"),
             ("Octopus", new DateOnly(2019, 2, 8), "Révision", "Aquasub")],
            _ctx.Db.Controles.Select(c => (c.Composant, c.DateControle, c.Nature, c.Intervenant)));
        Assert.All(_ctx.Db.Controles, c => Assert.Equal((det.Id, TypeControle.Revision, ResultatControle.Conforme), (c.EquipementId, c.Type, c.Resultat)));
        Assert.Equal(new DateOnly(2027, 1, 1), det.DateProchaineRevision);
        Assert.Contains(_ctx.Db.Journal, j => j.Action == ActionsJournal.FichesEpiImportees);
    }

    // Règle : hors détendeurs, la fiche n'a qu'un tableau, celui de l'équipement complet.
    [Fact]
    public async Task Une_stab_a_ses_interventions_sur_l_equipement_complet()
    {
        var stab = Stab("S05");

        var r = await _ctx.Revisions.ImporterFichesEpiAsync([FicheSimple("S05", L("", "Eric BRUNO", "", "Contrôle visuel = RAS", "", "", "43168"))]);

        Assert.True(r.Reussi, r.Message);
        var c = Assert.Single(_ctx.Db.Controles);
        Assert.Equal((stab.Id, "", new DateOnly(2018, 3, 9)), (c.EquipementId, c.Composant, c.DateControle));
    }

    // Règle : réimporter le même classeur ne recopie rien.
    [Fact]
    public async Task Reimporter_ne_cree_pas_de_doublon()
    {
        Stab("S05");
        var fiche = FicheSimple("S05", L("", "Eric BRUNO", "", "Contrôle visuel = RAS", "", "", "43168"));
        await _ctx.Revisions.ImporterFichesEpiAsync([fiche]);

        var r = await _ctx.Revisions.ImporterFichesEpiAsync([fiche]);

        Assert.True(r.Reussi, r.Message);
        Assert.Single(_ctx.Db.Controles);
        Assert.Contains("aucune nouvelle intervention", r.Message);
        Assert.Contains("1 déjà dans l'historique", r.Message);
    }

    // Règle : « A1 » et « A01 » sont deux équipements ; un onglet sans équipement et une date illisible sont cités.
    [Fact]
    public async Task Le_code_exact_passe_d_abord_et_les_problemes_sont_cites()
    {
        var a1 = Detendeur("A1");
        var a01 = Detendeur("A01");

        var r = await _ctx.Revisions.ImporterFichesEpiAsync(
        [
            FicheDetendeur("A1", L("", "Aquasub", "", "Révision", "", "", "43504")),
            FicheDetendeur("A01", L("", "Christian DODET", "", "Changement fléxible MP", "", "", "43251"),
                L("", "Aquasub", "", "Révision", "", "", "17/104/2018")),
            FicheSimple("Ancienne S33", L("", "Eric BRUNO", "", "Contrôle visuel", "", "", "43168")),
            new FeuilleTableur("Acceuil", [L("", "ACCEUIL")]),
        ]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal([(a1.Id, "Révision"), (a01.Id, "Changement fléxible MP")], _ctx.Db.Controles.Select(c => (c.EquipementId, c.Nature)));
        Assert.Contains("Ancienne S33", r.Message);
        Assert.DoesNotContain("Acceuil", r.Message);
        Assert.Contains("A01 ligne 6", r.Message);
    }

    // Règle : une date mal tapée reprend celle de la même ligne dans un autre tableau de la fiche.
    [Fact]
    public async Task Une_date_mal_tapee_reprend_celle_de_la_meme_ligne()
    {
        Detendeur("E42");
        var fiche = FicheDetendeur("E 42",
            L("", "Aquasub", "", "Révision", "", "", "43207", "", "Aquasub", "", "Révision + Changement DS", "", "", "17/104/2018"));

        var r = await _ctx.Revisions.ImporterFichesEpiAsync([fiche]);

        Assert.True(r.Reussi, r.Message);
        Assert.Equal([("Premier étage", new DateOnly(2018, 4, 17)), ("Deuxième étage", new DateOnly(2018, 4, 17))],
            _ctx.Db.Controles.Select(c => (c.Composant, c.DateControle)));
        Assert.DoesNotContain("illisible", r.Message);
    }

    [Theory]
    [InlineData("45307", 2024, 1, 16)]
    [InlineData("15/01/2024", 2024, 1, 15)]
    [InlineData("5/3/19", 2019, 3, 5)]
    public void Les_dates_excel_et_texte_sont_lues(string cellule, int a, int m, int j) =>
        Assert.Equal(new DateOnly(a, m, j), ServiceRevisions.LireDateExcel(cellule));

    [Fact]
    public async Task L_import_demande_le_code_gestionnaire()
    {
        Stab("S05");
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        var r = await _ctx.Revisions.ImporterFichesEpiAsync([FicheSimple("S05", L("", "Eric BRUNO", "", "Contrôle visuel", "", "", "43168"))]);

        Assert.False(r.Reussi);
        Assert.Empty(_ctx.Db.Controles);
    }
}
