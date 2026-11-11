using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtres « Ajouter un équipement » (inventaire, ou départ après un code scanné inconnu) et « Modifier l'équipement »
/// (inventaire). Fiche des registres du club : marque, modèle, n° de série, et pour un détendeur le premier étage, le
/// montage, le deuxième étage, D.S, manomètre et octopus. La validation est faite par <see cref="ServiceInventaire"/>.
/// </summary>
public static class FormulaireEquipement
{
    /// <summary>
    /// Ouvre la fenêtre d'ajout. <paramref name="code"/> pré-remplit le code (étiquette scannée) ; sinon un code est proposé
    /// selon la catégorie. Retourne le code créé, ou null si l'utilisateur a annulé.
    /// </summary>
    public static async Task<string?> OuvrirAsync(IWin32Window? proprietaire, ServiceInventaire inventaire, string? code = null, long? categorieId = null, string? type = null)
    {
        if (categorieId is null && !string.IsNullOrWhiteSpace(code))
        {
            // Type de la médaille s'il est connu (« (Stab) S10 » → Gilets), sinon d'après le code (A24 → Détendeurs).
            categorieId = await inventaire.DevinerCategorieAsync(code, type);
        }

        var champs = await Champs.CreerAsync(inventaire, categorieId, proposerCode: true);
        champs.Code.Valeur = code ?? champs.Code.Valeur;
        if (code is not null && champs.Nom.Valeur.Trim().Length == 0)
        {
            champs.Nom.Valeur = ServiceInventaire.NomPropose(Core.Saisie.CodeScanne.Nettoyer(code, null)); // E03 → Détendeur enfant.
        }

        string? cree = null;
        using var fenetre = new FenetreFormulaire("Ajouter un équipement", "Ajouter", async () =>
        {
            if (!champs.DatesValides)
            {
                return ResultatOperation.Echec(Champs.MessageDate);
            }

            var resultat = await inventaire.CreerAsync(champs.Lire(new Equipement()), champs.DerniereRevision.Date);
            if (resultat.Reussi)
            {
                cree = Core.Saisie.CodeScanne.Nettoyer(champs.Code.Valeur, null);
            }

            return resultat;
        });
        champs.Placer(fenetre, etat: false);
        fenetre.Shown += (_, _) => champs.Nom.Champ.Focus();

        return fenetre.ShowDialog(proprietaire) == DialogResult.OK ? cree : null;
    }

    /// <summary>Ouvre la fiche d'un équipement existant ; vrai s'il a été modifié.</summary>
    public static async Task<bool> ModifierAsync(IWin32Window? proprietaire, ServiceInventaire inventaire, Equipement equipement, DateOnly? derniereRevision)
    {
        var champs = await Champs.CreerAsync(inventaire, equipement.CategorieId, proposerCode: false);
        champs.Remplir(equipement, derniereRevision);

        using var fenetre = new FenetreFormulaire($"Modifier {equipement.Code}", "Enregistrer", async () =>
        {
            if (!champs.DatesValides)
            {
                return ResultatOperation.Echec(Champs.MessageDate);
            }

            // Dernière révision inchangée : rien à enregistrer.
            var revision = champs.DerniereRevision.Date == derniereRevision ? null : champs.DerniereRevision.Date;
            return await inventaire.ModifierAsync(champs.Lire(new Equipement { Id = equipement.Id }), revision);
        });
        champs.Placer(fenetre, etat: true);
        fenetre.Shown += (_, _) => champs.Nom.Champ.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK;
    }

    /// <summary>Les champs communs aux deux fenêtres.</summary>
    private sealed class Champs
    {
        public const string MessageDate = "Date invalide : écrivez-la comme 08/11/2027, ou laissez le champ vide.";

        public ListeDeroulante Categorie { get; } = new() { AccessibleName = "Catégorie" };
        public ChampSaisie Code { get; } = new() { TexteIndicatif = "BLOC-035" };

        // Gestion des catégories, à côté de la liste : + ajouter, corbeille supprimer.
        private ServiceInventaire? _inventaire;

        private readonly BoutonArrondi _ajouterCategorie =
            new("Ajouter une catégorie", StyleBouton.Icone, Icones.Plus) { Margin = new Padding(6, 0, 0, 0), Anchor = AnchorStyles.None };

