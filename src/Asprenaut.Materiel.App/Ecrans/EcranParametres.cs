using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;
using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Paramètres : pistolet, règles des sorties, données et sauvegardes, club, à propos.
/// Chaque changement est enregistré tout de suite (pas de bouton « Enregistrer »).
/// </summary>
public sealed class EcranParametres : EcranBase
{
    private readonly ServiceReglages _reglages;
    private readonly IServiceSauvegarde _sauvegardes;
    private readonly ISauvegardeComplete _complete;
    private readonly INavigation _navigation;
    private readonly ParametresApplication _parametres;
    private readonly BandeauMessage _etatSauvegarde = new() { Margin = new Padding(0, 0, 0, 18) };
    private readonly ChampSaisie _dossier = new();
    private readonly Label _dernierCode = Ui.Libelle("", Theme.Police(9.75f), Theme.RetourClair);
    private readonly Panel _cadre = new() { BackColor = Theme.Fond, Margin = new Padding(0, 4, 0, 0) };
    private CarteSection? _carteAffichage;

    /// <summary>Paramètre de navigation : rouvrir l'écran sur la carte « Affichage » (après un changement de thème).</summary>
    public const string VersAffichage = "affichage";

    private readonly ServiceGestion _gestion;
    private readonly GardeGestion _garde;
    private readonly ServiceExport _export;
    private readonly ServiceCompteur _compteur;
    private readonly ServiceTypesSortie _typesSortie;
    private readonly ServicePrestataires _prestataires;
    private readonly ServiceInventaire _inventaire;

