using System.Drawing.Drawing2D;
using Asprenaut.Materiel.App.Apparence;
using Asprenaut.Materiel.App.Controles;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Services;

namespace Asprenaut.Materiel.App.Ecrans;

/// <summary>
/// Carte d'un modèle de sortie (écran Sorties, maquette) : liseré à gauche et pictogramme de la couleur du type, nom, puis
/// le contenu propre à la carte. Le liseré suit l'arrondi de la carte.
/// </summary>
internal abstract class CarteModeleBase : Carte
{
    protected CarteModeleBase(FicheModele fiche)
    {
        Fiche = fiche;
        Couleur = fiche.Type is { } t ? Theme.CouleurTypeSortie(t) : Theme.TexteSecondaire;
        Rayon = 20;
        AccessibleName = fiche.Modele.Nom;
    }

    protected FicheModele Fiche { get; }

    protected Color Couleur { get; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        Dessin.Qualite(e.Graphics);
        using var forme = Dessin.Arrondi(new RectangleF(0, 0, Width - 1, Height - 1), Dessin.Echelle(this, Rayon));
        var etat = e.Graphics.Save();
        e.Graphics.SetClip(forme);
        using (var pinceau = new SolidBrush(Couleur))
        {
            e.Graphics.FillRectangle(pinceau, 0, 0, Dessin.Echelle(this, 4), Height);
        }

        e.Graphics.Restore(etat);
    }

    /// <summary>Pictogramme du type, nom du modèle et ligne de détail, en tête de carte.</summary>
    protected TableLayoutPanel Tete(string detail)
    {
        var picto = new Picto(Fiche.Type is { } t ? Icones.PourTypeSortie(t) : Icones.Autre, 42, Couleur, Theme.Teinte(Couleur, 0.14f));
        var nom = Ui.Libelle(Fiche.Modele.Nom, Theme.PoliceTitre(12f));
        nom.AutoSize = false;
        nom.AutoEllipsis = true;
        nom.Height = 26;
        nom.Dock = DockStyle.Fill;
        var sous = Ui.Libelle(detail, Theme.Police(9f), Theme.TexteSecondaire);
        sous.AutoSize = false;
        sous.AutoEllipsis = true;
        sous.Height = 20;
        sous.Dock = DockStyle.Fill;
        var textes = Ui.Colonne((nom, SizeType.Absolute, 26), (sous, SizeType.Absolute, 20));
        textes.Margin = new Padding(12, 0, 0, 0);
        var tete = Ui.Colonnes((picto, SizeType.Absolute, 42), (textes, SizeType.Percent, 100));
        picto.Dock = DockStyle.None;
        picto.Anchor = AnchorStyles.Left;
        return tete;
    }

