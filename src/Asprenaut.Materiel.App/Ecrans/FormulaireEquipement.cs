using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtres « Ajouter un équipement » (inventaire, ou départ après un code scanné inconnu) et « Modifier l'équipement »
/// (maquette « Inventaire · Ajouter équipements (formulaire) ») : catégorie, puis sections numérotées et aperçu à droite.
/// Détendeur : 1 Identification (Pour adulte / enfant, code de la médaille, nom), 2 Composition (premier étage avec
/// raccord et D.S, deuxième étage, octopus et manomètre à interrupteur), 3 Révision et usage (dernière révision, à réviser
/// avant, nitrox, notes). Autres catégories : 2 Fiche (marque, modèle, taille, n° de série). La validation est faite par
/// <see cref="ServiceInventaire"/>.
/// </summary>
public static class FormulaireEquipement
{
    private const int Largeur = 1120;
    private const int Marge = 28;
    private const int LargeurApercu = 290;
    private const int EcartColonnes = 20;

    /// <summary>
    /// Ouvre la fenêtre d'ajout. <paramref name="code"/> pré-remplit le code (étiquette scannée) ; sinon un code est proposé
    /// selon la catégorie, et « Ajouter et en créer un autre » permet d'en saisir plusieurs à la suite.
    /// Retourne le dernier code créé, ou null si rien n'a été créé.
    /// </summary>
    public static async Task<string?> OuvrirAsync(IWin32Window? proprietaire, ServiceInventaire inventaire, string? code = null, long? categorieId = null, string? type = null)
    {
        if (categorieId is null && !string.IsNullOrWhiteSpace(code))
        {
            // Type de la médaille s'il est connu (« (Stab) S10 » → Gilets), sinon d'après le code (A24 → Détendeurs).
            categorieId = await inventaire.DevinerCategorieAsync(code, type);
        }

        var fiche = await Fiche.CreerAsync(inventaire, categorieId, proposerCode: code is null, modification: false);
        if (code is not null)
        {
            fiche.Code.Valeur = Core.Saisie.CodeScanne.Nettoyer(code, null); // E03 → « Pour : Enfant », nom « Détendeur enfant E03 ».
        }

        string? cree = null;
        async Task<ResultatOperation> CreerAsync()
        {
            if (!fiche.DatesValides)
            {
                return ResultatOperation.Echec(Fiche.MessageDate);
            }

            var resultat = await inventaire.CreerAsync(fiche.Lire(new Equipement()), fiche.DerniereRevision.Date);
            if (resultat.Reussi)
            {
                cree = Core.Saisie.CodeScanne.Nettoyer(fiche.Code.Valeur, null);
            }

            return resultat;
        }

        using var fenetre = new FenetreFormulaire("Ajouter un équipement", "Ajouter", CreerAsync, Largeur, Marge);
        fenetre.EnTete("Il apparaîtra dans l'inventaire avec sa fiche EPI.", icone: Icones.Plus);
        if (code is null)
        {
            fenetre.BoutonEnPlus("Ajouter et en créer un autre", async () =>
            {
                var resultat = await CreerAsync();
                if (resultat.Reussi)
                {
                    fenetre.Resume($"{cree} ajouté. Saisissez le suivant.", enAvant: true);
                    await fiche.RecommencerAsync();
                }

                return resultat;
            });
        }

        fiche.Placer(fenetre);
        fenetre.Shown += (_, _) => (code is null ? fiche.Code.Champ : fiche.Nom.Champ).Focus();
        fenetre.ShowDialog(proprietaire);
        return cree;
    }

