using System.Diagnostics;
using System.Windows;

namespace JacquardStudio;
public partial class AboutWindow : Window
{
    public AboutWindow(bool english)
    {
        InitializeComponent();
        VersionText.Text = AppVersion.Display;
        if (english)
        {
            FlowDirection = FlowDirection.LeftToRight; Title = "About";
            DescriptionText.Text = "Experimental Windows tool for jacquard design, weave editing, and sample-profile loom files.";
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
