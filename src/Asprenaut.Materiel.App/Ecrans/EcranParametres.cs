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
/// Paramètres : pistolet, règles de prêt, données et sauvegardes, club, à propos.
/// Chaque changement est enregistré tout de suite (pas de bouton « Enregistrer »).
/// </summary>
public sealed class EcranParametres : EcranBase
{
    private readonly ServiceReglages _reglages;
    private readonly IServiceSauvegarde _sauvegardes;
    private readonly INavigation _navigation;
    private readonly ParametresApplication _parametres;
    private readonly BandeauMessage _etatSauvegarde = new() { Margin = new Padding(0, 0, 0, 18) };
    private readonly ChampSaisie _dossier = new();
    private readonly Label _dernierCode = Ui.Libelle("", Theme.Police(9.75f), Theme.RetourClair);
    private readonly Panel _cadre = new() { BackColor = Theme.Fond, Margin = new Padding(0, 4, 0, 0) };
    private CarteSection? _carteAffichage;

    /// <summary>Paramètre de navigation : rouvrir l'écran sur la carte « Affichage » (après un changement de thème).</summary>
    public const string VersAffichage = "affichage";

    public EcranParametres(ServiceReglages reglages, IServiceSauvegarde sauvegardes, INavigation navigation, ParametresApplication parametres)
    {
        _reglages = reglages;
        _sauvegardes = sauvegardes;
        _navigation = navigation;
        _parametres = parametres;

        // Deux colonnes de cartes ; chaque rangée prend la hauteur de sa carte la plus haute.
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
        var cartes = new[] { CartePistolet(), CarteRegles(), CarteDonnees(), CarteClub(), CarteAffichage(), CarteAPropos() };
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

        // Cadre défilant vertical (la page est plus haute que l'écran).
        var cadre = _cadre;
        cadre.HorizontalScroll.Maximum = 0;
        cadre.AutoScroll = false;
        cadre.VerticalScroll.Visible = false;
        cadre.AutoScroll = true;
        Natif.ScrollbarsDuTheme(cadre);
        cadre.Controls.Add(grille);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Pistolet, règles de prêt et sauvegardes."))), SizeType.AutoSize, 0),
            (cadre, SizeType.Percent, 100)));
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
        if (mode == Theme.Mode)
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
        essai.Champ.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            var brut = essai.Valeur.Trim();
            var code = Saisie.CodeScanne.Nettoyer(brut, _reglages.Actuels.PrefixeEtiquette);
            _dernierCode.Text = code.Length == 0
                ? "Rien n'a été lu. Visez l'étiquette et réessayez."
                : brut == code ? $"✓ Dernier code lu : {code}" : $"✓ Dernier code lu : {brut} → {code}";
            _dernierCode.ForeColor = code.Length == 0 ? Theme.Erreur : Theme.RetourClair;
            essai.Valeur = "";
        };
        _dernierCode.Text = "Le code lu s'affichera ici.";
        _dernierCode.ForeColor = Theme.TexteSecondaire;
        _dernierCode.Margin = Padding.Empty;

        var zone = new CadrePointille { Margin = new Padding(0, 0, 0, 6) };
        zone.Ajouter(Ui.LibelleChamp("Tester le pistolet"));
        zone.Ajouter(essai);
        zone.Ajouter(_dernierCode);
        carte.Ajouter(zone);

        carte.Ajouter(Interrupteur("Sons de confirmation", "Bip au succès, son grave en cas d'erreur", r.SonsActifs, v => _reglages.Modifier(x => x.SonsActifs = v), filet: false));
        carte.Ajouter(Interrupteur("Garder le champ de scan actif", "Le curseur revient seul dans la zone de scan", r.GarderFocusScan, v => _reglages.Modifier(x => x.GarderFocusScan = v)));
        carte.Ajouter(Interrupteur("Ignorer les doubles lectures", "Même code lu deux fois en moins d'une seconde", r.IgnorerDoublesLectures, v => _reglages.Modifier(x => x.IgnorerDoublesLectures = v)));

        var prefixe = new ChampSaisie { Size = new Size(110, Theme.HauteurCliquable), Valeur = r.PrefixeEtiquette };
        prefixe.Champ.Font = Theme.PoliceCode(11.25f);
        prefixe.Champ.TextAlign = HorizontalAlignment.Center;
        prefixe.Champ.CharacterCasing = CharacterCasing.Upper;
        prefixe.Champ.MaxLength = ServiceReglages.LongueurMaxPrefixe;
        prefixe.Champ.AccessibleName = "Préfixe des étiquettes";
        prefixe.Champ.Leave += (_, _) =>
        {
            var resultat = _reglages.ChangerPrefixe(prefixe.Valeur);
            prefixe.Valeur = _reglages.Actuels.PrefixeEtiquette;
            if (!resultat.Reussi)
            {
                Dialogues.Information(FindForm(), resultat.Message);
            }
        };
        carte.Ajouter(new LigneReglage("Préfixe des étiquettes", "Retiré automatiquement à la lecture", prefixe));
        return carte;
    }

    // ---------------------------------------------------------------- Règles

    private CarteSection CarteRegles()
    {
        var r = _reglages.Actuels;
        var carte = new CarteSection("Règles de prêt", Icones.Bouclier, Theme.DepartClair);

        var jours = new SelecteurNombre("Jours avant alerte", n => $"{n} j") { Nombre = r.JoursAvantAlerteRetour };
        jours.Demande += (_, n) => jours.Nombre = _reglages.ChangerJoursAlerte(n);
        carte.Ajouter(new LigneReglage("Alerte si non rendu après", "La sortie passe en orange dans la liste", jours, filet: false));

        var revision = new ListeDeroulante { Width = 150, AccessibleName = "Prévenir avant une révision" };
        foreach (var j in Reglages.JoursRevisionPossibles)
        {
            revision.Items.Add(new Choix<int>(j, $"{j} jours"));
        }

        revision.SelectedIndex = Math.Max(0, Array.IndexOf(Reglages.JoursRevisionPossibles, r.JoursAvantRevision));
        revision.SelectedIndexChanged += (_, _) =>
        {
            if (revision.SelectedItem is Choix<int> choix)
            {
                _reglages.ChangerJoursRevision(choix.Valeur);
            }
        };
        carte.Ajouter(new LigneReglage("Prévenir avant une révision", "Badge jaune dans l'inventaire", revision));

        carte.Ajouter(Interrupteur("Autoriser le départ si révision dépassée", "Le matériel part avec un avertissement jaune", r.DepartSiRevisionDepassee, v => _reglages.Modifier(x => x.DepartSiRevisionDepassee = v)));
        carte.Ajouter(Interrupteur("Confirmer avant de clôturer", "Demande validation s'il reste du matériel dehors", r.ConfirmerCloture, v => _reglages.Modifier(x => x.ConfirmerCloture = v)));
        return carte;
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
        var parcourir = new BoutonArrondi("Parcourir", StyleBouton.Secondaire) { Margin = new Padding(8, 0, 0, 0) };
        parcourir.Click += (_, _) => ChoisirDossier();
        var ligneDossier = Ui.Colonnes((_dossier, SizeType.Percent, 100), (parcourir, SizeType.AutoSize, 0));
        ligneDossier.Height = Theme.HauteurChamp;
        carte.Ajouter(ligneDossier);

        var aide = Ui.Libelle("Choisissez une clé USB ou un dossier synchronisé pour garder une copie hors du PC.", Theme.Police(9.75f), Theme.TexteSecondaire);
        aide.Margin = new Padding(0, 8, 0, 18);
        carte.Ajouter(aide);

        var sauvegarder = new BoutonArrondi("Sauvegarder", StyleBouton.Retour) { AutoSize = false, Margin = new Padding(0, 0, 6, 0) };
        sauvegarder.Click += (_, _) => Executer(SauvegarderAsync);
        var restaurer = new BoutonArrondi("Restaurer…", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(4, 0, 4, 0) };
        restaurer.Click += (_, _) => Executer(RestaurerAsync);
        var exporter = new BoutonArrondi("Exporter CSV", StyleBouton.Secondaire) { AutoSize = false, Margin = new Padding(6, 0, 0, 0) };
        exporter.Click += (_, _) => Dialogues.Information(FindForm(), "L'export CSV arrive dans une prochaine version.");
        var boutons = Ui.Colonnes((sauvegarder, SizeType.Percent, 33.3f), (restaurer, SizeType.Percent, 33.3f), (exporter, SizeType.Percent, 33.3f));
        boutons.Height = Theme.HauteurBouton;
        carte.Ajouter(boutons);
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

    private void ChoisirDossier()
    {
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
        using var dialogue = new OpenFileDialog
        {
            Title = "Choisir la sauvegarde à restaurer",
            InitialDirectory = _sauvegardes.Dossier,
            Filter = "Sauvegardes Asprenaut (materiel_*.db)|materiel_*.db|Toutes les bases (*.db)|*.db",
            CheckFileExists = true,
        };
        if (dialogue.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var nom = Path.GetFileName(dialogue.FileName);
        if (!Dialogues.Confirmer(FindForm(), $"Remplacer toutes les données actuelles par la sauvegarde « {nom} » ?\n\nUne copie des données actuelles est faite avant."))
        {
            return;
        }

        var resultat = await _sauvegardes.RestaurerAsync(dialogue.FileName);
        if (!resultat.Reussi)
        {
            Dialogues.Erreur(FindForm(), resultat.Message);
            return;
        }

        Dialogues.Information(FindForm(), resultat.Message);
        _navigation.AllerA<EcranSorties>(); // Les données ont changé : on repart de l'accueil.
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
            var resultat = _reglages.ChangerClub(nom.Valeur, responsable.Valeur);
            if (!resultat.Reussi)
            {
                nom.Valeur = _reglages.Actuels.NomClub;
                Dialogues.Information(FindForm(), resultat.Message);
            }
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

        // Types de sortie : fixés par l'application (liste de la base), affichés pour information.
        carte.Ajouter(Ui.LibelleChamp("Types de sortie proposés"));
        var types = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        foreach (var t in Enum.GetValues<TypeSortie>())
        {
            types.Controls.Add(new Pastille(Libelles.De(t), Theme.Teinte(Theme.Accent, 0.12f), Theme.AccentClair, police: Theme.PoliceTitre(9.75f), hauteur: 36)
            {
                Margin = new Padding(0, 0, 8, 8),
            });
        }

        carte.Ajouter(types);
        return carte;
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

    private static LigneReglage Interrupteur(string libelle, string aide, bool valeur, Action<bool> changement, bool filet = true)
    {
        var bouton = new Controles.Interrupteur(libelle) { Checked = valeur };
        bouton.CheckedChanged += (_, _) => changement(bouton.Checked);
        return new LigneReglage(libelle, aide, bouton, filet);
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

        public void Ajouter(Control c)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
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
