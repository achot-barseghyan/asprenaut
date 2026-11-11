using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.Core.Modeles;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Fiche « Vie d'un équipement » : en-tête, trois tuiles (sorties de la saison, où il est, prochaine échéance)
/// et frise de son parcours. Partagée par l'historique et la fiche de suivi de l'inventaire.
/// </summary>
public static class FicheVie
{
    /// <summary>Contrôles de la fiche, dans l'ordre : nom, précisions, tuiles, frise.</summary>
    /// <param name="avecCode">Ajoute le code en tête des précisions (fenêtre de suivi ; inutile quand le champ de scan l'affiche).</param>
    public static IEnumerable<Control> Construire(VieEquipement vie, bool avecCode = false)
    {
        var e = vie.Equipement;
        var nom = Ui.Libelle(e.Libelle.Length > 0 ? e.Libelle : e.Code, Theme.PoliceGrasse(15f));
        nom.AutoSize = false;
        nom.AutoEllipsis = true;
        nom.Height = 34;
        nom.Margin = Padding.Empty;
        yield return nom;

        var precisions = new List<string>();
        if (avecCode)
        {
            precisions.Add(e.Code);
        }

        if (vie.Categorie.Length > 0)
        {
            precisions.Add(vie.Categorie);
        }

        if (!string.IsNullOrWhiteSpace(e.Taille))
        {
            precisions.Add($"taille {e.Taille}");
        }

        precisions.Add($"ajouté le {e.CreeLe.ToLocalTime().ToString("dd/MM/yyyy", Libelles.Culture)}");
        var sous = Ui.Libelle(string.Join(" · ", precisions), Theme.Police(9.75f), Theme.TexteSecondaireClair);
        sous.AutoSize = false;
        sous.AutoEllipsis = true;
        sous.Height = 24;
        sous.Margin = new Padding(0, 0, 0, 14);
        yield return sous;

        yield return new TuilesVie(vie) { Height = 76, Margin = new Padding(0, 0, 0, 20) };

        var frise = new FriseVie(vie.Etapes) { Margin = Padding.Empty };
        frise.Height = FriseVie.HauteurPour(vie.Etapes.Count);
        yield return frise;
    }

    /// <summary>Couleur d'un type d'événement (pastilles du journal et de la frise).</summary>
    public static Color Couleur(TypeEvenement type) => type switch
    {
        TypeEvenement.Depart => Theme.Depart,
        TypeEvenement.Retour => Theme.Retour,
        TypeEvenement.Sortie => Theme.AccentClair,
        TypeEvenement.Revision => Theme.Avertissement,
        TypeEvenement.Materiel => Theme.Materiel,
        TypeEvenement.Manquant => Theme.Erreur,
        _ => Theme.TexteSecondaireClair,
    };

    /// <summary>Couleur d'un statut d'inventaire (disponible vert, dehors orange, manquant rouge...).</summary>
    public static Color CouleurStatut(StatutInventaire statut) => statut switch
    {
        StatutInventaire.Disponible => Theme.Retour,
        StatutInventaire.Dehors => Theme.DepartClair,
        StatutInventaire.Manquant => Theme.Erreur,
        StatutInventaire.HorsService => Theme.Avertissement,
        _ => Theme.TexteSecondaireClair,
    };

    /// <summary>Pictogramme d'un type d'événement.</summary>
    public static string Icone(TypeEvenement type) => type switch
    {
        TypeEvenement.Depart => Icones.Depart,
        TypeEvenement.Retour => Icones.Retour,
        TypeEvenement.Sortie => Icones.Sorties,
        TypeEvenement.Revision => Icones.Revisions,
        TypeEvenement.Materiel => Icones.Plus,
        TypeEvenement.Manquant => Icones.Alerte,
        _ => Icones.Info,
    };

    /// <summary>Trois encadrés : sorties de la saison, où est l'équipement, prochaine révision.</summary>
    private sealed class TuilesVie : ControleDessine
    {
        private readonly VieEquipement _vie;

        public TuilesVie(VieEquipement vie)
        {
            _vie = vie;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = $"{Libelles.Pluriel(vie.SortiesSaison, "sortie")} cette saison. {vie.LibelleStatut}, {vie.PrecisionStatut}. Prochaine révision : {vie.Echeance}.";
        }

