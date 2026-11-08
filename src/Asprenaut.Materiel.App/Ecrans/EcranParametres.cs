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
/// Paramètres : pistolet, règles de prêt, étiquettes, données et sauvegardes, club, à propos.
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
        var cartes = new[] { CartePistolet(), CarteRegles(), CarteEtiquettes(), CarteDonnees(), CarteClub(), CarteAPropos() };
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
        var cadre = new Panel { BackColor = Theme.Fond, Margin = new Padding(0, 4, 0, 0) };
        cadre.HorizontalScroll.Maximum = 0;
        cadre.AutoScroll = false;
        cadre.VerticalScroll.Visible = false;
        cadre.AutoScroll = true;
        Natif.ScrollbarsSombres(cadre);
        cadre.Controls.Add(grille);

        Controls.Add(Ui.Colonne(
            (Ui.Entete(Ui.Pile(Ui.Titre(Titre), Ui.SousTitre("Pistolet, règles de prêt, étiquettes et sauvegardes."))), SizeType.AutoSize, 0),
            (cadre, SizeType.Percent, 100)));
    }

    public override string Titre => "Paramètres";

    public override EntreeMenu Menu => EntreeMenu.Parametres;

    protected override Task ChargerAsync(object? parametre)
    {
        AfficherEtatSauvegarde();
        return Task.CompletedTask;
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

    // ---------------------------------------------------------------- Étiquettes

    private CarteSection CarteEtiquettes()
    {
        var carte = new CarteSection("Étiquettes QR", Icones.Qr, Theme.AccentClair);

        var format = new ListeDeroulante { AccessibleName = "Format des étiquettes", Margin = new Padding(0, 0, 0, 10) };
        foreach (var f in ServiceReglages.FormatsEtiquette)
        {
            format.Items.Add(f);
        }

        format.SelectedIndex = Math.Clamp(_reglages.Actuels.FormatEtiquette, 0, format.Items.Count - 1);
        format.SelectedIndexChanged += (_, _) => _reglages.ChangerFormatEtiquette(format.SelectedIndex);
        var conseil = Ui.Libelle("Utilisez des étiquettes plastifiées, résistantes à l'eau salée.", Theme.Police(9.75f), Theme.TexteSecondaire);
        conseil.AutoSize = false;
        conseil.Height = 40;

        // La liste déroulante prend sa hauteur à l'affichage : ligne de hauteur fixe pour ne pas chevaucher le conseil.
        var droite = Ui.Colonne(
            (Ui.LibelleChamp("Format"), SizeType.AutoSize, 0),
            (format, SizeType.Absolute, Theme.HauteurChamp + 10),
            (conseil, SizeType.AutoSize, 0));
        droite.AutoSize = true;
        droite.Margin = new Padding(20, 0, 0, 0);
        format.Dock = DockStyle.Top;

        var apercu = new ApercuEtiquette(_reglages.Actuels.NomClub) { Margin = new Padding(0, 0, 0, 18) };
        var ligne = Ui.Colonnes((apercu, SizeType.Absolute, 220), (droite, SizeType.Percent, 100));
        ligne.AutoSize = true;
        ligne.RowStyles[0] = new RowStyle(SizeType.AutoSize);
        apercu.Dock = DockStyle.None;
        apercu.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        carte.Ajouter(ligne);

        // L'impression arrivera avec QRCoder (dépendance à valider).
        void Bientot() => Dialogues.Information(FindForm(), "L'impression des étiquettes arrive avec l'étape « QR codes ».");
        var imprimer = new BoutonArrondi("Imprimer des étiquettes", StyleBouton.Principal, Icones.Imprimante) { AutoSize = false, Margin = new Padding(0, 0, 10, 0) };
        imprimer.Click += (_, _) => Bientot();
        var pdf = new BoutonArrondi("Aperçu PDF", StyleBouton.Secondaire) { Margin = Padding.Empty };
        pdf.Click += (_, _) => Bientot();
        var boutons = Ui.Colonnes((imprimer, SizeType.Percent, 100), (pdf, SizeType.AutoSize, 0));
        boutons.Height = Theme.HauteurBouton;
        carte.Ajouter(boutons);
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

    /// <summary>Aperçu d'une étiquette blanche : QR (pictogramme), nom du club, code et désignation.</summary>
    private sealed class ApercuEtiquette : ControleDessine
    {
        private readonly string _club;

        public ApercuEtiquette(string club)
        {
            _club = club.ToUpper(Libelles.Culture);
            Size = new Size(220, 112);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Aperçu d'étiquette";
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var chemin = Dessin.Arrondi(r, E(16)))
            using (var blanc = new SolidBrush(Color.White))
            {
                g.FillPath(blanc, chemin);
            }

            var qr = E(84);
            var x = E(14);
            var y = (Height - qr) / 2;
            Icones.Dessiner(g, Icones.Qr, new RectangleF(x, y, qr, qr), Theme.Fond, 2.2f);
            var gauche = (int)(x + qr + E(12));
            var largeur = Width - gauche - (int)E(8);
            Dessin.Texte(g, _club, Theme.PoliceTitre(7.5f), new Rectangle(gauche, (int)(y + E(14)), largeur, (int)E(16)), Theme.BandeauHaut);
            Dessin.Texte(g, "BLOC-031", Theme.PoliceCode(11f), new Rectangle(gauche, (int)(y + E(32)), largeur, (int)E(22)), Theme.Fond);
            Dessin.Texte(g, "Bloc 12 L acier", Theme.Police(8.25f), new Rectangle(gauche, (int)(y + E(56)), largeur, (int)E(16)), Theme.SurfaceActive);
        }
    }
}
