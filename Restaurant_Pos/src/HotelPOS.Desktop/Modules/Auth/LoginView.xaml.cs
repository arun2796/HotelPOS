using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotelPOS.Desktop.Modules.Auth;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    // Put the cursor where the user types next: username, or password when the username is remembered.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(UsernameBox.Text))
        {
            Keyboard.Focus(UsernameBox);
        }
        else
        {
            Keyboard.Focus(PasswordInput);
        }
    }
}
