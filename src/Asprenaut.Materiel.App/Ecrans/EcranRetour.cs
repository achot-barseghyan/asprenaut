using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.App.Navigation;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Étape 2 : scan du matériel qui rentre, puis clôture.</summary>
public sealed class EcranRetour : EcranScanBase
{
    private readonly ServiceSortie _sorties;
    private readonly ServiceScan _scan;
    private readonly AnneauProgression _anneau = new(104);
    private readonly BoutonArrondi _cloturer = new("Clôturer la sortie", StyleBouton.Secondaire);
    private readonly Action _actualiserCloture;
    private int _nbDehors;
    private readonly Label _conseil = Ui.Libelle("", Theme.Police(9.75f), Theme.TexteSecondaireClair);
    private readonly BoutonArrondi _ajouter = new("+ Ajouter du matériel à cette sortie", StyleBouton.Lien);
    private readonly Pastille _compteARendre = Compteur(Theme.Depart, Theme.DepartClair);
    private readonly Pastille _compteRendu = Compteur(Theme.Retour, Theme.Retour);
    private readonly ListeMateriel _aRendre = new(StyleListe.ARendre, "Tout le matériel est rentré", Icones.CercleCoche, Theme.Retour);
    private readonly ListeMateriel _rendu = new(StyleListe.Rendu, "Rien n'est encore rendu.");
    private readonly FiltreCategories _filtre = new(Theme.Depart, "Type") { Margin = new Padding(0, 0, 0, 12) };
    private IReadOnlyList<LigneSortie> _lignes = [];

    private readonly GardeGestion _garde;
    private readonly ServiceSignalements _signalements;
    private readonly BoutonArrondi _signalerDernier = new("Signaler un problème", StyleBouton.Secondaire, Icones.Drapeau) { Visible = false };
    private readonly LienSignalements _lienSignalements = new() { Visible = false };
    private IReadOnlyDictionary<long, Signalement> _ouverts = new Dictionary<long, Signalement>();
    private SortieResume? _resume;
    private long? _dernierRendu;
    private bool _boutonCourt;

