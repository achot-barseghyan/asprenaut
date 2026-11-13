using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Données affichées par une ligne de <see cref="ListeMateriel"/>.</summary>
public sealed record ElementMateriel(
    long EquipementId,
    string Code,
    string Libelle,
    string? Categorie = null,
    string? Detail = null,
    string? Alerte = null,
    Color? CouleurAlerte = null,
    long? ManuelId = null,
    string? Adherent = null,
    bool EnAttente = false);

public enum StyleListe
{
    /// <summary>Matériel de la sortie au départ : pictogramme de catégorie, code en turquoise.</summary>
    Depart,

    /// <summary>Matériel encore dehors : point orange.</summary>
    ARendre,

    /// <summary>Matériel rentré : coche verte, fond vert sombre.</summary>
    Rendu,

    /// <summary>
    /// Un adhérent et ses équipements (départ d'une sortie nominative, « Par adhérent ») : pastille aux initiales,
    /// nom (<see cref="ElementMateriel.Libelle"/>), codes (<see cref="ElementMateriel.Code"/>).
    /// </summary>
    Personne,
}

/// <summary>Liste verticale défilante de lignes de matériel, avec bouton optionnel par ligne (pictogramme seul).</summary>
public sealed class ListeMateriel : FlowLayoutPanel
{
    private readonly StyleListe _style;
    private readonly VideListe _vide;

    public ListeMateriel(StyleListe style, string texteVide, string? iconeVide = null, Color? couleurVide = null)
    {
        _style = style;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        // Défilement vertical seulement : une ligne un instant plus large (mise à l'échelle) faisait apparaître une barre
        // horizontale, qui volait de la hauteur et ajoutait une barre verticale inutile (fiche d'un moniteur).
        HorizontalScroll.Maximum = 0;
        AutoScroll = false;
        VerticalScroll.Visible = false;
        AutoScroll = true;
        BackColor = Theme.Surface;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        _vide = new VideListe(texteVide, iconeVide, couleurVide ?? Theme.TexteSecondaire) { Margin = Padding.Empty };
        Controls.Add(_vide);
        Natif.ScrollbarsDuTheme(this);
    }

    /// <summary>Change le message affiché quand la liste est vide (par exemple quand un filtre est actif).</summary>
    public void DefinirVide(string texte, string? icone = null, Color? couleur = null) =>
        _vide.Definir(texte, icone, couleur ?? Theme.TexteSecondaire);

    /// <summary>Nombre de lignes affichées.</summary>
    public int Nombre => Controls.Count - 1;

    /// <summary>
    /// Remplace le contenu. <paramref name="action"/> null = pas de bouton. <paramref name="ouvrirManuel"/> : bouton « Manuel »
    /// sur les lignes dont l'équipement a un manuel (<see cref="ElementMateriel.ManuelId"/>).
    /// </summary>
    public void Afficher(IEnumerable<ElementMateriel> elements, string? libelleAction = null, string? iconeAction = null, Action<ElementMateriel>? action = null, Action<ElementMateriel>? ouvrirManuel = null)
    {
        SuspendLayout();
        while (Controls.Count > 1)
        {
            var c = Controls[1];
            Controls.RemoveAt(1);
            c.Dispose();
        }

        foreach (var element in elements)
        {
            var ligne = new LigneMateriel(_style, element, libelleAction, iconeAction, action, ouvrirManuel);
            Controls.Add(ligne);
            ligne.Width = LargeurLigne(); // Après l'ajout, qui met la ligne à l'échelle de l'écran.
        }

        _vide.Visible = Controls.Count == 1;
        _vide.Size = new Size(LargeurLigne(), (int)Dessin.Echelle(this, 120));
        ResumeLayout(true);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        if (e.Control is { } enfant)
        {
            Dessin.MettreALEchelleSiAjoutTardif(this, enfant);
        }

        base.OnControlAdded(e);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        var largeur = LargeurLigne();
        foreach (Control c in Controls)
        {
            c.Width = largeur;
        }
    }

    private int LargeurLigne() => Math.Max(120, ClientSize.Width - Padding.Horizontal - 1);

    /// <summary>État vide : pictogramme facultatif et texte centrés.</summary>
    private sealed class VideListe : ControleDessine
    {
        private string? _icone;
        private Color _couleur;

        public VideListe(string texte, string? icone, Color couleur) => Definir(texte, icone, couleur);

        public void Definir(string texte, string? icone, Color couleur)
        {
            Text = texte;
            AccessibleName = texte;
            _icone = icone;
            _couleur = couleur;
            Invalidate();
        }