    /// <summary>Ouvre la fiche d'un équipement existant ; vrai s'il a été modifié.</summary>
    public static async Task<bool> ModifierAsync(IWin32Window? proprietaire, ServiceInventaire inventaire, Equipement equipement, DateOnly? derniereRevision)
    {
        var fiche = await Fiche.CreerAsync(inventaire, equipement.CategorieId, proposerCode: false, modification: true);
        fiche.Remplir(equipement, derniereRevision);

        using var fenetre = new FenetreFormulaire($"Modifier {equipement.Code}", "Enregistrer", async () =>
        {
            if (!fiche.DatesValides)
            {
                return ResultatOperation.Echec(Fiche.MessageDate);
            }

            // Dernière révision inchangée : rien à enregistrer.
            var revision = fiche.DerniereRevision.Date == derniereRevision ? null : fiche.DerniereRevision.Date;
            return await inventaire.ModifierAsync(fiche.Lire(new Equipement { Id = equipement.Id }), revision);
        }, Largeur, Marge);
        fenetre.EnTete("Les changements suivent dans l'inventaire et la fiche EPI.", icone: Icones.Crayon);
        fiche.Placer(fenetre);
        fenetre.Shown += (_, _) => fiche.Nom.Champ.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK;
    }

    /// <summary>Les champs, les sections et l'aperçu, communs aux deux fenêtres.</summary>
    private sealed class Fiche
    {
        public const string MessageDate = "Date invalide : écrivez-la comme 08/11/2027, ou laissez le champ vide.";

        private ServiceInventaire _inventaire = null!;
        private bool _proposerCode;
        private bool _modification;
        private string _codePropose = "";
        private string _nomPropose = "";
        private bool _maj;
        private bool _detendeurAffiche;

        // --- Identification ---
        public ChoixCategorie Categorie { get; } = new() { Margin = new Padding(0, 0, 0, 18) };
        public Bascule Pour { get; } = new("Adulte", "Enfant") { Compact = true, Etirer = true, AccessibleName = "Pour" };
        public ChampSaisie Code { get; } = new() { TexteIndicatif = "A01", Icone = Icones.Scanner };
        public ChampSaisie Nom { get; } = new() { TexteIndicatif = "Ex. : Bloc 12 L acier" };
        public ListeDeroulante Etat { get; } = new() { AccessibleName = "État" };

        // --- Fiche (hors détendeur) ---
        public ChampSaisie Marque { get; } = new() { TexteIndicatif = "Ex. : Aqualung" };
        public ChampSaisie Modele { get; } = new() { TexteIndicatif = "Ex. : Wave" };
        public ChampSaisie Taille { get; } = new() { TexteIndicatif = "M, 42-43, 12 L…" };
        public ChampSaisie NumeroSerie { get; } = new() { TexteIndicatif = "Facultatif" };

        // --- Composition (détendeur) ---
        private readonly ChampSaisie _marquePremier = new() { TexteIndicatif = "Ex. Aqualung" };
        private readonly ChampSaisie _modelePremier = new() { TexteIndicatif = "Ex. Legend" };
        private readonly ChampSaisie _numeroPremier = new() { TexteIndicatif = "BB388677" };
        private readonly ChampSaisie _marqueDeuxieme = new() { TexteIndicatif = "Ex. Scubapro" };
        private readonly ChampSaisie _modeleDeuxieme = new() { TexteIndicatif = "Ex. R 295" };
        private readonly ChampSaisie _numeroDeuxieme = new() { TexteIndicatif = "N° de série" };
        private readonly ChampSaisie _marqueOctopus = new() { TexteIndicatif = "Ex. Scubapro" };
        private readonly ChampSaisie _modeleOctopus = new() { TexteIndicatif = "Ex. R 195" };
        private readonly ChampSaisie _numeroOctopus = new() { TexteIndicatif = "N° de série" };
        private readonly ChampSaisie _marqueManometre = new() { TexteIndicatif = "Ex. Aqualung" };
        private readonly ChampSaisie _modeleManometre = new() { TexteIndicatif = "Ex. SPG 300" };
        private readonly ChampSaisie _numeroManometre = new() { TexteIndicatif = "N° de série" };
        private readonly ElementComposition _octopus = new("Octopus", "source de secours", facultatif: true);
        private readonly ElementComposition _manometre = new("Manomètre", "ou console", facultatif: true);
        private readonly Bascule _raccord = new("—", "DIN", "Étrier") { Compact = true, AccessibleName = "Raccord" };
        private readonly Bascule _directSystem = new("—", "Oui", "Non") { Compact = true, AccessibleName = "D.S" };