    public EcranRetour(ServiceSortie sorties, ServiceScan scan, ServiceReglages reglages, INavigation navigation, GardeGestion garde, ServiceManuels manuels,
        ServiceSignalements signalements)
        : base(navigation, reglages, manuels, ModeScan.Retour)
    {
        _garde = garde;
        _signalements = signalements;
        _actualiserCloture = _garde.Lier(_cloturer, ActionProtegee.CloturerAvecManquants, () => _nbDehors > 0);
        _sorties = sorties;
        _scan = scan;
        _cloturer.Click += (_, _) => Executer(CloturerAsync);
        _ajouter.Click += (_, _) => Navigation.AllerA<EcranDepart>(SortieId);

        // Carte de progression : anneau, bouton de clôture, conseil, ajout de matériel.
        _cloturer.AutoSize = false;
        _cloturer.Margin = Padding.Empty;
        _conseil.AutoSize = false;
        _ajouter.Margin = Padding.Empty;
        _ajouter.Height = Theme.HauteurCliquable;
        _ajouter.Font = Theme.PoliceTitre(10f);
        var actions = Ui.Colonne(
            (_cloturer, SizeType.Absolute, 48),
            (_conseil, SizeType.Percent, 100),
            (_ajouter, SizeType.Absolute, 44));
        _conseil.Margin = new Padding(0, 8, 0, 0);
        _ajouter.Dock = DockStyle.None;
        _ajouter.Anchor = AnchorStyles.Left;
        var carteProgression = new Carte { Padding = new Padding(20, 18, 20, 14), Margin = new Padding(0, 16, 0, 0) };
        var ligne = Ui.Colonnes((_anneau, SizeType.Absolute, 104 + 20), (actions, SizeType.Percent, 100));
        _anneau.Dock = DockStyle.None;
        _anneau.Anchor = AnchorStyles.Left;
        carteProgression.Controls.Add(ligne);

        // Carte étroite (petit écran) : l'anneau laisse la place au bouton et au conseil.
        carteProgression.Resize += (_, _) =>
        {
            var large = carteProgression.Width >= Dessin.Echelle(carteProgression, 360);
            _anneau.Visible = large;
            ligne.ColumnStyles[0].Width = large ? Dessin.Echelle(carteProgression, 124) : 0;
        };

        // Message du dernier scan, et à droite « Signaler un problème » sur l'équipement qui vient de rentrer.
        _signalerDernier.AutoSize = false;
        _signalerDernier.CouleurIcone = Theme.Avertissement;
        _signalerDernier.AccessibleDescription = "Signaler un problème sur le dernier équipement rendu";
        _signalerDernier.Click += (_, _) =>
        {
            if (_dernierRendu is { } id && _lignes.FirstOrDefault(l => l.EquipementId == id) is { } ligne)
            {
                Executer(() => SignalerAsync(ligne));
            }
        };
        ZoneScan.Bandeau.Margin = Padding.Empty;
        _signalerDernier.Margin = new Padding(12, 0, 0, 0);
        var ligneBandeau = Ui.Colonnes((ZoneScan.Bandeau, SizeType.Percent, 100), (_signalerDernier, SizeType.AutoSize, 0));
        _signalerDernier.Dock = DockStyle.None;
        _signalerDernier.Anchor = AnchorStyles.Right;
        _signalerDernier.Size = new Size(228, 52);
        ligneBandeau.Margin = new Padding(0, 16, 0, 0);

        // Colonne étroite (1280 × 800) : bouton court, le message du scan garde la place de deux lignes lisibles.
        ligneBandeau.Resize += (_, _) =>
        {
            _boutonCourt = ligneBandeau.Width < Dessin.Echelle(ligneBandeau, 600);
            _signalerDernier.Width = (int)Dessin.Echelle(ligneBandeau, _boutonCourt ? 132 : 228);
            AfficherSignalements();
        };

        var gauche = Ui.Colonne(
            (ZoneScan, SizeType.Percent, 100),
            (ligneBandeau, SizeType.Absolute, 76 + 16),
            (carteProgression, SizeType.Absolute, 190 + 16));
        gauche.Margin = new Padding(0, 0, 22, 0);

        // Écran peu haut : la zone de scan garde au moins la place de sa consigne et de son champ ;
        // le bandeau et la carte de progression se resserrent.
        gauche.Resize += (_, _) =>
        {
            var petit = gauche.Height < Dessin.Echelle(gauche, 560);
            gauche.RowStyles[1].Height = Dessin.Echelle(gauche, (petit ? 64 : 76) + 16);
            gauche.RowStyles[2].Height = Dessin.Echelle(gauche, (petit ? 150 : 190) + 16);
            _conseil.Visible = !petit;
        };

        var carteARendre = new Carte { CouleurBordure = Theme.Teinte(Theme.Depart, 0.22f), Padding = new Padding(20), Margin = new Padding(0, 0, 8, 0) };
        carteARendre.Controls.Add(Ui.Colonne(
            (EnteteListe(Ui.TitreCarte("À rendre"), _compteARendre), SizeType.Absolute, HauteurEnteteListe),
            (_aRendre, SizeType.Percent, 100)));
        var carteRendu = new Carte { CouleurBordure = Theme.Teinte(Theme.Retour, 0.22f), Padding = new Padding(20), Margin = new Padding(8, 0, 0, 0) };
        carteRendu.Controls.Add(Ui.Colonne(
            (EnteteListe(Ui.TitreCarte("Rendu"), _compteRendu), SizeType.Absolute, HauteurEnteteListe),
            (_rendu, SizeType.Percent, 100)));
        var cartes = Ui.Colonnes((carteARendre, SizeType.Percent, 50), (carteRendu, SizeType.Percent, 50));
        _filtre.Changement += (_, _) => AfficherListes();
        _lienSignalements.Margin = new Padding(0, 0, 0, 12);
        _lienSignalements.Click += (_, _) => Navigation.AllerA<EcranRevisions>();
        var droite = Ui.Colonne((_lienSignalements, SizeType.AutoSize, 0), (_filtre, SizeType.Absolute, 44 + 12), (cartes, SizeType.Percent, 100));

        Composer(Ui.Colonnes((gauche, SizeType.Percent, 40), (droite, SizeType.Percent, 60)));
    }

    public override string Titre => "Retour du matériel";

    protected override async Task RafraichirAsync()
    {
        var resume = await _sorties.ObtenirResumeAsync(SortieId) ?? throw new InvalidOperationException("Sortie introuvable.");
        var lignes = await _sorties.ListerMaterielAsync(SortieId);
        _ouverts = await _signalements.OuvertsAsync();
        _resume = resume;
        AfficherSortie(resume, resume.DepartValideLe is { } parti ? $"parti à {Heure(parti)}" : null);

        var cloturee = resume.Statut == StatutSortie.Cloturee;
        var aRendre = lignes.Where(l => l.Statut != StatutSortieEquipement.Rendu).ToList();
        var rendus = lignes.Where(l => l.Statut == StatutSortieEquipement.Rendu).OrderByDescending(l => l.ScanneRetourLe).ToList();

        _anneau.Valeur = resume.Progression;
        _anneau.Nombre = resume.NbRendus.ToString(Libelles.Culture);
        _anneau.Total = $"/{resume.NbTotal}";
        _anneau.Legende = resume.NbRendus > 1 ? "rendus" : "rendu";

        var toutRendu = aRendre.Count == 0;
        _cloturer.Visible = !cloturee;
        _nbDehors = resume.NbDehors;
        _actualiserCloture(); // Cadenas seulement s'il resterait des manquants (et si le réglage le demande).
        _cloturer.Style = toutRendu ? StyleBouton.Retour : StyleBouton.Secondaire;
        _ajouter.Visible = resume.Statut == StatutSortie.EnCours;
        _conseil.Text = cloturee
            ? resume.NbManquants > 0
                ? $"Sortie clôturée avec {Libelles.Pluriel(resume.NbManquants, "manquant")}. Scannez un manquant s'il revient."
                : "Sortie clôturée : tout a été rendu."
            : toutRendu
                ? "Tout est rentré. Vous pouvez clôturer la sortie."
                : $"{Libelles.Pluriel(resume.NbDehors, "équipement")} {(resume.NbDehors > 1 ? "seront notés manquants" : "sera noté manquant")} si vous clôturez maintenant.";

        // Puces « Type » : toutes les catégories de la sortie, comptées sur le matériel encore à rendre.
        await ChargerManuelsAsync();
        _lignes = lignes;
        _filtre.Afficher(aRendre.Count, EcranDepart.Categories(lignes, l => l.Statut != StatutSortieEquipement.Rendu));
        AfficherListes();
        AfficherSignalements();
    }