        protected override void Peindre(Graphics g)
        {
            var police = _icone is null ? Theme.Police(10.5f) : Theme.PoliceTitre(11.25f);
            if (_icone is null)
            {
                TextRenderer.DrawText(g, Text, police, new Rectangle(4, (int)E(8), Width - 8, Height), Theme.TexteSecondaire,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            var t = E(40);
            var y = (Height - t - E(8) - police.Height) / 2;
            Icones.Dessiner(g, _icone, new RectangleF((Width - t) / 2, y, t, t), _couleur);
            Dessin.Texte(g, Text, police, new Rectangle(0, (int)(y + t + E(8)), Width, police.Height + 4), _couleur, centre: true);
        }
    }

    /// <summary>Une ligne : dessinée d'un bloc, avec un éventuel bouton à droite.</summary>
    private sealed class LigneMateriel : ControleDessine
    {
        private readonly StyleListe _style;
        private readonly ElementMateriel _element;
        private readonly BoutonArrondi? _bouton;
        private readonly BoutonArrondi? _manuel;

        public LigneMateriel(StyleListe style, ElementMateriel element, string? libelleAction, string? iconeAction, Action<ElementMateriel>? action, Action<ElementMateriel>? ouvrirManuel)
        {
            _style = style;
            _element = element;
            // Couleur de la ligne : sert aussi aux coins du bouton posé dessus.
            BackColor = style == StyleListe.Rendu
                ? Theme.Sur(Theme.Surface, Theme.FondRetour, 0.6f)
                : Theme.Sur(Theme.Surface, Theme.Fond, 0.4f);
            Margin = new Padding(0, 0, 0, 6);
            Height = element.Alerte is not null && style == StyleListe.Depart ? 70 : 52;
            AccessibleRole = AccessibleRole.ListItem;
            AccessibleName = $"{element.Code} {element.Libelle}" + (element.Alerte is null ? "" : $", {element.Alerte}")
                + (element.EnAttente ? ", à attribuer" : element.Adherent is { } adherent ? $", pris par {adherent}" : "");
            if (element.EnAttente)
            {
                BackColor = Theme.Sur(Theme.Surface, Theme.Avertissement, 0.08f);
            }

            if (libelleAction is not null && action is not null)
            {
                _bouton = new BoutonArrondi($"{libelleAction} {element.Code}", StyleBouton.Icone, iconeAction ?? Icones.Croix)
                {
                    Margin = Padding.Empty,
                };
                _bouton.Click += (_, _) => action(element);
                Controls.Add(_bouton);
            }

            // Manuel de l'équipement (même catégorie, même marque) : à gauche du bouton d'action.
            if (element.ManuelId is not null && ouvrirManuel is not null)
            {
                _manuel = new BoutonArrondi($"Manuel de {element.Code}", StyleBouton.Icone, Icones.Manuels)
                {
                    Margin = Padding.Empty,
                    CouleurIcone = Theme.AccentClair,
                    AccessibleDescription = "Ouvre le manuel du fabricant",
                };
                _manuel.Click += (_, _) => ouvrirManuel(element);
                Controls.Add(_manuel);
            }
        }

        /// <summary>Bord gauche du premier bouton de la ligne (le texte s'arrête avant).</summary>
        private int DebutBoutons => _manuel?.Left ?? _bouton?.Left ?? Width;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var marge = (int)E(4);
            var droite = Width - marge;
            foreach (var bouton in new[] { _bouton, _manuel })
            {
                if (bouton is not null)
                {
                    bouton.Location = new Point(droite - bouton.Width, (Height - bouton.Height) / 2);
                    droite = bouton.Left - marge;
                }
            }
        }

        protected override void Peindre(Graphics g)
        {
            var fond = BackColor;
            using (var chemin = Dessin.Arrondi(new RectangleF(0, 0, Width - 1, Height - 1), E(14)))
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
                if (_element.EnAttente)
                {
                    // Équipement qui attend son adhérent : cadre jaune en tirets (maquette).
                    using var tirets = new Pen(Theme.Sur(fond, Theme.Avertissement, 0.6f), E(1)) { DashPattern = [4f, 3f] };
                    g.DrawPath(tirets, chemin);
                }
            }

            var x = E(10);
            var haut = E(52) / 2;
            switch (_style)
            {
                case StyleListe.Personne:
                    {
                        var d = E(34);
                        var cercle = new RectangleF(x, haut - (d / 2), d, d);
                        using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Accent, 0.16f)))
                        using (var bord = new Pen(Theme.Sur(fond, Theme.Accent, 0.45f), E(1)))
                        {
                            g.FillEllipse(pinceau, cercle);
                            g.DrawEllipse(bord, cercle);
                        }

                        Dessin.Texte(g, Core.Modeles.Libelles.Initiales(_element.Libelle), Theme.PoliceGrasse(9f), Rectangle.Round(cercle), Theme.AccentClair, centre: true);
                        x = cercle.Right + E(12);
                        break;
                    }

                case StyleListe.Depart:
                    {
                        var cote = E(34);
                        var boite = new RectangleF(x, haut - (cote / 2), cote, cote);
                        using (var chemin = Dessin.Arrondi(boite, E(10)))
                        using (var pinceau = new SolidBrush(Theme.FondAccent))
                        {
                            g.FillPath(pinceau, chemin);
                        }

                        Icones.Dessiner(g, Icones.PourCategorie(_element.Categorie), RectangleF.Inflate(boite, -E(8), -E(8)), Theme.AccentClair);
                        x = boite.Right + E(12);
                        break;
                    }

