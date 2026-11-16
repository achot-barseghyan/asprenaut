using System.Globalization;
using System.Text.RegularExpressions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Une feuille d'un classeur : son nom (onglet) et ses lignes de cellules en texte (colonne A = indice 0).</summary>
public sealed record FeuilleTableur(string Nom, IReadOnlyList<IReadOnlyList<string>> Lignes);

/// <summary>
/// Import des fiches de suivi EPI tenues dans Excel (classeur « INVENTAIRE » du club) : un onglet par équipement, nommé par
/// son code (« A2 », « E 42 », « AS 01 », « S05 »), avec un tableau « Nom de l'intervenant | Nature de l'intervention | date »
/// par composant (détendeur : premier étage, deuxième étage, octopus ; sinon un seul tableau = équipement complet).
/// </summary>
public sealed partial class ServiceRevisions
{
    private const int CitationsMax = 6;

    /// <summary>
    /// Ajoute à l'historique de chaque équipement les interventions des fiches EPI (une par composant et par ligne). Ce n'est
    /// que de l'historique : ni échéance, ni état, ni révision en cours ne changent. Une intervention déjà présente (même
    /// équipement, composant, date, nature et intervenant) n'est pas recopiée : le même classeur peut être réimporté.
    /// Onglets sans tableau d'interventions ignorés ; onglet dont le code n'est pas dans l'inventaire et ligne sans date
    /// lisible cités dans le message. Action Révisions.
    /// </summary>
    public async Task<ResultatOperation> ImporterFichesEpiAsync(IReadOnlyList<FeuilleTableur> feuilles, CancellationToken ct = default)
    {
        if (gestion?.Exiger(ActionProtegee.Revisions) is { Reussi: false } refus)
        {
            return refus;
        }

        var inventaire = await equipements.ListerInventaireAsync(null, null, ct);

        int fiches = 0, ajoutees = 0, dejaLa = 0;
        var inconnues = new List<string>();
        var illisibles = new List<string>();
        foreach (var feuille in feuilles)
        {
            var tableaux = TableauxInterventions(feuille.Lignes);
            if (tableaux.Count == 0)
            {
                continue; // Accueil, listes, onglets de saisie : pas une fiche.
            }

            if (EquipementDeLaFiche(feuille.Nom, inventaire) is not { } ligne)
            {
                inconnues.Add(feuille.Nom.Trim());
                continue;
            }

            if (await equipements.ObtenirAsync(ligne.EquipementId, ct) is not { } equipement)
            {
                continue;
            }

            fiches++;
            var detendeur = ServiceInventaire.EstDetendeur(ligne.Categorie);
            var type = TypesPour(ligne.Categorie)[0];
            var existants = (await revisions.ListerControlesAsync(equipement.Id, ct))
                .Select(c => CleIntervention(c.Composant, c.DateControle, c.Nature, c.Intervenant))
                .ToHashSet();
            // Dates lisibles de chaque ligne Excel, tous tableaux confondus : une date mal tapée ou vide dans un tableau
            // reprend celle de la même ligne d'un autre tableau (« 17/104/2018 » à côté de 17/04/2018), si elle est unique.
            var datesDeLaLigne = tableaux.SelectMany(t => t.Lignes)
                .Select(l => (l.Numero, Jour: LireDateExcel(l.Date)))
                .Where(l => l.Jour is { } j && j <= horloge.AujourdHui)
                .GroupBy(l => l.Numero)
                .Where(g => g.Select(l => l.Jour).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Jour!.Value);
            var ajouteesIci = 0;
            foreach (var tableau in tableaux)
            {
                var composant = detendeur ? tableau.Composant : "";
                foreach (var (numero, intervenant, nature, date) in tableau.Lignes)
                {
                    var lue = LireDateExcel(date);
                    if (lue is null || lue > horloge.AujourdHui)
                    {
                        lue = datesDeLaLigne.TryGetValue(numero, out var voisine) ? voisine : null;
                    }

                    if (lue is not { } jour)
                    {
                        var citation = $"{equipement.Code} ligne {numero} (« {date.Trim()} »)";
                        if (!illisibles.Contains(citation))
                        {
                            illisibles.Add(citation);
                        }

                        continue;
                    }

                    var natureNette = Nettoyer(nature, 200);
                    var intervenantNet = Nettoyer(intervenant, 80);
                    if (!existants.Add(CleIntervention(composant, jour, natureNette, intervenantNet)))
                    {
                        dejaLa++;
                        continue;
                    }

                    await revisions.AjouterControleAsync(new Controle
                    {
                        EquipementId = equipement.Id,
                        Type = type,
                        DateControle = jour,
                        Resultat = ResultatDe(natureNette),
                        SaisiLe = horloge.MaintenantUtc,
                        Nature = natureNette,
                        Intervenant = intervenantNet,
                        Composant = composant,
                    }, ct);
                    ajouteesIci++;
                }
            }

            ajoutees += ajouteesIci;
        }

        if (fiches == 0)
        {
            return ResultatOperation.Echec(inconnues.Count > 0
                ? $"Aucune fiche ne correspond à un équipement de l'inventaire ({Citer(inconnues)})."
                : "Aucune fiche de suivi trouvée : chaque onglet doit contenir le tableau « Nom de l'intervenant / Nature de l'intervention / date ».");
        }

        if (ajoutees > 0)
        {
            await JournaliserAsync(ActionsJournal.FichesEpiImportees,
                $"{ajoutees} intervention{(ajoutees > 1 ? "s" : "")} reprise{(ajoutees > 1 ? "s" : "")} de {fiches} fiche{(fiches > 1 ? "s" : "")} de suivi EPI", ct);
        }

        var message = ajoutees == 0
            ? $"{fiches} fiche{(fiches > 1 ? "s" : "")} lue{(fiches > 1 ? "s" : "")} : aucune nouvelle intervention."
            : $"{ajoutees} intervention{(ajoutees > 1 ? "s" : "")} ajoutée{(ajoutees > 1 ? "s" : "")} à l'historique de {fiches} équipement{(fiches > 1 ? "s" : "")}.";
        if (dejaLa > 0)
        {
            message += $"\n{dejaLa} déjà dans l'historique : pas recopiée{(dejaLa > 1 ? "s" : "")}.";
        }

        if (inconnues.Count > 0)
        {
            message += $"\nOnglet{(inconnues.Count > 1 ? "s" : "")} sans équipement dans l'inventaire : {Citer(inconnues)}.";
        }

        if (illisibles.Count > 0)
        {
            message += $"\nDate illisible ou future, ligne{(illisibles.Count > 1 ? "s" : "")} ignorée{(illisibles.Count > 1 ? "s" : "")} : {Citer(illisibles)}.";
        }

        return ResultatOperation.Ok(message);
    }

