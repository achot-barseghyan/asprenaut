using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtre « Nouveau modèle » / « Modifier le modèle » (maquette « Sorties », onglet Modèles) : 1 Le modèle (nom, jour
/// habituel) et 2 La sortie (type en tuiles, lieu, intitulé) | 3 Responsable (Moniteur | Classe puis cartes) et 4 Règles et
/// notes (utilisations, un équipement par catégorie, notes). Les règles sont dans <see cref="ServiceModelesSortie.EnregistrerAsync"/>.
/// </summary>
internal static class FormulaireModele
{
    private const int Largeur = 960;
    private const int Marge = 24;
    private const string SansJour = "-";

    /// <summary>Ouvre la fenêtre (<paramref name="fiche"/> null : nouveau modèle) ; vrai si le modèle a été enregistré.</summary>
    public static async Task<bool> OuvrirAsync(
        Form? proprietaire, FicheModele? fiche, ServiceModelesSortie modeles, ServiceTypesSortie typesSortie, ServiceMoniteurs moniteurs, GardeGestion garde)
    {
        var m = fiche?.Modele;
        var types = await typesSortie.ListerProposesAsync(m?.TypeId);
        var responsables = (await moniteurs.ListerTousAsync()).Where(r => r.Actif || r.Id == m?.MoniteurId).ToList();
        var genreInitial = responsables.FirstOrDefault(r => r.Id == m?.MoniteurId)?.Genre ?? GenreResponsable.Classe;

        // --- Champs ---
        var nom = new ChampSaisie { Valeur = m?.Nom ?? "", TexteIndicatif = "Ex. Cours du lundi" };
        nom.Champ.AccessibleName = "Nom du modèle";
        nom.Champ.MaxLength = ModeleSortie.LongueurMaxNom;
        var jours = new ChoixPuces { AccessibleName = "Jour habituel", Margin = new Padding(0, 0, 0, 6) };
        jours.Groupe("", new[] { (SansJour, "Aucun") }.Concat(ServiceModelesSortie.Semaine.Select(j =>
            (((int)j).ToString(Libelles.Culture), ServiceModelesSortie.NomJour(j)[..3]))));
        jours.Valeur = m?.Jour is { } jour ? ((int)jour).ToString(Libelles.Culture) : fiche is null ? ((int)DayOfWeek.Monday).ToString(Libelles.Culture) : SansJour;
        var aideJour = Ui.Libelle("Le modèle passe en premier ce jour-là et la sortie est datée du prochain.", Theme.Police(9f), Theme.TexteSecondaire);
        aideJour.Margin = new Padding(0, 0, 0, 4);

        var choixType = new ChoixTypeSortie { ColonnesMax = 3, Margin = new Padding(0, 0, 0, 14) };
        choixType.Remplir(types.Select(t => new ChoixTypeSortie.Element(t.Id, t.Nom, Icones.PourTypeSortie(t.Pictogramme), Theme.CouleurTypeSortie(t.Pictogramme))));
        choixType.Valeur = types.FirstOrDefault(t => t.Id == m?.TypeId)?.Id ?? types.FirstOrDefault()?.Id;
        var lieu = new ChampSaisie { Valeur = m?.Lieu ?? "", TexteIndicatif = "Nom de la piscine, du site…", Icone = Icones.Lieu };
        lieu.Champ.AccessibleName = "Lieu";
        lieu.Champ.MaxLength = ModeleSortie.LongueurMaxLieu;
        var intitule = new ChampSaisie { Valeur = m?.Intitule ?? "" };
        intitule.Champ.AccessibleName = "Intitulé";
        intitule.Champ.MaxLength = ModeleSortie.LongueurMaxIntitule;
        var aideIntitule = Ui.Libelle("Vide : construit à partir du type, du jour et du responsable.", Theme.Police(9f), Theme.TexteSecondaire);
        aideIntitule.Margin = new Padding(0, 6, 0, 4);

        var genre = new Bascule("Enseignant", "Classe") { Compact = true, AccessibleName = "Type de responsable", Margin = new Padding(0, 0, 0, 12) };
        var personnes = new ChoixPersonnes { LignesVisibles = 3, Margin = new Padding(0, 0, 0, 4) };
        var choixParGenre = new Dictionary<GenreResponsable, long?> { [genreInitial] = m?.MoniteurId };

        var utilisations = new SelecteurNombre("Utilisations comptées par équipement", n => Libelles.Pluriel(n, "utilisation"))
        {
            Nombre = fiche?.Utilisations ?? types.FirstOrDefault()?.Utilisations ?? 1,
            Width = 230,
            Anchor = AnchorStyles.Left,
        };
        garde.Lier(utilisations, ActionProtegee.CompteurSortie);
        var aideUtilisations = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
        aideUtilisations.MaximumSize = new Size(200, 0);
        aideUtilisations.Margin = new Padding(14, 0, 0, 0);
        var unParCategorie = new OptionUnParCategorie(garde) { Actif = m?.UnParCategorie ?? true, Margin = new Padding(0, 12, 0, 12) };
        var notes = new ChampSaisie(multiligne: true) { Valeur = m?.Notes ?? "", TexteIndicatif = "Ex. groupe N1, 20 h 30" };
        notes.Champ.AccessibleName = "Notes";
        notes.Champ.MaxLength = ModeleSortie.LongueurMaxNotes;

        // --- Sections ---
        var sectionModele = new SectionFormulaire(1, "Le modèle");
        sectionModele.Champ("Nom du modèle *", nom);
        sectionModele.Ajouter(FenetreFormulaire.LibelleChamp("Jour habituel"));
        sectionModele.Ajouter(jours);
        sectionModele.Ajouter(aideJour);

        var sectionSortie = new SectionFormulaire(2, "La sortie");
        sectionSortie.Ajouter(FenetreFormulaire.LibelleChamp("Type"));
        sectionSortie.Ajouter(choixType);
        sectionSortie.Champ("Lieu", lieu);
        sectionSortie.Champ("Intitulé", intitule);
        sectionSortie.Ajouter(aideIntitule);

        var sectionResponsable = new SectionFormulaire(3, "Responsable");
        var rangeeGenre = Ui.Rangee(true, genre);
        rangeeGenre.Margin = Padding.Empty;
        sectionResponsable.Ajouter(rangeeGenre);
        sectionResponsable.Ajouter(personnes);

        var regles = new SectionFormulaire(4, "Règles et notes") { TexteDroite = "facultatif" };
        regles.Ajouter(FenetreFormulaire.LibelleChamp("Compteur d'utilisation"));
        var rangeeCompteur = Ui.Colonnes((utilisations, SizeType.AutoSize, 0), (aideUtilisations, SizeType.Percent, 100));
        utilisations.Dock = aideUtilisations.Dock = DockStyle.None;
        utilisations.Anchor = aideUtilisations.Anchor = AnchorStyles.Left;
        rangeeCompteur.AutoSize = true;
        rangeeCompteur.RowStyles[0] = new RowStyle(SizeType.AutoSize);
        regles.Ajouter(rangeeCompteur);
        regles.Ajouter(unParCategorie);
        regles.Ajouter(FenetreFormulaire.LibelleChamp("Notes"));
        notes.Margin = Padding.Empty;
        regles.Ajouter(notes).Height = 80;

        var corps = FormulaireSortie.Corps(FormulaireSortie.Pile(sectionModele, sectionSortie), FormulaireSortie.Pile(sectionResponsable, regles));

        // --- Saisie ---
        var maj = false;
        int? utilisationsChoisies = fiche is { UtilisationsModifiees: true } ? fiche.Utilisations : null;
        TypeDeSortie? TypeChoisi() => types.FirstOrDefault(t => t.Id == choixType.Valeur);
        GenreResponsable GenreChoisi() => genre.SelectedIndex == 1 ? GenreResponsable.Classe : GenreResponsable.Moniteur;
        DayOfWeek? JourChoisi() => jours.Valeur is { } v && v != SansJour ? (DayOfWeek)int.Parse(v, Libelles.Culture) : null;

        void RemplirPersonnes()
        {
            var g = GenreChoisi();
            personnes.AccessibleName = g == GenreResponsable.Classe ? "Classe responsable" : "Enseignant responsable";
            personnes.TexteVide = g == GenreResponsable.Classe
                ? "Aucune classe : ajoutez-en une dans Membres › Classes."
                : "Aucun enseignant : dans Membres › Adhérents, activez « Enseignant » sur sa fiche.";
            personnes.Remplir(
                responsables.Where(r => r.Genre == g).Select((r, i) => new ChoixPersonnes.Element(
                    r.Id, EcranMoniteurs.Avatar.Initiales(r), r.NomComplet, FormulaireSortie.Detail(r), EcranMoniteurs.Avatar.Degrade(r, i), r.EstClasse, r.Actif)),
                choixParGenre.GetValueOrDefault(g));
        }

        void Actualiser()
        {
            if (maj)
            {
                return;
            }

            maj = true;
            if (jours.Valeur is null)
            {
                jours.Valeur = SansJour; // Re-clic sur le jour choisi : aucun jour fixe.
            }

            maj = false;
            var type = TypeChoisi();
            var n = utilisationsChoisies ?? type?.Utilisations ?? 1;
            utilisations.Nombre = n;
            aideUtilisations.Text = type is null ? ""
                : n == type.Utilisations ? $"Valeur par défaut du type {type.Nom}."
                : $"Par défaut pour {type.Nom} : {type.Utilisations}.";
            unParCategorie.Visible = type?.Nominatif == true;
            var responsable = responsables.FirstOrDefault(r => r.Id == personnes.Valeur);
            var date = ServiceModelesSortie.ProchaineDate(JourChoisi(), DateOnly.FromDateTime(DateTime.Today));
            intitule.TexteIndicatif = $"Vide = {ServiceSortie.IntitulePropose(type, JourChoisi() is null ? null : date, responsable, lieu.Valeur)}";
        }

        choixType.ValeurChangee += (_, _) =>
        {
            utilisationsChoisies = null; // Nouveau type : ses utilisations.
            Actualiser();
        };
        jours.ValeurChangee += (_, _) => Actualiser();
        lieu.Champ.TextChanged += (_, _) => Actualiser();
        genre.Changement += (_, _) =>
        {
            RemplirPersonnes();
            Actualiser();
        };
        personnes.ValeurChangee += (_, _) =>
        {
            choixParGenre[GenreChoisi()] = personnes.Valeur;
            Actualiser();
        };
        utilisations.Demande += (_, n) =>
        {
            n = Math.Clamp(n, TypeDeSortie.UtilisationsMin, TypeDeSortie.UtilisationsMax);
            if (n != utilisations.Nombre && garde.Exiger(utilisations.FindForm(), ActionProtegee.CompteurSortie, "Changer les utilisations"))
            {
                utilisationsChoisies = n == TypeChoisi()?.Utilisations ? null : n;
            }

            Actualiser();
        };

        if (genreInitial == GenreResponsable.Classe)
        {
            genre.SelectedIndex = 1; // Changement → RemplirPersonnes.
        }

        RemplirPersonnes();

        using var f = new FenetreFormulaire(fiche is null ? "Nouveau modèle" : "Modifier le modèle", "Enregistrer le modèle", () =>
            modeles.EnregistrerAsync(m?.Id, new SaisieModele(
                nom.Valeur,
                JourChoisi(),
                choixType.Valeur,
                personnes.Valeur,
                lieu.Valeur,
                intitule.Valeur,
                utilisations.Nombre,
                !unParCategorie.Visible || unParCategorie.Actif,
                notes.Valeur)), Largeur, Marge);
        f.Separations = true;
        f.EnTete("Une sortie qui revient toujours pareil, à créer ensuite en un clic.", icone: Icones.Calendrier);
        f.Valider.Icone = Icones.Coche;
        f.Ajouter(corps);
        Actualiser();
        f.Shown += (_, _) =>
        {
            nom.Champ.Focus();
            nom.Champ.SelectionStart = nom.Champ.TextLength;
        };
        return f.ShowDialog(proprietaire) == DialogResult.OK;
    }
}
