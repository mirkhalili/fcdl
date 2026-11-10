using System.Windows;
using System.Windows.Controls;

namespace JacquardStudio;
public sealed class PasswordPrompt : Window
{
    public string Password { get; private set; }="";
    public PasswordPrompt(string title,string description,bool confirm=false)
    {
        Title=title; Width=460; Height=confirm ? 300 : 245; MinWidth=360; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner; FlowDirection=FlowDirection.RightToLeft; SetResourceReference(BackgroundProperty,"Surface");
        var panel=new StackPanel {Margin=new Thickness(22)}; panel.Children.Add(new TextBlock {Text=description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var password=new PasswordBox {Padding=new Thickness(8),MaxLength=1024}; panel.Children.Add(password);
        var again=new PasswordBox {Padding=new Thickness(8),MaxLength=1024,Margin=new Thickness(0,8,0,0)}; if(confirm)panel.Children.Add(again);
        var warning=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,0)}; panel.Children.Add(warning);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
        var ok=new Button {Content="تأیید",Margin=new Thickness(4),IsDefault=true}; var cancel=new Button {Content="انصراف",Margin=new Thickness(4),IsCancel=true}; buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        ok.Click+=(_,_)=>{if(string.IsNullOrEmpty(password.Password) || confirm && password.Password!=again.Password) {warning.Text="رمز را وارد کنید و از یکسان بودن تکرار آن مطمئن شوید.";return;} Password=password.Password; DialogResult=true;}; Content=panel; Loaded+=(_,_)=>password.Focus();
    }
}
