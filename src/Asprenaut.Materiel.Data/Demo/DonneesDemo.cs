using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Data.Connexion;
using Dapper;

namespace Asprenaut.Materiel.Data.Demo;

/// <summary>
/// Données de démonstration (2 moniteurs, une quinzaine d'équipements).
/// Appelé uniquement en Debug par l'application (voir Program.cs), et par les tests.
/// Ne fait rien si la base contient déjà des moniteurs ou des équipements.
/// </summary>
public static class DonneesDemo
{
    public static async Task<bool> ChargerSiVideAsync(FabriqueConnexion fabrique, DateOnly aujourdHui, CancellationToken ct = default)
    {
        await using var cx = await fabrique.OuvrirAsync(ct);
        var existant = await cx.ExecuteScalarAsync<long>(
            "SELECT (SELECT COUNT(*) FROM moniteur WHERE genre = 'Moniteur') + (SELECT COUNT(*) FROM equipement);");
        if (existant > 0)
        {
            return false;
        }

        var categories = (await cx.QueryAsync<Categorie>("SELECT id, nom FROM categorie;"))
            .ToDictionary(c => c.Nom, c => c.Id);
        var maintenant = FormatIso.Ecrire(DateTime.UtcNow);

        await using var tx = await cx.BeginTransactionAsync(ct);

        const string sqlMoniteur = """
            INSERT INTO moniteur (nom, prenom, niveau, telephone, inspecteur_tiv, actif)
            VALUES (@nom, @prenom, @niveau, @telephone, @tiv, 1);
            """;
        await cx.ExecuteAsync(sqlMoniteur, new { nom = "Durand", prenom = "Pascal", niveau = "MF1", telephone = "06 12 34 56 78", tiv = 1 }, tx);
        await cx.ExecuteAsync(sqlMoniteur, new { nom = "Lemoine", prenom = "Sophie", niveau = "E3", telephone = "", tiv = 0 }, tx);

        // Adhérents (sorties nominatives : mer, lac).
        (string Nom, string Prenom, string Niveau, string Licence)[] adherents =
        [
            ("Martin", "Julie", "Niveau 2", "A-12-345678"), ("Bernard", "Thomas", "Niveau 3", "A-12-345679"),
            ("Dubois", "Léa", "Niveau 1", "A-12-345680"), ("Moreau", "Hugo", "Niveau 2", "A-12-345681"),
            ("Laurent", "Camille", "Niveau 1", "A-12-345682"), ("Petit", "Nicolas", "Niveau 4", "A-12-345683"),
        ];
        foreach (var a in adherents)
        {
            await cx.ExecuteAsync("INSERT INTO adherent (nom, prenom, niveau, licence, actif) VALUES (@Nom, @Prenom, @Niveau, @Licence, 1);",
                new { a.Nom, a.Prenom, a.Niveau, a.Licence }, tx); // Pas le tuple : Dapper ne voit pas les noms de ses éléments.
        }

        // (code, libellé, catégorie, taille, état, décalage de révision en jours ou null)
        (string Code, string Libelle, string Categorie, string? Taille, EtatEquipement Etat, int? Revision)[] equipements =
        [
            ("BLOC-031", "Bloc 12 L acier", "Blocs", null, EtatEquipement.EnService, 200),
            ("BLOC-032", "Bloc 12 L acier", "Blocs", null, EtatEquipement.EnService, 150),
            ("BLOC-033", "Bloc 15 L acier", "Blocs", null, EtatEquipement.EnService, -10),
            ("BLOC-034", "Bloc 6 L alu", "Blocs", null, EtatEquipement.HorsService, -60),
            ("S01", "Gilet Aqualung", "Gilets", "M", EtatEquipement.EnService, 300),
            ("S02", "Gilet Aqualung", "Gilets", "L", EtatEquipement.EnService, 300),
            ("S03", "Gilet Scubapro", "Gilets", "S", EtatEquipement.EnService, 90),
            ("A01", "Détendeur Mares", "Détendeurs", null, EtatEquipement.EnService, 120),
            ("A02", "Détendeur Mares", "Détendeurs", null, EtatEquipement.EnService, 45),
            ("E01", "Détendeur Apeks", "Détendeurs", null, EtatEquipement.Reforme, null),
            ("COMB-001", "Combinaison 5 mm", "Combinaisons", "M", EtatEquipement.EnService, null),
            ("COMB-002", "Combinaison 5 mm", "Combinaisons", "L", EtatEquipement.EnService, null),
            ("PALM-001", "Palmes chaussantes", "Palmes", "40-41", EtatEquipement.EnService, null),
            ("MASQ-001", "Masque bi-verre", "Masques", null, EtatEquipement.EnService, null),
            ("ORDI-001", "Ordinateur Suunto Zoop", "Ordinateurs", null, EtatEquipement.EnService, 365),
        ];

        const string sqlEquipement = """
            INSERT INTO equipement (code, libelle, categorie_id, taille, etat, date_prochaine_revision, notes, cree_le)
            VALUES (@code, @libelle, @categorieId, @taille, @etat, @revision, '', @creeLe);
            """;
        foreach (var e in equipements)
        {
            await cx.ExecuteAsync(sqlEquipement, new
            {
                code = e.Code,
                libelle = e.Libelle,
                categorieId = categories[e.Categorie],
                taille = e.Taille,
                etat = e.Etat.ToString(),
                revision = e.Revision is { } jours ? FormatIso.Ecrire(aujourdHui.AddDays(jours)) : null,
                creeLe = maintenant,
            }, tx);
        }

        // Révisions : requalification des blocs (jours), une TIV passée, une séance TIV dans deux semaines.
        (string Code, int Jours)[] requalifications = [("BLOC-031", 900), ("BLOC-032", 60), ("BLOC-033", 420), ("BLOC-034", 1000)];
        foreach (var (code, jours) in requalifications)
        {
            await cx.ExecuteAsync(
                "UPDATE equipement SET date_requalification = @date WHERE code = @code;",
                new { code, date = FormatIso.Ecrire(aujourdHui.AddDays(jours)) }, tx);
        }

        await cx.ExecuteAsync(
            """
            INSERT INTO controle (equipement_id, type, date_controle, resultat, inspecteur_id, notes, saisi_le)
            SELECT e.id, 'Tiv', @date, 'Conforme', (SELECT id FROM moniteur WHERE prenom = 'Pascal'), '', @maintenant
            FROM equipement e WHERE e.code = 'BLOC-031';
            """,
            new { date = FormatIso.Ecrire(aujourdHui.AddDays(200).AddMonths(-12)), maintenant }, tx);
        await cx.ExecuteAsync(
            "INSERT INTO seance_tiv (date_seance, heure, lieu) VALUES (@date, '9:00', 'Local du club');",
            new { date = FormatIso.Ecrire(aujourdHui.AddDays(14)) }, tx);

        await cx.ExecuteAsync(
            "INSERT INTO journal (horodatage, action, details) VALUES (@maintenant, 'DonneesDemo', 'Données de démonstration chargées');",
            new { maintenant }, tx);
        await cx.ExecuteAsync(
            """
            INSERT INTO journal (horodatage, action, details, equipement_id)
            SELECT cree_le, 'EquipementCree', code || ' « ' || libelle || ' » créé', id FROM equipement ORDER BY id;
            """,
            transaction: tx);
        await tx.CommitAsync(ct);
        return true;
    }
}
