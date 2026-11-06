using System.Diagnostics;
using System.Windows;

namespace JacquardStudio;
public partial class AboutWindow : Window
{
    public AboutWindow(bool english)
    {
        InitializeComponent();
        if (english)
        {
            FlowDirection = FlowDirection.LeftToRight; Title = "About";
            DescriptionText.Text = "Experimental Windows tool for preparing jacquard design images. Loom file conversion is not yet available.";
            AuthorText.Text = "Developer: Mirkhalili";
        }
    }
    private void Website_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://mirkhalili.com") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, "https://mirkhalili.com"); }
    }
}