                case StyleListe.ARendre:
                    {
                        var d = E(8);
                        using (var halo = new SolidBrush(Theme.Alpha(Theme.Depart, 0.25f)))
                        {
                            g.FillEllipse(halo, x - E(2), haut - (d / 2) - E(2), d + E(4), d + E(4));
                        }

                        using (var point = new SolidBrush(Theme.Depart))
                        {
                            g.FillEllipse(point, x, haut - (d / 2), d, d);
                        }

                        x += d + E(12);
                        break;
                    }

                default:
                    {
                        var d = E(22);
                        var cercle = new RectangleF(x, haut - (d / 2), d, d);
                        using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Retour, 0.18f)))
                        {
                            g.FillEllipse(pinceau, cercle);
                        }

                        Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(cercle, -E(5), -E(5)), Theme.Retour, 3f);
                        x = cercle.Right + E(12);
                        break;
                    }
            }

            var droite = _bouton is null && _manuel is null ? Width - (int)E(12) : DebutBoutons - (int)E(6);
            var pastille = _style != StyleListe.Depart && _element.Alerte is { } alerteADroite ? alerteADroite : null;
            if (pastille is not null)
            {
                // Statut à droite ("Manquant") sous forme de pastille.
                var police = Theme.PoliceTitre(8.5f);
                var largeur = TextRenderer.MeasureText(pastille, police).Width + (int)E(16);
                var r = new RectangleF(droite - largeur, haut - E(13), largeur, E(26));
                var couleur = _element.CouleurAlerte ?? Theme.Erreur;
                using (var chemin = Dessin.Arrondi(r, r.Height / 2))
                using (var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.16f)))
                {
                    g.FillPath(pinceau, chemin);
                }

                Dessin.Texte(g, pastille, police, Rectangle.Round(r), couleur, centre: true);
                droite = (int)(r.Left - E(8));
            }

            var nom = _style == StyleListe.Rendu ? Theme.Police(10.5f) : Theme.PoliceTitre(10.5f);
            var code = Theme.PoliceCode(9f);
            var hNom = TextRenderer.MeasureText(g, "Ag", nom, Size.Empty, TextFormatFlags.NoPadding).Height;
            var hCode = TextRenderer.MeasureText(g, "Ag", code, Size.Empty, TextFormatFlags.NoPadding).Height;
            var y = (int)(haut - ((hNom + hCode + E(2)) / 2));
            var gauche = (int)x;
            Dessin.Texte(g, _element.Libelle, nom, new Rectangle(gauche, y, droite - gauche, hNom), _style == StyleListe.Rendu ? Theme.TexteDoux : Theme.Texte);
            var ligneCode = _element.Detail is null ? _element.Code : $"{_element.Code} · {_element.Detail}";
            var yCode = y + hNom + (int)E(2);
            var couleurCode = _style is StyleListe.Depart or StyleListe.Personne ? Theme.Accent : Theme.TexteSecondaire;
            if (!_element.EnAttente && _element.Adherent is null)
            {
                Dessin.Texte(g, ligneCode, code, new Rectangle(gauche, yCode, droite - gauche, hCode), couleurCode);
            }
            else
            {
                // Sortie nominative : le code, puis l'adhérent qui l'a pris, ou l'étiquette « À attribuer ».
                // Mesure avec la marge de TextRenderer (celle du dessin), sinon le code est coupé par « … ».
                var largeurCode = Math.Min(droite - gauche, TextRenderer.MeasureText(g, ligneCode, code).Width);
                Dessin.Texte(g, ligneCode, code, new Rectangle(gauche, yCode, largeurCode, hCode), couleurCode);
                var xNom = gauche + largeurCode + (int)E(6);
                if (_element.EnAttente)
                {
                    var police = Theme.PoliceGrasse(7.5f);
                    var texte = "À attribuer";
                    var r = new RectangleF(xNom, yCode + ((hCode - E(18)) / 2), TextRenderer.MeasureText(g, texte, police).Width + E(8), E(18));
                    using (var chemin = Dessin.Arrondi(r, r.Height / 2))
                    using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Avertissement, 0.18f)))
                    {
                        g.FillPath(pinceau, chemin);
                    }

                    Dessin.Texte(g, texte, police, Rectangle.Round(r), Theme.Avertissement, centre: true);
                }
                else if (xNom < droite)
                {
                    Dessin.Texte(g, _element.Adherent!, Theme.PoliceTitre(8.5f), new Rectangle(xNom, yCode - (int)E(1), droite - xNom, hCode + (int)E(2)), Theme.TexteDoux);
                }
            }

            if (_style == StyleListe.Depart && _element.Alerte is { } alerte)
            {
                var police = Theme.PoliceTitre(9f);
                var couleur = _element.CouleurAlerte ?? Theme.Avertissement;
                var t = E(14);
                var ya = E(52);
                Icones.Dessiner(g, Icones.Exclamation, new RectangleF(gauche - E(2), ya, t, t), couleur, 2.6f);
                Dessin.Texte(g, alerte, police, new Rectangle(gauche + (int)E(14), (int)ya - 2, droite - gauche - (int)E(14), (int)t + 4), couleur);
            }
        }
    }
}
