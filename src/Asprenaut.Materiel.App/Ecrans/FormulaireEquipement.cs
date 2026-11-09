using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtre « Ajouter un équipement », utilisée par l'inventaire et par le départ (code scanné inconnu).
/// La validation est faite par <see cref="ServiceInventaire.CreerAsync"/>.
/// </summary>
public static class FormulaireEquipement
{
    /// <summary>
    /// Ouvre la fenêtre. <paramref name="code"/> pré-remplit le code (étiquette scannée) ; sinon un code est proposé
    /// selon la catégorie. Retourne le code créé, ou null si l'utilisateur a annulé.
    /// </summary>
    public static async Task<string?> OuvrirAsync(IWin32Window? proprietaire, ServiceInventaire inventaire, string? code = null, long? categorieId = null)
    {
        var categories = await inventaire.ListerCategoriesAsync();
        if (categorieId is null && !string.IsNullOrWhiteSpace(code))
        {
            categorieId = await inventaire.DevinerCategorieAsync(code); // GIL-010 → Gilets.
        }
        var categorie = new ListeDeroulante { AccessibleName = "Catégorie" };
        foreach (var c in categories.OrderBy(c => c.Id))
        {
            categorie.Items.Add(new Choix<long>(c.Id, c.Nom));
        }

        var champCode = new ChampSaisie { Valeur = code ?? "", TexteIndicatif = "BLOC-035" };
        champCode.Champ.Font = Theme.PoliceCode(11.25f);
        champCode.Champ.CharacterCasing = CharacterCasing.Upper;
        champCode.Champ.AccessibleName = "Code de l'étiquette";
        var nom = new ChampSaisie { TexteIndicatif = "Ex. : Bloc 12 L acier" };
        nom.Champ.AccessibleName = "Nom";
        var taille = new ChampSaisie { TexteIndicatif = "M, 42-43, 12 L…" };
        taille.Champ.AccessibleName = "Taille";
        var revision = new ChampDate { AccessibleName = "Prochaine révision" };
        var requalification = new ChampDate { AccessibleName = "Prochaine requalification" };
        var notes = new ChampSaisie { TexteIndicatif = "Numéro de série, marque…" };
        notes.Champ.AccessibleName = "Notes";
        var aide = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(460, 0);

        // Changement de catégorie : nouveau code proposé (si l'utilisateur ne l'a pas tapé), requalification pour les blocs.
        var dernierePropose = "";
        async Task CategorieChangeeAsync()
        {
            if (categorie.SelectedItem is not Choix<long> choix)
            {
                return;
            }

            var bloc = ServiceRevisions.EstBloc(choix.Texte);
            requalification.Enabled = bloc;
            if (!bloc)
            {
                requalification.Date = null;
            }

            aide.Text = bloc
                ? "Bloc : indiquez la date de la prochaine TIV et celle de la prochaine requalification, si vous les connaissez."
                : "Prochaine révision : date conseillée par le fabricant, si elle existe.";
            if (champCode.Valeur.Trim().Length == 0 || champCode.Valeur == dernierePropose)
            {
                dernierePropose = await inventaire.ProposerCodeAsync(choix.Valeur);
                champCode.Valeur = dernierePropose;
            }
        }

        categorie.SelectedIndexChanged += async (_, _) => await CategorieChangeeAsync();
        var initiale = categorieId is { } id ? categorie.Items.OfType<Choix<long>>().FirstOrDefault(c => c.Valeur == id) : null;
        categorie.SelectedItem = initiale ?? categorie.Items.OfType<Choix<long>>().FirstOrDefault();

        string? cree = null;
        using var fenetre = new FenetreFormulaire("Ajouter un équipement", "Ajouter", async () =>
        {
            if (!revision.Valide || !requalification.Valide)
            {
                return ResultatOperation.Echec("Date invalide : écrivez-la comme 08/11/2027, ou laissez le champ vide.");
            }

            var resultat = await inventaire.CreerAsync(new Equipement
            {
                Code = champCode.Valeur,
                Libelle = nom.Valeur,
                CategorieId = (categorie.SelectedItem as Choix<long>)?.Valeur ?? 0,
                Taille = taille.Valeur,
                DateProchaineRevision = revision.Date,
                DateRequalification = requalification.Date,
                Notes = notes.Valeur,
            });
            if (resultat.Reussi)
            {
                cree = Core.Saisie.CodeScanne.Nettoyer(champCode.Valeur, null);
            }

            return resultat;
        });
        fenetre.DeuxChamps("Catégorie", categorie, "Code de l'étiquette", champCode);
        fenetre.Champ("Nom", nom);
        fenetre.DeuxChamps("Taille (facultatif)", taille, "Prochaine révision / TIV", revision);
        fenetre.DeuxChamps("Prochaine requalification", requalification, "Notes (facultatif)", notes);
        fenetre.Ajouter(aide);
        fenetre.Shown += (_, _) => nom.Champ.Focus();

        return fenetre.ShowDialog(proprietaire) == DialogResult.OK ? cree : null;
    }
}
