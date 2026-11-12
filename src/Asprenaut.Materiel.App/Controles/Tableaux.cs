using Asprenaut.Materiel.App.Apparence;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>Dessin de cellules particulières des tableaux (pastilles de statut, pictogramme + texte).</summary>
public static class Tableaux
{
    /// <summary>En-têtes en majuscules, comme la maquette.</summary>
    public static void AjouterColonne(DataGridView grille, string nom, string titre, float poids = 100)
    {
        var i = grille.Columns.Add(nom, titre.ToUpper(Core.Modeles.Libelles.Culture));
        grille.Columns[i].FillWeight = poids;
        grille.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    /// <summary>
    /// Fond de la cellule (sélection comprise) et son trait de séparation. <c>e.PaintBackground</c> seul ne peint pas
    /// la bordure : avec <c>e.Handled</c>, ce pixel n'était jamais repeint et gardait ce que l'écran affichait avant
    /// (traits verts ou gris entre les lignes après un défilement ou un changement d'écran).
    /// </summary>
    private static void PeindreFond(DataGridViewCellPaintingEventArgs e, bool selection)
    {
        var parties = DataGridViewPaintParts.Background | DataGridViewPaintParts.Border;
        if (selection)
        {
            parties |= DataGridViewPaintParts.SelectionBackground;
        }

        e.Paint(e.CellBounds, parties);
    }

    /// <summary>Pastille arrondie avec point coloré, alignée à gauche dans la cellule.</summary>
    public static void PeindrePastille(DataGridViewCellPaintingEventArgs e, string texte, Color couleur, Color? couleurTexte = null)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        PeindreFond(e, selection);
        var etat = g.Save(); // Le lissage ne doit pas déborder sur les cellules suivantes (voir PeindreFond).
        Dessin.Qualite(g);
        var police = Theme.PoliceTitre(8.5f);
        var fond = selection ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
        var tTexte = TextRenderer.MeasureText(g, texte, police, Size.Empty, TextFormatFlags.NoPadding);
        var hauteur = 28f * g.DpiY / 96f;
        var point = 7f * g.DpiY / 96f;
        var largeur = tTexte.Width + point + (32f * g.DpiX / 96f);
        var r = new RectangleF(e.CellBounds.Left + e.CellStyle.Padding.Left, e.CellBounds.Top + ((e.CellBounds.Height - hauteur) / 2), largeur, hauteur);
        using (var chemin = Dessin.Arrondi(r, hauteur / 2))
        using (var pinceau = new SolidBrush(Theme.Sur(fond, couleur, 0.14f)))
        {
            g.FillPath(pinceau, chemin);
        }

        var x = r.Left + (12f * g.DpiX / 96f);
        using (var pinceauPoint = new SolidBrush(couleur))
        {
            g.FillEllipse(pinceauPoint, x, r.Top + ((hauteur - point) / 2), point, point);
        }

        x += point + (7f * g.DpiX / 96f);
        Dessin.Texte(g, texte, police, Rectangle.Round(new RectangleF(x, r.Top, r.Right - x, hauteur)), couleurTexte ?? couleur);
        g.Restore(etat);
        e.Handled = true;
    }

    /// <summary>
    /// Boutons dessinés dans la cellule d'actions (« Suivi », « Modifier »), de gauche à droite, alignés à droite.
    /// Le clic est géré par la grille via <see cref="BoutonSous"/> ; la cellule entière est assez haute
    /// (lignes de 60 px) pour un doigt ou une souris peu précise.
    /// </summary>
    public static void PeindreBoutons(DataGridViewCellPaintingEventArgs e, params string[] textes) =>
        PeindreBoutons(e, textes.Select(t => (t, (Color?)null)).ToArray());

