using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;
using static Asprenaut.Materiel.App.Impression.DocumentPdf;

namespace Asprenaut.Materiel.App.Impression;

/// <summary>
/// Registre des contrôles (bouton « Registre PDF » de Révisions) : pour chaque bloc puis chaque équipement suivi,
/// ses prochaines échéances et tous ses contrôles (date, contrôle, résultat, inspecteur, notes). Signature en fin de document.
/// </summary>
public static class RegistrePdf
{
    private static readonly float[] Colonnes = [0, 0.13f, 0.36f, 0.52f, 0.72f];

    public static void Enregistrer(RegistreControles registre, string nomClub, string responsable, string chemin) =>
        DocumentPdf.Enregistrer("Registre des contrôles", $"{nomClub} · registre des contrôles du {Libelles.Date(registre.Edition)}", Blocs(registre, nomClub, responsable), chemin);

    private static List<BlocPdf> Blocs(RegistreControles registre, string nomClub, string responsable)
    {
        var blocs = new List<BlocPdf>
        {
            Titre(nomClub, "Registre des contrôles",
                $"TIV, requalifications et révisions · édité le {registre.Edition.ToString("d MMMM yyyy", Libelles.Culture)} · "
                + $"{Libelles.Pluriel(registre.Blocs.Count, "bloc")}, {Libelles.Pluriel(registre.NombreControles, "contrôle")}"),
        };

        blocs.Add(TitreSection("Blocs · TIV et requalifications"));
        if (registre.Blocs.Count == 0)
        {
            blocs.Add(LigneVide("Aucun bloc dans l'inventaire."));
        }

        foreach (var fiche in registre.Blocs)
        {
            AjouterFiche(blocs, fiche, bloc: true);
        }

        blocs.Add(TitreSection("Autre matériel suivi · révisions"));
        if (registre.Autres.Count == 0)
        {
            blocs.Add(LigneVide("Aucun autre matériel avec une révision."));
        }

        foreach (var fiche in registre.Autres)
        {
            AjouterFiche(blocs, fiche, bloc: false);
        }

        // Visa du responsable.
        blocs.Add(new BlocPdf(90, (g, x, y, l) =>
        {
            Texte(g, $"Arrêté le {Libelles.Date(registre.Edition)}.", Theme.Police(9f), new RectangleF(x, y + 30, l, 18), P.TexteSecondaireClair);
            var qui = string.IsNullOrWhiteSpace(responsable) ? "Responsable du matériel" : $"Responsable du matériel : {responsable.Trim()}";
            Texte(g, qui, Theme.PoliceTitre(9f), new RectangleF(x + (l * 0.55f), y + 30, l * 0.45f, 18), P.Texte);
            Texte(g, "Signature :", Theme.Police(9f), new RectangleF(x + (l * 0.55f), y + 52, l * 0.45f, 18), P.TexteSecondaireClair);
        }));
        return blocs;
    }

    private static void AjouterFiche(List<BlocPdf> blocs, FicheRegistre f, bool bloc)
    {
        // En-tête de l'équipement + en-tête du tableau, gardés avec le premier contrôle.
        blocs.Add(new BlocPdf(50, (g, x, y, l) =>
        {
            Filet(g, x, y + 8, l);
            Texte(g, f.Code, Theme.PoliceCode(9.75f), new RectangleF(x, y + 13, 90, 18), P.Accent);
            var nom = f.Etat == EtatEquipement.EnService ? f.Libelle : $"{f.Libelle} · {Libelles.De(f.Etat).ToLower(Libelles.Culture)}";
            Texte(g, nom, Theme.PoliceTitre(9.75f), new RectangleF(x + 92, y + 12, (l * 0.55f) - 92, 18), P.Texte);
            Texte(g, Echeances(f, bloc), Theme.Police(8.25f), new RectangleF(x + (l * 0.55f), y + 14, l * 0.45f, 16), P.TexteSecondaireClair, StringAlignment.Far);
            EnteteTableau(g, x, y + 32, l, ["Date", "Contrôle", "Résultat", "Inspecteur", "Notes"], Colonnes);
        }, GarderAvecSuivant: true));

        if (f.Controles.Count == 0)
        {
            blocs.Add(LigneVide("Aucun contrôle enregistré."));
            return;
        }

        foreach (var c in f.Controles)
        {
            blocs.Add(new BlocPdf(20, (g, x, y, l) =>
            {
                var couleur = c.ResultatBrut switch
                {
                    ResultatControle.Conforme => P.Retour,
                    ResultatControle.ASurveiller => P.Avertissement,
                    _ => P.Erreur,
                };
                Texte(g, Libelles.Date(c.Date), Theme.Police(8.25f), new RectangleF(x, y + 3, l * Colonnes[1], 16), P.Texte);
                Texte(g, c.Controle, Theme.Police(8.25f), new RectangleF(x + (l * Colonnes[1]), y + 3, l * (Colonnes[2] - Colonnes[1]) - 4, 16), P.Texte);
                Texte(g, c.Resultat, Theme.PoliceGrasse(8.25f), new RectangleF(x + (l * Colonnes[2]), y + 3, l * (Colonnes[3] - Colonnes[2]) - 4, 16), couleur);
                Texte(g, c.Inspecteur.Length > 0 ? c.Inspecteur : "—", Theme.Police(8.25f), new RectangleF(x + (l * Colonnes[3]), y + 3, l * (Colonnes[4] - Colonnes[3]) - 4, 16), P.TexteDoux);
                Texte(g, c.Notes, Theme.Police(8.25f), new RectangleF(x + (l * Colonnes[4]), y + 3, l * (1 - Colonnes[4]), 16), P.TexteSecondaireClair);
            }));
        }
    }

    private static string Echeances(FicheRegistre f, bool bloc)
    {
        static string Mois(DateOnly? d) => d is { } v ? v.ToString("MM/yyyy", Libelles.Culture) : "—";
        if (f.Etat == EtatEquipement.Reforme)
        {
            return "plus de contrôle à faire";
        }

        return bloc
            ? $"Prochaine TIV {Mois(f.ProchaineRevision)} · requalification {Mois(f.ProchaineRequalification)}"
            : $"Prochaine révision {Mois(f.ProchaineRevision)}";
    }
}
