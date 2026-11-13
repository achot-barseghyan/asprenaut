using System.Security.Cryptography;
using Asprenaut.Materiel.Core.Abstractions;
using Asprenaut.Materiel.Core.Modeles;
using Asprenaut.Materiel.Core.Resultats;

namespace Asprenaut.Materiel.Core.Services;

/// <summary>Actions qui demandent le mode gestion (code gestionnaire). Scanner, créer une sortie et consulter restent libres.</summary>
public enum ActionProtegee
{
    /// <summary>Ajouter, modifier, supprimer du matériel.</summary>
    Materiel,

    /// <summary>Ajouter ou modifier un moniteur.</summary>
    Moniteurs,

    SupprimerSortie,

    /// <summary>Clôturer en laissant du matériel manquant (seulement si le réglage le demande).</summary>
    CloturerAvecManquants,

    /// <summary>Contrôles, atelier, séances TIV.</summary>
    Revisions,

    Parametres,

    RestaurerSauvegarde,

    /// <summary>Changer les utilisations comptées d'une sortie (autre valeur que celle de son type).</summary>
    CompteurSortie,

    /// <summary>Sortie clôturée : scanner un manquant qui revient, ou le marquer rendu à la main.</summary>
    RendreManquant,
}

/// <summary>
/// Mode consultation / mode gestion. Sans code gestionnaire, rien n'est verrouillé. Avec un code, l'application démarre
/// en consultation ; le code ouvre le mode gestion, qui se referme seul après <see cref="Reglages.MinutesDeverrouillage"/>
/// minutes sans activité. Le code est conservé haché (PBKDF2-SHA256 salé) ; le changer demande l'ancien.
/// Après <see cref="EssaisAvantBlocage"/> codes faux d'affilée, la saisie est bloquée <see cref="DureeBlocage"/>.
/// </summary>
public sealed class ServiceGestion
{
    public const int LongueurMin = 4;
    public const int LongueurMax = 6;
    public const int EssaisAvantBlocage = 5;
    public static readonly TimeSpan DureeBlocage = TimeSpan.FromMinutes(1);
    public const string MessageVerrouille = "Action protégée : entrez le code gestionnaire.";

    private const int Iterations = 100_000;
    private const string Format = "pbkdf2-sha256";

    private readonly Reglages _reglages;
    private readonly IStockageReglages _stockage;
    private readonly IHorloge _horloge;
    private bool _deverrouille;
    private DateTime _derniereActivite;
    private int _echecs;
    private DateTime? _bloqueJusqua;

    public ServiceGestion(Reglages reglages, IStockageReglages stockage, IHorloge horloge)
    {
        _reglages = reglages;
        _stockage = stockage;
        _horloge = horloge;

        // Mode gestion laissé ouvert à la fermeture, et le club a choisi de ne pas reverrouiller : on le reprend.
        if (ProtectionActive && reglages.DeverrouilleAuDemarrage && !reglages.VerrouillerALaFermeture)
        {
            _deverrouille = true;
            _derniereActivite = horloge.MaintenantUtc;
        }
    }

    /// <summary>Le mode a changé (déverrouillage, verrouillage manuel ou automatique, code défini ou retiré).</summary>
    public event EventHandler? EtatChange;

    /// <summary>Un code gestionnaire est défini : les actions protégées le demandent.</summary>
    public bool ProtectionActive => !string.IsNullOrEmpty(_reglages.CodeGestionnaire);

    /// <summary>Mode gestion ouvert (toujours vrai sans code).</summary>
    public bool ModeGestion
    {
        get
        {
            Verifier();
            return !ProtectionActive || _deverrouille;
        }
    }

    private TimeSpan Duree => TimeSpan.FromMinutes(Minutes);

    /// <summary>Minutes sans activité avant le retour en consultation.</summary>
    public int Minutes => _reglages.MinutesDeverrouillage > 0 ? _reglages.MinutesDeverrouillage : 15;

    /// <summary>Temps avant le verrouillage automatique ; null si l'application est en consultation ou sans code.</summary>
    public TimeSpan? TempsRestant
    {
        get
        {
            Verifier();
            return ProtectionActive && _deverrouille ? (_derniereActivite + Duree) - _horloge.MaintenantUtc : null;
        }
    }

    public bool EstAutorise(ActionProtegee action) =>
        (action == ActionProtegee.CloturerAvecManquants && !_reglages.CodePourCloturerAvecManquants) || ModeGestion;

    /// <summary>Réussi si l'action est permise maintenant ; sinon un échec à afficher.</summary>
    public ResultatOperation Exiger(ActionProtegee action) =>
        EstAutorise(action) ? ResultatOperation.Ok("") : ResultatOperation.Echec(MessageVerrouille);

    /// <summary>Activité de l'utilisateur (clavier, souris, scan) : repousse le verrouillage automatique.</summary>
    public void NoterActivite()
    {
        if (_deverrouille && !Verifier())
        {
            _derniereActivite = _horloge.MaintenantUtc;
        }
    }

