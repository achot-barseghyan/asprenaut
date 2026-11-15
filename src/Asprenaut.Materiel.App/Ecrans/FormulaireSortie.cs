using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtre « Modifier la sortie » (maquette) : carte de la sortie en en-tête (calendrier teinté du type, intitulé, type,
/// responsable · lieu), puis deux colonnes de sections numérotées — 1 La sortie (type en tuiles, date, intitulé, lieu),
/// 2 Notes | 3 Responsable (Moniteur | Classe puis cartes), 4 Règles du matériel (utilisations, un équipement par catégorie).
/// Un point turquoise marque chaque valeur changée ; le bas résume les modifications et « Enregistrer » attend qu'il y en ait.
/// Les règles sont dans <see cref="ServiceSortie.ModifierAsync"/>.
/// </summary>
internal static class FormulaireSortie
{
    private const int Largeur = 960;
    private const int Marge = 24;
    private const int LargeurGauche = 452;
    private const int EcartColonnes = 14;
    private const int LongueurNotes = 400;

    // Raccourcis du compteur (maquette) : une plongée, une journée, un week-end.
    private static readonly (int Nombre, string Texte)[] Raccourcis = [(1, "1 plongée"), (2, "Journée"), (4, "Week-end")];

    /// <summary>Ouvre la fenêtre ; vrai si la sortie a été modifiée.</summary>
    public static async Task<bool> ModifierAsync(
        Form? proprietaire, long sortieId, ServiceSortie sorties, ServiceTypesSortie typesSortie, ServiceMoniteurs moniteurs, GardeGestion garde)
    {
        if (await sorties.ObtenirAsync(sortieId) is not { } s)
        {
            return false;
        }

        // Types proposés, plus celui de la sortie s'il a été masqué depuis ; moniteurs et classes actifs, plus le responsable
        // actuel s'il est devenu inactif.
        var types = await typesSortie.ListerProposesAsync(s.TypeId);
        var responsables = (await moniteurs.ListerTousAsync()).Where(m => m.Actif || m.Id == s.MoniteurId).ToList();
        var genreInitial = responsables.FirstOrDefault(m => m.Id == s.MoniteurId)?.Genre ?? GenreResponsable.Moniteur;

        // --- Champs ---
        var choixType = new ChoixTypeSortie();
        choixType.Remplir(types.Select(t => new ChoixTypeSortie.Element(t.Id, t.Nom, Icones.PourTypeSortie(t.Pictogramme), Theme.CouleurTypeSortie(t.Pictogramme))));
        choixType.Valeur = types.FirstOrDefault(t => t.Id == s.TypeId)?.Id ?? types.FirstOrDefault()?.Id;
        var date = new ChampDate { Date = s.DateSortie };
        var intitule = new ChampSaisie { Valeur = s.Intitule, TexteIndicatif = "Ex. Week-end Marseille" };
        intitule.Champ.AccessibleName = "Intitulé";
        var lieu = new ChampSaisie { Valeur = s.Lieu, TexteIndicatif = "Nom de la piscine, du site…", Icone = Icones.Lieu };
        lieu.Champ.AccessibleName = "Lieu";
        var notes = new ChampSaisie(multiligne: true) { Valeur = s.Notes, TexteIndicatif = "Ex. groupe N1, 8 élèves, départ du club à 7 h" };
        notes.Champ.AccessibleName = "Notes";
        notes.Champ.MaxLength = Math.Max(LongueurNotes, s.Notes.Length);
        var compteurNotes = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
        compteurNotes.Anchor = AnchorStyles.Right;
        compteurNotes.Margin = new Padding(0, 6, 0, 12);

        var genre = new Bascule("Moniteur", "Classe") { Compact = true, AccessibleName = "Type de responsable", Margin = new Padding(0, 0, 0, 12) };
        var personnes = new ChoixPersonnes { LignesVisibles = 3, Margin = new Padding(0, 0, 0, 14) };
        var choixParGenre = new Dictionary<GenreResponsable, long?> { [genreInitial] = s.MoniteurId };

        var utilisations = new SelecteurNombre("Utilisations comptées par équipement", n => n.ToString(Libelles.Culture)) { Nombre = s.Utilisations, Width = 170, Anchor = AnchorStyles.Left };
        garde.Lier(utilisations, ActionProtegee.CompteurSortie);
        var aideUtilisations = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);
        aideUtilisations.MaximumSize = new Size(Largeur - (2 * Marge) - LargeurGauche - EcartColonnes - 32 - 170 - 16, 0);
        aideUtilisations.Anchor = AnchorStyles.Left;
        aideUtilisations.Margin = new Padding(16, 0, 0, 0);
        var raccourcis = new ChoixPuces { Margin = new Padding(0, 12, 0, 14), AccessibleName = "Raccourcis du compteur" };
        raccourcis.Groupe("", Raccourcis.Select(r => (r.Nombre.ToString(Libelles.Culture), $"{r.Texte} · {r.Nombre}")));

        var unParCategorie = new OptionUnParCategorie(garde)
        {
            Actif = s.UnParCategorie,
            Visible = s.Nominatif,
            Margin = new Padding(0, 0, 0, 12),
        };

        // --- Libellés marqués d'un point quand la valeur change ---
        static LibelleObligatoire Libelle(string texte, bool obligatoire) =>
            new(texte, Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair) { AvecPoint = true, Etoile = obligatoire, Margin = new Padding(0, 0, 0, 8) };
        var lType = Libelle("Type", false);
        var lDate = Libelle("Date", true);
        var lIntitule = Libelle("Intitulé", true);
        var lLieu = Libelle("Lieu", false);
        var lUtilisations = Libelle("Utilisations comptées par équipement", false);

        // --- Sections ---
        var sortie = new SectionFormulaire(1, "La sortie");
        sortie.Ajouter(lType);
        choixType.Margin = new Padding(0, 0, 0, 14);
        sortie.Ajouter(choixType);
        sortie.DeuxChamps(lDate, date, lIntitule, intitule);
        sortie.Champ(lLieu, lieu);

        var sectionNotes = new SectionFormulaire(2, "Notes") { TexteDroite = "facultatif" };
        notes.Margin = Padding.Empty;
        sectionNotes.Ajouter(notes).Height = 104;
        sectionNotes.Ajouter(compteurNotes);
        compteurNotes.Dock = DockStyle.None; // À droite, sous le champ (Anchor après Dock, qui le remet à zéro).
        compteurNotes.Anchor = AnchorStyles.Right;

        var sectionResponsable = new SectionFormulaire(3, "Responsable");
        var rangeeGenre = Ui.Rangee(true, genre);
        rangeeGenre.Margin = Padding.Empty;
        sectionResponsable.Ajouter(rangeeGenre);
        sectionResponsable.Ajouter(personnes);

        var regles = new SectionFormulaire(4, "Règles du matériel");
        regles.Ajouter(lUtilisations);
        var rangeeCompteur = Ui.Colonnes((utilisations, SizeType.AutoSize, 0), (aideUtilisations, SizeType.Percent, 100));
        utilisations.Dock = aideUtilisations.Dock = DockStyle.None;
        utilisations.Anchor = aideUtilisations.Anchor = AnchorStyles.Left;
        rangeeCompteur.AutoSize = true;
        rangeeCompteur.RowStyles[0] = new RowStyle(SizeType.AutoSize);
        regles.Ajouter(rangeeCompteur);
        regles.Ajouter(raccourcis);
        regles.Ajouter(unParCategorie);

        var corps = Corps(Pile(sortie, sectionNotes), Pile(sectionResponsable, regles));

        // --- Mise à jour pendant la saisie ---
        var entete = new EnTeteSortie();
        FenetreFormulaire? fenetre = null;
        var maj = false;

        TypeDeSortie? TypeChoisi() => types.FirstOrDefault(t => t.Id == choixType.Valeur);
        GenreResponsable GenreChoisi() => genre.SelectedIndex == 1 ? GenreResponsable.Classe : GenreResponsable.Moniteur;
        Moniteur? Responsable() => responsables.FirstOrDefault(m => m.Id == personnes.Valeur);

        void RemplirPersonnes()
        {
            var g = GenreChoisi();
            var liste = responsables.Where(m => m.Genre == g).ToList();
            personnes.AccessibleName = g == GenreResponsable.Classe ? "Classe responsable" : "Moniteur responsable";
            personnes.TexteVide = g == GenreResponsable.Classe
                ? "Aucune classe : ajoutez-en une dans Membres › Classes."
                : "Aucun moniteur : ajoutez-en un dans Membres › Moniteurs.";
            personnes.Remplir(
                liste.Select((m, i) => new ChoixPersonnes.Element(
                    m.Id,
                    EcranMoniteurs.Avatar.Initiales(m),
                    m.NomComplet,
                    Detail(m),
                    EcranMoniteurs.Avatar.Degrade(m, i),
                    m.EstClasse,
                    m.Actif)),
                choixParGenre.GetValueOrDefault(g));
        }

        void Actualiser()
        {
            if (maj)
            {
                return;
            }

            var type = TypeChoisi();
            var n = utilisations.Nombre;

            // Raccourci allumé s'il correspond au compteur.
            maj = true;
            raccourcis.Valeur = Raccourcis.Any(r => r.Nombre == n) ? n.ToString(Libelles.Culture) : null;
            maj = false;
            aideUtilisations.Text = $"Chaque équipement rendu ajoutera {Libelles.Pluriel(n, "utilisation")} à son compteur."
                + (type is not null && type.Utilisations != n ? $" Valeur du type {type.Nom} : {type.Utilisations}." : "");
            compteurNotes.Text = $"{notes.Valeur.Length} / {LongueurNotes}";

            var r = Responsable();
            var detail = r is null ? "Responsable à choisir" : $"{(r.EstClasse ? "Classe" : "Moniteur")} : {r.NomComplet}";
            if (lieu.Valeur.Trim().Length > 0)
            {
                detail += $" · {lieu.Valeur.Trim()}";
            }

            entete.Afficher(date.Date, intitule.Valeur, type?.Nom ?? "", type is null ? Theme.Accent : Theme.CouleurTypeSortie(type.Pictogramme), detail);

            // Modifications par rapport à la sortie enregistrée.
            var changes = new List<string>();
            void Comparer(bool change, string nom, LibelleObligatoire? libelle = null, SectionFormulaire? section = null)
            {
                if (change)
                {
                    changes.Add(nom);
                }

                if (libelle is not null)
                {
                    libelle.Modifie = change;
                }

                if (section is not null)
                {
                    section.Modifie = change;
                }
            }

            Comparer(choixType.Valeur != s.TypeId, "type", lType);
            Comparer(date.Date != s.DateSortie, "date", lDate);
            Comparer(intitule.Valeur.Trim() != s.Intitule.Trim(), "intitulé", lIntitule);
            Comparer(lieu.Valeur.Trim() != s.Lieu.Trim(), "lieu", lLieu);
            Comparer(personnes.Valeur != s.MoniteurId, "responsable", section: sectionResponsable);
            Comparer(n != s.Utilisations, "utilisations", lUtilisations);
            Comparer(unParCategorie.Visible && unParCategorie.Actif != s.UnParCategorie, "règle");
            Comparer(notes.Valeur.Trim() != s.Notes.Trim(), "notes", section: sectionNotes);
            regles.Modifie = unParCategorie.Visible && unParCategorie.Actif != s.UnParCategorie;

            if (fenetre is not null)
            {
                fenetre.Resume(
                    changes.Count == 0 ? "Aucune modification" : $"{Libelles.Pluriel(changes.Count, "modification")} : {string.Join(", ", changes)}",
                    changes.Count > 0);
                fenetre.Valider.Enabled = changes.Count > 0;
            }
        }

        choixType.ValeurChangee += (_, _) =>
        {
            // Un autre type reprend ses utilisations et son réglage nominatif ; revenir au type d'origine reprend ceux de la sortie.
            if (TypeChoisi() is { } choisi)
            {
                utilisations.Nombre = choisi.Id == s.TypeId ? s.Utilisations : choisi.Utilisations;
                unParCategorie.Visible = choisi.Id == s.TypeId ? s.Nominatif : choisi.Nominatif;
            }

            Actualiser();
        };
        date.DateChangee += (_, _) => Actualiser();
        intitule.Champ.TextChanged += (_, _) => Actualiser();
        lieu.Champ.TextChanged += (_, _) => Actualiser();
        notes.Champ.TextChanged += (_, _) => Actualiser();
        unParCategorie.ActifChange += (_, _) => Actualiser();
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

        // Compteur : en mode consultation, le changer demande le code gestionnaire (cadenas sur le sélecteur).
        void ChangerUtilisations(int n)
        {
            n = Math.Clamp(n, TypeDeSortie.UtilisationsMin, TypeDeSortie.UtilisationsMax);
            if (n != utilisations.Nombre && garde.Exiger(utilisations.FindForm(), ActionProtegee.CompteurSortie, "Changer les utilisations"))
            {
                utilisations.Nombre = n;
            }
        }

        utilisations.Demande += (_, n) =>
        {
            ChangerUtilisations(n);
            Actualiser();
        };
        raccourcis.ValeurChangee += (_, _) =>
        {
            if (maj)
            {
                return;
            }

            if (raccourcis.Valeur is { } v)
            {
                ChangerUtilisations(int.Parse(v, Libelles.Culture));
            }

            Actualiser(); // Remet la puce d'après le compteur (refus du code, re-clic sur la puce allumée).
        };

        if (genreInitial == GenreResponsable.Classe)
        {
            genre.SelectedIndex = 1; // Changement → RemplirPersonnes.
        }
        else
        {
            RemplirPersonnes();
        }

        using var f = new FenetreFormulaire("Modifier la sortie", "Enregistrer", () =>
            date.Date is not { } d
                ? Task.FromResult(ResultatOperation.Echec("Date invalide : écrivez-la comme 08/11/2026."))
                : sorties.ModifierAsync(sortieId, new NouvelleSortie(
                    d,
                    s.Type,
                    intitule.Valeur,
                    personnes.Valeur,
                    lieu.Valeur,
                    notes.Valeur,
                    choixType.Valeur,
                    utilisations.Nombre,
                    unParCategorie.Actif)), Largeur, Marge);
        fenetre = f;
        f.Separations = true;
        f.EnTetePersonnalise(entete);
        f.Valider.Icone = Icones.Coche;
        f.Ajouter(corps);
        Actualiser();
        f.Shown += (_, _) =>
        {
            intitule.Champ.Focus();
            intitule.Champ.SelectionStart = intitule.Champ.TextLength;
        };
        return f.ShowDialog(proprietaire) == DialogResult.OK;
    }

    /// <summary>« MF1 · Inspecteur TIV » ; classe : « Classe » ; inactif signalé.</summary>
    private static string Detail(Moniteur m)
    {
        var parties = new List<string>();
        if (m.EstClasse)
        {
            parties.Add("Classe");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(m.Niveau))
            {
                parties.Add(m.Niveau.Trim());
            }

            if (m.InspecteurTiv)
            {
                parties.Add("Inspecteur TIV");
            }

            if (parties.Count == 0)
            {
                parties.Add("Moniteur");
            }
        }

        if (!m.Actif)
        {
            parties.Add(m.EstClasse ? "inactive" : "inactif");
        }

        return string.Join(" · ", parties);
    }

    /// <summary>Colonne de sections, de la hauteur de son contenu.</summary>
    private static TableLayoutPanel Pile(params Control[] sections)
    {
        var colonne = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Dock = DockStyle.Fill,
        };
        colonne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var section in sections)
        {
            FenetreFormulaire.AjouterLigne(colonne, section, SizeType.AutoSize, 0);
        }

        return colonne;
    }

    /// <summary>Deux colonnes de largeur fixe (maquette : 1,1 / 1), alignées en haut.</summary>
    private static TableLayoutPanel Corps(Control gauche, Control droite)
    {
        var corps = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        corps.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LargeurGauche));
        corps.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Largeur - (2 * Marge) - LargeurGauche));
        corps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        droite.Margin = new Padding(EcartColonnes, 0, 0, 0);
        corps.Controls.Add(gauche, 0, 0);
        corps.Controls.Add(droite, 1, 0);
        // Hauteur de leur contenu, en haut de la rangée : en Dock Fill, la colonne la plus courte étirait sa dernière section.
        gauche.Dock = droite.Dock = DockStyle.None;
        gauche.Anchor = droite.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        return corps;
    }
}