        protected override void Peindre(Graphics g)
        {
            var ecart = E(8);
            var l = (Width - (2 * ecart)) / 3;
            var fond = Theme.Sur(Theme.Surface, Theme.Fond, 0.45f);

            Encadre(g, new RectangleF(0, 0, l, Height - 1), _vie.SortiesSaison.ToString(Libelles.Culture), Theme.PoliceGrasse(15f),
                "sorties cette saison", fond, Theme.Texte, Theme.TexteSecondaire);

            var statut = CouleurStatut(_vie.Statut);
            Encadre(g, new RectangleF(l + ecart, 0, l, Height - 1), _vie.LibelleStatut, Theme.PoliceTitre(10.5f),
                _vie.PrecisionStatut, Theme.Teinte(statut, 0.12f), statut, statut);

            var (fondRevision, couleurRevision) = _vie.EtatEcheance switch
            {
                EtatRevision.Depassee => (Theme.Teinte(Theme.Erreur, 0.12f), Theme.Erreur),
                EtatRevision.Proche => (Theme.Teinte(Theme.Avertissement, 0.12f), Theme.Avertissement),
                _ => (fond, Theme.Texte),
            };
            Encadre(g, new RectangleF(2 * (l + ecart), 0, l, Height - 1), _vie.Echeance, Theme.PoliceTitre(10.5f),
                _vie.EtatEcheance == EtatRevision.Depassee ? "révision dépassée" : "prochaine révision",
                fondRevision, couleurRevision, _vie.EtatEcheance == EtatRevision.AJour ? Theme.TexteSecondaire : couleurRevision);
        }

        private void Encadre(Graphics g, RectangleF r, string valeur, Font police, string legende, Color fond, Color couleurValeur, Color couleurLegende)
        {
            using (var chemin = Dessin.Arrondi(r, E(14)))
            using (var pinceau = new SolidBrush(fond))
            {
                g.FillPath(pinceau, chemin);
            }

            var x = (int)(r.X + E(12));
            var largeur = (int)(r.Width - E(20));
            Dessin.Texte(g, valeur, police, new Rectangle(x, (int)(r.Y + E(8)), largeur, (int)E(28)), couleurValeur);
            TextRenderer.DrawText(g, legende, Theme.Police(8.25f), new Rectangle(x, (int)(r.Y + E(38)), largeur, (int)(r.Height - E(40))), couleurLegende,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Frise verticale du parcours : pastille colorée, date, ce qui s'est passé, où et avec qui.</summary>
    private sealed class FriseVie : ControleDessine
    {
        private const int HauteurEtape = 68;
        private const int MargeBas = 14;
        private readonly IReadOnlyList<EtapeVie> _etapes;

        public FriseVie(IReadOnlyList<EtapeVie> etapes)
        {
            _etapes = etapes;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Parcours de l'équipement";
            AccessibleDescription = string.Join(". ", etapes.Select(e => $"{Date(e)}, {e.Titre}, {e.Detail}"));
        }

        public static int HauteurPour(int nombre) => Math.Max(1, nombre) * HauteurEtape + MargeBas;

        private static string Date(EtapeVie e) => e.Moment.ToString(e.DateSeule ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", Libelles.Culture);

        protected override void Peindre(Graphics g)
        {
            var h = E(HauteurEtape);
            var cx = E(11);
            var rHalo = E(11);
            var rPoint = E(7);
            var xTexte = (int)E(34);
            var largeur = Width - xTexte;
            for (var i = 0; i < _etapes.Count; i++)
            {
                var etape = _etapes[i];
                var y = i * h;
                var cy = y + E(11);
                var couleur = Couleur(etape.Type);

                // Trait vers l'étape suivante, puis pastille entourée d'un halo de sa couleur.
                if (i < _etapes.Count - 1)
                {
                    using var trait = new Pen(Theme.Sur(Theme.Surface, Theme.AccentClair, 0.16f), E(2));
                    g.DrawLine(trait, cx, cy + rHalo + E(4), cx, cy + h - rHalo - E(4));
                }

                using (var pinceau = new SolidBrush(Theme.Teinte(couleur, 0.20f)))
                {
                    g.FillEllipse(pinceau, cx - rHalo, cy - rHalo, 2 * rHalo, 2 * rHalo);
                }

                using (var pinceau = new SolidBrush(couleur))
                {
                    g.FillEllipse(pinceau, cx - rPoint, cy - rPoint, 2 * rPoint, 2 * rPoint);
                }

                Dessin.Texte(g, Date(etape), Theme.Police(9f), new Rectangle(xTexte, (int)y, largeur, (int)E(18)), Theme.TexteSecondaire);
                Dessin.Texte(g, etape.Titre, Theme.PoliceTitre(10.5f), new Rectangle(xTexte, (int)(y + E(19)), largeur, (int)E(24)), Theme.Texte);
                Dessin.Texte(g, etape.Detail, Theme.Police(9f), new Rectangle(xTexte, (int)(y + E(43)), largeur, (int)E(18)), Theme.TexteSecondaireClair);
            }
        }
    }
}