    private static string Citer(List<string> elements) =>
        string.Join(", ", elements.Take(CitationsMax)) + (elements.Count > CitationsMax ? $" et {elements.Count - CitationsMax} autre{(elements.Count - CitationsMax > 1 ? "s" : "")}" : "");

    /// <summary>Un tableau d'interventions : composant (libellé au-dessus, dans la fiche) et lignes (n° Excel, intervenant, nature, date).</summary>
    internal sealed record TableauEpi(string Composant, IReadOnlyList<(int Numero, string Intervenant, string Nature, string Date)> Lignes);

    /// <summary>
    /// Tableaux d'une fiche : la ligne d'en-tête contient une ou plusieurs fois « Nom de l'intervenant », suivi de « Nature de
    /// l'intervention » et « date ». Le composant est le libellé écrit plus haut dans la même colonne (« Premier étage »,
    /// « Deuxième étage », « Octopus »). Lignes sans nature ni intervenant ignorées.
    /// </summary>
    internal static IReadOnlyList<TableauEpi> TableauxInterventions(IReadOnlyList<IReadOnlyList<string>> lignes)
    {
        var entete = -1;
        for (var i = 0; i < lignes.Count && entete < 0; i++)
        {
            if (lignes[i].Any(c => Simplifie(c).StartsWith("nom de l'intervenant", StringComparison.Ordinal)))
            {
                entete = i;
            }
        }

        if (entete < 0)
        {
            return [];
        }

        var titres = lignes[entete].Select(Simplifie).ToList();
        var tableaux = new List<TableauEpi>();
        for (var col = 0; col < titres.Count; col++)
        {
            if (!titres[col].StartsWith("nom de l'intervenant", StringComparison.Ordinal))
            {
                continue;
            }

            var fin = titres.FindIndex(col + 1, t => t.StartsWith("nom de l'intervenant", StringComparison.Ordinal));
            fin = fin < 0 ? titres.Count : fin;
            var colNature = titres.FindIndex(col + 1, fin - col - 1, t => t.StartsWith("nature", StringComparison.Ordinal));
            var colDate = titres.FindIndex(col + 1, fin - col - 1, t => t == "date" || t.StartsWith("date", StringComparison.Ordinal));
            if (colNature < 0 || colDate < 0)
            {
                continue;
            }

            var composant = "";
            for (var i = entete - 1; i >= 0 && composant.Length == 0; i--)
            {
                composant = RoleComposant(Cellule(lignes[i], col));
            }

            var interventions = new List<(int, string, string, string)>();
            for (var i = entete + 1; i < lignes.Count; i++)
            {
                var intervenant = Cellule(lignes[i], col);
                var nature = Cellule(lignes[i], colNature);
                var date = Cellule(lignes[i], colDate);
                if (nature.Trim().Length == 0 && intervenant.Trim().Length == 0)
                {
                    continue;
                }

                interventions.Add((i + 1, intervenant, nature, date));
            }

            tableaux.Add(new TableauEpi(composant.Length > 0 ? composant : "Premier étage", interventions));
        }

        return tableaux;
    }

