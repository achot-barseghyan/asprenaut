using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Départ d'une sortie nominative (maquette « Départ du matériel ») : à la place de la zone de scan, après chaque scan,
/// « À qui est ce détendeur ? ». Raccourcis vers les 3 derniers adhérents de la sortie (le plus récent en avant), puis
/// un champ de recherche dont la liste des adhérents actifs s'ouvre par-dessus (frappe, flèches, clic, chevron).
/// Tout se fait au clavier : flèches et Entrée dans la recherche, Échap pour refermer, Tab vers les raccourcis.
/// Un code tapé par le pistolet dans la recherche est refusé (<see cref="ScanRefuse"/>).
/// Placé en haut de sa zone (hauteur d'après le contenu) : la liste ouverte peut déborder sous la carte, comme la maquette.
/// </summary>
public sealed class PanneauAdherent : Carte
{
    // Pixels logiques (96 dpi), convertis par E(...) à chaque mise en page.
    private const int Marge = 30;
    private const int HauteurEntete = 72;
    private const int HauteurTitre = 22;
    private const int HauteurTuile = 62;
    private const int EcartTuiles = 10;
    private const int HauteurBloque = 50;
    private const int HauteurBas = 44;

    private readonly EnTeteScan _entete = new();
    private readonly Label _titreRecents = Titre("Même adhérent que les derniers scans");
    private readonly Label _titreListe = Titre("Ou choisissez dans la liste des adhérents");
    private readonly List<TuileAdherent> _tuiles = [];
    private readonly ChampSaisie _recherche = new() { TexteIndicatif = "Rechercher un adhérent…", Icone = Icones.Recherche, IndicatifSiFocus = true };
    private readonly Chevron _chevron = new();
    private readonly ListeAdherents _liste = new() { Visible = false };
    private readonly BandeauBloque _bloque = new();
    private readonly Label _message = Ui.Libelle("", Theme.PoliceTitre(9.5f), Theme.Avertissement);
    private readonly BoutonArrondi _annuler = new("Annuler ce scan", StyleBouton.Lien, Icones.Croix) { Margin = Padding.Empty };
    private readonly ValidationSansEntree _rafale;
    private IReadOnlyList<Adherent> _tous = [];
    private string _code = "";
    private bool _nitrox;
    private bool _effacement;

    public PanneauAdherent()
    {
        CouleurBordure = Theme.Sur(Theme.Surface, Theme.Accent, 0.45f);
        BackColor = Theme.Sur(Theme.Surface, Theme.Accent, 0.04f);
        Padding = Padding.Empty;
        Rayon = Theme.RayonZoneScan;

        _recherche.Champ.AccessibleName = "Rechercher un adhérent";
        _recherche.ADroite = _chevron;
        _recherche.Champ.TextChanged += (_, _) => RechercheChangee();
        _recherche.Champ.KeyDown += ToucheRecherche;
        _recherche.Champ.MouseDown += (_, _) => Ouvrir();
        _recherche.Champ.Leave += (_, _) => Fermer();
        _chevron.Click += (_, _) =>
        {
            if (_liste.Visible)
            {
                Fermer();
            }
            else
            {
                Ouvrir();
            }

            _recherche.Champ.Focus();
        };
        _rafale = new ValidationSansEntree(_recherche.Champ, () => RefuserScan(_recherche.Valeur));
        _liste.Valide += (_, id) => Choisir(id);

        // Message (adhérent non choisi, scan refusé) et « Annuler ce scan » (scan fait par erreur : l'équipement ne part pas).
        _message.AutoEllipsis = true;
        _annuler.Click += (_, _) => AnnulerScan?.Invoke(this, EventArgs.Empty);

        Controls.AddRange([_entete, _titreRecents, _titreListe, _recherche, _bloque, _message, _annuler]);
        AccessibleRole = AccessibleRole.Grouping;
        _pret = true;
    }

    private readonly bool _pret;

    /// <summary>Un adhérent a été choisi (raccourci, liste, Entrée).</summary>
    public event EventHandler<long>? Choisi;

    /// <summary>« Annuler ce scan » : l'équipement en attente est retiré de la sortie.</summary>
    public event EventHandler? AnnulerScan;

    /// <summary>Le pistolet a tapé un code dans la recherche : le scan suivant est refusé tant que l'équipement n'a pas d'adhérent.</summary>
    public event EventHandler<string>? ScanRefuse;