    /// <summary>« Piscine · Niveau 1 · Piscine du Grand Chalon ».</summary>
    protected string Resume() =>
        string.Join(" · ", new[] { Fiche.Type?.Nom, Fiche.Responsable?.NomComplet, Fiche.Modele.Lieu.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>« Aujourd'hui », « Prochain : lun. 9 nov. », « Pas de jour fixe ».</summary>
    protected string Quand() =>
        Fiche.Modele.Jour is null ? "Pas de jour fixe"
        : Fiche.Aujourdhui ? "Aujourd'hui"
        : $"Prochain : {Fiche.Prochaine.ToString("ddd d MMM", Libelles.Culture)}";
}

/// <summary>Carte de « Démarrage rapide » (onglet En cours) : « Créer la sortie » et « Ajuster » (formulaire prérempli).</summary>
internal sealed class CarteRapide : CarteModeleBase
{
    public CarteRapide(FicheModele fiche)
        : base(fiche)
    {
        Padding = new Padding(20, 14, 14, 14);
        if (fiche.Aujourdhui)
        {
            CouleurBordure = Couleur; // Le modèle du jour ressort.
        }

        var tete = Tete(Resume());
        var quand = Ui.Libelle(fiche.Probleme is { } p ? $"À compléter : {p}" : Quand(), Theme.PoliceTitre(9f), fiche.Probleme is null ? Theme.TexteSecondaireClair : Theme.Erreur);
        quand.AutoSize = false;
        quand.AutoEllipsis = true;
        quand.Dock = DockStyle.Fill;
        quand.TextAlign = ContentAlignment.MiddleLeft;
        var badge = new Pastille("AUJOURD'HUI", Theme.Teinte(Couleur, 0.18f), Couleur, police: Theme.PoliceGrasse(7f), hauteur: 20) { Visible = fiche.Aujourdhui };
        var ligneQuand = Ui.Colonnes((quand, SizeType.Percent, 100), (badge, SizeType.AutoSize, 0));
        badge.Dock = DockStyle.None;
        badge.Anchor = AnchorStyles.Right;

        var creer = new BoutonArrondi("Créer la sortie", StyleBouton.Principal, Icones.Fleche, iconeApres: true) { AutoSize = false, Margin = Padding.Empty };
        creer.AccessibleName = $"Créer la sortie « {fiche.Intitule} »";
        creer.Click += (_, _) => Creer?.Invoke(this, EventArgs.Empty);
        var ajuster = new BoutonArrondi("Ajuster", StyleBouton.Secondaire) { Margin = new Padding(8, 0, 0, 0) };
        ajuster.AccessibleDescription = "Ouvre le formulaire de nouvelle sortie prérempli, pour changer un détail";
        ajuster.Click += (_, _) => Ajuster?.Invoke(this, EventArgs.Empty);
        var bas = Ui.Colonnes((creer, SizeType.Percent, 100), (ajuster, SizeType.AutoSize, 0));

        Controls.Add(Ui.Colonne((tete, SizeType.Absolute, 48), (ligneQuand, SizeType.Absolute, 30), (bas, SizeType.Percent, 100)));
        bas.Margin = new Padding(0, 8, 0, 0);
    }

    public event EventHandler? Creer;

    public event EventHandler? Ajuster;
}

/// <summary>Carte de l'onglet Modèles : jour, nom et intitulé, détail (type, responsable, lieu, compteur, notes), actions.</summary>
internal sealed class CarteModele : CarteModeleBase
{
    public CarteModele(FicheModele fiche)
        : base(fiche)
    {
        Padding = new Padding(22, 16, 16, 14);
        var m = fiche.Modele;
        var jour = m.Jour is { } j
            ? ServiceModelesSortie.NomJour(j) + (fiche.Aujourdhui ? " · aujourd'hui" : "")
            : "Sans jour fixe";
        var pastille = new Pastille(
            jour.ToUpper(Libelles.Culture),
            fiche.Aujourdhui ? Theme.Accent : Theme.Teinte(Theme.AccentClair, 0.12f),
            fiche.Aujourdhui ? Theme.TexteSurAccent : Theme.TexteSecondaireClair,
            police: Theme.PoliceGrasse(7f),
            hauteur: 24);
        var lignePastille = Ui.Rangee(true, pastille);
        lignePastille.Margin = new Padding(0, 0, 0, 10);

        var tete = Tete(fiche.Intitule);

        // Détail : libellé à gauche, valeur à droite (manque en rouge).
        var detail = new TableLayoutPanel { ColumnCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 10, 0, 0), Dock = DockStyle.Fill };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Ligne(string libelle, string valeur, Color? couleur = null)
        {
            var l = Ui.Libelle(libelle, Theme.Police(9.75f), Theme.TexteSecondaire);
            var v = Ui.Libelle(valeur, Theme.PoliceTitre(9.75f), couleur ?? Theme.Texte);
            foreach (var c in new[] { l, v })
            {
                c.AutoSize = false;
                c.AutoEllipsis = true;
                c.Dock = DockStyle.Fill;
                c.Margin = Padding.Empty;
                c.TextAlign = ContentAlignment.MiddleLeft;
            }

            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            detail.Controls.Add(l, 0, detail.RowCount);
            detail.Controls.Add(v, 1, detail.RowCount);
            detail.RowCount++;
        }

        Ligne("Type", fiche.Type is { } t ? t.Actif ? t.Nom : $"{t.Nom} (masqué)" : "À choisir", fiche.Type is { Actif: true } ? null : Theme.Erreur);
        Ligne(
            "Responsable",
            fiche.Responsable is { } r ? $"{(r.EstClasse ? "Classe" : "Enseignant")} · {r.NomComplet}{(r.Actif ? "" : r.EstClasse ? " (inactive)" : " (inactif)")}" : "À choisir",
            fiche.Responsable is { Actif: true } ? null : Theme.Erreur);
        Ligne("Lieu", m.Lieu.Trim().Length > 0 ? m.Lieu.Trim() : "Non précisé", m.Lieu.Trim().Length > 0 ? null : Theme.TexteSecondaire);
        var compteur = Libelles.Pluriel(fiche.Utilisations, "utilisation") + (fiche.UtilisationsModifiees ? " · modifié" : "");
        if (fiche.Type?.Nominatif == true)
        {
            compteur += m.UnParCategorie ? " · 1 par catégorie" : " · règle coupée";
        }

        Ligne("Compteur", compteur);
        Ligne("Notes", m.Notes.Trim().Length > 0 ? m.Notes.Trim().ReplaceLineEndings(" ") : "—", Theme.TexteSecondaireClair);
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // La place en trop va en bas, pas dans la dernière ligne.
        detail.RowCount++;

        // Actions : « Créer » prend la place qui reste (jamais coupé), « Modifier » garde son texte, dupliquer et supprimer
        // sont des pictogrammes avec bulle. Toutes tiennent dans la largeur minimale de la carte (GrilleAdaptative.LargeurMin).
        var creer = new BoutonArrondi("Créer", StyleBouton.Principal, Icones.Fleche, iconeApres: true) { AutoSize = false, Margin = new Padding(0, 0, 8, 0) };
        creer.AccessibleName = $"Créer la sortie « {fiche.Intitule} »";
        creer.Click += (_, _) => Creer?.Invoke(this, EventArgs.Empty);
        var modifier = new BoutonArrondi("Modifier", StyleBouton.Secondaire, Icones.Crayon) { Margin = new Padding(0, 0, 8, 0) };
        modifier.AccessibleName = $"Modifier le modèle {m.Nom}";
        modifier.Click += (_, _) => Modifier?.Invoke(this, EventArgs.Empty);
        var dupliquer = new BoutonArrondi("", StyleBouton.Icone, Icones.Document) { AccessibleName = $"Dupliquer le modèle {m.Nom}", Margin = new Padding(0, 0, 4, 0) };
        dupliquer.Click += (_, _) => Dupliquer?.Invoke(this, EventArgs.Empty);
        var supprimer = new BoutonArrondi("", StyleBouton.Icone, Icones.Corbeille) { AccessibleName = $"Supprimer le modèle {m.Nom}", Margin = Padding.Empty };
        supprimer.Click += (_, _) => Supprimer?.Invoke(this, EventArgs.Empty);
        var bulle = new ToolTip();
        bulle.SetToolTip(dupliquer, "Dupliquer le modèle");
        bulle.SetToolTip(supprimer, "Supprimer le modèle");
        Disposed += (_, _) => bulle.Dispose();
        var actions = Ui.Colonnes(
            (creer, SizeType.Percent, 100),
            (modifier, SizeType.AutoSize, 0),
            (dupliquer, SizeType.AutoSize, 0),
            (supprimer, SizeType.AutoSize, 0));
        foreach (var b in new[] { modifier, dupliquer, supprimer })
        {
            b.Dock = DockStyle.None;
            b.Anchor = AnchorStyles.Left;
        }

        actions.Margin = new Padding(0, 10, 0, 0);

        Controls.Add(Ui.Colonne(
            (lignePastille, SizeType.AutoSize, 0),
            (tete, SizeType.Absolute, 48),
            (detail, SizeType.Percent, 100),
            (actions, SizeType.Absolute, Theme.HauteurBouton + 10)));
    }

    public event EventHandler? Creer;

    public event EventHandler? Modifier;

    public event EventHandler? Dupliquer;

    public event EventHandler? Supprimer;
}

/// <summary>Carte en pointillés quand il n'y a encore aucun modèle, avec « Créer un modèle ».</summary>
internal sealed class CarteSansModele : Carte
{
    public CarteSansModele(string texte, Action creer)
    {
        Padding = new Padding(22, 14, 18, 14);
        BackColor = Theme.Fond;
        CouleurBordure = Theme.BordureForte;
        var libelle = Ui.Libelle(texte, Theme.Police(10.5f), Theme.TexteSecondaireClair);
        libelle.AutoSize = false;
        libelle.Dock = DockStyle.Fill;
        libelle.TextAlign = ContentAlignment.MiddleLeft;
        var bouton = new BoutonArrondi("Créer un modèle", StyleBouton.Secondaire, Icones.Plus) { Margin = new Padding(12, 0, 0, 0) };
        bouton.Click += (_, _) => creer();
        var ligne = Ui.Colonnes((libelle, SizeType.Percent, 100), (bouton, SizeType.AutoSize, 0));
        bouton.Dock = DockStyle.None;
        bouton.Anchor = AnchorStyles.Right;
        Controls.Add(ligne);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Dessin.EffacerAvecParent(this, e.Graphics);
        Dessin.Qualite(e.Graphics);
        var ep = Dessin.Echelle(this, 1.5f);
        using var forme = Dessin.Arrondi(new RectangleF(ep / 2, ep / 2, Width - ep - 1, Height - ep - 1), Dessin.Echelle(this, 18));
        using var stylo = new Pen(CouleurBordure, ep) { DashStyle = DashStyle.Dash };
        e.Graphics.DrawPath(stylo, forme);
    }
}