        // --- Révision et usage ---
        public ChampDate DerniereRevision { get; } = new() { AccessibleName = "Date de la dernière révision" };
        public ChampDate Revision { get; } = new() { AccessibleName = "Prochaine révision" };
        public ChampDate Requalification { get; } = new() { AccessibleName = "Prochaine requalification" };
        public OptionNitrox Nitrox { get; } = new();
        public ChampSaisie Notes { get; } = new() { TexteIndicatif = "Ex. acheté d'occasion, flexible changé en 2025" };
        private readonly Label _aide = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
        private readonly Label _libelleRevision = FenetreFormulaire.LibelleChamp("Prochaine révision");

        // --- Mise en page ---
        private readonly ApercuEquipement _apercu = new();
        private readonly RangeeChamps _identite = new();
        private readonly RangeeChamps _dates = new();
        private SectionFormulaire _composition = null!;
        private SectionFormulaire _sectionFiche = null!;
        private bool _nitroxPossible;

        public bool DatesValides => Revision.Valide && Requalification.Valide && DerniereRevision.Valide;

        private string NomCategorie => Categorie.NomCategorie;

        private bool EstDetendeur => ServiceInventaire.EstDetendeur(NomCategorie);

        public static async Task<Fiche> CreerAsync(ServiceInventaire inventaire, long? categorieId, bool proposerCode, bool modification)
        {
            var f = new Fiche { _inventaire = inventaire, _proposerCode = proposerCode, _modification = modification };
            foreach (var etat in Enum.GetValues<EtatEquipement>())
            {
                f.Etat.Items.Add(new Choix<EtatEquipement>(etat, Libelles.De(etat)));
            }

            f.Code.Champ.Font = Theme.PoliceCode(12f);
            f.Code.Champ.ForeColor = Theme.AccentClair;
            f.Code.Champ.CharacterCasing = CharacterCasing.Upper;
            f.Code.Champ.AccessibleName = "Code de la médaille";
            f._numeroPremier.Champ.Font = f._numeroDeuxieme.Champ.Font = f._numeroOctopus.Champ.Font = f._numeroManometre.Champ.Font = f.NumeroSerie.Champ.Font = Theme.PoliceCode(10.5f);
            foreach (var (champ, nom) in new[]
            {
                (f.Nom, "Nom affiché"), (f.Marque, "Marque"), (f.Modele, "Modèle"), (f.Taille, "Taille"), (f.NumeroSerie, "Numéro de série"),
                (f.Notes, "Notes"), (f._marquePremier, "Premier étage · marque"), (f._modelePremier, "Premier étage · modèle"),
                (f._numeroPremier, "Premier étage · numéro de série"), (f._marqueDeuxieme, "Deuxième étage · marque"),
                (f._modeleDeuxieme, "Deuxième étage · modèle"), (f._numeroDeuxieme, "Deuxième étage · numéro de série"),
                (f._marqueOctopus, "Octopus · marque"), (f._modeleOctopus, "Octopus · modèle"), (f._numeroOctopus, "Octopus · numéro de série"), (f._marqueManometre, "Manomètre · marque"), (f._modeleManometre, "Manomètre · modèle"), (f._numeroManometre, "Manomètre · numéro de série"),
            })
            {
                champ.Champ.AccessibleName = nom;
                champ.Champ.TextChanged += (_, _) => f.Actualiser();
            }

            // Dernière révision tapée (ou choisie dans le calendrier) : la prochaine est calculée tout de suite.
            f.DerniereRevision.DateChangee += (_, _) =>
            {
                f.CalculerProchaineRevision();
                f.Actualiser();
            };
            f.Revision.DateChangee += (_, _) => f.Actualiser();
            f.Requalification.DateChangee += (_, _) => f.Actualiser();
            f.Nitrox.CheckedChanged += (_, _) => f.Actualiser();
            f._raccord.Changement += (_, _) => f.Actualiser();
            f._directSystem.Changement += (_, _) => f.Actualiser();
            foreach (var element in new[] { f._octopus, f._manometre })
            {
                element.Presence!.CheckedChanged += (_, _) =>
                {
                    f.AdapterComposants();
                    f.Actualiser();
                };
            }

            // Code tapé : « Pour » suit la lettre (E03 → Enfant) et le nom proposé suit le code tant qu'il n'a pas été tapé.
            f.Code.Champ.TextChanged += (_, _) => f.CodeChange();
            f.Pour.Changement += async (_, _) =>
            {
                try
                {
                    await f.PourChangeAsync();
                }
                catch (Exception ex)
                {
                    Erreurs.Signaler(f.Pour.FindForm(), ex);
                }
            };

            // Changement de catégorie : nouveau code proposé (ajout, si l'utilisateur ne l'a pas tapé), champs et libellés adaptés.
            f.Categorie.Changement += async (_, _) =>
            {
                try
                {
                    f.Adapter();
                    await f.ProposerCodeAsync();
                }
                catch (Exception ex)
                {
                    Erreurs.Signaler(f.Categorie.FindForm(), ex);
                }
            };
            await f.Categorie.ChargerAsync(inventaire, categorieId);
            return f;
        }

