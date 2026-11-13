using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Écran Membres : recherche et filtres des moniteurs et des adhérents.</summary>
public class FiltresMembresTests
{
    private readonly Contexte _ctx = new();

    private Moniteur Moniteur(string prenom, string nom, bool actif = true, bool tiv = false, string? niveau = null, string telephone = "")
    {
        var m = new Moniteur { Id = _ctx.Db.ProchainId(), Prenom = prenom, Nom = nom, Actif = actif, InspecteurTiv = tiv, Niveau = niveau, Telephone = telephone };
        _ctx.Db.Moniteurs.Add(m);
        return m;
    }

    [Fact]
    public async Task Les_moniteurs_se_filtrent_et_se_cherchent()
    {
        Moniteur("Gilles", "Girard", tiv: true, niveau: "MF1", telephone: "06 12 34 56 78");
        Moniteur("Hélène", "Roux", actif: false, tiv: true);
        _ctx.Equipement("BLOC-031");
        await _ctx.SortieEnCoursAsync("Fosse", "BLOC-031"); // Pascal (moniteur du contexte) a du matériel dehors.
        var fiches = (await _ctx.Moniteurs.ListerFichesAsync()).Where(f => !f.Moniteur.EstClasse).ToList();

        var tous = ServiceMoniteurs.Filtrer(fiches, null, FiltreMoniteurs.Tous);
        Assert.Equal((3, 2, 1, 1, 1), (tous.Tous, tous.Actifs, tous.InspecteursTiv, tous.MaterielDehors, tous.Inactifs));
        Assert.Equal(["Gilles"], ServiceMoniteurs.Filtrer(fiches, null, FiltreMoniteurs.InspecteursTiv).Fiches.Select(f => f.Moniteur.Prenom)); // Hélène inactive.
        Assert.Equal(["Pascal"], ServiceMoniteurs.Filtrer(fiches, null, FiltreMoniteurs.MaterielDehors).Fiches.Select(f => f.Moniteur.Prenom));
        Assert.Equal(["Hélène"], ServiceMoniteurs.Filtrer(fiches, null, FiltreMoniteurs.Inactifs).Fiches.Select(f => f.Moniteur.Prenom));

        Assert.Equal(["Hélène"], ServiceMoniteurs.Filtrer(fiches, "helene", FiltreMoniteurs.Tous).Fiches.Select(f => f.Moniteur.Prenom));
        Assert.Equal(["Gilles"], ServiceMoniteurs.Filtrer(fiches, "mf1", FiltreMoniteurs.Tous).Fiches.Select(f => f.Moniteur.Prenom));
        Assert.Equal(["Gilles"], ServiceMoniteurs.Filtrer(fiches, "0612", FiltreMoniteurs.Tous).Fiches.Select(f => f.Moniteur.Prenom)); // Téléphone sans espaces.
        var recherche = ServiceMoniteurs.Filtrer(fiches, "gi", FiltreMoniteurs.Inactifs);
        Assert.Equal((1, 0), (recherche.Tous, recherche.Fiches.Count)); // Les nombres suivent la recherche.
    }

    [Fact]
    public async Task Les_adherents_se_filtrent_par_niveau_nitrox_et_desactives()
    {
        async Task Ajouter(string prenom, string? niveau, bool nitrox = false, bool actif = true)
        {
            var r = await _ctx.Adherents.EnregistrerAsync(new Adherent { Prenom = prenom, Nom = "Test", Niveau = niveau, Nitrox = nitrox, Actif = actif });
            Assert.True(r.Reussi, r.Message);
        }

        await Ajouter("Julie", "Niveau 2", nitrox: true);
        await Ajouter("Léa", "Niveau 2", actif: false);
        await Ajouter("Hugo", "Niveau 1");
        await Ajouter("Nina", null);

        var tous = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Tous, niveau: null);
        Assert.Equal((4, 1, 1), (tous.Tous, tous.Nitrox, tous.Desactives));
        Assert.Equal(["Julie"], (await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Nitrox, null)).Fiches.Select(f => f.Adherent.Prenom));
        Assert.Equal(["Léa"], (await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Desactives, null)).Fiches.Select(f => f.Adherent.Prenom));

        var niveau2 = await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Tous, "Niveau 2");
        Assert.Equal((2, 1, 1), (niveau2.Tous, niveau2.Nitrox, niveau2.Desactives));
        Assert.Equal(["Nina"], (await _ctx.Adherents.ListerAsync(null, FiltreAdherents.Tous, ServiceAdherents.SansNiveau)).Fiches.Select(f => f.Adherent.Prenom));
    }
}
