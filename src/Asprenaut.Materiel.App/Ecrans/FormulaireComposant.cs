using System.ComponentModel;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.App.Infrastructure;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Fenêtre « Ajouter un composant · A24 » (fiche de suivi d'un détendeur) : composition actuelle, composant à ajouter
/// (deuxième étage, octopus ou manomètre absent), modèle (avec les modèles déjà connus), n° de série, fabrication,
/// mise en service, origine, et l'intervention « Montage octopus » à noter dans l'historique.
/// La règle est dans <see cref="ServiceInventaire.AjouterComposantAsync"/> : la fenêtre ne se ferme qu'en cas de succès.
/// </summary>
public static class FormulaireComposant
{
    /// <summary>Ouvre la fenêtre ; vrai si un composant a été ajouté.</summary>
    public static async Task<bool> AjouterAsync(IWin32Window? proprietaire, ServiceInventaire inventaire, ServiceGestion gestion, Equipement equipement, string sousTitre)
    {
        var composition = ServiceInventaire.Composition(equipement);
        var aAjouter = composition.FirstOrDefault(c => !c.Present && ServiceInventaire.ComposantsAjoutables.Contains(c.Role));
        if (aAjouter is null)
        {
            Dialogues.Information(proprietaire, $"{equipement.Code} a déjà tous ses composants.");
            return false;
        }

        var etages = await inventaire.ModelesConnusAsync("Octopus");
        var manometres = await inventaire.ModelesConnusAsync("Manomètre");

        var etat = new CompositionActuelle(composition);
        var choix = new ChoixComposant(composition);
        var modele = new ChampSaisie();
        var numero = new ChampSaisie { TexteIndicatif = "Gravé sur le boîtier" };
        numero.Champ.Font = Theme.PoliceCode(10.5f);
        var puces = new PucesModeles();
        var fabrication = new ChampSaisie { TexteIndicatif = "MM/AAAA" };
        var service = new ChampDate { Date = DateOnly.FromDateTime(DateTime.Today), AccessibleName = "Mise en service" };
        var origine = new Bascule("Pièce neuve", "Récupérée sur un autre détendeur") { Etirer = true, SelectedIndex = 0 };
        var noter = new CaseACocher();
        modele.Champ.AccessibleName = "Modèle";
        numero.Champ.AccessibleName = "Numéro de série";
        fabrication.Champ.AccessibleName = "Date de fabrication";

        var gestionOuverte = gestion.ProtectionActive && gestion.ModeGestion;
        using var fenetre = new FenetreFormulaire($"Ajouter un composant · {equipement.Code}", "Ajouter le composant", async () =>
        {
            if (!service.Valide)
            {
                return ResultatOperation.Echec("Mise en service : écrivez la date comme 11/11/2026.");
            }

            return await inventaire.AjouterComposantAsync(equipement.Id, new NouveauComposant(
                choix.Choisi, modele.Valeur, numero.Valeur, fabrication.Valeur, service.Date,
                origine.SelectedIndex == 0 ? OrigineComposant.Neuve : OrigineComposant.Recuperee, noter.Checked));
        });
        fenetre.EnTete(sousTitre, gestionOuverte ? "Mode gestion" : null);
        fenetre.Ajouter(Ui.LibelleChamp("Composition actuelle"));
        etat.Margin = new Padding(0, 0, 0, 16);
        fenetre.Ajouter(etat, SizeType.Absolute, 62 + 16);
        fenetre.Ajouter(Ui.LibelleChamp("Composant à ajouter"));
        choix.Margin = new Padding(0, 0, 0, 16);
        fenetre.Ajouter(choix, SizeType.Absolute, 56 + 16);
        var rangeeModele = fenetre.DeuxChamps("Modèle", modele, "N° de série", numero);
        rangeeModele.Margin = new Padding(0, 0, 0, 8);
        puces.Margin = new Padding(0, 0, 0, 14);
        fenetre.Ajouter(puces, SizeType.Absolute, 30 + 14);
        fenetre.DeuxChamps("Date de fabrication", fabrication, "Mise en service", service);
        // Dans un cadre de taille fixe : la largeur voulue par la bascule (texte long) élargirait toute la fenêtre.
        var hoteOrigine = new Panel { BackColor = Color.Transparent };
        origine.Dock = DockStyle.Fill;
        hoteOrigine.Controls.Add(origine);
        fenetre.Champ("Origine", hoteOrigine, Theme.HauteurCliquable + 4);
        fenetre.Ajouter(noter, SizeType.Absolute, 56);

        void Appliquer()
        {
            var role = choix.Choisi;
            etat.Choisi = role;
            var manometre = role == "Manomètre";
            FenetreFormulaire.Libelles(rangeeModele).Gauche.Text = manometre ? "Marque" : "Modèle";
            modele.TexteIndicatif = manometre ? "Ex. Suunto" : "Ex. R095";
            var connus = manometre ? manometres : etages;
            puces.Afficher(connus);
            puces.Visible = connus.Count > 0;
            noter.Text = $"Noter « {ServiceInventaire.NatureMontage(role)} » dans l'historique";
        }

        puces.ModeleChoisi += (_, m) =>
        {
            modele.Valeur = m;
            numero.Champ.Focus();
        };
        choix.Changement += (_, _) => Appliquer();
        choix.Choisir(aAjouter.Role);
        Appliquer();
        fenetre.Shown += (_, _) => modele.Champ.Focus();
        return fenetre.ShowDialog(proprietaire) == DialogResult.OK;
    }

    // ---------------------------------------------------------------- Contrôles dessinés de la fenêtre

    /// <summary>Quatre tuiles : chaque composant du détendeur, son modèle (vert) ou « Absent » ; celui qu'on ajoute est entouré.</summary>
    private sealed class CompositionActuelle : ControleDessine
    {
        private readonly IReadOnlyList<EtatComposant> _composition;
        private string _choisi = "";

        public CompositionActuelle(IReadOnlyList<EtatComposant> composition)
        {
            _composition = composition;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = "Composition actuelle : " + string.Join(", ", composition.Select(c => $"{c.Role} {(c.Present ? c.Modele : "absent")}"));
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Choisi { get => _choisi; set => Invalider(ref _choisi, value); }

        protected override void Peindre(Graphics g)
        {
            var ecart = E(8);
            var l = (Width - 1 - (3 * ecart)) / 4;
            for (var i = 0; i < _composition.Count; i++)
            {
                var c = _composition[i];
                var r = new RectangleF(i * (l + ecart), 0.5f, l, Height - 1.5f);
                var choisi = c.Role == _choisi;
                var fond = c.Present ? Theme.Sur(Theme.Surface, Theme.Retour, 0.10f)
                    : choisi ? Theme.Sur(Theme.Surface, Theme.Accent, 0.12f)
                    : Theme.Sur(Theme.Surface, Theme.Fond, 0.5f);
                using (var chemin = Dessin.Arrondi(r, E(12)))
                using (var pinceau = new SolidBrush(fond))
                {
                    g.FillPath(pinceau, chemin);
                    var bord = c.Present ? Theme.Sur(Theme.Surface, Theme.Retour, 0.35f) : choisi ? Theme.Accent : Theme.Bordure;
                    using var stylo = new Pen(bord, E(choisi ? 1.5f : 1f));
                    if (choisi)
                    {
                        stylo.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    }

                    g.DrawPath(stylo, chemin);
                }

                var x = (int)(r.X + E(10));
                var lt = (int)(r.Width - E(16));
                Dessin.Texte(g, c.Role, Theme.Police(7.5f), new Rectangle(x, (int)E(9), lt, (int)E(18)), Theme.TexteSecondaire);
                var valeur = c.Present ? (c.Modele.Length > 0 ? c.Modele : "Présent") : "Absent";
                Dessin.Texte(g, valeur, Theme.PoliceGrasse(9.75f), new Rectangle(x, (int)E(29), lt, (int)E(22)),
                    c.Present ? Theme.Retour : choisi ? Theme.AccentClair : Theme.TexteSecondaireClair);
            }
        }
    }

    /// <summary>Choix du composant à ajouter : ceux déjà présents sont grisés (« Déjà présent »). Clic ou flèches.</summary>
    private sealed class ChoixComposant : ControleDessine
    {
        private readonly IReadOnlyList<EtatComposant> _composition;
        private int _choisi = -1;
        private int _survol = -1;

        public ChoixComposant(IReadOnlyList<EtatComposant> composition)
        {
            _composition = composition;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Composant à ajouter";
        }

        public event EventHandler? Changement;

        public string Choisi => _choisi >= 0 ? _composition[_choisi].Role : "";

        private bool Possible(int i) => i >= 0 && i < _composition.Count && !_composition[i].Present
            && ServiceInventaire.ComposantsAjoutables.Contains(_composition[i].Role);

        public void Choisir(string role)
        {
            var i = _composition.ToList().FindIndex(c => c.Role == role);
            if (Possible(i) && i != _choisi)
            {
                _choisi = i;
                AccessibleDescription = role;
                Invalidate();
                Changement?.Invoke(this, EventArgs.Empty);
            }
        }

        private float Largeur => (Width - 1 - (3 * E(8))) / 4;

        private int IndexSous(Point p) => (int)(p.X / (Largeur + E(8))) is var i and >= 0 and < 4 ? i : -1;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var i = IndexSous(e.Location);
            Cursor = Possible(i) ? Cursors.Hand : Cursors.Default;
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
            var i = IndexSous(e.Location);
            if (Possible(i))
            {
                Focus();
                Choisir(_composition[i].Role);
            }
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is not (Keys.Left or Keys.Right))
            {
                return;
            }

            var pas = e.KeyCode == Keys.Right ? 1 : -1;
            for (var i = _choisi + pas; i >= 0 && i < _composition.Count; i += pas)
            {
                if (Possible(i))
                {
                    Choisir(_composition[i].Role);
                    break;
                }
            }

            e.Handled = true;
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
            var l = Largeur;
            for (var i = 0; i < _composition.Count; i++)
            {
                var c = _composition[i];
                var possible = Possible(i);
                var choisi = i == _choisi;
                var r = new RectangleF(i * (l + E(8)), 0.75f, l, Height - 2f);
                var fond = choisi ? Theme.Sur(Theme.Surface, Theme.Accent, 0.16f)
                    : possible && i == _survol ? Theme.Sur(Theme.Surface, Theme.Fond, 0.25f)
                    : Theme.Sur(Theme.Surface, Theme.Fond, possible ? 0.5f : 0.3f);
                using (var chemin = Dessin.Arrondi(r, E(12)))
                using (var pinceau = new SolidBrush(fond))
                using (var stylo = new Pen(choisi ? Theme.Accent : possible ? Theme.BordureForte : Theme.Bordure, E(choisi ? (Focused ? 2f : 1.5f) : 1f)))
                {
                    g.FillPath(pinceau, chemin);
                    g.DrawPath(stylo, chemin);
                }

                var x = (int)(r.X + E(9));
                var lt = (int)(r.Width - E(11));
                Dessin.Texte(g, c.Role, possible ? Theme.PoliceTitre(8f) : Theme.Police(8f), new Rectangle(x, (int)E(9), lt, (int)E(22)),
                    possible ? Theme.Texte : Theme.TexteSecondaire);
                Dessin.Texte(g, possible ? "À ajouter" : "Déjà présent", Theme.Police(8.25f), new Rectangle(x, (int)E(30), lt, (int)E(18)),
                    choisi ? Theme.AccentClair : Theme.TexteSecondaire);
            }
        }
    }

    /// <summary>Modèles déjà connus dans l'inventaire, en petites puces : un clic remplit le champ « Modèle ».</summary>
    private sealed class PucesModeles : ControleDessine
    {
        private IReadOnlyList<string> _modeles = [];
        private readonly List<RectangleF> _zones = [];
        private int _survol = -1;

        public PucesModeles()
        {
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Modèles connus";
        }

        public event EventHandler<string>? ModeleChoisi;

        public void Afficher(IReadOnlyList<string> modeles)
        {
            _modeles = modeles;
            AccessibleDescription = string.Join(", ", modeles);
            Invalidate();
        }

        private int IndexSous(Point p) => _zones.FindIndex(z => z.Contains(p));

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
            if (IndexSous(e.Location) is var i and >= 0 && i < _modeles.Count)
            {
                ModeleChoisi?.Invoke(this, _modeles[i]);
            }
        }

        protected override void Peindre(Graphics g)
        {
            _zones.Clear();
            var police = Theme.PoliceCode(8.25f);
            var x = 0f;
            for (var i = 0; i < _modeles.Count; i++)
            {
                var l = TextRenderer.MeasureText(g, _modeles[i], police).Width + E(14);
                if (x + l > Width)
                {
                    break;
                }

                var r = new RectangleF(x, 0.5f, l, Height - 1.5f);
                _zones.Add(r);
                using (var chemin = Dessin.Arrondi(r, r.Height / 2))
                using (var pinceau = new SolidBrush(i == _survol ? Theme.Sur(Theme.Surface, Theme.Accent, 0.16f) : Theme.Sur(Theme.Surface, Theme.Fond, 0.5f)))
                using (var stylo = new Pen(i == _survol ? Theme.Accent : Theme.Bordure, E(1)))
                {
                    g.FillPath(pinceau, chemin);
                    g.DrawPath(stylo, chemin);
                }

                Dessin.Texte(g, _modeles[i], police, Rectangle.Round(r), Theme.Texte, centre: true);
                x += l + E(8);
            }
        }
    }

    /// <summary>Case à cocher dans un cadre arrondi : carré turquoise coché, texte à droite.</summary>
    private sealed class CaseACocher : CheckBox
    {
        public CaseACocher()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Checked = true;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Font = Theme.PoliceTitre(9.75f);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            Dessin.EffacerAvecParent(this, g);
            Dessin.Qualite(g);
            float E(float v) => Dessin.Echelle(this, v);
            var cadre = new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f);
            using (var chemin = Dessin.Arrondi(cadre, E(12)))
            using (var fond = new SolidBrush(Theme.Sur(Theme.Surface, Theme.Fond, 0.45f)))
            using (var bord = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Bordure, E(1)))
            {
                g.FillPath(fond, chemin);
                g.DrawPath(bord, chemin);
            }

            var t = E(22);
            var boite = new RectangleF(E(16), (Height - t) / 2, t, t);
            using (var chemin = Dessin.Arrondi(boite, E(6)))
            {
                if (Checked)
                {
                    using var plein = new SolidBrush(Theme.Accent);
                    g.FillPath(plein, chemin);
                    Icones.Dessiner(g, Icones.Coche, RectangleF.Inflate(boite, -E(4), -E(4)), Theme.TexteSurAccent, 2.6f);
                }
                else
                {
                    using var bord = new Pen(Theme.BordureForte, E(1.5f));
                    g.DrawPath(bord, chemin);
                }
            }

            var x = (int)(boite.Right + E(12));
            Dessin.Texte(g, Text, Font, new Rectangle(x, 0, Width - x - (int)E(12), Height), Theme.Texte);
        }
    }
}
