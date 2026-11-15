using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Modèles de sortie (écran Sorties : « Modèles » et « Démarrage rapide »). Le 06/11/2026 est un vendredi.</summary>
public class ModelesSortieTests
{
    private readonly Contexte _ctx = new();

    private long Type(string nom) => _ctx.Db.TypesSortie.Single(t => t.Nom == nom).Id;

    private async Task<long> ModeleAsync(string nom, DayOfWeek? jour, string type = "Piscine", long? responsable = null, string intitule = "", int? utilisations = null)
    {
        var r = await _ctx.ModelesSortie.EnregistrerAsync(null, new SaisieModele(nom, jour, Type(type), responsable ?? _ctx.Pascal.Id, "Piscine du Grand Chalon", intitule, utilisations));
        Assert.True(r.Reussi, r.Message);
        return r.Id!.Value;
    }

    private void Consultation()
    {
        Assert.True(_ctx.Gestion.DefinirCode(null, "1234", "1234").Reussi);
        _ctx.Gestion.Verrouiller();
    }

    [Fact]
    public void La_prochaine_date_est_le_prochain_jour_du_modele_aujourd_hui_compris()
    {
        var vendredi = new DateOnly(2026, 11, 6);
        Assert.Equal(new DateOnly(2026, 11, 12), ServiceModelesSortie.ProchaineDate(DayOfWeek.Thursday, vendredi));
        Assert.Equal(vendredi, ServiceModelesSortie.ProchaineDate(DayOfWeek.Friday, vendredi));
        Assert.Equal(new DateOnly(2026, 11, 9), ServiceModelesSortie.ProchaineDate(DayOfWeek.Monday, vendredi));
        Assert.Equal(vendredi, ServiceModelesSortie.ProchaineDate(null, vendredi));
        Assert.Equal("Lundi", ServiceModelesSortie.NomJour(DayOfWeek.Monday));
    }

    [Fact]
    public async Task Nom_type_et_responsable_sont_obligatoires_et_le_nom_unique()
    {
        var m = _ctx.ModelesSortie;
        Assert.False((await m.EnregistrerAsync(null, new SaisieModele("  ", null, Type("Piscine"), _ctx.Pascal.Id))).Reussi);
        Assert.False((await m.EnregistrerAsync(null, new SaisieModele("Cours", null, null, _ctx.Pascal.Id))).Reussi);
        Assert.False((await m.EnregistrerAsync(null, new SaisieModele("Cours", null, Type("Piscine"), null))).Reussi);
        Assert.False((await m.EnregistrerAsync(null, new SaisieModele(new string('a', 41), null, Type("Piscine"), _ctx.Pascal.Id))).Reussi);
        await ModeleAsync("Cours du lundi", DayOfWeek.Monday);
        var doublon = await m.EnregistrerAsync(null, new SaisieModele("COURS DU LUNDI", null, Type("Piscine"), _ctx.Pascal.Id));
        Assert.Equal((false, "Un modèle porte déjà ce nom."), (doublon.Reussi, doublon.Message));
    }

    [Fact]
    public async Task Le_demarrage_rapide_commence_par_le_modele_du_jour()
    {
        await ModeleAsync("Sans jour", null);
        await ModeleAsync("Fosse du jeudi", DayOfWeek.Thursday, "Fosse");
        await ModeleAsync("Cours du lundi", DayOfWeek.Monday);
        await ModeleAsync("Piscine du vendredi", DayOfWeek.Friday);
        await ModeleAsync("Week-end", DayOfWeek.Saturday, "Mer");

        var rapide = await _ctx.ModelesSortie.DemarrageRapideAsync();
        Assert.Equal(["Piscine du vendredi", "Week-end", "Cours du lundi", "Fosse du jeudi"], rapide.Select(f => f.Modele.Nom));
        Assert.True(rapide[0].Aujourdhui);
        Assert.False(rapide[1].Aujourdhui);

        // Onglet Modèles : du lundi au dimanche, puis sans jour fixe.
        var tous = await _ctx.ModelesSortie.ListerAsync();
        Assert.Equal(["Cours du lundi", "Fosse du jeudi", "Piscine du vendredi", "Week-end", "Sans jour"], tous.Select(f => f.Modele.Nom));
    }

    [Fact]
    public async Task Creer_la_sortie_reprend_le_modele_et_la_date_du_prochain_jour()
    {
        var niveau1 = _ctx.AjouterClasses("Niveau 1")[0];
        var auto = await ModeleAsync("Cours N1", DayOfWeek.Monday, responsable: niveau1.Id);
        var tape = await ModeleAsync("Fosse", DayOfWeek.Thursday, "Fosse", intitule: "Entraînement fosse N2");

        var r = await _ctx.ModelesSortie.CreerSortieAsync(auto);
        Assert.True(r.Reussi, r.Message);
        var sortie = _ctx.Db.Sorties.Single(s => s.Id == r.Id);
        Assert.Equal((new DateOnly(2026, 11, 9), "Piscine Niveau 1", niveau1.Id, StatutSortie.EnPreparation), (sortie.DateSortie, sortie.Intitule, sortie.MoniteurId, sortie.Statut));
        Assert.Equal(("Piscine du Grand Chalon", Type("Piscine"), 1), (sortie.Lieu, sortie.TypeId, sortie.Utilisations));

        var r2 = await _ctx.ModelesSortie.CreerSortieAsync(tape);
        Assert.Equal("Entraînement fosse N2", _ctx.Db.Sorties.Single(s => s.Id == r2.Id).Intitule);
    }

