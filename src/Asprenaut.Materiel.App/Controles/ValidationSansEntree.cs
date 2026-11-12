using Saisie = Asprenaut.Materiel.Core.Saisie;

namespace Asprenaut.Materiel.App.Controles;

/// <summary>
/// Valide un champ de scan sans Entrée : pistolet réglé sans suffixe, ou téléphone en clavier Bluetooth.
/// Une frappe très rapide (voir Core.Saisie.DetecteurRafale) suivie de 150 ms de pause est un scan complet :
/// <c>valider</c> est appelé. La saisie à la main, plus lente, attend toujours Entrée.
/// Appeler <see cref="Annuler"/> quand le champ est validé par Entrée.
/// </summary>
public sealed class ValidationSansEntree : IDisposable
{
    private readonly TextBoxBase _champ;
    private readonly Action _valider;
    private readonly Saisie.DetecteurRafale _rafale = new();
    private readonly System.Windows.Forms.Timer _pause = new() { Interval = Saisie.DetecteurRafale.PauseValidationMs };

    public ValidationSansEntree(TextBoxBase champ, Action valider)
    {
        _champ = champ;
        _valider = valider;
        champ.KeyPress += SurCaractere;
        champ.Disposed += (_, _) => Dispose();
        _pause.Tick += (_, _) =>
        {
            var rafale = _rafale.EstRafale && _champ.TextLength > 0;
            Annuler();
            if (rafale)
            {
                _valider();
            }
        };
    }

    public void Annuler()
    {
        _pause.Stop();
        _rafale.Reinitialiser();
    }

    public void Dispose() => _pause.Dispose();

    private void SurCaractere(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
        {
            return;
        }

        _rafale.Caractere(Environment.TickCount64);
        _pause.Stop();
        _pause.Start();
    }
}