        /// <summary>Code proposé (ajout) si le champ est vide ou garde le dernier code proposé ; « E » si « Pour : Enfant ».</summary>
        private async Task ProposerCodeAsync()
        {
            if (!_proposerCode || Categorie.CategorieId is not { } id || (Code.Valeur.Trim().Length > 0 && Code.Valeur != _codePropose))
            {
                return;
            }

            var prefixe = EstDetendeur ? (Pour.SelectedIndex == 1 ? "E" : "A") : null;
            _codePropose = await _inventaire.ProposerCodeAsync(id, prefixe);
            Code.Valeur = _codePropose;
        }

        private async Task PourChangeAsync()
        {
            if (_maj)
            {
                return;
            }

            await ProposerCodeAsync();
            ProposerNom();
            Actualiser();
        }

        private void CodeChange()
        {
            if (EstDetendeur && !_modification)
            {
                // A24 / E03 : la lettre de la médaille choisit adulte ou enfant.
                var code = Code.Valeur.Trim();
                var enfant = ServiceBilan.EstDetendeurEnfant(code, null);
                var adulte = code.Length > 1 && char.ToUpperInvariant(code[0]) == 'A' && code.Skip(1).All(char.IsAsciiDigit);
                if (enfant || adulte)
                {
                    _maj = true;
                    Pour.SelectedIndex = enfant ? 1 : 0;
                    _maj = false;
                }
            }

            ProposerNom();
            Actualiser();
        }

        /// <summary>Nom proposé d'après le code (A24 → « Détendeur adulte A24 ») tant que l'utilisateur ne l'a pas tapé.</summary>
        private void ProposerNom()
        {
            if (_modification || (Nom.Valeur.Trim().Length > 0 && Nom.Valeur != _nomPropose))
            {
                return;
            }

            var propose = ServiceInventaire.NomPropose(Core.Saisie.CodeScanne.Nettoyer(Code.Valeur, null));
            _nomPropose = propose;
            Nom.Valeur = propose;
        }

        /// <summary>« Ajouter et en créer un autre » : même catégorie, marque et modèle ; code suivant, nom, numéros et notes vidés.</summary>
        public async Task RecommencerAsync()
        {
            Code.Valeur = "";
            Nom.Valeur = "";
            foreach (var champ in new[] { NumeroSerie, _numeroPremier, _numeroDeuxieme, _numeroOctopus, _numeroManometre, Notes })
            {
                champ.Valeur = "";
            }

            await ProposerCodeAsync();
            ProposerNom();
            Actualiser();
            Code.Champ.Focus();
            Code.Champ.SelectAll();
        }