    [Fact]
    public async Task Un_modele_au_type_masque_ou_au_responsable_inactif_ne_se_lance_pas()
    {
        var id = await ModeleAsync("Cours", DayOfWeek.Monday);
        _ctx.Db.TypesSortie.Single(t => t.Nom == "Piscine").Actif = false;
        var r = await _ctx.ModelesSortie.CreerSortieAsync(id);
        Assert.False(r.Reussi);
        Assert.Contains("masqué", r.Message);

        _ctx.Db.TypesSortie.Single(t => t.Nom == "Piscine").Actif = true;
        _ctx.Pascal.Actif = false;
        Assert.False((await _ctx.ModelesSortie.CreerSortieAsync(id)).Reussi);
        Assert.Empty(_ctx.Db.Sorties);

        // Le modifier en gardant ce responsable reste possible (il était déjà choisi).
        Assert.True((await _ctx.ModelesSortie.EnregistrerAsync(id, new SaisieModele("Cours", DayOfWeek.Tuesday, Type("Piscine"), _ctx.Pascal.Id))).Reussi);
    }

    [Fact]
    public async Task Utilisations_du_type_non_retenues_autres_valeurs_en_mode_gestion_puis_lancement_libre()
    {
        var id = await ModeleAsync("Mer", DayOfWeek.Saturday, "Mer", utilisations: 4); // Celles du type Mer.
        Assert.Null(_ctx.DepotModeles.Modeles.Single().Utilisations);

        Consultation();
        var refus = await _ctx.ModelesSortie.EnregistrerAsync(null, new SaisieModele("Lac journée", null, Type("Lac"), _ctx.Pascal.Id, Utilisations: 3));
        Assert.False(refus.Reussi);

        Assert.True(_ctx.Gestion.Deverrouiller("1234").Reussi);
        var lac = await _ctx.ModelesSortie.EnregistrerAsync(null, new SaisieModele("Lac journée", null, Type("Lac"), _ctx.Pascal.Id, Utilisations: 3));
        Assert.True(lac.Reussi, lac.Message);
        _ctx.Gestion.Verrouiller();

        // Lancé en consultation : les utilisations acceptées à l'enregistrement ne redemandent pas le code.
        var r = await _ctx.ModelesSortie.CreerSortieAsync(lac.Id!.Value);
        Assert.True(r.Reussi, r.Message);
        Assert.Equal(3, _ctx.Db.Sorties.Single().Utilisations);

        // Le modifier sans toucher aux utilisations reste libre.
        Assert.True((await _ctx.ModelesSortie.EnregistrerAsync(lac.Id, new SaisieModele("Lac du dimanche", DayOfWeek.Sunday, Type("Lac"), _ctx.Pascal.Id, Utilisations: 3))).Reussi);
        Assert.True((await _ctx.ModelesSortie.CreerSortieAsync(id)).Reussi);
    }

    [Fact]
    public async Task Couper_un_equipement_par_categorie_demande_le_code_seulement_pour_un_type_nominatif()
    {
        _ctx.Db.TypesSortie.Single(t => t.Nom == "Mer").Nominatif = true;
        Consultation();
        Assert.False((await _ctx.ModelesSortie.EnregistrerAsync(null, new SaisieModele("Mer", null, Type("Mer"), _ctx.Pascal.Id, UnParCategorie: false))).Reussi);

        // Type non nominatif : la règle ne s'applique pas, elle reste active.
        var piscine = await _ctx.ModelesSortie.EnregistrerAsync(null, new SaisieModele("Piscine", null, Type("Piscine"), _ctx.Pascal.Id, UnParCategorie: false));
        Assert.True(piscine.Reussi, piscine.Message);
        Assert.True(_ctx.DepotModeles.Modeles.Single().UnParCategorie);
    }

    [Fact]
    public async Task Dupliquer_donne_un_nom_libre_et_supprimer_retire_le_modele()
    {
        var id = await ModeleAsync("Cours", DayOfWeek.Monday);
        var c1 = await _ctx.ModelesSortie.DupliquerAsync(id);
        var c2 = await _ctx.ModelesSortie.DupliquerAsync(id);
        Assert.Equal(["Cours", "Cours (copie 2)", "Cours (copie)"], _ctx.DepotModeles.Modeles.Select(m => m.Nom).Order(StringComparer.Ordinal));
        Assert.Equal(DayOfWeek.Monday, _ctx.DepotModeles.Modeles.Single(m => m.Id == c2.Id).Jour);

        Assert.True((await _ctx.ModelesSortie.SupprimerAsync(c1.Id!.Value)).Reussi);
        Assert.Equal(2, _ctx.DepotModeles.Modeles.Count);
    }
}
