using System;
using CommunityToolkit.Mvvm.Input;
using VolejbalVrsovice.App.Services;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>
    /// Jednorázové zobrazení záchranného kódu. Kód se nikde jinde neukládá; dialog nejde zavřít,
    /// dokud uživatel nezaškrtne, že si ho bezpečně uložil.
    /// </summary>
    public sealed class RecoveryCodeViewModel : DialogViewModel
    {
        private readonly IClipboardService _clipboard;
        private readonly IRecoveryCodePrinter _printer;

        private bool _isConfirmed;
        private string _statusMessage;

        public RecoveryCodeViewModel(string code, IClipboardService clipboard, IRecoveryCodePrinter printer)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("Záchranný kód musí být zadán.", nameof(code));

            Code = code;
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _printer = printer ?? throw new ArgumentNullException(nameof(printer));

            CopyCommand = new RelayCommand(Copy);
            PrintCommand = new RelayCommand(Print);
            CloseCommand = new RelayCommand(() => Close(true), () => IsConfirmed);
        }

        public IRelayCommand CopyCommand { get; }

        public IRelayCommand PrintCommand { get; }

        public IRelayCommand CloseCommand { get; }

        public override string Title
        {
            get { return "Záchranný kód"; }
        }

        /// <summary>Kód ve tvaru <c>XXXX-XXXX-XXXX-XXXX-XXXX-XXXX</c>.</summary>
        public string Code { get; }

        public string Explanation
        {
            get { return RecoveryCodeTexts.Explanation + " Kód se zobrazuje jen teď – později ho už nelze zjistit, jen nahradit novým."; }
        }

        public string DataLossWarning
        {
            get { return RecoveryCodeTexts.DataLossWarning; }
        }

        /// <summary>Povinné zaškrtnutí „Kód jsem si bezpečně uložil“.</summary>
        public bool IsConfirmed
        {
            get { return _isConfirmed; }
            set
            {
                if (SetProperty(ref _isConfirmed, value))
                {
                    CloseCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { SetProperty(ref _statusMessage, value); }
        }

        public override bool CanClose
        {
            get { return IsConfirmed; }
        }

        private void Copy()
        {
            StatusMessage = _clipboard.TrySetText(Code)
                ? "Kód je zkopírován do schránky."
                : "Kód se nepodařilo zkopírovat – opište ho ručně nebo vytiskněte.";
        }

        private void Print()
        {
            StatusMessage = _printer.Print(Code) ? "Kód byl odeslán na tiskárnu." : null;
        }
    }
}
