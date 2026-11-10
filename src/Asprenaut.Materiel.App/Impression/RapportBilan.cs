using static Asprenaut.Materiel.App.Impression.DocumentPdf;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Impression;

/// <summary>
/// Rapport PDF du bilan de saison pour l'assemblée générale. Aucune bibliothèque : la page est dessinée en GDI+
/// et envoyée à l'imprimante « Microsoft Print to PDF » de Windows, qui écrit le fichier sans rien demander.
/// Toujours en couleurs du thème clair (papier blanc), quel que soit le thème de l'écran.
/// Coordonnées en centièmes de pouce (unité des pages imprimées) ; A4 = 827 × 1169.
/// </summary>
public static class RapportBilan
{
    /// <summary>Écrit le rapport dans <paramref name="chemin"/> (remplacé s'il existe).</summary>
    public static void Enregistrer(Bilan bilan, string nomClub, string responsable, string chemin) =>
        DocumentPdf.Enregistrer($"Bilan de saison {bilan.Saison.Libelle}", $"{nomClub} · bilan de saison {bilan.Saison.Libelle}", Blocs(bilan, nomClub, responsable), chemin);

    private static List<BlocPdf> Blocs(Bilan bilan, string nomClub, string responsable)
    {
        var blocs = new List<BlocPdf>();

        // --- Titre ---
        blocs.Add(new BlocPdf(96, (g, x, y, l) =>
        {
            Texte(g, nomClub.ToUpper(Libelles.Culture), Theme.PoliceTitre(9f), new RectangleF(x, y, l, 18), P.Accent);
            Texte(g, $"Bilan de saison {bilan.Saison.Libelle}", Theme.PoliceGrasse(20f), new RectangleF(x, y + 18, l, 40), P.Texte);
            var precision = $"Rapport pour l'assemblée générale · édité le {DateTime.Today.ToString("d MMMM yyyy", Libelles.Culture)}";
            if (bilan.Saison.EnCours)
            {
                precision += " · saison en cours";
            }

            if (!string.IsNullOrWhiteSpace(responsable))
            {
                precision += $" · responsable matériel : {responsable.Trim()}";
            }

            Texte(g, precision, Theme.Police(9f), new RectangleF(x, y + 60, l, 18), P.TexteSecondaire);
        }));

        // --- Grands chiffres ---
        Color[] couleurs = [P.Texte, P.Accent, P.Depart, P.Avertissement];
        blocs.Add(new BlocPdf(100, (g, x, y, l) =>
        {
            const float ecart = 10;
            var lc = (l - (3 * ecart)) / 4;
            for (var i = 0; i < bilan.Chiffres.Count; i++)
            {
                var c = bilan.Chiffres[i];
                var r = new RectangleF(x + (i * (lc + ecart)), y, lc, 80);
                Cadre(g, r, P.SurfaceCreuse, P.Bordure);
                Texte(g, c.Libelle, Theme.PoliceTitre(8.25f), new RectangleF(r.X + 10, r.Y + 8, r.Width - 16, 16), P.TexteSecondaireClair);
                Texte(g, c.Valeur, Theme.PoliceGrasse(18f), new RectangleF(r.X + 10, r.Y + 24, r.Width - 16, 32), couleurs[i]);
                Texte(g, c.Precision, Theme.Police(7.5f), new RectangleF(r.X + 10, r.Y + 56, r.Width - 16, 20), P.TexteSecondaire, retour: true);
            }
        }));

        // --- Sorties par mois ---
        blocs.Add(TitreSection($"Sorties par mois · {bilan.NoteMois}"));
        blocs.Add(new BlocPdf(150, (g, x, y, l) =>
        {
            var max = Math.Max(1, bilan.Mois.Max(m => m.Sorties));
            const float ecart = 8;
            var lc = (l - ((bilan.Mois.Count - 1) * ecart)) / bilan.Mois.Count;
            var basBarres = y + 118;
            for (var i = 0; i < bilan.Mois.Count; i++)
            {
                var m = bilan.Mois[i];
                var cx = x + (i * (lc + ecart));
                var h = m.Sorties == 0 ? 2 : Math.Max(4, 92f * m.Sorties / max);
                using (var pinceau = new SolidBrush(m.Sorties == max && m.Sorties > 0 ? P.Accent : Theme.Sur(P.Surface, P.Accent, 0.45f)))
                {
                    g.FillRectangle(pinceau, cx + (lc * 0.15f), basBarres - h, lc * 0.7f, h);
                }

                if (m.Sorties > 0)
                {
                    Texte(g, m.Sorties.ToString(Libelles.Culture), Theme.PoliceTitre(8.25f), new RectangleF(cx, basBarres - h - 16, lc, 16), P.Texte, StringAlignment.Center);
                }

                Texte(g, m.Libelle, Theme.Police(8.25f), new RectangleF(cx, basBarres + 4, lc, 16), P.TexteSecondaire, StringAlignment.Center);
            }
        }));

        // --- Par type ---
        blocs.Add(TitreSection("Par type de sortie"));
        if (bilan.Types.Count == 0)
        {
            blocs.Add(LigneVide("Aucune sortie."));
        }

        var maxType = Math.Max(1, bilan.Types.Count == 0 ? 1 : bilan.Types.Max(t => t.Sorties));
        foreach (var t in bilan.Types)
        {
            blocs.Add(new BlocPdf(24, (g, x, y, l) =>
            {
                Texte(g, t.Libelle, Theme.PoliceTitre(9f), new RectangleF(x, y + 2, 160, 18), P.Texte);
                var largeur = l - 160 - 90;
                Barre(g, new RectangleF(x + 160, y + 7, largeur, 8), (double)t.Sorties / maxType, P.Accent);
                Texte(g, Libelles.Pluriel(t.Sorties, "sortie"), Theme.Police(9f), new RectangleF(x + l - 84, y + 2, 84, 18), P.TexteSecondaireClair, StringAlignment.Far);
            }));
        }

        // --- Tension sur le stock ---
        blocs.Add(TitreSection("Tension sur le stock"));
        if (bilan.Tension.Count == 0)
        {
            blocs.Add(LigneVide("Aucun matériel sorti."));
        }
        else
        {
            blocs.Add(new BlocPdf(36, (g, x, y, l) =>
            {
                Texte(g, "Part du stock sortie lors des sorties les plus chargées. À 100 %, il n'en restait plus au local.", Theme.Police(8.25f), new RectangleF(x, y, l, 16), P.TexteSecondaire);
                EnteteTableau(g, x, y + 16, l, ["Matériel", "Stock", "Pic d'occupation", "Ruptures"], [0, 0.40f, 0.52f, 0.86f]);
            }, GarderAvecSuivant: true));
            foreach (var t in bilan.Tension)
            {
                blocs.Add(new BlocPdf(24, (g, x, y, l) =>
                {
                    Filet(g, x, y, l);
                    var couleur = t.Pourcentage >= 100 ? P.Erreur : t.Pourcentage >= 90 ? P.Avertissement : P.Accent;
                    Texte(g, t.Libelle, Theme.PoliceTitre(9f), new RectangleF(x, y + 4, (l * 0.40f) - 6, 18), P.Texte);
                    Texte(g, t.Stock.ToString(Libelles.Culture), Theme.Police(9f), new RectangleF(x + (l * 0.40f), y + 4, l * 0.12f, 18), P.TexteDoux);
                    Barre(g, new RectangleF(x + (l * 0.52f), y + 9, (l * 0.34f) - 50, 7), t.Pourcentage / 100.0, couleur);
                    Texte(g, $"{t.Pourcentage} %", Theme.PoliceGrasse(8.25f), new RectangleF(x + (l * 0.86f) - 48, y + 4, 42, 18), couleur, StringAlignment.Far);
                    Texte(g, t.Ruptures == 0 ? "—" : $"{t.Ruptures} fois", Theme.PoliceTitre(9f), new RectangleF(x + (l * 0.86f), y + 4, l * 0.14f, 18), t.Ruptures == 0 ? P.TexteSecondaire : P.Erreur);
                }));
            }
        }

        // --- Classements ---
        blocs.Add(TitreSection("Les plus utilisés"));
        AjouterEquipements(blocs, bilan.PlusUtilises, "Aucun matériel sorti.", rang: true);
        blocs.Add(TitreSection($"Peu ou pas sortis (moins de {ServiceBilan.SeuilPeuSorti} sorties)"));
        AjouterEquipements(blocs, bilan.PeuSortis, $"Tout le matériel est sorti au moins {ServiceBilan.SeuilPeuSorti} fois.", rang: false);

        // --- Pertes ---
        blocs.Add(TitreSection("Manquants"));
        if (bilan.Pertes.Count == 0)
        {
            blocs.Add(LigneVide("Aucun manquant cette saison : tout est rentré."));
        }

        foreach (var p in bilan.Pertes)
        {
            blocs.Add(new BlocPdf(24, (g, x, y, l) =>
            {
                Filet(g, x, y, l);
                Texte(g, p.Libelle.Length > 0 ? $"{p.Libelle} · {p.Code}" : p.Code, Theme.PoliceTitre(9f), new RectangleF(x, y + 4, l * 0.40f, 18), P.Texte);
                // Retrouvé : la date de retour plutôt que le moniteur (la ligne serait coupée).
                var detail = $"{p.Sortie} du {Libelles.Date(p.DateSortie)}" + (p.RetrouveLe is { } le
                    ? $" · retrouvé le {Libelles.Date(le)}"
                    : string.IsNullOrWhiteSpace(p.Moniteur) ? "" : $" · {p.Moniteur}");
                Texte(g, detail, Theme.Police(8.25f), new RectangleF(x + (l * 0.40f), y + 5, (l * 0.48f) - 6, 18), P.TexteSecondaireClair);
                Texte(g, p.Retrouve ? "Retrouvé" : "Perdu", Theme.PoliceGrasse(8.25f), new RectangleF(x + (l * 0.88f), y + 4, l * 0.12f, 18), p.Retrouve ? P.Retour : P.Erreur, StringAlignment.Far);
            }));
        }

        // --- Contrôles ---
        blocs.Add(TitreSection($"Contrôles faits · {Libelles.Pluriel(bilan.NombreRevisions, "contrôle")}"));
        if (bilan.Revisions.Count == 0)
        {
            blocs.Add(LigneVide("Aucun contrôle enregistré cette saison."));
        }

        foreach (var r in bilan.Revisions)
        {
            blocs.Add(new BlocPdf(24, (g, x, y, l) =>
            {
                Filet(g, x, y, l);
                Texte(g, r.Libelle, Theme.PoliceTitre(9f), new RectangleF(x, y + 4, l - 80, 18), P.Texte);
                Texte(g, r.Nombre.ToString(Libelles.Culture), Theme.PoliceGrasse(9f), new RectangleF(x + l - 80, y + 4, 80, 18), P.Avertissement, StringAlignment.Far);
            }));
        }

        return blocs;
    }