    private static string RoleComposant(string libelle)
    {
        var s = Simplifie(libelle);
        return s.StartsWith("premier etage", StringComparison.Ordinal) ? "Premier étage"
            : s.StartsWith("deuxieme etage", StringComparison.Ordinal) || s.StartsWith("2e etage", StringComparison.Ordinal) ? "Deuxième étage"
            : s.StartsWith("octopus", StringComparison.Ordinal) ? "Octopus"
            : s.StartsWith("mano", StringComparison.Ordinal) ? "Manomètre"
            : "";
    }

    private static string Cellule(IReadOnlyList<string> ligne, int colonne) => colonne < ligne.Count ? ligne[colonne] ?? "" : "";

    private static string Simplifie(string texte) => ServiceAdherents.Simplifier(texte.Replace('’', '\''));

    /// <summary>« A 02 », « a2 » → « A2 » ; « E07 » → « E7 » : lettres puis nombre sans zéros devant.</summary>
    internal static string CodeSimplifie(string code)
    {
        var net = Regex.Replace(code.ToUpperInvariant(), "[^A-Z0-9]", "");
        var m = Regex.Match(net, "^([A-Z]+)0*([0-9]+)$");
        return m.Success ? m.Groups[1].Value + m.Groups[2].Value : net;
    }

    private static string CodeNet(string code) => Regex.Replace(code.ToUpperInvariant(), "[^A-Z0-9]", "");

    /// <summary>
    /// Équipement d'un onglet, d'après son nom : code identique (espaces et majuscules ignorés), sinon même code aux zéros
    /// près (« A2 » = A02) s'il n'y en a qu'un. Le code identique passe d'abord : « A1 » et « A01 » sont deux détendeurs du club.
    /// </summary>
    internal static LigneInventaire? EquipementDeLaFiche(string nomOnglet, IReadOnlyList<LigneInventaire> inventaire)
    {
        var net = CodeNet(nomOnglet);
        if (net.Length == 0)
        {
            return null;
        }

        if (inventaire.FirstOrDefault(l => CodeNet(l.Code) == net) is { } exact)
        {
            return exact;
        }

        var proches = inventaire.Where(l => CodeSimplifie(l.Code) == CodeSimplifie(net)).Take(2).ToList();
        return proches.Count == 1 ? proches[0] : null;
    }

    /// <summary>Date d'une cellule : numéro de série Excel (45307), ou texte jj/mm/aaaa (jj/mm/aa).</summary>
    internal static DateOnly? LireDateExcel(string cellule)
    {
        var t = cellule.Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serie))
        {
            return serie is >= 20000 and <= 80000 ? DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serie))) : null;
        }

        string[] formats = ["d/M/yyyy", "d/M/yy", "d.M.yyyy", "d-M-yyyy", "yyyy-MM-dd", "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss"];
        return DateTime.TryParseExact(t, formats, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var d)
            ? DateOnly.FromDateTime(d)
            : null;
    }

    /// <summary>« Contrôle visuel = RAS » : conforme ; « à surveiller » dans la nature : à surveiller ; sinon conforme.</summary>
    private static ResultatControle ResultatDe(string nature) =>
        Simplifie(nature).Contains("a surveiller", StringComparison.Ordinal) ? ResultatControle.ASurveiller : ResultatControle.Conforme;

    private static string Nettoyer(string texte, int max)
    {
        var t = Regex.Replace(texte, @"\s+", " ").Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }

    private static string CleIntervention(string composant, DateOnly date, string nature, string intervenant) =>
        $"{Simplifie(composant)}|{date:yyyy-MM-dd}|{Simplifie(Nettoyer(nature, 200))}|{Simplifie(Nettoyer(intervenant, 80))}";
}