    public EcranParametres(ServiceReglages reglages, IServiceSauvegarde sauvegardes, ISauvegardeComplete complete, INavigation navigation, ParametresApplication parametres, ServiceGestion gestion, GardeGestion garde, ServiceExport export, ServiceCompteur compteur, ServiceTypesSortie typesSortie, ServicePrestataires prestataires, ServiceInventaire inventaire)
    {
        _export = export;
        _compteur = compteur;
        _typesSortie = typesSortie;
        _prestataires = prestataires;
        _inventaire = inventaire;
        _gestion = gestion;
        _garde = garde;
        _reglages = reglages;
        _sauvegardes = sauvegardes;
        _complete = complete;
        _navigation = navigation;
        _parametres = parametres;

        // Onglet « Paramètres » : le fonctionnement de l'application. L'onglet « Configuration du club » (les listes et données
        // propres au club) n'est construit qu'à sa première ouverture : la page s'ouvre plus vite.
        _grilleParametres = Grille(CarteSecurite(), CartePistolet(), CarteRegles(), CarteDonnees(), CarteAffichage(), CarteAPropos());

        // Cadre défilant vertical (la page est plus haute que l'écran).
        var cadre = _cadre;
        cadre.HorizontalScroll.Maximum = 0;
        cadre.AutoScroll = false;
        cadre.VerticalScroll.Visible = false;
        cadre.AutoScroll = true;
        Natif.ScrollbarsDuTheme(cadre);
        cadre.Controls.Add(_grilleParametres);

        var consultation = new BandeauConsultation(_garde, "Consultez librement. Toute modification des paramètres demande le code gestionnaire.");
        _garde.Lier(consultation, ActionProtegee.Parametres);

        _onglets.AccessibleName = "Onglets des paramètres";
        _onglets.Margin = new Padding(0, 0, 0, 18);
        _onglets.Changement += (_, _) => Executer(AfficherOngletAsync);
        _sousTitre = Ui.SousTitre(SousTitreParametres);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), _sousTitre)), SizeType.AutoSize, 0),
            (Ui.Rangee(true, _onglets), SizeType.AutoSize, 0),
            (consultation, SizeType.AutoSize, 0),
            (cadre, SizeType.Percent, 100)));
    }

    private const string SousTitreParametres = "Fonctionnement de l'application : sécurité, pistolet, règles des sorties, sauvegardes, affichage.";
    private const string SousTitreConfiguration = "Données propres au club : identité, types de sortie, compteurs, niveaux, prestataires, problèmes signalés.";

    private readonly Bascule _onglets = new("Paramètres", "Configuration du club");
    private readonly TableLayoutPanel _grilleParametres;
    private TableLayoutPanel? _grilleConfiguration;
    private Label _sousTitre = null!;

    /// <summary>
    /// Deux colonnes de cartes ; chaque rangée prend la hauteur de sa carte la plus haute. Toutes les cartes sauf « À propos »
    /// demandent le code gestionnaire.
    /// </summary>
    private TableLayoutPanel Grille(params CarteSection[] cartes)
    {
        var grille = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grille.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var carte in cartes.Where(c => c.AccessibleName != "À propos"))
        {
            _garde.Lier(carte, ActionProtegee.Parametres);
        }

        grille.SuspendLayout(); // Une seule mise en page pour toutes les cartes (sinon la grille se recalcule à chaque ajout).
        for (var i = 0; i < cartes.Length; i++)
        {
            if (i % 2 == 0)
            {
                grille.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            cartes[i].AutoSize = true;
            cartes[i].Dock = DockStyle.Fill;
            cartes[i].Margin = new Padding(i % 2 == 0 ? 0 : 10, 0, i % 2 == 0 ? 10 : 0, 20);
            grille.Controls.Add(cartes[i], i % 2, i / 2);
        }

        grille.ResumeLayout(true);
        return grille;
    }

    /// <summary>Affiche l'onglet choisi ; « Configuration du club » est construit et rempli la première fois.</summary>
    private async Task AfficherOngletAsync()
    {
        var configuration = _onglets.SelectedIndex == 1;
        _sousTitre.Text = configuration ? SousTitreConfiguration : SousTitreParametres;
        if (configuration && _grilleConfiguration is null)
        {
            _grilleConfiguration = Grille(CarteClub(), CarteTypesSortie(), CarteCompteur(), CarteNiveaux(), CartePrestataires(), CarteTuilesSignalement());
            _grilleConfiguration.Visible = false;
            Dessin.MettreALEchelle(_grilleConfiguration, this); // Construite après l'affichage : pixels logiques à convertir.
            _cadre.Controls.Add(_grilleConfiguration);
            await AfficherSeuilsAsync();
            await AfficherTypesAsync();
            await AfficherPrestatairesAsync();
            await ChargerCategoriesTuilesAsync();
            if (IsDisposed)
            {
                return;
            }
        }

        _cadre.SuspendLayout();
        _grilleParametres.Visible = !configuration;
        if (_grilleConfiguration is not null)
        {
            _grilleConfiguration.Visible = configuration;
        }

        _cadre.ResumeLayout(true);
        _cadre.AutoScrollPosition = Point.Empty;
    }

    public override string Titre => "Paramètres";

    public override EntreeMenu Menu => EntreeMenu.Parametres;

    protected override async Task ChargerAsync(object? parametre)
    {
        AfficherEtatSauvegarde();
        if (parametre is VersAffichage && _carteAffichage is { } carte)
        {
            // L'écran vient d'être recréé dans le nouveau thème : on revient sur la carte du choix, une fois que
            // les cartes ont pris leur hauteur (ajustements différés), et avant que l'écran soit montré.
            _versAffichage = true;
            for (var i = 0; i < 4 && !IsDisposed; i++)
            {
                await Task.Yield();
                PerformLayout();
            }

            AllerALaCarteAffichage(carte);
        }
    }

    private bool _versAffichage;

    /// <summary>Une fois l'écran en place (Dock = Fill), on vérifie qu'il est bien positionné sur la carte « Affichage ».</summary>
    protected override void OnDockChanged(EventArgs e)
    {
        base.OnDockChanged(e);
        if (_versAffichage && Dock == DockStyle.Fill && _carteAffichage is { } carte && IsHandleCreated)
        {
            _versAffichage = false;
            BeginInvoke(() => AllerALaCarteAffichage(carte));
        }
    }

    private void AllerALaCarteAffichage(Control carte)
    {
        if (IsDisposed || carte.Parent is not { } grille)
        {
            return;
        }

        // Position de la carte dans la page qui défile (la grille est décalée de la position de défilement).
        var haut = grille.Top - _cadre.AutoScrollPosition.Y + carte.Top;
        _cadre.AutoScrollPosition = new Point(0, Math.Max(0, haut - (int)Dessin.Echelle(this, 8)));
    }

    // ---------------------------------------------------------------- Affichage

    private CarteSection CarteAffichage()
    {
        var carte = new CarteSection("Affichage", Theme.EstSombre ? Icones.Lune : Icones.Soleil, Theme.AccentClair);
        carte.Ajouter(Ui.LibelleChamp("Thème de l'écran"));
        var aide = Ui.Libelle("Le thème clair se lit mieux en plein jour ou près d'une fenêtre.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.Margin = new Padding(0, 0, 0, 14);
        carte.Ajouter(aide);

        var sombre = new ApercuTheme(ModeAffichage.Sombre) { Margin = new Padding(0, 0, 8, 0) };
        var clair = new ApercuTheme(ModeAffichage.Clair) { Margin = new Padding(8, 0, 0, 0) };
        foreach (var apercu in new[] { sombre, clair })
        {
            var mode = apercu.Mode;
            apercu.Click += (_, _) => ChoisirTheme(mode);
        }

        var choix = Ui.Colonnes((sombre, SizeType.Percent, 50), (clair, SizeType.Percent, 50));
        choix.Height = ApercuTheme.Hauteur;
        carte.Ajouter(choix);
        _carteAffichage = carte;
        return carte;
    }

    private void ChoisirTheme(ModeAffichage mode)
    {
        if (mode == Theme.Mode || !Autoriser())
        {
            return;
        }

        var resultat = _reglages.ChangerAffichage(mode);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        // Le menu se refait tout de suite (MainForm) ; cet écran est recréé dans le nouveau thème.
        Theme.Appliquer(mode);
        _navigation.AllerA<EcranParametres>(VersAffichage);
    }

    // ---------------------------------------------------------------- Sécurité · mode gestion

    private CarteSection CarteSecurite()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Sécurité · mode gestion", Icones.Cadenas, Theme.Avertissement)
        {
            CouleurBordure = Theme.Teinte(Theme.Avertissement, 0.35f),
        };
        var intro = Ui.Libelle("Scanner les départs et retours reste libre pour tous. Ajouter, modifier ou supprimer demande le mode gestion.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        intro.Margin = new Padding(0, 0, 0, 6);
        carte.Ajouter(intro);

        if (_gestion.ProtectionActive)
        {
            var points = Ui.Libelle("••••••", Theme.PoliceGrasse(13f), Theme.TexteDoux);
            points.Margin = new Padding(0, 8, 14, 0);
            points.AccessibleName = "Code masqué";
            var changer = new BoutonArrondi("Changer", StyleBouton.Secondaire) { Height = Theme.HauteurCliquable, Margin = Padding.Empty };
            changer.Click += (_, _) => ChangerCode();
            carte.Ajouter(new LigneReglage("Code gestionnaire", "4 à 6 chiffres, partagé avec le bureau", Ui.Rangee(true, points, changer), filet: false));
        }
        else
        {
            var definir = new BoutonArrondi("Définir un code", StyleBouton.Principal, Icones.Cadenas) { Height = Theme.HauteurCliquable, Margin = Padding.Empty };
            definir.Click += (_, _) => ChangerCode();
            carte.Ajouter(new LigneReglage("Code gestionnaire", "Aucun code : tout le monde peut tout modifier", definir, filet: false));
        }

        var duree = new ListeDeroulante { Width = 150, AccessibleName = "Rester déverrouillé" };
        foreach (var m in Reglages.MinutesDeverrouillagePossibles)
        {
            duree.Items.Add(new Choix<int>(m, $"{m} min"));
        }

        duree.SelectedIndex = Math.Max(0, Array.IndexOf(Reglages.MinutesDeverrouillagePossibles, r.MinutesDeverrouillage));
        ProtegerListe(duree, () => Array.IndexOf(Reglages.MinutesDeverrouillagePossibles, _reglages.Actuels.MinutesDeverrouillage), choix =>
        {
            if (choix is Choix<int> c)
            {
                _reglages.ChangerMinutesDeverrouillage(c.Valeur);
            }
        });
        carte.Ajouter(new LigneReglage("Rester déverrouillé", "Sans activité, l'appli repasse seule en consultation", duree));
        carte.Ajouter(Interrupteur("Verrouiller à la fermeture", "Au prochain lancement, l'appli démarre en consultation", r.VerrouillerALaFermeture,
            v => _reglages.Modifier(x => x.VerrouillerALaFermeture = v)));
        carte.Ajouter(Interrupteur("Code pour clôturer avec des manquants", "Évite de clôturer trop vite une sortie incomplète", r.CodePourCloturerAvecManquants,
            v => _reglages.Modifier(x => x.CodePourCloturerAvecManquants = v)));

        var titreActions = Ui.LibelleChamp("Actions protégées");
        titreActions.Margin = new Padding(0, 14, 0, 8);
        carte.Ajouter(titreActions);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        foreach (var a in new[] { ActionProtegee.Materiel, ActionProtegee.Moniteurs, ActionProtegee.SupprimerSortie, ActionProtegee.Revisions, ActionProtegee.CompteurSortie, ActionProtegee.Parametres, ActionProtegee.RestaurerSauvegarde })
        {
            actions.Controls.Add(new Pastille(ServiceGestion.Libelle(a), Theme.Teinte(Theme.Avertissement, 0.10f), Theme.Avertissement, police: Theme.PoliceTitre(9f), hauteur: 30)
            {
                Margin = new Padding(0, 0, 6, 6),
            });
        }

        carte.Ajouter(actions);
        var libres = Ui.Libelle("Libres pour tous : créer ou modifier une sortie en préparation, scanner départ et retour, consulter. Tous les paramètres demandent le code.",
            Theme.Police(9f), Theme.TexteSecondaire);
        libres.Margin = new Padding(0, 2, 0, 0);
        carte.Ajouter(libres);

        if (_gestion.ProtectionActive)
        {
            var retirer = new BoutonArrondi("Retirer le code (plus rien de verrouillé)", StyleBouton.Lien) { Height = Theme.HauteurCliquable, Margin = new Padding(0, 8, 0, 0) };
            retirer.Click += (_, _) => RetirerCode();
            carte.Ajouter(retirer, pleineLargeur: false);
        }

        return carte;
    }

    /// <summary>Champ de code : chiffres seulement, masqués.</summary>
    private static ChampSaisie ChampCode(string nom)
    {
        var champ = new ChampSaisie();
        champ.Champ.UseSystemPasswordChar = true;
        champ.Champ.MaxLength = ServiceGestion.LongueurMax;
        champ.Champ.AccessibleName = nom;
        champ.Champ.KeyPress += (_, e) => e.Handled = !char.IsAsciiDigit(e.KeyChar) && !char.IsControl(e.KeyChar);
        return champ;
    }

    /// <summary>Premier code, ou changement : l'ancien code est alors demandé.</summary>
    private void ChangerCode()
    {
        var premier = !_gestion.ProtectionActive;
        var ancien = ChampCode("Code actuel");
        var nouveau = ChampCode("Nouveau code");
        var confirmation = ChampCode("Nouveau code, encore une fois");
        using var fenetre = new FenetreFormulaire(premier ? "Définir le code gestionnaire" : "Changer le code gestionnaire", "Enregistrer",
            () => Task.FromResult(_gestion.DefinirCode(premier ? null : ancien.Valeur, nouveau.Valeur, confirmation.Valeur)));
        if (!premier)
        {
            fenetre.Champ("Code actuel", ancien);
        }

        fenetre.DeuxChamps("Nouveau code (4 à 6 chiffres)", nouveau, "Encore une fois", confirmation);
        var aide = Ui.Libelle(premier
                ? "Dès maintenant, ajouter, modifier ou supprimer demandera ce code. Notez-le et donnez-le au bureau."
                : "Le nouveau code remplace l'ancien pour tout le monde.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(460, 0);
        fenetre.Ajouter(aide);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Dialogues.Information(FindForm(), premier ? "Code gestionnaire défini." : "Code gestionnaire changé.");
            _navigation.AllerA<EcranParametres>(); // La carte Sécurité change d'aspect.
        }
    }

    private void RetirerCode()
    {
        var ancien = ChampCode("Code actuel");
        using var fenetre = new FenetreFormulaire("Retirer le code gestionnaire", "Retirer le code",
            () => Task.FromResult(_gestion.RetirerCode(ancien.Valeur)));
        fenetre.Champ("Code actuel", ancien);
        var aide = Ui.Libelle("Sans code, tout le monde peut ajouter, modifier ou supprimer.", Theme.Police(9.75f), Theme.TexteSecondaire);
        fenetre.Ajouter(aide);
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _navigation.AllerA<EcranParametres>();
        }
    }

    // ---------------------------------------------------------------- Pistolet

    private CarteSection CartePistolet()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Pistolet scanner", Icones.Scanner, Theme.AccentClair);

        // Zone d'essai : on scanne une étiquette, on voit ce que l'application en lit.
        var essai = new ChampSaisie { TexteIndicatif = "Scannez une étiquette ici…" };
        essai.Champ.Font = Theme.PoliceCode(11.25f);
        essai.Champ.AccessibleName = "Tester le pistolet";
        essai.Margin = new Padding(0, 0, 0, 10);
        ValidationSansEntree? sansEntree = null;
        void Lire()
        {
            sansEntree?.Annuler();
            var brut = essai.Valeur.Trim();
            var code = Saisie.CodeScanne.Nettoyer(brut, _reglages.Actuels.PrefixeEtiquette);
            _dernierCode.Text = code.Length == 0
                ? "Rien n'a été lu. Visez l'étiquette et réessayez."
                : brut == code ? $"✓ Dernier code lu : {code}" : $"✓ Dernier code lu : {brut} → {code}";
            _dernierCode.ForeColor = code.Length == 0 ? Theme.Erreur : Theme.RetourClair;
            essai.Valeur = "";
        }

        sansEntree = new ValidationSansEntree(essai.Champ, Lire);
        essai.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Lire();
            }
        };
        _dernierCode.Text = $"Ex. : {Saisie.CodeScanne.ExempleMedaille} → A24";
        _dernierCode.ForeColor = Theme.TexteSecondaire;
        _dernierCode.Margin = Padding.Empty;

        var zone = new CadrePointille { Margin = new Padding(0, 0, 0, 6) };
        zone.Ajouter(Ui.LibelleChamp("Tester le pistolet"));
        zone.Ajouter(essai);
        zone.Ajouter(_dernierCode);
        carte.Ajouter(zone);

        carte.Ajouter(Interrupteur("Sons de confirmation", "Son de confirmation au succès, son grave en cas d'erreur", r.SonsActifs, v => _reglages.Modifier(x => x.SonsActifs = v), filet: false));
        carte.Ajouter(Interrupteur("Garder le champ de scan actif", "Le curseur revient seul dans la zone de scan", r.GarderFocusScan, v => _reglages.Modifier(x => x.GarderFocusScan = v)));
        carte.Ajouter(Interrupteur("Ignorer les doubles lectures", "Même code lu deux fois en moins d'une seconde", r.IgnorerDoublesLectures, v => _reglages.Modifier(x => x.IgnorerDoublesLectures = v)));

        // Médailles du club : « (Detendeur) A24 Asprenaut Chalon 14 71-0025 » → A24 (voir CodeScanne.Lire).
        var medaille = new ChampSaisie { Size = new Size(160, Theme.HauteurCliquable), Valeur = "(Type) CODE …" };
        medaille.Champ.Font = Theme.PoliceCode(11.25f);
        medaille.Champ.TextAlign = HorizontalAlignment.Center;
        medaille.Champ.ReadOnly = true;
        medaille.Champ.TabStop = false;
        medaille.Champ.AccessibleName = "Lecture des médailles";
        carte.Ajouter(new LigneReglage(
            "Lecture des médailles",
            "Le code est le mot qui suit le type entre parenthèses. Sa lettre donne la catégorie : A = détendeur adulte, E = détendeur enfant, S = gilet stab.",
            medaille));
        return carte;
    }

    // ---------------------------------------------------------------- Règles

    private CarteSection CarteRegles()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Règles des sorties", Icones.Bouclier, Theme.DepartClair);

        var jours = new SelecteurNombre("Jours avant alerte", n => $"{n} j") { Nombre = r.JoursAvantAlerteRetour };
        jours.Demande += (_, n) =>
        {
            if (Autoriser())
            {
                jours.Nombre = _reglages.ChangerJoursAlerte(n);
            }
        };
        carte.Ajouter(new LigneReglage("Alerte si non rendu après", "La sortie passe en orange dans la liste", jours, filet: false));

        var revision = new ListeDeroulante { Width = 150, AccessibleName = "Prévenir avant une révision" };
        foreach (var j in Reglages.JoursRevisionPossibles)
        {
            revision.Items.Add(new Choix<int>(j, $"{j} jours"));
        }

        revision.SelectedIndex = Math.Max(0, Array.IndexOf(Reglages.JoursRevisionPossibles, r.JoursAvantRevision));
        ProtegerListe(revision, () => Array.IndexOf(Reglages.JoursRevisionPossibles, _reglages.Actuels.JoursAvantRevision), choix =>
        {
            if (choix is Choix<int> c)
            {
                _reglages.ChangerJoursRevision(c.Valeur);
            }
        });
        carte.Ajouter(new LigneReglage("Prévenir avant une révision", "Badge jaune dans l'inventaire", revision));

        carte.Ajouter(Interrupteur("Autoriser le départ si révision dépassée", "Le matériel part avec un avertissement jaune", r.DepartSiRevisionDepassee, v => _reglages.Modifier(x => x.DepartSiRevisionDepassee = v)));
        carte.Ajouter(Interrupteur("Confirmer avant de clôturer", "Demande validation s'il reste du matériel dehors", r.ConfirmerCloture, v => _reglages.Modifier(x => x.ConfirmerCloture = v)));
        return carte;
    }

    // ---------------------------------------------------------------- Compteur d'utilisation

    private readonly TableLayoutPanel _seuilsSpecifiques = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    /// <summary>
    /// Révision au bout d'un nombre de sorties (retours scannés) depuis le dernier contrôle : seuil par catégorie,
    /// seuils propres à un modèle ou à un équipement, prévenir à 90 %, bloquer au seuil.
    /// </summary>
    private CarteSection CarteCompteur()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Compteur d'utilisation", Icones.Revisions, Theme.Avertissement);
        var intro = Ui.Libelle("Chaque retour scanné ajoute les utilisations de la sortie (réglées par type dans « Types de sortie et utilisations »). La révision tombe au seuil ou à la date, le premier atteint.", Theme.Police(9.75f), Theme.TexteSecondaire);
        intro.MaximumSize = new Size(560, 0);
        intro.Margin = new Padding(0, 0, 0, 6);
        carte.Ajouter(intro);

        (string Categorie, string Libelle, string Aide)[] types =
        [
            ("Détendeurs", "Détendeurs", "Conseil : suivre la notice du fabricant"),
            ("Gilets", "Gilets stabilisateurs", "Inflateur, purges et sangles"),
            ("Ordinateurs", "Ordinateurs", "Pile et joints"),
            ("Blocs", "Blocs", "Robinetterie, en plus de la TIV"),
        ];
        for (var i = 0; i < types.Length; i++)
        {
            var (categorie, libelle, aide) = types[i];
            var seuil = new SelecteurNombre($"Seuil {libelle}", n => n == 0 ? "Désactivé" : $"{n} utilis.") { Width = 210, Nombre = r.SeuilsSorties.GetValueOrDefault(categorie) };
            seuil.Demande += (_, n) =>
            {
                if (Autoriser())
                {
                    var voulu = seuil.Nombre + (Math.Sign(n - seuil.Nombre) * ServiceCompteur.Pas);
                    if (_compteur.ChangerSeuilCategorie(categorie, voulu) is { Reussi: true, Id: { } retenu })
                    {
                        seuil.Nombre = (int)retenu;
                        Executer(AfficherCandidatsAsync);
                    }
                }
            };
            carte.Ajouter(new LigneReglage(libelle, aide, seuil, filet: i > 0));
        }

        carte.Ajouter(Interrupteur("Prévenir à 90 % du seuil", "L'équipement passe en « À planifier » dans Révisions", r.PrevenirAvantSeuil, v => _reglages.Modifier(x => x.PrevenirAvantSeuil = v)));
        carte.Ajouter(Interrupteur("Bloquer au seuil atteint", "Refus au scan de départ tant que la révision n'est pas faite", r.BloquerAuSeuil, v => _reglages.Modifier(x => x.BloquerAuSeuil = v)));

        // --- Seuils spécifiques ---
        var titre = Ui.Libelle("Seuils spécifiques", Theme.PoliceTitre(11.25f));
        titre.Margin = new Padding(0, 18, 0, 2);
        carte.Ajouter(titre);
        var aideSpecifique = Ui.Libelle("Pour un modèle (nom de l'équipement dans l'inventaire) ou un équipement précis, selon la notice du fabricant. Remplace le seuil du type.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        aideSpecifique.MaximumSize = new Size(560, 0);
        aideSpecifique.Margin = new Padding(0, 0, 0, 10);
        carte.Ajouter(aideSpecifique);
        _seuilsSpecifiques.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        carte.Ajouter(_seuilsSpecifiques);

        carte.Ajouter(PanneauAjoutSeuil());
        return carte;
    }

    // --- Ajouter un seuil spécifique : Modèle | Équipement, recherche ou scan, liste de l'inventaire, valeur, « + Ajouter ».
    private readonly Bascule _porteeAjout = new("Modèle", "Équipement") { Compact = true };
    private readonly ChampSaisie _rechercheAjout = new() { Icone = Icones.Recherche };
    private readonly Label _titreListeAjout = Ui.Libelle("", Theme.PoliceTitre(8.25f), Theme.TexteSecondaire);
    private readonly ListeChoix _listeAjout = new() { AccessibleName = "Choix du modèle ou de l'équipement" };
    private readonly Label _aideAjout = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaire);
    private readonly SelecteurNombre _valeurAjout = new("Seuil à ajouter", n => $"{n} utilis.") { Width = 210, Nombre = ServiceCompteur.SeuilParDefaut };
    private readonly BoutonArrondi _boutonAjout = new("+ Ajouter", StyleBouton.Principal) { AfficherPastille = false };
    private readonly System.Windows.Forms.Timer _attenteAjout = new() { Interval = 250 };

    private PorteeSeuil PorteeAjout => _porteeAjout.SelectedIndex == 1 ? PorteeSeuil.Equipement : PorteeSeuil.Modele;

    private Control PanneauAjoutSeuil()
    {
        var cadre = new CadrePointille { Margin = new Padding(0, 14, 0, 0) };

        var titre = Ui.Libelle("Ajouter un seuil spécifique", Theme.PoliceTitre(10.5f));
        titre.Anchor = AnchorStyles.Left;
        titre.Margin = Padding.Empty;
        _porteeAjout.Anchor = AnchorStyles.Right;
        _porteeAjout.AccessibleName = "Appliquer à";
        var entete = RangeeDeux(titre, _porteeAjout);
        entete.Margin = new Padding(0, 0, 0, 12);
        cadre.Ajouter(entete);

        // Champ : recherche d'un modèle, ou scan d'un équipement (médaille, code) avec la pastille « Prêt à scanner ».
        var pret = new Pastille("Prêt à scanner", Theme.Teinte(Theme.Retour, 0.16f), Theme.RetourClair, Theme.Retour, Theme.PoliceTitre(8.25f), hauteur: 28);
        _rechercheAjout.Champ.AccessibleName = "Rechercher ou scanner";
        _rechercheAjout.Margin = new Padding(0, 0, 0, 14);
        _rechercheAjout.ADroite = pret;
        cadre.Ajouter(_rechercheAjout);

        _titreListeAjout.Margin = new Padding(2, 0, 0, 8);
        cadre.Ajouter(_titreListeAjout);
        _listeAjout.Margin = new Padding(0, 0, 0, 12);
        cadre.Ajouter(_listeAjout);

        _aideAjout.Anchor = AnchorStyles.Left;
        _aideAjout.Margin = Padding.Empty;
        _valeurAjout.Anchor = AnchorStyles.Right;
        _valeurAjout.Margin = new Padding(12, 0, 0, 0);
        var pied = RangeeDeux(_aideAjout, _valeurAjout);
        pied.Margin = new Padding(0, 0, 0, 12);
        cadre.Ajouter(pied);
        _boutonAjout.Margin = Padding.Empty;
        cadre.Ajouter(_boutonAjout, pleineLargeur: false);

        void Mode()
        {
            var equipement = PorteeAjout == PorteeSeuil.Equipement;
            _rechercheAjout.Icone = equipement ? Icones.Scanner : Icones.Recherche;
            _rechercheAjout.TexteIndicatif = equipement ? "Scannez l'équipement : médaille ou code (ex. A24)" : "Chercher un modèle de l'inventaire";
            pret.Visible = equipement;
            _rechercheAjout.PerformLayout();
            _rechercheAjout.Invalidate();
            _titreListeAjout.Text = equipement ? "ÉQUIPEMENTS DE L'INVENTAIRE" : "MODÈLES DE L'INVENTAIRE";
        }

        Mode();
        MettreAJourAideAjout();
        _porteeAjout.Changement += (_, _) =>
        {
            Mode();
            _rechercheAjout.Valeur = "";
            _listeAjout.EffacerChoix();
            Executer(AfficherCandidatsAsync);
        };
        _rechercheAjout.Champ.TextChanged += (_, _) =>
        {
            _attenteAjout.Stop();
            _attenteAjout.Start();
        };
        _attenteAjout.Tick += (_, _) =>
        {
            _attenteAjout.Stop();
            Executer(AfficherCandidatsAsync);
        };
        Disposed += (_, _) => _attenteAjout.Dispose();

        // Le pistolet tape la médaille puis Entrée : l'équipement est choisi dans la liste.
        _rechercheAjout.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _attenteAjout.Stop();
                Executer(ChoisirDepuisLaSaisieAsync);
            }
        };
        _listeAjout.ChoixChange += (_, _) => MettreAJourAideAjout();
        _valeurAjout.Demande += (_, n) =>
        {
            var pas = Math.Sign(n - _valeurAjout.Nombre) * ServiceCompteur.Pas;
            _valeurAjout.Nombre = Math.Clamp(_valeurAjout.Nombre + pas, ServiceCompteur.SeuilSpecifiqueMin, ServiceCompteur.SeuilSpecifiqueMax);
        };
        _boutonAjout.Click += (_, _) => Executer(AjouterSeuilAsync);
        return cadre;
    }

    /// <summary>Rangée « texte à gauche, contrôle à droite » à la hauteur de son contenu (sans étirer le contrôle).</summary>
    private static TableLayoutPanel RangeeDeux(Control gauche, Control droite)
    {
        var rangee = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        rangee.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rangee.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rangee.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gauche.Anchor = AnchorStyles.Left;
        droite.Anchor = AnchorStyles.Right;
        rangee.Controls.Add(gauche, 0, 0);
        rangee.Controls.Add(droite, 1, 0);
        return rangee;
    }

    /// <summary>« Choisissez dans la liste ou scannez une médaille », puis le choix (« A07 · Détendeur adulte »).</summary>
    private void MettreAJourAideAjout()
    {
        var choix = _listeAjout.Choix;
        _aideAjout.Text = choix is null
            ? "Choisissez dans la liste ou scannez une médaille"
            : PorteeAjout == PorteeSeuil.Equipement ? $"Choisi : {choix.Cle} · {choix.Titre}" : $"Choisi : {choix.Titre}";
        _aideAjout.ForeColor = choix is null ? Theme.TexteSecondaire : Theme.Texte;
        _boutonAjout.Enabled = choix is not null;
    }

    /// <summary>Modèles ou équipements de l'inventaire, filtrés par la saisie, avec leur seuil actuel.</summary>
    private async Task AfficherCandidatsAsync()
    {
        var saisie = _rechercheAjout.Valeur;
        IReadOnlyList<ElementChoix> elements;
        string vide;
        if (PorteeAjout == PorteeSeuil.Equipement)
        {
            var equipements = await _compteur.ListerEquipementsAsync(saisie);
            elements = equipements.Select(e => new ElementChoix(
                e.Code, e.Code, true,
                e.Fiche.Length > 0 ? $"{e.Libelle} · {e.Fiche}" : e.Libelle,
                DetailEquipement(e.Usage))).ToList();
            vide = "Aucun équipement ne correspond.";
        }
        else
        {
            var modeles = await _compteur.ListerModelesAsync(saisie);
            elements = modeles.Select(m => new ElementChoix(
                m.Nom, "M", false, m.Nom,
                $"{m.Categorie} · {Libelles.Pluriel(m.NbEquipements, "équipement")} · {(m.SeuilActuel is { } s ? $"seuil actuel {s}" : "sans compteur")}")).ToList();
            vide = "Aucun modèle ne correspond.";
        }

        if (!IsDisposed && saisie == _rechercheAjout.Valeur)
        {
            _listeAjout.Afficher(elements, vide);
        }
    }

    /// <summary>« Seuil du modèle : 100 · 23 utilisations ».</summary>
    private static string DetailEquipement(UtilisationInventaire u)
    {
        var utilisations = Libelles.Pluriel(u.Utilisations, "utilisation");
        if (u.Seuil is not { } seuil)
        {
            return $"Sans compteur · {utilisations}";
        }

        var origine = u.Origine switch
        {
            OrigineSeuil.Equipement => "Seuil spécifique",
            OrigineSeuil.Modele => "Seuil du modèle",
            _ => "Seuil de la catégorie",
        };
        return $"{origine} : {seuil} · {utilisations}";
    }

    /// <summary>Entrée dans le champ : la médaille scannée (ou le seul résultat) devient le choix.</summary>
    private async Task ChoisirDepuisLaSaisieAsync()
    {
        await AfficherCandidatsAsync();
        var saisie = _rechercheAjout.Valeur.Trim();
        if (saisie.Length == 0)
        {
            return;
        }

        var cle = PorteeAjout == PorteeSeuil.Equipement ? Saisie.CodeScanne.Nettoyer(saisie, _reglages.Actuels.PrefixeEtiquette) : saisie;
        if (_listeAjout.Choisir(cle))
        {
            if (PorteeAjout == PorteeSeuil.Equipement)
            {
                _rechercheAjout.Valeur = cle; // « (Detendeur) A24 Asprenaut… » → A24.
                _rechercheAjout.Champ.SelectAll();
            }

            return;
        }

        if (PorteeAjout == PorteeSeuil.Equipement)
        {
            Dialogues.Information(FindForm(), $"Code inconnu : {cle}.");
            _rechercheAjout.Champ.SelectAll();
        }
    }

    private async Task AjouterSeuilAsync()
    {
        if (!Autoriser() || _listeAjout.Choix is not { } choix)
        {
            return;
        }

        var resultat = await _compteur.AjouterSeuilSpecifiqueAsync(PorteeAjout, choix.Cle, _valeurAjout.Nombre);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        _rechercheAjout.Valeur = "";
        _attenteAjout.Stop();
        _listeAjout.EffacerChoix();
        _valeurAjout.Nombre = ServiceCompteur.SeuilParDefaut;
        await AfficherSeuilsAsync(); // Recharge aussi la liste (seuils actuels).
        _rechercheAjout.Champ.Focus();
    }

    /// <summary>Liste des seuils spécifiques, reconstruite après chaque ajout ou retrait.</summary>
    private async Task AfficherSeuilsAsync()
    {
        var seuils = await _compteur.DecrireSeuilsSpecifiquesAsync();
        if (IsDisposed)
        {
            return;
        }

        _seuilsSpecifiques.SuspendLayout();
        while (_seuilsSpecifiques.Controls.Count > 0)
        {
            var c = _seuilsSpecifiques.Controls[0];
            _seuilsSpecifiques.Controls.RemoveAt(0);
            c.Dispose();
        }

        _seuilsSpecifiques.RowStyles.Clear();
        if (seuils.Count == 0)
        {
            var vide = Ui.Libelle("Aucun seuil spécifique : chaque équipement suit le seuil de son type.", Theme.Police(9.75f), Theme.TexteSecondaire);
            vide.Margin = new Padding(0, 4, 0, 4);
            AjouterSeuil(vide);
        }

        foreach (var s in seuils)
        {
            AjouterSeuil(LigneSeuil(s));
        }

        _seuilsSpecifiques.ResumeLayout(true);
        await AfficherCandidatsAsync(); // « seuil actuel » des modèles et équipements proposés.
    }

    private void AjouterSeuil(Control ligne)
    {
        if (IsHandleCreated)
        {
            Dessin.MettreALEchelle(ligne, this); // Lignes construites en pixels logiques, ajoutées après l'affichage.
        }

        _seuilsSpecifiques.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Dock = DockStyle.Fill;
        _seuilsSpecifiques.Controls.Add(ligne, 0, _seuilsSpecifiques.RowStyles.Count - 1);
    }

    /// <summary>« Modèle | Détendeur Mares · 4 équipements | − 200 sorties + | × ».</summary>
    private TableLayoutPanel LigneSeuil(SeuilSpecifiqueDecrit s)
    {
        var equipement = s.Portee == PorteeSeuil.Equipement;
        var couleur = equipement ? Theme.Avertissement : Theme.AccentClair;
        var portee = new Pastille(equipement ? "Équipement" : "Modèle", Theme.Teinte(couleur, 0.14f), couleur) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        var nom = Ui.Libelle(s.Cle, equipement ? Theme.PoliceCode(10.5f) : Theme.PoliceTitre(10.5f), equipement ? Theme.Accent : Theme.Texte);
        nom.Margin = Padding.Empty;
        var precision = Ui.Libelle(s.Precision, Theme.Police(9f), Theme.TexteSecondaire);
        precision.Margin = Padding.Empty;
        var textes = Ui.Pile(nom, precision);
        textes.Anchor = AnchorStyles.Left;

        var seuil = new SelecteurNombre($"Seuil {s.Cle}", n => $"{n} utilis.") { Width = 210, Nombre = s.Sorties, Anchor = AnchorStyles.Right, Margin = new Padding(8, 0, 6, 0) };
        seuil.Demande += (_, n) =>
        {
            if (Autoriser())
            {
                var voulu = seuil.Nombre + (Math.Sign(n - seuil.Nombre) * ServiceCompteur.Pas);
                if (_compteur.ChangerSeuilSpecifique(s.Portee, s.Cle, voulu) is { Reussi: true, Id: { } retenu })
                {
                    seuil.Nombre = (int)retenu;
                    Executer(AfficherCandidatsAsync);
                }
            }
        };
        var retirer = new BoutonArrondi($"Supprimer le seuil de {s.Cle}", StyleBouton.Icone, Icones.Croix) { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        retirer.Click += (_, _) =>
        {
            if (Autoriser() && _compteur.RetirerSeuilSpecifique(s.Portee, s.Cle).Reussi)
            {
                Executer(AfficherSeuilsAsync);
            }
        };

        var ligne = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 6, 0, 6),
        };
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Controls.Add(portee, 0, 0);
        ligne.Controls.Add(textes, 1, 0);
        ligne.Controls.Add(seuil, 2, 0);
        ligne.Controls.Add(retirer, 3, 0);
        return ligne;
    }

    // ---------------------------------------------------------------- Données

    private CarteSection CarteDonnees()
    {
        var carte = new CarteSection("Données et sauvegardes", Icones.Base, Theme.Retour);
        _etatSauvegarde.Height = 72;
        carte.Ajouter(_etatSauvegarde);

        carte.Ajouter(Ui.LibelleChamp("Dossier des sauvegardes"));
        _dossier.Valeur = _sauvegardes.Dossier;
        _dossier.Champ.ReadOnly = true;
        _dossier.Champ.Font = Theme.PoliceCode(10f);
        _dossier.Champ.AccessibleName = "Dossier des sauvegardes";
        _dossier.Margin = Padding.Empty;
        var parcourir = new BoutonArrondi("Parcourir", StyleBouton.Secondaire) { Margin = new Padding(8, 0, 0, 0), AfficherPastille = false };
        _garde.Lier(parcourir, ActionProtegee.Parametres);
        parcourir.Click += (_, _) => ChoisirDossier();
        var ligneDossier = Ui.Colonnes((_dossier, SizeType.Percent, 100), (parcourir, SizeType.AutoSize, 0));
        ligneDossier.Height = Theme.HauteurChamp;
        carte.Ajouter(ligneDossier);

        var aide = Ui.Libelle("Choisissez une clé USB ou un dossier synchronisé pour garder une copie hors du PC.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.Margin = new Padding(0, 8, 0, 18);
        carte.Ajouter(aide);

        var sauvegarder = new BoutonArrondi("Sauvegarder", StyleBouton.Retour) { AutoSize = false, Margin = new Padding(0, 0, 6, 0) };
        sauvegarder.Click += (_, _) => Executer(SauvegarderAsync);
        var restaurer = new BoutonArrondi("Restaurer…", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(4, 0, 4, 0), AfficherPastille = false };
        _garde.Lier(restaurer, ActionProtegee.RestaurerSauvegarde);
        restaurer.Click += (_, _) => Executer(RestaurerAsync);
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(6, 0, 0, 0) };
        exporter.Click += (_, _) => Executer(ExporterToutAsync);
        var boutons = Ui.Colonnes((sauvegarder, SizeType.Percent, 33.3f), (restaurer, SizeType.Percent, 33.3f), (exporter, SizeType.Percent, 33.3f));
        boutons.Height = Theme.HauteurBouton;
        carte.Ajouter(boutons);

        // Sauvegarde complète : base + réglages + manuels dans un seul fichier (panne du PC, installation du PC du club).
        var titreComplete = Ui.Libelle("Sauvegarde complète", Theme.PoliceTitre(10.5f), Theme.Texte);
        titreComplete.Margin = new Padding(0, 22, 0, 4);
        carte.Ajouter(titreComplete);
        var aideComplete = Ui.Libelle("Un seul fichier .zip avec les données, tous les réglages et les manuels. « Restaurer… » le reprend en entier, par exemple pour installer un autre PC.",
            Theme.Police(9.75f), Theme.TexteSecondaire);
        aideComplete.Margin = new Padding(0, 0, 0, 12);
        carte.Ajouter(aideComplete);
        var complete = new BoutonArrondi("Faire une sauvegarde complète…", StyleBouton.Secondaire, Icones.Telecharger) { AutoSize = false, Margin = Padding.Empty };
        complete.Click += (_, _) => Executer(SauvegarderCompleteAsync); // Libre, comme une sauvegarde simple.
        var ligneComplete = Ui.Colonnes((complete, SizeType.Percent, 100));
        ligneComplete.Height = Theme.HauteurBouton;
        carte.Ajouter(ligneComplete);
        return carte;
    }

    private void AfficherEtatSauvegarde()
    {
        var detail = $"Automatique à chaque démarrage · {_sauvegardes.NombreAConserver} copies conservées";
        if (_sauvegardes.DerniereSauvegarde() is { } date)
        {
            var jour = date.Date == DateTime.Today ? "aujourd'hui" : date.Date == DateTime.Today.AddDays(-1) ? "hier" : $"le {date.ToString("dd/MM/yyyy", Libelles.Culture)}";
            _etatSauvegarde.Afficher(NiveauResultat.Succes, $"Dernière sauvegarde : {jour} à {date.ToString("HH:mm", Libelles.Culture)}", detail, detailEnCode: false);
        }
        else
        {
            _etatSauvegarde.Afficher(NiveauResultat.Avertissement, "Aucune sauvegarde pour l'instant", detail, detailEnCode: false);
        }
    }

    /// <summary>
    /// Toutes les données en CSV (un fichier par table) dans un nouveau dossier « Export Asprenaut AAAA-MM-JJ »
    /// créé dans le dossier choisi. Libre en consultation, comme une sauvegarde.
    /// </summary>
    private async Task ExporterToutAsync()
    {
        using var dialogue = new FolderBrowserDialog
        {
            Description = "Où créer le dossier d'export ?",
            UseDescriptionForTitle = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ShowNewFolderButton = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var fichiers = await _export.ExporterAsync();
        var dossier = Path.Combine(dialogue.SelectedPath, _export.NomDossier());
        for (var n = 2; Directory.Exists(dossier); n++)
        {
            dossier = Path.Combine(dialogue.SelectedPath, $"{_export.NomDossier()} ({n})"); // Deuxième export du jour.
        }

        try
        {
            Directory.CreateDirectory(dossier);
            foreach (var f in fichiers)
            {
                await File.WriteAllTextAsync(Path.Combine(dossier, f.Nom), f.Contenu, Fichiers.Utf8Excel);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogues.Erreur(FindForm(), "Impossible d'écrire dans ce dossier. Choisissez-en un autre (Documents, clé USB…).");
            return;
        }

        var materiel = fichiers.FirstOrDefault(f => f.Nom == "materiel.csv")?.Lignes ?? 0;
        var sorties = fichiers.FirstOrDefault(f => f.Nom == "sorties.csv")?.Lignes ?? 0;
        if (Dialogues.Confirmer(FindForm(), $"{Libelles.Pluriel(fichiers.Count, "fichier")} créés ({Libelles.Pluriel(materiel, "équipement")}, {Libelles.Pluriel(sorties, "sortie")}).\nOuvrir le dossier ?"))
        {
            Fichiers.Ouvrir(dossier);
        }
    }

    private void ChoisirDossier()
    {
        if (!Autoriser())
        {
            return;
        }

        using var dialogue = new FolderBrowserDialog
        {
            Description = "Dossier des sauvegardes",
            UseDescriptionForTitle = true,
            InitialDirectory = _sauvegardes.Dossier,
            ShowNewFolderButton = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var resultat = _reglages.ChangerDossierSauvegardes(dialogue.SelectedPath);
        _dossier.Valeur = _sauvegardes.Dossier;
        AfficherEtatSauvegarde();
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
        }
    }

    private async Task SauvegarderAsync()
    {
        var chemin = await _sauvegardes.SauvegarderAsync();
        AfficherEtatSauvegarde();
        Dialogues.Information(FindForm(), chemin is null ? "Rien à sauvegarder : la base est vide." : $"Sauvegarde faite : {Path.GetFileName(chemin)}");
    }

    private async Task RestaurerAsync()
    {
        if (!_garde.Exiger(FindForm(), ActionProtegee.RestaurerSauvegarde, "Restaurer une sauvegarde"))
        {
            return;
        }

        using var dialogue = new OpenFileDialog
        {
            Title = "Choisir la sauvegarde à restaurer",
            InitialDirectory = _sauvegardes.Dossier,
            Filter = "Sauvegardes Asprenaut (*.zip, materiel_*.db)|*.zip;materiel_*.db|Sauvegardes complètes (*.zip)|*.zip|Toutes les bases (*.db)|*.db",
            CheckFileExists = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var nom = Path.GetFileName(dialogue.FileName);
        var complete = string.Equals(Path.GetExtension(dialogue.FileName), ISauvegardeComplete.Extension, StringComparison.OrdinalIgnoreCase);
        if (!Dialogues.Confirmer(FindForm(), complete
            ? $"Remplacer les données, tous les réglages et les manuels par la sauvegarde complète « {nom} » ?\n\n" +
              "Une copie des données et des réglages actuels est faite avant. L'application redémarrera ensuite."
            : $"Remplacer toutes les données actuelles par la sauvegarde « {nom} » ?\n\nUne copie des données actuelles est faite avant."))
        {
            return;
        }

        var resultat = complete ? await _complete.RestaurerAsync(dialogue.FileName) : await _sauvegardes.RestaurerAsync(dialogue.FileName);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        if (complete)
        {
            // Les réglages restaurés ne sont lus qu'au démarrage : on relance l'application (mêmes options, ex. --base).
            Dialogues.Information(FindForm(), resultat.Message + "\n\nL'application va redémarrer.");
            Application.Restart();
            return;
        }

        Dialogues.Information(FindForm(), resultat.Message);
        _navigation.AllerA<EcranSorties>(); // Les données ont changé : on repart de l'accueil.
    }

    /// <summary>« Faire une sauvegarde complète… » : où enregistrer le .zip (clé USB, Documents…), puis l'ouvrir dans l'Explorateur.</summary>
    private async Task SauvegarderCompleteAsync()
    {
        using var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer la sauvegarde complète",
            FileName = _complete.NomPropose(),
            Filter = "Sauvegarde complète (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var resultat = await _complete.CreerAsync(dialogue.FileName);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        if (Dialogues.Confirmer(FindForm(), resultat.Message + "\n\nOuvrir le dossier ?"))
        {
            Fichiers.Ouvrir(Path.GetDirectoryName(dialogue.FileName) ?? dialogue.FileName);
        }
    }

    // ---------------------------------------------------------------- Club

    private CarteSection CarteClub()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Club", Icones.Logo, Theme.AccentClair);

        var nom = new ChampSaisie { Valeur = r.NomClub };
        nom.Champ.AccessibleName = "Nom du club";
        var responsable = new ChampSaisie { Valeur = r.ResponsableMateriel, TexteIndicatif = "Nom et téléphone" };
        responsable.Champ.AccessibleName = "Responsable matériel";
        void Enregistrer()
        {
            if (nom.Valeur.Trim() == _reglages.Actuels.NomClub && responsable.Valeur.Trim() == _reglages.Actuels.ResponsableMateriel)
            {
                return;
            }

            // Différé : pas de fenêtre (code gestionnaire, erreur) pendant que le focus quitte le champ.
            BeginInvoke(() =>
            {
                var resultat = Autoriser() ? _reglages.ChangerClub(nom.Valeur, responsable.Valeur) : null;
                if (resultat is not { Reussi: true })
                {
                    nom.Valeur = _reglages.Actuels.NomClub;
                    responsable.Valeur = _reglages.Actuels.ResponsableMateriel;
                }

                if (resultat is { Reussi: false })
                {
                    Dialogues.Information(FindForm(), resultat.Message);
                }
            });
        }

        nom.Champ.Leave += (_, _) => Enregistrer();
        responsable.Champ.Leave += (_, _) => Enregistrer();

        var champs = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 18) };
        champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        champs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        champs.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.HauteurChamp));
        var lNom = Ui.LibelleChamp("Nom du club");
        var lResp = Ui.LibelleChamp("Responsable matériel");
        lResp.Margin = new Padding(7, 0, 0, 8);
        nom.Margin = new Padding(0, 0, 7, 0);
        responsable.Margin = new Padding(7, 0, 0, 0);
        nom.Dock = responsable.Dock = DockStyle.Fill;
        champs.Controls.Add(lNom, 0, 0);
        champs.Controls.Add(lResp, 1, 0);
        champs.Controls.Add(nom, 0, 1);
        champs.Controls.Add(responsable, 1, 1);
        carte.Ajouter(champs);

        // Numéro d'affiliation : repris dans l'identification marquée sur le matériel.
        var numero = new ChampSaisie { Valeur = r.NumeroClub, TexteIndicatif = "Ex. : 14 71-0025" };
        numero.Champ.AccessibleName = "Numéro d'affiliation";
        numero.Champ.Leave += (_, _) =>
        {
            if (numero.Valeur.Trim() == _reglages.Actuels.NumeroClub)
            {
                return;
            }

            BeginInvoke(() =>
            {
                if (!Autoriser() || !_reglages.ChangerNumeroClub(numero.Valeur).Reussi)
                {
                    numero.Valeur = _reglages.Actuels.NumeroClub;
                }
            });
        };
        carte.Ajouter(Ui.LibelleChamp("Numéro d'affiliation FFESSM"));
        numero.Height = Theme.HauteurChamp;
        numero.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(numero);
        var aide = Ui.Libelle("Repris dans l'identification du matériel : « (Stab) S01 taille XL Asprenaut 14 71-0025 ».", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        carte.Ajouter(aide);
        return carte;
    }

    // ---------------------------------------------------------------- Types de sortie

    private readonly TableLayoutPanel _listeTypes = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    /// <summary>
    /// Types de sortie du club et leurs utilisations : ajouter, modifier (nom, pictogramme), utilisations par sortie,
    /// supprimer (jamais utilisé) ou masquer / réafficher (bouton œil).
    /// </summary>
    private CarteSection CarteTypesSortie()
    {
        var carte = new CarteSection("Types de sortie et utilisations", Icones.Sorties, Theme.AccentClair);
        var aide = Ui.Libelle("Nombre d'utilisations ajouté au compteur de chaque équipement sorti. Modifiable sur chaque sortie. "
            + "Un type déjà utilisé ne se supprime pas : il se masque, pour garder l'historique.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        aide.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(aide);
        _listeTypes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        carte.Ajouter(_listeTypes);

        var ajouter = new BoutonArrondi("Ajouter un type", StyleBouton.Secondaire, Icones.Plus) { Margin = new Padding(0, 12, 0, 0) };
        ajouter.Click += (_, _) => Executer(() => EditerTypeAsync(null));
        carte.Ajouter(ajouter, pleineLargeur: false);
        return carte;
    }

    private async Task AfficherTypesAsync()
    {
        var types = await _typesSortie.ListerAsync();
        if (IsDisposed)
        {
            return;
        }

        _listeTypes.SuspendLayout();
        while (_listeTypes.Controls.Count > 0)
        {
            var c = _listeTypes.Controls[0];
            _listeTypes.Controls.RemoveAt(0);
            c.Dispose();
        }

        _listeTypes.RowStyles.Clear();
        foreach (var t in types)
        {
            var ligne = LigneType(t);
            if (IsHandleCreated)
            {
                Dessin.MettreALEchelle(ligne, this); // Lignes construites en pixels logiques, ajoutées après l'affichage.
            }

            _listeTypes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ligne.Dock = DockStyle.Fill;
            _listeTypes.Controls.Add(ligne, 0, _listeTypes.RowStyles.Count - 1);
        }

        _listeTypes.ResumeLayout(true);
    }

    /// <summary>« Piscine · 3 sorties | − 1 utilisation + | Modifier | 🗑 ou 👁 ».</summary>
    private TableLayoutPanel LigneType(TypeDeSortie t)
    {
        var nom = Ui.Libelle(t.Nom, Theme.PoliceTitre(10.5f), t.Actif ? Theme.Texte : Theme.TexteSecondaire);
        nom.Margin = Padding.Empty;
        var precision = (t.NbSorties == 0 ? "Aucune sortie" : Libelles.Pluriel(t.NbSorties, "sortie")) + (t.Actif ? "" : " · masqué");
        var detail = Ui.Libelle(precision, Theme.Police(9f), Theme.TexteSecondaire);
        detail.Margin = Padding.Empty;
        var textes = Ui.Pile(nom, detail);
        textes.Anchor = AnchorStyles.Left;

        var utilisations = new SelecteurNombre($"Utilisations pour {t.Nom}", n => Libelles.Pluriel(n, "utilisation"))
        {
            Width = 226,
            Nombre = t.Utilisations,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(8, 0, 4, 0),
        };
        utilisations.Demande += (_, n) =>
        {
            if (Autoriser())
            {
                Executer(async () =>
                {
                    if (await _typesSortie.ChangerUtilisationsAsync(t.Id, n) is { Reussi: true, Id: { } retenu })
                    {
                        utilisations.Nombre = (int)retenu;
                        t.Utilisations = (int)retenu;
                    }
                });
            }
        };

        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire) { Height = Theme.HauteurCliquable, Anchor = AnchorStyles.Right, Margin = new Padding(4, 0, 4, 0), AccessibleName = $"Modifier le type {t.Nom}" };
        modifier.Click += (_, _) => Executer(() => EditerTypeAsync(t));

        // Jamais utilisé : corbeille (suppression). Déjà utilisé : œil, pour masquer ou réafficher sans perdre l'historique.
        var utilise = t.NbSorties > 0;
        var action = utilise
            ? new BoutonArrondi(t.Actif ? $"Masquer {t.Nom}" : $"Réafficher {t.Nom}", StyleBouton.Icone, t.Actif ? Icones.OeilBarre : Icones.Oeil)
            : new BoutonArrondi($"Supprimer {t.Nom}", StyleBouton.Icone, Icones.Corbeille) { CouleurIcone = Theme.Erreur };
        action.Anchor = AnchorStyles.Right;
        action.Margin = Padding.Empty;
        action.Click += (_, _) => Executer(() => utilise ? BasculerTypeAsync(t) : SupprimerTypeAsync(t));

        // Sortie nominative (maquette) : le nom de l'adhérent est demandé à chaque scan au départ.
        var nominatif = new CaseOption("Nom de l'adhérent à chaque scan",
            "Au départ, chaque équipement doit être attribué à un adhérent. Pratique pour savoir qui a perdu quoi.")
        {
            Checked = t.Nominatif,
            AutoCheck = false, // Le clic passe par le mode gestion et le service.
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
            AccessibleName = $"{t.Nom} : nom de l'adhérent à chaque scan",
        };
        nominatif.Click += (_, _) =>
        {
            if (!Autoriser())
            {
                return;
            }

            Executer(async () =>
            {
                var resultat = await _typesSortie.ChangerNominatifAsync(t.Id, !nominatif.Checked);
                if (!resultat.Reussi)
                {
                    Dialogues.Erreur(FindForm(), resultat.Message);
                    return;
                }

                t.Nominatif = !nominatif.Checked;
                nominatif.Checked = t.Nominatif;
            });
        };

        var ligne = new CarteType
        {
            ColumnCount = 4,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 12, 12, 12),
        };
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Controls.Add(textes, 0, 0);
        ligne.Controls.Add(utilisations, 1, 0);
        ligne.Controls.Add(modifier, 2, 0);
        ligne.Controls.Add(action, 3, 0);
        ligne.Controls.Add(nominatif, 0, 1);
        ligne.SetColumnSpan(nominatif, 4);
        return ligne;
    }

    /// <summary>Carte d'un type de sortie (fond arrondi un peu plus sombre que la section).</summary>
    private sealed class CarteType : TableLayoutPanel
    {
        public CarteType()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.35f);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            using var chemin = Dessin.Arrondi(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Dessin.Echelle(this, 16));
            using var fond = new SolidBrush(BackColor);
            using var bord = new Pen(Theme.Bordure, Dessin.Echelle(this, 1));
            g.FillPath(fond, chemin);
            g.DrawPath(bord, chemin);
        }
    }

    /// <summary>Bouton œil : masque un type utilisé (plus proposé, historique gardé) ou le réaffiche.</summary>
    private async Task BasculerTypeAsync(TypeDeSortie type)
    {
        if (!Autoriser())
        {
            return;
        }

        var resultat = await _typesSortie.BasculerMasqueAsync(type.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        await AfficherTypesAsync();
    }

    /// <summary>Fenêtre d'ajout (<paramref name="type"/> null) ou de modification d'un type de sortie.</summary>
    private async Task EditerTypeAsync(TypeDeSortie? type)
    {
        if (!Autoriser())
        {
            return;
        }

        var nom = new ChampSaisie { Valeur = type?.Nom ?? "", TexteIndicatif = "Ex. : Sortie bateau" };
        nom.Champ.AccessibleName = "Nom du type de sortie";
        nom.Champ.MaxLength = TypeDeSortie.LongueurMaxNom;
        var pictogramme = new ListeDeroulante { AccessibleName = "Pictogramme" };
        foreach (var famille in Enum.GetValues<TypeSortie>())
        {
            pictogramme.Items.Add(new Choix<TypeSortie>(famille, Icones.NomPictogramme(famille)));
        }

        pictogramme.SelectedItem = pictogramme.Items.OfType<Choix<TypeSortie>>().First(c => c.Valeur == (type?.Pictogramme ?? TypeSortie.Autre));

        // Proposé ou masqué : bouton œil de la liste. Utilisations : sélecteur de la liste (nouveau type : selon le pictogramme).
        using var fenetre = new FenetreFormulaire(type is null ? "Ajouter un type de sortie" : $"Modifier « {type.Nom} »", type is null ? "Ajouter" : "Enregistrer", () =>
        {
            var famille = (pictogramme.SelectedItem as Choix<TypeSortie>)?.Valeur ?? TypeSortie.Autre;
            return type is null
                ? _typesSortie.CreerAsync(nom.Valeur, famille)
                : _typesSortie.ModifierAsync(type.Id, nom.Valeur, famille, type.Actif);
        });
        fenetre.DeuxChamps("Nom", nom, "Pictogramme", pictogramme);

        fenetre.Shown += (_, _) => nom.Champ.Focus();
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await AfficherTypesAsync();
        }
    }

    private async Task SupprimerTypeAsync(TypeDeSortie type)
    {
        if (!Autoriser() || !Dialogues.Confirmer(FindForm(), $"Supprimer le type « {type.Nom} » ?"))
        {
            return;
        }

        var resultat = await _typesSortie.SupprimerAsync(type.Id);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        await AfficherTypesAsync();
    }

    // ---------------------------------------------------------------- Niveaux des adhérents

    /// <summary>
    /// Niveaux proposés pour les adhérents (formulaire, modèle Excel, import) : un par ligne, dans l'ordre de la liste.
    /// Les adhérents gardent leur niveau s'il est retiré de la liste.
    /// </summary>
    private CarteSection CarteNiveaux()
    {
        var carte = new CarteSection("Niveaux des adhérents", Icones.Moniteurs, Theme.AccentClair);
        var aide = Ui.Libelle("Proposés dans la fiche d'un adhérent et dans le modèle Excel. Un niveau par ligne, dans l'ordre voulu. "
            + "À l'import, « N2 » ou « niveau 2 » deviennent « Niveau 2 » ; un niveau inconnu est laissé vide.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        aide.Margin = new Padding(0, 0, 0, 10);
        carte.Ajouter(aide);

        var liste = new ChampSaisie(multiligne: true) { Height = 230, Margin = new Padding(0, 0, 0, 12) };
        liste.Champ.AccessibleName = "Niveaux, un par ligne";
        liste.Valeur = string.Join(Environment.NewLine, _reglages.Actuels.NiveauxAdherents);
        carte.Ajouter(liste);

        var enregistrer = new BoutonArrondi("Enregistrer la liste", StyleBouton.Secondaire, Icones.Coche) { Margin = Padding.Empty };
        enregistrer.Click += (_, _) =>
        {
            if (!Autoriser())
            {
                return;
            }

            var r = _reglages.ChangerNiveauxAdherents(liste.Valeur.Split('\n'));
            if (r.Reussi)
            {
                liste.Valeur = string.Join(Environment.NewLine, _reglages.Actuels.NiveauxAdherents); // Nettoyée (espaces, lignes vides).
                Dialogues.Information(FindForm(), r.Message);
            }
            else
            {
                Dialogues.Erreur(FindForm(), r.Message);
            }
        };
        var retablir = new BoutonArrondi("Rétablir la liste d'origine", StyleBouton.Lien) { Margin = new Padding(12, 0, 0, 0) };
        retablir.Click += (_, _) =>
        {
            if (Autoriser() && Dialogues.Confirmer(FindForm(), "Revenir à la liste d'origine des niveaux ?")
                && _reglages.ChangerNiveauxAdherents(Reglages.NiveauxParDefaut).Reussi)
            {
                liste.Valeur = string.Join(Environment.NewLine, Reglages.NiveauxParDefaut);
            }
        };
        carte.Ajouter(Ui.Rangee(true, enregistrer, retablir), pleineLargeur: false);
        return carte;
    }

    // ---------------------------------------------------------------- Prestataires

    private readonly TableLayoutPanel _listePrestataires = new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    /// <summary>
    /// Magasins et organismes chez qui le matériel part en révision (Révisions › « Envoyer chez un prestataire »).
    /// Jamais supprimés : on les désactive (plus proposés), les révisions passées les gardent.
    /// </summary>
    private CarteSection CartePrestataires()
    {
        var carte = new CarteSection("Prestataires", Icones.Camion, Theme.PrestataireClair);
        var aide = Ui.Libelle("Magasins et organismes proposés dans Révisions › « Envoyer chez un prestataire ». "
            + "Un prestataire désactivé n'est plus proposé ; les envois passés le gardent.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        aide.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(aide);
        _listePrestataires.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        carte.Ajouter(_listePrestataires);

        var ajouter = new BoutonArrondi("Ajouter un prestataire", StyleBouton.Secondaire, Icones.Plus) { Margin = new Padding(0, 12, 0, 0) };
        ajouter.Click += (_, _) => Executer(() => EditerPrestataireAsync(null));
        carte.Ajouter(ajouter, pleineLargeur: false);
        return carte;
    }

    private async Task AfficherPrestatairesAsync()
    {
        var prestataires = await _prestataires.ListerAsync();
        if (IsDisposed)
        {
            return;
        }

        _listePrestataires.SuspendLayout();
        while (_listePrestataires.Controls.Count > 0)
        {
            var c = _listePrestataires.Controls[0];
            _listePrestataires.Controls.RemoveAt(0);
            c.Dispose();
        }

        _listePrestataires.RowStyles.Clear();
        if (prestataires.Count == 0)
        {
            var vide = Ui.Libelle("Aucun prestataire pour l'instant.", Theme.Police(9.75f), Theme.TexteSecondaire);
            vide.Margin = new Padding(0, 4, 0, 4);
            _listePrestataires.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _listePrestataires.Controls.Add(vide, 0, 0);
        }

        foreach (var p in prestataires)
        {
            var ligne = LignePrestataire(p);
            if (IsHandleCreated)
            {
                Dessin.MettreALEchelle(ligne, this); // Lignes construites en pixels logiques, ajoutées après l'affichage.
            }

            _listePrestataires.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ligne.Dock = DockStyle.Fill;
            _listePrestataires.Controls.Add(ligne, 0, _listePrestataires.RowStyles.Count - 1);
        }

        _listePrestataires.ResumeLayout(true);
    }

    /// <summary>« Aqua Service · Magasin · 03 85… | Modifier | Désactiver ».</summary>
    private TableLayoutPanel LignePrestataire(Prestataire p)
    {
        var nom = Ui.Libelle(p.Nom, Theme.PoliceTitre(10.5f), p.Actif ? Theme.Texte : Theme.TexteSecondaire);
        nom.Margin = Padding.Empty;
        var precision = string.Join(" · ", new[] { p.Type == TypePrestataire.Organisme ? "Organisme" : "Magasin", p.Telephone, p.Adresse, p.Actif ? "" : "désactivé" }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        var detail = Ui.Libelle(precision, Theme.Police(9f), Theme.TexteSecondaire);
        detail.Margin = Padding.Empty;
        detail.AutoEllipsis = true;
        var textes = Ui.Pile(nom, detail);
        textes.Anchor = AnchorStyles.Left;

        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire) { Height = Theme.HauteurCliquable, Anchor = AnchorStyles.Right, Margin = new Padding(4, 0, 4, 0), AccessibleName = $"Modifier {p.Nom}" };
        modifier.Click += (_, _) => Executer(() => EditerPrestataireAsync(p));
        var actif = new BoutonArrondi(p.Actif ? $"Désactiver {p.Nom}" : $"Réactiver {p.Nom}", StyleBouton.Icone, p.Actif ? Icones.OeilBarre : Icones.Oeil)
        {
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        actif.Click += (_, _) => Executer(async () =>
        {
            if (!Autoriser())
            {
                return;
            }

            var resultat = await _prestataires.ChangerActifAsync(p.Id, !p.Actif);
            if (!resultat.Reussi)
            {
                Dialogues.Erreur(FindForm(), resultat.Message);
                return;
            }

            await AfficherPrestatairesAsync();
        });

        var ligne = new CarteType
        {
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 12, 12, 12),
        };
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ligne.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        ligne.Controls.Add(textes, 0, 0);
        ligne.Controls.Add(modifier, 1, 0);
        ligne.Controls.Add(actif, 2, 0);
        return ligne;
    }

    /// <summary>Fenêtre d'ajout (<paramref name="prestataire"/> null) ou de modification d'un prestataire.</summary>
    private async Task EditerPrestataireAsync(Prestataire? prestataire)
    {
        if (!Autoriser())
        {
            return;
        }

        var nom = new ChampSaisie { Valeur = prestataire?.Nom ?? "", TexteIndicatif = "Ex. : Aqua Service" };
        nom.Champ.AccessibleName = "Nom du prestataire";
        nom.Champ.MaxLength = ServicePrestataires.LongueurNom;
        var type = new ListeDeroulante { AccessibleName = "Type de prestataire" };
        type.Items.Add(new Choix<TypePrestataire>(TypePrestataire.Magasin, "Magasin"));
        type.Items.Add(new Choix<TypePrestataire>(TypePrestataire.Organisme, "Organisme (requalification)"));
        type.SelectedIndex = prestataire?.Type == TypePrestataire.Organisme ? 1 : 0;
        var telephone = new ChampSaisie { Valeur = prestataire?.Telephone ?? "" };
        telephone.Champ.AccessibleName = "Téléphone";
        var adresse = new ChampSaisie { Valeur = prestataire?.Adresse ?? "" };
        adresse.Champ.AccessibleName = "Adresse";

        using var fenetre = new FenetreFormulaire(prestataire is null ? "Ajouter un prestataire" : $"Modifier « {prestataire.Nom} »",
            prestataire is null ? "Ajouter" : "Enregistrer", () =>
                _prestataires.EnregistrerAsync(prestataire?.Id, nom.Valeur, (type.SelectedItem as Choix<TypePrestataire>)?.Valeur ?? TypePrestataire.Magasin,
                    telephone.Valeur, adresse.Valeur));
        fenetre.DeuxChamps("Nom", nom, "Type", type);
        fenetre.DeuxChamps("Téléphone (facultatif)", telephone, "Adresse (facultatif)", adresse);
        fenetre.Shown += (_, _) => nom.Champ.Focus();
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await AfficherPrestatairesAsync();
        }
    }

    // ---------------------------------------------------------------- Problèmes signalés (tuiles)

    private readonly ListeDeroulante _categorieTuiles = new() { AccessibleName = "Catégorie des tuiles", Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _origineTuiles = Ui.Libelle("", Theme.Police(9f), Theme.TexteSecondaire);

    private readonly ListeTuiles _listeTuiles = new() { Margin = Padding.Empty };

    /// <summary>
    /// Tuiles de la fenêtre « Problème sur A07 », par catégorie : nom, pictogramme, ordre, et « Précision obligatoire »
    /// (le champ Précision doit alors être rempli). « Rétablir » revient aux tuiles d'origine.
    /// </summary>
    private CarteSection CarteTuilesSignalement()
    {
        var carte = new CarteSection("Problèmes signalés", Icones.Drapeau, Theme.Avertissement);
        var aide = Ui.Libelle("Tuiles proposées dans la fenêtre « Problème sur… », selon la catégorie de l'équipement. "
            + "Pour chaque tuile : nom, pictogramme, ordre, et si la précision doit être remplie.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.MaximumSize = new Size(560, 0);
        aide.Margin = new Padding(0, 0, 0, 10);
        carte.Ajouter(aide);
        carte.Ajouter(Ui.LibelleChamp("Catégorie"));
        _categorieTuiles.SelectedIndexChanged += (_, _) => AfficherTuiles();
        carte.Ajouter(_categorieTuiles);
        _origineTuiles.Margin = new Padding(0, 0, 0, 8);
        carte.Ajouter(_origineTuiles);
        _listeTuiles.Geste += (_, g) => GesteSurTuile(g.Geste, g.Index);
        carte.Ajouter(_listeTuiles);

        var ajouter = new BoutonArrondi("Ajouter une tuile", StyleBouton.Secondaire, Icones.Plus) { Margin = new Padding(0, 12, 0, 0) };
        ajouter.Click += (_, _) => EditerTuile(null);
        carte.Ajouter(ajouter, pleineLargeur: false);
        var retablir = new BoutonArrondi("Rétablir les tuiles d'origine", StyleBouton.Lien) { Margin = new Padding(0, 6, 0, 0) };
        retablir.Click += (_, _) =>
        {
            if (CategorieTuiles is { } categorie && Autoriser()
                && Dialogues.Confirmer(FindForm(), $"Revenir aux tuiles d'origine pour « {categorie} » ?"))
            {
                AfficherResultat(_reglages.RetablirTuilesSignalement(categorie));
            }
        };
        carte.Ajouter(retablir, pleineLargeur: false);
        return carte;
    }

    private string? CategorieTuiles => (_categorieTuiles.SelectedItem as Choix<string>)?.Valeur;

    private async Task ChargerCategoriesTuilesAsync()
    {
        var categories = await _inventaire.ListerCategoriesAsync();
        if (IsDisposed)
        {
            return;
        }

        _categorieTuiles.Items.Clear();
        foreach (var c in categories.OrderBy(c => c.Nom, StringComparer.Create(Libelles.Culture, true)))
        {
            _categorieTuiles.Items.Add(new Choix<string>(c.Nom, c.Nom));
        }

        if (_categorieTuiles.Items.Count > 0)
        {
            _categorieTuiles.SelectedIndex = 0; // Affiche ses tuiles.
        }
    }

    private void AfficherResultat(ResultatOperation resultat)
    {
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
        }

        AfficherTuiles();
    }

    /// <summary>Enregistre la nouvelle liste de tuiles de la catégorie choisie (code gestionnaire demandé avant).</summary>
    private void EnregistrerTuiles(List<CauseSignalement> tuiles)
    {
        if (CategorieTuiles is { } categorie && Autoriser())
        {
            AfficherResultat(_reglages.ChangerTuilesSignalement(categorie, tuiles));
        }
    }

    private List<CauseSignalement> TuilesActuelles() => ServiceSignalements.TuilesPour(CategorieTuiles, _reglages.Actuels).ToList();

    private void AfficherTuiles()
    {
        var categorie = CategorieTuiles;
        var tuiles = TuilesActuelles();
        _origineTuiles.Text = categorie is null ? "" : ServiceSignalements.TuilesPersonnalisees(categorie, _reglages.Actuels)
            ? $"Tuiles personnalisées · {Libelles.Pluriel(tuiles.Count, "tuile")}"
            : $"Tuiles d'origine · {Libelles.Pluriel(tuiles.Count, "tuile")}";

        _listeTuiles.Afficher(tuiles);
    }

    /// <summary>Monter, descendre, modifier, supprimer une tuile, ou basculer « Précision obligatoire » (code gestionnaire demandé).</summary>
    private void GesteSurTuile(GesteTuile geste, int index)
    {
        var tuiles = TuilesActuelles();
        if (index < 0 || index >= tuiles.Count)
        {
            return;
        }

        switch (geste)
        {
            case GesteTuile.Modifier:
                EditerTuile(index);
                return;
            case GesteTuile.Supprimer when !Dialogues.Confirmer(FindForm(), $"Retirer la tuile « {tuiles[index].Libelle} » ?"):
                return;
            case GesteTuile.Supprimer:
                tuiles.RemoveAt(index);
                break;
            case GesteTuile.Monter when index > 0:
                (tuiles[index - 1], tuiles[index]) = (tuiles[index], tuiles[index - 1]);
                break;
            case GesteTuile.Descendre when index < tuiles.Count - 1:
                (tuiles[index + 1], tuiles[index]) = (tuiles[index], tuiles[index + 1]);
                break;
            case GesteTuile.Precision:
                tuiles[index].PrecisionObligatoire = !tuiles[index].PrecisionObligatoire;
                break;
            default:
                return;
        }

        EnregistrerTuiles(tuiles);
    }

    /// <summary>Fenêtre d'ajout (<paramref name="index"/> null) ou de modification d'une tuile : nom, pictogramme, précision.</summary>
    private void EditerTuile(int? index)
    {
        if (CategorieTuiles is not { } categorie || !Autoriser())
        {
            return;
        }

        var tuiles = TuilesActuelles();
        var actuelle = index is { } i ? tuiles[i] : null;
        var nom = new ChampSaisie { Valeur = actuelle?.Libelle ?? "", TexteIndicatif = "Ex. : Manomètre bloqué", IndicatifSiFocus = true };
        nom.Champ.AccessibleName = "Nom de la tuile";
        nom.Champ.MaxLength = ServiceSignalements.LongueurMaxCause;
        var picto = new ListeDeroulante { AccessibleName = "Pictogramme" };
        foreach (var (cle, libelle, _) in Icones.PictosCause)
        {
            picto.Items.Add(new Choix<string>(cle, libelle));
        }

        picto.SelectedItem = picto.Items.OfType<Choix<string>>().FirstOrDefault(c => string.Equals(c.Valeur, actuelle?.Pictogramme ?? "Autre", StringComparison.OrdinalIgnoreCase))
            ?? picto.Items[^1];
        var apercu = new PictoTuile((picto.SelectedItem as Choix<string>)?.Valeur) { Size = new Size(52, 52), Margin = new Padding(0, 0, 0, 12) };
        picto.SelectedIndexChanged += (_, _) => apercu.Cle = (picto.SelectedItem as Choix<string>)?.Valeur;
        var obligatoire = new CaseOption("Précision obligatoire", "Le moniteur doit écrire une précision quand il choisit cette tuile.")
        {
            Checked = actuelle?.PrecisionObligatoire ?? false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 0),
        };

        using var fenetre = new FenetreFormulaire(actuelle is null ? "Ajouter une tuile" : $"Modifier « {actuelle.Libelle} »",
            actuelle is null ? "Ajouter" : "Enregistrer", () =>
            {
                var tuile = new CauseSignalement
                {
                    Libelle = nom.Valeur,
                    Pictogramme = (picto.SelectedItem as Choix<string>)?.Valeur ?? "Autre",
                    PrecisionObligatoire = obligatoire.Checked,
                };
                var liste = tuiles.Select(t => t.Copie()).ToList();
                if (index is { } position)
                {
                    liste[position] = tuile;
                }
                else
                {
                    // Nouvelle tuile : avant « Autre » s'il est en dernier.
                    var autre = liste.Count > 0 && string.Equals(liste[^1].Libelle, ServiceSignalements.CauseAutre, StringComparison.OrdinalIgnoreCase);
                    liste.Insert(autre ? liste.Count - 1 : liste.Count, tuile);
                }

                return Task.FromResult(_reglages.ChangerTuilesSignalement(categorie, liste));
            });
        fenetre.EnTete($"Catégorie : {categorie}");
        fenetre.Champ("Nom", nom);
        fenetre.Champ("Pictogramme", picto);
        fenetre.Ajouter(apercu, SizeType.Absolute, 64);
        fenetre.Ajouter(obligatoire, SizeType.AutoSize, 0);
        fenetre.Shown += (_, _) => nom.Champ.Focus();
        if (fenetre.ShowDialog(FindForm()) == DialogResult.OK)
        {
            AfficherTuiles();
        }
    }

    /// <summary>Ce qu'on demande sur une tuile de la liste.</summary>
    private enum GesteTuile
    {
        Monter,
        Descendre,
        Modifier,
        Supprimer,
        Precision,
    }

    /// <summary>
    /// Liste des tuiles d'une catégorie, entièrement dessinée dans un seul contrôle (une douzaine de contrôles par tuile
    /// ralentissaient l'ouverture de Paramètres) : par ligne, pictogramme, nom, monter / descendre / modifier / supprimer,
    /// puis la case « Précision obligatoire ». Souris (clic retrouvé par calcul) et clavier : flèches pour choisir la ligne,
    /// Ctrl + flèches pour la déplacer, Espace pour la case, Entrée pour modifier, Suppr pour retirer.
    /// </summary>
    private sealed class ListeTuiles : ControleDessine
    {
        // Pixels logiques (96 dpi).
        private const int HauteurLigne = 128;
        private const int Ecart = 10;
        private const int HauteurEntete = 58;
        private const int Bouton = 44;

        private IReadOnlyList<CauseSignalement> _tuiles = [];
        private int _choisie;
        private (int Ligne, GesteTuile Geste)? _survol;

        public ListeTuiles()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Tuiles de la catégorie";
        }

        /// <summary>Un geste sur une ligne (le code gestionnaire est demandé par l'écran).</summary>
        public event EventHandler<(GesteTuile Geste, int Index)>? Geste;

        // Placée dans la carte avant d'avoir sa fenêtre : l'échelle de l'écran se lit sur le parent.
        private float Ec(float v) => Dessin.Echelle(Parent ?? this, v);

        public void Afficher(IReadOnlyList<CauseSignalement> tuiles)
        {
            _tuiles = tuiles;
            _choisie = Math.Clamp(_choisie, 0, Math.Max(0, tuiles.Count - 1));
            _survol = null;
            AccessibleDescription = string.Join(", ", tuiles.Select(t => t.Libelle + (t.PrecisionObligatoire ? " (précision obligatoire)" : "")));
            AjusterHauteur();
            Invalidate();
        }

        private int HauteurVoulue => _tuiles.Count == 0 ? 0 : (int)Ec((_tuiles.Count * HauteurLigne) + ((_tuiles.Count - 1) * Ecart));

        // La carte (TableLayoutPanel) garde la hauteur du contrôle : on la pose, et la carte suit d'elle-même.
        private void AjusterHauteur()
        {
            if (Height != HauteurVoulue)
            {
                Height = HauteurVoulue;
            }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            AjusterHauteur();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            AjusterHauteur();
        }

        public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width is > 0 and < int.MaxValue ? proposedSize.Width : Width, HauteurVoulue);

        private RectangleF Ligne(int i) => new(0.5f, (i * Ec(HauteurLigne + Ecart)) + 0.5f, Width - 1.5f, Ec(HauteurLigne) - 1.5f);

        /// <summary>Zone d'un geste dans sa ligne (vide si sans objet : monter la première, descendre la dernière, retirer la seule).</summary>
        private RectangleF Zone(int i, GesteTuile geste)
        {
            var r = Ligne(i);
            if (geste == GesteTuile.Precision)
            {
                return new RectangleF(r.Left + Ec(14), r.Top + Ec(HauteurEntete), r.Width - Ec(28), r.Bottom - r.Top - Ec(HauteurEntete) - Ec(12));
            }

            var possible = geste switch
            {
                GesteTuile.Monter => i > 0,
                GesteTuile.Descendre => i < _tuiles.Count - 1,
                GesteTuile.Supprimer => _tuiles.Count > 1,
                _ => true,
            };
            if (!possible)
            {
                return RectangleF.Empty;
            }

            var rang = 3 - (int)geste; // Supprimer tout à droite, puis Modifier, Descendre, Monter.
            var b = Ec(Bouton);
            return new RectangleF(r.Right - Ec(10) - b - (rang * (b + Ec(4))), r.Top + Ec(8), b, b);
        }

        private (int Ligne, GesteTuile Geste)? Sous(Point p)
        {
            for (var i = 0; i < _tuiles.Count; i++)
            {
                if (!Ligne(i).Contains(p))
                {
                    continue;
                }

                foreach (var g in Enum.GetValues<GesteTuile>())
                {
                    if (Zone(i, g).Contains(p))
                    {
                        return (i, g);
                    }
                }

                return null;
            }

            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var s = Sous(e.Location);
            Cursor = s is null ? Cursors.Default : Cursors.Hand;
            if (s != _survol)
            {
                _survol = s;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _survol = null;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && Sous(e.Location) is { } s)
            {
                _choisie = s.Ligne;
                Invalidate();
                Geste?.Invoke(this, (s.Geste, s.Ligne));
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter or Keys.Space or Keys.Delete || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_tuiles.Count == 0)
            {
                return;
            }

            GesteTuile? geste = (e.KeyCode, e.Control) switch
            {
                (Keys.Up, true) when _choisie > 0 => GesteTuile.Monter,
                (Keys.Down, true) when _choisie < _tuiles.Count - 1 => GesteTuile.Descendre,
                (Keys.Space, _) => GesteTuile.Precision,
                (Keys.Enter, _) => GesteTuile.Modifier,
                (Keys.Delete, _) when _tuiles.Count > 1 => GesteTuile.Supprimer,
                _ => null,
            };
            if (geste is { } g)
            {
                var index = _choisie;
                _choisie += g == GesteTuile.Monter ? -1 : g == GesteTuile.Descendre ? 1 : 0; // La ligne suit la tuile déplacée.
                e.Handled = e.SuppressKeyPress = true;
                Geste?.Invoke(this, (g, index));
            }
            else if (e.KeyCode is Keys.Up or Keys.Down)
            {
                _choisie = Math.Clamp(_choisie + (e.KeyCode == Keys.Up ? -1 : 1), 0, _tuiles.Count - 1);
                AccessibilityNotifyClients(AccessibleEvents.Focus, _choisie);
                e.Handled = true;
                Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void Peindre(Graphics g)
        {
            var fond = Theme.Sur(Theme.Surface, Theme.Fond, 0.35f);
            for (var i = 0; i < _tuiles.Count; i++)
            {
                var t = _tuiles[i];
                var r = Ligne(i);
                using (var chemin = Dessin.Arrondi(r, Ec(16)))
                using (var pinceau = new SolidBrush(fond))
                using (var bord = new Pen(Focused && i == _choisie ? Theme.Accent : Theme.Bordure, Ec(Focused && i == _choisie ? 2 : 1)))
                {
                    g.FillPath(pinceau, chemin);
                    g.DrawPath(bord, chemin);
                }

                // Pictogramme et nom.
                var cote = Ec(38);
                var picto = new RectangleF(r.Left + Ec(14), r.Top + Ec(8) + ((Ec(Bouton) - cote) / 2), cote, cote);
                using (var chemin = Dessin.Arrondi(picto, Ec(10)))
                using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Avertissement, 0.14f)))
                {
                    g.FillPath(pinceau, chemin);
                }

                var ti = cote * 0.55f;
                Icones.Dessiner(g, Icones.PourPictogramme(t.Pictogramme), new RectangleF(picto.X + ((cote - ti) / 2), picto.Y + ((cote - ti) / 2), ti, ti), Theme.Avertissement, 1.9f);
                var premierBouton = Enum.GetValues<GesteTuile>().Where(x => x != GesteTuile.Precision).Select(x => Zone(i, x)).Where(z => !z.IsEmpty).Min(z => z.Left);
                var xNom = (int)(picto.Right + Ec(12));
                Dessin.Texte(g, t.Libelle, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(xNom, (int)picto.Top, (int)(premierBouton - Ec(6)), (int)picto.Bottom), Theme.Texte);

                // Boutons.
                foreach (var geste in new[] { GesteTuile.Monter, GesteTuile.Descendre, GesteTuile.Modifier, GesteTuile.Supprimer })
                {
                    var z = Zone(i, geste);
                    if (z.IsEmpty)
                    {
                        continue;
                    }

                    if (_survol == (i, geste))
                    {
                        using var chemin = Dessin.Arrondi(z, Ec(12));
                        using var pinceau = new SolidBrush(Theme.SurfaceActive);
                        g.FillPath(pinceau, chemin);
                    }

                    var icone = geste switch
                    {
                        GesteTuile.Monter => "M6 15l6-6 6 6",
                        GesteTuile.Descendre => Icones.ChevronBas,
                        GesteTuile.Modifier => Icones.Crayon,
                        _ => Icones.Corbeille,
                    };
                    var tb = Ec(20);
                    Icones.Dessiner(g, icone, new RectangleF(z.X + ((z.Width - tb) / 2), z.Y + ((z.Height - tb) / 2), tb, tb),
                        geste == GesteTuile.Supprimer ? Theme.Erreur : _survol == (i, geste) ? Theme.Texte : Theme.TexteSecondaire, 2f);
                }

                // Case « Précision obligatoire ».
                var c = Zone(i, GesteTuile.Precision);
                var coche = t.PrecisionObligatoire;
                using (var chemin = Dessin.Arrondi(c, Ec(12)))
                using (var pinceau = new SolidBrush(coche ? Theme.Sur(fond, Theme.Accent, 0.12f) : _survol == (i, GesteTuile.Precision) ? Theme.Sur(fond, Theme.Texte, 0.05f) : Theme.Sur(fond, Theme.Fond, 0.4f)))
                using (var bord = new Pen(coche ? Theme.Sur(fond, Theme.Accent, 0.6f) : Theme.Bordure, Ec(1)))
                {
                    g.FillPath(pinceau, chemin);
                    g.DrawPath(bord, chemin);
                }

                var tc = Ec(22);
                var boite = new RectangleF(c.Left + Ec(14), c.Top + ((c.Height - tc) / 2), tc, tc);
                using (var chemin = Dessin.Arrondi(boite, Ec(6)))
                {
                    if (coche)
                    {
                        using var plein = new SolidBrush(Theme.Accent);
                        g.FillPath(plein, chemin);
                        Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(boite, -Ec(4), -Ec(4)), Theme.TexteSurAccent, 3f);
                    }
                    else
                    {
                        using var bord = new Pen(Theme.BordureForte, Ec(1.5f));
                        g.DrawPath(bord, chemin);
                    }
                }

                var xt = (int)(boite.Right + Ec(14));
                var milieu = (int)(c.Top + (c.Height / 2));
                Dessin.Texte(g, "Précision obligatoire", Theme.PoliceTitre(10f), Rectangle.FromLTRB(xt, milieu - (int)Ec(21), (int)(c.Right - Ec(10)), milieu), Theme.Texte);
                Dessin.Texte(g, "Le moniteur doit écrire une précision quand il choisit cette tuile.", Theme.Police(9f),
                    Rectangle.FromLTRB(xt, milieu, (int)(c.Right - Ec(10)), milieu + (int)Ec(20)), Theme.TexteSecondaireClair);
            }

        }
    }

    /// <summary>Pictogramme d'une tuile dans un carré teinté jaune (comme dans la fenêtre « Problème sur… »).</summary>
    private sealed class PictoTuile : ControleDessine
    {
        private string? _cle;

        public PictoTuile(string? cle)
        {
            _cle = cle;
            AccessibleRole = AccessibleRole.Graphic;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? Cle { get => _cle; set => Invalider(ref _cle, value); }

        protected override void Peindre(Graphics g)
        {
            var cote = Math.Min(Width, Height) - 1;
            var r = new RectangleF(0.5f, 0.5f, cote, cote);
            using (var chemin = Dessin.Arrondi(r, E(10)))
            using (var fond = new SolidBrush(Theme.Sur(Dessin.CouleurParent(this), Theme.Avertissement, 0.14f)))
            {
                g.FillPath(fond, chemin);
            }

            var t = cote * 0.55f;
            Icones.Dessiner(g, Icones.PourPictogramme(_cle), new RectangleF((cote - t) / 2, (cote - t) / 2, t, t), Theme.Avertissement, 1.9f);
        }
    }

    // ---------------------------------------------------------------- À propos

    private CarteSection CarteAPropos()
    {
        var carte = new CarteSection("À propos", null, Theme.AccentClair);
        var infos = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
        infos.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        infos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Info(string terme, string valeur, Font? police = null)
        {
            infos.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var t = Ui.Libelle(terme, Theme.Police(10.5f), Theme.TexteSecondaire);
            t.Margin = new Padding(0, 0, 20, 10);
            var v = Ui.Libelle(valeur, police ?? Theme.Police(10.5f), Theme.TexteDoux);
            v.Margin = new Padding(0, 0, 0, 10);
            infos.Controls.Add(t, 0, infos.RowStyles.Count - 1);
            infos.Controls.Add(v, 1, infos.RowStyles.Count - 1);
        }

        Info("Version", Application.ProductVersion.Split('+')[0]);
        Info("Base de données", _parametres.CheminBase, Theme.PoliceCode(9.75f));
        Info("Framework", ".NET 10 · WinForms");
        carte.Ajouter(infos);

        var journal = new BoutonArrondi("Ouvrir le journal des actions", StyleBouton.Lien) { Margin = Padding.Empty, Height = Theme.HauteurCliquable };
        journal.Click += (_, _) => _navigation.AllerA<EcranHistorique>();
        carte.Ajouter(journal, pleineLargeur: false);
        return carte;
    }

    /// <summary>Paramètres : action protégée (le code est demandé si le mode gestion n'est pas ouvert).</summary>
    private bool Autoriser() => _garde.Exiger(FindForm(), ActionProtegee.Parametres, "Modifier les paramètres");

    /// <summary>Interrupteur de réglage : en mode consultation sans code accepté, il revient à sa position.</summary>
    private LigneReglage Interrupteur(string libelle, string aide, bool valeur, Action<bool> changement, bool filet = true)
    {
        var bouton = new Controles.Interrupteur(libelle) { Checked = valeur };
        var retour = false;
        bouton.CheckedChanged += (_, _) =>
        {
            if (retour)
            {
                return;
            }

            if (!Autoriser())
            {
                retour = true;
                bouton.Checked = !bouton.Checked;
                retour = false;
                return;
            }

            changement(bouton.Checked);
        };
        return new LigneReglage(libelle, aide, bouton, filet);
    }

    /// <summary>Liste de réglage : en mode consultation sans code accepté, elle revient au choix enregistré.</summary>
    private void ProtegerListe(ListeDeroulante liste, Func<int> indexActuel, Action<object?> changement)
    {
        var retour = false;
        liste.SelectedIndexChanged += (_, _) =>
        {
            if (retour)
            {
                return;
            }

            if (!Autoriser())
            {
                retour = true;
                liste.SelectedIndex = Math.Max(0, indexActuel());
                retour = false;
                return;
            }

            changement(liste.SelectedItem);
        };
    }

    /// <summary>
    /// Choix d'un thème : une mini-fenêtre dessinée avec les couleurs de ce thème (menu, cartes, bouton),
    /// son nom et une coche s'il est actif. Le cadre de sélection suit le thème actuel.
    /// </summary>
    private sealed class ApercuTheme : Button
    {
        public const int Hauteur = 176;
        private bool _survol;

        public ApercuTheme(ModeAffichage mode)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Mode = mode;
            Text = mode == ModeAffichage.Clair ? "Clair" : "Sombre";
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = $"Thème {Text.ToLower(Libelles.Culture)}";
            AccessibleDescription = Actif ? "Thème actuel" : null;
        }

        public ModeAffichage Mode { get; }

        private bool Actif => Mode == Theme.Mode;

        protected override void OnMouseEnter(EventArgs e)
        {
            _survol = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _survol = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            float E(float v) => Dessin.Echelle(this, v);
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var p = Theme.PaletteDe(Mode);

            // Cadre : accent du thème actuel si choisi, bordure sinon.
            var hLegende = E(44);
            var cadre = new RectangleF(E(1), E(1), Width - E(3), Height - hLegende - E(4));
            using (var chemin = Dessin.Arrondi(cadre, E(16)))
            {
                using var fond = new SolidBrush(p.Fond);
                g.FillPath(fond, chemin);
                var couleur = Actif ? Theme.Accent : _survol ? Theme.Sur(Theme.Surface, Theme.Accent, 0.5f) : Theme.BordureForte;
                using var bord = new Pen(couleur, E(Actif ? 2.5f : 1.2f));
                g.DrawPath(bord, chemin);
            }

            // Mini-fenêtre : menu à gauche, deux cartes, un bouton.
            var interieur = RectangleF.Inflate(cadre, -E(10), -E(10));
            using var decoupe = Dessin.Arrondi(interieur, E(10));
            g.SetClip(decoupe);
            using (var menu = new SolidBrush(p.FondMenu))
            {
                g.FillRectangle(menu, interieur.X, interieur.Y, interieur.Width * 0.27f, interieur.Height);
            }

            Barre(g, p.Accent, interieur.X + E(8), interieur.Y + E(12), interieur.Width * 0.27f - E(16), E(8));
            for (var i = 0; i < 3; i++)
            {
                Barre(g, p.TexteSecondaire, interieur.X + E(8), interieur.Y + E(30 + (i * 15)), (interieur.Width * 0.27f) - E(24), E(5));
            }

            var x = interieur.X + (interieur.Width * 0.27f) + E(10);
            var largeurCarte = (interieur.Right - x - E(18)) / 2;
            Barre(g, p.Texte, x, interieur.Y + E(10), largeurCarte, E(8));
            for (var i = 0; i < 2; i++)
            {
                var c = new RectangleF(x + (i * (largeurCarte + E(8))), interieur.Y + E(28), largeurCarte, interieur.Bottom - interieur.Y - E(38));
                using (var chemin = Dessin.Arrondi(c, E(7)))
                {
                    using var surface = new SolidBrush(p.Surface);
                    g.FillPath(surface, chemin);
                    using var bord = new Pen(p.Bordure, E(1));
                    g.DrawPath(bord, chemin);
                }

                Barre(g, p.Texte, c.X + E(8), c.Y + E(10), c.Width * 0.6f, E(6));
                Barre(g, p.TexteSecondaire, c.X + E(8), c.Y + E(22), c.Width * 0.75f, E(5));
                Barre(g, i == 0 ? p.Depart : p.Retour, c.X + E(8), c.Y + E(33), c.Width * 0.4f, E(5));
                Barre(g, p.Accent, c.X + E(8), c.Bottom - E(18), c.Width - E(16), E(10));
            }

            g.ResetClip();

            // Légende : pictogramme, nom, coche si actif.
            var y = cadre.Bottom + E(6);
            var t = E(20);
            Icones.Dessiner(g, Mode == ModeAffichage.Clair ? Icones.Soleil : Icones.Lune, new RectangleF(E(4), y + ((hLegende - t) / 2), t, t), Actif ? Theme.Accent : Theme.TexteSecondaireClair);
            Dessin.Texte(g, Text, Actif ? Theme.PoliceTitre(10.5f) : Theme.Police(10.5f), new Rectangle((int)E(32), (int)y, Width - (int)E(70), (int)hLegende), Theme.Texte);
            if (Actif)
            {
                var d = E(24);
                var rond = new RectangleF(Width - d - E(4), y + ((hLegende - d) / 2), d, d);
                using (var pinceau = new SolidBrush(Theme.Accent))
                {
                    g.FillEllipse(pinceau, rond);
                }

                Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(rond, -E(5), -E(5)), Theme.TexteSurAccent, 2.4f);
            }

            if (Focused && ShowFocusCues)
            {
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(cadre, -E(4), -E(4)), E(12));
                using var focus = new Pen(Theme.Accent, E(1.5f)) { DashPattern = [3f, 2f] };
                g.DrawPath(focus, chemin);
            }
        }

        private static void Barre(Graphics g, Color couleur, float x, float y, float largeur, float hauteur)
        {
            if (largeur <= 0)
            {
                return;
            }

            using var chemin = Dessin.Arrondi(new RectangleF(x, y, largeur, hauteur), hauteur / 2);
            using var pinceau = new SolidBrush(couleur);
            g.FillPath(pinceau, chemin);
        }
    }

    /// <summary>Encadré à bordure pointillée (zone d'essai du pistolet).</summary>
    private sealed class CadrePointille : TableLayoutPanel
    {
        public CadrePointille()
        {
            ColumnCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Sur(Theme.Surface, Theme.Fond, 0.5f);
            Padding = new Padding(16);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Ajouter(Control c, bool pleineLargeur = true)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (pleineLargeur)
            {
                c.Dock = DockStyle.Fill;
            }

            Controls.Add(c, 0, RowStyles.Count - 1);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            using var chemin = Dessin.Arrondi(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Dessin.Echelle(this, 18));
            using var fond = new SolidBrush(BackColor);
            g.FillPath(fond, chemin);
            using var bord = new Pen(Theme.Sur(BackColor, Theme.Accent, 0.35f), Dessin.Echelle(this, 1)) { DashPattern = [4f, 3f] };
            g.DrawPath(bord, chemin);
        }
    }
}
