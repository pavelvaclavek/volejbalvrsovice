using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VolejbalVrsovice.App.Services;
using VolejbalVrsovice.App.ViewModels;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Services;

namespace VolejbalVrsovice.Tests.TestSupport
{
    internal sealed class FakeSession : ISession
    {
        public AppUser User { get; set; }

        public bool IsAuthenticated
        {
            get { return User != null; }
        }

        public int? UserId
        {
            get { return User == null ? (int?)null : User.Id; }
        }
    }

    internal sealed class FakeAuthService : IAuthService
    {
        public Exception InitializeError { get; set; }

        public Exception LoginError { get; set; }

        public Exception RecoverError { get; set; }

        public LoginResult LoginResult { get; set; } = LoginResult.InvalidCredentials();

        public PasswordChangeResult ForcedChangeResult { get; set; } = PasswordChangeResult.Changed();

        public PasswordChangeResult OwnChangeResult { get; set; } = PasswordChangeResult.Changed();

        public OperationResult RecoverResult { get; set; } = OperationResult.Success();

        /// <summary>Kód, který „chybí“ a má se vydat po přihlášení; <c>null</c> = nic nechybí.</summary>
        public string MissingRecoveryCode { get; set; }

        /// <summary>Pokud je nastaveno, příprava úložiště „běží“, dokud se tato úloha nedokončí.</summary>
        public Task InitializeGate { get; set; }

        /// <summary>Pokud je nastaveno, operace s hesly „běží“ (několik sekund jako ve skutečnosti), dokud se úloha nedokončí.</summary>
        public Task OperationGate { get; set; }

        public List<string> Calls { get; } = new List<string>();

        public Task InitializeAsync()
        {
            Calls.Add("Initialize");
            if (InitializeGate != null)
            {
                return InitializeGate;
            }

            return InitializeError == null ? Task.CompletedTask : Task.FromException(InitializeError);
        }

        public Task<LoginResult> LoginAsync(string email, string password)
        {
            Calls.Add("Login:" + email + ":" + password);
            return LoginError == null ? Task.FromResult(LoginResult) : Task.FromException<LoginResult>(LoginError);
        }

        public void Logout()
        {
            Calls.Add("Logout");
        }

        public Task<PasswordChangeResult> CompleteForcedPasswordChangeAsync(string newPassword)
        {
            Calls.Add("ForcedChange:" + newPassword);
            return Gated(ForcedChangeResult);
        }

        public Task<PasswordChangeResult> ChangeOwnPasswordAsync(string currentPassword, string newPassword)
        {
            Calls.Add("OwnChange:" + currentPassword + ":" + newPassword);
            return Gated(OwnChangeResult);
        }

        public Task<string> IssueMissingRecoveryCodeAsync()
        {
            Calls.Add("IssueMissingRecoveryCode");
            return Task.FromResult(MissingRecoveryCode);
        }

        public async Task<OperationResult> RecoverAsync(string recoveryCode, string adminEmail, string newPassword)
        {
            Calls.Add("Recover:" + recoveryCode + ":" + adminEmail + ":" + newPassword);
            var result = await Gated(RecoverResult);
            if (RecoverError != null)
            {
                throw RecoverError;
            }

            return result;
        }

        private async Task<T> Gated<T>(T result)
        {
            if (OperationGate != null)
            {
                await OperationGate;
            }

            return result;
        }
    }

    internal sealed class FakeUserService : IUserService
    {
        public List<AppUser> Users { get; } = new List<AppUser>();

        public CreateUserResult CreateResult { get; set; }

        public OperationResult ResetResult { get; set; } = OperationResult.Success();

        public OperationResult DeactivateResult { get; set; } = OperationResult.Success();

        public string RecoveryCode { get; set; } = "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ";

        /// <summary>Pokud je nastaveno, operace s klíčem „běží“, dokud se úloha nedokončí.</summary>
        public Task OperationGate { get; set; }

        public List<string> Calls { get; } = new List<string>();

        public Task<IReadOnlyList<AppUser>> GetUsersAsync()
        {
            return Task.FromResult<IReadOnlyList<AppUser>>(Users.ToList());
        }

        public async Task<CreateUserResult> CreateUserAsync(string email, string displayName, UserRole role, string temporaryPassword)
        {
            Calls.Add("Create:" + email + ":" + displayName + ":" + role + ":" + temporaryPassword);
            await Gate();

            if (CreateResult != null)
            {
                return CreateResult;
            }

            var user = new AppUser { Id = Users.Count + 1, Email = email, DisplayName = displayName, Role = role, IsActive = true, MustChangePassword = true };
            Users.Add(user);
            return CreateUserResult.Created(user);
        }

        public async Task<OperationResult> ResetPasswordAsync(int userId, string temporaryPassword)
        {
            Calls.Add("Reset:" + userId + ":" + temporaryPassword);
            await Gate();
            return ResetResult;
        }

        public async Task<OperationResult> DeactivateAsync(int userId)
        {
            Calls.Add("Deactivate:" + userId);
            await Gate();

            if (DeactivateResult.Succeeded)
            {
                Users.Single(user => user.Id == userId).IsActive = false;
            }

            return DeactivateResult;
        }

        public async Task<string> GenerateRecoveryCodeAsync()
        {
            Calls.Add("GenerateRecoveryCode");
            await Gate();
            return RecoveryCode;
        }

        private Task Gate()
        {
            return OperationGate ?? Task.CompletedTask;
        }
    }

    /// <summary>Zaznamenává hlášky a dialogy; odpovědi lze předem nastavit.</summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public List<string> Infos { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        public List<string> Questions { get; } = new List<string>();

        public List<DialogViewModel> Dialogs { get; } = new List<DialogViewModel>();

        public bool ConfirmAnswer { get; set; } = true;

        /// <summary>Co má „uživatel“ v dialogu udělat; návratová hodnota = potvrzeno / zrušeno.</summary>
        public Func<DialogViewModel, bool> OnDialog { get; set; } = dialog => true;

        public void ShowInfo(string message)
        {
            Infos.Add(message);
        }

        public void ShowWarning(string message)
        {
            Warnings.Add(message);
        }

        public void ShowError(string message)
        {
            Errors.Add(message);
        }

        public bool Confirm(string message)
        {
            Questions.Add(message);
            return ConfirmAnswer;
        }

        public bool ShowDialog(DialogViewModel viewModel)
        {
            Dialogs.Add(viewModel);
            return OnDialog(viewModel);
        }
    }

    internal sealed class FakeClipboard : IClipboardService
    {
        public string Text { get; private set; }

        public bool IsAvailable { get; set; } = true;

        public bool TrySetText(string text)
        {
            if (!IsAvailable)
            {
                return false;
            }

            Text = text;
            return true;
        }
    }

    internal sealed class FakePrinter : IRecoveryCodePrinter
    {
        public List<string> Printed { get; } = new List<string>();

        public bool Accept { get; set; } = true;

        public bool Print(string recoveryCode)
        {
            if (Accept)
            {
                Printed.Add(recoveryCode);
            }

            return Accept;
        }
    }

    internal static class TestUsers
    {
        public static AppUser Admin(int id = 1)
        {
            return new AppUser { Id = id, Email = "admin@admin.cz", DisplayName = "Správce", Role = UserRole.Admin, IsActive = true };
        }

        public static AppUser Ordinary(int id = 2)
        {
            return new AppUser { Id = id, Email = "trener@example.cz", DisplayName = "Petr Svoboda", Role = UserRole.User, IsActive = true };
        }
    }
}
