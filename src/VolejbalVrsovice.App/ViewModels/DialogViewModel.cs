using System;

namespace VolejbalVrsovice.App.ViewModels
{
    public sealed class DialogCloseEventArgs : EventArgs
    {
        public DialogCloseEventArgs(bool result)
        {
            Result = result;
        }

        /// <summary><c>true</c> = potvrzeno, <c>false</c> = zrušeno.</summary>
        public bool Result { get; }
    }

    /// <summary>Základ ViewModelů modálních dialogů (Enter = potvrdit, Esc = zrušit).</summary>
    public abstract class DialogViewModel : ValidatingViewModel
    {
        /// <summary>Dialog žádá o zavření s daným výsledkem.</summary>
        public event EventHandler<DialogCloseEventArgs> CloseRequested;

        public abstract string Title { get; }

        /// <summary>
        /// Smí uživatel dialog zavřít křížkem nebo klávesou Esc? Dialog se záchranným kódem to
        /// nedovolí, dokud uživatel nepotvrdí, že si kód uložil.
        /// </summary>
        public virtual bool CanClose
        {
            get { return true; }
        }

        protected void Close(bool result)
        {
            CloseRequested?.Invoke(this, new DialogCloseEventArgs(result));
        }
    }
}
