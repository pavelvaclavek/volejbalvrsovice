using VolejbalVrsovice.App.ViewModels;

namespace VolejbalVrsovice.App.Services
{
    /// <summary>Hlášky a modální dialogy – ViewModely díky tomu neznají okna a jdou testovat.</summary>
    public interface IDialogService
    {
        void ShowInfo(string message);

        void ShowWarning(string message);

        void ShowError(string message);

        /// <summary>Dotaz Ano/Ne (potvrzení před smazáním apod.). Výchozí odpověď je Ne.</summary>
        bool Confirm(string message);

        /// <summary>Zobrazí modální dialog; vrací <c>true</c>, pokud ho uživatel potvrdil.</summary>
        bool ShowDialog(DialogViewModel viewModel);
    }

    public interface IClipboardService
    {
        /// <summary>Zkopíruje text do schránky; vrací <c>false</c>, pokud je schránka nedostupná.</summary>
        bool TrySetText(string text);
    }

    public interface IRecoveryCodePrinter
    {
        /// <summary>Nabídne výběr tiskárny a vytiskne záchranný kód. Vrací <c>false</c>, pokud uživatel tisk zrušil.</summary>
        bool Print(string recoveryCode);
    }
}
