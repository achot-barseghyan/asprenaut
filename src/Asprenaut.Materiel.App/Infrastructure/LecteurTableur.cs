using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>
/// Lit la première feuille d'un classeur Excel (.xlsx) ou un fichier CSV en lignes de cellules (texte), sans bibliothèque :
/// un .xlsx est un zip de fichiers XML (feuille, textes partagés). Le fichier peut rester ouvert dans Excel (lecture partagée).
/// </summary>
public static class LecteurTableur
{
    public const string Filtre = "Liste des adhérents (*.xlsx;*.csv)|*.xlsx;*.csv|Classeur Excel (*.xlsx)|*.xlsx|Fichier CSV (*.csv)|*.csv";

    public static IReadOnlyList<IReadOnlyList<string>> Lire(string chemin)
    {
        using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Path.GetExtension(chemin).ToLowerInvariant() is ".xlsx" or ".xlsm" ? LireXlsx(flux) : LireCsv(flux);
    }

    // ---------------------------------------------------------------- CSV

    /// <summary>CSV d'Excel : séparateur « ; » (ou « , », tabulation), guillemets doublés, UTF-8 ou ANSI.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> LireCsv(Stream flux)
    {
        using var memoire = new MemoryStream();
        flux.CopyTo(memoire);
        var octets = memoire.ToArray();
        string texte;
        try
        {
            texte = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(octets);
        }
        catch (DecoderFallbackException)
        {
            texte = Encoding.Latin1.GetString(octets); // Fichier enregistré en ANSI par un ancien Excel.
        }

        texte = texte.TrimStart('﻿');
        var premiere = texte.Split('\n')[0];
        var separateur = new[] { ';', ',', '\t' }.MaxBy(c => premiere.Count(x => x == c));

        var lignes = new List<IReadOnlyList<string>>();
        var cellules = new List<string>();
        var cellule = new StringBuilder();
        var entreGuillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (entreGuillemets)
            {
                if (c == '"' && i + 1 < texte.Length && texte[i + 1] == '"')
                {
                    cellule.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreGuillemets = false;
                }
                else
                {
                    cellule.Append(c);
                }
            }
            else if (c == '"')
            {
                entreGuillemets = true;
            }
            else if (c == separateur)
            {
                cellules.Add(cellule.ToString());
                cellule.Clear();
            }
            else if (c == '\n')
            {
                cellules.Add(cellule.ToString().TrimEnd('\r'));
                cellule.Clear();
                lignes.Add(cellules);
                cellules = [];
            }
            else
            {
                cellule.Append(c);
            }
        }

        if (cellule.Length > 0 || cellules.Count > 0)
        {
            cellules.Add(cellule.ToString().TrimEnd('\r'));
            lignes.Add(cellules);
        }