    /// <summary>Bouton du dernier rendu (« Signaler » ou « Modifier le problème ») et bandeau des problèmes de cette sortie.</summary>
    private void AfficherSignalements()
    {
        var dernier = _dernierRendu is { } id ? _lignes.FirstOrDefault(l => l.EquipementId == id && l.Statut == StatutSortieEquipement.Rendu) : null;
        _signalerDernier.Visible = dernier is not null;
        if (dernier is not null)
        {
            var dejaSignale = _ouverts.ContainsKey(dernier.EquipementId);
            _signalerDernier.Text = (dejaSignale, _boutonCourt) switch
            {
                (true, true) => "Modifier",
                (true, false) => "Modifier le problème",
                (false, true) => "Signaler",
                _ => "Signaler un problème",
            };
            _signalerDernier.AccessibleName = dejaSignale ? "Modifier le problème" : "Signaler un problème";
        }

        var deCetteSortie = _lignes.Count(l => _ouverts.TryGetValue(l.EquipementId, out var s) && s.SortieId == SortieId);
        _lienSignalements.Nombre = deCetteSortie;
        _lienSignalements.Visible = deCetteSortie > 0;
    }

    /// <summary>Fenêtre « Problème sur A07 » pour un équipement rendu, puis le message dans le bandeau du scan.</summary>
    private async Task SignalerAsync(LigneSortie ligne)
    {
        var contexte = _resume is { } s
            ? $"{s.Intitule} du {s.DateSortie.ToString("dd/MM", Libelles.Culture)} · {s.MoniteurNom}"
            : "";
        var resultat = await FormulaireSignalement.OuvrirAsync(FindForm(), _signalements, _garde,
            new CibleSignalement(ligne.EquipementId, ligne.Code, ligne.Libelle, ligne.Categorie ?? "", SortieId, contexte));
        if (resultat is null || IsDisposed)
        {
            ZoneScan.PrendreFocus();
            return;
        }

        ZoneScan.AfficherMessage(NiveauResultat.Avertissement, resultat.Message, ligne.Code);
        await RafraichirAsync();
        ZoneScan.PrendreFocus();
    }

    private void AfficherListes()
    {
        var aRendre = _lignes.Where(l => l.Statut != StatutSortieEquipement.Rendu && _filtre.Accepte(l.Categorie)).ToList();
        var rendus = _lignes.Where(l => l.Statut == StatutSortieEquipement.Rendu && _filtre.Accepte(l.Categorie))
            .OrderByDescending(l => l.ScanneRetourLe).ToList();
        _compteARendre.Text = aRendre.Count.ToString(Libelles.Culture);
        _compteRendu.Text = rendus.Count.ToString(Libelles.Culture);

        if (_filtre.Selection is null)
        {
            _aRendre.DefinirVide("Tout le matériel est rentré", Icones.CercleCoche, Theme.Retour);
            _rendu.DefinirVide("Rien n'est encore rendu.");
        }
        else
        {
            _aRendre.DefinirVide("Rien à rendre dans cette catégorie", Icones.CercleCoche, Theme.Retour);
            _rendu.DefinirVide("Rien de rendu dans cette catégorie.");
        }

        _aRendre.Afficher(aRendre.Select(l => new ElementMateriel(
            l.EquipementId,
            l.Code,
            l.Libelle,
            l.Categorie,
            Alerte: l.Statut == StatutSortieEquipement.Manquant ? "Manquant" : null,
            CouleurAlerte: Theme.Erreur,
            ManuelId: ManuelDe(l.EquipementId))), ouvrirManuel: OuvrirManuel);
        // Rendu : drapeau « Signaler » sur chaque ligne ; un problème ouvert se voit en pastille et dans le détail.
        _rendu.Afficher(rendus.Select(l =>
        {
            var probleme = _ouverts.GetValueOrDefault(l.EquipementId);
            var heure = l.ScanneRetourLe is { } r ? Heure(r) : null;
            return new ElementMateriel(
                l.EquipementId,
                l.Code,
                l.Libelle,
                l.Categorie,
                probleme is null ? heure : string.Join(" · ", new[] { heure, probleme.Cause }.Where(t => t is not null)),
                Alerte: probleme is null ? null : ServiceSignalements.Libelle(probleme.Gravite),
                CouleurAlerte: probleme?.Gravite == GraviteSignalement.HorsService ? Theme.Erreur : Theme.Avertissement,
                ManuelId: ManuelDe(l.EquipementId));
        }), "Signaler un problème sur", Icones.Drapeau, e =>
        {
            if (_lignes.FirstOrDefault(l => l.EquipementId == e.EquipementId) is { } ligne)
            {
                Executer(() => SignalerAsync(ligne));
            }
        }, OuvrirManuel);
    }