    /// <summary>Code de l'équipement qui attend son adhérent.</summary>
    public string Code => _code;

    private static Label Titre(string texte)
    {
        var l = Ui.Libelle(texte, Theme.PoliceTitre(9.75f), Theme.TexteSecondaireClair);
        l.AutoSize = false;
        l.Margin = Padding.Empty;
        l.TextAlign = ContentAlignment.MiddleLeft;
        return l;
    }

    private float E(float v) => Dessin.Echelle(this, v);

    /// <summary>
    /// Équipement à attribuer (« À qui est ce détendeur ? »), raccourcis de la sortie et adhérents actifs.
    /// Garde la recherche si c'est le même équipement (rechargement après un refus).
    /// </summary>
    public void Afficher(LigneSortie ligne, IReadOnlyList<AdherentRecent> recents, IReadOnlyList<Adherent> tous)
    {
        var nouveau = ligne.Code != _code;
        _code = ligne.Code;
        _tous = tous;
        _nitrox = ligne.Nitrox;
        var question = $"À qui est {Article(ligne.Categorie)} ?";
        _entete.Afficher(Icones.PourCategorie(ligne.Categorie), $"Scanné à {ligne.ScanneDepartLe.ToLocalTime():HH:mm}", question,
            ligne.Libelle, ligne.Code, ligne.Nitrox ? "nitrox : adhérent certifié nitrox" : null);
        AccessibleName = $"{question} {ligne.Libelle}, {ligne.Code}";

        SuspendLayout();
        foreach (var t in _tuiles)
        {
            Controls.Remove(t);
            t.Dispose();
        }

        _tuiles.Clear();
        for (var i = 0; i < recents.Count; i++)
        {
            var r = recents[i];
            var refuse = _tous.FirstOrDefault(a => a.Id == r.AdherentId) is not { } adherent || ServiceAdherents.RefusPour(adherent, _nitrox, _code) is not null;
            var detail = refuse ? (_nitrox ? "Pas certifié nitrox" : "Désactivé") : (i == 0 ? "Dernier scan · " : "") + $"{r.NbEquipements} équip.";
            var tuile = new TuileAdherent(r.AdherentId, r.Nom, detail, enAvant: i == 0 && !refuse, refuse) { TabIndex = i };
            tuile.Click += (_, _) => Choisir(r.AdherentId);
            _tuiles.Add(tuile);
            Controls.Add(tuile);
        }

        if (nouveau)
        {
            _message.Text = "";
            Effacer();
        }

        ResumeLayout(true);
        Filtrer();
        Fermer();
    }

    /// <summary>« ce détendeur », « cette combinaison », « cette paire de palmes »...</summary>
    public static string Article(string categorie) => categorie.Trim().ToLower(Libelles.Culture) switch
    {
        var c when c.StartsWith("détendeur", StringComparison.Ordinal) => "ce détendeur",
        var c when c.StartsWith("gilet", StringComparison.Ordinal) => "ce gilet",
        var c when c.StartsWith("bloc", StringComparison.Ordinal) => "ce bloc",
        var c when c.StartsWith("combinaison", StringComparison.Ordinal) => "cette combinaison",
        var c when c.StartsWith("palme", StringComparison.Ordinal) => "cette paire de palmes",
        var c when c.StartsWith("masque", StringComparison.Ordinal) => "ce masque",
        var c when c.StartsWith("ordinateur", StringComparison.Ordinal) => "cet ordinateur",
        _ => "cet équipement",
    };

    public void PrendreFocus() => _recherche.Champ.Focus();

    /// <summary>Message sous le champ (« Choisissez d'abord l'adhérent de A07… »).</summary>
    public void Signaler(string message) => _message.Text = message;

    // --- Mise en page : de haut en bas, hauteur d'après le contenu ---

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (!_pret)
        {
            return; // Appelé depuis le constructeur (classe de base, ajout des enfants).
        }

        // Petit écran (1280 × 800) : sans place, on retire les deux titres de section et le bandeau « bloqué »
        // (le message sous le panneau explique déjà un scan refusé) ; recherche et raccourcis restent.
        var place = Parent?.ClientSize.Height ?? int.MaxValue;
        var compact = Placer(compact: false, appliquer: false) > place;
        var hauteur = Placer(compact, appliquer: true);
        if (Height != hauteur)
        {
            Height = hauteur;
        }

