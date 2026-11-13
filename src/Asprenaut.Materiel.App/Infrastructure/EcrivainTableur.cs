using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Asprenaut.Materiel.App.Infrastructure;

/// <summary>Une colonne du modèle : en-tête, aide (bulle à la sélection d'une case), largeur Excel, liste de choix éventuelle.</summary>
public sealed record ColonneClasseur(string EnTete, string Aide, int Largeur, IReadOnlyList<string>? Choix = null);

/// <summary>
/// Écrit un classeur Excel (.xlsx) à remplir, sans bibliothèque : un zip de quelques fichiers XML (comme
/// <see cref="LecteurTableur"/> les lit). Première feuille : en-têtes en couleur, première ligne figée, cases au format
/// texte (les zéros des téléphones et licences restent), bulle d'aide par colonne, liste de choix (Oui / Non).
/// Seconde feuille : le mode d'emploi. Troisième, cachée : les listes de choix.
/// </summary>
public static class EcrivainTableur
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Paquet = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Lignes prévues pour la saisie (aide et listes de choix s'y appliquent).</summary>
    public const int LignesSaisie = 1000;

    // Styles (styles.xml) : 0 normal, 1 en-tête, 2 texte saisi, 3 titre du mode d'emploi, 4 paragraphe.
    private const int StyleEnTete = 1;
    private const int StyleTexte = 2;
    private const int StyleTitre = 3;
    private const int StyleParagraphe = 4;

    /// <summary>Écrit le modèle dans <paramref name="chemin"/> (remplacé s'il existe). <paramref name="couleurEnTete"/> : « 0F5F86 ».</summary>
    public static void EcrireModele(string chemin, string nomFeuille, IReadOnlyList<ColonneClasseur> colonnes, IReadOnlyList<string> modeEmploi, string couleurEnTete)
    {
        using var flux = File.Create(chemin);
        EcrireModele(flux, nomFeuille, colonnes, modeEmploi, couleurEnTete);
    }

    public static void EcrireModele(Stream flux, string nomFeuille, IReadOnlyList<ColonneClasseur> colonnes, IReadOnlyList<string> modeEmploi, string couleurEnTete)
    {
        using var zip = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: true);
        Ecrire(zip, "[Content_Types].xml", new XElement(Types + "Types",
            new XElement(Types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            Remplacement("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
            Remplacement("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
            Remplacement("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
            Remplacement("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
            Remplacement("/xl/worksheets/sheet3.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        Ecrire(zip, "_rels/.rels", Relations(("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")));
        Ecrire(zip, "xl/workbook.xml", new XElement(Ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", R),
            new XElement(Ns + "bookViews", new XElement(Ns + "workbookView", new XAttribute("activeTab", 0))),
            new XElement(Ns + "sheets",
                new XElement(Ns + "sheet", new XAttribute("name", NomFeuilleValide(nomFeuille)), new XAttribute("sheetId", 1), new XAttribute(R + "id", "rId1")),
                new XElement(Ns + "sheet", new XAttribute("name", "Mode d'emploi"), new XAttribute("sheetId", 2), new XAttribute(R + "id", "rId2")),
                new XElement(Ns + "sheet", new XAttribute("name", NomFeuilleListes), new XAttribute("sheetId", 3), new XAttribute("state", "hidden"), new XAttribute(R + "id", "rId4")))));
        Ecrire(zip, "xl/_rels/workbook.xml.rels", Relations(
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet2.xml"),
            ("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml"),
            ("rId4", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet3.xml")));
        Ecrire(zip, "xl/styles.xml", Styles(couleurEnTete));
        Ecrire(zip, "xl/worksheets/sheet1.xml", FeuilleSaisie(colonnes));
        Ecrire(zip, "xl/worksheets/sheet2.xml", FeuilleModeEmploi(modeEmploi));
        Ecrire(zip, "xl/worksheets/sheet3.xml", FeuilleListes(colonnes));
    }

    private static XElement Remplacement(string partie, string type) =>
        new(Types + "Override", new XAttribute("PartName", partie), new XAttribute("ContentType", type));

    private static XElement Relations(params (string Id, string Type, string Cible)[] relations) =>
        new(Paquet + "Relationships", relations.Select(r =>
            new XElement(Paquet + "Relationship", new XAttribute("Id", r.Id), new XAttribute("Type", r.Type), new XAttribute("Target", r.Cible))));

    private static void Ecrire(ZipArchive zip, string nom, XElement racine)
    {
        using var flux = zip.CreateEntry(nom, CompressionLevel.Optimal).Open();
        using var ecrivain = new StreamWriter(flux, new UTF8Encoding(false));
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), racine).Save(ecrivain, SaveOptions.DisableFormatting);
    }

    /// <summary>Excel refuse : \ / ? * [ ] et plus de 31 caractères.</summary>
    private static string NomFeuilleValide(string nom)
    {
        var propre = new string(nom.Where(c => !"\\/?*[]:".Contains(c)).ToArray()).Trim();
        return propre.Length == 0 ? "Feuille1" : propre[..Math.Min(31, propre.Length)];
    }

    private static string Lettre(int index) => index < 26 ? ((char)('A' + index)).ToString() : Lettre((index / 26) - 1) + (char)('A' + (index % 26));

    private static XElement Texte(string reference, string valeur, int style) =>
        new(Ns + "c", new XAttribute("r", reference), new XAttribute("s", style), new XAttribute("t", "inlineStr"),
            new XElement(Ns + "is", new XElement(Ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), valeur)));

    private static XElement Styles(string couleurEnTete) =>
        new(Ns + "styleSheet",
            new XElement(Ns + "fonts", new XAttribute("count", 4),
                Police(11, false, null), Police(11, true, "FFFFFFFF"), Police(14, true, null), Police(11, false, null)),
            new XElement(Ns + "fills", new XAttribute("count", 3),
                new XElement(Ns + "fill", new XElement(Ns + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(Ns + "fill", new XElement(Ns + "patternFill", new XAttribute("patternType", "gray125"))),
                new XElement(Ns + "fill", new XElement(Ns + "patternFill", new XAttribute("patternType", "solid"),
                    new XElement(Ns + "fgColor", new XAttribute("rgb", "FF" + couleurEnTete)), new XElement(Ns + "bgColor", new XAttribute("indexed", 64))))),
            new XElement(Ns + "borders", new XAttribute("count", 1),
                new XElement(Ns + "border", new XElement(Ns + "left"), new XElement(Ns + "right"), new XElement(Ns + "top"), new XElement(Ns + "bottom"), new XElement(Ns + "diagonal"))),
            new XElement(Ns + "cellStyleXfs", new XAttribute("count", 1), Format(0, 0, 0)),
            new XElement(Ns + "cellXfs", new XAttribute("count", 5),
                Format(0, 0, 0),
                Format(1, 2, 0, new XElement(Ns + "alignment", new XAttribute("vertical", "center"))),
                Format(3, 0, 49),
                Format(2, 0, 0),
                Format(3, 0, 0, new XElement(Ns + "alignment", new XAttribute("wrapText", 1), new XAttribute("vertical", "top")))),
            new XElement(Ns + "cellStyles", new XAttribute("count", 1),
                new XElement(Ns + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));

    private static XElement Police(int taille, bool gras, string? couleur) =>
        new(Ns + "font",
            gras ? new XElement(Ns + "b") : null,
            new XElement(Ns + "sz", new XAttribute("val", taille)),
            couleur is null ? null : new XElement(Ns + "color", new XAttribute("rgb", couleur)),
            new XElement(Ns + "name", new XAttribute("val", "Calibri")),
            new XElement(Ns + "family", new XAttribute("val", 2)));

    /// <summary>numFmtId 49 = format « Texte » (@).</summary>
    private static XElement Format(int police, int remplissage, int nombre, XElement? alignement = null) =>
        new(Ns + "xf",
            new XAttribute("numFmtId", nombre), new XAttribute("fontId", police), new XAttribute("fillId", remplissage), new XAttribute("borderId", 0),
            police > 0 ? new XAttribute("applyFont", 1) : null,
            remplissage > 0 ? new XAttribute("applyFill", 1) : null,
            nombre > 0 ? new XAttribute("applyNumberFormat", 1) : null,
            alignement is null ? null : new XAttribute("applyAlignment", 1),
            alignement);

    private static XElement FeuilleSaisie(IReadOnlyList<ColonneClasseur> colonnes)
    {
        var derniere = Lettre(colonnes.Count - 1);
        var validations = colonnes.Select((c, i) =>
        {
            var plage = $"{Lettre(i)}2:{Lettre(i)}{LignesSaisie + 1}";
            var aide = c.Aide.Length > 250 ? c.Aide[..250] : c.Aide;
            return c.Choix is { Count: > 0 } choix
                ? new XElement(Ns + "dataValidation",
                    new XAttribute("type", "list"), new XAttribute("allowBlank", 1), new XAttribute("showInputMessage", 1), new XAttribute("showErrorMessage", 1),
                    new XAttribute("errorTitle", c.EnTete), new XAttribute("error", $"Choisissez : {string.Join(", ", choix)}."),
                    new XAttribute("promptTitle", c.EnTete), new XAttribute("prompt", aide), new XAttribute("sqref", plage),
                    new XElement(Ns + "formula1", $"{NomFeuilleListes}!${ColonneListe(colonnes, i)}$1:${ColonneListe(colonnes, i)}${choix.Count}"))
                : new XElement(Ns + "dataValidation",
                    new XAttribute("allowBlank", 1), new XAttribute("showInputMessage", 1),
                    new XAttribute("promptTitle", c.EnTete), new XAttribute("prompt", aide), new XAttribute("sqref", plage));
        }).ToList();

        return new XElement(Ns + "worksheet", new XAttribute(XNamespace.Xmlns + "r", R),
            new XElement(Ns + "dimension", new XAttribute("ref", $"A1:{derniere}1")),
            new XElement(Ns + "sheetViews", new XElement(Ns + "sheetView", new XAttribute("tabSelected", 1), new XAttribute("workbookViewId", 0),
                new XElement(Ns + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")),
                new XElement(Ns + "selection", new XAttribute("pane", "bottomLeft"), new XAttribute("activeCell", "A2"), new XAttribute("sqref", "A2")))),
            new XElement(Ns + "sheetFormatPr", new XAttribute("defaultRowHeight", 15)),
            new XElement(Ns + "cols", colonnes.Select((c, i) => new XElement(Ns + "col",
                new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", c.Largeur), new XAttribute("style", StyleTexte), new XAttribute("customWidth", 1)))),
            new XElement(Ns + "sheetData",
                new XElement(Ns + "row", new XAttribute("r", 1), new XAttribute("ht", 24), new XAttribute("customHeight", 1),
                    colonnes.Select((c, i) => Texte($"{Lettre(i)}1", c.EnTete, StyleEnTete)))),
            new XElement(Ns + "dataValidations", new XAttribute("count", validations.Count), validations));
    }

    /// <summary>
    /// Feuille cachée des listes de choix (une colonne par colonne à liste) : une liste écrite dans la validation est limitée
    /// à 255 caractères, trop peu pour des niveaux réglés par le club.
    /// </summary>
    private const string NomFeuilleListes = "Listes";

    private static string ColonneListe(IReadOnlyList<ColonneClasseur> colonnes, int index) =>
        Lettre(colonnes.Take(index).Count(c => c.Choix is { Count: > 0 }));

    private static XElement FeuilleListes(IReadOnlyList<ColonneClasseur> colonnes)
    {
        var listes = colonnes.Where(c => c.Choix is { Count: > 0 }).Select(c => c.Choix!).ToList();
        var hauteur = listes.Count == 0 ? 0 : listes.Max(l => l.Count);
        return new XElement(Ns + "worksheet",
            new XElement(Ns + "sheetData", Enumerable.Range(0, hauteur).Select(ligne =>
                new XElement(Ns + "row", new XAttribute("r", ligne + 1),
                    listes.Select((l, j) => ligne < l.Count ? Texte($"{Lettre(j)}{ligne + 1}", l[ligne], 0) : null)))));
    }

    private static XElement FeuilleModeEmploi(IReadOnlyList<string> lignes) =>
        new(Ns + "worksheet",
            new XElement(Ns + "cols", new XElement(Ns + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 110), new XAttribute("customWidth", 1))),
            new XElement(Ns + "sheetData", lignes.Select((texte, i) =>
                new XElement(Ns + "row", new XAttribute("r", i + 1), i == 0 ? new XAttribute("ht", 26) : null, i == 0 ? new XAttribute("customHeight", 1) : null,
                    Texte($"A{i + 1}", texte, i == 0 ? StyleTitre : StyleParagraphe)))));
}