        return lignes;
    }

    // ---------------------------------------------------------------- XLSX

    private static readonly XNamespace Feuille = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace RelationsPaquet = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Première feuille du classeur : textes partagés, textes en ligne, nombres (valeur brute), cellules vides gardées.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> LireXlsx(Stream flux)
    {
        using var zip = new ZipArchive(flux, ZipArchiveMode.Read, leaveOpen: true);
        var partages = TextesPartages(zip);

        var entreeFeuille = zip.GetEntry(CheminPremiereFeuille(zip)) ?? zip.Entries.First(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        return LireFeuille(entreeFeuille, partages);
    }

    public const string FiltreClasseur = "Classeur Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm";

    /// <summary>
    /// Toutes les feuilles d'un classeur .xlsx ou .xlsm (avec macros : elles ne sont pas lues), dans l'ordre des onglets.
    /// Le fichier peut rester ouvert dans Excel.
    /// </summary>
    public static IReadOnlyList<FeuilleTableur> LireFeuilles(string chemin)
    {
        using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(flux, ZipArchiveMode.Read);
        var partages = TextesPartages(zip);
        XDocument classeur, rels;
        using (var s = zip.GetEntry("xl/workbook.xml")?.Open() ?? throw new InvalidDataException("Classeur sans workbook.xml."))
        {
            classeur = XDocument.Load(s);
        }

        using (var s = zip.GetEntry("xl/_rels/workbook.xml.rels")?.Open() ?? throw new InvalidDataException("Classeur sans relations."))
        {
            rels = XDocument.Load(s);
        }

        var cibles = rels.Descendants(RelationsPaquet + "Relationship")
            .ToDictionary(r => (string?)r.Attribute("Id") ?? "", r => (string?)r.Attribute("Target") ?? "");
        var feuilles = new List<FeuilleTableur>();
        foreach (var sheet in classeur.Descendants(Feuille + "sheet"))
        {
            var id = (string?)sheet.Attribute(Relations + "id") ?? "";
            if (!cibles.TryGetValue(id, out var cible))
            {
                continue;
            }

            var chemin2 = cible.StartsWith('/') ? cible.TrimStart('/') : $"xl/{cible}";
            if (zip.GetEntry(chemin2) is { } entree)
            {
                feuilles.Add(new FeuilleTableur((string?)sheet.Attribute("name") ?? "", LireFeuille(entree, partages)));
            }
        }

        return feuilles;
    }

    private static List<string> TextesPartages(ZipArchive zip)
    {
        var partages = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } entreeTextes)
        {
            using var s = entreeTextes.Open();
            partages.AddRange(XDocument.Load(s).Descendants(Feuille + "si").Select(si => string.Concat(si.Descendants(Feuille + "t").Select(t => t.Value))));
        }

        return partages;
    }

    private static IReadOnlyList<IReadOnlyList<string>> LireFeuille(ZipArchiveEntry entreeFeuille, List<string> partages)
    {
        XDocument doc;
        using (var s = entreeFeuille.Open())
        {
            doc = XDocument.Load(s);
        }

        var lignes = new List<IReadOnlyList<string>>();
        foreach (var row in doc.Descendants(Feuille + "row"))
        {
            // Lignes sautées (vides) : elles comptent pour les numéros de ligne affichés à l'import.
            if (int.TryParse((string?)row.Attribute("r"), out var numero))
            {
                while (lignes.Count < numero - 1)
                {
                    lignes.Add([]);
                }
            }

            var cellules = new List<string>();
            foreach (var c in row.Elements(Feuille + "c"))
            {
                var colonne = Colonne((string?)c.Attribute("r")) ?? cellules.Count;
                while (cellules.Count < colonne)
                {
                    cellules.Add("");
                }

                var type = (string?)c.Attribute("t");
                var valeur = (string?)c.Element(Feuille + "v") ?? "";
                cellules.Add(type switch
                {
                    "s" when int.TryParse(valeur, out var i) && i < partages.Count => partages[i],
                    "inlineStr" => string.Concat(c.Descendants(Feuille + "t").Select(t => t.Value)),
                    _ => valeur,
                });
            }

            lignes.Add(cellules);
        }

        return lignes;
    }

    /// <summary>Chemin de la première feuille d'après workbook.xml et ses relations (sinon sheet1.xml).</summary>
    private static string CheminPremiereFeuille(ZipArchive zip)
    {
        try
        {
            using var classeur = zip.GetEntry("xl/workbook.xml")!.Open();
            var id = (string?)XDocument.Load(classeur).Descendants(Feuille + "sheet").First().Attribute(Relations + "id");
            using var rels = zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
            var cible = (string?)XDocument.Load(rels).Descendants(RelationsPaquet + "Relationship").First(r => (string?)r.Attribute("Id") == id).Attribute("Target") ?? "";
            return cible.StartsWith('/') ? cible.TrimStart('/') : $"xl/{cible}";
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or System.Xml.XmlException)
        {
            return "xl/worksheets/sheet1.xml";
        }
    }

    /// <summary>« C12 » → 2 (colonnes comptées depuis 0).</summary>
    private static int? Colonne(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        var n = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            n = (n * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return n == 0 ? null : n - 1;
    }
}
