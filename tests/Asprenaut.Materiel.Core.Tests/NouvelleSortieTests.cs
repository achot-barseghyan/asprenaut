using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;
using Asprenaut.Materiel.Core.Tests.Memoire;

namespace Asprenaut.Materiel.Core.Tests;

/// <summary>Formulaire « Nouvelle sortie » (maquette) : intitulé proposé, raccourcis de date et de lieu.</summary>
public class NouvelleSortieTests
{
    private readonly Contexte _ctx = new();

    private static readonly TypeDeSortie Piscine = new() { Nom = "Piscine", Pictogramme = TypeSortie.Piscine };
    private static readonly TypeDeSortie Mer = new() { Nom = "Mer", Pictogramme = TypeSortie.Mer };
    private static readonly TypeDeSortie Nettoyage = new() { Nom = "Nettoyage du lac", Pictogramme = TypeSortie.Autre };

    // Jeudi 12 novembre 2026, samedi 14.
    private static readonly DateOnly Jeudi = new(2026, 11, 12);
    private static readonly DateOnly Samedi = new(2026, 11, 14);

    [Fact]
    public void L_intitule_propose_suit_le_type_le_jour_la_classe_et_le_lieu()
    {
        var pascal = new Moniteur { Prenom = "Pascal", Nom = "Durand" };
        var niveau2 = new Moniteur { Nom = "Niveau 2", Genre = GenreResponsable.Classe };

        Assert.Equal("Piscine du jeudi", ServiceSortie.IntitulePropose(Piscine, Jeudi, pascal, ""));
        Assert.Equal("Sortie mer du samedi", ServiceSortie.IntitulePropose(Mer, Samedi, null, ""));
        Assert.Equal("Piscine Niveau 2", ServiceSortie.IntitulePropose(Piscine, Jeudi, niveau2, "Chalon"));
        Assert.Equal("Sortie Marseille", ServiceSortie.IntitulePropose(Mer, Samedi, pascal, " Marseille "));
        Assert.Equal("Piscine du jeudi", ServiceSortie.IntitulePropose(Piscine, Jeudi, pascal, "Chalon")); // Lieu : mer et lac seulement.
        Assert.Equal("Nettoyage du lac du jeudi", ServiceSortie.IntitulePropose(Nettoyage, Jeudi, null, ""));
        Assert.Equal("Piscine", ServiceSortie.IntitulePropose(Piscine, null, null, "")); // Date illisible.
    }

    [Fact]
    public void Les_dates_proposees_vont_jusqu_au_prochain_week_end()
    {
        var jeudi = ServiceSortie.DatesProposees(Jeudi);
        Assert.Equal(["Aujourd'hui", "Demain", "Samedi", "Dimanche"], jeudi.Select(d => d.Texte));
        Assert.Equal([Jeudi, Jeudi.AddDays(1), Samedi, Samedi.AddDays(1)], jeudi.Select(d => d.Date));

        // Vendredi ou samedi : samedi est déjà « Demain » ou « Aujourd'hui », on propose le week-end suivant.
        Assert.Equal(Samedi.AddDays(7), ServiceSortie.DatesProposees(Jeudi.AddDays(1))[2].Date);
        Assert.Equal(Samedi.AddDays(7), ServiceSortie.DatesProposees(Samedi)[2].Date);
        Assert.Equal(Samedi.AddDays(7), ServiceSortie.DatesProposees(Samedi.AddDays(1))[2].Date); // Dimanche → samedi qui vient.
        Assert.Equal(Samedi, ServiceSortie.DatesProposees(Jeudi.AddDays(-3))[2].Date); // Lundi → ce samedi.
    }

    [Fact]
    public async Task Les_lieux_recents_sont_ceux_des_dernieres_sorties_sans_doublon()
    {
        async Task Sortie(DateOnly date, string lieu) =>
            Assert.True((await _ctx.Sorties.CreerAsync(new NouvelleSortie(date, TypeSortie.Piscine, "Sortie", _ctx.Pascal.Id, lieu))).Reussi);

        await Sortie(new DateOnly(2026, 10, 1), "Piscine Chalon");
        await Sortie(new DateOnly(2026, 10, 8), "Marseille");
        await Sortie(new DateOnly(2026, 10, 15), "  ");
        await Sortie(new DateOnly(2026, 10, 22), "piscine chalon");
        await Sortie(new DateOnly(2026, 9, 1), "Lac des Settons");
        await Sortie(new DateOnly(2026, 8, 1), "Fosse Villeneuve");

        Assert.Equal(["piscine chalon", "Marseille", "Lac des Settons"], await _ctx.Sorties.LieuxRecentsAsync());
    }
}
