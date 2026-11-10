using System.Windows;

namespace VolejbalVrsovice.App.Views
{
    // Dialogy nemají žádnou vlastní logiku – vše je ve ViewModelech; zavírání řeší DialogService.

    public partial class ChangePasswordDialog : Window
    {
        public ChangePasswordDialog()
        {
            InitializeComponent();
        }
    }

    public partial class RecoveryCodeDialog : Window
    {
        public RecoveryCodeDialog()
        {
            InitializeComponent();
        }
    }

    public partial class ForgotPasswordDialog : Window
    {
        public ForgotPasswordDialog()
        {
            InitializeComponent();
        }
    }

    public partial class AddUserDialog : Window
    {
        public AddUserDialog()
        {
            InitializeComponent();
        }
    }

    public partial class ResetPasswordDialog : Window
    {
        public ResetPasswordDialog()
        {
            InitializeComponent();
        }
    }
}
