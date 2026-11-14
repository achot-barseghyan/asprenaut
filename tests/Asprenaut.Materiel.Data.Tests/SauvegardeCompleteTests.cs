using System.IO.Compression;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Depots;
using Asprenaut.Materiel.Data.Sauvegardes;

namespace Asprenaut.Materiel.Data.Tests;

/// <summary>Sauvegarde complète : un .zip avec la base, les réglages et les PDF des manuels ; restauration de tout.</summary>
public class SauvegardeCompleteTests : IAsyncLifetime
{
    private readonly BaseTemporaire _base = new();
    private readonly ReglagesFactices _reglages = new();
    private SauvegardeComplete _complete = null!;

    private string DossierManuels => Path.Combine(_base.Dossier, "Manuels");

    public async Task InitializeAsync()
    {
        await _base.InitializeAsync();
        var simple = new ServiceSauvegarde(_base.Fabrique, Path.Combine(_base.Dossier, "Sauvegardes"), () => new DateTime(2026, 11, 14, 10, 30, 0));
        _complete = new SauvegardeComplete(_base.Fabrique, simple, _reglages, DossierManuels, () => new DateTime(2026, 11, 14, 10, 30, 0));
    }

    public Task DisposeAsync() => _base.DisposeAsync();

    /// <summary>Réglages en mémoire, comme parametres.json.</summary>
    private sealed class ReglagesFactices : IReglagesSauvegardables
    {
        public string Json { get; set; } = """{"NomClub":"Asprenaut"}""";

        public string Lire() => Json;

        public bool EstLisible(string json) => json.StartsWith('{');

        public void Remplacer(string json) => Json = json;
    }

    [Fact]
    public async Task La_sauvegarde_complete_remet_la_base_les_reglages_et_les_manuels()
    {
        var adherents = new AdherentRepository(_base.Fabrique);
        await adherents.AjouterAsync(new Adherent { Nom = "Martin", Prenom = "Julie" });
        Directory.CreateDirectory(DossierManuels);
        await File.WriteAllTextAsync(Path.Combine(DossierManuels, "Notice.pdf"), "pdf d'origine");
        var zip = Path.Combine(_base.Dossier, _complete.NomPropose());
        Assert.Equal("Asprenaut sauvegarde complète 2026-11-14.zip", Path.GetFileName(zip));

        var cree = await _complete.CreerAsync(zip);
        Assert.True(cree.Reussi, cree.Message);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal(["Manuels/Notice.pdf", "asprenaut-sauvegarde.json", "materiel.db", "reglages.json"], archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        }

        // Tout change après la sauvegarde…
        await adherents.AjouterAsync(new Adherent { Nom = "Bernard", Prenom = "Thomas" });
        _reglages.Json = """{"NomClub":"Autre club"}""";
        File.Delete(Path.Combine(DossierManuels, "Notice.pdf"));

        // … et revient comme avant.
        var restaure = await _complete.RestaurerAsync(zip);
        Assert.True(restaure.Reussi, restaure.Message);
        Assert.Equal(["Julie"], (await adherents.ListerAsync(actifsSeulement: false)).Select(a => a.Prenom));
        Assert.Equal("""{"NomClub":"Asprenaut"}""", _reglages.Json);
        Assert.Equal("pdf d'origine", await File.ReadAllTextAsync(Path.Combine(DossierManuels, "Notice.pdf")));
        Assert.Single(Directory.GetFiles(Path.Combine(_base.Dossier, "Sauvegardes"), "*_avant-restauration.db")); // Filet de sécurité.
    }

    [Fact]
    public async Task Un_fichier_qui_n_est_pas_une_sauvegarde_complete_ne_change_rien()
    {
        var adherents = new AdherentRepository(_base.Fabrique);
        await adherents.AjouterAsync(new Adherent { Nom = "Martin", Prenom = "Julie" });
        var faux = Path.Combine(_base.Dossier, "faux.zip");
        await File.WriteAllTextAsync(faux, "pas un zip");

        var r = await _complete.RestaurerAsync(faux);

        Assert.False(r.Reussi);
        Assert.Equal("Ce fichier n'est pas une sauvegarde complète Asprenaut.", r.Message);
        Assert.Single(await adherents.ListerAsync(actifsSeulement: false));
        Assert.Equal("""{"NomClub":"Asprenaut"}""", _reglages.Json);
    }

    [Fact]
    public async Task Un_zip_sans_base_ou_aux_reglages_illisibles_est_refuse()
    {
        var sansBase = Path.Combine(_base.Dossier, "sans-base.zip");
        using (var archive = ZipFile.Open(sansBase, ZipArchiveMode.Create))
        {
            archive.CreateEntry("reglages.json");
        }

        Assert.Equal("Ce fichier n'est pas une sauvegarde complète Asprenaut.", (await _complete.RestaurerAsync(sansBase)).Message);

        // Réglages illisibles : refusé avant de toucher à la base.
        var zip = Path.Combine(_base.Dossier, "complete.zip");
        Assert.True((await _complete.CreerAsync(zip)).Reussi);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("reglages.json")!.Delete();
            using var flux = new StreamWriter(archive.CreateEntry("reglages.json").Open());
            flux.Write("illisible");
        }

        var r = await _complete.RestaurerAsync(zip);
        Assert.Equal("Les réglages de cette sauvegarde sont illisibles : rien n'a été changé.", r.Message);
        Assert.False(Directory.Exists(Path.Combine(_base.Dossier, "Sauvegardes"))); // Aucune restauration commencée.
    }
}