    /// <summary>Comme <see cref="PeindreBoutons(DataGridViewCellPaintingEventArgs, string[])"/>, avec une couleur par bouton (null = turquoise).</summary>
    public static void PeindreBoutons(DataGridViewCellPaintingEventArgs e, params (string Texte, Color? Couleur)[] boutons)
    {
        if (e.Graphics is not { } g)
        {
            return;
        }

        PeindreFond(e, (e.State & DataGridViewElementStates.Selected) != 0);
        var etat = g.Save(); // Le lissage ne doit pas déborder sur les cellules suivantes (voir PeindreFond).
        Dessin.Qualite(g);
        var zones = ZonesBoutons(e.CellBounds, g.DpiX / 96f, boutons.Length);
        for (var i = 0; i < boutons.Length; i++)
        {
            var r = zones[i];
            var couleur = boutons[i].Couleur ?? Theme.Accent;
            using (var chemin = Dessin.Arrondi(r, 12f * g.DpiX / 96f))
            using (var fond = new SolidBrush(Theme.Teinte(couleur, 0.12f)))
            using (var bord = new Pen(Theme.Teinte(couleur, boutons[i].Couleur is null ? 0.30f : 0.45f)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            Dessin.Texte(g, boutons[i].Texte, Theme.PoliceTitre(9f), Rectangle.Round(r), boutons[i].Couleur ?? Theme.AccentClair, centre: true);
        }

        g.Restore(etat);
        e.Handled = true;
    }

    /// <summary>
    /// Zones des boutons d'une cellule d'actions, de gauche à droite. Elles se partagent la largeur disponible
    /// (100 px logiques au plus) : sur une fenêtre étroite, les libellés sont tronqués plutôt que débordants.
    /// </summary>
    public static RectangleF[] ZonesBoutons(Rectangle cellule, float echelle, int nombre)
    {
        const float hauteur = 40f;
        const float largeurMax = 100f;
        const float ecart = 6f;
        const float marge = 6f;
        var disponible = cellule.Width - (2 * marge * echelle);
        var ecartPx = ecart * echelle;
        var largeur = Math.Min(largeurMax * echelle, (disponible - ((nombre - 1) * ecartPx)) / Math.Max(1, nombre));
        var total = (nombre * largeur) + ((nombre - 1) * ecartPx);
        var gauche = cellule.Right - (marge * echelle) - total;
        var haut = cellule.Top + ((cellule.Height - (hauteur * echelle)) / 2f);
        var zones = new RectangleF[nombre];
        for (var i = 0; i < nombre; i++)
        {
            zones[i] = new RectangleF(gauche + (i * (largeur + ecartPx)), haut, largeur, hauteur * echelle);
        }

        return zones;
    }

    // ---------------------------------------------------------------- Boutons pictogrammes (colonne Actions de l'inventaire)

    /// <summary>
    /// Bouton carré de la colonne Actions : pictogramme, bulle d'aide, couleur (null = neutre), pictogramme rempli,
    /// et cadenas jaune au coin si l'action demande le code gestionnaire.
    /// </summary>
    public sealed record BoutonIcone(string Icone, string Bulle, Color? Couleur = null, bool Plein = false, bool Verrou = false);

    private const float CoteBoutonIcone = 38f;
    private const float EcartBoutonIcone = 6f;
    private const float HauteurPastilleAction = 20f;
    private const float EcartPastilleAction = 5f;

    /// <summary>Zones des boutons carrés, alignés à droite ; sous la pastille du problème s'il y en a une.</summary>
    public static RectangleF[] ZonesBoutonsIcones(Rectangle cellule, float echelle, int nombre, bool avecPastille)
    {
        var cote = CoteBoutonIcone * echelle;
        var ecart = EcartBoutonIcone * echelle;
        var bloc = cote + (avecPastille ? (HauteurPastilleAction + EcartPastilleAction) * echelle : 0);
        var haut = cellule.Top + ((cellule.Height - bloc) / 2f) + (bloc - cote);
        var gauche = cellule.Right - (12f * echelle) - (nombre * cote) - ((nombre - 1) * ecart);
        var zones = new RectangleF[nombre];
        for (var i = 0; i < nombre; i++)
        {
            zones[i] = new RectangleF(gauche + (i * (cote + ecart)), haut, cote, cote);
        }

        return zones;
    }

    /// <summary>Indice du bouton carré sous la souris (position relative à la cellule), ou -1.</summary>
    public static int BoutonIconeSous(Rectangle cellule, float echelle, int nombre, bool avecPastille, Point position)
    {
        var zones = ZonesBoutonsIcones(cellule, echelle, nombre, avecPastille);
        var absolu = new PointF(cellule.Left + position.X, cellule.Top + position.Y);
        for (var i = 0; i < zones.Length; i++)
        {
            if (RectangleF.Inflate(zones[i], 3 * echelle, 3 * echelle).Contains(absolu))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Colonne Actions (maquette « Inventaire ») : la pastille du problème ouvert (« À surveiller · Embout abîmé »)
    /// alignée à droite, puis les boutons carrés.
    /// </summary>
    public static void PeindreBoutonsIcones(DataGridViewCellPaintingEventArgs e, IReadOnlyList<BoutonIcone> boutons, (string Texte, Color Couleur)? pastille = null)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        PeindreFond(e, selection);
        var etat = g.Save(); // Le lissage ne doit pas déborder sur les cellules suivantes (voir PeindreFond).
        Dessin.Qualite(g);
        var s = g.DpiX / 96f;
        var fond = selection ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
        var zones = ZonesBoutonsIcones(e.CellBounds, s, boutons.Count, pastille is not null);

        if (pastille is { } p)
        {
            var police = Theme.PoliceGrasse(7.5f);
            var droite = zones[^1].Right;
            var largeur = Math.Min(TextRenderer.MeasureText(g, p.Texte, police).Width + (10f * s), droite - e.CellBounds.Left - (8f * s));
            var r = new RectangleF(droite - largeur, zones[0].Top - ((HauteurPastilleAction + EcartPastilleAction) * s), largeur, HauteurPastilleAction * s);
            using (var chemin = Dessin.Arrondi(r, r.Height / 2))
            using (var pinceau = new SolidBrush(Theme.Sur(fond, p.Couleur, 0.16f)))
            {
                g.FillPath(pinceau, chemin);
            }

            Dessin.Texte(g, p.Texte, police, Rectangle.Round(r), p.Couleur, centre: true);
        }

        for (var i = 0; i < boutons.Count; i++)
        {
            var b = boutons[i];
            var r = zones[i];
            var couleurFond = b.Couleur is { } c ? Theme.Sur(fond, c, 0.14f) : Theme.Sur(fond, Theme.Fond, 0.5f);
            var couleurBord = b.Couleur ?? Theme.Sur(fond, Theme.AccentClair, 0.24f);
            using (var chemin = Dessin.Arrondi(r, 11f * s))
            using (var pinceau = new SolidBrush(couleurFond))
            using (var stylo = new Pen(couleurBord, s))
            {
                g.FillPath(pinceau, chemin);
                g.DrawPath(stylo, chemin);
            }

            var icone = 17f * s;
            Icones.Dessiner(g, b.Icone, new RectangleF(r.X + ((r.Width - icone) / 2), r.Y + ((r.Height - icone) / 2), icone, icone),
                b.Couleur ?? Theme.TexteDoux, 1.9f, b.Plein);

            if (b.Verrou)
            {
                // Cadenas jaune au coin haut droit, détouré de la couleur de la ligne (comme PastilleVerrou).
                var d = 18f * s;
                var rv = new RectangleF(r.Right - (d * 2 / 3), r.Top - (d / 3), d, d);
                using (var detour = new SolidBrush(fond))
                {
                    g.FillEllipse(detour, RectangleF.Inflate(rv, 2 * s, 2 * s));
                }

                using (var jaune = new SolidBrush(Theme.Avertissement))
                {
                    g.FillEllipse(jaune, rv);
                }

                Icones.Dessiner(g, Icones.Cadenas, RectangleF.Inflate(rv, -4.5f * s, -4.5f * s), Theme.TexteSurAvertissement, 3f);
            }
        }

        g.Restore(etat);
        e.Handled = true;
    }

    /// <summary>Indice du bouton sous le clic (position relative à la cellule), ou -1.</summary>
    public static int BoutonSous(Rectangle cellule, float echelle, int nombre, Point position)
    {
        var zones = ZonesBoutons(cellule, echelle, nombre);
        var absolu = new PointF(cellule.Left + position.X, cellule.Top + position.Y);
        for (var i = 0; i < zones.Length; i++)
        {
            if (zones[i].Contains(absolu))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Zone du titre dans une cellule « pictogramme + deux lignes » (sa ligne du haut) : sert au dessin et à savoir
    /// si un clic tombe sur le nom souligné.
    /// </summary>
    public static Rectangle ZoneTitre(Graphics g, Rectangle cellule, Padding padding, string titre)
    {
        var sx = g.DpiX / 96f;
        var gauche = (int)(cellule.Left + padding.Left + (48f * sx));
        var t = TextRenderer.MeasureText(g, titre, Theme.PoliceTitre(10.5f), Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var s = TextRenderer.MeasureText(g, "Ag", Theme.Police(9f), Size.Empty, TextFormatFlags.NoPadding);
        var haut = cellule.Top + ((cellule.Height - t.Height - s.Height - (int)(2 * sx)) / 2);
        return new Rectangle(gauche, haut, Math.Min(t.Width, cellule.Right - 4 - gauche), t.Height);
    }

    /// <summary>
    /// Pictogramme dans un carré teinté, puis le nom souligné en pointillé (il ouvre la fiche) et une ligne
    /// secondaire (« Scubapro MK25 · Détendeurs »), comme la maquette.
    /// </summary>
    public static void PeindreIconeDeuxLignes(DataGridViewCellPaintingEventArgs e, string icone, string titre, string sousTitre, bool barre = false)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        PeindreFond(e, selection);
        var etat = g.Save(); // Le lissage ne doit pas déborder sur les cellules suivantes (voir PeindreFond).
        Dessin.Qualite(g);
        var sx = g.DpiX / 96f;
        var cote = 36f * sx;
        var boite = new RectangleF(e.CellBounds.Left + e.CellStyle.Padding.Left, e.CellBounds.Top + ((e.CellBounds.Height - cote) / 2), cote, cote);
        using (var chemin = Dessin.Arrondi(boite, cote * 0.3f))
        using (var pinceau = new SolidBrush(Theme.FondAccent))
        {
            g.FillPath(pinceau, chemin);
        }

        Icones.Dessiner(g, icone, RectangleF.Inflate(boite, -cote * 0.25f, -cote * 0.25f), Theme.AccentClair);
        var zone = ZoneTitre(g, e.CellBounds, e.CellStyle.Padding, titre);
        var droite = e.CellBounds.Right - 4;
        var couleurTitre = barre ? Theme.TexteSecondaire : Theme.Texte;
        TextRenderer.DrawText(g, titre, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(zone.Left, zone.Top, droite, zone.Bottom), couleurTitre,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        var y = zone.Bottom;
        if (barre)
        {
            using var trait = new Pen(couleurTitre, Math.Max(1f, sx));
            g.DrawLine(trait, zone.Left, zone.Top + (zone.Height / 2f), zone.Right, zone.Top + (zone.Height / 2f));
        }
        else
        {
            using var pointille = new Pen(Theme.Teinte(Theme.AccentClair, 0.45f), Math.Max(1f, sx)) { DashPattern = [3f, 2f] };
            g.DrawLine(pointille, zone.Left, y, zone.Right, y);
        }

        if (sousTitre.Length > 0)
        {
            var police = Theme.Police(9f);
            var h = TextRenderer.MeasureText(g, "Ag", police, Size.Empty, TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(g, sousTitre, police, Rectangle.FromLTRB(zone.Left, y + (int)(2 * sx), droite, y + (int)(2 * sx) + h), Theme.TexteSecondaire,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        g.Restore(etat);
        e.Handled = true;
    }

    /// <summary>Lien sous le compteur d'utilisation de l'inventaire (ouvre la page « Utilisations »).</summary>
    public const string LienUtilisation = "Voir les sorties";

    /// <summary>Hauteurs de la cellule « Utilisation » : haut du texte, et zone du lien (vide sans lien).</summary>
    private static (float Haut, Size Texte, RectangleF Lien) DispositionUtilisation(Graphics g, Rectangle cellule, Padding padding, string texte, bool barreVisible, bool avecLien)
    {
        var sx = g.DpiX / 96f;
        const TextFormatFlags drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var tTexte = TextRenderer.MeasureText(g, texte, Theme.PoliceTitre(9.5f), Size.Empty, drapeaux);
        var hBarre = barreVisible ? 13f * sx : 0;
        var tLien = avecLien ? TextRenderer.MeasureText(g, LienUtilisation, Theme.PoliceTitre(8.25f), Size.Empty, drapeaux) : Size.Empty;
        var icone = 13f * sx;
        var hLien = avecLien ? tLien.Height + (5f * sx) : 0;
        var haut = cellule.Top + ((cellule.Height - tTexte.Height - hBarre - hLien) / 2);
        var lien = avecLien
            ? new RectangleF(cellule.Left + padding.Left, haut + tTexte.Height + hBarre + (5f * sx), icone + (5f * sx) + tLien.Width, tLien.Height)
            : RectangleF.Empty;
        return (haut, tTexte, lien);
    }

    /// <summary>Zone du lien « Voir les sorties » d'une cellule « Utilisation » (agrandie pour le doigt), pour le clic.</summary>
    public static Rectangle ZoneLienUtilisation(Graphics g, Rectangle cellule, Padding padding, string texte, bool barreVisible)
    {
        var zone = Rectangle.Round(DispositionUtilisation(g, cellule, padding, texte, barreVisible, avecLien: true).Lien);
        zone.Inflate((int)(6 * g.DpiX / 96f), (int)(5 * g.DpiY / 96f));
        return zone;
    }

    /// <summary>
    /// Compteur d'utilisation (colonne « Utilisation ») : « 64 / 80 utilisations », pastille « SEUIL MODÈLE » ou
    /// « SEUIL ÉQUIPEMENT » si le seuil n'est pas celui de la catégorie, une barre fine (jaune à 90 %, rouge au seuil),
    /// puis le lien « Voir les sorties » si <paramref name="avecLien"/>.
    /// </summary>
    public static void PeindreUtilisation(DataGridViewCellPaintingEventArgs e, string texte, double part, Color couleur, Color couleurTexte, string? badge, bool barreVisible,
        bool avecLien = false)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        PeindreFond(e, selection);
        var etat = g.Save(); // Le lissage ne doit pas déborder sur les cellules suivantes (voir PeindreFond).
        Dessin.Qualite(g);
        var sx = g.DpiX / 96f;
        var fond = selection ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
        var police = Theme.PoliceTitre(9.5f);
        var drapeaux = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var (haut, tTexte, lien) = DispositionUtilisation(g, e.CellBounds, e.CellStyle.Padding, texte, barreVisible, avecLien);
        var hBarre = barreVisible ? 6f * sx : 0;
        var ecart = barreVisible ? 7f * sx : 0;
        var gauche = e.CellBounds.Left + e.CellStyle.Padding.Left;
        var droite = e.CellBounds.Right - (12f * sx);
        TextRenderer.DrawText(g, texte, police, new Point(gauche, (int)haut), couleurTexte, drapeaux);

        if (avecLien)
        {
            var icone = 13f * sx;
            Icones.Dessiner(g, Icones.Liste, new RectangleF(lien.Left, lien.Top + ((lien.Height - icone) / 2), icone, icone), Theme.AccentClair);
            TextRenderer.DrawText(g, LienUtilisation, Theme.PoliceTitre(8.25f), new Point((int)(lien.Left + icone + (5f * sx)), (int)lien.Top), Theme.AccentClair, drapeaux);
        }

        if (badge is not null)
        {
            var pb = Theme.PoliceGrasse(7f);
            var tb = TextRenderer.MeasureText(g, badge, pb, Size.Empty, drapeaux);
            var rb = new RectangleF(gauche + tTexte.Width + (6f * sx), haut + ((tTexte.Height - tb.Height - (4f * sx)) / 2), tb.Width + (12f * sx), tb.Height + (4f * sx));
            if (rb.Right <= e.CellBounds.Right - 2)
            {
                using (var chemin = Dessin.Arrondi(rb, rb.Height / 2))
                using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.Avertissement, 0.16f)))
                {
                    g.FillPath(pinceau, chemin);
                }

                TextRenderer.DrawText(g, badge, pb, Rectangle.Round(rb), Theme.Avertissement, drapeaux | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        if (barreVisible)
        {
            var piste = new RectangleF(gauche, haut + tTexte.Height + ecart, Math.Max(0, droite - gauche), hBarre);
            using (var chemin = Dessin.Arrondi(piste, hBarre / 2))
            using (var pinceau = new SolidBrush(Theme.Sur(fond, Theme.AccentClair, 0.14f)))
            {
                g.FillPath(pinceau, chemin);
            }

            var plein = (float)(piste.Width * Math.Clamp(part, 0, 1));
            if (plein >= 1)
            {
                using var chemin = Dessin.Arrondi(new RectangleF(piste.Left, piste.Top, Math.Max(plein, hBarre), hBarre), hBarre / 2);
                using var pinceau = new SolidBrush(couleur);
                g.FillPath(pinceau, chemin);
            }
        }

        g.Restore(etat);
        e.Handled = true;
    }

    /// <summary>Pictogramme dans un carré teinté, puis texte (première colonne de l'inventaire).</summary>
    public static void PeindreIconeTexte(DataGridViewCellPaintingEventArgs e, string icone, string texte)
    {
        if (e.Graphics is not { } g || e.CellStyle is null)
        {
            return;
        }

        var selection = (e.State & DataGridViewElementStates.Selected) != 0;
        PeindreFond(e, selection);
        var etat = g.Save(); // Le lissage ne doit pas déborder sur les cellules suivantes (voir PeindreFond).
        Dessin.Qualite(g);
        var cote = 36f * g.DpiX / 96f;
        var boite = new RectangleF(e.CellBounds.Left + e.CellStyle.Padding.Left, e.CellBounds.Top + ((e.CellBounds.Height - cote) / 2), cote, cote);
        using (var chemin = Dessin.Arrondi(boite, cote * 0.3f))
        using (var pinceau = new SolidBrush(Theme.FondAccent))
        {
            g.FillPath(pinceau, chemin);
        }

        Icones.Dessiner(g, icone, RectangleF.Inflate(boite, -cote * 0.25f, -cote * 0.25f), Theme.AccentClair);
        var gauche = (int)(boite.Right + (12f * g.DpiX / 96f));
        Dessin.Texte(g, texte, Theme.PoliceTitre(10.5f), Rectangle.FromLTRB(gauche, e.CellBounds.Top, e.CellBounds.Right - 4, e.CellBounds.Bottom), Theme.Texte);
        g.Restore(etat);
        e.Handled = true;
    }
}