    /// <summary>Referme le mode gestion s'il a expiré. Vrai si l'état vient de changer.</summary>
    public bool Verifier()
    {
        if (_deverrouille && ProtectionActive && _horloge.MaintenantUtc - _derniereActivite >= Duree)
        {
            _deverrouille = false;
            EtatChange?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    public ResultatOperation Deverrouiller(string? code)
    {
        if (!ProtectionActive)
        {
            return ResultatOperation.Ok("Aucun code n'est défini : tout est déjà permis.");
        }

        var verification = ControlerCode(code);
        if (!verification.Reussi)
        {
            return verification;
        }

        _deverrouille = true;
        _derniereActivite = _horloge.MaintenantUtc;
        EtatChange?.Invoke(this, EventArgs.Empty);
        return ResultatOperation.Ok("Mode gestion activé.");
    }

    public void Verrouiller()
    {
        if (_deverrouille)
        {
            _deverrouille = false;
            EtatChange?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Définit le code (premier code) ou le change : l'ancien code est alors obligatoire. 4 à 6 chiffres, saisis deux fois.
    /// Celui qui vient de le définir reste en mode gestion.
    /// </summary>
    public ResultatOperation DefinirCode(string? ancien, string? nouveau, string? confirmation)
    {
        if (ProtectionActive && ControlerCode(ancien) is { Reussi: false } refus)
        {
            return refus.Message == "Code incorrect." ? ResultatOperation.Echec("L'ancien code est incorrect.") : refus;
        }

        var code = (nouveau ?? "").Trim();
        if (code.Length is < LongueurMin or > LongueurMax || !code.All(char.IsAsciiDigit))
        {
            return ResultatOperation.Echec($"Le code doit faire {LongueurMin} à {LongueurMax} chiffres.");
        }

        if (code != (confirmation ?? "").Trim())
        {
            return ResultatOperation.Echec("Les deux saisies du nouveau code ne sont pas identiques.");
        }

        var premier = !ProtectionActive;
        _reglages.CodeGestionnaire = Hacher(code);
        _stockage.Enregistrer(_reglages);
        _deverrouille = true;
        _derniereActivite = _horloge.MaintenantUtc;
        EtatChange?.Invoke(this, EventArgs.Empty);
        return ResultatOperation.Ok(premier ? "Code gestionnaire défini." : "Code gestionnaire changé.");
    }

    /// <summary>Retire le code (plus rien n'est verrouillé) : l'ancien code est obligatoire.</summary>
    public ResultatOperation RetirerCode(string? ancien)
    {
        if (!ProtectionActive)
        {
            return ResultatOperation.Ok("Aucun code n'est défini.");
        }

        if (ControlerCode(ancien) is { Reussi: false } refus)
        {
            return refus;
        }

        _reglages.CodeGestionnaire = null;
        _reglages.DeverrouilleAuDemarrage = false;
        _stockage.Enregistrer(_reglages);
        _deverrouille = false;
        EtatChange?.Invoke(this, EventArgs.Empty);
        return ResultatOperation.Ok("Code retiré : plus rien n'est verrouillé.");
    }

    /// <summary>À la fermeture : retient le mode gestion ouvert seulement si le club ne veut pas reverrouiller.</summary>
    public void PreparerFermeture()
    {
        var garder = ProtectionActive && !_reglages.VerrouillerALaFermeture && ModeGestion;
        if (_reglages.DeverrouilleAuDemarrage != garder)
        {
            _reglages.DeverrouilleAuDemarrage = garder;
            _stockage.Enregistrer(_reglages);
        }
    }

    /// <summary>Nom affiché d'une action protégée (liste de la carte Sécurité).</summary>
    public static string Libelle(ActionProtegee action) => action switch
    {
        ActionProtegee.Materiel => "Ajouter, modifier, supprimer du matériel",
        ActionProtegee.Moniteurs => "Moniteurs",
        ActionProtegee.SupprimerSortie => "Supprimer une sortie",
        ActionProtegee.CloturerAvecManquants => "Clôturer avec des manquants",
        ActionProtegee.Revisions => "Enregistrer une révision",
        ActionProtegee.Parametres => "Paramètres",
        ActionProtegee.CompteurSortie => "Utilisations d'une sortie",
        ActionProtegee.RendreManquant => "Rendre un manquant d'une sortie clôturée",
        _ => "Restaurer une sauvegarde",
    };

    // ---------------------------------------------------------------- Code

    private ResultatOperation ControlerCode(string? code)
    {
        var maintenant = _horloge.MaintenantUtc;
        if (_bloqueJusqua is { } fin && maintenant < fin)
        {
            var secondes = (int)Math.Ceiling((fin - maintenant).TotalSeconds);
            return ResultatOperation.Echec($"Trop d'essais. Réessayez dans {secondes} s.");
        }

        if (VerifierHache((code ?? "").Trim(), _reglages.CodeGestionnaire ?? ""))
        {
            _echecs = 0;
            _bloqueJusqua = null;
            return ResultatOperation.Ok("");
        }

        if (++_echecs >= EssaisAvantBlocage)
        {
            _echecs = 0;
            _bloqueJusqua = maintenant + DureeBlocage;
            return ResultatOperation.Echec("Code incorrect. Trop d'essais : attendez une minute.");
        }

        return ResultatOperation.Echec("Code incorrect.");
    }

    private static string Hacher(string code)
    {
        var sel = RandomNumberGenerator.GetBytes(16);
        var hache = Rfc2898DeriveBytes.Pbkdf2(code, sel, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Format}:{Iterations}:{Convert.ToBase64String(sel)}:{Convert.ToBase64String(hache)}";
    }

    private static bool VerifierHache(string code, string stocke)
    {
        var parties = stocke.Split(':');
        if (parties.Length != 4 || parties[0] != Format || !int.TryParse(parties[1], out var iterations) || code.Length == 0)
        {
            return false;
        }

        try
        {
            var sel = Convert.FromBase64String(parties[2]);
            var attendu = Convert.FromBase64String(parties[3]);
            var calcule = Rfc2898DeriveBytes.Pbkdf2(code, sel, iterations, HashAlgorithmName.SHA256, attendu.Length);
            return CryptographicOperations.FixedTimeEquals(calcule, attendu);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
