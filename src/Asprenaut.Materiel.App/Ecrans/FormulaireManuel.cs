using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtres « Ajouter un manuel » (choix du PDF, nombre de pages lu par Windows) et « Modifier le manuel ».
/// La validation est faite par <see cref="ServiceManuels"/> ; ici on ne fait que lire les champs.
/// </summary>
public static class FormulaireManuel
{
    /// <summary>Ouvre la fenêtre d'ajout ; retourne l'id du manuel créé, ou null si l'utilisateur a annulé.</summary>
    public static async Task<long?> AjouterAsync(IWin32Window? proprietaire, ServiceManuels manuels)
    {
        var champs = new Champs(await manuels.ListerCategoriesAsync(), null);
        string? chemin = null;
        var pages = 0;

        var fichier = new ChampSaisie { TexteIndicatif = "Aucun fichier choisi", Margin = Padding.Empty };
        fichier.Champ.ReadOnly = true;
        fichier.Champ.Font = Theme.Police(10f);
        fichier.Champ.AccessibleName = "Fichier PDF";
        var parcourir = new BoutonArrondi("Choisir le PDF…", StyleBouton.Secondaire) { Margin = new Padding(10, 0, 0, 0) };
        var ligneFichier = Ui.Colonnes((fichier, SizeType.Percent, 100), (parcourir, SizeType.AutoSize, 0));

        long? cree = null;
        using var fenetre = new FenetreFormulaire("Ajouter un manuel", "Ajouter", async () =>
        {
            if (chemin is null)
            {
                return ResultatOperation.Echec("Choisissez le fichier PDF du manuel.");
            }

            if (champs.Lire() is not { } fiche)
            {
                return ResultatOperation.Echec(champs.Erreur);
            }

            var resultat = await manuels.AjouterAsync(fiche, chemin, pages);
            cree = resultat.Id;
            return resultat;
        });

        parcourir.Click += async (_, _) =>
        {
            using var choix = new OpenFileDialog { Title = "Choisir la notice du fabricant", Filter = "Fichiers PDF (*.pdf)|*.pdf", CheckFileExists = true };
            if (choix.ShowDialog(fenetre) != DialogResult.OK)
            {
                return;
            }

            var lues = await LecteurPdf.CompterPagesAsync(choix.FileName);
            if (lues == 0)
            {
                Dialogues.Erreur(fenetre, "Ce fichier PDF ne peut pas être lu (abîmé ou protégé par un mot de passe).");
                return;
            }

            chemin = choix.FileName;
            pages = lues;
            fichier.Valeur = $"{Path.GetFileName(chemin)} · {lues} pages";
            if (champs.Titre.Valeur.Trim().Length == 0)
            {
                champs.Titre.Valeur = Path.GetFileNameWithoutExtension(chemin).Replace('_', ' ');
            }
        };

        fenetre.Champ("Fichier PDF", ligneFichier);
        champs.Placer(fenetre);
        fenetre.Shown += (_, _) => parcourir.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK ? cree : null;
    }