    private static void AjouterEquipements(List<BlocPdf> blocs, IReadOnlyList<UtilisationBilan> liste, string vide, bool rang)
    {
        if (liste.Count == 0)
        {
            blocs.Add(LigneVide(vide));
        }

        for (var i = 0; i < liste.Count; i++)
        {
            var u = liste[i];
            var n = i + 1;
            blocs.Add(new BlocPdf(22, (g, x, y, l) =>
            {
                Filet(g, x, y, l);
                var dx = rang ? 28f : 0;
                if (rang)
                {
                    Texte(g, n.ToString(Libelles.Culture), Theme.PoliceGrasse(8.25f), new RectangleF(x, y + 3, 22, 18), P.Accent);
                }

                Texte(g, u.Libelle, Theme.PoliceTitre(9f), new RectangleF(x + dx, y + 3, (l * 0.55f) - dx, 18), P.Texte);
                Texte(g, u.Code, Theme.PoliceCode(8.25f), new RectangleF(x + (l * 0.55f), y + 4, l * 0.25f, 18), P.Accent);
                Texte(g, Libelles.Pluriel(u.Sorties, "sortie"), Theme.Police(9f), new RectangleF(x + (l * 0.80f), y + 3, l * 0.20f, 18), P.TexteDoux, StringAlignment.Far);
            }));
        }
    }
}
