using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Microsoft.Data.Sqlite;

namespace Asprenaut.Materiel.Data.Depots;

/// <summary>
/// Lecture de toutes les tables pour l'export CSV : jointures pour remplacer les identifiants par des noms,
/// valeurs brutes (mises en forme par <see cref="Core.Services.ServiceExport"/>).
/// </summary>
public sealed class ExportRepository(FabriqueConnexion fabrique) : IExportRepository
{
    private const string Moniteur = "TRIM(m.prenom || ' ' || m.nom)";

    private static readonly (string Fichier, string Sql, ColonneExport[] Colonnes)[] Tables =
    [
        ("materiel.csv",
            """
            SELECT e.code, e.libelle, c.nom, e.marque, e.modele, e.taille, e.numero_serie, e.etat,
                   (SELECT MAX(ct.date_controle) FROM controle ct WHERE ct.equipement_id = e.id AND ct.type = 'Revision'),
                   e.date_prochaine_revision, e.date_requalification, e.montage, e.marque_deuxieme_etage, e.modele_deuxieme_etage,
                   e.numero_deuxieme_etage, e.direct_system, e.manometre, e.octopus, e.numero_octopus, e.notes, e.cree_le
            FROM equipement e JOIN categorie c ON c.id = e.categorie_id ORDER BY e.code
            """,
            [new("Code"), new("Équipement"), new("Catégorie"), new("Marque"), new("Modèle"), new("Taille"), new("N° série"),
             new("État", GenreColonne.EtatEquipement), new("Dernière révision", GenreColonne.Date),
             new("Prochaine révision / TIV", GenreColonne.Date), new("Prochaine requalification", GenreColonne.Date),
             new("Étrier / DIN", GenreColonne.Montage), new("Marque deuxième étage"), new("Deuxième étage"), new("N° deuxième étage"),
             new("D.S", GenreColonne.OuiNon), new("Mano"), new("Octopus"), new("N° octopus"), new("Notes"),
             new("Ajouté le", GenreColonne.Horodatage)]),
        ("moniteurs.csv",
            "SELECT m.genre, m.prenom, m.nom, m.abrege, m.niveau, m.telephone, m.inspecteur_tiv, m.actif FROM moniteur m ORDER BY m.genre = 'Classe', m.nom, m.prenom",
            [new("Responsable"), new("Prénom"), new("Nom"), new("Abrégé"), new("Niveau"), new("Téléphone"), new("Inspecteur TIV", GenreColonne.OuiNon), new("Actif", GenreColonne.OuiNon)]),
        ("sorties.csv",
            $"""
            SELECT s.id, s.date_sortie, COALESCE(ts.nom, s.type), s.intitule, {Moniteur}, s.lieu, s.statut,
                   (SELECT COUNT(*) FROM sortie_equipement se WHERE se.sortie_id = s.id),
                   (SELECT COUNT(*) FROM sortie_equipement se WHERE se.sortie_id = s.id AND se.statut = 'Manquant'),
                   s.depart_valide_le, s.cloturee_le, s.notes
            FROM sortie s JOIN moniteur m ON m.id = s.moniteur_id LEFT JOIN type_sortie ts ON ts.id = s.type_sortie_id
            ORDER BY s.date_sortie, s.id
            """,
            [new("N°"), new("Date", GenreColonne.Date), new("Type", GenreColonne.TypeSortie), new("Intitulé"), new("Moniteur"), new("Lieu"),
             new("Statut", GenreColonne.StatutSortie), new("Équipements"), new("Manquants"), new("Départ validé le", GenreColonne.Horodatage),
             new("Clôturée le", GenreColonne.Horodatage), new("Notes")]),
        ("materiel_des_sorties.csv",
            """
            SELECT s.id, s.date_sortie, s.intitule, e.code, e.libelle, se.statut, se.scanne_depart_le, se.scanne_retour_le
            FROM sortie_equipement se
            JOIN sortie s ON s.id = se.sortie_id
            JOIN equipement e ON e.id = se.equipement_id
            ORDER BY s.date_sortie, s.id, se.scanne_depart_le, se.id
            """,
            [new("Sortie n°"), new("Date de la sortie", GenreColonne.Date), new("Sortie"), new("Code"), new("Équipement"),
             new("Statut", GenreColonne.StatutSortieEquipement), new("Parti le", GenreColonne.Horodatage), new("Rentré le", GenreColonne.Horodatage)]),
        ("controles.csv",
            $"""
            SELECT ct.date_controle, e.code, e.libelle, ct.type, ct.resultat, {Moniteur}, ct.notes, ct.saisi_le
            FROM controle ct
            JOIN equipement e ON e.id = ct.equipement_id
            LEFT JOIN moniteur m ON m.id = ct.inspecteur_id
            ORDER BY ct.date_controle, ct.id
            """,
            [new("Date", GenreColonne.Date), new("Code"), new("Équipement"), new("Contrôle", GenreColonne.TypeControle),
             new("Résultat", GenreColonne.ResultatControle), new("Inspecteur"), new("Notes"), new("Saisi le", GenreColonne.Horodatage)]),
        ("atelier.csv",
            """
            SELECT a.envoye_le, e.code, e.libelle, a.prestataire, a.motif, a.retour_prevu, a.revenu_le
            FROM atelier a JOIN equipement e ON e.id = a.equipement_id ORDER BY a.envoye_le, a.id
            """,
            [new("Envoyé le", GenreColonne.Date), new("Code"), new("Équipement"), new("Prestataire"), new("Motif"),
             new("Retour prévu", GenreColonne.Date), new("Revenu le", GenreColonne.Date)]),
        ("seances_tiv.csv",
            "SELECT date_seance, heure, lieu FROM seance_tiv ORDER BY date_seance, id",
            [new("Date", GenreColonne.Date), new("Heure"), new("Lieu")]),
        ("journal.csv",
            """
            SELECT j.horodatage, j.action, j.details, e.code, s.intitule
            FROM journal j
            LEFT JOIN equipement e ON e.id = j.equipement_id
            LEFT JOIN sortie s ON s.id = j.sortie_id
            ORDER BY j.horodatage, j.id
            """,
            [new("Date", GenreColonne.Horodatage), new("Action"), new("Détail"), new("Code"), new("Sortie")]),
    ];

    public async Task<IReadOnlyList<TableExport>> ListerTablesAsync(CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        // Une seule transaction de lecture : les fichiers forment un instantané cohérent, même si on scanne pendant l'export.
        await using var tx = (SqliteTransaction)await cx.BeginTransactionAsync(ct);
        var resultat = new List<TableExport>();
        foreach (var (fichier, sql, colonnes) in Tables)
        {
            await using var commande = cx.CreateCommand();
            commande.Transaction = tx;
            commande.CommandText = sql;
            await using var lecteur = await commande.ExecuteReaderAsync(ct);
            var lignes = new List<string?[]>();
            while (await lecteur.ReadAsync(ct))
            {
                var ligne = new string?[lecteur.FieldCount];
                for (var i = 0; i < ligne.Length; i++)
                {
                    ligne[i] = lecteur.IsDBNull(i) ? null : Convert.ToString(lecteur.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
                }

                lignes.Add(ligne);
            }

            resultat.Add(new TableExport(fichier, colonnes, lignes));
        }

        await tx.CommitAsync(ct);
        return resultat;
    }
}