        if (_liste.Visible)
        {
            PlacerListe();
        }
    }

    private int Placer(bool compact, bool appliquer)
    {
        var gauche = (int)E(Marge);
        var largeur = Width - (2 * gauche);
        var y = (int)E(24);

        void Mettre(Control c, int hauteur, bool visible = true)
        {
            if (!appliquer)
            {
                return;
            }

            if (c.Visible != visible)
            {
                c.Visible = visible;
            }

            if (visible)
            {
                c.SetBounds(gauche, y, largeur, hauteur);
            }
        }

        Mettre(_entete, (int)E(HauteurEntete));
        y += (int)E(HauteurEntete + 18);

        Mettre(_titreRecents, (int)E(HauteurTitre), _tuiles.Count > 0 && !compact);
        if (_tuiles.Count > 0)
        {
            if (!compact)
            {
                y += (int)E(HauteurTitre + 8);
            }

            // Raccourcis côte à côte, à la ligne quand ils ne tiennent plus (maquette : 2 puis 1).
            var x = gauche;
            var hauteurTuile = (int)E(HauteurTuile);
            var ecart = (int)E(EcartTuiles);
            foreach (var t in _tuiles)
            {
                var l = t.LargeurIdeale();
                if (x > gauche && x + l > gauche + largeur)
                {
                    x = gauche;
                    y += hauteurTuile + ecart;
                }

                if (appliquer)
                {
                    t.SetBounds(x, y, Math.Min(l, largeur), hauteurTuile);
                }

                x += l + ecart;
            }

            y += hauteurTuile + (int)E(20);
        }

        Mettre(_titreListe, (int)E(HauteurTitre), !compact);
        if (!compact)
        {
            y += (int)E(HauteurTitre + 8);
        }

        Mettre(_recherche, (int)E(Theme.HauteurChamp));
        y += (int)E(Theme.HauteurChamp + 16);

        Mettre(_bloque, (int)E(HauteurBloque), !compact);
        if (!compact)
        {
            y += (int)E(HauteurBloque + 4);
        }

        // Message à gauche, « Annuler ce scan » à droite.
        if (appliquer)
        {
            var bouton = _annuler.GetPreferredSize(Size.Empty);
            var hb = (int)E(HauteurBas);
            _annuler.SetBounds(gauche + largeur - bouton.Width + (int)E(6), y + ((hb - bouton.Height) / 2), bouton.Width, bouton.Height);
            _message.SetBounds(gauche, y, Math.Max(0, largeur - bouton.Width - (int)E(12)), hb);
        }

        y += (int)E(HauteurBas + 12);
        return y;
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);

        // La liste s'ouvre par-dessus la carte (et peut la dépasser) : elle vit dans la zone qui porte le panneau.
        _liste.Parent?.Controls.Remove(_liste);
        Parent?.Controls.Add(_liste);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            Fermer();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Fermer();
    }

    // --- Liste des adhérents, ouverte sous le champ ---

    private void Ouvrir()
    {
        if (_liste.Visible || Parent is null || !Visible)
        {
            return;
        }

        if (_liste.Choix is null)
        {
            _liste.Deplacer(1);
        }

        PlacerListe();
        _liste.Visible = true;
        _liste.BringToFront();
        _chevron.Ouvert = true;
    }

    private void Fermer()
    {
        if (_liste.Visible)
        {
            _liste.Visible = false;
        }

        _chevron.Ouvert = false;
    }

    /// <summary>Sous le champ (au-dessus s'il y a nettement plus de place), jusqu'à 6 lignes et demie, sans sortir de la zone.</summary>
    private void PlacerListe()
    {
        if (Parent is not { } zone)
        {
            return;
        }

        var champ = zone.RectangleToClient(_recherche.RectangleToScreen(_recherche.ClientRectangle));
        var ecart = (int)E(8);
        var voulu = _liste.HauteurIdeale();
        var dessous = zone.ClientSize.Height - champ.Bottom - (2 * ecart);
        var dessus = champ.Top - (2 * ecart);
        var auDessus = voulu > dessous && dessus > dessous;
        var hauteur = Math.Max((int)E(60), Math.Min(voulu, auDessus ? dessus : dessous));
        _liste.SetBounds(champ.Left, auDessus ? champ.Top - ecart - hauteur : champ.Bottom + ecart, champ.Width, hauteur);
    }

    private void RechercheChangee()
    {
        Filtrer();
        if (!_effacement && _recherche.Champ.Focused)
        {
            Ouvrir();
        }

        if (_liste.Visible)
        {
            PlacerListe(); // La hauteur suit le nombre d'adhérents trouvés.
        }
    }

    private void Effacer()
    {
        _effacement = true;
        _recherche.Valeur = "";
        _effacement = false;
    }

    private void Filtrer()
    {
        // Équipement nitrox : les certifiés d'abord ; les autres restent visibles, marqués (le service refuse leur choix).
        var trouves = ServiceAdherents.Filtrer(_tous, _recherche.Valeur)
            .Select(a => (Adherent: a, Refus: ServiceAdherents.RefusPour(a, _nitrox, _code) is not null))
            .OrderBy(t => t.Refus)
            .ToList();
        _liste.Afficher(trouves.Select(t => new LigneAdherent(t.Adherent.Id, t.Adherent.Initiales, t.Adherent.NomComplet,
                t.Refus
                    ? (_nitrox ? "Pas certifié nitrox" : "Désactivé")
                    : string.Join(" · ", new[] { t.Adherent.Niveau, _nitrox ? "nitrox" : null }.Where(s => !string.IsNullOrEmpty(s))),
                t.Refus)).ToList(),
            _tous.Count == 0 ? "Aucun adhérent : ajoutez-les dans Membres › Adhérents." : "Aucun adhérent ne correspond.");
    }

    private void ToucheRecherche(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down or Keys.Up:
                if (_liste.Visible)
                {
                    _liste.Deplacer(e.KeyCode == Keys.Down ? 1 : -1);
                }
                else
                {
                    Ouvrir();
                }

                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                e.Handled = e.SuppressKeyPress = true;
                _rafale.Annuler();
                if (ServiceAdherents.Filtrer(_tous, _recherche.Valeur).Count == 0 && _recherche.Valeur.Trim().Length > 0)
                {
                    RefuserScan(_recherche.Valeur); // Rien ne correspond : c'est sans doute le pistolet.
                }
                else if (!_liste.Visible)
                {
                    Ouvrir();
                }
                else if (_liste.Choix is { } id)
                {
                    Choisir(id);
                }
                else
                {
                    _message.Text = "Choisissez un adhérent dans la liste.";
                }

                break;
            case Keys.Escape:
                if (_liste.Visible)
                {
                    Fermer();
                }
                else
                {
                    Effacer();
                    Filtrer();
                }

                e.Handled = e.SuppressKeyPress = true;
                break;
        }
    }

    private void Choisir(long id)
    {
        Fermer();
        Choisi?.Invoke(this, id);
    }

    private void RefuserScan(string texte)
    {
        var code = texte.Trim();
        Effacer();
        Filtrer();
        Fermer();
        if (code.Length > 0)
        {
            ScanRefuse?.Invoke(this, code);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rafale.Dispose();
            _liste.Parent?.Controls.Remove(_liste);
            _liste.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Pastille ronde d'un adhérent (maquette) : disque sombre teinté, anneau et initiales de sa couleur, toujours la même pour
    /// un adhérent (d'après son numéro). Grisée s'il ne peut pas prendre l'équipement.
    /// </summary>
    internal static void PeindreAvatar(Graphics g, RectangleF r, string initiales, long id, Color fond, bool grise, Font police)
    {
        var couleur = grise ? Theme.TexteSecondaire : Theme.DegradesAvatar[(int)(id % Theme.DegradesAvatar.Length)].Fonce;

        // Thème clair : initiales assombries (contraste sur le disque pâle).
        var texte = grise ? Theme.TexteSecondaire : Theme.EstSombre ? Theme.DegradesAvatar[(int)(id % Theme.DegradesAvatar.Length)].Clair : Theme.Sur(couleur, Theme.Texte, 0.6f);
        using (var disque = new SolidBrush(Theme.Sur(fond, couleur, Theme.EstSombre ? 0.16f : 0.22f)))
        {
            g.FillEllipse(disque, r);
        }

        using (var anneau = new Pen(Theme.Sur(fond, couleur, 0.6f), Math.Max(1f, r.Width / 26f)))
        {
            g.DrawEllipse(anneau, r);
        }

        Dessin.Texte(g, initiales, police, Rectangle.Round(r), texte, centre: true);
    }

    /// <summary>Une ligne de la liste : pastille, nom, niveau à droite (ou la raison du refus).</summary>
    private sealed record LigneAdherent(long Id, string Initiales, string Nom, string Detail, bool Refuse);

    /// <summary>
    /// Liste des adhérents ouverte sous le champ de recherche (maquette) : cadre arrondi, une ligne par adhérent, barre de
    /// défilement fine. Tout est dessiné dans un seul contrôle. Jamais focusable : le curseur reste dans la recherche
    /// (flèches et Entrée), la souris choisit d'un clic.
    /// </summary>
    private sealed class ListeAdherents : Control
    {
        private const int HauteurLigne = 52;
        private const int MargeInterne = 6;
        private const int LignesVisibles = 6;

        private IReadOnlyList<LigneAdherent> _lignes = [];
        private string _messageVide = "";
        private int _choix = -1;
        private int _survol = -1;
        private float _defilement;
        private float? _saisieBarre;

        public ListeAdherents()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Margin = Padding.Empty;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Adhérents";
        }

        public event EventHandler<long>? Valide;

        /// <summary>Adhérent en surbrillance (même refusé : le service explique pourquoi), ou null.</summary>
        public long? Choix => _choix >= 0 && _choix < _lignes.Count ? _lignes[_choix].Id : null;

        private float E(float v) => Dessin.Echelle(this, v);

        private float Pas => E(HauteurLigne);

        private Color FondListe => Theme.Sur(Theme.Surface, Theme.Accent, Theme.EstSombre ? 0.07f : 0.04f);

        /// <summary>Remplace les lignes ; garde l'adhérent choisi s'il y est encore, sinon le premier.</summary>
        public void Afficher(IReadOnlyList<LigneAdherent> lignes, string messageVide)
        {
            var id = _choix >= 0 && _choix < _lignes.Count ? _lignes[_choix].Id : (long?)null;
            _lignes = lignes;
            _messageVide = messageVide;
            _survol = -1;
            _choix = id is { } garde && lignes.ToList().FindIndex(l => l.Id == garde) is var i and >= 0 ? i : (lignes.Count > 0 ? 0 : -1);
            _defilement = 0;
            Montrer(_choix);
            AccessibleDescription = lignes.Count == 0 ? messageVide : $"{lignes.Count} adhérents";
            Invalidate();
        }

        public int HauteurIdeale() => (int)(E(MargeInterne * 2) + (Math.Clamp(_lignes.Count, 1, LignesVisibles) * Pas) + (_lignes.Count > LignesVisibles ? Pas / 2 : 0));

        public void Deplacer(int sens)
        {
            if (_lignes.Count > 0)
            {
                _choix = Math.Clamp(_choix + sens, 0, _lignes.Count - 1);
                Montrer(_choix);
                AccessibilityNotifyClients(AccessibleEvents.Selection, _choix);
                Invalidate();
            }
        }

        private float HauteurContenu => _lignes.Count * Pas;

        private float HauteurVue => Height - E(MargeInterne * 2);

        private float DefilementMax => Math.Max(0, HauteurContenu - HauteurVue);

        private void Defiler(float valeur)
        {
            var v = Math.Clamp(valeur, 0, DefilementMax);
            if (Math.Abs(v - _defilement) > 0.1f)
            {
                _defilement = v;
                Invalidate();
            }
        }

        private void Montrer(int index)
        {
            if (index < 0 || Height <= 0)
            {
                return;
            }

            var haut = index * Pas;
            if (haut < _defilement)
            {
                Defiler(haut);
            }
            else if (haut + Pas > _defilement + HauteurVue)
            {
                Defiler(haut + Pas - HauteurVue);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using var chemin = Dessin.Arrondi(new RectangleF(0, 0, Width, Height), E(14));
            Region = new Region(chemin); // Coins arrondis : la liste passe par-dessus la carte et le fond de page.
            Defiler(_defilement);
            Montrer(_choix);
        }

        private RectangleF Barre => new(Width - E(MargeInterne + 8), E(MargeInterne + 4), E(6), Height - E((MargeInterne + 4) * 2));

        private RectangleF Curseur
        {
            get
            {
                var barre = Barre;
                var part = HauteurVue / Math.Max(HauteurContenu, 1);
                var h = Math.Max(E(28), barre.Height * part);
                var y = barre.Top + ((barre.Height - h) * (DefilementMax <= 0 ? 0 : _defilement / DefilementMax));
                return new RectangleF(barre.Left, y, barre.Width, h);
            }
        }

        private bool AvecBarre => DefilementMax > 0;

        private int IndexSous(Point p)
        {
            if (AvecBarre && p.X >= Barre.Left - E(4))
            {
                return -1;
            }

            var y = p.Y - E(MargeInterne) + _defilement;
            var i = (int)Math.Floor(y / Pas);
            return p.Y >= E(MargeInterne) && p.Y <= Height - E(MargeInterne) && i >= 0 && i < _lignes.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_saisieBarre is { } decalage)
            {
                var barre = Barre;
                var course = barre.Height - Curseur.Height;
                Defiler(course <= 0 ? 0 : (e.Y - decalage - barre.Top) / course * DefilementMax);
                return;
            }

            var i = IndexSous(e.Location);
            Cursor = i >= 0 && !_lignes[i].Refuse ? Cursors.Hand : Cursors.Default;
            if (i != _survol)
            {
                _survol = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_survol >= 0)
            {
                _survol = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !AvecBarre || e.X < Barre.Left - E(4))
            {
                return;
            }

            // Barre de défilement : on attrape le curseur, ou on saute à l'endroit cliqué.
            var curseur = Curseur;
            if (e.Y < curseur.Top || e.Y > curseur.Bottom)
            {
                Defiler(_defilement + (e.Y < curseur.Top ? -HauteurVue : HauteurVue));
                curseur = Curseur;
            }

            _saisieBarre = e.Y - curseur.Top;
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_saisieBarre is not null)
            {
                _saisieBarre = null;
                Capture = false;
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && IndexSous(e.Location) is var i and >= 0)
            {
                _choix = i;
                Invalidate();
                Valide?.Invoke(this, _lignes[i].Id);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Defiler(_defilement - (e.Delta / 120f * Pas * 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.Qualite(g);
            var fond = FondListe;
            g.Clear(fond);
            using (var chemin = Dessin.Arrondi(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), E(14)))
            using (var bord = new Pen(Theme.Sur(fond, Theme.Accent, 0.5f), E(1.2f)))
            {
                g.DrawPath(bord, chemin);
            }

            var marge = E(MargeInterne);
            var largeur = Width - (2 * marge) - (AvecBarre ? E(14) : 0);
            if (_lignes.Count == 0)
            {
                Dessin.Texte(g, _messageVide, Theme.Police(10f), new Rectangle((int)marge, (int)marge, (int)largeur, (int)Pas), Theme.TexteSecondaire, centre: true);
                return;
            }

            var etat = g.Save();
            g.SetClip(new RectangleF(0, marge, Width, Height - (2 * marge)));
            var premier = Math.Max(0, (int)(_defilement / Pas));
            for (var i = premier; i < _lignes.Count; i++)
            {
                var y = marge + (i * Pas) - _defilement;
                if (y > Height)
                {
                    break;
                }

                PeindreLigne(g, _lignes[i], new RectangleF(marge, y, largeur, Pas), fond, i == _choix, i == _survol);
            }

            g.Restore(etat);

            if (AvecBarre)
            {
                using var piste = new SolidBrush(Theme.Sur(fond, Theme.Texte, 0.08f));
                using var cheminPiste = Dessin.Arrondi(Barre, E(3));
                g.FillPath(piste, cheminPiste);
                using var pouce = new SolidBrush(Theme.Sur(fond, Theme.Texte, _saisieBarre is null ? 0.38f : 0.55f));
                using var cheminPouce = Dessin.Arrondi(Curseur, E(3));
                g.FillPath(pouce, cheminPouce);
            }
        }

        private void PeindreLigne(Graphics g, LigneAdherent l, RectangleF r, Color fond, bool choisi, bool survol)
        {
            var ligne = choisi ? Theme.Sur(fond, Theme.Accent, 0.15f) : survol ? Theme.Sur(fond, Theme.Texte, 0.06f) : fond;
            if (ligne != fond)
            {
                using var chemin = Dessin.Arrondi(RectangleF.Inflate(r, 0, -E(2)), E(10));
                using var pinceau = new SolidBrush(ligne);
                g.FillPath(pinceau, chemin);
            }

            var d = E(34);
            var avatar = new RectangleF(r.Left + E(12), r.Top + ((r.Height - d) / 2), d, d);
            PeindreAvatar(g, avatar, l.Initiales, l.Id, ligne, l.Refuse, Theme.PoliceGrasse(9f));

            var detail = TextRenderer.MeasureText(l.Detail, Theme.Police(9f)).Width;
            var droite = (int)(r.Right - E(14));
            var gauche = (int)(avatar.Right + E(14));
            Dessin.Texte(g, l.Nom, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(gauche, (int)r.Top, droite - detail - (int)E(10), (int)r.Bottom),
                l.Refuse ? Theme.TexteSecondaire : Theme.Texte);
            TextRenderer.DrawText(g, l.Detail, Theme.Police(9f), Rectangle.FromLTRB(droite - detail, (int)r.Top, droite, (int)r.Bottom),
                l.Refuse ? Theme.Avertissement : Theme.TexteSecondaireClair,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// En-tête du panneau : pictogramme de la catégorie dans un carré teinté, « Scanné à 07:42 », la question,
    /// puis « Détendeur adulte · A07 » (code en couleur, comme la maquette).
    /// </summary>
    private sealed class EnTeteScan : ControleDessine
    {
        private string _icone = Icones.Autre;
        private string _heure = "";
        private string _question = "";
        private string _libelle = "";
        private string _code = "";
        private string? _note;

        public EnTeteScan() => AccessibleRole = AccessibleRole.StaticText;

        public void Afficher(string icone, string heure, string question, string libelle, string code, string? note)
        {
            (_icone, _heure, _question, _libelle, _code, _note) = (icone, heure, question, libelle, code, note);
            AccessibleName = $"{heure}. {question} {libelle} {code}{(note is null ? "" : $", {note}")}";
            Invalidate();
        }

        protected override void Peindre(Graphics g)
        {
            var parent = Dessin.CouleurParent(this);
            var cote = E(64);
            var carre = new RectangleF(0.5f, (Height - cote) / 2, cote, cote);
            using (var chemin = Dessin.Arrondi(carre, E(16)))
            using (var fond = new SolidBrush(Theme.Sur(parent, Theme.Accent, 0.13f)))
            using (var bord = new Pen(Theme.Sur(parent, Theme.Accent, 0.22f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            Icones.Dessiner(g, _icone, RectangleF.Inflate(carre, -cote * 0.27f, -cote * 0.27f), Theme.AccentClair);

            var x = (int)(carre.Right + E(18));
            var largeur = Width - x;
            var haut = (int)((Height - E(72)) / 2);
            Dessin.Texte(g, _heure, Theme.PoliceTitre(9.75f), new Rectangle(x, haut, largeur, (int)E(20)), Theme.Retour);
            Dessin.Texte(g, _question, Theme.PoliceGrasse(17f), new Rectangle(x, haut + (int)E(17), largeur, (int)E(34)), Theme.Texte);

            const TextFormatFlags Ligne = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            var y = new Rectangle(x, haut + (int)E(51), largeur, (int)E(22));
            var debut = $"{_libelle} · ";
            var police = Theme.Police(10.5f);
            var l1 = TextRenderer.MeasureText(g, debut, police, Size.Empty, Ligne).Width;
            TextRenderer.DrawText(g, debut, police, y, Theme.TexteSecondaireClair, Ligne | TextFormatFlags.EndEllipsis);
            var code = Theme.PoliceCode(10f);
            var xCode = x + Math.Min(l1, largeur);
            TextRenderer.DrawText(g, _code, code, Rectangle.FromLTRB(xCode, y.Top, x + largeur, y.Bottom), Theme.Accent, Ligne);
            if (_note is not null)
            {
                var xNote = xCode + TextRenderer.MeasureText(g, _code, code, Size.Empty, Ligne).Width;
                TextRenderer.DrawText(g, $" · {_note}", police, Rectangle.FromLTRB(xNote, y.Top, x + largeur, y.Bottom), Theme.Avertissement, Ligne | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Chevron à droite du champ de recherche : ouvre ou ferme la liste.</summary>
    private sealed class Chevron : ControleDessine
    {
        private bool _ouvert;

        public Chevron()
        {
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "Afficher la liste des adhérents";
            Size = new Size(30, 30);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Ouvert
        {
            get => _ouvert;
            set
            {
                if (_ouvert != value)
                {
                    _ouvert = value;
                    AccessibleName = value ? "Fermer la liste des adhérents" : "Afficher la liste des adhérents";
                    Invalidate();
                }
            }
        }

        public override Size GetPreferredSize(Size proposedSize) => new((int)E(30), (int)E(30));

        protected override void Peindre(Graphics g)
        {
            var t = E(18);
            var r = new RectangleF((Width - t) / 2, (Height - t) / 2, t, t);
            if (_ouvert)
            {
                g.TranslateTransform(Width / 2f, Height / 2f);
                g.RotateTransform(180);
                g.TranslateTransform(-Width / 2f, -Height / 2f);
            }

            Icones.Dessiner(g, Icones.ChevronBas, r, Theme.AccentClair, 2f);
            g.ResetTransform();
        }
    }

    /// <summary>« Le scan suivant est bloqué tant que cet équipement n'a pas d'adhérent. » (cadenas, bandeau jaune).</summary>
    private sealed class BandeauBloque : ControleDessine
    {
        private const string Texte = "Le scan suivant est bloqué tant que cet équipement n'a pas d'adhérent.";

        public BandeauBloque()
        {
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = Texte;
        }

        protected override void Peindre(Graphics g)
        {
            var parent = Dessin.CouleurParent(this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var fond = new SolidBrush(Theme.Sur(parent, Theme.Avertissement, 0.07f)))
            using (var bord = new Pen(Theme.Sur(parent, Theme.Avertissement, 0.3f), E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var t = E(18);
            Icones.Dessiner(g, Icones.Cadenas, new RectangleF(E(18), (Height - t) / 2, t, t), Theme.Avertissement, 2f);
            var x = (int)(E(18) + t + E(12));
            TextRenderer.DrawText(g, Texte, Theme.Police(9.75f), new Rectangle(x, 0, Width - x - (int)E(12), Height), Theme.Texte,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Raccourci vers un adhérent récent : pastille ronde de sa couleur, nom, « Dernier scan · 3 équip. ». Clic, Entrée ou Espace.</summary>
    private sealed class TuileAdherent : Control
    {
        private readonly long _id;
        private readonly string _detail;
        private readonly bool _enAvant;
        private readonly bool _refuse;
        private bool _survol;

        public TuileAdherent(long id, string nom, string detail, bool enAvant, bool refuse)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _id = id;
            Text = nom;
            _detail = detail;
            _enAvant = enAvant;
            _refuse = refuse;
            TabStop = true;
            Margin = Padding.Empty;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = $"{nom}, {detail}";
        }

        private float E(float v) => Dessin.Echelle(this, v);

        /// <summary>Largeur d'après le nom et le détail (pastille et marges comprises), au moins 200 px logiques.</summary>
        public int LargeurIdeale()
        {
            var texte = Math.Max(TextRenderer.MeasureText(Text, Theme.PoliceTitre(10.5f)).Width, TextRenderer.MeasureText(_detail, Theme.Police(9f)).Width);
            return Math.Max((int)E(200), texte + (int)E(84));
        }

        protected override void OnMouseEnter(EventArgs e) { _survol = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { _survol = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = e.SuppressKeyPress = true;
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            var parent = Dessin.CouleurParent(this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            var fond = _enAvant
                ? Theme.Sur(parent, Theme.Accent, _survol ? 0.19f : 0.13f)
                : Theme.Sur(parent, Theme.Fond, _survol ? 0.25f : 0.5f);
            var bord = Focused ? Theme.Accent
                : _enAvant ? Theme.Sur(parent, Theme.Accent, 0.6f)
                : Theme.Sur(parent, Theme.Texte, _survol ? 0.22f : 0.12f);
            using (var chemin = Dessin.Arrondi(r, E(12)))
            using (var pinceau = new SolidBrush(fond))
            using (var stylo = new Pen(bord, Focused ? E(2) : E(1.2f)))
            {
                g.FillPath(pinceau, chemin);
                g.DrawPath(stylo, chemin);
            }

            var d = E(40);
            var cercle = new RectangleF(E(12), (Height - d) / 2, d, d);
            PeindreAvatar(g, cercle, Libelles.Initiales(Text), _id, fond, _refuse, Theme.PoliceGrasse(9.75f));

            var x = (int)(cercle.Right + E(12));
            var milieu = Height / 2;
            Dessin.Texte(g, Text, Theme.PoliceTitre(10.5f), new Rectangle(x, milieu - (int)E(21), Width - x - (int)E(10), (int)E(22)), _refuse ? Theme.TexteSecondaire : Theme.Texte);
            Dessin.Texte(g, _detail, Theme.Police(9f), new Rectangle(x, milieu + (int)E(1), Width - x - (int)E(10), (int)E(18)),
                _refuse ? Theme.Avertissement : Theme.TexteSecondaire);
        }
    }
}
