using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>Équipement visé par la fenêtre « Problème sur A07 ».</summary>
public sealed record CibleSignalement(long EquipementId, string Code, string Libelle, string Categorie, long? SortieId, string Contexte);

/// <summary>
/// Fenêtre « Problème sur A07 » (maquette « Retour du matériel ») : ce qui ne va pas (choix selon la catégorie), peut-il
/// repartir (À surveiller libre, Hors service avec le code gestionnaire), précision. Ouverte au retour d'une sortie et
/// depuis l'inventaire. La règle est dans <see cref="ServiceSignalements.SignalerAsync"/> : la fenêtre ne se ferme qu'en cas de succès.
/// </summary>
public static class FormulaireSignalement
{
    /// <summary>Ouvre la fenêtre (pré-remplie si l'équipement a déjà un problème ouvert) ; le message du service, ou null si annulé.</summary>
    public static async Task<ResultatOperation?> OuvrirAsync(IWin32Window? proprietaire, ServiceSignalements service, GardeGestion garde, CibleSignalement cible)
    {
        var existant = await service.OuvertAsync(cible.EquipementId);
        var causes = ServiceSignalements.CausesPour(cible.Categorie);
        var choixCause = new GrilleCauses(causes);
        var gravite = new ChoixGravite(garde.Gestion.EstAutorise(ActionProtegee.Revisions));
        var precision = new ChampSaisie { TexteIndicatif = "Ex. fuite au raccord du flexible MP" };
        precision.Champ.MaxLength = ServiceSignalements.LongueurMaxPrecision;
        precision.Champ.AccessibleName = "Précision (facultatif)";

        if (existant is not null)
        {
            choixCause.Choisir(existant.Cause);
            gravite.Choisie = existant.Gravite;
            precision.Valeur = existant.Precision;
        }

        ResultatOperation? resultat = null;
        using var fenetre = new FenetreFormulaire($"Problème sur {cible.Code}", existant is null ? "Signaler le problème" : "Enregistrer", async () =>
        {
            resultat = await service.SignalerAsync(new NouveauSignalement(cible.EquipementId, cible.SortieId, choixCause.Choisie ?? "", gravite.Choisie, precision.Valeur));
            return resultat;
        });
        fenetre.EnTete(string.Join(" · ", new[] { cible.Libelle, cible.Contexte }.Where(t => !string.IsNullOrWhiteSpace(t))),
            existant is null ? null : "Déjà signalé");

        var adaptes = Ui.Libelle($"Choix adaptés : {cible.Categorie.ToLower(Libelles.Culture)}", Theme.Police(9f), Theme.AccentClair);
        adaptes.Margin = new Padding(0, 0, 0, 14);
        adaptes.Visible = cible.Categorie.Length > 0;
        fenetre.Ajouter(adaptes);
        fenetre.Ajouter(Ui.LibelleChamp("Qu'est-ce qui ne va pas ?"));
        choixCause.Margin = new Padding(0, 0, 0, 18);
        fenetre.Ajouter(choixCause, SizeType.Absolute, choixCause.HauteurVoulue + 18);
        fenetre.Ajouter(Ui.LibelleChamp("Peut-il repartir ?"));
        gravite.Margin = new Padding(0, 0, 0, 18);
        fenetre.Ajouter(gravite, SizeType.Absolute, ChoixGravite.Hauteur + 18);
        fenetre.Champ("Précision (facultatif)", precision);
        var note = Ui.Libelle($"Envoyé dans Révisions et noté dans la fiche EPI de {cible.Code}.", Theme.Police(9f), Theme.TexteSecondaireClair);
        note.Margin = new Padding(0, 0, 0, 4);
        fenetre.Ajouter(note);

        // « Hors service » verrouillé : le pavé du code gestionnaire, puis le choix est accepté.
        gravite.DemandeDeverrouillage += (_, _) =>
        {
            if (garde.Exiger(fenetre, ActionProtegee.Revisions, "Mettre hors service"))
            {
                gravite.Deverrouille = true;
                gravite.Choisie = GraviteSignalement.HorsService;
            }
        };

        fenetre.Shown += (_, _) => choixCause.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK ? resultat : null;
    }

    // ---------------------------------------------------------------- Contrôles dessinés de la fenêtre

    /// <summary>Tuiles des causes, trois par rangée : pictogramme et libellé ; la choisie est jaune. Clic ou flèches.</summary>
    private sealed class GrilleCauses : ControleDessine
    {
        private const int Colonnes = 3;
        private const int HauteurTuile = 88;
        private const int Ecart = 10;
        private readonly IReadOnlyList<string> _causes;
        private int _choisie = -1;
        private int _survol = -1;