    /// <summary>Ouvre la fiche d'un manuel ; vrai s'il a été modifié.</summary>
    public static async Task<bool> ModifierAsync(IWin32Window? proprietaire, ServiceManuels manuels, Manuel manuel)
    {
        var champs = new Champs(await manuels.ListerCategoriesAsync(), manuel);
        using var fenetre = new FenetreFormulaire($"Modifier « {manuel.Titre} »", "Enregistrer", async () =>
            champs.Lire() is { } fiche ? await manuels.ModifierAsync(manuel.Id, fiche) : ResultatOperation.Echec(champs.Erreur));
        var aide = Ui.Libelle($"{manuel.Fichier} · {manuel.NbPages} pages", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.Margin = new Padding(0, 0, 0, 14);
        fenetre.Ajouter(aide);
        champs.Placer(fenetre);
        fenetre.Shown += (_, _) => champs.Marque.Champ.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK;
    }

    /// <summary>Champs communs aux deux fenêtres.</summary>
    private sealed class Champs
    {
        public Champs(IReadOnlyList<Categorie> categories, Manuel? manuel)
        {
            Categorie.Items.Add(new Choix<long?>(null, "Aucune"));
            foreach (var c in categories)
            {
                Categorie.Items.Add(new Choix<long?>(c.Id, c.Nom));
            }

            Categorie.SelectedIndex = 0;
            if (manuel is null)
            {
                return;
            }

            Marque.Valeur = manuel.Marque;
            Edition.Valeur = manuel.Edition;
            Titre.Valeur = manuel.Titre;
            Modeles.Valeur = manuel.Modeles;
            MoisRevision.Valeur = manuel.MoisRevision?.ToString() ?? "";
            PageEntretien.Valeur = manuel.PageEntretien?.ToString() ?? "";
            Entretien.Valeur = manuel.Entretien.Replace("\n", Environment.NewLine);
            Sommaire.Valeur = manuel.Sommaire.Replace("\n", Environment.NewLine);
            var index = categories.ToList().FindIndex(c => c.Id == manuel.CategorieId);
            Categorie.SelectedIndex = index + 1;
        }

        public ChampSaisie Marque { get; } = new() { TexteIndicatif = "Scubapro" };
        public ChampSaisie Edition { get; } = new() { TexteIndicatif = "07/2020" };
        public ChampSaisie Titre { get; } = new() { TexteIndicatif = "Détendeurs" };
        public ListeDeroulante Categorie { get; } = new() { AccessibleName = "Catégorie" };
        public ChampSaisie Modeles { get; } = new() { TexteIndicatif = "MK25 EVO, MK21, A700…" };
        public ChampSaisie MoisRevision { get; } = new() { TexteIndicatif = "24 = tous les 2 ans" };
        public ChampSaisie PageEntretien { get; } = new() { TexteIndicatif = "17" };
        public ChampSaisie Entretien { get; } = new(multiligne: true) { TexteIndicatif = "Révision : tous les 2 ans" };
        public ChampSaisie Sommaire { get; } = new(multiligne: true) { TexteIndicatif = "8.2 Entretien ; 17" };

        public string Erreur { get; private set; } = "";

        public void Placer(FenetreFormulaire fenetre)
        {
            fenetre.DeuxChamps("Marque", Marque, "Titre", Titre);
            fenetre.DeuxChamps("Catégorie", Categorie, "Édition", Edition);
            fenetre.Champ("Modèles couverts", Modeles);
            fenetre.DeuxChamps("Révision du fabricant (mois)", MoisRevision, "Page de l'entretien", PageEntretien);
            fenetre.Champ("Délais du fabricant · une ligne par délai, « Libellé : délai »", Entretien, 84);
            fenetre.Champ("Sommaire · une section par ligne, la page à la fin", Sommaire, 150);
        }

        /// <summary>Fiche lue dans les champs, ou null (voir <see cref="Erreur"/>) si un nombre est mal écrit.</summary>
        public FicheManuel? Lire()
        {
            if (!Nombre(MoisRevision.Valeur, out var mois))
            {
                Erreur = "Révision du fabricant : écrivez un nombre de mois (24 pour tous les 2 ans), ou laissez vide.";
                return null;
            }

            if (!Nombre(PageEntretien.Valeur, out var page))
            {
                Erreur = "Page de l'entretien : écrivez un numéro de page, ou laissez vide.";
                return null;
            }

            var categorie = Categorie.SelectedItem is Choix<long?> c ? c.Valeur : null;
            return new FicheManuel(Marque.Valeur, Titre.Valeur, categorie, Modeles.Valeur, Edition.Valeur, mois, page, Entretien.Valeur, Sommaire.Valeur);
        }

        private static bool Nombre(string texte, out int? valeur)
        {
            valeur = null;
            if (string.IsNullOrWhiteSpace(texte))
            {
                return true;
            }

            if (int.TryParse(texte.Trim(), out var n))
            {
                valeur = n;
                return true;
            }

            return false;
        }
    }
}