        /// <summary>
        /// Selon la catégorie : composition (détendeur) ou fiche, « Pour », requalification (blocs), dernière révision (pas pour
        /// les blocs, qui passent par la TIV), nitrox, libellés et aide. Les valeurs tapées passent de la fiche à la composition.
        /// </summary>
        private void Adapter()
        {
            var categorie = NomCategorie;
            var detendeur = EstDetendeur;
            var bloc = ServiceRevisions.EstBloc(categorie);

            if (detendeur != _detendeurAffiche)
            {
                var (de, vers) = detendeur
                    ? (new[] { Marque, Modele, NumeroSerie }, new[] { _marquePremier, _modelePremier, _numeroPremier })
                    : (new[] { _marquePremier, _modelePremier, _numeroPremier }, new[] { Marque, Modele, NumeroSerie });
                for (var i = 0; i < de.Length; i++)
                {
                    vers[i].Valeur = de[i].Valeur;
                }

                _detendeurAffiche = detendeur;
            }

            if (_composition is not null)
            {
                _composition.Visible = detendeur;
                _sectionFiche.Visible = !detendeur;
            }

            _identite.Montrer(Pour, detendeur);
            Nom.TexteIndicatif = detendeur ? (Pour.SelectedIndex == 1 ? "Détendeur enfant" : "Détendeur adulte") : $"Ex. : {categorie.TrimEnd('s')}";
            _dates.Montrer(DerniereRevision, !bloc);
            _dates.Montrer(Requalification, bloc);
            if (!bloc)
            {
                Requalification.Date = null;
            }
            else
            {
                DerniereRevision.Date = null;
            }

            _nitroxPossible = ServiceInventaire.PeutEtreNitrox(categorie);
            _dates.Montrer(Nitrox, _nitroxPossible);
            if (!_nitroxPossible)
            {
                Nitrox.Checked = false;
            }

            _libelleRevision.Text = bloc ? "Prochaine TIV" : detendeur ? "À réviser avant" : "Prochaine révision";

            var delai = _inventaire.DelaiRevision(categorie);
            var duree = delai % 12 == 0 ? (delai == 12 ? "chaque année" : $"tous les {delai / 12} ans") : $"tous les {delai} mois";
            _aide.Text = bloc
                ? "Bloc : indiquez la prochaine TIV et la prochaine requalification, si vous les connaissez."
                : delai > 0
                    ? $"Révision {duree} : « {_libelleRevision.Text} » est calculée d'après la dernière révision (modifiable)."
                    : ServiceInventaire.EstGilet(categorie)
                        ? "Stab : pas de date de révision obligatoire. Notez la dernière révision si elle a été faite."
                        : "Pas de délai de révision pour cette catégorie : tapez la prochaine révision à la main si elle existe.";
            CalculerProchaineRevision();
            AdapterComposants();
            Actualiser();
        }

        /// <summary>Octopus et manomètre absents : leurs cases sont grisées (et enregistrées vides).</summary>
        private void AdapterComposants()
        {
            _marqueOctopus.Enabled = _modeleOctopus.Enabled = _numeroOctopus.Enabled = _octopus.Present;
            _marqueManometre.Enabled = _modeleManometre.Enabled = _numeroManometre.Enabled = _manometre.Present;
        }

        /// <summary>« Prochaine révision » = dernière révision + délai de la catégorie (même calcul qu'à l'enregistrement).</summary>
        private void CalculerProchaineRevision()
        {
            if (DerniereRevision.Date is { } derniere && _inventaire.ProchaineRevisionCalculee(NomCategorie, derniere) is { } prochaine)
            {
                Revision.Date = prochaine;
            }
        }

