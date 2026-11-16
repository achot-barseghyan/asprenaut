using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtre « Planifier une séance TIV » / « Modifier la séance TIV » : date, heure, lieu, inspecteurs (cases, parmi les
/// moniteurs inspecteurs TIV actifs), note. Ouverte depuis Révisions (en-tête de « Réparation interne ») et depuis
/// Paramètres › Configuration du club › Séance TIV. Le code gestionnaire est demandé avant (action Révisions).
/// </summary>
public static class FormulaireSeanceTiv
{
    /// <summary>Vrai si la séance a été enregistrée.</summary>
    public static async Task<bool> OuvrirAsync(IWin32Window? proprietaire, ServiceRevisions revisions, SeanceTiv? existante)
    {
        var inspecteurs = await revisions.InspecteursTivAsync();
        var date = new ChampDate { Date = existante?.DateSeance ?? DateOnly.FromDateTime(DateTime.Today).AddDays(7), AccessibleName = "Date de la séance" };
        var heure = new ChampSaisie { Valeur = existante?.Heure ?? "9:00", TexteIndicatif = "9:00" };
        heure.Champ.AccessibleName = "Heure";
        heure.Champ.MaxLength = ServiceRevisions.LongueurHeureSeance;
        var lieu = new ChampSaisie { Valeur = existante?.Lieu ?? "Local du club", TexteIndicatif = "Local du club" };
        lieu.Champ.AccessibleName = "Lieu";
        lieu.Champ.MaxLength = ServiceRevisions.LongueurLieuSeance;
        var note = new ChampSaisie { Valeur = existante?.Note ?? "", TexteIndicatif = "Ex. : apporter les blocs vides, robinets fermés" };
        note.Champ.AccessibleName = "Note";
        note.Champ.MaxLength = ServiceRevisions.LongueurNoteSeance;

        // Nouvelle séance : tous les inspecteurs cochés ; modification : ceux de la séance.
        var cases = inspecteurs.Select(m => (m.Id, Case: new CaseOption(m.NomComplet, Detail(m))
        {
            Checked = existante is null || existante.InspecteurIds.Contains(m.Id),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
        })).ToList();

        Task<ResultatOperation> Valider()
        {
            if (date.Date is not { } d)
            {
                return Task.FromResult(ResultatOperation.Echec("Date invalide : écrivez-la comme 21/11/2026."));
            }

            var saisie = new SaisieSeance(d, heure.Valeur, lieu.Valeur, cases.Where(c => c.Case.Checked).Select(c => c.Id).ToList(), note.Valeur);
            return existante is null ? revisions.PlanifierSeanceAsync(saisie) : revisions.ModifierSeanceAsync(existante.Id, saisie);
        }

        using var fenetre = new FenetreFormulaire(existante is null ? "Planifier une séance TIV" : "Modifier la séance TIV",
            existante is null ? "Planifier" : "Enregistrer", Valider);
        fenetre.Champ("Date *", date);
        fenetre.DeuxChamps("Heure", heure, "Lieu", lieu);
        fenetre.Ajouter(FenetreFormulaire.LibelleChamp("Inspecteurs TIV présents"));
        if (cases.Count == 0)
        {
            var aucun = Ui.Libelle("Aucun enseignant n'est inspecteur TIV : cochez « Inspecteur TIV » sur sa fiche (Membres › Enseignants).",
                Theme.Police(9.75f), Theme.TexteSecondaire);
            aucun.MaximumSize = new Size(460, 0);
            aucun.Margin = new Padding(0, 0, 0, 14);
            fenetre.Ajouter(aucun);
        }

        foreach (var (_, c) in cases)
        {
            fenetre.Ajouter(c);
        }

        fenetre.Champ("Note (facultatif)", note);
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK;
    }

    private static string Detail(Moniteur m) =>
        string.Join(" · ", new[] { m.Niveau, m.Telephone }.Where(t => !string.IsNullOrWhiteSpace(t)));
}