        private readonly BoutonArrondi _supprimerCategorie =
            new("Supprimer la catégorie", StyleBouton.Icone, Icones.Corbeille)
            {
                Margin = new Padding(6, 0, 0, 0), Anchor = AnchorStyles.None, CouleurIcone = Theme.Erreur,
            };

        public ChampSaisie Nom { get; } = new() { TexteIndicatif = "Ex. : Bloc 12 L acier" };
        public ListeDeroulante Etat { get; } = new() { AccessibleName = "État" };
        public ChampSaisie Marque { get; } = new() { TexteIndicatif = "Ex. : Aqualung" };
        public ChampSaisie Modele { get; } = new() { TexteIndicatif = "Ex. : Wave" };
        public ChampSaisie Taille { get; } = new() { TexteIndicatif = "M, 42-43, 12 L…" };
        public ChampSaisie NumeroSerie { get; } = new() { TexteIndicatif = "Ex. : BB388677" };
        public ChampDate DerniereRevision { get; } = new() { AccessibleName = "Date de la dernière révision" };
        public ChampDate Revision { get; } = new() { AccessibleName = "Prochaine révision" };
        public ChampDate Requalification { get; } = new() { AccessibleName = "Prochaine requalification" };
        public ChampSaisie Notes { get; } = new() { TexteIndicatif = "Remarques…" };

        // --- Détendeurs ---
        public ListeDeroulante Montage { get; } = new() { AccessibleName = "Étrier ou DIN" };
        public ListeDeroulante DirectSystem { get; } = new() { AccessibleName = "D.S" };
        public ChampSaisie MarqueDeuxiemeEtage { get; } = new() { TexteIndicatif = "Ex. : Scubapro" };
        public ChampSaisie ModeleDeuxiemeEtage { get; } = new() { TexteIndicatif = "Ex. : R 295" };
        public ChampSaisie NumeroDeuxiemeEtage { get; } = new() { TexteIndicatif = "N° de série" };
        public ChampSaisie Manometre { get; } = new() { TexteIndicatif = "Marque (vide : sans)" };
        public ChampSaisie Octopus { get; } = new() { TexteIndicatif = "Modèle (vide : sans)" };
        public ChampSaisie NumeroOctopus { get; } = new() { TexteIndicatif = "N° de série" };

        public Label Aide { get; } = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaire);

        /// <summary>Rangées propres au détendeur (masquées pour les autres catégories) et rangées aux libellés changeants.</summary>
        private readonly List<TableLayoutPanel> _rangeesDetendeur = [];
        private TableLayoutPanel? _rangeeModele;
        private TableLayoutPanel? _rangeeTaille;
        private TableLayoutPanel? _rangeeDates;
        private TableLayoutPanel? _rangeeRequalif;

        public bool DatesValides => Revision.Valide && Requalification.Valide && DerniereRevision.Valide;

        private string NomCategorie => (Categorie.SelectedItem as Choix<long>)?.Texte ?? "";