        /// <summary>Aperçu de droite d'après la saisie.</summary>
        private void Actualiser()
        {
            var detendeur = EstDetendeur;
            var code = Core.Saisie.CodeScanne.Nettoyer(Code.Valeur, null);
            var nom = Nom.Valeur.Trim().Length > 0 ? Nom.Valeur.Trim() : detendeur ? Nom.TexteIndicatif : "Nouvel équipement";

            var pastilles = new List<(string, Color)>();
            var points = new List<(string, bool)>();
            static bool Rempli(params ChampSaisie[] champs) => champs.Any(c => c.Valeur.Trim().Length > 0);
            if (detendeur)
            {
                var raccord = _raccord.SelectedIndex switch { 1 => "DIN", 2 => "Étrier", _ => "" };
                var ds = _directSystem.SelectedIndex switch { 1 => "D.S Oui", 2 => "D.S Non", _ => "" };
                var texte = string.Join(" · ", new[] { raccord, ds }.Where(t => t.Length > 0));
                if (texte.Length > 0)
                {
                    pastilles.Add((texte, Theme.AccentClair));
                }

                var etages = Rempli(_marquePremier, _modelePremier) && Rempli(_marqueDeuxieme, _modeleDeuxieme);
                points.Add((etages ? "Premier et deuxième étage" : "Premier et deuxième étage à renseigner", etages));
                points.Add((_octopus.Present ? "Octopus" : "Sans octopus", _octopus.Present));
                points.Add((_manometre.Present ? "Manomètre" : "Sans manomètre", _manometre.Present));
            }
            else
            {
                if (Taille.Valeur.Trim().Length > 0)
                {
                    pastilles.Add(($"Taille {Taille.Valeur.Trim()}", Theme.AccentClair));
                }

                points.Add((Rempli(Marque, Modele) ? "Marque et modèle" : "Marque et modèle à renseigner", Rempli(Marque, Modele)));
                points.Add((Rempli(NumeroSerie) ? "N° de série" : "Sans n° de série", Rempli(NumeroSerie)));
            }

            if (_nitroxPossible && Nitrox.Checked)
            {
                pastilles.Add(("Nitrox", Theme.Nitrox));
            }

            var bloc = ServiceRevisions.EstBloc(NomCategorie);
            if (bloc)
            {
                points.Add((Requalification.Date is { } q ? $"Requalification {q:dd/MM/yyyy}" : "Requalification non renseignée", Requalification.Date is not null));
            }
            else
            {
                points.Add((DerniereRevision.Date is { } d ? $"Dernière révision {d:dd/MM/yyyy}" : "Révision non renseignée", DerniereRevision.Date is not null));
            }

            _apercu.Afficher(new ApercuEquipement.Donnees(
                Categorie.CategorieChoisie,
                code,
                nom,
                pastilles,
                points,
                Revision.Date is null ? null : bloc ? "Prochaine TIV" : "Prochaine révision",
                Revision.Date?.ToString("dd/MM/yyyy", Libelles.Culture)));
        }

