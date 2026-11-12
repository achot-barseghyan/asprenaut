using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;
using static Asprenaut.Materiel.App.Impression.DocumentPdf;

namespace Asprenaut.Materiel.App.Impression;

/// <summary>
/// Fiche de suivi EPI imprimée (bouton « Imprimer la fiche ») : identité et statut de l'équipement, ses composants
/// (modèle, numéro de série, dates), puis toutes ses interventions (date, composant, nature, intervenant, type, résultat).
/// </summary>
public static class FicheSuiviPdf
{
    private static readonly float[] Colonnes = [0, 0.12f, 0.31f, 0.62f, 0.79f, 0.89f];

    public static void Enregistrer(FicheSuiviEquipement fiche, string nomClub, DateOnly edition, string chemin)
    {
        var e = fiche.Vie.Equipement;
        DocumentPdf.Enregistrer($"Fiche de suivi {e.Code}", $"{nomClub} · fiche de suivi EPI {e.Code} du {Libelles.Date(edition)}", Blocs(fiche, nomClub, edition), chemin);
    }

    private static List<BlocPdf> Blocs(FicheSuiviEquipement fiche, string nomClub, DateOnly edition)
    {
        var vie = fiche.Vie;
        var e = vie.Equipement;
        var precisions = new List<string> { vie.Categorie.Length > 0 ? vie.Categorie : "Équipement" };
        if (!string.IsNullOrWhiteSpace(e.Taille))
        {
            precisions.Add($"taille {e.Taille.Trim()}");
        }

        precisions.Add($"éditée le {edition.ToString("d MMMM yyyy", Libelles.Culture)}");
        var blocs = new List<BlocPdf>
        {
            Titre(nomClub, $"Fiche de suivi EPI · {e.Code}", string.Join(" · ", precisions)),
            new(30, (g, x, y, l) =>
            {
                Texte(g, fiche.Titre, Theme.PoliceTitre(11.25f), new RectangleF(x, y, l, 20), P.Texte);
            }),
            new(26, (g, x, y, l) =>
            {
                var statut = vie.PrecisionStatut.Trim().Length > 0 ? $"{vie.LibelleStatut} · {vie.PrecisionStatut.Trim()}" : vie.LibelleStatut;
                var echeance = vie.Echeance == "—" ? "pas de révision prévue" : $"prochaine révision {vie.Echeance}";
                Texte(g, $"{statut} · {Libelles.De(e.Etat).ToLower(Libelles.Culture)} · {echeance}", Theme.Police(9f), new RectangleF(x, y, l, 18), P.TexteSecondaireClair);
            }),
            TitreSection(fiche.ParComposant ? "Composants" : "Identification"),
        };

        foreach (var c in fiche.Composants)
        {
            blocs.Add(new BlocPdf(62, (g, x, y, l) =>
            {
                Cadre(g, new RectangleF(x, y + 2, l, 54), P.Surface, P.Bordure);
                Texte(g, c.Role.ToUpper(Libelles.Culture), Theme.PoliceTitre(7.5f), new RectangleF(x + 12, y + 8, l * 0.4f, 14), P.TexteSecondaire);
                Texte(g, c.Modele, Theme.PoliceTitre(10.5f), new RectangleF(x + 12, y + 22, l * 0.4f, 18), P.Texte);
                Texte(g, c.NumeroSerie.Length > 0 ? $"N° de série {c.NumeroSerie}" : "N° de série non renseigné", Theme.Police(8.25f),
                    new RectangleF(x + 12, y + 40, l * 0.4f, 14), P.TexteSecondaireClair);
                var lt = (l * 0.58f) / Math.Max(1, c.Tuiles.Count);
                for (var i = 0; i < c.Tuiles.Count; i++)
                {
                    var tx = x + (l * 0.42f) + (i * lt);
                    Texte(g, c.Tuiles[i].Libelle, Theme.Police(7.5f), new RectangleF(tx, y + 14, lt - 6, 14), P.TexteSecondaire);
                    Texte(g, c.Tuiles[i].Valeur, Theme.PoliceTitre(9f), new RectangleF(tx, y + 30, lt - 6, 16), P.Texte);
                }
            }));
        }

        blocs.Add(TitreSection("Interventions"));
        blocs.Add(new BlocPdf(22, (g, x, y, l) =>
            EnteteTableau(g, x, y, l, ["Date", "Composant", "Nature", "Intervenant", "Type", "Résultat"], Colonnes), GarderAvecSuivant: true));
        if (fiche.Interventions.Count == 0)
        {
            blocs.Add(LigneVide("Aucune intervention enregistrée."));
        }

        foreach (var i in fiche.Interventions)
        {
            blocs.Add(new BlocPdf(20, (g, x, y, l) =>
            {
                var couleur = i.ResultatBrut switch
                {
                    ResultatControle.Conforme => P.Retour,
                    ResultatControle.ASurveiller => P.Avertissement,
                    ResultatControle.Refuse => P.Erreur,
                    _ => P.TexteDoux,
                };
                string[] valeurs = [Libelles.Date(i.Date), i.Composant, i.Nature, i.Intervenant, Ecrans.EcranFicheSuivi.LibelleType(i.Type)];
                for (var c = 0; c < valeurs.Length; c++)
                {
                    Texte(g, valeurs[c], Theme.Police(8.25f), new RectangleF(x + (l * Colonnes[c]), y + 3, (l * (Colonnes[c + 1] - Colonnes[c])) - 4, 16), c == 2 ? P.Texte : P.TexteDoux);
                }

                Texte(g, i.Resultat, Theme.PoliceGrasse(8.25f), new RectangleF(x + (l * Colonnes[5]), y + 3, l * (1 - Colonnes[5]), 16), couleur);
            }));
        }

        return blocs;
    }
}
