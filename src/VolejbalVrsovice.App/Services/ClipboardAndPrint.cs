using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VolejbalVrsovice.App.Services
{
    public sealed class ClipboardService : IClipboardService
    {
        public bool TrySetText(string text)
        {
            try
            {
                Clipboard.SetDataObject(text ?? string.Empty, true);
                return true;
            }
            catch (COMException)
            {
                // Schránku právě drží jiný program.
                return false;
            }
        }
    }

    /// <summary>
    /// Tisk záchranného kódu (zadání, sekce 5a – tlačítko „Tisk“ v dialogu s kódem). Jiný tisk
    /// aplikace neumí a umět nemá.
    /// </summary>
    public sealed class RecoveryCodePrinter : IRecoveryCodePrinter
    {
        public bool Print(string recoveryCode)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            var document = new FlowDocument
            {
                PageWidth = dialog.PrintableAreaWidth,
                PageHeight = dialog.PrintableAreaHeight,
                PagePadding = new Thickness(72),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
            };

            document.Blocks.Add(new Paragraph(new Run(AppInfo.ApplicationName + " – záchranný kód"))
            {
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
            });

            document.Blocks.Add(new Paragraph(new Run(recoveryCode))
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 26,
                Margin = new Thickness(0, 24, 0, 24),
            });

            document.Blocks.Add(new Paragraph(new Run(
                "Vytištěno " + DateTime.Now.ToString(UiCulture.DateTimePattern, UiCulture.Create()) + ".")));

            document.Blocks.Add(new Paragraph(new Run(RecoveryCodeTexts.Explanation)));
            document.Blocks.Add(new Paragraph(new Run(RecoveryCodeTexts.DataLossWarning)) { FontWeight = FontWeights.SemiBold });

            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, AppInfo.ApplicationName + " – záchranný kód");
            return true;
        }
    }

    /// <summary>Texty k záchrannému kódu – stejné v dialogu i na výtisku.</summary>
    public static class RecoveryCodeTexts
    {
        public const string Explanation =
            "Záchranným kódem lze na přihlašovací obrazovce (odkaz „Zapomenuté heslo“) nastavit správci nové heslo. "
            + "Uložte ho na bezpečné místo mimo tento počítač – kdo kód má, dostane se k datům.";

        public const string DataLossWarning =
            "Bez hesla i záchranného kódu jsou data nenávratně ztracena.";
    }
}