        public void Remplir(Equipement e, DateOnly? derniereRevision)
        {
            _maj = true;
            Pour.SelectedIndex = ServiceBilan.EstDetendeurEnfant(e.Code, e.Libelle) ? 1 : 0;
            _maj = false;
            Code.Valeur = e.Code;
            Nom.Valeur = e.Libelle;
            Taille.Valeur = e.Taille ?? "";
            Marque.Valeur = _marquePremier.Valeur = e.Marque;
            Modele.Valeur = _modelePremier.Valeur = e.Modele;
            NumeroSerie.Valeur = _numeroPremier.Valeur = e.NumeroSerie;
            DerniereRevision.Date = derniereRevision;
            Revision.Date = e.DateProchaineRevision;
            Requalification.Date = e.DateRequalification;
            Notes.Valeur = e.Notes;
            Etat.SelectedItem = Etat.Items.OfType<Choix<EtatEquipement>>().First(x => x.Valeur == e.Etat);
            _raccord.SelectedIndex = e.Montage switch { MontageDetendeur.Din => 1, MontageDetendeur.Etrier => 2, _ => 0 };
            _directSystem.SelectedIndex = e.DirectSystem switch { true => 1, false => 2, _ => 0 };
            _marqueDeuxieme.Valeur = e.MarqueDeuxiemeEtage;
            _modeleDeuxieme.Valeur = e.ModeleDeuxiemeEtage;
            _numeroDeuxieme.Valeur = e.NumeroDeuxiemeEtage;
            _marqueManometre.Valeur = e.Manometre;
            _marqueOctopus.Valeur = e.MarqueOctopus;
            _modeleOctopus.Valeur = e.Octopus;
            _modeleManometre.Valeur = e.ModeleManometre;
            _numeroManometre.Valeur = e.NumeroManometre;
            _numeroOctopus.Valeur = e.NumeroOctopus;
            _octopus.Presence!.Checked = new[] { e.MarqueOctopus, e.Octopus, e.NumeroOctopus }.Any(v => v.Trim().Length > 0);
            _manometre.Presence!.Checked = new[] { e.Manometre, e.ModeleManometre, e.NumeroManometre }.Any(v => v.Trim().Length > 0);
            Nitrox.Checked = e.Nitrox;
            AdapterComposants();
            Actualiser();
        }

        public Equipement Lire(Equipement e)
        {
            var detendeur = EstDetendeur;
            e.Code = Code.Valeur;
            e.Libelle = Nom.Valeur;
            e.CategorieId = Categorie.CategorieId ?? 0;
            e.Taille = detendeur ? null : Taille.Valeur;
            e.Marque = detendeur ? _marquePremier.Valeur : Marque.Valeur;
            e.Modele = detendeur ? _modelePremier.Valeur : Modele.Valeur;
            e.NumeroSerie = detendeur ? _numeroPremier.Valeur : NumeroSerie.Valeur;
            e.DateProchaineRevision = Revision.Date;
            e.DateRequalification = Requalification.Date;
            e.Notes = Notes.Valeur;
            e.Etat = _modification ? (Etat.SelectedItem as Choix<EtatEquipement>)?.Valeur ?? EtatEquipement.EnService : EtatEquipement.EnService;
            e.Montage = _raccord.SelectedIndex switch { 1 => MontageDetendeur.Din, 2 => MontageDetendeur.Etrier, _ => null };
            e.DirectSystem = _directSystem.SelectedIndex switch { 1 => true, 2 => false, _ => null };
            e.MarqueDeuxiemeEtage = _marqueDeuxieme.Valeur;
            e.ModeleDeuxiemeEtage = _modeleDeuxieme.Valeur;
            e.NumeroDeuxiemeEtage = _numeroDeuxieme.Valeur;
            e.Manometre = _manometre.Present ? _marqueManometre.Valeur : "";
            e.Octopus = _octopus.Present ? _modeleOctopus.Valeur : "";
            e.NumeroOctopus = _octopus.Present ? _numeroOctopus.Valeur : "";
            e.MarqueOctopus = _octopus.Present ? _marqueOctopus.Valeur : "";
            e.ModeleManometre = _manometre.Present ? _modeleManometre.Valeur : "";
            e.NumeroManometre = _manometre.Present ? _numeroManometre.Valeur : "";
            e.Nitrox = _nitroxPossible && Nitrox.Checked;
            return e;
        }

