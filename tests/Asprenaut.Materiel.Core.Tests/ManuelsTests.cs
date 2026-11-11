using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Manuels des fabricants : ajout, sommaire, délais du fabricant comparés à ceux du club.</summary>
public sealed class ManuelsTests : IDisposable
{
    private readonly Contexte _ctx = new();
    private readonly ManuelRepositoryMemoire _depot;
    private readonly StockageManuelsMemoire _stockage = new();
    private readonly ServiceManuels _manuels;
    private readonly string _pdf = Path.Combine(Path.GetTempPath(), $"notice-{Guid.NewGuid():N}.pdf");
    private readonly long _detendeurs;
    private readonly long _gilets;

    public ManuelsTests()
    {
        File.WriteAllText(_pdf, "%PDF-1.4");
        _ctx.Db.Categories.Add(new Categorie { Id = _ctx.Db.ProchainId(), Nom = "Gilets" });
        _detendeurs = _ctx.Db.Categories.First(c => c.Nom == "Détendeurs").Id;
        _gilets = _ctx.Db.Categories.First(c => c.Nom == "Gilets").Id;
        _depot = new ManuelRepositoryMemoire(_ctx.Db);
        _manuels = new ServiceManuels(_depot, new CategorieRepositoryMemoire(_ctx.Db), _stockage, _ctx.Reglages, _ctx.ServiceReglages, _ctx.Stockage, _ctx.Gestion);
    }

    public void Dispose() => File.Delete(_pdf);

    private FicheManuel Fiche(string sommaire = "", long? categorie = null, int? mois = 24, int? pageEntretien = 17) =>
        new("Scubapro", "Détendeurs", categorie ?? _detendeurs, "MK25 EVO, A700", "07/2020", mois, pageEntretien,
            "Révision : tous les 2 ans\nPlus de 100 plongées par an : révision 1 an", sommaire);

    [Fact]
    public async Task L_ajout_copie_le_pdf_et_range_le_sommaire()
    {
        var r = await _manuels.AjouterAsync(Fiche("4. Système de détendeur 6\n\n8.2 Entretien ....... p. 17\n9. Nitrox ; 18"), _pdf, 24);

        Assert.True(r.Reussi, r.Message);
        var manuel = Assert.Single(await _manuels.ListerAsync());
        Assert.Contains(manuel.Fichier, _stockage.Fichiers);
        Assert.Equal(24, manuel.NbPages);
        Assert.Equal([new SectionManuel("4. Système de détendeur", 6), new SectionManuel("8.2 Entretien", 17), new SectionManuel("9. Nitrox", 18)], manuel.Sections);
        Assert.Equal(("Plus de 100 plongées par an", "révision 1 an"), manuel.LignesEntretien[1]);
    }

    [Theory]
    [InlineData("Entretien", "Ligne 1 du sommaire")]
    [InlineData("Avertissements 4\n17", "Ligne 2 du sommaire")]
    [InlineData("Dépannage ; 40", "le PDF n'a que 24 pages")]
    public async Task Un_sommaire_mal_ecrit_ou_hors_du_pdf_est_refuse(string sommaire, string attendu)
    {
        var r = await _manuels.AjouterAsync(Fiche(sommaire), _pdf, 24);

        Assert.False(r.Reussi);
        Assert.Contains(attendu, r.Message);
        Assert.Empty(_stockage.Fichiers);
    }

    [Fact]
    public async Task Marque_titre_fichier_pdf_et_page_d_entretien_sont_verifies()
    {
        Assert.False((await _manuels.AjouterAsync(Fiche() with { Marque = " " }, _pdf, 24)).Reussi);
        Assert.False((await _manuels.AjouterAsync(Fiche() with { Titre = "" }, _pdf, 24)).Reussi);
        Assert.False((await _manuels.AjouterAsync(Fiche(pageEntretien: 30), _pdf, 24)).Reussi);
        Assert.False((await _manuels.AjouterAsync(Fiche(mois: 0), _pdf, 24)).Reussi);
        Assert.False((await _manuels.AjouterAsync(Fiche(), _pdf, 0)).Reussi);
        Assert.False((await _manuels.AjouterAsync(Fiche(), Path.ChangeExtension(_pdf, ".txt"), 24)).Reussi);
        Assert.Empty(await _manuels.ListerAsync());
    }

    [Fact]
    public async Task Ajouter_un_manuel_demande_le_code_gestionnaire()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();

        var r = await _manuels.AjouterAsync(Fiche(), _pdf, 24);