    protected override async Task TraiterCodeAsync(string code)
    {
        var resultat = await _scan.ScannerRetourAsync(SortieId, code);
        ZoneScan.AfficherResultat(resultat);
        if (resultat is { Niveau: NiveauResultat.Succes, Equipement: { } rendu })
        {
            _dernierRendu = rendu.Id; // « Signaler un problème » apparaît à côté du message.
        }

        if (resultat is { Action: ActionProposee.RentrerDansAutreSortie, AutreSortieId: { } autre, Code: { } c }
            && Dialogues.Confirmer(FindForm(), resultat.Message))
        {
            ZoneScan.AfficherResultat(await _scan.ScannerRetourAsync(autre, c));
        }

        await RafraichirAsync();
    }

    private async Task CloturerAsync()
    {
        var apercu = await _sorties.ApercuClotureAsync(SortieId);
        if (apercu.ConfirmationNecessaire && !Dialogues.Confirmer(FindForm(), apercu.MessageConfirmation))
        {
            return;
        }

        // Réglage « Code pour clôturer avec des manquants » : le garde ne demande le code que s'il est activé.
        if (apercu.NbManquants > 0 && !_garde.Exiger(FindForm(), ActionProtegee.CloturerAvecManquants, "Clôturer avec du matériel manquant"))
        {
            return;
        }

        var resultat = await _sorties.CloturerAsync(SortieId);
        if (!resultat.Reussi)
        {
            ZoneScan.AfficherMessage(NiveauResultat.Erreur, resultat.Message);
            return;
        }

        Dialogues.Information(FindForm(), resultat.Message);
        Navigation.AllerA<EcranSorties>();
    }

    /// <summary>
    /// Bandeau au-dessus des listes : « 2 problèmes signalés · envoyés dans Révisions › À prévoir. » et « Voir › »
    /// (ouvre Révisions). Toute la bande est cliquable.
    /// </summary>
    private sealed class LienSignalements : ControleDessine
    {
        private int _nombre;
        private bool _survol;

        public LienSignalements()
        {
            Height = 48;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.Link;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Nombre
        {
            get => _nombre;
            set
            {
                _nombre = value;
                AccessibleName = $"{Texte} Voir dans Révisions";
                Invalidate();
            }
        }

        private string Texte => $"{Libelles.Pluriel(_nombre, "problème signalé", "problèmes signalés")} · {(_nombre > 1 ? "envoyés" : "envoyé")} dans Révisions › À prévoir.";

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _survol = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _survol = false;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void Peindre(Graphics g)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var chemin = Dessin.Arrondi(r, E(16)))
            using (var fond = new SolidBrush(Theme.Sur(Theme.Fond, Theme.Erreur, _survol ? 0.16f : 0.10f)))
            using (var bord = new Pen(Theme.Sur(Theme.Fond, Theme.Erreur, Focused ? 0.8f : 0.4f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var t = E(20);
            Icones.Dessiner(g, Icones.Alerte, new RectangleF(E(16), (Height - t) / 2, t, t), Theme.Erreur, 2f);
            var voir = "Voir ›";
            var policeVoir = Theme.PoliceTitre(9.75f);
            var lVoir = TextRenderer.MeasureText(g, voir, policeVoir).Width;
            var droite = Width - (int)E(16);
            Dessin.Texte(g, voir, policeVoir, new Rectangle(droite - lVoir, 0, lVoir, Height), Theme.AccentClair);
            var gauche = (int)(E(16) + t + E(12));
            Dessin.Texte(g, Texte, Theme.Police(10f), new Rectangle(gauche, 0, droite - lVoir - (int)E(12) - gauche, Height), Theme.Texte);
        }
    }
}