        public void Placer(FenetreFormulaire fenetre)
        {
            fenetre.Separations = true;
            fenetre.Valider.Icone = Icones.Coche;
            fenetre.Note("obligatoire");

            // Catégorie (liste avec recherche, ajout et suppression intégrés).
            var libelleCategorie = Ui.LibelleChamp("Catégorie");

            // 1 Identification.
            var identification = new SectionFormulaire(1, "Identification");
            _identite.Ajouter("Pour", Pour, 200);
            _identite.Ajouter("Code de la médaille *", Code, 220);
            _identite.Ajouter("Nom affiché *", Nom);
            identification.Ajouter(_identite);
            if (_modification)
            {
                var etat = new RangeeChamps();
                etat.Ajouter("État", Etat, 260);
                identification.Ajouter(etat);
            }

            // 2 Composition (détendeur).
            _composition = new SectionFormulaire(2, "Composition");
            var grille = new GrilleComposition();
            grille.Element(new ElementComposition("Premier étage", "obligatoire", facultatif: false), _marquePremier, _modelePremier, _numeroPremier);
            var libelleRaccord = Ui.Libelle("Raccord", Theme.PoliceTitre(9f), Theme.TexteSecondaire);
            var libelleDs = Ui.Libelle("D.S", Theme.PoliceTitre(9f), Theme.TexteSecondaire);
            libelleRaccord.Anchor = libelleDs.Anchor = _raccord.Anchor = _directSystem.Anchor = AnchorStyles.Left;
            libelleRaccord.Margin = new Padding(0, 0, 10, 0);
            libelleDs.Margin = new Padding(24, 0, 10, 0);
            var reglages = Ui.Rangee(true, libelleRaccord, _raccord, libelleDs, _directSystem);
            grille.SousLigne(reglages);
            grille.Element(new ElementComposition("Deuxième étage", "obligatoire", facultatif: false), _marqueDeuxieme, _modeleDeuxieme, _numeroDeuxieme);
            grille.Element(_octopus, _marqueOctopus, _modeleOctopus, _numeroOctopus);
            grille.Element(_manometre, _marqueManometre, _modeleManometre, _numeroManometre);
            _composition.Ajouter(grille);

            // 2 Fiche (autres catégories).
            _sectionFiche = new SectionFormulaire(2, "Fiche");
            var marqueModele = new RangeeChamps();
            marqueModele.Ajouter("Marque", Marque);
            marqueModele.Ajouter("Modèle", Modele);
            var tailleSerie = new RangeeChamps();
            tailleSerie.Ajouter("Taille", Taille);
            tailleSerie.Ajouter("N° de série", NumeroSerie);
            _sectionFiche.Ajouter(marqueModele);
            _sectionFiche.Ajouter(tailleSerie);

            // 3 Révision et usage.
            var revision = new SectionFormulaire(3, "Révision et usage");
            _dates.Ajouter("Dernière révision", DerniereRevision);
            _dates.Ajouter(_libelleRevision, Revision);
            _dates.Ajouter("Prochaine requalification", Requalification);
            _dates.Ajouter(FenetreFormulaire.LibelleChamp(" "), Nitrox);
            _dates.Margin = new Padding(0, 0, 0, 8);
            revision.Ajouter(_dates);
            _aide.Margin = new Padding(0, 0, 0, 14);
            revision.Ajouter(_aide);
            revision.Champ("Notes · facultatif", Notes);

            var gauche = FormulaireSortie.Pile(libelleCategorie, Categorie, identification, _composition, _sectionFiche, revision);
            fenetre.Ajouter(Corps(gauche, _apercu));
            Adapter();
        }

        /// <summary>Formulaire à gauche, aperçu à droite (de la hauteur du formulaire).</summary>
        private static TableLayoutPanel Corps(Control gauche, Control apercu)
        {
            var largeurGauche = Largeur - (2 * Marge) - LargeurApercu - EcartColonnes;
            var corps = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
            };
            corps.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, largeurGauche));
            corps.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LargeurApercu + EcartColonnes));
            corps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            apercu.Margin = new Padding(EcartColonnes, 0, 0, 14);
            corps.Controls.Add(gauche, 0, 0);
            corps.Controls.Add(apercu, 1, 0);
            gauche.Dock = DockStyle.None;
            gauche.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            apercu.Dock = DockStyle.None;
            apercu.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            return corps;
        }
    }
}