        Assert.False(r.Reussi);
        Assert.Empty(_stockage.Fichiers);
    }

    [Fact]
    public async Task Les_equipements_lies_ont_la_meme_categorie_et_la_meme_marque()
    {
        var scubapro = _ctx.Equipement("DET-001", categorie: "Détendeurs");
        scubapro.Marque = "scubapro ";
        var aqualung = _ctx.Equipement("DET-002", categorie: "Détendeurs");
        aqualung.Marque = "Aqua Lung";
        _ctx.Equipement("DET-004", categorie: "Détendeurs").Marque = "Scuba-Pro";
        _ctx.Equipement("DET-003", EtatEquipement.Reforme, categorie: "Détendeurs").Marque = "Scubapro";
        var id = (await _manuels.AjouterAsync(Fiche(), _pdf, 24)).Id!.Value;

        Assert.Equal(["DET-001", "DET-004"], await _manuels.ListerCodesEquipementsAsync(id));
        Assert.Equal(2, Assert.Single(await _manuels.ListerAsync()).NbEquipements);

        // Bouton « Manuel » des lignes de matériel : l'équipement Scubapro mène à ce manuel, l'Aqua Lung à aucun.
        var parEquipement = await _manuels.ManuelsDesEquipementsAsync();
        Assert.Equal(id, parEquipement[scubapro.Id]);
        Assert.False(parEquipement.ContainsKey(aqualung.Id));
    }

    [Fact]
    public async Task Detendeurs_revises_tous_les_24_mois_conformes_au_fabricant()
    {
        await _manuels.AjouterAsync(Fiche(), _pdf, 24);

        var conformite = _manuels.Conformite(Assert.Single(await _manuels.ListerAsync()));

        Assert.NotNull(conformite);
        Assert.True(conformite.Conforme);
        Assert.Equal(24, conformite.MoisClub);
    }

    [Fact]
    public async Task Gilets_sans_date_obligatoire_signales_puis_alignes_sur_le_fabricant()
    {
        await _manuels.AjouterAsync(Fiche(categorie: _gilets, mois: 12, pageEntretien: 6) with { Titre = "Gilets" }, _pdf, 36);
        var manuel = Assert.Single(await _manuels.ListerAsync());

        var avant = _manuels.Conformite(manuel);
        Assert.NotNull(avant);
        Assert.False(avant.Conforme);
        Assert.Contains("n'impose pas de date", avant.Message);

        Assert.True(_manuels.AlignerRevision(manuel).Reussi);
        Assert.Equal(12, ServiceRevisions.MoisRevision(_ctx.Reglages, "Gilets"));
        Assert.True(_manuels.Conformite(manuel)!.Conforme);
    }

    [Fact]
    public async Task Pas_de_comparaison_sans_delai_du_fabricant()
    {
        await _manuels.AjouterAsync(Fiche(mois: null), _pdf, 24);

        Assert.Null(_manuels.Conformite(Assert.Single(await _manuels.ListerAsync())));
    }

    [Fact]
    public async Task Modifier_garde_le_pdf_et_retirer_supprime_le_fichier()
    {
        var id = (await _manuels.AjouterAsync(Fiche(), _pdf, 24)).Id!.Value;

        Assert.True((await _manuels.ModifierAsync(id, Fiche("8.2 Entretien ; 17") with { Edition = "2021" })).Reussi);
        var manuel = Assert.Single(await _manuels.ListerAsync());
        Assert.Equal("2021", manuel.Edition);
        Assert.Contains(manuel.Fichier, _stockage.Fichiers);

        Assert.True((await _manuels.SupprimerAsync(id)).Reussi);
        Assert.Empty(await _manuels.ListerAsync());
        Assert.Empty(_stockage.Fichiers);
    }

    [Fact]
    public async Task Les_notices_fournies_s_installent_une_seule_fois_et_sans_code()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
        var dossier = Directory.CreateTempSubdirectory("fournis-").FullName;
        string Pdf(string nom)
        {
            var chemin = Path.Combine(dossier, nom);
            File.WriteAllText(chemin, "%PDF-1.4");
            return chemin;
        }

        try
        {
            ManuelFourni[] fournis =
            [
                new("Détendeurs", Fiche("8.2 Entretien ; 17", categorie: 0), Pdf("detendeurs.pdf"), 24),
                new("Gilets", Fiche(mois: 12, pageEntretien: 6) with { Titre = "Gilets", CategorieId = null }, Pdf("gilets.pdf"), 36),
                new("Inconnue", Fiche(pageEntretien: 40) with { Titre = "Trop court" }, Pdf("court.pdf"), 24), // Page hors du PDF : ignoré.
            ];

            Assert.Equal(2, await _manuels.InstallerFournisAsync(fournis));
            var installes = await _manuels.ListerAsync();
            Assert.Equal(["Détendeurs", "Gilets"], installes.Select(m => m.Categorie));
            Assert.True(_manuels.EstInstalle("gilets.pdf"));

            // Retiré par le club : il ne revient pas au lancement suivant.
            _ctx.Gestion.Deverrouiller("1234");
            Assert.True((await _manuels.SupprimerAsync(installes[0].Id)).Reussi);
            Assert.Equal(0, await _manuels.InstallerFournisAsync(fournis));
            Assert.Single(await _manuels.ListerAsync());

            // Nouvelle notice livrée avec une version suivante : installée, les autres ne bougent pas.
            ManuelFourni masque = new("Masques", Fiche(mois: null, pageEntretien: 1) with { Titre = "Masques" }, Pdf("masques.pdf"), 2);
            Assert.Equal(1, await _manuels.InstallerFournisAsync([.. fournis, masque]));
            Assert.Equal(2, (await _manuels.ListerAsync()).Count);
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    [Theory]
    [InlineData(12, "chaque année")]
    [InlineData(24, "tous les 2 ans")]
    [InlineData(6, "tous les 6 mois")]
    public void Duree_lisible(int mois, string attendu) => Assert.Equal(attendu, ServiceManuels.DureeLisible(mois));
}