        public GrilleCauses(IReadOnlyList<string> causes)
        {
            _causes = causes;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Qu'est-ce qui ne va pas ?";
        }

        private int Rangees => (_causes.Count + Colonnes - 1) / Colonnes;

        /// <summary>Hauteur en pixels logiques pour toutes les rangées.</summary>
        public int HauteurVoulue => (Rangees * HauteurTuile) + ((Rangees - 1) * Ecart);

        public string? Choisie => _choisie >= 0 ? _causes[_choisie] : null;

        public void Choisir(string cause)
        {
            var i = _causes.ToList().FindIndex(c => string.Equals(c, cause, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                i = _causes.Count - 1; // Cause d'une autre catégorie (catégorie changée depuis) : « Autre ».
            }

            _choisie = i;
            AccessibleDescription = _causes[i];
            Invalidate();
        }

        private RectangleF Zone(int i)
        {
            var l = (Width - 1 - ((Colonnes - 1) * E(Ecart))) / Colonnes;
            var h = E(HauteurTuile);
            return new RectangleF((i % Colonnes) * (l + E(Ecart)), (i / Colonnes) * (h + E(Ecart)) + 0.5f, l, h - 1);
        }

        private int IndexSous(Point p)
        {
            for (var i = 0; i < _causes.Count; i++)
            {
                if (Zone(i).Contains(p))
                {
                    return i;
                }
            }

            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var i = IndexSous(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i != _survol)
            {
                _survol = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _survol = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (IndexSous(e.Location) is var i and >= 0)
            {
                Focus();
                Choisir(_causes[i]);
            }
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var pas = e.KeyCode switch
            {
                Keys.Left => -1,
                Keys.Right => 1,
                Keys.Up => -Colonnes,
                Keys.Down => Colonnes,
                _ => 0,
            };
            if (pas != 0)
            {
                Choisir(_causes[Math.Clamp((_choisie < 0 ? 0 : _choisie + pas), 0, _causes.Count - 1)]);
                e.Handled = true;
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
            for (var i = 0; i < _causes.Count; i++)
            {
                var r = Zone(i);
                var choisie = i == _choisie;
                var fond = choisie ? Theme.Avertissement
                    : i == _survol ? Theme.Sur(Theme.Surface, Theme.Avertissement, 0.08f)
                    : Theme.Sur(Theme.Surface, Theme.Fond, 0.5f);
                using (var chemin = Dessin.Arrondi(r, E(16)))
                using (var pinceau = new SolidBrush(fond))
                using (var stylo = new Pen(choisie && Focused ? Theme.Texte : Theme.Sur(Theme.Surface, Theme.Avertissement, 0.3f), E(choisie && Focused ? 2f : 1f)))
                {
                    g.FillPath(pinceau, chemin);
                    g.DrawPath(stylo, chemin);
                }

                var couleur = choisie ? Theme.TexteSurAvertissement : Theme.Texte;
                var t = E(24);
                Icones.Dessiner(g, Icones.PourCause(_causes[i]), new RectangleF(r.X + ((r.Width - t) / 2), r.Y + E(12), t, t),
                    choisie ? Theme.TexteSurAvertissement : Theme.Avertissement, 1.9f);
                var texte = Rectangle.Round(new RectangleF(r.X + E(6), r.Y + E(42), r.Width - E(12), r.Height - E(46)));
                TextRenderer.DrawText(g, _causes[i], Theme.PoliceTitre(9.5f), texte, couleur,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// « À surveiller » / « Hors service ». Hors service demande le code gestionnaire : tuile en pointillés avec la pastille
    /// au cadenas tant que le mode gestion n'est pas ouvert ; un clic lève <see cref="DemandeDeverrouillage"/>.
    /// </summary>
    private sealed class ChoixGravite : ControleDessine
    {
        public const int Hauteur = 74 + Debord;
        private const int Debord = 14;
        private const int Ecart = 10;
        private GraviteSignalement _choisie = GraviteSignalement.ASurveiller;
        private bool _deverrouille;
        private int _survol = -1;

        public ChoixGravite(bool deverrouille)
        {
            _deverrouille = deverrouille;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Peut-il repartir ?";
        }

        public event EventHandler? DemandeDeverrouillage;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GraviteSignalement Choisie
        {
            get => _choisie;
            set
            {
                _choisie = value;
                AccessibleDescription = ServiceSignalements.Libelle(value);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Deverrouille { get => _deverrouille; set => Invalider(ref _deverrouille, value); }

        // Les tuiles laissent en haut la place de la pastille du cadenas (elle déborde de la tuile, pas du contrôle).
        private RectangleF Zone(int i)
        {
            var l = (Width - 1 - E(Ecart)) / 2;
            return new RectangleF(i * (l + E(Ecart)), E(Debord) + 0.5f, l, Height - E(Debord) - 1.5f);
        }

        private int IndexSous(Point p) => Zone(0).Contains(p) ? 0 : Zone(1).Contains(p) ? 1 : -1;

        private void Choisir(int i)
        {
            if (i == 1 && !_deverrouille && _choisie != GraviteSignalement.HorsService)
            {
                DemandeDeverrouillage?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (i >= 0)
            {
                Choisie = i == 0 ? GraviteSignalement.ASurveiller : GraviteSignalement.HorsService;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var i = IndexSous(e.Location);
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i != _survol)
            {
                _survol = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _survol = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Focus();
            Choisir(IndexSous(e.Location));
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                Choisir(e.KeyCode == Keys.Left ? 0 : 1);
                e.Handled = true;
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
            (string Titre, string Aide, Color Couleur)[] tuiles =
            [
                ("À surveiller", "Peut encore sortir, contrôle à prévoir", Theme.Avertissement),
                ("Hors service", _deverrouille ? "Bloqué pour les sorties jusqu'à réparation" : "Code gestionnaire requis", Theme.Erreur),
            ];
            for (var i = 0; i < 2; i++)
            {
                var r = Zone(i);
                var choisie = (i == 0) == (_choisie == GraviteSignalement.ASurveiller);
                var verrou = i == 1 && !_deverrouille && !choisie;
                var (titre, aide, couleur) = tuiles[i];
                var fond = choisie ? Theme.Sur(Theme.Surface, couleur, 0.14f)
                    : i == _survol ? Theme.Sur(Theme.Surface, Theme.Fond, 0.25f)
                    : Theme.Sur(Theme.Surface, Theme.Fond, verrou ? 0.3f : 0.5f);
                using (var chemin = Dessin.Arrondi(r, E(16)))
                using (var pinceau = new SolidBrush(fond))
                using (var stylo = new Pen(choisie ? couleur : verrou ? Theme.Sur(Theme.Surface, Theme.Avertissement, 0.5f) : Theme.Bordure, E(choisie ? 2f : 1.5f)))
                {
                    if (verrou)
                    {
                        stylo.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    }

                    g.FillPath(pinceau, chemin);
                    g.DrawPath(stylo, chemin);
                    if (choisie && Focused)
                    {
                        using var focus = new Pen(Theme.Accent, E(1.5f));
                        using var interieur = Dessin.Arrondi(RectangleF.Inflate(r, -E(4), -E(4)), E(12));
                        g.DrawPath(focus, interieur);
                    }
                }

                var x = (int)(r.X + E(16));
                var l = (int)(r.Width - E(28));
                var couleurTexte = choisie ? couleur : verrou ? Theme.TexteSecondaire : Theme.Texte;
                Dessin.Texte(g, titre, Theme.PoliceGrasse(10.5f), new Rectangle(x, (int)(r.Y + E(14)), l, (int)E(24)), couleurTexte);
                TextRenderer.DrawText(g, aide, Theme.Police(8.5f), new Rectangle(x, (int)(r.Y + E(40)), l, (int)(r.Height - E(42))),
                    choisie ? couleur : Theme.TexteSecondaireClair,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                if (verrou)
                {
                    // Pastille jaune au cadenas, à cheval sur le coin haut droit (comme PastilleVerrou).
                    var d = E(24);
                    var centre = new PointF(r.Right - E(16), r.Top);
                    using (var lisere = new SolidBrush(Theme.Surface))
                    {
                        g.FillEllipse(lisere, centre.X - (d / 2) - E(3), centre.Y - (d / 2) - E(3), d + E(6), d + E(6));
                    }

                    using (var jaune = new SolidBrush(Theme.Avertissement))
                    {
                        g.FillEllipse(jaune, centre.X - (d / 2), centre.Y - (d / 2), d, d);
                    }

                    var c = E(13);
                    Icones.Dessiner(g, Icones.Cadenas, new RectangleF(centre.X - (c / 2), centre.Y - (c / 2), c, c), Theme.TexteSurAvertissement, 2.6f);
                }
            }
        }
    }
}