        public static async Task<Champs> CreerAsync(ServiceInventaire inventaire, long? categorieId, bool proposerCode)
        {
            var c = new Champs { _inventaire = inventaire };
            foreach (var categorie in (await inventaire.ListerCategoriesAsync()).OrderBy(x => x.Id))
            {
                c.Categorie.Items.Add(new Choix<long>(categorie.Id, categorie.Nom));
            }

            c._ajouterCategorie.Click += async (_, _) =>
            {
                try
                {
                    await c.AjouterCategorieAsync();
                }
                catch (Exception ex)
                {
                    Erreurs.Signaler(c._ajouterCategorie.FindForm(), ex);
                }
            };
            c._supprimerCategorie.Click += async (_, _) =>
            {
                try
                {
                    await c.SupprimerCategorieAsync();
                }
                catch (Exception ex)
                {
                    Erreurs.Signaler(c._supprimerCategorie.FindForm(), ex);
                }
            };

            foreach (var etat in Enum.GetValues<EtatEquipement>())
            {
                c.Etat.Items.Add(new Choix<EtatEquipement>(etat, Libelles.De(etat)));
            }

            c.Montage.Items.Add(new Choix<MontageDetendeur?>(null, "—"));
            foreach (var m in Enum.GetValues<MontageDetendeur>())
            {
                c.Montage.Items.Add(new Choix<MontageDetendeur?>(m, Libelles.De(m)));
            }

            c.DirectSystem.Items.Add(new Choix<bool?>(null, "—"));
            c.DirectSystem.Items.Add(new Choix<bool?>(true, "Oui"));
            c.DirectSystem.Items.Add(new Choix<bool?>(false, "Non"));
            c.Montage.SelectedIndex = c.DirectSystem.SelectedIndex = 0;

            c.Code.Champ.Font = Theme.PoliceCode(11.25f);
            c.Code.Champ.CharacterCasing = CharacterCasing.Upper;
            c.Code.Champ.AccessibleName = "Code de l'étiquette";
            foreach (var (champ, nom) in new[]
            {
                (c.Nom, "Nom"), (c.Marque, "Marque"), (c.Modele, "Modèle"), (c.Taille, "Taille"), (c.NumeroSerie, "Numéro de série"),
                (c.Notes, "Notes"), (c.MarqueDeuxiemeEtage, "Marque du deuxième étage"), (c.ModeleDeuxiemeEtage, "Deuxième étage"),
                (c.NumeroDeuxiemeEtage, "Numéro du deuxième étage"), (c.Manometre, "Manomètre"), (c.Octopus, "Octopus"), (c.NumeroOctopus, "Numéro de l'octopus"),
            })
            {
                champ.Champ.AccessibleName = nom;
            }

            c.Aide.MaximumSize = new Size(460, 0);

            // Changement de catégorie : nouveau code proposé (ajout, si l'utilisateur ne l'a pas tapé), champs et libellés adaptés.
            var dernierePropose = "";
            async Task CategorieChangeeAsync()
            {
                if (c.Categorie.SelectedItem is not Choix<long> choix)
                {
                    return;
                }

                c.Adapter();
                c.MajBoutonSupprimer();
                if (proposerCode && (c.Code.Valeur.Trim().Length == 0 || c.Code.Valeur == dernierePropose))
                {
                    dernierePropose = await inventaire.ProposerCodeAsync(choix.Valeur);
                    c.Code.Valeur = dernierePropose;
                }
            }

            c.Categorie.SelectedIndexChanged += async (_, _) => await CategorieChangeeAsync();
            var initiale = categorieId is { } id ? c.Categorie.Items.OfType<Choix<long>>().FirstOrDefault(x => x.Valeur == id) : null;
            c.Categorie.SelectedItem = initiale ?? c.Categorie.Items.OfType<Choix<long>>().FirstOrDefault();
            c.MajBoutonSupprimer();
            return c;
        }

        /// <summary>La corbeille ne supprime pas la dernière catégorie ; son info-bulle nomme la catégorie choisie.</summary>
        private void MajBoutonSupprimer()
        {
            var choix = Categorie.SelectedItem as Choix<long>;
            _supprimerCategorie.Enabled = Categorie.Items.Count > 1;
            _supprimerCategorie.AccessibleName = choix is null ? "Supprimer la catégorie" : $"Supprimer la catégorie {choix.Texte}";
        }

        /// <summary>Ajoute une catégorie et la choisit (le code proposé suit).</summary>
        private async Task AjouterCategorieAsync()
        {
            if (_inventaire is not { } inventaire)
            {
                return;
            }

            var nom = new ChampSaisie { TexteIndicatif = "Ex. : Combinaisons" };
            nom.Champ.AccessibleName = "Nom de la catégorie";
            nom.Champ.MaxLength = Asprenaut.Materiel.Core.Modeles.Categorie.LongueurMaxNom;

            long? creee = null;
            using var fenetre = new FenetreFormulaire("Ajouter une catégorie", "Ajouter", async () =>
            {
                var resultat = await inventaire.AjouterCategorieAsync(nom.Valeur);
                if (resultat.Reussi)
                {
                    creee = resultat.Id;
                }

                return resultat;
            });
            fenetre.Champ("Nom de la catégorie", nom);
            fenetre.Shown += (_, _) => nom.Champ.Focus();
            if (fenetre.ShowDialog(_ajouterCategorie.FindForm()) == DialogResult.OK && creee is { } id)
            {
                await RafraichirCategoriesAsync(inventaire, id);
            }
        }

