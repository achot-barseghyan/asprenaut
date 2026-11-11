namespace Asprenaut.Materiel.Core.Modeles;

/// <summary>
/// Notice d'un fabricant (table manuel, migration 009) : un PDF copié dans le dossier « Manuels » à côté de la base,
/// relié à une catégorie. Les équipements liés sont ceux de la même catégorie et de la même marque.
/// Sommaire et délais d'entretien sont saisis à l'ajout (le PDF n'est pas analysé).
/// </summary>
public sealed class Manuel
{
    public const int LongueurMaxMarque = 40;
    public const int LongueurMaxTitre = 60;
    public const int LongueurMaxModeles = 160;
    public const int LongueurMaxEdition = 20;
    public const int MoisRevisionMax = 120;

    public long Id { get; set; }
    public string Marque { get; set; } = "";
    public string Titre { get; set; } = "";
    public long? CategorieId { get; set; }

    /// <summary>Nom de la catégorie (lecture seulement).</summary>
    public string? Categorie { get; set; }

    /// <summary>Modèles couverts, texte libre : « MK25 EVO, MK21, A700… ».</summary>
    public string Modeles { get; set; } = "";

    /// <summary>Édition de la notice, texte libre : « 07/2020 ».</summary>
    public string Edition { get; set; } = "";

    /// <summary>Nom du fichier dans le dossier des manuels.</summary>
    public string Fichier { get; set; } = "";

    public int NbPages { get; set; }

    /// <summary>Révision demandée par le fabricant, en mois (24 = tous les 2 ans) ; null = non renseigné.</summary>
    public int? MoisRevision { get; set; }

    /// <summary>Page du PDF qui décrit l'entretien (« Lire la page 17 »).</summary>
    public int? PageEntretien { get; set; }

    /// <summary>Délais du fabricant, une ligne par délai : « Plus de 100 plongées par an : révision 1 an ».</summary>
    public string Entretien { get; set; } = "";

    /// <summary>Sommaire, une ligne par section : « 8.2 Entretien ; 17 » (voir <see cref="SectionManuel.Lire"/>).</summary>
    public string Sommaire { get; set; } = "";

    /// <summary>Équipements liés (même catégorie, même marque, hors réformés) — lecture seulement.</summary>
    public int NbEquipements { get; set; }

    public IReadOnlyList<SectionManuel> Sections => SectionManuel.Lire(Sommaire).Sections;

    /// <summary>Lignes d'entretien découpées au premier « : » (libellé, valeur).</summary>
    public IReadOnlyList<(string Libelle, string Valeur)> LignesEntretien =>
        Entretien.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.IndexOf(':') is var i and > 0 ? (l[..i].Trim(), l[(i + 1)..].Trim()) : (l, ""))
            .ToList();
}

/// <summary>Une entrée du sommaire : titre et page (à partir de 1).</summary>
public sealed record SectionManuel(string Titre, int Page)
{
    /// <summary>
    /// Lit un sommaire tapé, une section par ligne, la page à la fin : « 8.2 Entretien ; 17 », « 8.2 Entretien 17 »,
    /// « 8.2 Entretien ..... p. 17 ». Les lignes vides sont ignorées. Erreur : numéro de la ligne (à partir de 1) et message.
    /// </summary>
    public static (IReadOnlyList<SectionManuel> Sections, string? Erreur) Lire(string? texte)
    {
        var sections = new List<SectionManuel>();
        var lignes = (texte ?? "").Replace("\r", "").Split('\n');
        for (var i = 0; i < lignes.Length; i++)
        {
            var ligne = lignes[i].Trim();
            if (ligne.Length == 0)
            {
                continue;
            }

            var fin = ligne.Length;
            while (fin > 0 && char.IsAsciiDigit(ligne[fin - 1]))
            {
                fin--;
            }

            var titre = ligne[..fin].TrimEnd(' ', ';', ':', '.', '…', '-', '–', '\t');
            if (titre.EndsWith(" p", StringComparison.OrdinalIgnoreCase))
            {
                titre = titre[..^2].TrimEnd(' ', ';', ':', '.', '…', '-', '–', '\t');
            }

            if (fin == ligne.Length || titre.Length == 0 || !int.TryParse(ligne[fin..], out var page) || page < 1)
            {
                return (sections, $"Ligne {i + 1} du sommaire : écrivez le titre puis la page, par exemple « 8.2 Entretien ; 17 ».");
            }

            sections.Add(new SectionManuel(titre, page));
        }

        return (sections, null);
    }
}

/// <summary>
/// Comparaison du délai de révision du fabricant avec celui du club pour la catégorie du manuel
/// (<see cref="Reglages.MoisEntreRevisionsParCategorie"/>). <see cref="MoisClub"/> 0 = pas de date obligatoire.
/// </summary>
public sealed record ConformiteEntretien(int MoisFabricant, int MoisClub, bool Conforme, string Message);
