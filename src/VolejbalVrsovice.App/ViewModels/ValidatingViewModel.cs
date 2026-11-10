using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VolejbalVrsovice.App.ViewModels
{
    /// <summary>
    /// Základ ViewModelů s validací polí přes <see cref="INotifyDataErrorInfo"/> – WPF pak chybu
    /// zobrazí přímo u pole. Každé pole má nejvýše jednu chybovou hlášku.
    /// </summary>
    public abstract class ValidatingViewModel : ObservableObject, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public bool HasErrors
        {
            get { return _errors.Count > 0; }
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return _errors.Values.ToList();
            }

            return _errors.TryGetValue(propertyName, out var error) ? new[] { error } : new string[0];
        }

        /// <summary>Chybová hláška pole, nebo <c>null</c>.</summary>
        public string GetError(string propertyName)
        {
            return _errors.TryGetValue(propertyName, out var error) ? error : null;
        }

        /// <summary>Nastaví (nebo při <c>null</c> zruší) chybu pole.</summary>
        protected void SetError(string propertyName, string error)
        {
            var hadError = _errors.TryGetValue(propertyName, out var current);

            if (error == null)
            {
                if (!hadError)
                {
                    return;
                }

                _errors.Remove(propertyName);
            }
            else
            {
                if (hadError && current == error)
                {
                    return;
                }

                _errors[propertyName] = error;
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }

        protected void ClearErrors()
        {
            foreach (var propertyName in _errors.Keys.ToList())
            {
                SetError(propertyName, null);
            }
        }
    }
}