        /// <summary>Supprime la catégorie choisie (refusé si du matériel ou un manuel l'utilise).</summary>
        private async Task SupprimerCategorieAsync()
        {
            if (_inventaire is not { } inventaire || Categorie.SelectedItem is not Choix<long> choix)
            {
                return;
            }

            var proprietaire = _supprimerCategorie.FindForm();
            if (!Dialogues.Confirmer(proprietaire, $"Supprimer la catégorie « {choix.Texte} » ?\n\nElle ne doit plus contenir de matériel ni de manuel."))
            {
                return;
            }

            var resultat = await inventaire.SupprimerCategorieAsync(choix.Valeur);
            if (!resultat.Reussi)
            {
                Dialogues.Erreur(proprietaire, resultat.Message);
                return;
            }

            await RafraichirCategoriesAsync(inventaire, null);
        }

        /// <summary>Relit les catégories (après un ajout ou une suppression) et choisit celle demandée.</summary>
        private async Task RafraichirCategoriesAsync(ServiceInventaire inventaire, long? aSelectionner)
        {
            var categories = (await inventaire.ListerCategoriesAsync()).OrderBy(x => x.Id).ToList();
            Categorie.Items.Clear();
            foreach (var categorie in categories)
            {
                Categorie.Items.Add(new Choix<long>(categorie.Id, categorie.Nom));
            }

            var choix = Categorie.Items.OfType<Choix<long>>().FirstOrDefault(x => x.Valeur == aSelectionner)
                ?? Categorie.Items.OfType<Choix<long>>().FirstOrDefault();
            Categorie.SelectedItem = choix;
            MajBoutonSupprimer();
        }

        /// <summary>
        /// Selon la catégorie : rangées du détendeur visibles ou non, libellés du premier étage, requalification (blocs),
        /// dernière révision (pas pour les blocs, qui passent par la TIV), aide.
        /// </summary>
        private void Adapter()
        {
            var categorie = NomCategorie;
            var detendeur = ServiceInventaire.EstDetendeur(categorie);
            var bloc = ServiceRevisions.EstBloc(categorie);
            Requalification.Enabled = bloc;
            if (!bloc)
            {
                Requalification.Date = null;
            }

            DerniereRevision.Enabled = !bloc;
            if (bloc)
            {
                DerniereRevision.Date = null;
            }

            foreach (var r in _rangeesDetendeur)
            {
                r.Visible = detendeur;
            }

            if (_rangeeModele is { } modele)
            {
                var (marque, mod) = FenetreFormulaire.Libelles(modele);
                marque.Text = detendeur ? "Marque (premier étage)" : "Marque";
                mod.Text = detendeur ? "Premier étage" : "Modèle";
            }

            if (_rangeeTaille is { } taille)
            {
                // Pas de taille pour un détendeur : la case reste vide, le n° de série du premier étage à droite.
                var (lTaille, lSerie) = FenetreFormulaire.Libelles(taille);
                lSerie.Text = detendeur ? "N° premier étage" : "N° de série";
                lTaille.Text = detendeur ? "" : "Taille";
                Taille.Visible = Taille.Enabled = !detendeur;
            }

            if (_rangeeRequalif is { } requalif)
            {
                FenetreFormulaire.Libelles(requalif).Gauche.Text = bloc ? "Prochaine requalification" : "Requalification (blocs)";
            }

            if (_rangeeDates is { } dates)
            {
                FenetreFormulaire.Libelles(dates).Droite.Text = bloc ? "Prochaine TIV" : detendeur ? "À réviser avant" : "Prochaine révision";
            }

            Aide.Text = bloc
                ? "Bloc : indiquez la date de la prochaine TIV et celle de la prochaine requalification, si vous les connaissez."
                : detendeur
                    ? "Détendeur : révision tous les 2 ans. Indiquez la date de la dernière révision : « À réviser avant » est calculée."
                    : ServiceInventaire.EstGilet(categorie)
                        ? "Stab : pas de date de révision obligatoire. Notez la dernière révision si elle a été faite."
                        : "Prochaine révision : date conseillée par le fabricant, si elle existe.";
        }

        public void Remplir(Equipement e, DateOnly? derniereRevision)
        {
            Code.Valeur = e.Code;
            Nom.Valeur = e.Libelle;
            Taille.Valeur = e.Taille ?? "";
            Marque.Valeur = e.Marque;
            Modele.Valeur = e.Modele;
            NumeroSerie.Valeur = e.NumeroSerie;
            DerniereRevision.Date = derniereRevision;
            Revision.Date = e.DateProchaineRevision;
            Requalification.Date = e.DateRequalification;
            Notes.Valeur = e.Notes;
            Etat.SelectedItem = Etat.Items.OfType<Choix<EtatEquipement>>().First(x => x.Valeur == e.Etat);
            Montage.SelectedItem = Montage.Items.OfType<Choix<MontageDetendeur?>>().First(x => x.Valeur == e.Montage);
            DirectSystem.SelectedItem = DirectSystem.Items.OfType<Choix<bool?>>().First(x => x.Valeur == e.DirectSystem);
            MarqueDeuxiemeEtage.Valeur = e.MarqueDeuxiemeEtage;
            ModeleDeuxiemeEtage.Valeur = e.ModeleDeuxiemeEtage;
            NumeroDeuxiemeEtage.Valeur = e.NumeroDeuxiemeEtage;
            Manometre.Valeur = e.Manometre;
            Octopus.Valeur = e.Octopus;
            NumeroOctopus.Valeur = e.NumeroOctopus;
        }

        public Equipement Lire(Equipement e)
        {
            e.Code = Code.Valeur;
            e.Libelle = Nom.Valeur;
            e.CategorieId = (Categorie.SelectedItem as Choix<long>)?.Valeur ?? 0;
            e.Taille = Taille.Enabled ? Taille.Valeur : null;
            e.Marque = Marque.Valeur;
            e.Modele = Modele.Valeur;
            e.NumeroSerie = NumeroSerie.Valeur;
            e.DateProchaineRevision = Revision.Date;
            e.DateRequalification = Requalification.Date;
            e.Notes = Notes.Valeur;
            e.Etat = (Etat.SelectedItem as Choix<EtatEquipement>)?.Valeur ?? EtatEquipement.EnService;
            e.Montage = (Montage.SelectedItem as Choix<MontageDetendeur?>)?.Valeur;
            e.DirectSystem = (DirectSystem.SelectedItem as Choix<bool?>)?.Valeur;
            e.MarqueDeuxiemeEtage = MarqueDeuxiemeEtage.Valeur;
            e.ModeleDeuxiemeEtage = ModeleDeuxiemeEtage.Valeur;
            e.NumeroDeuxiemeEtage = NumeroDeuxiemeEtage.Valeur;
            e.Manometre = Manometre.Valeur;
            e.Octopus = Octopus.Valeur;
            e.NumeroOctopus = NumeroOctopus.Valeur;
            return e;
        }

        /// <summary>Liste des catégories pleine largeur, avec les boutons + (ajouter) et corbeille (supprimer).</summary>
        private Control RangeeCategorie()
        {
            var rangee = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Height = Theme.HauteurChamp,
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };
            rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            rangee.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Categorie.Dock = DockStyle.Fill;
            Categorie.Margin = Padding.Empty;
            rangee.Controls.Add(Categorie, 0, 0);
            rangee.Controls.Add(_ajouterCategorie, 1, 0);
            rangee.Controls.Add(_supprimerCategorie, 2, 0);
            return rangee;
        }

        public void Placer(FenetreFormulaire fenetre, bool etat)
        {
            fenetre.Champ("Catégorie", RangeeCategorie());
            fenetre.Champ("Code de l'étiquette", Code);
            if (etat)
            {
                fenetre.DeuxChamps("Nom", Nom, "État", Etat);
            }
            else
            {
                fenetre.Champ("Nom", Nom);
            }

            _rangeeModele = fenetre.DeuxChamps("Marque", Marque, "Modèle", Modele);
            _rangeeTaille = fenetre.DeuxChamps("Taille", Taille, "N° de série", NumeroSerie);
            _rangeesDetendeur.Add(fenetre.DeuxChamps("Étrier / DIN", Montage, "D.S", DirectSystem));
            _rangeesDetendeur.Add(fenetre.DeuxChamps("Marque deuxième étage", MarqueDeuxiemeEtage, "Deuxième étage", ModeleDeuxiemeEtage));
            _rangeesDetendeur.Add(fenetre.DeuxChamps("N° deuxième étage", NumeroDeuxiemeEtage, "Mano", Manometre));
            _rangeesDetendeur.Add(fenetre.DeuxChamps("Octopus", Octopus, "N° octopus", NumeroOctopus));
            _rangeeDates = fenetre.DeuxChamps("Date de la dernière révision", DerniereRevision, "Prochaine révision", Revision);
            _rangeeRequalif = fenetre.DeuxChamps("Prochaine requalification", Requalification, "Notes (facultatif)", Notes);
            fenetre.Ajouter(Aide);
            Adapter();
        }
    }
}
